using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

// First-time tutorials: the first time something is needed (the HUD on arrival, equipping the starter kit the guide
// gives, putting a potion on the belt, the automatic quest, the first attack, spending free attribute points), the screen
// dims around the button to press, a pulsing gold ring marks it and a card explains what to do. Only that button answers touches; the step moves on by itself once its goal
// is met (or with Entendi on a step that only explains). Each lesson is shown once (HudLayoutEditor keeps which), and
// none starts while the automatic quest is running. Refresh is called on the UI thread a few times a second.
internal sealed class VulkanTutorial
{
    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly HudLayoutEditor _records;
    private readonly List<Lesson> _lessons;
    private Lesson _lesson;
    private int _step;
    private Coach _coach;

    // A step: the view to press (null for a step that only explains), its card, and when it is done (null: Entendi).
    public sealed record Step(Func<View> Target, string Title, string Text, Func<bool> Done = null);

    // A lesson: its id, when it starts, its steps, and whether its first step must come back (the window it opened was
    // closed midway).
    public sealed record Lesson(string Id, Func<bool> Starts, Step[] Steps, Func<bool> Lost = null);

    public VulkanTutorial(Activity activity, FrameLayout overlay, HudLayoutEditor records, List<Lesson> lessons)
    {
        _activity = activity;
        _overlay = overlay;
        _records = records;
        _lessons = lessons;
    }

    // The HUD's lessons, in the order they are offered.
    public static List<Lesson> Lessons(VulkanWorldSession session, View player, View quest, View menu, View bag, VulkanActionWheel wheel,
        VulkanQuestTracker tracker, VulkanInventoryPanel inventory, VulkanStatusPanel status, Func<bool> statusSeen)
    {
        bool Idle() => session.Navigator is not { Enabled: true } && session.Owner is { Life: > 0 };
        OwnedItemView BagPotion() => session.Owner?.Items.FirstOrDefault(item => item.InBag && item.Kind == ItemKind.Potion);
        bool BeltEmpty() => session.Owner is { } owner && Enumerable.Range(0, PotionBelt.Boxes).All(box => OwnedItemView.InBox(owner.Items, box) is null);
        // A piece of the starter kit (weapon, off-hand, armour) still in the bag while its slot is empty.
        OwnedItemView NextPiece() => session.Owner is { } owner
            ? owner.Items.FirstOrDefault(item => item.InBag && ItemEquipmentMetadata.SlotOf(item.Kind) is EquipmentSlot.Weapon or EquipmentSlot.OffHand or EquipmentSlot.Armor
                && OwnedItemView.InSlot(owner.Items, (int)ItemEquipmentMetadata.SlotOf(item.Kind)!.Value) is null)
            : null;
        bool Hunting() => session.Quest is { KillsNeeded: > 0 } hunt && hunt.Kills == 0;
        // The quest card stands over the action wheel; a lesson on the attack button folds it first.
        View Attack()
        {
            if (!tracker.Collapsed)
            {
                tracker.Collapsed = true;
            }

            return wheel.Attack;
        }

        return
        [
            new("hud", Idle,
            [
                new(() => player, "Seu personagem", "Aqui ficam sua vida (vermelho), mana (azul) e estamina (verde); o selo dourado é o seu nível."),
                new(() => player, "Status do personagem", "Toque no seu retrato para ver força, espírito, talento, agilidade, vitalidade, ataque e defesa.", statusSeen),
                new(() => quest, "Sua missão", "O cartão mostra o objetivo da missão. Toque nele para minimizar e ver só o progresso."),
                new(Attack, "Atacar", "Toque para atacar o monstro selecionado ou o mais próximo. Habilidades e poções ficam em volta."),
                new(() => menu, "Menu", "No Menu você abre o inventário, ajusta os gráficos, edita a posição de cada parte da HUD, revê este tutorial e sai do jogo.")
            ]),
            new("equip", () => Idle() && NextPiece() is not null,
            [
                new(() => bag, "Equipar seu kit", "O guia te deu arma, armadura e, se sua classe usa, escudo. Toque em Bolsa para abrir o inventário.",
                    () => inventory.IsOpen),
                new(() => NextPiece() is { } piece ? inventory.TileOf(piece.InstanceId) : null, "Escolha a peça", "Toque na peça destacada na sua bolsa.",
                    () => NextPiece() is not { } piece || inventory.SelectedItem == piece.InstanceId),
                new(() => inventory.ActionStartingWith("Equipar"), "Equipe", "Toque em Equipar para vestir a peça. Repita com as outras.",
                    () => NextPiece() is null)
            ], () => NextPiece() is { } piece && (!inventory.IsOpen || inventory.SelectedItem != piece.InstanceId)),
            new("belt", () => Idle() && BeltEmpty() && BagPotion() is not null,
            [
                new(() => bag, "Poção no cinto", "Poções no cinto podem ser usadas em combate com um toque. Toque em Bolsa.", () => inventory.IsOpen),
                new(() => BagPotion() is { } potion ? inventory.TileOf(potion.InstanceId) : null, "Escolha a poção", "Toque na poção na sua bolsa.",
                    () => BagPotion() is { } potion && inventory.SelectedItem == potion.InstanceId || !BeltEmpty()),
                new(() => inventory.ActionStartingWith("Cinto"), "Guarde no cinto", "Toque em Cinto 1 para guardar a poção. Ela aparece ao lado do botão de ataque.",
                    () => !BeltEmpty())
            ], () => !inventory.IsOpen && BeltEmpty()),
            new("auto", () => Idle() && tracker.AutoButton.Visibility == ViewStates.Visible && !tracker.Collapsed,
            [
                new(() => tracker.AutoButton, "Auto-missão", "Toque aqui e o personagem segue a missão sozinho: anda até o NPC, caça e entrega. Toque de novo para pausar.",
                    () => session.Navigator is { Enabled: true })
            ]),
            new("attack", () => Idle() && Hunting() && session.Target is { MaxLife: > 0 },
            [
                new(Attack, "Hora de lutar", "Um monstro está por perto. Toque em Atacar para lutar com ele.", () => session.Quest is not { Kills: 0 })
            ]),
            new("points", () => Idle() && session.Owner is { StatPoints: > 0 },
            [
                new(() => player, "Pontos de atributo", "Você subiu de nível e ganhou pontos para fortalecer seu personagem. Toque no seu retrato "
                    + "para abrir o Status.", () => status.IsOpen),
                new(() => status.PointsView, "Pontos livres", "Aqui ficam os pontos que você ainda não usou. " + status.Advice),
                new(() => status.PlusOf(status.Main), "Gaste um ponto", $"Toque em + ao lado de {status.MainName}. Cada toque gasta um ponto e "
                    + "vale na hora.", () => status.Spent > 0)
            ], () => !status.IsOpen)
        ];
    }

