using PristonTale.Gameplay.World;

namespace PristonTale.Gameplay.Screens.Gameplay;

// The touch HUD's free auto-hunt (FreeHuntSession) in the world: made with the scene, fed every presence update and
// every confirmed pickup, run once a frame in place of the quest hunt while it is on, paused by the player's own
// commands and stopped by a warp, the quest hunt or the automatic quest, the PC HUD or a page in the background.
public sealed partial class GameplayScreen
{
    private FreeHuntSession _freeHunt;

    private void AttachFreeHunt()
    {
        _freeHunt = new FreeHuntSession(_scene);
        _scene.PresenceObserved += _freeHunt.Observe;
        _scene.GroundPickup += _freeHunt.ObservePickup;
        _hud.AttachFreeHunt(_freeHunt);
    }

    private void DetachFreeHunt()
    {
        if (_freeHunt is null)
        {
            return;
        }

        _hud.SaveHuntRecord(force: true);
        _scene.PresenceObserved -= _freeHunt.Observe;
        _scene.GroundPickup -= _freeHunt.ObservePickup;
    }

    // The monster the free hunt fights this frame, 0 for none; manualInput is the player's own command this frame.
    private ulong UpdateFreeHunt(bool manualInput)
    {
        var hunt = _freeHunt;
        if (hunt.Enabled && (!TouchInterface || !Context.IsWindowActive))
        {
            hunt.SetEnabled(false, HuntStopReason.Interface);
        }
        else if (hunt.Enabled && (_scene.Warping || _hud.AutoHuntEnabled || _hud.QuestGuideEnabled))
        {
            hunt.SetEnabled(false, HuntStopReason.MapChange);
        }

        if (manualInput)
        {
            hunt.NoteManualInput();
        }

        var field = _scene.ActiveFieldDefinition;
        var canHunt = TouchInterface && !_scene.PlayerDead && !_scene.Warping && !_hud.InTown && field is not null;
        var target = hunt.Update(_hud.FreeHuntFrame(canHunt, field?.FieldCode ?? -1));
        _hud.SaveHuntRecord(force: false);
        return target;
    }
}
