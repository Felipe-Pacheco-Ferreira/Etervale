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
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Events;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

// Quests in the HUD: the dialog, its boxes and the quest window.
public sealed partial class GameplayHud
{
    private string _questNavigationMessage = "";
    internal bool QuestGuideEnabled { get; private set; }
    internal bool QuestTrackingEnabled => !_context.Presentation.Requested.HideQuestTracker;
    public event Action<RoutePoint> QuestDestinationRequested;
    public event Action QuestNavigationCanceled;

    // Quest notices go to the combat log (chat lines), the notice strip (events and messages) or the fanfare; a new quest
    // dialog takes the help box from the NPC dialog until it closes.
    private void ApplyQuests()
    {
        _questWindow.DailyOpened ??= RefreshDailyQuestRewards;
        _questWindow.ClaimDaily ??= ClaimDailyQuestReward;
        _questWindow.NavigateRequested ??= NavigateSelectedQuest;
        _questWindow.ToggleAutoHunt ??= ToggleQuickHunt;
        _questWindow.ToggleGuide ??= ToggleQuestGuide;
        _questWindow.TrackingChanged ??= SetQuestTracking;
        _questWindow.SetTracking(QuestTrackingEnabled);
        _questWindow.AutoHuntEnabled = AutoHuntEnabled;
        _questWindow.GuideEnabled = QuestGuideEnabled;
        _questWindow.CanNavigate = _questWindow.Page == HudQuestPage.Running && CanNavigateTrackedQuest;
        _questWindow.SetDailyRewards(_rewardBoard.Find(RewardTrack.Daily), _state?.Level ?? 1,
            Environment.TickCount64 < _rewardDeadline,
            _questWindow.Page == HudQuestPage.Daily ? _rewardMessage : _questNavigationMessage);
        while (_quests.TryTakeNotice(out var notice))
        {
            switch (notice.Kind)
            {
                case QuestNoticeKind.Chat:
                    _chat.Assist(ClientText.Format(notice.Text));
                    break;
                case QuestNoticeKind.Fanfare:
                    QuestFanfare?.Invoke();
                    break;
                default:
                    _notice.Show(ClientText.Format(notice.Text));
                    break;
            }
        }

        if (_quests.DialogsOpened != _questDialogs)
        {
            _questDialogs = _quests.DialogsOpened;
            ClearQuestNavigationNotice();
            _skills.CancelTraining();
            _settingsOpen = false;
            foreach (var document in _quests.Dialog?.ChoiceDocuments ?? [])
            {
                _questDocuments.Lines(HudQuestDocuments.DialogPath(document));
            }
        }

        // Quest texts load off the game thread; asking early has them ready when the dialog or window draws.
        if (_quests.Dialog is not null)
        {
            _questDocuments.Lines(HudQuestDocuments.DialogPath(_quests.DialogDocument));
        }

        if (!ReferenceEquals(_quests.View, _questView))
        {
            _questView = _quests.View;
            _questViewMs = _clock.Elapsed.TotalMilliseconds;
            _weaponHunt = _questView is { Code: RankUpByQuestWeapon.Code }
                ? RankUpByQuestWeapon.HuntLine(QuestClasses.WeaponHunt(_class), _questView.WeaponStage, _questView.WeaponKillsLeft)
                : "";
            _questWindow.Refresh(_questView);
            if (QuestGuideEnabled)
            {
                NavigateSelectedQuest();
            }
            foreach (var entry in _questView is null ? [] : _questView.Progress.Concat(_questView.Finished))
            {
                _questDocuments.Lines(HudQuestDocuments.BoardPath(entry.Description));
            }
        }
    }

