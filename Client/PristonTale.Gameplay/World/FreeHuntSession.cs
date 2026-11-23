using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.Combat;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.World;

// What the free hunt reads of the game each frame: the owner, its quest, whether an NPC talks to it, whether
// the hunt may run here (alive, out of town, no warp) and the field it stands in.
public readonly record struct FreeHuntFrame(OwnerState Owner, QuestView Quest, bool NpcOpen, bool CanHunt, int FieldId);

// The free auto-hunt (FreeHuntSettings): from where it was started it fights the wild monsters its filters accept within
// the radius, casts the chosen skills, drinks potions, picks up drops by the loot rules, keeps itself safe and walks back
// to the start, through the same commands as manual play; the server checks every target, item use and pickup. It runs
// on the game thread, once a frame (Update); its statistics and log are in FreeHuntSession.Statistics.
public sealed partial class FreeHuntSession(GameScene scene)
{
    private const int SearchMs = 250;
    // A monster that swung at the player counts as an aggressor this long.
    private const long AggressorMs = 6000;
    private const long ClaimMs = 6000;
    private const long PreviewMs = 4000;
    private const float ArrivedSquared = 28f * 28f;
    private const float WaypointSquared = 12f * 12f;
    private const float ProgressStep = 36f;

    private readonly Dictionary<ulong, EntitySpawnInfo> _monsters = [];
    private readonly Dictionary<ulong, (ulong Attacker, long Until)> _claims = [];
    private readonly Dictionary<ulong, long> _rejected = [];
    private readonly Dictionary<ulong, long> _attackers = [];
    private readonly HashSet<ulong> _ownSummons = [];
    private readonly long[] _potionReady = new long[3];
    private readonly Queue<long> _recentDeaths = new();
    private bool _enabled;
    private int _field = -1;
    private ulong _target;
    private long _nextSearch;
    private Vector2 _origin;
    private long _showHuntUntil;
    private long _showLootUntil;
    private long _manualUntil;
    private long _progressAt;
    private float _bestDistance;
    private bool _returning;
    private Task<GroundRoute> _returnRoute;
    private IReadOnlyList<Vector2> _waypoints = [];
    private int _waypoint;
    private bool _returnForLoot;
    private bool _tradeOpen;
    private bool _pickingUp;
    private long _nextPickup;
    private Guid _drop;
    private long _combatSince;
    private long _nextPotion;
    private long _nextSpecial;
    private long _nextSkill;
    private int _skillCursor;
    private Task<InventoryOutcome> _inventory;
    private Task<ItemUseOutcome> _item;
    private Task<RestartOutcome> _restart;
    private long _lootCooldown;
    private bool _deathHandled;
    private int _lastLife;
    private int _lastMaxLife;
    private long _lastLifeAt;
    private int _recentHealthLoss;
    private HuntSafety _previousBagAction;
    private int _returnWeapon = -1;
    private uint _shieldBuff;
    private long _shieldUntil;
    private long _nextShieldSwap;
    private CrystalPetTargeting _petTargeting;
    private PartyInfo _party = PartyInfo.None;

    public FreeHuntSettings Settings { get; set; } = new();

    // The monster catalog, for the boss priority; set once the world catalog arrives.
    public WorldRenderCatalog Catalog { get; set; }

    public bool Enabled => _enabled;

    // The hunt may start here: alive, out of town and of the quest arena, no warp and no NPC talking.
    public bool Available { get; private set; }

    public bool ManuallyPaused => _enabled && Environment.TickCount64 < _manualUntil;

    public void SetEnabled(bool enabled, HuntStopReason reason = HuntStopReason.Manual)
    {
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        Log(enabled ? HuntLogKind.Started : HuntLogKind.Stopped, enabled ? HuntStopReason.Manual : reason);
    }

    // The start moves to where the player stands now.
    public void SetOrigin()
    {
        _origin = scene.PlayerPlanar;
        CancelReturn();
        _nextSearch = 0;
    }

