using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

internal sealed class VulkanHudLifecycle : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    // How long a quest dialog stays before the automatic quest answers it.
    private const int AutoAnswerMilliseconds = 2200;

    private static readonly (int Code, string Name)[] MinimapFields =
    [
        (PristonTale.Simulation.World.FieldCodes.Ricarten, "ricarten"),
        (PristonTale.Simulation.World.FieldCodes.Pillai, "pillai"),
        (PristonTale.Simulation.World.FieldCodes.ForeverFall1, "forever-fall-01")
    ];

    public void OnActivityCreated(Activity activity, Bundle state)
    {
        if (activity.Class.Name != "org.libsdl.app.SDLActivity")
        {
            return;
        }

        var decor = activity.Window?.DecorView;
        VulkanLoadingScreen loading = null;
        void WaitForFirstFrame()
        {
            if (activity.IsFinishing || activity.IsDestroyed)
            {
                return;
            }

            if (ProbeApplication.FirstFrameReady)
            {
                (loading?.Parent as ViewGroup)?.RemoveView(loading);
            }
            else
            {
                if (loading is null)
                {
                    loading = new VulkanLoadingScreen(activity, "Carregando…");
                    activity.AddContentView(loading, new ViewGroup.LayoutParams(-1, -1));
                }

                decor?.PostDelayed(WaitForFirstFrame, 60);
            }
        }

        decor?.Post(WaitForFirstFrame);
        void WaitForWorld()
        {
            if (activity.IsFinishing || activity.IsDestroyed)
                return;
            if (ProbeApplication.CurrentWorld is { Play: not null } session)
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
        var portraitSize = (int)(Math.Min(activity.Resources!.DisplayMetrics!.WidthPixels, activity.Resources.DisplayMetrics.HeightPixels) * .105f);
        var portraitLayout = new FrameLayout.LayoutParams(portraitSize, portraitSize, GravityFlags.Top | GravityFlags.Left)
        {
            LeftMargin = (int)(portraitSize * .24f),
            TopMargin = (int)(portraitSize * .24f)
        };
        overlay.AddView(hud.Portrait, portraitLayout);
        var statusLayout = Place(152, -2, GravityFlags.Top | GravityFlags.Left);
        statusLayout.LeftMargin += portraitSize;
        overlay.AddView(hud.Status, statusLayout);
        overlay.AddView(hud.Experience, Place(-1, 15, GravityFlags.Bottom, 0));

        var chat = MobileHudTheme.Button(activity, "Chat");
        MobileHudTheme.Icon(chat, HudGlyph.Chat);
        chat.Click += (_, _) => OpenChat(activity, session);
        var chatLayout = Place(67, -2, GravityFlags.Top | GravityFlags.Left);
        chatLayout.TopMargin = Dp(74);
        overlay.AddView(chat, chatLayout);

        var right = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        // A minimap for each field one is packaged for, shown while the player is in it.
        var maps = new Dictionary<int, MobileMinimap>();
        foreach (var (code, name) in MinimapFields)
        {
            if (MobileMinimap.Open(activity, name) is { } fieldMap)
            {
                fieldMap.Visibility = ViewStates.Gone;
                maps[code] = fieldMap;
                right.AddView(fieldMap, new LinearLayout.LayoutParams(Dp(124), Dp(124)));
            }
        }
        var attack = MobileHudTheme.Button(activity, "Atacar", prominent: true);
        MobileHudTheme.Icon(attack, HudGlyph.Attack, stacked: true);
        attack.SetSingleLine(true);
        attack.TextSize = 12;
        attack.Click += (_, _) => session.AttackSelected();
        overlay.AddView(attack, Place(72, 64, GravityFlags.Bottom | GravityFlags.Right, 20));

        var menu = new LinearLayout(activity);
        var mainMenu = MobileHudTheme.Button(activity, "Menu");
        MobileHudTheme.Icon(mainMenu, HudGlyph.Menu);
        var inventory = new VulkanInventoryPanel(activity, overlay, session);
        var bag = MobileHudTheme.Button(activity, "Bolsa");
        MobileHudTheme.Icon(bag, HudGlyph.Bag);
        bag.Click += (_, _) => inventory.Toggle();
        menu.AddView(mainMenu);
        menu.AddView(hud.Journal);
        menu.AddView(bag);
        overlay.AddView(menu, Place(-2, -2, GravityFlags.Bottom | GravityFlags.Left, 19));
        var navigation = new LinearLayout(activity) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        navigation.SetPadding(Dp(14), Dp(10), Dp(14), Dp(12));
        navigation.Clickable = true;
        var menuFrame = new Android.Graphics.Drawables.GradientDrawable(
            Android.Graphics.Drawables.GradientDrawable.Orientation.TopBottom,
            [Color.Rgb(23, 48, 53).ToArgb(), Color.Rgb(12, 20, 28).ToArgb()]);
        menuFrame.SetCornerRadius(Dp(12));
        menuFrame.SetStroke(Dp(1), Color.Rgb(168, 132, 71));
        navigation.Background = menuFrame;
        var heading = new LinearLayout(activity);
        navigation.AddView(heading);
        var identity = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        heading.AddView(identity, new LinearLayout.LayoutParams(0, -2, 1));
        var characterName = new TextView(activity) { Text = session.Character.Name, TextSize = 20 };
        characterName.SetTextColor(MobileHudTheme.Gold);
        characterName.Typeface = Typeface.Create("serif", TypefaceStyle.Bold);
        identity.AddView(characterName);
        var version = activity.PackageManager?.GetPackageInfo(activity.PackageName!, Android.Content.PM.PackageInfoFlags.Activities)?.VersionName;
        if (!string.IsNullOrWhiteSpace(version))
        {
            var versionLabel = new TextView(activity) { Text = $"Priston Avalon · {version}", TextSize = 11 };
            versionLabel.SetTextColor(Color.Rgb(168, 199, 193));
            identity.AddView(versionLabel);
        }
        var closeMenu = MobileHudTheme.Button(activity, "×");
        closeMenu.ContentDescription = "Fechar menu";
        closeMenu.Click += (_, _) => navigation.Visibility = ViewStates.Gone;
        heading.AddView(closeMenu, new LinearLayout.LayoutParams(Dp(42), Dp(42)));
        overlay.AddView(navigation, Place(304, -2, GravityFlags.Bottom | GravityFlags.Left, 66));
        mainMenu.Click += (_, _) => navigation.Visibility = navigation.Visibility == ViewStates.Visible
            ? ViewStates.Gone : ViewStates.Visible;
        var firstRow = AddMenuRow();
        AddMenuAction(firstRow, "Personagem", HudGlyph.Skill, () => OpenStatus(activity, session));
        AddMenuAction(firstRow, "Inventário", HudGlyph.Bag, inventory.Toggle);
        var secondRow = AddMenuRow();
        AddMenuAction(secondRow, "Missões", HudGlyph.Journal, () => hud.Journal.PerformClick());
        AddMenuAction(secondRow, "Movimento", HudGlyph.Run, session.ToggleRun);
        var footer = AddMenuRow();
        AddMenuAction(footer, "Sair do jogo", HudGlyph.Exit, activity.FinishAffinity, stacked: false);

        LinearLayout AddMenuRow()
        {
            var row = new LinearLayout(activity);
            navigation.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
            return row;
        }

        void AddMenuAction(LinearLayout row, string name, HudGlyph glyph, Action action, bool stacked = true)
        {
            var entry = MobileHudTheme.Button(activity, name);
            MobileHudTheme.Icon(entry, glyph, stacked);
            entry.Click += (_, _) =>
            {
                navigation.Visibility = ViewStates.Gone;
                action();
            };
            row.AddView(entry, new LinearLayout.LayoutParams(0, Dp(stacked ? 48 : 38), 1) { RightMargin = Dp(3) });
        }

        var messages = new TextView(activity) { TextSize = 11 };
        messages.SetMaxLines(4);
        messages.SetTextColor(MobileHudTheme.Ink);
        messages.SetShadowLayer(2f, 0f, 1f, Color.Black);
        var messageLayout = Place(225, -2, GravityFlags.Top | GravityFlags.Left);
        messageLayout.TopMargin = Dp(114);
        overlay.AddView(messages, messageLayout);
        activity.AddContentView(overlay, new ViewGroup.LayoutParams(-1, -1));

        var journal = new MobileQuestJournal(activity);
        var npcPanel = new VulkanNpcPanel(activity, overlay, session);
        // While the automatic quest runs, a dialog shows long enough to be read and answers its first choice itself.
        async Task<int?> Present(string title, string message, string[] choices, CancellationToken ct)
        {
            if (session.Navigator is not { AnswersDialogs: true } || choices.Length == 0)
            {
                return await npcPanel.ShowAsync(title, message, choices, ct);
            }

            using var shown = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var answer = npcPanel.ShowAsync(title, message, choices, shown.Token);
            if (await Task.WhenAny(answer, Task.Delay(AutoAnswerMilliseconds, ct)) == answer)
            {
                return await answer;
            }

            shown.Cancel();
            try
            {
                await answer;
            }
            catch (System.OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }

            return 0;
        }

        var questDialog = new MobileQuestDialog(activity, session.Play, messages, Present);
        var questLifetime = new CancellationTokenSource();
        overlay.ViewDetachedFromWindow += (_, _) => questLifetime.Cancel();
        var shortcuts = new MobileShortcuts(activity, session.Play, messages, questLifetime.Token);
        overlay.AddView(shortcuts.View, Place(-2, -2, GravityFlags.Bottom | GravityFlags.CenterHorizontal, 18));
        var tracker = new VulkanQuestTracker(activity, session);
        hud.Journal.Click += (_, _) => journal.Open(() => session.Navigator?.SetEnabled(true));
        right.AddView(tracker.View, new LinearLayout.LayoutParams(Dp(172), -2) { TopMargin = Dp(5) });
        overlay.AddView(right, Place(-2, -2, GravityFlags.Top | GravityFlags.Right));

        var readingNpc = false;
        async Task OpenNpcAsync(ulong id)
        {
            readingNpc = true;
            try
            {
                var interaction = await session.Play.InteractNpcAsync(id, questLifetime.Token);
                if (interaction.Ok)
                {
                    npcPanel.EntityId = id;
                    npcPanel.NpcName = interaction.Name;
                    var handled = await questDialog.TalkAsync(id, interaction.Name, questLifetime.Token);
                    if (!handled && interaction.Dialog.Count > 0)
                    {
                        await npcPanel.ShowAsync(interaction.Name, string.Join("\n\n", interaction.Dialog), ["OK"], questLifetime.Token);
                    }
                }
            }
            catch (System.OperationCanceledException) when (questLifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                messages.Text = "Não foi possível abrir a missão.";
            }
            finally
            {
                npcPanel.EntityId = 0;
                npcPanel.NpcName = null;
                readingNpc = false;
            }
        }

        PristonTale.Simulation.Quests.QuestView previousQuest = null;
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
                shortcuts.Update(owner);
                inventory.Update(owner);
                previousOwner = owner;
            }

            if (session.Quest is { } quest && !ReferenceEquals(previousQuest, quest))
            {
                previousQuest = quest;
                journal.Update(quest);
            }

            tracker.Refresh();
            if (session.MapPose is { } pose)
            {
                var marks = session.Navigator?.Marks;
                foreach (var (code, fieldMap) in maps)
                {
                    var here = code == pose.FieldId;
                    fieldMap.Visibility = here ? ViewStates.Visible : ViewStates.Gone;
                    if (here)
                    {
                        fieldMap.UpdatePosition(pose.Position, pose.Facing);
                        fieldMap.SetRoute(marks is { } shown && shown.FieldCode == code ? shown.Points : []);
                    }
                }
            }
            shortcuts.SetTarget(session.AttackTarget);
            if (!readingNpc && session.TryReadNpc(out var npc))
            {
                _ = OpenNpcAsync(npc);
            }
            if (!readingNpc && session.TryReadPrompt(out var prompt))
            {
                questDialog.ShowWorldPrompt(prompt, questLifetime.Token);
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

    private static void OpenStatus(Activity activity, VulkanWorldSession session)
    {
        if (session.Owner is { } owner)
        {
            ShowPanel(activity, "Status", $"Nível {owner.Level}\n\nForça  {owner.Strength}\nEspírito  {owner.Spirit}\nTalento  {owner.Talent}\n"
                + $"Agilidade  {owner.Agility}\nVitalidade  {owner.Health}\n\nAtaque  {owner.DamageMin}–{owner.DamageMax}\nDefesa  {owner.Defence}");
        }
    }

    private static void ShowPanel(Activity activity, string title, string text)
    {
        var content = new TextView(activity) { Text = text, TextSize = 15 };
        content.SetTextColor(MobileHudTheme.Ink);
        content.SetPadding(22, 16, 22, 16);
        var scroll = new ScrollView(activity);
        MobileHudTheme.Card(scroll);
        scroll.AddView(content);
        new AlertDialog.Builder(activity).SetTitle(title)!.SetView(scroll)!.SetNegativeButton("Fechar", (_, _) => { })!.Show();
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
