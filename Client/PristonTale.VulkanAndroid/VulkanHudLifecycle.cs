using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

internal sealed class VulkanHudLifecycle : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    public void OnActivityCreated(Activity activity, Bundle state)
    {
        if (activity.Class.Name != "org.libsdl.app.SDLActivity")
        {
            return;
        }

        var decor = activity.Window?.DecorView;
        void WaitForWorld()
        {
            if (activity.IsFinishing || activity.IsDestroyed)
                return;
            if (ProbeApplication.CurrentWorld is { } session)
                Attach(activity, session);
            else
                decor?.PostDelayed(WaitForWorld, 200);
        }
        decor?.Post(WaitForWorld);
    }

    private static void Attach(Activity activity, VulkanWorldSession session)
    {
        if (activity.IsFinishing)
        {
            return;
        }

        var hud = new MobileWorldHud(activity, session.Character.Name);
        var overlay = new FrameLayout(activity);
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(int value) => (int)MathF.Round(value * density);
        FrameLayout.LayoutParams Place(int width, int height, GravityFlags gravity, int edge = 10) =>
            new(width < 0 ? width : Dp(width), height < 0 ? height : Dp(height), gravity)
            {
                LeftMargin = Dp(edge), RightMargin = Dp(edge), TopMargin = Dp(edge), BottomMargin = Dp(edge)
            };
        overlay.AddView(hud.Status, Place(158, -2, GravityFlags.Top | GravityFlags.Left));
        overlay.AddView(hud.Experience, Place(-1, 15, GravityFlags.Bottom, 0));

        var map = new ImageView(activity) { ContentDescription = "Mapa de Ricarten" };
        using (var source = activity.Assets!.Open("hud-minimap.png"))
        {
            map.SetImageBitmap(BitmapFactory.DecodeStream(source));
        }
        MobileHudTheme.Card(map);
        overlay.AddView(map, Place(98, 98, GravityFlags.Top | GravityFlags.Right));

        var actions = new LinearLayout(activity);
        var run = MobileHudTheme.Button(activity, "Corrida");
        MobileHudTheme.Icon(run, HudGlyph.Run);
        run.Click += (_, _) =>
        {
            session.ToggleRun();
            run.Text = run.Text == "Corrida" ? "Caminhada" : "Corrida";
        };
        actions.AddView(run);
        var chat = MobileHudTheme.Button(activity, "Chat");
        MobileHudTheme.Icon(chat, HudGlyph.Chat);
        chat.Click += (_, _) => OpenChat(activity, session);
        actions.AddView(chat);
        var exit = MobileHudTheme.Button(activity, "Sair");
        MobileHudTheme.Icon(exit, HudGlyph.Exit);
        exit.Click += (_, _) => activity.Finish();
        actions.AddView(exit);
        overlay.AddView(actions, Place(-2, -2, GravityFlags.Bottom | GravityFlags.Right, 19));

        var messages = new TextView(activity) { TextSize = 11 };
        messages.SetMaxLines(4);
        messages.SetTextColor(MobileHudTheme.Ink);
        messages.SetShadowLayer(2f, 0f, 1f, Color.Black);
        var messageLayout = Place(225, -2, GravityFlags.Top | GravityFlags.Left);
        messageLayout.TopMargin = Dp(79);
        overlay.AddView(messages, messageLayout);
        activity.AddContentView(overlay, new ViewGroup.LayoutParams(-1, -1));

        var lines = new Queue<string>(4);
        PristonTale.Simulation.Characters.OwnerState previousOwner = null;
        void Refresh()
        {
            if (activity.IsFinishing || activity.IsDestroyed || !overlay.IsAttachedToWindow)
            {
                return;
            }

            var owner = session.Owner;
            if (owner is not null && !ReferenceEquals(owner, previousOwner))
            {
                hud.UpdateOwner(owner);
                previousOwner = owner;
            }

            var changed = false;
            while (session.TryReadChat(out var line))
            {
                if (lines.Count == 4)
                {
                    lines.Dequeue();
                }
                lines.Enqueue(line);
                changed = true;
            }
            if (changed)
            {
                messages.Text = string.Join("\n", lines);
            }
            overlay.PostDelayed(Refresh, 250);
        }
        overlay.Post(Refresh);
    }

    private static void OpenChat(Activity activity, VulkanWorldSession session)
    {
        var input = new EditText(activity) { Hint = "Mensagem" };
        input.SetSingleLine(true);
        new AlertDialog.Builder(activity)
            .SetTitle("Chat")!
            .SetView(input)!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Enviar", async (_, _) =>
            {
                var text = input.Text?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }
                try
                {
                    await session.SendChatAsync(text);
                }
                catch (Exception)
                {
                    Toast.MakeText(activity, "Não foi possível enviar a mensagem.", ToastLength.Short)?.Show();
                }
            })!
            .Show();
    }

    public void OnActivityDestroyed(Activity activity) { }
    public void OnActivityPaused(Activity activity) { }
    public void OnActivityResumed(Activity activity) { }
    public void OnActivitySaveInstanceState(Activity activity, Bundle state) { }
    public void OnActivityStarted(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
}