    // The quest dialog, its yes/no box and the skill redistribution offer are modal: they take every click.
    private bool ClickQuests(HudLayout layout, Point pointer)
    {
        if (_quests.SkillResetBox)
        {
            if (HudMessageBox.Accept.Contains(pointer) || HudMessageBox.Reject.Contains(pointer))
            {
                _quests.AnswerSkillReset(HudMessageBox.Accept.Contains(pointer));
            }

            return true;
        }

        if (_quests.Confirming)
        {
            if (HudMessageBox.Accept.Contains(pointer) || HudMessageBox.Reject.Contains(pointer))
            {
                _quests.Answer(HudMessageBox.Accept.Contains(pointer));
            }

            return true;
        }

        if (_quests.Dialog is { } dialog)
        {
            if (HudQuestDialog.OkButton.Contains(pointer))
            {
                _quests.Ok();
            }
            else if (HudQuestDialog.ChoiceAt(dialog, pointer) is var choice and >= 0)
            {
                _quests.Choose(choice);
            }
            else if (dialog.Followup == QuestFollowup.ConfirmChoice && HudQuestDialog.PreviousArrow.Contains(pointer))
            {
                _quests.BrowseBy(-1);
            }
            else if (dialog.Followup == QuestFollowup.ConfirmChoice && HudQuestDialog.NextArrow.Contains(pointer))
            {
                _quests.BrowseBy(1);
            }

            return HudQuestDialog.Box.Contains(pointer);
        }

        return _questWindow.Click(layout, pointer, _questView);
    }

    private void RefreshDailyQuestRewards() => _ = RequestDailyQuestRewardsAsync(RewardEventsAction.Open, 0);

    private void ClaimDailyQuestReward(int index)
    {
        var entry = _rewardBoard.Find(RewardTrack.Daily)?.Entries.ElementAtOrDefault(index);
        if (entry?.State != RewardEntryState.Ready || Environment.TickCount64 < _rewardDeadline || SendRewards is null)
        {
            return;
        }
        _rewardDeadline = Environment.TickCount64 + 8000;
        _rewardMessage = "";
        _ = RequestDailyQuestRewardsAsync(RewardEventsAction.Claim, index);
    }

    private async Task RequestDailyQuestRewardsAsync(RewardEventsAction action, int index)
    {
        if (SendRewards is null)
        {
            return;
        }
        try
        {
            await SendRewards(action, RewardTrack.Daily, index);
        }
        catch (Exception exception)
        {
            _rewardDeadline = 0;
            _rewardMessage = ClientText.Get("Rewards.UpdateFailed");
            _context.Log.Warn($"[Quest] daily reward request failed: {exception.Message}");
        }
    }

    internal bool CanNavigateTrackedQuest => QuestDestinationRequested is not null && TryJourneyDestination(_questView, _class, out _);

    internal void NavigateSelectedQuest()
    {
        if (_questWindow.Page == HudQuestPage.Running)
        {
            NavigateTrackedQuest();
        }
    }

    internal void NavigateTrackedQuest()
    {
        if (TryJourneyDestination(_questView, _class, out var destination))
        {
            QuestDestinationRequested?.Invoke(destination);
        }
    }

    private void ToggleQuestGuide()
    {
        QuestGuideEnabled = !QuestGuideEnabled;
        if (QuestGuideEnabled)
        {
            NavigateSelectedQuest();
        }
        else
        {
            QuestNavigationCanceled?.Invoke();
        }
    }

    private void SetQuestTracking(bool enabled)
    {
        _context.Presentation.Requested = _context.Presentation.Requested with { HideQuestTracker = !enabled };
    }

    internal void CancelQuestGuide()
    {
        QuestGuideEnabled = false;
    }

    internal void QuestNavigationNotice(string message)
    {
        _questNavigationMessage = message;
        _chat.Assist(message);
    }

    internal void ClearQuestNavigationNotice() => _questNavigationMessage = "";

    internal static bool TryJourneyDestination(QuestView view, PlayerClass playerClass, out RoutePoint destination)
    {
        destination = default;
        if (view is null)
        {
            return false;
        }
        var welcome = view.Code == QuestCode.None && view.Progress.Any(entry =>
            entry.Description == AdventurerJourneyRoute.MeetSkadiDescription || entry.Description == AdventurerJourneyRoute.MeetJinDescription);
        if (!welcome && view.Code != QuestCode.AdventurerJourney)
        {
            return false;
        }
        var route = AdventurerJourneyRoute.For(playerClass);
        destination = view is { Code: QuestCode.AdventurerJourney, KillsNeeded: > 0 } && view.Kills < view.KillsNeeded
            ? route.HuntPoints[0]
            : route.Guide;
        return true;
    }
}
