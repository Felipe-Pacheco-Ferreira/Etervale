using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Loot;

internal sealed partial class VulkanWorldSession
{
    private FreeHuntSettings huntSettings = new();
    private readonly Dictionary<ulong, EntitySpawnInfo> huntDescriptions = [];
    private readonly Dictionary<ulong, (ulong Attacker, long Until)> huntClaims = [];
    private readonly Dictionary<ulong, long> huntRejected = [];
    private readonly HashSet<ulong> ownSummons = [];
    private readonly long[] potionReady = new long[3];
    private readonly Queue<long> recentDeaths = new();
    private Vector2 huntOrigin;
    private int requestHuntOrigin;
    private long showHuntUntil;
    private long showLootUntil;
    private long manualUntil;
    private long huntProgressAt;
    private float huntBestDistance;
    private bool huntReturning;
    private Task<GroundRoute> huntReturnRoute;
    private IReadOnlyList<Vector2> huntWaypoints = [];
    private int huntWaypoint;
    private bool huntReturnForLoot;
    private bool huntTradeOpen;
    private bool huntPickingUp;
    private long nextPickup;
    private Guid huntDrop;
    private long combatSince;
    private long nextPotion;
    private long nextSpecial;
    private long nextSkill;
    private int skillCursor;
    private Task<InventoryOutcome> huntInventory;
    private Task<ItemUseOutcome> huntItem;
    private Task<RestartOutcome> huntRestart;
    private long huntLootCooldown;
    private bool huntDeathHandled;
    private int lastLife;
    private int lastMaxLife;
    private long lastLifeAt;
    private int recentHealthLoss;
    private HuntSafety previousBagAction;
    private int huntReturnWeapon = -1;
    private uint huntShieldBuff;
    private long huntShieldUntil;
    private long nextShieldSwap;

    public FreeHuntSettings HuntSettings
    {
        get => Volatile.Read(ref huntSettings);
        set => Volatile.Write(ref huntSettings, value);
    }

    public void SetHuntOrigin() => Interlocked.Exchange(ref requestHuntOrigin, 1);
    public void PreviewHuntRange(bool loot)
    {
        var until = Environment.TickCount64 + 4000;
        if (loot)
        {
            Interlocked.Exchange(ref showLootUntil, until);
        }
        else
        {
            Interlocked.Exchange(ref showHuntUntil, until);
        }
    }

    private void UpdateHuntPreview()
    {
        var now = Environment.TickCount64;
        var hunting = now < Interlocked.Read(ref showHuntUntil);
        var looting = now < Interlocked.Read(ref showLootUntil);
        var settings = HuntSettings;
        scene.SetFreeHuntRings(FreeHuntEnabled ? huntOrigin : scene.PlayerPlanar,
            hunting ? FreeHuntSettings.WorldRadius(settings.Radius) : 0f, looting ? FreeHuntSettings.WorldRadius(settings.LootRadius) : 0f);
    }
    public bool HuntManuallyPaused => FreeHuntEnabled && Environment.TickCount64 < Interlocked.Read(ref manualUntil);

    private void NoteManualHuntInput()
    {
        if (!FreeHuntEnabled || !HuntSettings.PauseForManual)
        {
            return;
        }

        Interlocked.Exchange(ref manualUntil, Environment.TickCount64 + HuntSettings.ResumeMs);
        scene.CancelQuestSkill();
        if (huntReturning)
        {
            CancelHuntReturn();
        }
        if (huntPickingUp)
        {
            scene.CancelAutomaticPickup();
            huntPickingUp = huntReturnForLoot = false;
        }
    }

    private void BeginHunt()
    {
        huntOrigin = scene.PlayerPlanar;
        PreviewHuntRange(loot: false);
        scene.StopAutoMove();
        huntReturning = false;
        huntPickingUp = false;
        huntReturnForLoot = false;
        huntWaypoints = [];
        huntRejected.Clear();
        nextFreeHuntSearch = 0;
        huntTarget = 0;
        huntDeathHandled = false;
        huntLootCooldown = 0;
        previousBagAction = HuntSafety.Continue;
        combatSince = 0;
        scene.SetQuestHuntTarget(0);
    }

    private CrystalPetTargeting crystalPetTargeting;

    private void UpdateCrystalPetTargeting(bool hunting)
    {
        var mode = hunting ? HuntSettings.CompanionTarget : CrystalPetTargeting.Independent;
        if (mode != crystalPetTargeting)
        {
            scene.SetCrystalPetTargeting(mode);
            crystalPetTargeting = mode;
        }
    }

