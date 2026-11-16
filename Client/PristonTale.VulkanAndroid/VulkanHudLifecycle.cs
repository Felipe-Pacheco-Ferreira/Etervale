using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

internal sealed class VulkanHudLifecycle : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    // How many chat lines the chat window keeps.
    private const int MostChatHistory = 50;
    // How many chat lines show on screen under the chat button.
    private const int ChatFeedLines = 5;
    private const int SkillNoticeMs = 3500;
    private const float PreferredGameRefreshRate = 90f;

    // The minimaps the APK carries (Android assets <name>-minimap.png/.bin); every other field's comes with its pack.
    private static readonly Dictionary<int, string> ApkMinimaps = new()
    {
        [PristonTale.Simulation.World.FieldCodes.Ricarten] = "ricarten",
        [PristonTale.Simulation.World.FieldCodes.Pillai] = "pillai",
        [PristonTale.Simulation.World.FieldCodes.ForeverFall1] = "forever-fall-01",
        [PristonTale.Simulation.World.Fields.Forest1Field.Definition.FieldCode] = "forest-01"
    };

    public void OnActivityCreated(Activity activity, Bundle state)
    {
        if (activity.Class.Name != "org.libsdl.app.SDLActivity")
        {
            return;
        }

        var window = activity.Window;
        var decor = window?.DecorView;
        decor?.Post(() => PreferGameRefreshRate(window));
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
        VulkanDownloadCard.Attach(activity);
        WaitForWorld(activity);
    }

    // Puts the HUD over the world once one is entered; again after the player went back to the character selection.
    private static void WaitForWorld(Activity activity)
    {
        var decor = activity.Window?.DecorView;
        void Wait()
        {
            if (activity.IsFinishing || activity.IsDestroyed)
            {
                return;
            }

            if (ProbeApplication.CurrentWorld is { Play: not null, FramesDrawn: > 0 } session)
            {
                Attach(activity, session);
            }
            else
            {
                decor?.PostDelayed(Wait, 100);
            }
        }

        decor?.Post(Wait);
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
        session.HuntSettings = editor.FreeHuntSettingsOf(session.Character.Name, session.HuntProfileScope);
        session.RestoreHuntLog(editor.FreeHuntLogOf(session.Character.Name));
        session.RestoreHuntTotals(editor.FreeHuntTotalsOf(session.Character.Name));
        var icons = new VulkanItemIcons(session.AssetRoot, action => activity.RunOnUiThread(action));
        var freeHunt = new VulkanFreeHuntPanel(activity, overlay, session, editor, icons);
        var targetFrame = new VulkanTargetFrame(activity, screenHeight);
        overlay.AddView(targetFrame, Pixels((int)(HudPlacement.TargetFrameWidth * screenHeight) + 2, targetFrame.FrameHeight, GravityFlags.Top | GravityFlags.CenterHorizontal));
        var player = new VulkanPlayerFrame(activity, session.Character, screenHeight);
        overlay.AddView(player, Pixels(player.FrameWidth, player.FrameHeight, GravityFlags.Top | GravityFlags.Left));
        player.Clickable = true;
        var status = new VulkanStatusPanel(activity, overlay, session);
        var statusSeen = false;
        player.Click += (_, _) =>
        {
            statusSeen = true;
            status.Open();
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
        // The frames per second and latency line, left of the minimap.
        var stats = new VulkanStatsReadout(activity) { Visibility = editor.StatsHidden ? ViewStates.Gone : ViewStates.Visible };
        var statsLayout = Place(-2, -2, GravityFlags.Top | GravityFlags.Right);
        statsLayout.RightMargin = Dp(10 + 124 + 8);
        overlay.AddView(stats, statsLayout);
        // Each field's minimap, made the first time the player is in the field (and its minimap is on the phone), shown
        // while the player is there.
        var maps = new Dictionary<int, MobileMinimap>();
        var worldMap = new VulkanWorldMap(activity, overlay, session);
        void EnsureMinimap(int code)
        {
            if (maps.ContainsKey(code))
            {
                return;
            }

            var fieldMap = ApkMinimaps.TryGetValue(code, out var name) ? MobileMinimap.Open(activity, name)
                : VulkanPacks.MinimapOf(code) is { } prefix ? MobileMinimap.OpenFile(activity, prefix) : null;
            if (fieldMap is null)
            {
                return;
            }

            fieldMap.Visibility = ViewStates.Gone;
            maps[code] = fieldMap;
            var title = VulkanWorldMap.FieldTitle(code);
            fieldMap.Click += (_, _) => worldMap.Open(fieldMap, title);
            mapBox.AddView(fieldMap, new FrameLayout.LayoutParams(-1, -1));
        }
        var inventory = new VulkanInventoryPanel(activity, overlay, session, icons);
        var journal = new VulkanQuestJournal(activity, overlay, session);
        void OpenJournal() => journal.Open(() => session.Navigator?.SetEnabled(true));

        // The menu buttons stand on their own, so the HUD editor moves each.
        var menuOrbs = new List<VulkanActionOrb>();
        var mainMenu = MenuOrb(HudGlyph.Menu, "Menu");
        var bagOrb = MenuOrb(HudGlyph.Bag, "Bolsa");
        var freeHuntOrb = MenuOrb(HudGlyph.Auto, "Auto-caça");
        var huntState = VulkanPanelArt.Chip(activity, "Auto-caça · Ligado", VulkanPanelArt.Done);
        huntState.Visibility = ViewStates.Gone;
        var huntStateLayout = Place(-2, -2, GravityFlags.Bottom | GravityFlags.Left, 12);
        huntStateLayout.BottomMargin = Dp(91);
        overlay.AddView(huntState, huntStateLayout);
        // Items that came into the bag since the player last looked in it: a count on the bag button.
        var seenItems = new HashSet<Guid>();
        var bagSeenOnce = false;
        void OpenBag()
        {
            inventory.Toggle();
            SeeBag();
        }

        void SeeBag()
        {
            if (session.Owner is not { } current)
            {
                return;
            }

            seenItems.Clear();
            foreach (var item in current.Items)
            {
                seenItems.Add(item.InstanceId);
            }

            bagSeenOnce = true;
            bagOrb.SetBadge(0);
        }

        void CountNewItems(PristonTale.Simulation.Characters.OwnerState current)
        {
            if (!bagSeenOnce || inventory.IsOpen)
            {
                SeeBag();
                return;
            }

            var fresh = 0;
            foreach (var item in current.Items)
            {
                if (!seenItems.Contains(item.InstanceId))
                {
                    fresh++;
                }
            }

            bagOrb.SetBadge(fresh);
        }

        bagOrb.Click += (_, _) => OpenBag();
        var rewards = new VulkanRewardsPanel(activity, overlay, session, icons);
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
        freeHuntOrb.Click += (_, _) =>
        {
            navigation.Visibility = ViewStates.Gone;
            if (session.FreeHuntEnabled)
            {
                session.SetFreeHunt(false);
                RefreshFreeHuntButton();
                return;
            }

            freeHunt.Open();
        };
        freeHuntOrb.LongClick += (_, args) =>
        {
            args.Handled = true;
            navigation.Visibility = ViewStates.Gone;
            freeHunt.Open();
        };

        void RefreshFreeHuntButton()
        {
            var active = session.FreeHuntEnabled;
            var showStatus = active && session.HuntSettings.ShowStatus ? ViewStates.Visible : ViewStates.Gone;
            if (huntState.Visibility != showStatus)
            {
                huntState.Visibility = showStatus;
            }

            if (showStatus == ViewStates.Visible)
            {
                var text = session.HuntManuallyPaused ? "Auto-caça · Pausa manual" : "Auto-caça · Ligado";
                if (huntState.Text != text)
                {
                    huntState.Text = text;
                }
            }
            if (freeHuntOrb.Selected != active)
            {
                freeHuntOrb.Selected = active;
                freeHuntOrb.SetCaption(active ? "Pausar" : "Auto-caça");
                freeHuntOrb.ContentDescription = active ? "Pausar Auto-caça" : "Auto-caça";
            }
        }

        var firstRow = AddMenuRow();
        AddMenuAction(firstRow, "Inventário", HudGlyph.Bag, OpenBag);
        AddMenuAction(firstRow, "Missões", HudGlyph.Journal, OpenJournal);
        var eventsEntry = AddMenuAction(firstRow, "Eventos", HudGlyph.Gift, rewards.Open);
        var secondRow = AddMenuRow();
        // The skills window needs the action wheel, made further down.
        VulkanSkillsPanel skills = null;
        AddMenuAction(secondRow, "Habilidades", HudGlyph.Skill, () => skills.Open());
        AddMenuAction(secondRow, "Corrida", HudGlyph.Run, session.ToggleRun);
        var thirdRow = AddMenuRow();
        AddMenuAction(thirdRow, "Gráficos", HudGlyph.Menu, () => VulkanHudWindows.Graphics(activity, overlay, session, !editor.StatsHidden, shown =>
        {
            editor.StatsHidden = !shown;
            stats.Visibility = shown ? ViewStates.Visible : ViewStates.Gone;
        }), stacked: false);
        AddMenuAction(thirdRow, "Editar HUD", HudGlyph.Menu, () => editor.Open(), stacked: false);
        AddMenuAction(AddMenuRow(), "Mapas baixados", HudGlyph.Menu, () => _ = VulkanPackMenu.OpenAsync(activity, overlay), stacked: false);
        AddMenuAction(secondRow, "Correios", HudGlyph.Chat, () => VulkanHudWindows.Mail(activity, overlay));
        var calendar = new VulkanEventCalendar(activity, overlay, session);
        AddMenuAction(AddMenuRow(), "Calendário", HudGlyph.Calendar, calendar.Open, stacked: false);
        var footer = AddMenuRow();
        AddMenuAction(footer, "Trocar personagem", HudGlyph.Journal, session.RequestCharacterSelection, stacked: false);
        AddMenuAction(footer, "Sair do jogo", HudGlyph.Exit, activity.FinishAffinity, stacked: false);

        LinearLayout AddMenuRow()
        {
            var row = new LinearLayout(activity);
            navigation.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
            return row;
        }

        Button AddMenuAction(LinearLayout row, string name, HudGlyph glyph, Action action, bool stacked = true)
        {
            var entry = MobileHudTheme.Button(activity, name);
            MobileHudTheme.Icon(entry, glyph, stacked);
            entry.Click += (_, _) =>
            {
                navigation.Visibility = ViewStates.Gone;
                action();
            };
            row.AddView(entry, new LinearLayout.LayoutParams(0, Dp(stacked ? 48 : 38), 1) { RightMargin = Dp(3) });
            return entry;
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

        var openNpcPortraits = 0;
        var npcPanel = new VulkanNpcPanel(activity, overlay, session, visible =>
        {
            openNpcPortraits += visible ? 1 : -1;
            var hudVisibility = openNpcPortraits == 0 ? ViewStates.Visible : ViewStates.Invisible;
            targetFrame.Visibility = hudVisibility;
        });
        async Task<int?> Present(string title, string message, string[] choices, CancellationToken ct)
        {
            var navigator = session.Navigator;
            bool ShouldAdvance() => navigator?.AnswersDialog(choices.Length) == true;
            var selected = await npcPanel.ShowAsync(title, message, choices, ct, ShouldAdvance);
            if (selected is null && navigator is { Enabled: true })
            {
                navigator.SetEnabled(false);
            }

            return selected;
        }

        var questDialog = new MobileQuestDialog(activity, session.Play, messages, Present,
            (title, message, choices, ct) => npcPanel.ShowAsync(title, message, choices, ct));
        var questLifetime = new CancellationTokenSource();
        overlay.ViewDetachedFromWindow += (_, _) => questLifetime.Cancel();
        var wheelRight = Dp(8);
        var wheel = new VulkanActionWheel(activity, session, icons, messages, screenHeight, wheelRight, Dp(20), questLifetime.Token);
        wheel.Restore(editor.SkillSlotsOf(session.Character.Name));
        wheel.SkillsChosen += codes => editor.KeepSkillSlots(session.Character.Name, codes);
        overlay.AddView(wheel.View, new FrameLayout.LayoutParams(-1, -1));
        skills = new VulkanSkillsPanel(activity, overlay, session, icons, wheel);
        // NPC dialogs sit between the menu buttons and the wheel, at their default places.
        var lastOrb = (FrameLayout.LayoutParams)menuOrbs[^1].LayoutParameters!;
        npcPanel.LeftClear = lastOrb.LeftMargin + lastOrb.Width;
        npcPanel.RightClear = wheelRight + wheel.BoxPixelsWide;
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
        editor.Register("bag", "Bolsa", bagOrb);
        editor.Register("freeHunt", "Auto-caça", freeHuntOrb);
        editor.Register("minimap", "Minimapa", mapBox);
        editor.Register("stats", "FPS e ping", stats);
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
        var restart = new VulkanRestartPanel(activity, overlay, session);
        var party = new VulkanPartyHud(activity, overlay, session);
        var inviteLayout = Place(-2, 34, GravityFlags.Top | GravityFlags.CenterHorizontal, 0);
        inviteLayout.TopMargin = targetFrame.FrameHeight + Dp(2);
        overlay.AddView(party.Invite, inviteLayout);
        overlay.AddView(party.Members, Place(-2, -2, GravityFlags.Bottom | GravityFlags.Left, 12));
        ((FrameLayout.LayoutParams)party.Members.LayoutParameters!).BottomMargin = Dp(96);
        editor.Register("party", "Grupo", party.Members);
        var tutorial = new VulkanTutorial(activity, overlay, editor,
            VulkanTutorial.Lessons(session, player, tracker.View, mainMenu, bagOrb, wheel, tracker, inventory, status, () => statusSeen));

        async Task OpenNpcAsync(ulong id)
        {
            session.NpcConversationOpen = true;
            try
            {
                var interaction = await session.Play.InteractNpcAsync(id, questLifetime.Token);
                if (interaction.Ok)
                {
                    npcPanel.EntityId = id;
                    npcPanel.NpcName = interaction.Name;
                    var master = interaction.Offers(PristonTale.Simulation.World.Npcs.NpcServiceKind.SkillMaster);
                    if (master)
                    {
                        skills.Master = id;
                    }

                    var handled = await questDialog.TalkAsync(id, interaction.Name, questLifetime.Token);
                    if (!handled && interaction.Dialog.Count > 0)
                    {
                        await npcPanel.ShowAsync(interaction.Name, string.Join("\n\n", interaction.Dialog), ["OK"], questLifetime.Token);
                    }

                    // A skill master opens the skills window once the talk ends, unless the automatic quest is playing.
                    if (master && session.Navigator is not { Enabled: true })
                    {
                        skills.Open();
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
                session.NpcConversationOpen = false;
            }
        }

        PristonTale.Simulation.Quests.QuestView previousQuest = null;
        PristonTale.Simulation.Characters.OwnerState previousOwner = null;
        string lastSkillNotice = null;
        long skillNoticeUntil = 0;
        PristonTale.Client.Net.Presence.RewardEventsInfo previousRewards = null;
        TargetView lastTarget = null;
        void Refresh()
        {
            if (activity.IsFinishing || activity.IsDestroyed || !overlay.IsAttachedToWindow)
            {
                return;
            }

            if (!ReferenceEquals(ProbeApplication.CurrentWorld, session))
            {
                // The player went back to the character selection: this HUD leaves with its world and the next waits.
                (overlay.Parent as ViewGroup)?.RemoveView(overlay);
                WaitForWorld(activity);
                return;
            }

            var owner = session.Owner;
            if (owner is not null && !ReferenceEquals(owner, previousOwner))
            {
                player.Update(owner);
                CountNewItems(owner);
                experience.Update(owner);
                feed.Observe(owner);
                wheel.Update(owner);
                inventory.Update(owner);
                status.Update(owner);
                skills.Update(owner);
                previousOwner = owner;
            }
            wheel.RefreshCooldowns();
            RefreshFreeHuntButton();
            freeHunt.Refresh();

            if (session.Rewards is { } rewardWindow && !ReferenceEquals(previousRewards, rewardWindow))
            {
                previousRewards = rewardWindow;
                rewards.Update(rewardWindow);
                // Rewards to claim: the count on the menu button and on its Eventos entry.
                mainMenu.SetBadge(rewardWindow.Ready);
                VulkanAttention.Dot(eventsEntry, rewardWindow.Ready);
            }

            while (session.TryReadRewardResult(out var rewardResult))
            {
                rewards.Result(rewardResult);
            }

            if (session.Quest is { } quest && !ReferenceEquals(previousQuest, quest))
            {
                var offerReset = quest.SkillResetOffered && previousQuest?.SkillResetOffered != true;
                previousQuest = quest;
                journal.Update(quest);
                feed.Observe(quest);
                if (offerReset)
                {
                    _ = questDialog.ShowSkillResetAsync(questLifetime.Token);
                }
            }

            var target = session.Target;
            targetFrame.Show(target);
            lastTarget = target ?? lastTarget;
            while (session.TryReadKill(out var killed))
            {
                feed.Kill(killed, lastTarget);
            }

            if (stats.Visibility == ViewStates.Visible)
            {
                stats.Update(session.FramesDrawn, session.LatencyMs, System.Environment.TickCount64, session.Meter);
            }

            tracker.Refresh();
            worldMap.Refresh();
            restart.Refresh();
            party.Refresh();
            if (!editor.IsOpen)
            {
                tutorial.Refresh();
            }
            if (session.MapPose is { } pose)
            {
                EnsureMinimap(pose.FieldId);
                var marks = session.Navigator?.Marks;
                var hunt = VulkanWorldMap.HuntAreaIn(pose.FieldId, session.JourneyRoute, session.Quest);
                foreach (var (code, fieldMap) in maps)
                {
                    var here = code == pose.FieldId;
                    fieldMap.Visibility = here ? ViewStates.Visible : ViewStates.Gone;
                    if (here)
                    {
                        fieldMap.UpdatePosition(pose.Position, pose.Facing);
                        fieldMap.SetRoute(marks is { } shown && shown.FieldCode == code ? shown.Points : []);
                        fieldMap.SetHuntArea(hunt is { } area ? new System.Numerics.Vector2(area.X, area.Z) : default, hunt?.Radius ?? 0f);
                    }
                }
            }
            if (!session.NpcConversationOpen && session.TryReadNpc(out var npc))
            {
                _ = OpenNpcAsync(npc);
            }
            if (!session.NpcConversationOpen && session.TryReadPrompt(out var prompt))
            {
                questDialog.ShowWorldPrompt(prompt, questLifetime.Token);
            }

            while (session.TryReadSkillNotice(out var skillNotice))
            {
                messages.Text = skillNotice;
                lastSkillNotice = skillNotice;
                skillNoticeUntil = System.Environment.TickCount64 + SkillNoticeMs;
            }

            if (lastSkillNotice is not null && System.Environment.TickCount64 >= skillNoticeUntil)
            {
                if (messages.Text == lastSkillNotice)
                {
                    messages.Text = "";
                }

                lastSkillNotice = null;
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
            if (ProbeApplication.CurrentWorld is { } world)
            {
                _ = world.SuspendFreeHuntAsync();
            }

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

    private static void PreferGameRefreshRate(Window window)
    {
        var display = window.DecorView.Display;
        if (display?.GetMode() is not { } current || window.Attributes is not { } attributes)
        {
            return;
        }

        Display.Mode preferred = null;
        foreach (var mode in display.GetSupportedModes())
        {
            if (mode.PhysicalWidth == current.PhysicalWidth && mode.PhysicalHeight == current.PhysicalHeight
                && mode.RefreshRate > 60.1f
                && (preferred is null || MathF.Abs(mode.RefreshRate - PreferredGameRefreshRate) < MathF.Abs(preferred.RefreshRate - PreferredGameRefreshRate)))
            {
                preferred = mode;
            }
        }

        if (preferred is null)
        {
            return;
        }

        attributes.PreferredDisplayModeId = preferred.ModeId;
        window.Attributes = attributes;
        if (FindSurface(window.DecorView) is { } view && view.Holder is { } holder)
        {
            var refreshRate = new GameSurfaceRefreshRate(preferred.RefreshRate);
            holder.AddCallback(refreshRate);
            refreshRate.SurfaceCreated(holder);
        }
    }

    private static SurfaceView FindSurface(View view)
    {
        if (view is SurfaceView surface)
        {
            return surface;
        }

        if (view is ViewGroup group)
        {
            for (var index = 0; index < group.ChildCount; index++)
            {
                if (FindSurface(group.GetChildAt(index)) is { } child)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private sealed class GameSurfaceRefreshRate(float rate) : Java.Lang.Object, ISurfaceHolderCallback
    {
        public void SurfaceCreated(ISurfaceHolder holder)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30) && holder.Surface is { IsValid: true } surface)
            {
                surface.SetFrameRate(rate, (int)SurfaceFrameRateCompatibility.Default);
            }
        }

        public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height) { }
        public void SurfaceDestroyed(ISurfaceHolder holder) { }
    }

    public void OnActivitySaveInstanceState(Activity activity, Bundle state) { }
    public void OnActivityStarted(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
}
