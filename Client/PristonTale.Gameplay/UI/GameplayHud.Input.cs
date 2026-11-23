using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.UI;

// Mouse and keyboard: keys, clicks routed to the windows in priority order, menu buttons and toggles, and what the HUD captures.
public sealed partial class GameplayHud
{
    // The bar's buttons and toggles, read every frame for the pointer.
    private static readonly HudMenuButton[] MenuButtons = Enum.GetValues<HudMenuButton>();
    private static readonly HudToggle[] Toggles = Enum.GetValues<HudToggle>();

    private bool ClickBuff(Point pointer)
    {
        if (Statuses is null || SelfId == 0)
        {
            return false;
        }

        var code = _skillHud.BuffAt(pointer);
        if (code == 0)
        {
            return false;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (code == _lastBuffClicked && now - _lastBuffClickSeconds < PristonTale.Gameplay.UI.Parties.HudPartyWindow.DoubleClickSeconds && PristonTale.Simulation.Skills.SkillRuleCatalog.TryGet(code, out var rule) && rule.Summon is not null)
        {
            _lastBuffClicked = 0;
            SummonModeRequested?.Invoke(code);
            return true;
        }

        _lastBuffClicked = code;
        _lastBuffClickSeconds = now;
        return true;
    }

    private void OpenNews()
    {
        _systemMenu = false;
        _news.Open(_context.Presentation.Requested);
    }

    private void OpenSettingsPanel()
    {
        _systemMenu = false;
        if (WebWorldHud.OpenSettings is { } openWebSettings)
        {
            openWebSettings();
            return;
        }

        _settings.Open();
        _settingsOpen = true;
    }

    private void UpdateKeys(KeyboardState keyboard)
    {
        if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl))
        {
            if (Pressed(keyboard, Keys.B))
            {
                _actionBarVisible = !_actionBarVisible;
            }

            return;
        }