    private ulong ChooseFreeHuntTarget(long now, FreeHuntSettings settings)
    {
        var radius = FreeHuntSettings.WorldRadius(settings.Radius);
        var radiusSquared = radius * radius;
        var chase = radius * (1f + settings.PursuitPercent / 100f);
        var current = scene.PlayerPlanar;
        var fromOrigin = Vector2.DistanceSquared(current, huntOrigin);
        if (settings.BreakCombatToReturn && fromOrigin > radiusSquared)
        {
            return 0;
        }

        if (huntTarget != 0 && scene.TrackQuestMonster(huntTarget, out var distance)
            && scene.TryGetEntityPlanar(huntTarget, out var lockedAt) && Vector2.DistanceSquared(lockedAt, huntOrigin) <= chase * chase)
        {
            if (distance < huntBestDistance - 36f || distance <= MathF.Pow(Owner.AttackRange + 14f, 2f))
            {
                huntProgressAt = now;
                huntBestDistance = distance;
            }

            if (now - huntProgressAt >= settings.TargetTimeout * 1000L)
            {
                huntRejected[huntTarget] = now + settings.IgnoreSeconds * 1000L;
            }
            else if (settings.KeepTarget || now < nextFreeHuntSearch)
            {
                return huntTarget;
            }
        }

        if (now < nextFreeHuntSearch)
        {
            return 0;
        }

        nextFreeHuntSearch = now + FreeHuntSearchMs;
        var best = float.PositiveInfinity;
        var selected = 0UL;
        var bestPriority = int.MinValue;
        var quest = Quest;
        foreach (var (id, info) in huntDescriptions)
        {
            if (huntRejected.GetValueOrDefault(id) > now || !scene.TrackQuestMonster(id, out var here)
                || !scene.TryGetEntityPlanar(id, out var at))
            {
                continue;
            }

            var aggressive = attackers.TryGetValue(id, out var last) && now - last <= AggressorMs;
            var inRadius = Vector2.DistanceSquared(huntOrigin, at) <= radiusSquared;
            if ((!inRadius && !(aggressive && here <= Owner.AttackRange * Owner.AttackRange))
                || !settings.AcceptsMonster(info.Name, info.Level, aggressive))
            {
                continue;
            }

            if (settings.AvoidClaimed && !aggressive && huntClaims.TryGetValue(id, out var claim) && claim.Until > now
                && !(settings.IncludePartyTargets && Party.Contains(claim.Attacker)))
            {
                continue;
            }

            var monster = Volatile.Read(ref _renderCatalog)?.GetMonster(info.TemplateId);
            var priority = (aggressive ? 2 : 0)
                + (settings.PrioritizeQuest && quest is { TargetQuestCode: > 0 } && quest.Kills < quest.KillsNeeded && quest.TargetQuestCode == info.QuestCode ? 8 : 0)
                + (settings.PrioritizeBoss && monster?.Class == 1 ? 16 : 0);
            if (settings.PrioritizeRanged && aggressive && here > Owner.AttackRange * Owner.AttackRange)
            {
                priority += 4;
            }

            if (priority > bestPriority || priority == bestPriority && here < best)
            {
                (selected, best, bestPriority) = (id, here, priority);
            }
        }

        if (selected != huntTarget)
        {
            huntBestDistance = best;
            huntProgressAt = now;
        }

        return selected;
    }

    private ulong UpdateFreeHunt(long now)
    {
        var settings = HuntSettings;
        var state = Owner;
        if (Interlocked.Exchange(ref requestHuntOrigin, 0) != 0)
        {
            huntOrigin = scene.PlayerPlanar;
            CancelHuntReturn();
            nextFreeHuntSearch = 0;
        }

        if (state is null || !HandleHuntSafety(now, state, settings))
        {
            return 0;
        }

        var potionBusy = UpdateHuntPotions(now, state, settings);
        if (now < Interlocked.Read(ref manualUntil) || NpcConversationOpen || huntTradeOpen)
        {
            return 0;
        }
        if (scene.AutomaticSkillInProgress)
        {
            return 0;
        }

        if (huntReturnForLoot)
        {
            TryReturnToHuntOrigin(now, settings, true);
            huntReturnForLoot = huntReturning;
            if (huntReturnForLoot)
            {
                return 0;
            }
        }

        var target = ChooseFreeHuntTarget(now, settings);
        var forcedLoot = settings.LootAfterSeconds > 0 && combatSince != 0 && now - combatSince >= settings.LootAfterSeconds * 1000L;
        if (!potionBusy && (target == 0 || forcedLoot || huntPickingUp) && UpdateHuntLoot(now, state, settings))
        {
            if (forcedLoot)
            {
                combatSince = 0;
            }

            return 0;
        }

        if (target == 0)
        {
            combatSince = 0;
            TryReturnToHuntOrigin(now, settings, settings.ReturnToStart || settings.BreakCombatToReturn);
        }
        else
        {
            if (huntReturning)
            {
                CancelHuntReturn();
            }

            if (combatSince == 0)
            {
                combatSince = now;
            }
        }

        if (!potionBusy)
        {
            TryHuntItem(now, state, settings, target != 0);
        }

        if (potionBusy ? huntReturnWeapon >= 0 : HandleShieldBuff(now, settings, state, target))
        {
            return 0;
        }

        if (target != 0 && TryHuntSkill(now, settings, target))
        {
            return 0;
        }

        return settings.BasicAttacks ? target : 0;
    }