    // Shows the hunting (blue) or loot (violet) circle on the ground for a few seconds, as a slider moves.
    public void PreviewRange(bool loot)
    {
        var until = Environment.TickCount64 + PreviewMs;
        if (loot)
        {
            _showLootUntil = until;
        }
        else
        {
            _showHuntUntil = until;
        }
    }

    // The player moved, attacked, cast or tapped: with PauseForManual the hunt waits ResumeMs after the last command.
    public void NoteManualInput()
    {
        if (!_enabled || !Settings.PauseForManual)
        {
            return;
        }

        _manualUntil = Environment.TickCount64 + Settings.ResumeMs;
        scene.CancelQuestSkill();
        if (_returning)
        {
            CancelReturn();
        }

        if (_pickingUp)
        {
            scene.CancelAutomaticPickup();
            _pickingUp = _returnForLoot = false;
        }
    }

    // A tap on Atacar or an enemy skill with no marked target: the nearest living wild monster within the hunt radius.
    public ulong NearestInRadius()
    {
        var radius = FreeHuntSettings.WorldRadius(Settings.Radius);
        var best = radius * radius;
        var nearest = 0UL;
        foreach (var id in _monsters.Keys)
        {
            if (scene.TrackQuestMonster(id, out var distance) && distance <= best)
            {
                (nearest, best) = (id, distance);
            }
        }

        return nearest;
    }

    // The frame's work: starts and ends the hunt by the frame's state and returns the monster to attack (0 for none).
    public ulong Update(in FreeHuntFrame frame)
    {
        var now = Environment.TickCount64;
        Available = frame.CanHunt;
        if (_enabled && _field >= 0 && _field != frame.FieldId)
        {
            SetEnabled(false, HuntStopReason.MapChange);
        }

        if (!_enabled && _returnWeapon >= 0 && frame.Owner is { Life: > 0 } owner && _inventory is not { IsCompleted: false })
        {
            if (owner.WeaponSet != _returnWeapon)
            {
                SwitchWeapon(owner, _returnWeapon);
            }

            _returnWeapon = -1;
        }

        if (_enabled && _field < 0)
        {
            Begin();
        }
        else if (!_enabled && _field >= 0)
        {
            scene.CancelQuestSkill();
            if (_returning || _pickingUp)
            {
                scene.CancelAutomaticPickup();
                CancelReturn();
                _pickingUp = false;
            }
        }

        _field = _enabled ? frame.FieldId : -1;
        UpdatePetTargeting(_enabled);
        var target = _enabled ? Hunt(now, frame) : 0;
        RefreshStatistics(now);
        UpdatePreview(now);
        if (!_enabled)
        {
            _target = 0;
        }

        return target;
    }

    private void Begin()
    {
        _origin = scene.PlayerPlanar;
        PreviewRange(loot: false);
        scene.StopAutoMove();
        _returning = false;
        _pickingUp = false;
        _returnForLoot = false;
        _waypoints = [];
        _rejected.Clear();
        _nextSearch = 0;
        _target = 0;
        _deathHandled = false;
        _lootCooldown = 0;
        _previousBagAction = HuntSafety.Continue;
        _combatSince = 0;
        scene.SetQuestHuntTarget(0);
    }

    private void UpdatePreview(long now)
    {
        var settings = Settings;
        scene.SetFreeHuntRings(_enabled ? _origin : scene.PlayerPlanar,
            now < _showHuntUntil ? FreeHuntSettings.WorldRadius(settings.Radius) : 0f, now < _showLootUntil ? FreeHuntSettings.WorldRadius(settings.LootRadius) : 0f);
    }

    private void UpdatePetTargeting(bool hunting)
    {
        var mode = hunting ? Settings.CompanionTarget : CrystalPetTargeting.Independent;
        if (mode != _petTargeting)
        {
            scene.SetCrystalPetTargeting(mode);
            _petTargeting = mode;
        }
    }