        if (_actionBarVisible)
        {
            for (var slot = 0; slot < HudSkillBindings.ShortcutCount; slot++)
            {
                if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.FirstActionBarShortcut + slot))
                {
                    SelectActionSlot(slot);
                    return;
                }
            }
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.InventoryShortcut) || (_settings.DefaultInventoryKey && Pressed(keyboard, Keys.I)))
        {
            ToggleInventory();
            HelpPetKey("InvenTory");
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.CharacterShortcut))
        {
            ToggleStatus();
            HelpPetKey("CarStatus");
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.SkillsShortcut))
        {
            ToggleSkills();
            HelpPetKey("Skill");
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, 5))
        {
            _systemMenu = !_systemMenu;
            HelpPetKey("System");
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, 3))
        {
            ToggleQuestWindow();
            HelpPetKey("Quest");
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, 4))
        {
            TogglePartyWindow();
            HelpPetKey("Party");
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, 6))
        {
            RunToggleRequested?.Invoke();
            HelpPetKey("RunAndWalk");
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.SettingsShortcut))
        {
            OpenSettingsPanel();
            return;
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.MinimapShortcut))
        {
            MapVisible = !MapVisible;
        }

        UpdateSettingsShortcuts(keyboard);

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.NewsShortcut))
        {
            OpenNews();
            return;
        }

        if (Pressed(keyboard, Keys.Space))
        {
            YahooKeyPressed?.Invoke();
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, 8))
        {
            _inventory.ToggleBagPage();
        }

        if (Pressed(keyboard, Keys.P) && _skillSlide.Open)
        {
            _skills.TurnPageByKey();
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, 9))
        {
            _inventory.ToggleWeaponPair();
        }

        var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        for (var box = 0; box < BeltSlotCount; box++)
        {
            if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.FirstBeltShortcut + box))
            {
                _inventory.BeltKey(box, shift);
                HelpPetKey("Potion_ShortKey", KeyIndex + box);
            }
        }
    }

    // The dismiss question's Yes (a click or Enter); 0 is any other answer.
    private void RequestCrystalPetDismiss(uint statusCode)
    {
        if (statusCode != 0)
        {
            CrystalPetDismissRequested?.Invoke(statusCode);
        }
    }

    private void Click(OwnerState state, HudLayout layout, Point pointer, Point design, KeyboardState keyboard)
    {
        if (ClickGlobalBonuses(layout, pointer) || ClickShopDirectory(layout, pointer) || ClickRecentDrops(layout, pointer) || ClickRewards(layout, pointer) || _news.Click(pointer)
            || !TouchHudShown && ClickActionBar(layout, pointer))
        {
            return;
        }

        if (_mail.Click(pointer, _chat) || (!_settingsOpen && !ModalOpen && ClickQuickMenu(layout, pointer)))
        {
            return;
        }

        // The count box holds every click until it closes.
        if (_countBox.IsOpen)
        {
            _countBox.Click(pointer);
            return;
        }

        if (_personalShop.ModalOpen)
        {
            _personalShop.Click(pointer);
            return;
        }

        // The help window lies over the helper pet's box.
        if (ClickHelp(pointer) || _helpPet.Click(pointer, _clock.Elapsed.TotalMilliseconds))
        {
            return;
        }

        if (!TouchHudShown && _restart.Click(layout, pointer, RestartOffer?.FieldOpen == true))
        {
            return;
        }

        if (_settingsOpen && HudSettingsPanel.Box.Contains(pointer))
        {
            if (HudSettingsPanel.OkButton.Contains(pointer))
            {
                _settings.Save();
                _settingsOpen = false;
            }
            else if (HudSettingsPanel.CancelButton.Contains(pointer) || HudSettingsPanel.CloseButton.Contains(pointer))
            {
                _settingsOpen = false;
            }
            else
            {
                _settings.Click(pointer, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            }

            return;
        }

        if (ClickTouchMap(layout, pointer))
        {
            return;
        }

        if (layout.Browser && _partyWindow.Contains(layout, pointer))
        {
            _partyWindow.Click(layout, pointer, Party, SelfId, _clock.Elapsed.TotalSeconds);
            return;
        }

        if (!TouchHudShown && _chat.Window.Click(layout, pointer, SideWindowOpen))
        {
            return;
        }

        if (_itemUse.Click(pointer, state) || _wingGate.Click(pointer, state) || _puzzle.Click(pointer, state, _services))
        {
            return;
        }

        if (_skills.TrainingPending)
        {
            _skills.HandleTrainingClick(PanelOrigin(layout, _skillSlide), pointer, state, _skillMaster, _progression);
            return;
        }

        if (_crystalDismiss.Click(pointer, out var dismissed))
        {
            RequestCrystalPetDismiss(dismissed);
            return;
        }

        if (ClickClan(pointer) || ClickCastle(pointer) || ClickBellatra(pointer) || ClickQuests(layout, pointer) || ClickDuel(layout, pointer) || ClickTrade(pointer) || ClickParty(layout, pointer))
        {
            return;
        }

        if (_npcOpen && !TouchHudShown && ClickNpc(state, layout, pointer))
        {
            return;
        }

        if (_personalShop.Click(pointer))
        {
            return;
        }

        if (_shop.Contains(pointer))
        {
            ClickShop(state, pointer);
            return;
        }

        if (_systemMenu)
        {
            var entry = HudSystemMenu.At(design, layout.Browser);
            if (entry is HudSystemEntry.Characters or HudSystemEntry.Servers)
            {
                _systemMenu = false;
                LobbyRequested?.Invoke(entry == HudSystemEntry.Servers);
                return;
            }
            if (entry == HudSystemEntry.Settings)
            {
                OpenSettingsPanel();
                return;
            }

            if (entry == HudSystemEntry.Help)
            {
                _systemMenu = false;
                _helpWindow.OpenHelp();
                return;
            }

            if (entry == HudSystemEntry.Exit)
            {
                _systemMenu = false;
                ExitRequested?.Invoke();
                return;
            }

            if (entry is not null)
            {
                return;
            }

            if (_hoveredButton != HudMenuButton.System)
            {
                _systemMenu = false;
            }
        }

        if (_hoveredButton is { } button)
        {
            PressButton(button);
            return;
        }

        if (ClickBuff(pointer))
        {
            return;
        }

        if (_hoveredToggle == HudToggle.Map)
        {
            MapVisible = !MapVisible;
            return;
        }

        if (_hoveredToggle == HudToggle.Walk)
        {
            RunToggleRequested?.Invoke();
            return;
        }

        ClickPanels(state, layout, pointer);
    }

    private bool ClickNpc(OwnerState state, HudLayout layout, Point pointer)
    {
        if (_npc.DestinationAt(pointer) is { } destination && state is not null)
        {
            ChooseNpcDestination(state, destination);
            return true;
        }

        if (_npc.ServiceAt(pointer) is { } service)
        {
            OpenNpcService(service);
            return true;
        }

        if (HudNpcPanel.OkButton.Contains(pointer))
        {
            CloseNpc();
            return true;
        }

        return HudNpcPanel.Box.Contains(pointer);
    }

    private void ChooseNpcDestination(OwnerState state, TeleportOffer destination)
    {
        var current = _npc.Current;
        // A Bellatra team booth offers its ticket as its one destination.
        var (kind, action) = _services.Open == NpcServiceKind.EventBooth
            ? (NpcServiceKind.EventBooth, NpcServiceAction.EnterBellatra)
            : (NpcServiceKind.Teleporter, NpcServiceAction.Teleport);
        if (_services.Request(current.EntityId, current.Name, kind, action, state.InventoryRevision, anchor: destination.Index))
        {
            _prepareField?.Invoke(destination.FieldCode);
        }
    }

    private void OpenNpcService(NpcServiceOffer service) =>
        _services.Request(_npc.Current.EntityId, _npc.Current.Name, service.Kind, NpcServiceAction.Open);

    private void CloseNpc()
    {
        _npcOpen = false;
        _npc.Dismiss();
        _quests.LeaveNpc();
    }

    private void PressButton(HudMenuButton button)
    {
        switch (button)
        {
            case HudMenuButton.Status:
                ToggleStatus();
                break;
            case HudMenuButton.Inventory:
                ToggleInventory();
                break;
            case HudMenuButton.Skills:
                ToggleSkills();
                break;
            case HudMenuButton.System:
                _systemMenu = !_systemMenu;
                break;
            case HudMenuButton.Quest:
                ToggleQuestWindow();
                break;
            case HudMenuButton.Party:
                TogglePartyWindow();
                break;
        }
    }

    private void ClickPanels(OwnerState state, HudLayout layout, Point pointer)
    {
        if (_inventorySlide.FullyOpen && HudInventoryLayout.Close(PanelOrigin(layout, _inventorySlide)).Contains(pointer))
        {
            _inventorySlide.Open = false;
            return;
        }

        if (_statusSlide.FullyOpen)
        {
            var origin = PanelOrigin(layout, _statusSlide);
            if (HudStatusPanel.Close(origin).Contains(pointer))
            {
                _statusSlide.Open = false;
                return;
            }

            if (ClickTitlePlaque(origin, pointer))
            {
                return;
            }

            if (state is not null && state.StatPoints > 0 && !_progression.Pending)
            {
                for (var index = 0; index < 5; index++)
                {
                    if (HudStatusPanel.AllocateButton(origin, index).Contains(pointer))
                    {
                        _progression.Submit(new ProgressionCommand(Guid.NewGuid(), state.ProgressionRevision,
                            ProgressionAction.AllocateAttribute, (CharacterAttribute)index, 1));
                    }
                }
            }
        }

        if (_skillSlide.FullyOpen)
        {
            var origin = PanelOrigin(layout, _skillSlide);
            if (HudSkillTreeLayout.Close(origin).Contains(pointer))
            {
                _skillSlide.Open = false;
                _skillMaster = 0;
                return;
            }

            if (_skills.ClickPage(origin, pointer))
            {
                return;
            }

            // The class change button: the master's next rank-up quest, which ends the training as the original does.
            if (_skillMaster != 0 && _quests.ClassChangeNpc == _skillMaster && HudSkillTreeLayout.ClassChange.Contains(pointer))
            {
                _quests.ChangeClass(_skillMaster);
                _skillSlide.Open = false;
                _skillMaster = 0;
                return;
            }

            _skills.HandleTrainingClick(origin, pointer, state, _skillMaster, _progression);
        }
    }

    private bool ServiceWindowOpen(bool blockingOnly)
    {
        foreach (var window in _serviceWindows)
        {
            if (window.IsOpen && (!blockingOnly || window.BlocksWorld))
            {
                return true;
            }
        }

        return false;
    }

    private bool ServiceWindowContains(Point pointer)
    {
        foreach (var window in _serviceWindows)
        {
            if (window.Contains(pointer))
            {
                return true;
            }
        }

        return false;
    }

    private bool Captures(HudLayout layout, Point pointer, Point design)
    {
        if (BlocksWorld)
        {
            return true;
        }

        if (_helpPet.Captures(pointer) || CapturesHelp(pointer) || CapturesParty(layout, pointer) || CapturesClan(pointer) || CapturesCastle(pointer)
            || CapturesBellatra(pointer) || !TouchHudShown && _tradeRequest.Captures(pointer) || CapturesDuel(layout, pointer) || _personalShop.Captures(pointer)
            || _itemUse.Captures(pointer) || _wingGate.Captures(pointer) || _puzzle.Captures(pointer) || _crystalDismiss.Captures(pointer) || _premium.Contains(pointer))
        {
            return true;
        }

        if (GlobalBonusesCapture(layout, pointer) || ShopDirectoryCaptures(layout, pointer) || RecentDropsCapture(layout, pointer) || RewardsCapture(layout, pointer) || QuickMenuCaptures(layout, pointer)
            || !TouchHudShown && (ActionBarCaptures(layout, pointer) || HudBar.Captures(design)) || MinimapShown && HudMinimap.Box(layout).Contains(pointer) || _skillHud.BuffAt(pointer) != 0
            || WindowContains(layout, _inventorySlide, pointer) || WindowContains(layout, _statusSlide, pointer) || TitlePlaqueCaptures(layout, pointer)
            || WindowContains(layout, _skillSlide, pointer)
            || _skills.MasterBoxShown && HudSkillTreeLayout.MasterBox.Contains(pointer))
        {
            return true;
        }

        if (ServiceWindowContains(pointer))
        {
            return true;
        }

        if (_quests.SkillResetBox || _quests.Confirming || !TouchHudShown && _quests.Dialog is not null && HudQuestDialog.Box.Contains(pointer)
            || _questWindow.Contains(layout, pointer) || _skillHud.QuestTimerAt(pointer))
        {
            return true;
        }

        return !TouchHudShown && _restart.Open && HudRestartPanel.Box(layout).Contains(pointer)
            || _systemMenu && HudSystemMenu.Captures(design, layout.Browser)
            || _settingsOpen && HudSettingsPanel.Box.Contains(pointer)
            || !TouchHudShown && _npcOpen && HudNpcPanel.Box.Contains(pointer)
            || _skills.TrainingPending && HudMessageBox.Bounds.Contains(pointer);
    }

    private static HudMenuButton? ButtonAt(Point design)
    {
        foreach (var button in MenuButtons)
        {
            if (HudBar.MenuButton(button).Contains(design))
            {
                return button;
            }
        }

        return null;
    }

    private static HudToggle? ToggleAt(Point design)
    {
        foreach (var toggle in Toggles)
        {
            if (HudBar.Toggle(toggle).Contains(design))
            {
                return toggle;
            }
        }

        return null;
    }

    private void TogglePanel(HudSlide slide)
    {
        if (slide.Open)
        {
            slide.Open = false;
            if (slide == _skillSlide)
            {
                _skillMaster = 0;
                _skills.CancelTraining();
            }

            return;
        }

        _skillMaster = 0;
        OpenOnly(slide);
    }

    // The status, skill and inventory windows share the same screen area; opening one closes the others (an item on the
    // inventory's cursor stays there, as the original keeps the mouse item).
    private void OpenOnly(HudSlide slide)
    {
        _inventorySlide.Open = slide == _inventorySlide;
        _statusSlide.Open = slide == _statusSlide;
        _skillSlide.Open = slide == _skillSlide;
        _skills.CancelTraining();
    }

    // The potion belt's boxes, left to right.
    private const int BeltSlotCount = 3;

    private bool Pressed(KeyboardState state, Keys key) => state.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
}