    private void TryReturnToHuntOrigin(long now, FreeHuntSettings settings, bool enabled)
    {
        var distance = Vector2.DistanceSquared(scene.PlayerPlanar, huntOrigin);
        if (!enabled || distance <= 28f * 28f)
        {
            if (huntReturning)
            {
                CancelHuntReturn();
            }
            return;
        }

        if (!huntReturning || distance < huntBestDistance - 36f)
        {
            huntBestDistance = distance;
            huntProgressAt = now;
        }
        else if (now - huntProgressAt >= settings.TargetTimeout * 1000L && settings.RelocateWhenBlocked)
        {
            huntOrigin = scene.PlayerPlanar;
            CancelHuntReturn();
            return;
        }

        if (!huntReturning)
        {
            huntReturnRoute = scene.PlanRouteAsync(huntOrigin, mostCells: GroundRoute.Stretch);
            huntReturning = huntReturnRoute is not null;
            return;
        }

        if (huntReturnRoute is { IsCompleted: true } completed)
        {
            huntReturnRoute = null;
            if (completed.IsCompletedSuccessfully && completed.Result.Points.Count > 0)
            {
                huntWaypoints = completed.Result.Points;
                huntWaypoint = 0;
            }
            else
            {
                _ = completed.Exception;
                if (settings.RelocateWhenBlocked)
                {
                    huntOrigin = scene.PlayerPlanar;
                }
                CancelHuntReturn();
                return;
            }
        }

        if (huntReturnRoute is null && huntWaypoint < huntWaypoints.Count)
        {
            var point = huntWaypoints[huntWaypoint];
            if (Vector2.DistanceSquared(scene.PlayerPlanar, point) <= 12f * 12f)
            {
                huntWaypoint++;
            }
            else if (!scene.AutoMoving)
            {
                scene.WalkTo(point, arriveWithin: 10f);
            }
        }
        else if (huntReturnRoute is null)
        {
            huntReturnRoute = scene.PlanRouteAsync(huntOrigin, mostCells: GroundRoute.Stretch);
        }
    }

    private void CancelHuntReturn()
    {
        if (huntReturnRoute is { } route)
        {
            _ = ObserveHuntRouteAsync(route);
            huntReturnRoute = null;
        }
        huntWaypoints = [];
        huntWaypoint = 0;
        huntReturning = false;
        scene.StopAutoMove();
    }

    private static async Task ObserveHuntRouteAsync(Task route)
    {
        try
        {
            await route.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Android.Util.Log.Warn("Etervale.AutoHunt", error.Message);
        }
    }

    private bool UpdateHuntLoot(long now, OwnerState owner, FreeHuntSettings settings)
    {
        if (huntPickingUp && scene.AutoMoving && now < nextPickup)
        {
            return true;
        }

        if (huntPickingUp && now >= nextPickup)
        {
            scene.CancelAutomaticPickup();
        }

        if (huntPickingUp && !scene.AutoMoving && settings.ReturnAfterLoot)
        {
            huntReturnForLoot = true;
            huntPickingUp = false;
            return true;
        }

        huntPickingUp = false;
        if (scene.HuntPickupFailed(huntDrop))
        {
            huntLootCooldown = now + 60_000;
            huntDrop = Guid.Empty;
            if (ApplySafety(settings.FullBag, owner, "Coleta recusada"))
            {
                return false;
            }
        }

        if (!settings.Loot || now < nextPickup)
        {
            return false;
        }

        var goldOnly = owner.Overweight || now < huntLootCooldown;
        if (!scene.PickUpForHunt(huntOrigin, FreeHuntSettings.WorldRadius(settings.LootRadius), settings, owner, goldOnly, out huntDrop))
        {
            nextPickup = now + 350;
            return false;
        }

        huntPickingUp = true;
        nextPickup = now + 4000;
        return true;
    }