    private ulong Hunt(long now, in FreeHuntFrame frame)
    {
        var settings = Settings;
        var state = frame.Owner;
        if (state is null || !HandleSafety(now, state, settings))
        {
            return 0;
        }

        var potionBusy = UpdatePotions(now, state, settings);
        if (now < _manualUntil || frame.NpcOpen || _tradeOpen || scene.AutomaticSkillInProgress)
        {
            return 0;
        }

        if (_returnForLoot)
        {
            ReturnToOrigin(now, settings, true);
            _returnForLoot = _returning;
            if (_returnForLoot)
            {
                return 0;
            }
        }

        var target = ChooseTarget(now, settings, state, frame.Quest);
        _target = target;
        var forcedLoot = settings.LootAfterSeconds > 0 && _combatSince != 0 && now - _combatSince >= settings.LootAfterSeconds * 1000L;
        if (!potionBusy && (target == 0 || forcedLoot || _pickingUp) && UpdateLoot(now, state, settings))
        {
            if (forcedLoot)
            {
                _combatSince = 0;
            }

            return 0;
        }

        if (target == 0)
        {
            _combatSince = 0;
            ReturnToOrigin(now, settings, settings.ReturnToStart || settings.BreakCombatToReturn);
        }
        else
        {
            if (_returning)
            {
                CancelReturn();
            }

            if (_combatSince == 0)
            {
                _combatSince = now;
            }
        }

        if (!potionBusy)
        {
            UseHuntItem(now, state, settings, target != 0);
        }

        if (potionBusy ? _returnWeapon >= 0 : HandleShieldBuff(now, settings, state, target))
        {
            return 0;
        }

        if (target != 0 && TryHuntSkill(now, settings, state, target))
        {
            return 0;
        }

        return settings.BasicAttacks ? target : 0;
    }

    private ulong ChooseTarget(long now, FreeHuntSettings settings, OwnerState owner, QuestView quest)
    {
        var radius = FreeHuntSettings.WorldRadius(settings.Radius);
        var radiusSquared = radius * radius;
        var chase = radius * (1f + settings.PursuitPercent / 100f);
        var reach = owner.AttackRange * owner.AttackRange;
        if (settings.BreakCombatToReturn && Vector2.DistanceSquared(scene.PlayerPlanar, _origin) > radiusSquared)
        {
            return 0;
        }

        if (_target != 0 && scene.TrackQuestMonster(_target, out var distance)
            && scene.TryGetEntityPlanar(_target, out var lockedAt) && Vector2.DistanceSquared(lockedAt, _origin) <= chase * chase)
        {
            if (distance < _bestDistance - ProgressStep || distance <= MathF.Pow(owner.AttackRange + 14f, 2f))
            {
                _progressAt = now;
                _bestDistance = distance;
            }

            if (now - _progressAt >= settings.TargetTimeout * 1000L)
            {
                _rejected[_target] = now + settings.IgnoreSeconds * 1000L;
            }
            else if (settings.KeepTarget || now < _nextSearch)
            {
                return _target;
            }
        }

        if (now < _nextSearch)
        {
            return 0;
        }

        _nextSearch = now + SearchMs;
        var best = float.PositiveInfinity;
        var selected = 0UL;
        var bestPriority = int.MinValue;
        foreach (var (id, info) in _monsters)
        {
            if (_rejected.GetValueOrDefault(id) > now || !scene.TrackQuestMonster(id, out var here) || !scene.TryGetEntityPlanar(id, out var at))
            {
                continue;
            }

            var aggressive = _attackers.TryGetValue(id, out var last) && now - last <= AggressorMs;
            var inRadius = Vector2.DistanceSquared(_origin, at) <= radiusSquared;
            if ((!inRadius && !(aggressive && here <= reach)) || !settings.AcceptsMonster(info.Name, info.Level, aggressive))
            {
                continue;
            }

            if (settings.AvoidClaimed && !aggressive && _claims.TryGetValue(id, out var claim) && claim.Until > now
                && !(settings.IncludePartyTargets && _party.Contains(claim.Attacker)))
            {
                continue;
            }

            var priority = (aggressive ? 2 : 0)
                + (settings.PrioritizeQuest && quest is { TargetQuestCode: > 0 } && quest.Kills < quest.KillsNeeded && quest.TargetQuestCode == info.QuestCode ? 8 : 0)
                + (settings.PrioritizeBoss && Catalog?.GetMonster(info.TemplateId)?.Class == 1 ? 16 : 0)
                + (settings.PrioritizeRanged && aggressive && here > reach ? 4 : 0);
            if (priority > bestPriority || priority == bestPriority && here < best)
            {
                (selected, best, bestPriority) = (id, here, priority);
            }
        }

        if (selected != _target)
        {
            _bestDistance = best;
            _progressAt = now;
        }

        return selected;
    }

