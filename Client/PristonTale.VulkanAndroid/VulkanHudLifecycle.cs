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
    // How many chat lines the chat window keeps.
    private const int MostChatHistory = 50;
    // How many chat lines show on screen under the chat button.
    private const int ChatFeedLines = 5;

    private static readonly (int Code, string Name, string Title)[] MinimapFields =
    [
        (PristonTale.Simulation.World.FieldCodes.Ricarten, "ricarten", "Ricarten"),
        (PristonTale.Simulation.World.FieldCodes.Pillai, "pillai", "Pillai"),
        (PristonTale.Simulation.World.FieldCodes.ForeverFall1, "forever-fall-01", "Forever Fall 1")
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

        var overlay = new FrameLayout(activity);
        var metrics = activity.Resources!.DisplayMetrics!;
        var density = metrics.Density;
        // The HUD is laid out in fractions of the screen height (HudPlacement), the short side in landscape.
        var screenHeight = (float)Math.Min(metrics.WidthPixels, metrics.HeightPixels);
        int Dp(int value) => (int)MathF.Round(value * density);
        FrameLayout.LayoutParams Place(int width, int height, GravityFlags gravity, int edge = 10) =>
            new(width < 0 ? width : Dp(width), height < 0 ? height : Dp(height), gravity)
            {
                LeftMargin = Dp(edge), RightMargin = Dp(edge), TopMargin = Dp(edge), BottomMargin = Dp(edge)
            };
        FrameLayout.LayoutParams Pixels(int width, int height, GravityFlags gravity) => new(width, height, gravity);

        var editor = new HudLayoutEditor(activity, overlay, screenHeight);
        var targetFrame = new VulkanTargetFrame(activity, screenHeight);
        overlay.AddView(targetFrame, Pixels((int)(HudPlacement.TargetFrameWidth * screenHeight) + 2, targetFrame.FrameHeight, GravityFlags.Top | GravityFlags.CenterHorizontal));
        var player = new VulkanPlayerFrame(activity, session.Character.Name, screenHeight);
        overlay.AddView(player, Pixels(player.FrameWidth, player.FrameHeight, GravityFlags.Top | GravityFlags.Left));
        player.Clickable = true;
        var statusSeen = false;
        player.Click += (_, _) =>
        {
            statusSeen = true;
            VulkanHudWindows.Status(activity, overlay, session);
        };
        var experience = new VulkanExperienceBar(activity);
        overlay.AddView(experience, Place(-1, 18, GravityFlags.Bottom, 0));

        var chat = new VulkanActionOrb(activity, HudGlyph.Chat) { ContentDescription = "Chat" };
        var chatHistory = new List<VulkanWorldSession.ChatEntry>();
        // The last chat lines under the chat button, unless the player hid them from the chat window.
        var chatFeed = new TextView(activity) { TextSize = 11.5f, Clickable = false };
        chatFeed.SetMaxLines(ChatFeedLines);
        chatFeed.SetShadowLayer(3f, 0f, 1.2f, Color.Black);
        chatFeed.SetLineSpacing(Dp(1), 1f);
        chatFeed.Visibility = editor.ChatHidden ? ViewStates.Gone : ViewStates.Visible;
        void ShowFeed()
        {
            using var text = VulkanChatText.Format(chatHistory, chatHistory.Count - ChatFeedLines);
            chatFeed.SetText(text, TextView.BufferType.Spannable);
        }

        chat.Click += (_, _) => VulkanHudWindows.Chat(activity, overlay, session, chatHistory, !editor.ChatHidden, shown =>
        {
            editor.ChatHidden = !shown;
            chatFeed.Visibility = shown ? ViewStates.Visible : ViewStates.Gone;
        });
        var chatLayout = Place(44, 44, GravityFlags.Top | GravityFlags.Left, 12);
        chatLayout.TopMargin = player.FrameHeight + Dp(4);
        overlay.AddView(chat, chatLayout);
        var chatFeedLayout = Place(300, -2, GravityFlags.Top | GravityFlags.Left, 12);
        chatFeedLayout.TopMargin = player.FrameHeight + Dp(52);
        overlay.AddView(chatFeed, chatFeedLayout);

        var mapBox = new FrameLayout(activity);
        overlay.AddView(mapBox, Place(124, 124, GravityFlags.Top | GravityFlags.Right));
        // A minimap for each field one is packaged for, shown while the player is in it.
        var maps = new Dictionary<int, MobileMinimap>();
        var worldMap = new VulkanWorldMap(activity, overlay, session);
        foreach (var (code, name, title) in MinimapFields)
        {
            if (MobileMinimap.Open(activity, name) is { } fieldMap)
            {
                fieldMap.Visibility = ViewStates.Gone;
                maps[code] = fieldMap;
                fieldMap.Click += (_, _) => worldMap.Open(fieldMap, title);
                mapBox.AddView(fieldMap, new FrameLayout.LayoutParams(-1, -1));
            }
        }
        var icons = new VulkanItemIcons(session.AssetRoot, action => activity.RunOnUiThread(action));
        var inventory = new VulkanInventoryPanel(activity, overlay, session, icons);
        var journal = new VulkanQuestJournal(activity, overlay, session);
        void OpenJournal() => journal.Open(() => session.Navigator?.SetEnabled(true));

        // The menu buttons stand on their own, so the HUD editor moves each.
        var menuOrbs = new List<VulkanActionOrb>();
        var mainMenu = MenuOrb(HudGlyph.Menu, "Menu");
        var journalOrb = MenuOrb(HudGlyph.Journal, "Missões");
        journalOrb.Click += (_, _) => OpenJournal();
        var bagOrb = MenuOrb(HudGlyph.Bag, "Bolsa");
        bagOrb.Click += (_, _) => inventory.Toggle();
        VulkanActionOrb MenuOrb(HudGlyph glyph, string caption)
        {
            var orb = new VulkanActionOrb(activity, glyph) { ContentDescription = caption };
            orb.SetCaption(caption);
            var layout = Place(58, 58, GravityFlags.Bottom | GravityFlags.Left, 22);
            layout.LeftMargin = Dp(22 + 64 * menuOrbs.Count);
            overlay.AddView(orb, layout);
            menuOrbs.Add(orb);
            return orb;
        }

        var navigation = new LinearLayout(activity) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        navigation.SetPadding(Dp(14), Dp(10), Dp(14), Dp(12));
        navigation.Clickable = true;
        navigation.Background = new PlateDrawable(Dp(14));
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
        var navigationLayout = Place(304, -2, GravityFlags.Bottom | GravityFlags.Left, 22);
        navigationLayout.BottomMargin = Dp(90);
        overlay.AddView(navigation, navigationLayout);
        mainMenu.Click += (_, _) => navigation.Visibility = navigation.Visibility == ViewStates.Visible
            ? ViewStates.Gone : ViewStates.Visible;
        var firstRow = AddMenuRow();
        AddMenuAction(firstRow, "Inventário", HudGlyph.Bag, inventory.Toggle);
        AddMenuAction(firstRow, "Missões", HudGlyph.Journal, OpenJournal);
        var secondRow = AddMenuRow();
        AddMenuAction(secondRow, "Movimento", HudGlyph.Run, session.ToggleRun);
        var thirdRow = AddMenuRow();
        AddMenuAction(thirdRow, "Gráficos", HudGlyph.Menu, () => VulkanHudWindows.Graphics(activity, overlay, session), stacked: false);
        AddMenuAction(thirdRow, "Editar HUD", HudGlyph.Menu, () => editor.Open(), stacked: false);
        AddMenuAction(secondRow, "Rever tutorial", HudGlyph.Journal, editor.ReplayTutorials);
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
        messages.SetTextColor(MobileHudTheme.Gold);
        messages.SetShadowLayer(3f, 0f, 1.2f, Color.Black);
        var messageLayout = Place(260, -2, GravityFlags.Top | GravityFlags.Left, 12);
        // Below the chat lines.
        messageLayout.TopMargin = player.FrameHeight + Dp(52 + ChatFeedLines * 17);
        overlay.AddView(messages, messageLayout);
        activity.AddContentView(overlay, new ViewGroup.LayoutParams(-1, -1));

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
        var wheel = new VulkanActionWheel(activity, session, icons, messages, screenHeight, Dp(8), Dp(20), questLifetime.Token);
        overlay.AddView(wheel.View, new FrameLayout.LayoutParams(-1, -1));
        var feed = new VulkanCombatFeed(activity, screenHeight);
        var feedLayout = Pixels((int)(screenHeight * .6f), (int)(screenHeight * .3f), GravityFlags.Bottom | GravityFlags.Right);
        feedLayout.BottomMargin = wheel.BoxPixelsHigh + Dp(20);
        feedLayout.RightMargin = Dp(12);
        overlay.AddView(feed, feedLayout);
        var tracker = new VulkanQuestTracker(activity, session);
        var trackerLayout = Place(172, -2, GravityFlags.Top | GravityFlags.Right);
        trackerLayout.TopMargin = Dp(10 + 124 + 5);
        overlay.AddView(tracker.View, trackerLayout);
        tracker.Collapsed = editor.QuestFolded;
        tracker.Folded += folded => editor.QuestFolded = folded;
        editor.Register("player", "Personagem", player, HudPlacement.ShiftPlayer);
        editor.Register("target", "Alvo", targetFrame, HudPlacement.ShiftTarget);
        editor.Register("chat", "Chat", chat);
        editor.Register("chatFeed", "Histórico do chat", chatFeed);
        editor.Register("menu", "Menu", mainMenu);
        editor.Register("journal", "Missões", journalOrb);
        editor.Register("bag", "Bolsa", bagOrb);
        editor.Register("minimap", "Minimapa", mapBox);
        editor.Register("quest", "Missão", tracker.View);
        editor.Register("attack", "Atacar", wheel.Attack);
        for (var slot = 0; slot < wheel.Skills.Count; slot++)
        {
            editor.Register($"skill-{slot}", $"Habilidade {slot + 1}", wheel.Skills[slot]);
        }

        for (var box = 0; box < wheel.Potions.Count; box++)
        {
            editor.Register($"potion-{box}", $"Poção {box + 1}", wheel.Potions[box]);
        }

        editor.Register("feed", "Abates", feed);
        editor.Register("experience", "Experiência", experience);
        var tutorial = new VulkanTutorial(activity, overlay, editor,
            VulkanTutorial.Lessons(session, player, tracker.View, mainMenu, bagOrb, wheel, tracker, inventory, () => statusSeen));

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
        PristonTale.Simulation.Characters.OwnerState previousOwner = null;
        TargetView lastTarget = null;
        void Refresh()
        {
            if (activity.IsFinishing || activity.IsDestroyed || !overlay.IsAttachedToWindow)
            {
                return;
            }

            var owner = session.Owner;
            if (owner is not null && !ReferenceEquals(owner, previousOwner))
            {
                player.Update(owner);
                experience.Update(owner);
                feed.Observe(owner);
                wheel.Update(owner);
                inventory.Update(owner);
                previousOwner = owner;
            }

            if (session.Quest is { } quest && !ReferenceEquals(previousQuest, quest))
            {
                previousQuest = quest;
                journal.Update(quest);
                feed.Observe(quest);
            }

            var target = session.Target;
            targetFrame.Show(target);
            lastTarget = target ?? lastTarget;
            while (session.TryReadKill(out var killed))
            {
                feed.Kill(killed, lastTarget);
            }

            tracker.Refresh();
            worldMap.Refresh();
            if (!editor.IsOpen)
            {
                tutorial.Refresh();
            }
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
                chatHistory.Add(line);
                if (chatHistory.Count > MostChatHistory)
                {
                    chatHistory.RemoveAt(0);
                }

                changed = true;
            }
            if (changed)
            {
                ShowFeed();
            }
            overlay.PostDelayed(Refresh, 250);
        }
        overlay.Post(Refresh);
    }

    public void OnActivityDestroyed(Activity activity) { }
    public void OnActivityPaused(Activity activity)
    {
        if (activity.Class.Name == "org.libsdl.app.SDLActivity")
        {
            SurfaceGate.Close();
        }
    }

    public void OnActivityResumed(Activity activity)
    {
        if (activity.Class.Name == "org.libsdl.app.SDLActivity")
        {
            SurfaceGate.Open();
        }
    }

    public void OnActivitySaveInstanceState(Activity activity, Bundle state) { }
    public void OnActivityStarted(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
}