    private bool HandleHuntSafety(long now, OwnerState owner, FreeHuntSettings settings)
    {
        if (owner.Life <= 0)
        {
            if (!huntDeathHandled)
            {
                huntDeathHandled = true;
                recentDeaths.Enqueue(now);
                while (recentDeaths.TryPeek(out var time) && now - time > 600_000)
                {
                    recentDeaths.Dequeue();
                }
            }

            if (settings.RestartInTown || settings.TownAfterDeaths && recentDeaths.Count >= 3
                || settings.TownWhenLowScrolls && !HasResurrectionScroll(owner))
            {
                huntRestart ??= scene.RestartAsync(RestartChoice.Town);
                SetFreeHunt(false, "Volta à cidade após morrer");
            }

            return false;
        }

        huntDeathHandled = false;
        huntRestart = null;
        if (settings.StopOutsideParty && Party.Members.Count == 0)
        {
            SetFreeHunt(false, "Sem grupo");
            return false;
        }

        if (owner.Overweight && previousBagAction != settings.FullBag)
        {
            previousBagAction = settings.FullBag;
            if (ApplySafety(settings.FullBag, owner, "Bolsa cheia"))
            {
                return false;
            }
        }
        else if (!owner.Overweight)
        {
            previousBagAction = HuntSafety.Continue;
        }

        if (!HasPotion(owner, PotionResource.Life))
        {
            var critical = settings.CriticalHp > 0 && owner.Life * 100L <= owner.MaxLife * (long)settings.CriticalHp;
            var action = critical ? settings.CriticalAction
                : settings.UseCoreWhenOutOfPotions ? HuntSafety.Town : settings.NoHealthPotion;
            if (ApplySafety(action, owner, "Sem poção de vida"))
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
            if (item.InBag && item.CatalogKey == PristonTale.Simulation.Progression.DeathPenalty.ResurrectionScroll && item.Count > 0)
            {
                return true;
            }
        }
        return false;
    }