    public void Refresh()
    {
        if (_lesson is null)
        {
            _lesson = _lessons.FirstOrDefault(lesson => !_records.TutorialDone(lesson.Id) && lesson.Starts());
            _step = 0;
            if (_lesson is null)
            {
                return;
            }
        }

        if (_step > 0 && _lesson.Lost?.Invoke() == true)
        {
            _step = 0;
        }

        while (_step < _lesson.Steps.Length && _lesson.Steps[_step].Done?.Invoke() == true)
        {
            _step++;
        }

        if (_step >= _lesson.Steps.Length)
        {
            Finish();
            return;
        }

        Show(_lesson.Steps[_step]);
    }

    private void Show(Step step)
    {
        if (_coach is null)
        {
            _coach = new Coach(_activity, this);
            _overlay.AddView(_coach, new FrameLayout.LayoutParams(-1, -1));
        }

        _coach.BringToFront();
        _coach.Point(step, step.Target?.Invoke(), _step, _lesson.Steps.Length);
    }

    private void Next()
    {
        _step++;
        Refresh();
    }

    private void Finish()
    {
        if (_lesson is not null)
        {
            _records.MarkTutorial(_lesson.Id);
        }

        _lesson = null;
        if (_coach is not null)
        {
            _overlay.RemoveView(_coach);
            _coach = null;
        }
    }

    // The dimmed screen with its round window over the target, the pulsing ring and the card.
    private sealed class Coach : FrameLayout
    {
        private readonly VulkanTutorial _tutorial;
        private readonly Paint _dim = new() { Color = Color.Argb(170, 0, 0, 0) };
        private readonly Paint _ring = new() { AntiAlias = true };
        private readonly global::Android.Graphics.Path _shape = new();
        private readonly LinearLayout _card;
        private readonly TextView _title;
        private readonly TextView _text;
        private readonly TextView _count;
        private readonly Button _ok;
        private readonly int[] _here = new int[2];
        private readonly int[] _there = new int[2];
        private View _target;
        private float _x;
        private float _y;
        private float _radius;
        private float _halfWidth;
        private float _halfHeight;

        public Coach(Activity activity, VulkanTutorial tutorial) : base(activity)
        {
            _tutorial = tutorial;
            SetWillNotDraw(false);
            Clickable = true;
            _ring.SetStyle(Paint.Style.Stroke);
            var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int value) => (int)MathF.Round(value * density);