    private void ReturnToOrigin(long now, FreeHuntSettings settings, bool enabled)
    {
        var distance = Vector2.DistanceSquared(scene.PlayerPlanar, _origin);
        if (!enabled || distance <= ArrivedSquared)
        {
            if (_returning)
            {
                CancelReturn();
            }

            return;
        }

        if (!_returning || distance < _bestDistance - ProgressStep)
        {
            _bestDistance = distance;
            _progressAt = now;
        }
        else if (now - _progressAt >= settings.TargetTimeout * 1000L && settings.RelocateWhenBlocked)
        {
            _origin = scene.PlayerPlanar;
            CancelReturn();
            return;
        }

        if (!_returning)
        {
            _returnRoute = scene.PlanRouteAsync(_origin, mostCells: GroundRoute.Stretch);
            _returning = _returnRoute is not null;
            return;
        }

        if (_returnRoute is { IsCompleted: true } completed)
        {
            _returnRoute = null;
            if (completed.IsCompletedSuccessfully && completed.Result.Points.Count > 0)
            {
                _waypoints = completed.Result.Points;
                _waypoint = 0;
            }
            else
            {
                _ = completed.Exception;
                if (settings.RelocateWhenBlocked)
                {
                    _origin = scene.PlayerPlanar;
                }

                CancelReturn();
                return;
            }
        }

        if (_returnRoute is null && _waypoint < _waypoints.Count)
        {
            var point = _waypoints[_waypoint];
            if (Vector2.DistanceSquared(scene.PlayerPlanar, point) <= WaypointSquared)
            {
                _waypoint++;
            }
            else if (!scene.AutoMoving)
            {
                scene.WalkTo(point, arriveWithin: 10f);
            }
        }
        else if (_returnRoute is null)
        {
            _returnRoute = scene.PlanRouteAsync(_origin, mostCells: GroundRoute.Stretch);
        }
    }

    private void CancelReturn()
    {
        if (_returnRoute is { } route)
        {
            // A route still searching is let finish; its fault, if any, is observed then and dropped.
            _ = route.ContinueWith(static finished => _ = finished.Exception, TaskScheduler.Default);
            _returnRoute = null;
        }

        _waypoints = [];
        _waypoint = 0;
        _returning = false;
        scene.StopAutoMove();
    }

    private bool UpdateLoot(long now, OwnerState owner, FreeHuntSettings settings)
    {
        if (_pickingUp && scene.AutoMoving && now < _nextPickup)
        {
            return true;
        }

        if (_pickingUp && now >= _nextPickup)
        {
            scene.CancelAutomaticPickup();
        }

        if (_pickingUp && !scene.AutoMoving && settings.ReturnAfterLoot)
        {
            _returnForLoot = true;
            _pickingUp = false;
            return true;
        }

        _pickingUp = false;
        if (scene.HuntPickupFailed(_drop))
        {
            _lootCooldown = now + 60_000;
            _drop = Guid.Empty;
            if (ApplySafety(settings.FullBag, owner, HuntStopReason.PickupRefused))
            {
                return false;
            }
        }

        if (!settings.Loot || now < _nextPickup)
        {
            return false;
        }

        var goldOnly = owner.Overweight || now < _lootCooldown;
        if (!scene.PickUpForHunt(_origin, FreeHuntSettings.WorldRadius(settings.LootRadius), settings, owner, goldOnly, out _drop))
        {
            _nextPickup = now + 350;
            return false;
        }

        _pickingUp = true;
        _nextPickup = now + 4000;
        return true;
    }

