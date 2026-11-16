using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.World;

// The glow of every drawn player's held weapon and off-hand item (HeldItemGlow, from the server's description of the
// player): each frame the pulse is painted over the item's skill look, and a weapon with the class-up setting carries
// its circling lights while its holder is alive, as does a weapon whose holder carries a status with a weapon orbit
// (WeaponOrbitLights).
public sealed partial class GameScene
{
    private readonly WeaponOrbitLights _weaponOrbits = new();
    private HeldItemGlow _ownWeaponGlow = HeldItemGlow.None;
    private HeldItemGlow _ownOffHandGlow = HeldItemGlow.None;
    private HeldItemGlow _ownArmorGlow = HeldItemGlow.None;
    private HeldItemGlow _ownerWeaponAging = HeldItemGlow.None;
    private IReadOnlyList<OwnedItemView> _glowInventory;
    private Action<ulong, PlayerActorHandles, PlayerAppearanceInfo, bool> _holdRemote;
    private long _heldClockMs;
    private float _heldSeconds;

    // The local player's glows arrive with its own description.
    private void ObserveOwnGlow(PresenceUpdate update)
    {
        if (_remote is null)
        {
            return;
        }

        foreach (var spawn in update.Spawns)
        {
            if (spawn.EntityId == _remote.SelfEntityId && spawn.Player is { } appearance)
            {
                _ownWeaponGlow = appearance.WeaponGlow;
                _ownOffHandGlow = appearance.OffHandGlow;
                if (_glowInventory is null)
                {
                    _ownArmorGlow = HeldItemGlow.FromAging(appearance.ArmorAge);
                }
            }
        }

        if (update.Owner is { } owner && !ReferenceEquals(_glowInventory, owner.Items))
        {
            _glowInventory = owner.Items;
            _ownArmorGlow = EquippedAging(owner.Items, EquipmentSlot.Armor);
            _ownerWeaponAging = EquippedAging(owner.Items, EquipmentSlot.Weapon);
        }
    }

    private static HeldItemGlow EquippedAging(IReadOnlyList<OwnedItemView> inventory, EquipmentSlot slot) =>
        HeldItemGlow.FromAging(OwnedItemView.InSlot(inventory, (int)slot)?.Details?.UpgradeLevel ?? 0);

    private void UpdateHeldItems(float seconds)
    {
        if (_looks is null || _skillFx is null || _remote is null)
        {
            return;
        }

        _heldClockMs = (long)_skillClockMs;
        _heldSeconds = seconds;
        if (_equipmentPresentation is not null && _playerBody is not null)
        {
            var weapon = _ownerWeaponAging.IsMaximumAging && !_ownWeaponGlow.IsMaximumAging
                ? _ownerWeaponAging with { ClassUp = _ownWeaponGlow.ClassUp } : _ownWeaponGlow;
            Hold(_remote.SelfEntityId, _equipmentPresentation.Handles, weapon, _ownOffHandGlow, _ownArmorGlow, _equipmentPresentation.WeaponKey,
                !_playerController.IsDead);
        }

        _remote.ForEachHolder(_holdRemote ??= (entityId, handles, appearance, shown) =>
            Hold(entityId, handles, appearance.WeaponGlow, appearance.OffHandGlow, HeldItemGlow.FromAging(appearance.ArmorAge), appearance.WeaponKey, shown));
        UpdateShots(seconds);
        _looks.EndGlowFrame();
        _weaponOrbits.EndFrame();
    }

    private void Hold(ulong entityId, PlayerActorHandles handles, in HeldItemGlow weapon, in HeldItemGlow offHand,
        in HeldItemGlow armor, string weaponKey, bool shown)
    {
        _looks.PaintGlow(handles.Weapon, weapon, _heldClockMs);
        _looks.PaintGlow(handles.OffHand, handles.Twin ? weapon : offHand, _heldClockMs);
        if (shown)
        {
            _looks.PaintGlow(handles.Body, armor, _heldClockMs, armor: true);
        }

        if (!shown || weaponKey.Length == 0 || !_renderer.TryGetActorWorld(handles.Weapon, out var world)
            || !_renderer.TryGetActorReach(handles.Weapon, out var reach))
        {
            return;
        }

        var leftHand = EquipmentVisuals.HeldInLeftHand(weaponKey);
        var twoHanded = ItemEquipmentMetadata.IsTwoHanded(weaponKey);
        if (weapon.ClassUp || weapon.IsMaximumAging)
        {
            _weaponOrbits.Hold(_skillFx, entityId, WeaponOrbitLights.ClassUp, WeaponOrbitLights.ClassUpLight, WeaponOrbitLights.ClassUpTrail, world, reach,
                weaponKey, leftHand, twoHanded, _heldSeconds);
        }

        foreach (var status in _skillFx.Statuses.For(entityId))
        {
            if (SkillRuleCatalog.TryGet(status.Code, out var rule) && rule.WeaponOrbit.Length == 2)
            {
                _weaponOrbits.Hold(_skillFx, entityId, status.Code, rule.WeaponOrbit[0], rule.WeaponOrbit[1], world, reach, weaponKey, leftHand,
                    twoHanded, _heldSeconds);
            }
        }
    }
}