            _card = new LinearLayout(activity) { Orientation = Orientation.Vertical, Background = new PlateDrawable(Dp(12)) };
            _card.SetPadding(Dp(16), Dp(12), Dp(16), Dp(12));
            _card.Clickable = true;
            var heading = new LinearLayout(activity);
            _title = new TextView(activity) { TextSize = 17, Typeface = Typeface.Create("serif", TypefaceStyle.Bold) };
            _title.SetTextColor(MobileHudTheme.Gold);
            heading.AddView(_title, new LinearLayout.LayoutParams(0, -2, 1));
            _count = new TextView(activity) { TextSize = 11 };
            _count.SetTextColor(Color.Rgb(168, 199, 193));
            heading.AddView(_count);
            _card.AddView(heading);
            _text = new TextView(activity) { TextSize = 14 };
            _text.SetTextColor(MobileHudTheme.Ink);
            _card.AddView(_text, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
            var buttons = new LinearLayout(activity);
            buttons.SetGravity(GravityFlags.End);
            var skip = MobileHudTheme.Button(activity, "Pular tutorial");
            skip.Click += (_, _) => tutorial.Finish();
            buttons.AddView(skip, new LinearLayout.LayoutParams(-2, Dp(38)) { RightMargin = Dp(6) });
            _ok = MobileHudTheme.Button(activity, "Entendi", prominent: true);
            _ok.Click += (_, _) => tutorial.Next();
            buttons.AddView(_ok, new LinearLayout.LayoutParams(-2, Dp(38)));
            _card.AddView(buttons, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(10) });
            AddView(_card, new FrameLayout.LayoutParams(Dp(340), -2));
        }

        public void Point(Step step, View target, int index, int count)
        {
            _target = target is { IsShown: true, Width: > 0 } ? target : null;
            _title.Text = step.Title;
            _text.Text = step.Text;
            _count.Text = count > 1 ? $"{index + 1}/{count}" : "";
            _ok.Visibility = step.Done is null ? ViewStates.Visible : ViewStates.Gone;
            Place();
            Invalidate();
        }

        // The window over the target and the card beside it: below the target when there is room, else above.
        private void Place()
        {
            var card = (FrameLayout.LayoutParams)_card.LayoutParameters;
            if (_target is null || Width == 0)
            {
                _radius = 0f;
                card.Gravity = GravityFlags.Center;
                card.LeftMargin = card.TopMargin = 0;
                _card.LayoutParameters = card;
                return;
            }

            GetLocationOnScreen(_here);
            _target.GetLocationOnScreen(_there);
            var scale = _target.ScaleX;
            _x = _there[0] - _here[0] + _target.Width * scale / 2f;
            _y = _there[1] - _here[1] + _target.Height * scale / 2f;
            _halfWidth = _target.Width * scale / 2f;
            _halfHeight = _target.Height * scale / 2f;
            _radius = MathF.Max(_halfWidth, _halfHeight) + 14f;
            card.Gravity = GravityFlags.Top | GravityFlags.Left;
            var cardWidth = card.Width;
            var height = _card.Height > 0 ? _card.Height : Height / 4;
            var below = _y + _radius + 16f;
            var above = _y - _radius - 16f - height;
            if (below + height < Height || above >= 12f)
            {
                card.LeftMargin = (int)Math.Clamp(_x - cardWidth / 2f, 12f, Math.Max(12f, Width - cardWidth - 12f));
                card.TopMargin = (int)(below + height < Height ? below : above);
            }
            else
            {
                // A tall or wide target leaves no room above or below: the card goes beside its window, on the wider side.
                var left = _x - _radius - 16f - cardWidth;
                card.LeftMargin = (int)Math.Clamp(_x > Width / 2f ? left : _x + _radius + 16f, 12f, Math.Max(12f, Width - cardWidth - 12f));
                card.TopMargin = (int)Math.Clamp(_y - height / 2f, 12f, Math.Max(12f, Height - height - 12f));
            }

            _card.LayoutParameters = card;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            _shape.Reset();
            _shape.SetFillType(global::Android.Graphics.Path.FillType.EvenOdd!);
            _shape.AddRect(0, 0, Width, Height, global::Android.Graphics.Path.Direction.Cw!);
            if (_radius > 0f)
            {
                _shape.AddCircle(_x, _y, _radius, global::Android.Graphics.Path.Direction.Cw!);
            }

            canvas.DrawPath(_shape, _dim);
            if (_radius > 0f)
            {
                var pulse = (float)(Math.Sin(global::Android.OS.SystemClock.UptimeMillis() / 180.0) * .5 + .5);
                _ring.StrokeWidth = 4f + pulse * 4f;
                _ring.Color = Color.Argb((int)(160 + pulse * 95), 255, 214, 120);
                canvas.DrawCircle(_x, _y, _radius + pulse * 10f, _ring);
                PostInvalidateOnAnimation();
            }
        }

        // The card always takes its own touches (Entendi, Pular tutorial), even where it overlaps the window; a touch on the
        // target itself reaches it below; the rest of the screen waits for the tutorial.
        public override bool DispatchTouchEvent(MotionEvent e)
        {
            var x = e.GetX();
            var y = e.GetY();
            var onCard = x >= _card.Left && x <= _card.Right && y >= _card.Top && y <= _card.Bottom;
            if (!onCard && _radius > 0f && MathF.Abs(x - _x) <= _halfWidth && MathF.Abs(y - _y) <= _halfHeight)
            {
                return false;
            }

            return base.DispatchTouchEvent(e);
        }
    }
}