    private bool HandleSafety(long now, OwnerState owner, FreeHuntSettings settings)
    {
        if (owner.Life <= 0)
        {
            if (!_deathHandled)
            {
                _deathHandled = true;
                _recentDeaths.Enqueue(now);
                while (_recentDeaths.TryPeek(out var time) && now - time > 600_000)
                {
                    _recentDeaths.Dequeue();
                }
            }

            if (settings.RestartInTown || settings.TownAfterDeaths && _recentDeaths.Count >= 3
                || settings.TownWhenLowScrolls && !HasResurrectionScroll(owner))
            {
                _restart ??= scene.RestartAsync(RestartChoice.Town);
                SetEnabled(false, HuntStopReason.TownAfterDeath);
            }

            return false;
        }

        _deathHandled = false;
        _restart = null;
        if (settings.StopOutsideParty && _party.Members.Count == 0)
        {
            SetEnabled(false, HuntStopReason.NoParty);
            return false;
        }

        if (owner.Overweight && _previousBagAction != settings.FullBag)
        {
            _previousBagAction = settings.FullBag;
            if (ApplySafety(settings.FullBag, owner, HuntStopReason.FullBag))
            {
                return false;
            }
        }
        else if (!owner.Overweight)
        {
            _previousBagAction = HuntSafety.Continue;
        }

        if (!HasPotion(owner, PotionResource.Life))
        {
            var critical = settings.CriticalHp > 0 && owner.Life * 100L <= owner.MaxLife * (long)settings.CriticalHp;
            var action = critical ? settings.CriticalAction : settings.UseCoreWhenOutOfPotions ? HuntSafety.Town : settings.NoHealthPotion;
            if (ApplySafety(action, owner, HuntStopReason.NoLifePotion))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasResurrectionScroll(OwnerState owner)
    {
        foreach (var item in owner.Items)
        {
            if (item.InBag && item.CatalogKey == Simulation.Progression.DeathPenalty.ResurrectionScroll && item.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    // Pause stops the hunt; Town also reads a return scroll from the bag first.
    private bool ApplySafety(HuntSafety action, OwnerState owner, HuntStopReason reason)
    {
        if (action == HuntSafety.Continue)
        {
            return false;
        }

        if (action == HuntSafety.Town && _item is not { IsCompleted: false })
        {
            foreach (var item in owner.Items)
            {
                if (item.InBag && UsableItemCatalog.TryGet(item.CatalogKey, out var rule) && rule.Action == UsableItemAction.ReturnToTown)
                {
                    _item = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, item.InstanceId));
                    break;
                }
            }
        }

        SetEnabled(false, reason);
        return true;
    }

    private bool UpdatePotions(long now, OwnerState owner, FreeHuntSettings settings)
    {
        if (_inventory is { IsCompleted: false } || _item is { IsCompleted: false })
        {
            return true;
        }

        if (_inventory is { IsFaulted: true })
        {
            _ = _inventory.Exception;
        }

        _inventory = null;
        if (now < _nextPotion)
        {
            return false;
        }

        if (owner.Life != _lastLife || owner.MaxLife != _lastMaxLife)
        {
            _recentHealthLoss = Math.Max(0, _lastLife - owner.Life);
            _lastLife = owner.Life;
            _lastMaxLife = owner.MaxLife;
            _lastLifeAt = now;
        }

        // After heavy damage the life threshold rises by the share just lost, up to 30 points.
        var lifeThreshold = settings.HP.Threshold;
        if (settings.AdaptivePotion && owner.MaxLife > 0 && now - _lastLifeAt <= 1500)
        {
            lifeThreshold = Math.Min(100, lifeThreshold + (int)Math.Min(30, _recentHealthLoss * 100L / owner.MaxLife));
        }

        OwnedItemView potion = null;
        var best = 101;
        var slot = -1;
        for (var index = 0; index < _potionReady.Length; index++)
        {
            var family = Family(index);
            var (rule, value, cap, threshold) = index switch
            {
                0 => (settings.HP, owner.Life, owner.MaxLife, lifeThreshold),
                1 => (settings.MP, owner.Mana, owner.MaxMana, settings.MP.Threshold),
                _ => (settings.RES, owner.Stamina, owner.MaxStamina, settings.RES.Threshold)
            };
            if (!rule.Enabled || cap <= 0 || now < _potionReady[index])
            {
                continue;
            }

            var percent = (int)(value * 100L / cap);
            var needed = (int)((threshold * (long)cap + 99) / 100) - value;
            var priority = index == 0 && percent <= settings.CriticalHp ? -1 : percent;
            if (priority < best && needed > 0 && HuntPotionSelection.Select(owner.Items, family, rule.Key, needed) is { } chosen)
            {
                (potion, best, slot) = (chosen, priority, index);
            }
        }

        if (potion is not null)
        {
            _potionReady[slot] = now + settings.SamePotionMs;
            _nextPotion = now + Math.Max(500, settings.AnyPotionMs);
            SendInventory(owner, InventoryAction.ConsumePotion, potion, 0, 1);
            return true;
        }

        if (settings.RefillBelt && now >= _nextSpecial)
        {
            for (var index = 0; index < PotionBelt.Boxes; index++)
            {
                if (OwnedItemView.InBox(owner.Items, index) is null
                    && HuntPotionSelection.Select(owner.Items, Family(index), "", int.MaxValue, bagOnly: true) is { } bagPotion)
                {
                    _nextSpecial = now + 1200;
                    SendInventory(owner, InventoryAction.BeltPut, bagPotion, index, 0);
                    return true;
                }
            }
        }

        return false;
    }

    // The belt box and potion rule of each resource: life, mana, then stamina.
    private static PotionResource Family(int index) => index switch { 0 => PotionResource.Life, 1 => PotionResource.Mana, _ => PotionResource.Stamina };

    private static bool HasPotion(OwnerState owner, PotionResource family) => HuntPotionSelection.Select(owner.Items, family, "", 1) is not null;

    private void SendInventory(OwnerState owner, InventoryAction action, OwnedItemView item, int destination, int count) =>
        _inventory = scene.ApplyInventoryAsync(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, action, item.InstanceId, destination, count, default));

    // A two-handed weapon with a shield-only buff in the rotation: the other weapon set is worn for the buff, then back.
    private bool HandleShieldBuff(long now, FreeHuntSettings settings, OwnerState owner, ulong target)
    {
        var self = scene.SelfEntityId;
        if (_returnWeapon >= 0)
        {
            if (_inventory is { IsCompleted: false })
            {
                return true;
            }

            if (owner.WeaponSet == _returnWeapon)
            {
                if (now >= _shieldUntil)
                {
                    _returnWeapon = -1;
                }

                return true;
            }

            var hasShield = OwnedItemView.InSlot(owner.Items, (int)EquipmentSlot.OffHand)?.Kind == ItemKind.Shield;
            if (now >= _shieldUntil || !hasShield || scene.HasQuestSkillEffect(self, _shieldBuff))
            {
                SwitchWeapon(owner, _returnWeapon);
                _returnWeapon = -1;
            }
            else
            {
                scene.TryCastQuestSkill(_shieldBuff, 0);
            }

            return true;
        }

        var held = OwnedItemView.InSlot(owner.Items, (int)EquipmentSlot.Weapon);
        if (!settings.SwapShieldForBuff || target == 0 || now < _nextShieldSwap || held is null || !ItemEquipmentMetadata.IsTwoHanded(held.CatalogKey))
        {
            return false;
        }

        foreach (var code in settings.Skills)
        {
            if (ClassSkillCatalog.TryGet(code, out var skill) && skill.AllowedWeaponsMask == 1u << (int)ItemKind.Shield
                && SkillRuleCatalog.TryGet(code, out var rule) && rule.Kind == SkillKind.SelfBuff && !scene.HasQuestSkillEffect(self, code))
            {
                _shieldBuff = code;
                _returnWeapon = owner.WeaponSet;
                _shieldUntil = now + 3500;
                _nextShieldSwap = now + 15000;
                SwitchWeapon(owner, 1 - owner.WeaponSet);
                return true;
            }
        }

        return false;
    }

    private void SwitchWeapon(OwnerState owner, int destination) =>
        _inventory = scene.ApplyInventoryAsync(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.SwitchWeaponSet,
            Guid.Empty, destination, 0, default));

    // A crystal pet up to SummonCount, else the chosen Force Orb, else a premium whose timer is not running.
    private void UseHuntItem(long now, OwnerState owner, FreeHuntSettings settings, bool fighting)
    {
        if (_item is { IsFaulted: true })
        {
            _ = _item.Exception;
        }

        if (now < _nextSpecial || _item is { IsCompleted: false })
        {
            return;
        }

        OwnedItemView force = null;
        OwnedItemView premium = null;
        var hasForce = scene.HasActiveHuntForce();
        var crystals = settings.UseCrystals ? scene.ActiveCrystalPets() : 0;
        foreach (var item in owner.Items)
        {
            if (!item.InBag || item.Count <= 0)
            {
                continue;
            }

            if (settings.UseCrystals && crystals < settings.SummonCount && (fighting || !settings.CrystalInCombat)
                && (settings.Crystal.Length == 0 || item.CatalogKey.Equals(settings.Crystal, StringComparison.OrdinalIgnoreCase))
                && MonsterCrystals.TryGet(item.CatalogKey, out var crystal) && crystal.Kind == CrystalKind.Pet)
            {
                _nextSpecial = now + 5000;
                _item = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, item.InstanceId));
                return;
            }

            if (settings.UseForce && (fighting || !settings.ForceInCombat) && !hasForce && ForceOrbs.TryGet(item.CatalogKey, out var orb)
                && owner.Level >= orb.MinimumLevel && (orb.MaximumLevel == 0 || owner.Level <= orb.MaximumLevel)
                && (force is null || item.CatalogKey == settings.ForceKey || force.CatalogKey != settings.ForceKey
                    && (settings.WeakForceFirst ? item.Code < force.Code : item.Code > force.Code)))
            {
                force = item;
            }

            if (premium is null && fighting && settings.UsePremiums && UsableItemCatalog.TryGet(item.CatalogKey, out var usable)
                && usable.Action == UsableItemAction.PremiumTimers && usable.Grants.Count > 0 && PremiumGrantAvailable(usable))
            {
                premium = item;
            }
        }