    private bool ApplySafety(HuntSafety action, OwnerState owner, string reason)
    {
        if (action == HuntSafety.Continue)
        {
            return false;
        }

        if (action == HuntSafety.Town && huntItem is not { IsCompleted: false })
        {
            foreach (var item in owner.Items)
            {
                if (item.InBag && UsableItemCatalog.TryGet(item.CatalogKey, out var rule) && rule.Action == UsableItemAction.ReturnToTown)
                {
                    huntItem = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, item.InstanceId));
                    break;
                }
            }
        }

        SetFreeHunt(false, reason);
        return true;
    }

    private bool UpdateHuntPotions(long now, OwnerState owner, FreeHuntSettings settings)
    {
        if (huntInventory is { IsCompleted: false } || huntItem is { IsCompleted: false })
        {
            return true;
        }

        if (huntInventory is { IsFaulted: true })
        {
            _ = huntInventory.Exception;
        }

        huntInventory = null;
        if (now < nextPotion)
        {
            return false;
        }

        if (owner.Life != lastLife || owner.MaxLife != lastMaxLife)
        {
            recentHealthLoss = Math.Max(0, lastLife - owner.Life);
            lastLife = owner.Life;
            lastMaxLife = owner.MaxLife;
            lastLifeAt = now;
        }

        var lifeThreshold = settings.HP.Threshold;
        if (settings.AdaptivePotion && owner.MaxLife > 0 && now - lastLifeAt <= 1500)
        {
            lifeThreshold = Math.Min(100, lifeThreshold + (int)Math.Min(30, recentHealthLoss * 100L / owner.MaxLife));
        }

        OwnedItemView potion = null;
        var best = 101;
        var slot = -1;
        for (var index = 0; index < potionReady.Length; index++)
        {
            var family = index switch { 0 => PotionResource.Life, 1 => PotionResource.Mana, _ => PotionResource.Stamina };
            var (rule, value, cap, threshold) = index switch
            {
                0 => (settings.HP, owner.Life, owner.MaxLife, lifeThreshold),
                1 => (settings.MP, owner.Mana, owner.MaxMana, settings.MP.Threshold),
                _ => (settings.RES, owner.Stamina, owner.MaxStamina, settings.RES.Threshold)
            };

            if (!rule.Enabled || cap <= 0 || now < potionReady[index])
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
            potionReady[slot] = now + settings.SamePotionMs;
            nextPotion = now + Math.Max(500, settings.AnyPotionMs);
            SendHuntInventory(owner, InventoryAction.ConsumePotion, potion, 0, 1);
            return true;
        }

        if (settings.RefillBelt && now >= nextSpecial)
        {
            for (var index = 0; index < PotionBelt.Boxes; index++)
            {
                var family = index switch { 0 => PotionResource.Life, 1 => PotionResource.Mana, _ => PotionResource.Stamina };
                if (OwnedItemView.InBox(owner.Items, index) is null
                    && HuntPotionSelection.Select(owner.Items, family, "", int.MaxValue, bagOnly: true) is { } bagPotion)
                {
                    nextSpecial = now + 1200;
                    SendHuntInventory(owner, InventoryAction.BeltPut, bagPotion, index, 0);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasPotion(OwnerState owner, PotionResource family) => HuntPotionSelection.Select(owner.Items, family, "", 1) is not null;

    private void SendHuntInventory(OwnerState owner, InventoryAction action, OwnedItemView item, int destination, int count) =>
        huntInventory = scene.ApplyInventoryAsync(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, action, item.InstanceId, destination, count, default));

    private bool HandleShieldBuff(long now, FreeHuntSettings settings, OwnerState owner, ulong target)
    {
        if (huntReturnWeapon >= 0)
        {
            if (huntInventory is { IsCompleted: false })
            {
                return true;
            }

            if (owner.WeaponSet == huntReturnWeapon)
            {
                if (now >= huntShieldUntil)
                {
                    huntReturnWeapon = -1;
                }
                return true;
            }

            var hasShield = OwnedItemView.InSlot(owner.Items, (int)EquipmentSlot.OffHand)?.Kind == ItemKind.Shield;
            if (now >= huntShieldUntil || !hasShield || scene.HasQuestSkillEffect(Entered.EntityId, huntShieldBuff))
            {
                SwitchHuntWeapon(owner, huntReturnWeapon);
                huntReturnWeapon = -1;
            }
            else
            {
                scene.TryCastQuestSkill(huntShieldBuff, 0);
            }

            return true;
        }

        var held = OwnedItemView.InSlot(owner.Items, (int)EquipmentSlot.Weapon);
        if (!settings.SwapShieldForBuff || target == 0 || now < nextShieldSwap || held is null || !ItemEquipmentMetadata.IsTwoHanded(held.CatalogKey))
        {
            return false;
        }

        foreach (var code in settings.Skills)
        {
            if (ClassSkillCatalog.TryGet(code, out var skill) && skill.AllowedWeaponsMask == 1u << (int)ItemKind.Shield
                && SkillRuleCatalog.TryGet(code, out var rule) && rule.Kind == SkillKind.SelfBuff
                && !scene.HasQuestSkillEffect(Entered.EntityId, code))
            {
                huntShieldBuff = code;
                huntReturnWeapon = owner.WeaponSet;
                huntShieldUntil = now + 3500;
                nextShieldSwap = now + 15000;
                SwitchHuntWeapon(owner, 1 - owner.WeaponSet);
                return true;
            }
        }

        return false;
    }

    private void SwitchHuntWeapon(OwnerState owner, int destination) =>
        huntInventory = scene.ApplyInventoryAsync(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.SwitchWeaponSet,
            Guid.Empty, destination, 0, default));

    private void TryHuntItem(long now, OwnerState owner, FreeHuntSettings settings, bool fighting)
    {
        if (huntItem is { IsFaulted: true })
        {
            _ = huntItem.Exception;
        }

        if (now < nextSpecial || huntItem is { IsCompleted: false })
        {
            return;
        }

        OwnedItemView force = null;
        OwnedItemView premium = null;
        var hasForce = scene.HasActiveHuntForce();
        var crystalCount = settings.UseCrystals ? scene.ActiveCrystalPets() : 0;
        foreach (var item in owner.Items)
        {
            if (!item.InBag || item.Count <= 0)
            {
                continue;
            }

            if (settings.UseCrystals && crystalCount < settings.SummonCount && (fighting || !settings.CrystalInCombat)
                && (settings.Crystal.Length == 0 || item.CatalogKey.Equals(settings.Crystal, StringComparison.OrdinalIgnoreCase))
                && MonsterCrystals.TryGet(item.CatalogKey, out var crystal) && crystal.Kind == CrystalKind.Pet)
            {
                nextSpecial = now + 5000;
                huntItem = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, item.InstanceId));
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
            nextSpecial = now + 10000;
            huntItem = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, force.InstanceId));
        }
        else if (premium is not null)
        {
            nextSpecial = now + 60_000;
            huntItem = scene.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, premium.InstanceId));
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

    private bool TryHuntSkill(long now, FreeHuntSettings settings, ulong target)
    {
        if (now < nextSkill || settings.Skills.Length == 0)
        {
            return false;
        }

        nextSkill = now + 300;
        if (settings.PreferArea && TryRotation(settings, target, areaFirst: true))
        {
            return true;
        }
        return TryRotation(settings, target, areaFirst: false);
    }

    private bool TryRotation(FreeHuntSettings settings, ulong target, bool areaFirst)
    {
        var owner = Owner;
        for (var step = 0; step < settings.Skills.Length; step++)
        {
            var index = (skillCursor + step) % settings.Skills.Length;
            var code = settings.Skills[index];
            var rank = owner.SkillRank(code);
            if (!SkillRuleCatalog.TryGet(code, out var rule) || rule.Passive is not null || areaFirst && rule.Kind != SkillKind.Area
                || rank <= 0 || !ClassSkillCatalog.TryGet(code, out var skill)
                || owner.Mana < skill.ManaForRank(rank) || owner.Stamina < skill.StaminaForRank(rank)
                || owner.Mana * 100L < settings.SkillManaFloor.GetValueOrDefault(code) * (long)owner.MaxMana
                || rule.Buff is not null && rule.Target != SkillTarget.Enemy && !scene.HuntBuffNeedsRefresh(Entered.EntityId, code))
            {
                continue;
            }

            if (rule.Kind == SkillKind.Area && (areaFirst || settings.PreferArea))
            {
                if (settings.AreaCastRange > 0 && scene.TrackQuestMonster(target, out var distance)
                    && distance > settings.AreaCastRange * settings.AreaCastRange)
                {
                    continue;
                }

                var radius = settings.AreaRadius > 0 ? settings.AreaRadius
                    : rule.Radius.Length > 0 ? rule.Radius[Math.Clamp(rank - 1, 0, rule.Radius.Length - 1)] : 0;
                if (areaFirst && radius > 0 && HuntMonstersNear(rule.AreaCenter == SkillAreaCenter.Target ? target : 0, radius) < settings.AreaMinimum)
                {
                    continue;
                }
            }

            if (scene.TryCastQuestSkill(code, target))
            {
                skillCursor = (index + 1) % settings.Skills.Length;
                return true;
            }
        }
        return false;
    }

    private int HuntMonstersNear(ulong target, float radius)
    {
        var center = target != 0 && scene.TryGetEntityPlanar(target, out var at) ? at : scene.PlayerPlanar;
        var count = 0;
        foreach (var (id, _) in huntDescriptions)
        {
            if (scene.TrackQuestMonster(id, out _) && scene.TryGetEntityPlanar(id, out var position)
                && Vector2.DistanceSquared(center, position) <= radius * radius)
            {
                count++;
            }
        }

        return count;
    }

    private void TrackFreeHuntPresence(PresenceUpdate update)
    {
        var now = Environment.TickCount64;
        if (update.Trade is { } trade)
        {
            huntTradeOpen = trade.Open;
        }
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Kind == EntityKind.Monster && spawn.OwnerId == 0)
            {
                huntDescriptions[spawn.EntityId] = spawn;
            }
            else if (spawn.Kind == EntityKind.Monster && spawn.OwnerId == Entered.EntityId)
            {
                ownSummons.Add(spawn.EntityId);
            }
        }

        foreach (var hit in update.Events)
        {
            if (hit.AttackerId != Entered.EntityId && !huntDescriptions.ContainsKey(hit.AttackerId) && huntDescriptions.ContainsKey(hit.TargetId)
                && hit.Kind is CombatEventKind.Hit or CombatEventKind.SwingBegin)
            {
                huntClaims[hit.TargetId] = (hit.AttackerId, now + 6000);
            }
        }

        foreach (var state in update.States)
        {
            if (state.Life <= 0)
            {
                ownSummons.Remove(state.EntityId);
            }
        }

        foreach (var gone in update.Removed)
        {
            huntDescriptions.Remove(gone);
            huntRejected.Remove(gone);
            huntClaims.Remove(gone);
            ownSummons.Remove(gone);
        }

        ObserveHuntStatistics(update, now);
    }
}
