using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Opengl;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Java.Lang;
using PristonTale.Client.Net;
using PristonTale.Contracts.Characters;

namespace PristonTale.Mobile.CharacterSelect;

// The Android views around CharSelectScreen: the GL surface it draws on, the touches it reads as a pointer and the
// hidden text field whose keyboard types the new character's name. Everything here runs on the UI thread.
internal sealed class CharacterSelection : ICharSelectHost, IDisposable
{
    private const string SceneAsset = "CharSelect/scene.bin";

    private readonly Activity _activity;
    private readonly CharSelectScreen _screen;
    private readonly FrameLayout _root;
    private readonly SurfaceView _surface;
    private readonly EditText _name;
    private readonly SoftInput _previousSoftInput;

    private CharacterSelection(Activity activity, CharSelectSceneData data, GameServerConnection connection)
    {
        _activity = activity;
        _screen = new CharSelectScreen(activity, data, connection, this);
        _surface = new SurfaceView(activity, _screen);
        _name = new EditText(activity)
        {
            InputType = InputTypes.ClassText | InputTypes.TextVariationVisiblePassword | InputTypes.TextFlagNoSuggestions,
            ImeOptions = (ImeAction)((int)ImeAction.Done | (int)ImeFlags.NoExtractUi | (int)ImeFlags.NoFullscreen),
            Alpha = 0f
        };
        _name.SetSingleLine();
        _name.SetFilters([new InputFilterLengthFilter(CharacterNames.MaxLength), new NameCharacters()]);
        _name.TextChanged += (_, _) => _screen.SetTypedName(_name.Text ?? "");
        _name.EditorAction += (_, args) =>
        {
            args.Handled = args.ActionId == ImeAction.Done;
            if (args.Handled)
            {
                Keyboard()?.HideSoftInputFromWindow(_name.WindowToken, HideSoftInputFlags.None);
            }
        };
        _root = new FrameLayout(activity);
        _root.AddView(_name, new FrameLayout.LayoutParams(1, 1));
        _root.AddView(_surface, new FrameLayout.LayoutParams(-1, -1));
        _previousSoftInput = activity.Window?.Attributes?.SoftInputMode ?? SoftInput.StateUnspecified;
        // The keyboard covers the screen instead of resizing it: the scene keeps its size while the name is typed.
        activity.Window?.SetSoftInputMode(SoftInput.AdjustNothing);
    }

    public View View => _root;

    // Reads the packaged scene off the UI thread, then builds the views on it.
    public static async Task<CharacterSelection> OpenAsync(Activity activity, GameServerConnection connection, CancellationToken ct)
    {
        var data = await Task.Run(() =>
        {
            using var asset = activity.Assets?.Open(SceneAsset) ?? throw new InvalidOperationException("The character select scene is not in the APK.");
            using var buffered = new BufferedStream(asset, 1 << 16);
            return CharSelectSceneData.Read(buffered);
        }, ct);
        return new CharacterSelection(activity, data, connection);
    }

    // The character that entered the world, or null when the player left the selection.
    public async Task<CharSelectEntered?> RunAsync(CancellationToken ct)
    {
        using var cancel = ct.Register(_screen.Close);
        var entered = await _screen.Completion;
        ct.ThrowIfCancellationRequested();
        return entered;
    }

    public async Task<Bitmap?> CaptureLastFrameAsync()
    {
        if (_surface.Width == 0 || _surface.Height == 0 || _surface.Holder?.Surface?.IsValid != true)
        {
            return null;
        }

        var bitmap = Bitmap.CreateBitmap(_surface.Width, _surface.Height, Bitmap.Config.Argb8888!)!;
        using var handler = new Handler(Looper.MainLooper!);
        using var listener = new FrameCopyListener();
        try
        {
            PixelCopy.Request(_surface, bitmap, listener, handler);
            if (await listener.Result.Task == (int)PixelCopyResult.Success)
            {
                return bitmap;
            }
        }
        catch (Java.Lang.IllegalArgumentException)
        {
        }

        bitmap.Dispose();
        return null;
    }

    private sealed class FrameCopyListener : Java.Lang.Object, PixelCopy.IOnPixelCopyFinishedListener
    {
        public TaskCompletionSource<int> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnPixelCopyFinished(int copyResult) => Result.TrySetResult(copyResult);
    }

    public void OnPause()
    {
        _surface.OnPause();
        _screen.Pause();
    }

    public void OnResume()
    {
        _surface.OnResume();
        _screen.Resume();
    }

    void ICharSelectHost.SetNameInput(bool open, bool clear) => _activity.RunOnUiThread(() =>
    {
        if (clear)
        {
            _name.Text = "";
        }

        if (open)
        {
            _name.RequestFocus();
            Keyboard()?.ShowSoftInput(_name, ShowFlags.Implicit);
        }
        else
        {
            Keyboard()?.HideSoftInputFromWindow(_name.WindowToken, HideSoftInputFlags.None);
        }
    });

    private InputMethodManager? Keyboard() => _activity.GetSystemService(Context.InputMethodService) as InputMethodManager;

    public void Dispose()
    {
        _surface.OnPause();
        _screen.Close();
        Keyboard()?.HideSoftInputFromWindow(_name.WindowToken, HideSoftInputFlags.None);
        _activity.Window?.SetSoftInputMode(_previousSoftInput);
    }

    // The desktop name field's characters: ASCII letters and digits, '.', '-' and '_'.
    private sealed class NameCharacters : Java.Lang.Object, IInputFilter
    {
        public ICharSequence? FilterFormatted(ICharSequence? source, int start, int end, ISpanned? dest, int dstart, int dend)
        {
            if (source is null)
            {
                return null;
            }

            var kept = new System.Text.StringBuilder(end - start);
            for (var index = start; index < end; index++)
            {
                var character = source.CharAt(index);
                if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
                {
                    kept.Append(character);
                }
            }

            return kept.Length == end - start ? null : new Java.Lang.String(kept.ToString());
        }
    }

    // The surface the screen renders to; a touch moves the screen's pointer and a lift without a drag presses.
    private sealed class SurfaceView : GLSurfaceView
    {
        private readonly CharSelectScreen _screen;
        private readonly int _touchSlop;
        private float _downX;
        private float _downY;
        private bool _dragged;

        public SurfaceView(Context context, CharSelectScreen screen) : base(context)
        {
            _screen = screen;
            _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16;
            SetEGLContextClientVersion(2);
            SetEGLConfigChooser(8, 8, 8, 8, 16, 0);
            PreserveEGLContextOnPause = true;
            SetRenderer(screen);
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e is null)
            {
                return false;
            }

            _screen.SetPointer(e.GetX(), e.GetY(), Height);
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    (_downX, _downY, _dragged) = (e.GetX(), e.GetY(), false);
                    break;
                case MotionEventActions.Move:
                    _dragged |= MathF.Abs(e.GetX() - _downX) > _touchSlop || MathF.Abs(e.GetY() - _downY) > _touchSlop;
                    break;
                case MotionEventActions.Up:
                    if (!_dragged)
                    {
                        _screen.Press();
                    }

                    break;
            }

            return true;
        }
    }
}