        if (force is not null)
        {
            _nextSpecial = now + 10000;
            _item = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, force.InstanceId));
        }
        else if (premium is not null)
        {
            _nextSpecial = now + 60_000;
            _item = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, premium.InstanceId));
        }
    }

    private bool PremiumGrantAvailable(UsableItemRule rule)
    {
        if (scene.PremiumTimers is not { } current)
        {
            return false;
        }

        foreach (var grant in rule.Grants)
        {
            foreach (var timer in current.Timers)
            {
                if (timer.Kind == grant.Kind && timer.RemainingSeconds > 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryHuntSkill(long now, FreeHuntSettings settings, OwnerState owner, ulong target)
    {
        if (now < _nextSkill || settings.Skills.Length == 0)
        {
            return false;
        }

        _nextSkill = now + 300;
        return settings.PreferArea && TryRotation(settings, owner, target, areaFirst: true) || TryRotation(settings, owner, target, areaFirst: false);
    }

    // The skills in their order from the one after the last cast: learned, affordable, above their mana floor and, for a
    // buff, about to end; an area skill first when AreaMinimum monsters stand within its radius.
    private bool TryRotation(FreeHuntSettings settings, OwnerState owner, ulong target, bool areaFirst)
    {
        var self = scene.SelfEntityId;
        for (var step = 0; step < settings.Skills.Length; step++)
        {
            var index = (_skillCursor + step) % settings.Skills.Length;
            var code = settings.Skills[index];
            var rank = owner.SkillRank(code);
            if (!SkillRuleCatalog.TryGet(code, out var rule) || rule.Passive is not null || areaFirst && rule.Kind != SkillKind.Area
                || rank <= 0 || !ClassSkillCatalog.TryGet(code, out var skill)
                || owner.Mana < skill.ManaForRank(rank) || owner.Stamina < skill.StaminaForRank(rank)
                || owner.Mana * 100L < settings.SkillManaFloor.GetValueOrDefault(code) * (long)owner.MaxMana
                || rule.Buff is not null && rule.Target != SkillTarget.Enemy && !scene.HuntBuffNeedsRefresh(self, code))
            {
                continue;
            }

            if (rule.Kind == SkillKind.Area && (areaFirst || settings.PreferArea))
            {
                if (settings.AreaCastRange > 0 && scene.TrackQuestMonster(target, out var distance) && distance > settings.AreaCastRange * settings.AreaCastRange)
                {
                    continue;
                }

                var radius = settings.AreaRadius > 0 ? settings.AreaRadius
                    : rule.Radius.Length > 0 ? rule.Radius[Math.Clamp(rank - 1, 0, rule.Radius.Length - 1)] : 0;
                if (areaFirst && radius > 0 && MonstersNear(rule.AreaCenter == SkillAreaCenter.Target ? target : 0, radius) < settings.AreaMinimum)
                {
                    continue;
                }
            }

            if (scene.TryCastQuestSkill(code, target))
            {
                _skillCursor = (index + 1) % settings.Skills.Length;
                return true;
            }
        }

        return false;
    }

    private int MonstersNear(ulong target, float radius)
    {
        var center = target != 0 && scene.TryGetEntityPlanar(target, out var at) ? at : scene.PlayerPlanar;
        var count = 0;
        foreach (var id in _monsters.Keys)
        {
            if (scene.TrackQuestMonster(id, out _) && scene.TryGetEntityPlanar(id, out var position) && Vector2.DistanceSquared(center, position) <= radius * radius)
            {
                count++;
            }
        }

        return count;
    }

    // Each presence update: the wild monsters in view, who else fights them, who swings at the player, its own summons,
    // an open trade and the statistics.
    public void Observe(PresenceUpdate update)
    {
        var now = Environment.TickCount64;
        var self = scene.SelfEntityId;
        if (update.Trade is { } trade)
        {
            _tradeOpen = trade.Open;
        }

        foreach (var spawn in update.Spawns)
        {
            if (spawn.Kind == EntityKind.Monster && spawn.OwnerId == 0)
            {
                _monsters[spawn.EntityId] = spawn;
            }
            else if (spawn.Kind == EntityKind.Monster && spawn.OwnerId == self)
            {
                _ownSummons.Add(spawn.EntityId);
            }
        }

        foreach (var hit in update.Events)
        {
            if (hit.Kind is not (CombatEventKind.Hit or CombatEventKind.SwingBegin))
            {
                continue;
            }

            if (hit.TargetId == self && _monsters.ContainsKey(hit.AttackerId))
            {
                _attackers[hit.AttackerId] = now;
            }
            else if (hit.AttackerId != self && !_monsters.ContainsKey(hit.AttackerId) && _monsters.ContainsKey(hit.TargetId))
            {
                _claims[hit.TargetId] = (hit.AttackerId, now + ClaimMs);
            }
        }

        foreach (var state in update.States)
        {
            if (state.Life <= 0)
            {
                _ownSummons.Remove(state.EntityId);
            }
        }

        foreach (var gone in update.Removed)
        {
            _monsters.Remove(gone);
            _rejected.Remove(gone);
            _claims.Remove(gone);
            _attackers.Remove(gone);
            _ownSummons.Remove(gone);
        }

        ObserveStatistics(update, now);
    }
}
