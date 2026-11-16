using System.Diagnostics;
using System.Numerics;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

// The automatic quest (docs/android.md, "Automatic quest"): from what the server says of the Adventurer Journey, it walks
// the player to the tribe's guide and talks to it, readies the weapon and the belt the guide asks for, travels to the
// hunting field, hunts the quest's monsters and picks up their drops until the crystal is in the bag, and returns. It
// runs on the game thread, once a frame, and only moves the player as a tap would; the server decides everything.
internal sealed class QuestNavigator(VulkanWorldSession session)
{
    private const float WaypointReach = 40f;
    // How far a route's goal may move before the route is planned again.
    private const float GoalDrift = 60f;
    // How much nearer a stretch must lead to count as progress, and how many points before its end the next is planned.
    private const float StretchProgress = 48f;
    private const int AheadPoints = 2;
    // A route's point counts as passed this close: the player walks the legs the search checked and walked, never
    // cutting a corner into a planter or a lamp post.
    private const float PointReach = GameScene.RoutePointReach;
    // The guide is talked to from this close; the walk there follows a route, never a straight line.
    private const float TalkReach = 110f;
    private const float LootRadius = 260f;
    private const float StuckDistance = 8f;
    // A walk that has not moved this long searches again from where it stands (a crack in the floor finer than the
    // route's samples, a player in the way).
    private const double StuckSeconds = 1;
    private const double TalkCooldownSeconds = 6;
    // A guide that answered without moving the quest on is asked again only after this long.
    private const double RepeatTalkSeconds = 30;
    private const double InventoryCooldownSeconds = 2;
    private const int MostRouteFailures = 3;
    // With none of the quest's monsters in view, a monster this close is fought rather than walked past.
    private const float DefendRange = 140f;
    // Life fraction under which a belt potion is drunk, and the pause between two.
    private const float DrinkBelow = .45f;
    private const double DrinkCooldownSeconds = 3;
    // An attack that has not moved the player this long while its target is out of reach walks there along a route.
    private const double AttackStuckSeconds = 1.5;
    private const float AttackReach = 90f;
    // A monster this near a player stuck on its way to its target is in the way.
    private const float BlockRange = 70f;

    private enum Step { Idle, Guide, Prepare, Hunt, Collect, Done }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _enabled;
    private int _replan;
    // A search that found no way from here is tried again only after this long.
    private const double NoRouteRetrySeconds = 2;
    // Journeys longer than this ride the travel mount; a refused mount (combat) is asked again after MountRetrySeconds.
    private const float MountDistance = 500f;
    // How near the goal the rider gets off.
    private const float DismountDistance = 120f;
    private const double MountRetrySeconds = 10;
    private double _mountRetryAt;
    private string _ridingStatus;
    private string _ridingStatusOf;
    private double _retryAt;
    private string _status = "";
    private Step _step;
    private Task<VulkanProgression.Answer> _firstSkillTraining;
    private long _firstSkillTrainingRevision = -1;
    private double _firstSkillTrainingRetryAt;
    private Guid _firstSkillTrainingOperation;
    private double _nextQuestCast;
    private int _nextPracticeSkill;
    private long _firstSkillRewardRevision = -1;
    private Guid _firstSkillRewardOperation;
    private Task<QuestOutcome> _firstSkillReward;
    private double _firstSkillRewardRetryAt;

    private IReadOnlyList<XnaVector2> _route = [];
    private int _routeIndex;
    private int _routeField;
    private Vector2 _routeGoal;
    private Task<GroundRoute> _routing;
    // The next stretch, planned from where the one walked now ends (_stretchEnd, null when it reaches the goal); where the
    // search that leads the way started; whether the next search is a whole one.
    private Task<GroundRoute> _ahead;
    private XnaVector3? _stretchEnd;
    private Vector2 _searchFrom;
    private bool _searchWhole;
    private int _routeFailures;
    private Vector3 _lastPosition;
    private double _drankAt = double.NegativeInfinity;
    private Task _drinking = Task.CompletedTask;
    private static readonly RouteMarks NoMarks = new(0, []);
    private RouteMarks _marks = NoMarks;
    // A monster the attack could not reach in a straight line, walked to along a route instead.
    private ulong _chasing;
    private ulong _questAttackTarget;
    private (QuestCode Code, int State) _questAttackStep;
    private double _attackStillSince = double.NaN;
    private Vector3 _attackFrom;
    private double _lastProgressAt;

    private double _talkedAt = double.NegativeInfinity;
    private (QuestCode, int, long) _talkedAbout;
    private double _inventoryAt = double.NegativeInfinity;
    private Task _inventory = Task.CompletedTask;
    private int _huntPoint;
    private int? _crystalBaseline;
    private bool _collectAfterTalk;

    private static readonly WorldCatalog World = new();
    private static readonly Lazy<Dictionary<(int Target, int Field), Vector2[]>> QuestHunts = new(ReadQuestHunts);
    private (int Target, int From, int Level) _huntTravelKey;
    private int? _huntTravelField;

    private static Dictionary<(int Target, int Field), Vector2[]> ReadQuestHunts()
    {
        using var stream = Android.App.Application.Context.Assets!.Open("quest-hunts.csv");
        using var reader = new StreamReader(stream);
        var points = new Dictionary<(int Target, int Field), List<Vector2>>();
        while (reader.ReadLine() is { } line)
        {
            var row = line.Split(',');
            var key = (int.Parse(row[0]), int.Parse(row[1]));
            if (!points.TryGetValue(key, out var fieldPoints))
            {
                fieldPoints = [];
                points.Add(key, fieldPoints);
            }

            fieldPoints.Add(new Vector2(int.Parse(row[2]), int.Parse(row[3])));
        }

        return points.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static readonly Lazy<QuestNpcPlace[]> QuestNpcs = new(ReadQuestNpcs);
    private sealed record QuestNpcPlace(int Field, uint Template, Vector2 Position, NpcServiceKind Service, int Value);

    private static QuestNpcPlace[] ReadQuestNpcs()
    {
        using var stream = Android.App.Application.Context.Assets!.Open("quest-npcs.csv");
        using var reader = new StreamReader(stream);
        var placements = new List<QuestNpcPlace>();
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            placements.Add(new QuestNpcPlace(int.Parse(fields[0]), uint.Parse(fields[1]),
                new Vector2(GameUnits.FromFixed(int.Parse(fields[2])), GameUnits.FromFixed(int.Parse(fields[3]))),
                (NpcServiceKind)byte.Parse(fields[4]), int.Parse(fields[5])));
        }

        return placements.ToArray();
    }

    private int _skillMaster;
    private int _findBeginnerQuest;
    private int _wingExchange;
    private uint _wingResult;
    private static readonly Lazy<Dictionary<uint, ItemHunt>> ItemHunts = new(ReadItemHunts);
    private sealed record ItemHunt(HashSet<uint> Templates, Dictionary<int, List<Vector2>> Fields);
    private uint _collectingItem;
    private bool _pendingMorionPromotion;

    private static Dictionary<uint, ItemHunt> ReadItemHunts()
    {
        using var stream = Android.App.Application.Context.Assets!.Open("quest-items.csv");
        using var reader = new StreamReader(stream);
        var hunts = new Dictionary<uint, ItemHunt>();
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            var code = ItemCodes.Parse(fields[0]);
            if (!hunts.TryGetValue(code, out var hunt))
            {
                hunt = new ItemHunt([], []);
                hunts.Add(code, hunt);
            }

            hunt.Templates.Add(uint.Parse(fields[2]));
            var field = int.Parse(fields[1]);
            if (!hunt.Fields.TryGetValue(field, out var points))
            {
                points = [];
                hunt.Fields.Add(field, points);
            }

            points.Add(new Vector2(int.Parse(fields[3]), int.Parse(fields[4])));
        }

        return hunts;
    }

    public void RequestWingExchange()
    {
        Interlocked.Exchange(ref _wingExchange, 1);
        SetEnabled(true);
    }
    private (int From, int To, int Level) _worldExitKey;
    private RouteExit _worldExit;
    private long _itemAttemptRevision = -1;
    private Task<QuestOutcome> _itemAttempt;

    public void RequestBeginnerQuest()
    {
        Volatile.Write(ref _findBeginnerQuest, 1);
        SetEnabled(true);
    }

    public bool CanFindBeginnerQuest => HasBeginnerOffer(session.Quest);

    public void RequestSkillMaster()
    {
        Volatile.Write(ref _skillMaster, 1);
        SetEnabled(true);
    }

    public bool Enabled => Volatile.Read(ref _enabled) != 0;

    private void AttackForQuest(GameScene scene, ulong target)
    {
        if (session.Quest is { } quest)
        {
            _questAttackStep = (quest.Code, quest.State);
        }

        _questAttackTarget = target;
        scene.SetQuestHuntTarget(target);
    }

    private void StopQuestAttack(GameScene scene)
    {
        if (_questAttackTarget != 0 && scene.AttackTargetId == _questAttackTarget)
        {
            scene.SetQuestHuntTarget(0);
        }

        _questAttackTarget = 0;
        _chasing = 0;
    }

    // The route being walked, in field coordinates, for the minimap's dots; read on the UI thread.
    public RouteMarks Marks => Volatile.Read(ref _marks);

    // What the automatic quest is doing, for the HUD.
    public string Status => Volatile.Read(ref _status);

    // Called on the UI thread; the game thread drops the old route on its next update, so every start plans afresh from
    // where the player stands.
    public void SetEnabled(bool value)
    {
        Volatile.Write(ref _replan, 1);
        Volatile.Write(ref _marks, NoMarks);
        Volatile.Write(ref _enabled, value ? 1 : 0);
        if (!value)
        {
            Volatile.Write(ref _skillMaster, 0);
            Volatile.Write(ref _findBeginnerQuest, 0);
            Volatile.Write(ref _wingExchange, 0);
            _pendingMorionPromotion = false;
            _wingResult = 0;
            session.SetAutoHunt(false);
            session.KeepHunting = false;
            Show("Missão automática pausada");
        }
    }

    // Whether there is a step to take: the journey is running or waiting to start.
    public bool HasWork(QuestView quest) =>
        quest is not null && (IsFirstSkill(quest) || HasClassSkillOffer(quest) || SkillQuestCatalog.TryGet(quest.Code, out _) ||
        HasBeginnerOffer(quest) || HasHunt(quest) ||
        EliteHuntTarget(quest) != 0 || quest is { Code: QuestCode.RankUpQuestWeapon, State: 3 } ||
        PotionService(quest).Kind != 0 || MissingClassItem(quest) != 0 || FuryHuntField(quest) != 0 || NeedsHomeMaster(quest) ||
        ReportNpcCode(quest) != 0 || IsArenaQuest(quest) || EliteItemObjective(quest) is not null || Plan(quest) is not (Step.Idle or Step.Done));

    private static bool IsFirstSkill(QuestView quest) =>
        quest.Code == FirstSkillQuest.Code || quest.Code == QuestCode.None &&
        quest.Progress.Any(entry => string.Equals(entry.Description, "Avalon/FirstSkill.msg", StringComparison.OrdinalIgnoreCase));

    private static bool HasBeginnerOffer(QuestView quest) =>
        quest is { Code: QuestCode.None } &&
        quest.Progress.Any(entry => entry.Title == ElementaryQuests.InstructorObjective);

    private static bool HasClassSkillOffer(QuestView quest) =>
        quest is { Code: QuestCode.None } &&
        quest.Progress.Any(entry => entry.Description == ClassSkillQuests.Document);

    private static bool HasHunt(QuestView quest) => quest is { TargetQuestCode: > 0, KillsNeeded: > 0 } && quest.Kills < quest.KillsNeeded;

    // Answers a quest dialog by itself while it runs, so the conversation does not wait for a tap.
    public bool AnswersDialog(int choices) =>
        choices > 0 && Enabled && Volatile.Read(ref _skillMaster) == 0 &&
        (choices == 1 && (Volatile.Read(ref _wingExchange) != 0 || Volatile.Read(ref _findBeginnerQuest) != 0 || _pendingMorionPromotion) ||
         session.Quest is { } quest && (Plan(quest) is Step.Guide or Step.Prepare ||
             choices == 1 && (HasBeginnerOffer(quest) || HasClassSkillOffer(quest) || NeedsHomeMaster(quest) ||
                 ReportNpcCode(quest) != 0 || PotionService(quest).Kind != 0)));

    public void Update(GameScene scene, bool manualMovement)
    {
        if (!Enabled)
        {
            scene.CancelQuestSkill();
            StopQuestAttack(scene);
            // The travel mount belongs to the automatic quest: pausing it, finishing it or taking the joystick leaves it.
            if (scene.Mounted)
            {
                scene.Dismount();
            }

            return;
        }

        if (manualMovement)
        {
            scene.CancelQuestSkill();
            scene.StopAutoMove();
            SetEnabled(false);
            return;
        }

        if (_questAttackTarget != 0 && session.Quest is { } attackQuest &&
            _questAttackStep != (attackQuest.Code, attackQuest.State))
        {
            StopQuestAttack(scene);
        }

        if (Interlocked.Exchange(ref _replan, 0) != 0)
        {
            scene.StopAutoMove();
            ClearRoute();
            _routeGoal = default;
            _chasing = 0;
            _retryAt = 0;
        }

        if (scene.PlayerDead || scene.Warping || session.MapPose is not { } pose)
        {
            return;
        }

        if (Volatile.Read(ref _skillMaster) != 0)
        {
            GoToSkillMaster(scene, pose);
            return;
        }

        if (Volatile.Read(ref _wingExchange) != 0)
        {
            AdvanceWingExchange(scene, pose);
            return;
        }

        if (Volatile.Read(ref _findBeginnerQuest) != 0 || HasBeginnerOffer(session.Quest))
        {
            if (!HasBeginnerOffer(session.Quest))
            {
                Volatile.Write(ref _findBeginnerQuest, 0);
            }
            else
            {
                var home = AdventurerJourneyRoute.For(session.Character.Class).Guide.FieldCode;
                if (pose.FieldId != home)
                {
                    TravelWorld(scene, pose, home, "Indo à cidade buscar uma missão");
                }
                else
                {
                    TravelToService(scene, pose, NpcServiceKind.QuestGiver, ElementaryQuests.InstructorCode, "Indo à instrutora de missões");
                }

                return;
            }
        }

        if (session.Quest is not { } quest)
        {
            return;
        }

        DrinkIfHurt();

        if (_pendingMorionPromotion && quest.Code == QuestCode.None && session.Owner is { Tier: 0 })
        {
            GoToSkillMaster(scene, pose, quest);
            return;
        }

        if (quest is { Code: QuestCode.RankUpQuestWeapon, State: 3, WeaponKillsLeft: > 0 })
        {
            AdvanceQuestWeapon(scene, pose, quest);
            return;
        }

        var potion = PotionService(quest);
        if (potion.Kind != 0)
        {
            _pendingMorionPromotion |= HasQuestItem(QuestItems.RoyalJelly) || HasQuestItem(QuestItems.HairTonic);
            TravelToService(scene, pose, potion.Kind, potion.Value, "Indo preparar ou entregar a poção");
            return;
        }

        if (MissingClassItem(quest) is not 0 and var item)
        {
            CollectQuestItem(scene, pose, item);
            return;
        }

        if (pose.FieldId == QuestArena.FieldCode && IsArenaQuest(quest))
        {
            FightArenaOpponent(scene, pose, "Enfrentando o adversário da arena", "Aguardando a próxima etapa da arena");
            return;
        }

        if (FuryHuntField(quest) is not 0 and var furyField)
        {
            if (pose.FieldId != furyField)
            {
                TravelWorld(scene, pose, furyField, "Indo para a caçada da Fúria do Fantasma");
            }
            else
            {
                FightArenaOpponent(scene, pose, "Caçando para Fúria do Fantasma", "Procure monstros na área da missão");
            }

            return;
        }

        if (TryReportQuest(scene, pose, quest) || TryUseEliteItem(scene, pose, quest))
        {
            return;
        }

        if (NeedsHomeMaster(quest))
        {
            GoToSkillMaster(scene, pose, quest);
            return;
        }

        if (EliteHuntTarget(quest) is not 0 and var elitePrey)
        {
            HuntActiveQuest(scene, pose, quest with { TargetQuestCode = elitePrey }, directTarget: true);
            return;
        }

        if (quest.Code is not (QuestCode.None or QuestCode.AdventurerJourney) && HasHunt(quest))
        {
            HuntActiveQuest(scene, pose, quest);
            return;
        }

        if (HasClassSkillOffer(quest))
        {
            GoToSkillMaster(scene, pose, quest);
            return;
        }

        if (SkillQuestCatalog.TryGet(quest.Code, out var classSkill))
        {
            AdvanceClassSkill(scene, pose, quest, classSkill);
            return;
        }

        var firstSkillAvailable = IsFirstSkill(quest);
        if (firstSkillAvailable && session.Owner is { Skills.Count: 0 } owner)
        {
            var skills = ClassSkillCatalog.For(session.Character.Class);
            if (skills.Count > 0 &&
                SkillLearning.Check(skills, 0, owner.SkillRank, owner.Level, owner.Tier, owner.SkillPoints, owner.EliteSkillPoints) == SkillTrainingCheck.Ready &&
                owner.Money >= SkillLearning.Price(0, 0))
            {
                GoToSkillMaster(scene, pose, quest, skills[0].Code);
            }
            else
            {
                Show("Confira os requisitos para aprender a primeira habilidade");
            }

            return;
        }

        if (firstSkillAvailable)
        {
            session.SetAutoHunt(false);
            if (quest.State == FirstSkillQuest.ReadyState)
            {
                scene.CancelQuestSkill();
                scene.StopAutoMove();
                AdvanceSkillReward(quest);
            }
            else
            {
                Show($"Use habilidades: {quest.Kills}/{FirstSkillQuest.RequiredCasts}");
                if (_clock.Elapsed.TotalSeconds >= _nextQuestCast && session.Owner is { } caster)
                {
                    _nextQuestCast = _clock.Elapsed.TotalSeconds + 1;
                    var target = session.ClosestMonster(float.PositiveInfinity) ?? 0;
                    for (var offset = 0; offset < caster.Skills.Count; offset++)
                    {
                        var index = (_nextPracticeSkill + offset) % caster.Skills.Count;
                        if (scene.TryCastQuestSkill(caster.Skills[index].Code, target))
                        {
                            _nextPracticeSkill = (index + 1) % caster.Skills.Count;
                            ClearRoute();
                            return;
                        }
                    }
                }

                if (!scene.SelfCasting && session.Owner is { } learner &&
                    learner.Skills.Any(skill => SkillRuleCatalog.TryGet(skill.Code, out var rule) && rule.Target == SkillTarget.Enemy) &&
                    session.ClosestMonster(float.PositiveInfinity) is null)
                {
                    var practice = AdventurerJourneyRoute.For(session.Character.Class);
                    const string looking = "Procurando um monstro para praticar a habilidade";
                    if (pose.FieldId != practice.HuntField)
                    {
                        TravelWorld(scene, pose, practice.HuntField, looking);
                    }
                    else if (practice.HuntPoints.Count > 0)
                    {
                        var point = practice.HuntPoints[_huntPoint % practice.HuntPoints.Count];
                        if (Walk(scene, pose, practice.HuntField, new Vector2(point.X, point.Z), 0, looking))
                        {
                            _huntPoint++;
                            ClearRoute();
                        }
                    }
                }
            }

            return;
        }

        var route = AdventurerJourneyRoute.For(session.Character.Class);
        var step = Plan(quest);
        if (step != _step)
        {
            _step = step;
            ClearRoute();
            _huntPoint = 0;
        }

        switch (step)
        {
            case Step.Done:
                session.KeepHunting = false;
                Show(quest.Code == QuestCode.None ? "Nenhuma etapa automática em andamento" : "Confira a próxima etapa no diário de missões");
                SetEnabled(false);
                return;
            case Step.Idle:
                Show("Nenhuma missão automática");
                return;
            case Step.Prepare when ReadyEquipment(quest):
                return;
            case Step.Prepare:
            case Step.Guide:
                session.SetAutoHunt(false);
                session.KeepHunting = false;
                GoToGuide(scene, pose, route, quest);
                return;
            default:
                Hunt(scene, pose, route, quest, step == Step.Collect);
                return;
        }
    }

    private void AdvanceClassSkill(GameScene scene, VulkanWorldSession.WorldMapPose pose, QuestView quest, SkillQuestDefinition definition)
    {
        session.SetAutoHunt(false);
        if (quest.State == ClassSkillQuests.ReadyState)
        {
            scene.CancelQuestSkill();
            scene.StopAutoMove();
            AdvanceSkillReward(quest);
            return;
        }

        if (session.Owner is not { } owner || !SkillRuleCatalog.TryGet(definition.SkillCode, out var rule))
        {
            return;
        }

        if (owner.SkillRank(definition.SkillCode) == 0)
        {
            var skills = ClassSkillCatalog.For(session.Character.Class);
            for (var index = 0; index < skills.Count; index++)
            {
                var skill = skills[index];
                if (owner.SkillRank(skill.Code) != 0)
                {
                    continue;
                }

                if (SkillLearning.Check(skills, index, owner.SkillRank, owner.Level, owner.Tier, owner.SkillPoints, owner.EliteSkillPoints) ==
                    SkillTrainingCheck.Ready && owner.Money >= SkillLearning.Price(index, 0))
                {
                    GoToSkillMaster(scene, pose, quest, skill.Code);
                }
                else
                {
                    Show("Confira os requisitos para treinar a habilidade da missão");
                }

                return;
            }
        }

        var practice = AdventurerJourneyRoute.For(session.Character.Class);
        if (pose.FieldId == practice.Guide.FieldCode)
        {
            TravelWorld(scene, pose, practice.HuntField, "Indo praticar a habilidade");
            return;
        }

        if (rule.Resurrect is { } revive)
        {
            var ally = session.Party.Members.FirstOrDefault(member => member.EntityId != scene.SelfEntityId && member.Life <= 0);
            if (ally is null || !scene.TryGetEntityPlanar(ally.EntityId, out var position))
            {
                Show("Aguardando um aliado caído no grupo");
                return;
            }

            if (!Walk(scene, pose, pose.FieldId, new Vector2(position.X, -position.Y), 0,
                "Indo até o aliado caído", MathF.Max(1, revive.Radius * .75f)))
            {
                return;
            }
        }

        var needsUndead = definition.Objective is SkillQuestObjective.UndeadHits or SkillQuestObjective.ConvertUndead;
        var target = session.ClosestMonster(float.PositiveInfinity, questCode: needsUndead ? QuestMonsters.Skeleton : 0) ?? 0;
        if (target == 0 && rule.Target == SkillTarget.Enemy)
        {
            const string looking = "Procurando um monstro para praticar a habilidade";
            if (needsUndead)
            {
                SeekPracticeUndead(scene, pose, owner.Level);
            }
            else if (pose.FieldId != practice.HuntField)
            {
                TravelWorld(scene, pose, practice.HuntField, looking);
            }
            else if (practice.HuntPoints.Count > 0)
            {
                var point = practice.HuntPoints[_huntPoint % practice.HuntPoints.Count];
                if (Walk(scene, pose, practice.HuntField, new Vector2(point.X, point.Z), 0, looking))
                {
                    _huntPoint++;
                    ClearRoute();
                }
            }

            return;
        }

        Show($"Praticando a habilidade: {quest.Kills}/{definition.Required}");
        if (definition.Objective is SkillQuestObjective.MaintainEffect or SkillQuestObjective.SummonKills &&
            scene.HasQuestSkillEffect(scene.SelfEntityId, definition.SkillCode))
        {
            if (definition.Objective == SkillQuestObjective.SummonKills && scene.SummonAutomatic(definition.SkillCode) == false)
            {
                scene.ToggleSummonMode(definition.SkillCode);
            }

            return;
        }

        if (definition.Objective == SkillQuestObjective.MarkedKills && target != 0 && scene.HasQuestSkillEffect(target, definition.SkillCode))
        {
            if (scene.AttackTargetId != target)
            {
                AttackForQuest(scene, target);
            }

            return;
        }

        if (_clock.Elapsed.TotalSeconds >= _nextQuestCast)
        {
            _nextQuestCast = _clock.Elapsed.TotalSeconds + 1;
            if (scene.TryCastQuestSkill(definition.SkillCode, target))
            {
                ClearRoute();
            }
        }
    }

    private void SeekPracticeUndead(GameScene scene, VulkanWorldSession.WorldMapPose pose, int level)
    {
        const string looking = "Procurando esqueletos para praticar a habilidade";
        if (QuestHunts.Value.TryGetValue((QuestMonsters.Skeleton, pose.FieldId), out var points) && points.Length > 0)
        {
            if (Walk(scene, pose, pose.FieldId, points[_huntPoint % points.Length], 0, looking))
            {
                _huntPoint++;
                ClearRoute();
            }

            return;
        }

        foreach (var (prey, field) in QuestHunts.Value.Keys)
        {
            if (prey == QuestMonsters.Skeleton && FindWorldExit(pose.FieldId, field, level) is not null)
            {
                TravelWorld(scene, pose, field, looking);
                return;
            }
        }

        Show("Nenhuma rota acessível até os esqueletos");
    }

    private void AdvanceSkillReward(QuestView quest)
    {
        if (_firstSkillReward is { IsCompleted: false })
        {
            return;
        }

        if (_firstSkillRewardRevision != quest.Revision)
        {
            _firstSkillRewardRevision = quest.Revision;
            _firstSkillRewardOperation = Guid.NewGuid();
            _firstSkillRewardRetryAt = 0;
            _firstSkillReward = null;
        }

        if (_firstSkillReward is { IsCompletedSuccessfully: true } finished && finished.Result.Ok)
        {
            return;
        }

        _ = _firstSkillReward?.Exception;
        if (_clock.Elapsed.TotalSeconds < _firstSkillRewardRetryAt)
        {
            Show("Resgate pendente. Confira o espaço na bolsa.");
            return;
        }

        _firstSkillRewardRetryAt = _clock.Elapsed.TotalSeconds + RepeatTalkSeconds;
        var command = new QuestCommand(_firstSkillRewardOperation, 0, QuestAction.Proceed, quest.Code, quest.State, 0);
        _firstSkillReward = ClaimFirstSkillAsync(command);
        Show("Resgatando recompensa");
    }

    private async Task<QuestOutcome> ClaimFirstSkillAsync(QuestCommand command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await session.Play.RequestQuestAsync(command, timeout.Token);
    }

    private Step Plan(QuestView quest)
    {
        if (quest is null)
        {
            return Step.Idle;
        }

        if (quest.Code != AdventurerJourneyRoute.Code)
        {
            var welcome = quest.Progress.FirstOrDefault()?.Description;
            return welcome is AdventurerJourneyRoute.MeetSkadiDescription or AdventurerJourneyRoute.MeetJinDescription ? Step.Guide
                : quest.Finished.Count > 0 || quest.Progress.Count > 0 ? Step.Done : Step.Idle;
        }

        switch (quest.State)
        {
            case 0:
                return Step.Guide;
            case 1:
                return Step.Prepare;
        }

        if (quest.Kills < quest.KillsNeeded)
        {
            _crystalBaseline ??= CrystalCount();
            return Step.Hunt;
        }

        // The guide takes the crystal only when one more is in the bag than when the hunt began; the baseline is the
        // count this session first saw during the hunt, or unknown after a restart, when the guide says whether it fits.
        if (_collectAfterTalk && _crystalBaseline is { } gathered && CrystalCount() > gathered)
        {
            _collectAfterTalk = false;
        }

        if (_collectAfterTalk || _crystalBaseline is { } baseline && CrystalCount() <= baseline)
        {
            return Step.Collect;
        }

        return Step.Guide;
    }

    private void GoToGuide(GameScene scene, VulkanWorldSession.WorldMapPose pose, TribeRoute route, QuestView quest)
    {
        if (pose.FieldId != route.Guide.FieldCode)
        {
            Travel(scene, pose, route, route.Guide.FieldCode, $"Voltando para {GuideName(route)}");
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        var about = (quest.Code, quest.State, quest.Revision);
        if (about == _talkedAbout && now - _talkedAt < TalkCooldownSeconds)
        {
            Show($"Falando com {GuideName(route)}");
            return;
        }

        if (about == _talkedAbout && quest.Code == AdventurerJourneyRoute.Code && quest.State == 2)
        {
            // The guide kept the hunt open: it wants a crystal gathered since the hunt began.
            _collectAfterTalk = true;
            _crystalBaseline = CrystalCount();
            return;
        }

        if (about == _talkedAbout && now - _talkedAt < RepeatTalkSeconds)
        {
            Show(quest.State == 1 ? "Equipe uma arma e ponha uma poção no cinto" : $"Fale com {GuideName(route)}");
            return;
        }

        // Walk to the guide along a route (its place in view, else the route data's), then talk from beside it. The talk
        // walks straight, so it starts only where that walk is clear: near the guide but above it on a terrace, or behind
        // a wall, the route goes on (down the stairs, around the wall).
        var known = session.TryFindNpc(route.GuideName, out var guide, out var at);
        var target = known ? new Vector2(at.X, at.Z) : new Vector2(route.Guide.X, route.Guide.Z);
        var distance = Vector2.Distance(new Vector2(pose.Position.X, pose.Position.Z), target);
        var beside = known && distance <= TalkReach
            && (distance <= WaypointReach || scene.WalksStraightTo(new XnaVector2(target.X, -target.Y)));
        if (!beside)
        {
            Walk(scene, pose, route.Guide.FieldCode, target, 0, $"Indo até {GuideName(route)}", 0f, ride: true);
            return;
        }

        ClearRoute();
        if (!scene.AutoMoving && scene.ApproachNpc(guide))
        {
            _talkedAt = now;
            _talkedAbout = about;
        }

        Show($"Falando com {GuideName(route)}");
    }

    private void GoToSkillMaster(GameScene scene, VulkanWorldSession.WorldMapPose pose, QuestView quest = null, uint trainSkill = 0)
    {
        var hometown = AdventurerJourneyRoute.For(session.Character.Class).Guide.FieldCode;
        if (pose.FieldId != hometown)
        {
            TravelWorld(scene, pose, hometown, "Indo à cidade do Mestre de Habilidades");
            return;
        }

        if (!session.TryFindSkillMaster(pose.Position, out var master, out var position))
        {
            var placed = Array.Find(QuestNpcs.Value, place => place.Field == hometown && place.Service == NpcServiceKind.SkillMaster);
            if (placed is not null)
            {
                Walk(scene, pose, hometown, placed.Position, 0, "Indo ao Mestre de Habilidades", ride: true);
            }
            else
            {
                Show("Aproxime-se de um Mestre de Habilidades na cidade");
            }
            return;
        }

        var goal = new Vector2(position.X, position.Z);
        if (trainSkill != 0 && session.Owner is { } owner)
        {
            if (_firstSkillTraining is { IsCompleted: false })
            {
                return;
            }

            if (_firstSkillTrainingRevision == owner.ProgressionRevision && _firstSkillTraining is { IsCompletedSuccessfully: true } previous &&
                (previous.Result.Ok || _clock.Elapsed.TotalSeconds < _firstSkillTrainingRetryAt))
            {
                Show(previous.Result.Ok ? "Aguardando confirmação da habilidade" : previous.Result.Refusal);
                return;
            }

            if (Walk(scene, pose, pose.FieldId, goal, 0, "Indo aprender a primeira habilidade", TalkReach, ride: true) && !scene.AutoMoving)
            {
                if (_firstSkillTrainingRevision != owner.ProgressionRevision)
                {
                    _firstSkillTrainingRevision = owner.ProgressionRevision;
                    _firstSkillTrainingOperation = Guid.NewGuid();
                }

                _firstSkillTrainingRetryAt = _clock.Elapsed.TotalSeconds + RepeatTalkSeconds;
                var command = new ProgressionCommand(_firstSkillTrainingOperation, owner.ProgressionRevision, ProgressionAction.LearnSkill,
                    SkillCode: trainSkill, NpcEntityId: master);
                _firstSkillTraining = VulkanProgression.ApplyAsync(session.Play, command, "Não foi possível aprender a habilidade");
            }

            return;
        }

        if (quest is not null && WaitingForQuestDialog(quest))
        {
            return;
        }
        if (Walk(scene, pose, pose.FieldId, goal, 0, "Indo ao Mestre de Habilidades", TalkReach, ride: true)
            && !scene.AutoMoving && scene.ApproachNpc(master))
        {
            ClearRoute();
            if (quest is null)
            {
                SetEnabled(false);
            }
            else
            {
                MarkQuestDialog(quest);
            }
        }
    }

    private void TravelWorld(GameScene scene, VulkanWorldSession.WorldMapPose pose, int target, string status)
    {
        var key = (pose.FieldId, target, session.Owner?.Level ?? session.Character.Level);
        if (key != _worldExitKey)
        {
            _worldExitKey = key;
            _worldExit = FindWorldExit(key.Item1, key.Item2, key.Item3);
            ClearRoute();
        }

        if (_worldExit is not { } exit)
        {
            Show("Nenhuma passagem acessível para o destino");
            return;
        }

        Walk(scene, pose, exit.From, new Vector2(exit.Point.X, exit.Point.Z), exit.Seam ? exit.To : 0, status,
            reach: PointReach, ride: true);
    }

    private static RouteExit FindWorldExit(int from, int target, int level)
    {
        if (!World.TryGetFieldByCode(from, out var start))
        {
            return null;
        }

        var seen = new HashSet<int> { from };
        var pending = new Queue<(FieldDef Field, RouteExit First)>();
        pending.Enqueue((start, null));
        while (pending.TryDequeue(out var current))
        {
            if (current.Field.FieldCode == target)
            {
                return current.First;
            }

            foreach (var gate in current.Field.Gates)
            {
                Add(gate.DestinationFieldId, gate.X, gate.Z, seam: true);
            }

            foreach (var gate in current.Field.WarpGates)
            {
                if (level >= gate.LimitLevel)
                {
                    Add(gate.DestinationFieldId, gate.X, gate.Z, seam: false);
                }
            }

            void Add(string destination, int x, int z, bool seam)
            {
                if (World.TryGetField(destination, out var field) && seen.Add(field.FieldCode))
                {
                    var code = current.Field.FieldCode;
                    pending.Enqueue((field, current.First ?? new RouteExit(code, field.FieldCode, new RoutePoint(code, x, z), seam)));
                }
            }
        }

        return null;
    }

    private static EliteQuest EliteReport(QuestView quest) =>
        quest is { State: EliteQuests.Met } ? EliteQuests.Find(quest.Code) : null;

    private void AdvanceQuestWeapon(GameScene scene, VulkanWorldSession.WorldMapPose pose, QuestView quest)
    {
        if (session.Owner is not { } owner || session.Play is not { } play)
        {
            return;
        }

        var playerClass = session.Character.Class;
        var weaponCode = QuestClasses.QuestWeapon(playerClass);
        var weapon = owner.Items.FirstOrDefault(item => item.Code == weaponCode && item.Details?.UniqueItem == ItemDefinition.QuestUnique);
        if (weapon is null)
        {
            Show("Aguardando a arma de missão");
            return;
        }

        if (!weapon.Equipped)
        {
            if (_inventory.IsCompleted && _clock.Elapsed.TotalSeconds - _inventoryAt >= InventoryCooldownSeconds)
            {
                _inventoryAt = _clock.Elapsed.TotalSeconds;
                var command = new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.Equip,
                    weapon.InstanceId, 0, 0, EquipmentSlot.Weapon);
                _inventory = ApplyQuestEquipmentAsync(play, command);
            }

            Show("Equipando a arma de missão");
            return;
        }

        var hunt = QuestClasses.WeaponHunt(playerClass);
        var field = RankUpByQuestWeapon.StageField(hunt, quest.WeaponStage);
        if (field != 0)
        {
            if (pose.FieldId == field)
            {
                FightArenaOpponent(scene, pose, "Caçando com a arma de missão", "Procure monstros na área da missão");
            }
            else
            {
                TravelWorld(scene, pose, field, "Indo para a área da arma de missão");
            }

            return;
        }

        var target = RankUpByQuestWeapon.StagePrey(hunt, quest.WeaponStage);
        HuntActiveQuest(scene, pose, quest with { TargetQuestCode = target }, directTarget: true);
    }

    private static async Task ApplyQuestEquipmentAsync(PristonTale.Client.Net.IPlayChannel play, InventoryCommand command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await play.ApplyInventoryAsync(command, timeout.Token);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
        {
            Android.Util.Log.Warn("Etervale.Quest", error.Message);
        }
    }

    private (NpcServiceKind Kind, int Value) PotionService(QuestView quest)
    {
        if (quest?.Code is not (QuestCode.RankUpHoney or QuestCode.RankUpOil) || quest.State is not (1 or 2))
        {
            return default;
        }

        var honey = quest.Code == QuestCode.RankUpHoney;
        if (!HasQuestItem(QuestItems.GlassBottle) || HasQuestItem(honey ? QuestItems.RoyalJelly : QuestItems.HairTonic))
        {
            return (NpcServiceKind.QuestScript, (int)quest.Code);
        }

        return CountItem(honey ? QuestItems.Honey : QuestItems.Oil) >= (honey ? QuestItems.HoneyForJelly : QuestItems.OilForTonic)
            ? (NpcServiceKind.ItemAging, 0) : default;
    }

    private int CountItem(uint item) => session.Owner?.Items.Where(entry => entry.Code == item).Sum(entry => entry.Count) ?? 0;

    private uint MissingClassItem(QuestView quest) => (quest?.Code, quest?.State) switch
    {
        (QuestCode.RankUpHoney, 1 or 2) when CountItem(QuestItems.Honey) < QuestItems.HoneyForJelly => QuestItems.Honey,
        (QuestCode.RankUpOil, 1 or 2) when CountItem(QuestItems.Oil) < QuestItems.OilForTonic => QuestItems.Oil,
        (QuestCode.RankUpTrophies, 1) when !HasQuestItem(QuestItems.SilverWolverineTrophy) => QuestItems.SilverWolverineTrophy,
        (QuestCode.RankUpTrophies, 1) when !HasQuestItem(QuestItems.BronzeWolverineTrophy) => QuestItems.BronzeWolverineTrophy,
        (QuestCode.RankUpTrophies, 1) when !HasQuestItem(QuestItems.GoldenWolverineTrophy) => QuestItems.GoldenWolverineTrophy,
        (QuestCode.RankUpQuestWeapon, 1) when CountItem(QuestItems.Vamp) < RankUpByQuestWeapon.VampsNeeded => QuestItems.Vamp,
        (QuestCode.RankUpMasterTrial, 5) when !HasQuestItem(QuestItems.RoyalAmulet) => QuestItems.RoyalAmulet,
        (QuestCode.ElementaryG, 1 or 2) when !HasQuestItem(QuestItems.LostHammer) => QuestItems.LostHammer,
        (QuestCode.Level80Bonus, 2) when !HasQuestItem(QuestItems.BonusVampOne) => QuestItems.BonusVampOne,
        (QuestCode.Level80Bonus, 2) when !HasQuestItem(QuestItems.BonusVampTwo) => QuestItems.BonusVampTwo,
        (QuestCode.Level80Bonus, 2) when !HasQuestItem(QuestItems.BonusVampThree) => QuestItems.BonusVampThree,
        _ => 0
    };

    private void AdvanceWingExchange(GameScene scene, VulkanWorldSession.WorldMapPose pose)
    {
        var requirements = WingQuest.NextRequirements(HasQuestItem);
        if (requirements is null || _wingResult != 0 && HasQuestItem(_wingResult))
        {
            _wingResult = 0;
            SetEnabled(false);
            Show("Troca de asas concluída");
            return;
        }

        var (gold, items, result) = requirements.Value;
        foreach (var item in items)
        {
            if (!HasQuestItem(item))
            {
                CollectQuestItem(scene, pose, item);
                return;
            }
        }

        if (session.Owner is not { } owner || owner.Money < gold)
        {
            Show("Ouro insuficiente para trocar as asas");
            return;
        }

        TravelToService(scene, pose, NpcServiceKind.WarpGate, -1, "Indo trocar as asas");
        _wingResult = result;
    }

    private void TravelToService(GameScene scene, VulkanWorldSession.WorldMapPose pose, NpcServiceKind kind, int value, string status)
    {
        QuestNpcPlace destination = null;
        foreach (var place in QuestNpcs.Value)
        {
            if (place.Service == kind && (value < 0 || place.Value == value))
            {
                destination ??= place;
                if (place.Field == pose.FieldId)
                {
                    destination = place;
                    break;
                }
            }
        }

        if (destination is not null)
        {
            ApproachQuestNpc(scene, pose, destination, status);
        }
    }

    private void ApproachQuestNpc(GameScene scene, VulkanWorldSession.WorldMapPose pose, QuestNpcPlace destination, string status)
    {
        session.SetAutoHunt(false);
        if (destination.Field != pose.FieldId)
        {
            TravelWorld(scene, pose, destination.Field, status);
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (now - _talkedAt < RepeatTalkSeconds)
        {
            return;
        }

        var known = session.TryFindNpc(destination.Template, pose.Position, out var npc, out var at);
        var goal = known ? new Vector2(at.X, at.Z) : destination.Position;
        if (Walk(scene, pose, pose.FieldId, goal, 0, status, known ? TalkReach : WaypointReach, ride: true)
            && known && !scene.AutoMoving && scene.ApproachNpc(npc))
        {
            ClearRoute();
            _talkedAt = now;
        }
    }

    private void CollectQuestItem(GameScene scene, VulkanWorldSession.WorldMapPose pose, uint item)
    {
        session.SetAutoHunt(false);
        if (!ItemHunts.Value.TryGetValue(item, out var hunt))
        {
            Show($"Procure o item da missão ({ItemCodes.KeyOf(item)})");
            return;
        }

        if (_collectingItem != item)
        {
            _collectingItem = item;
            _huntPoint = 0;
            ClearRoute();
        }

        if (!hunt.Fields.TryGetValue(pose.FieldId, out var points))
        {
            var level = session.Owner?.Level ?? session.Character.Level;
            foreach (var field in hunt.Fields.Keys)
            {
                if (FindWorldExit(pose.FieldId, field, level) is not null)
                {
                    TravelWorld(scene, pose, field, "Indo coletar item de missão");
                    return;
                }
            }

            Show("Nenhuma passagem acessível para a coleta");
            return;
        }

        if (_chasing != 0)
        {
            Chase(scene, pose);
        }
        else if (scene.AttackTargetId != 0)
        {
            WatchAttack(scene, pose);
        }
        else if (scene.AutoMoving && _route.Count == 0 || scene.PickUpNear(LootRadius))
        {
            ClearRoute();
        }
        else if (session.ClosestMonster(float.PositiveInfinity, hunt.Templates) is { } target)
        {
            ClearRoute();
            AttackForQuest(scene, target);
        }
        else if (Walk(scene, pose, pose.FieldId, points[_huntPoint % points.Count], 0, "Buscando item de missão"))
        {
            _huntPoint++;
            ClearRoute();
        }
    }

    private bool NeedsHomeMaster(QuestView quest)
    {
        if (quest is null || HasHunt(quest))
        {
            return false;
        }

        return (quest.Code, quest.State) switch
        {
            (QuestCode.RankUpTrophies, 1) => HasQuestItem(QuestItems.SilverWolverineTrophy) &&
                HasQuestItem(QuestItems.BronzeWolverineTrophy) && HasQuestItem(QuestItems.GoldenWolverineTrophy),
            (QuestCode.RankUpHoney or QuestCode.RankUpOil, 3) => true,
            (QuestCode.RankUpQuestWeapon, 1) => (session.Owner?.Items.Where(item => item.Code == QuestItems.Vamp)
                .Sum(item => item.Count) ?? 0) >= RankUpByQuestWeapon.VampsNeeded,
            (QuestCode.RankUpQuestWeapon, 2 or 4 or 6) => true,
            (QuestCode.RankUpMasterTrial, 2 or 4 or 6 or 8) => true,
            (QuestCode.RankUpHestian, 2 or 4 or 6 or 10) => true,
            _ => false
        };
    }

    private bool WaitingForQuestDialog(QuestView quest) =>
        _talkedAbout == (quest.Code, quest.State, quest.Revision) && _clock.Elapsed.TotalSeconds - _talkedAt < RepeatTalkSeconds;

    private void MarkQuestDialog(QuestView quest)
    {
        _talkedAbout = (quest.Code, quest.State, quest.Revision);
        _talkedAt = _clock.Elapsed.TotalSeconds;
    }

    private int ReportNpcCode(QuestView quest)
    {
        if (quest is null || HasHunt(quest))
        {
            return 0;
        }

        if (EliteReport(quest) is { } elite)
        {
            return elite.Reporter;
        }

        var tempskron = AdventurerJourneyRoute.For(session.Character.Class).Guide.FieldCode == FieldCodes.Ricarten;
        return (quest.Code, quest.State) switch
        {
            (QuestCode.ElementaryA, 1) when HasQuestItem(QuestItems.Cake) => tempskron ? 1015 : 1016,
            (QuestCode.ElementaryA, 2) or (QuestCode.ElementaryB or QuestCode.ElementaryF, 3) => 1014,
            (QuestCode.ElementaryC, 1 or 2) when HasQuestItem(QuestItems.Candy) => tempskron ? 1018 : 1017,
            (QuestCode.ElementaryD, 3) => tempskron ? 1019 : 1020,
            (QuestCode.ElementaryE, 3) => 1021,
            (QuestCode.ElementaryG, 3) => 1022,
            (QuestCode.ElementaryG, _) when HasQuestItem(QuestItems.LostHammer) => 1022,
            (QuestCode.Level30, 2) => tempskron ? 1001 : 1002,
            (QuestCode.Level55, 2) => 1003,
            (QuestCode.Level55Morion, 2) => 1004,
            (QuestCode.Level70 or QuestCode.Level80, 2) or (QuestCode.Level85, 1 or 3) => 1005,
            (QuestCode.Level90, 2) => 1006,
            (QuestCode.Level80Bonus, 1 or 3) => 1011,
            (QuestCode.Level80Bonus, 2) when HasQuestItem(QuestItems.BonusVampOne) &&
                HasQuestItem(QuestItems.BonusVampTwo) && HasQuestItem(QuestItems.BonusVampThree) => 1011,
            (QuestCode.Level80Bonus, 4) when HasQuestItem(QuestItems.Recommendation) => 1010,
            (QuestCode.Level90Bonus, 1 or 3) => 1013,
            (QuestCode.FuryOfPhantom, _) when FuryHuntField(quest) == 0 => 1013,
            (QuestCode.RankUpHestian, 7 or 8 or 9) => RankUpByHestian.ElderGiverCode,
            _ => 0
        };
    }

    private bool HasQuestItem(uint code) =>
        session.Owner?.Items.Any(item => item.Code == code && item.Count > 0) == true;

    private bool TryReportQuest(GameScene scene, VulkanWorldSession.WorldMapPose pose, QuestView quest)
    {
        var giver = ReportNpcCode(quest);
        if (giver == 0)
        {
            return false;
        }

        session.SetAutoHunt(false);
        QuestNpcPlace destination = null;
        var instructorField = giver == ElementaryQuests.InstructorCode
            ? AdventurerJourneyRoute.For(session.Character.Class).Guide.FieldCode
            : (int?)null;
        foreach (var place in QuestNpcs.Value)
        {
            if (place.Service == NpcServiceKind.QuestGiver && place.Value == giver &&
                (!instructorField.HasValue || place.Field == instructorField.Value))
            {
                destination ??= place;
                if (place.Field == pose.FieldId)
                {
                    destination = place;
                    break;
                }
            }
        }

        if (destination is null)
        {
            Show("Procure o NPC que recebe a entrega da missão");
            return true;
        }

        if (destination.Field != pose.FieldId)
        {
            TravelWorld(scene, pose, destination.Field, "Indo entregar a missão");
            return true;
        }

        var known = session.TryFindNpc(destination.Template, pose.Position, out var npc, out var at);
        var goal = known ? new Vector2(at.X, at.Z) : destination.Position;
        var about = (quest.Code, quest.State, quest.Revision);
        var now = _clock.Elapsed.TotalSeconds;
        if (about == _talkedAbout && now - _talkedAt < RepeatTalkSeconds)
        {
            Show("Aguardando a entrega da missão");
            return true;
        }

        if (Walk(scene, pose, pose.FieldId, goal, 0, "Indo entregar a missão", known ? TalkReach : WaypointReach, ride: true)
            && known && !scene.AutoMoving && scene.ApproachNpc(npc))
        {
            ClearRoute();
            _talkedAbout = about;
            _talkedAt = now;
        }

        return true;
    }

    private static int FuryHuntField(QuestView quest) =>
        quest is { Code: QuestCode.FuryOfPhantom, KillsNeeded: > 0 } && quest.Kills < quest.KillsNeeded
            ? quest.State switch
            {
                2 => FuryOfPhantomQuest.HeartOfPerumField,
                7 => FuryOfPhantomQuest.GalluviaValleyField,
                _ => 0
            }
            : 0;

    private static bool IsArenaQuest(QuestView quest) =>
        quest?.Code is QuestCode.RankUpMasterTrial or QuestCode.RankUpHestian or QuestCode.FuryOfPhantom;

    private void FightArenaOpponent(GameScene scene, VulkanWorldSession.WorldMapPose pose, string attacking, string waiting)
    {
        session.SetAutoHunt(false);
        if (_chasing != 0)
        {
            Chase(scene, pose);
            return;
        }

        if (scene.AttackTargetId != 0)
        {
            WatchAttack(scene, pose);
            return;
        }

        // The server only publishes the owner's duel monsters in Quest Arena.
        if (session.ClosestMonster(float.PositiveInfinity) is { } opponent)
        {
            AttackForQuest(scene, opponent);
            Show(attacking);
        }
        else
        {
            Show(waiting);
        }
    }

    private int EliteHuntTarget(QuestView quest)
    {
        if (quest is not { State: EliteQuests.Running } || EliteQuests.Find(quest.Code) is not { } elite)
        {
            return 0;
        }

        if (elite.Objective == EliteObjective.Crystals)
        {
            for (var index = 0; index < Math.Min(elite.Crystals.Length, elite.Monsters.Length); index++)
            {
                if (!HasQuestItem(elite.Crystals[index]))
                {
                    return elite.Monsters[index];
                }
            }
        }
        else if (elite.Objective == EliteObjective.UseAndKill && elite.Monsters.Length > 0)
        {
            return elite.Monsters[0];
        }

        return 0;
    }

    private static EliteQuest EliteItemObjective(QuestView quest) => quest is { State: EliteQuests.Running } &&
        EliteQuests.Find(quest.Code) is { Objective: EliteObjective.Use or EliteObjective.UseAndKill } elite ? elite : null;

    private bool TryUseEliteItem(GameScene scene, VulkanWorldSession.WorldMapPose pose, QuestView quest)
    {
        if (_itemAttempt is { IsCompleted: false })
        {
            return true;
        }

        if (_itemAttempt is { } attempt)
        {
            _itemAttempt = null;
            if (!attempt.IsCompletedSuccessfully)
            {
                _ = attempt.Exception;
            }
        }

        if (EliteItemObjective(quest) is not { } elite || _itemAttemptRevision == quest.Revision
            || session.Owner?.Items.Any(item => item.InBag && item.Code == elite.AcceptItem) != true)
        {
            return false;
        }

        session.SetAutoHunt(false);
        if (pose.FieldId != elite.Area.Field)
        {
            TravelWorld(scene, pose, elite.Area.Field, "Indo para a área do item de missão");
            return true;
        }

        if (!elite.Area.Contains(pose.FieldId, pose.Position.X, pose.Position.Z))
        {
            var center = new Vector2((elite.Area.MinX + elite.Area.MaxX) * .5f, (elite.Area.MinZ + elite.Area.MaxZ) * .5f);
            Walk(scene, pose, pose.FieldId, center, 0, "Indo para a área do item de missão", reach: 4f);
            return true;
        }

        _itemAttemptRevision = quest.Revision;
        var command = new QuestCommand(Guid.NewGuid(), 0, QuestAction.UseItem, quest.Code, quest.State, 0);
        _itemAttempt = session.Play.RequestQuestAsync(command, CancellationToken.None);
        Show("Usando item de missão");
        return true;
    }

    private void HuntActiveQuest(GameScene scene, VulkanWorldSession.WorldMapPose pose, QuestView quest, bool directTarget = false)
    {
        if (EliteQuests.Find(quest.Code) is { Field: >= 0 } elite && elite.Field != pose.FieldId)
        {
            session.SetAutoHunt(false);
            TravelWorld(scene, pose, elite.Field, "Indo para o mapa da missão");
            return;
        }

        var target = quest.TargetQuestCode;
        if (!session.HasHuntTarget(target) && !QuestHunts.Value.ContainsKey((target, pose.FieldId))
            && EliteQuests.Find(quest.Code) is not { Field: >= 0 })
        {
            var level = session.Owner?.Level ?? session.Character.Level;
            var key = (target, pose.FieldId, level);
            if (_huntTravelKey != key)
            {
                _huntTravelKey = key;
                _huntTravelField = null;
                foreach (var (prey, field) in QuestHunts.Value.Keys)
                {
                    if (prey == target && FindWorldExit(pose.FieldId, field, level) is not null)
                    {
                        _huntTravelField = field;
                        break;
                    }
                }
            }

            if (_huntTravelField is { } destination)
            {
                session.SetAutoHunt(false);
                TravelWorld(scene, pose, destination, "Indo para a área da caçada");
                return;
            }
        }

        Hunt(scene, pose, null, quest, collecting: false, directTarget);
    }

    private void Hunt(GameScene scene, VulkanWorldSession.WorldMapPose pose, TribeRoute route, QuestView quest, bool collecting, bool directTarget = false)
    {
        if (route is not null && pose.FieldId != route.HuntField)
        {
            session.SetAutoHunt(false);
            Travel(scene, pose, route, route.HuntField, $"Indo caçar {quest.TargetName}");
            return;
        }

        session.KeepHunting = collecting;
        Show(collecting ? $"Procurando o Cristal de {quest.TargetName}" : $"Caçando {quest.TargetName}  {quest.Kills}/{quest.KillsNeeded}");
        // What swings at the player or stands in its way is fought first, before a chase, the drops or the quest's monster:
        // walking past it to the quest's monster left the player hit from behind until it died.
        if (session.FirstToClear() is not 0 and var first)
        {
            _chasing = 0;
            ClearRoute();
            session.SetAutoHunt(false);
            if (scene.AttackTargetId != first)
            {
                AttackForQuest(scene, first);
            }

            return;
        }

        if (_chasing != 0)
        {
            Chase(scene, pose);
            return;
        }

        // Between fights the drops in reach are gathered first (the crystal the guide wants is one of them), and a walk to
        // one (not a route) is not cut short by the next target.
        if (scene.AttackTargetId == 0 && (scene.AutoMoving && _route.Count == 0 || scene.PickUpNear(LootRadius)))
        {
            session.SetAutoHunt(false);
            ClearRoute();
            return;
        }

        // The quest's monster comes first; another monster is fought only when none of the quest's is in view.
        var questTargetInView = session.HasHuntTarget(quest.TargetQuestCode);
        session.SetAutoHunt(questTargetInView && !directTarget);
        if (directTarget && scene.AttackTargetId == 0 && session.ClosestMonster(float.PositiveInfinity, questCode: quest.TargetQuestCode) is { } prey)
        {
            ClearRoute();
            AttackForQuest(scene, prey);
            return;
        }
        if (!questTargetInView && scene.AttackTargetId == 0 && session.ClosestMonster(DefendRange) is { } other)
        {
            ClearRoute();
            AttackForQuest(scene, other);
            return;
        }

        if (scene.AttackTargetId != 0)
        {
            ClearRoute();
            WatchAttack(scene, pose);
            return;
        }

        _attackStillSince = double.NaN;
        if (questTargetInView)
        {
            ClearRoute();
            return;
        }

        if (route is null)
        {
            if (QuestHunts.Value.TryGetValue((quest.TargetQuestCode, pose.FieldId), out var points) &&
                Walk(scene, pose, pose.FieldId, points[_huntPoint % points.Length], 0, "Buscando monstros da missão"))
            {
                _huntPoint++;
                ClearRoute();
            }
            else if (points is null)
            {
                Show($"Procure {quest.TargetName} na área da missão");
            }
            return;
        }

        var point = route.HuntPoints[_huntPoint % route.HuntPoints.Count];
        if (Walk(scene, pose, route.HuntField, new Vector2(point.X, point.Z), 0, Status))
        {
            _huntPoint++;
            ClearRoute();
        }
    }

    // The attack walks straight at its target; when that leaves the player standing against something, the target is
    // reached along a route and the attack starts again from beside it.
    private void WatchAttack(GameScene scene, VulkanWorldSession.WorldMapPose pose)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (double.IsNaN(_attackStillSince) || Vector3.Distance(pose.Position, _attackFrom) > StuckDistance)
        {
            _attackStillSince = now;
            _attackFrom = pose.Position;
            return;
        }

        var target = scene.AttackTargetId;
        if (now - _attackStillSince > AttackStuckSeconds && session.ClosestMonster(BlockRange) is { } near && near != target)
        {
            // Stuck against another monster: it is cleared out of the way first.
            session.ClearFirst(near);
            _attackStillSince = double.NaN;
            return;
        }

        if (now - _attackStillSince > AttackStuckSeconds && scene.TryGetEntityPlanar(target, out var at)
            && XnaVector2.Distance(at, scene.PlayerPlanar) > AttackReach)
        {
            _chasing = target;
            _attackStillSince = double.NaN;
            session.SetAutoHunt(false);
            scene.SetQuestHuntTarget(0);
            ClearRoute();
        }
    }

    private void Chase(GameScene scene, VulkanWorldSession.WorldMapPose pose)
    {
        if (!scene.TryGetEntityPlanar(_chasing, out var at) || !scene.TrackQuestMonster(_chasing, out _))
        {
            _chasing = 0;
            ClearRoute();
            return;
        }

        if (Walk(scene, pose, pose.FieldId, new Vector2(at.X, -at.Y), 0, Status, AttackReach))
        {
            ClearRoute();
            AttackForQuest(scene, _chasing);
            _chasing = 0;
        }
    }

    // Walks towards another field through the exits of the route; false while it is under way.
    private void Travel(GameScene scene, VulkanWorldSession.WorldMapPose pose, TribeRoute route, int destination, string status)
    {
        var exit = route.ExitTowards(pose.FieldId, destination);
        if (exit is null)
        {
            Show($"Vá para {(destination == route.Guide.FieldCode ? GuideName(route) : "a área de caça")} para continuar");
            return;
        }

        Walk(scene, pose, exit.From, new Vector2(exit.Point.X, exit.Point.Z), exit.Seam ? exit.To : 0, status, ride: true);
    }

    // Follows a route to a point of a field (its own planar x, z); true once there. ride: a journey (to the guide, to
    // another field) that takes the travel mount when long; the hunt's short walks between fights stay on foot.
    private bool Walk(GameScene scene, VulkanWorldSession.WorldMapPose pose, int field, Vector2 goal, int joinWith, string status,
        float reach = WaypointReach, bool ride = false)
    {
        var here = new Vector2(pose.Position.X, pose.Position.Z);
        if (Vector2.Distance(here, goal) <= reach)
        {
            if (scene.Mounted)
            {
                scene.Dismount();
            }

            Show(status);
            return true;
        }

        var now = _clock.Elapsed.TotalSeconds;
        var left = Vector2.Distance(here, goal);
        if (ride && left > DismountDistance)
        {
            Ride(scene, left, now);
        }
        else if (scene.Mounted)
        {
            // Close to the goal, or a walk on foot (the hunt's): off the mount, as a player would get off at the door.
            scene.Dismount();
        }

        Show(scene.Mounted ? RidingStatus(status) : status);
        // Routes are planned for the rider's own body, mounted or not: a route for the hound's width failed in narrow streets
        // and left the mount on and off; the hound may brush a wall instead.
        // A goal that moves (a monster chased) is planned for again only once it moved GoalDrift: replanning at every step
        // of it dropped each search before it ended, and the player stood still among the monsters.
        var renderGoal = new XnaVector2(goal.X, -goal.Y);
        if (_routeField != field || Vector2.Distance(_routeGoal, goal) > GoalDrift || (_route.Count == 0 && _routing is null && now >= _retryAt))
        {
            _routeField = field;
            _routeGoal = goal;
            _route = [];
            _ahead = null;
            _searchFrom = here;
            // A long way is led a stretch at a time (GroundRoute.Stretch), so the walk starts at once; a whole search runs
            // only where a stretch could not come nearer (a dead end it cannot see out of).
            _routing = scene.PlanRouteAsync(renderGoal, session.NpcPoints(renderGoal, TalkReach), GameScene.WalkingBodyRadius,
                _searchWhole ? GroundRoute.WholeSearch : GroundRoute.Stretch);
        }

        if (_routing is { IsCompleted: true } search)
        {
            var routing = search.IsCompletedSuccessfully ? search.Result : null;
            var whole = _searchWhole;
            _routing = null;
            _searchWhole = false;
            LogRoute(routing, search.Exception);
            if (routing is not { Points.Count: > 0 } || !whole && !routing.ReachesGoal && Distance(routing.End, _searchFrom) < StretchProgress)
            {
                if (!whole && routing is not null)
                {
                    // The stretch found no way nearer: the whole search, at once.
                    _searchWhole = true;
                    _retryAt = 0;
                    Show("Calculando a rota…");
                    return false;
                }

                // No walkable way from here: never walk blind at the goal, which runs into the walls between.
                _retryAt = now + NoRouteRetrySeconds;
                Show("Sem caminho daqui; tentando de novo");
                return false;
            }

            Follow(routing, field);
        }

        if (_route.Count == 0)
        {
            if (_routing is not null)
            {
                Show("Calculando a rota…");
            }

            return false;
        }

        var player = scene.PlayerPlanar;
        while (_routeIndex < _route.Count && XnaVector2.Distance(player, _route[_routeIndex]) <= PointReach)
        {
            _routeIndex++;
        }

        // Near the end of a stretch the next one is planned from where it ends, so the walk goes on without stopping.
        if (_stretchEnd is { } end && _ahead is null && _route.Count - _routeIndex <= AheadPoints)
        {
            _ahead = scene.PlanRouteAsync(renderGoal, session.NpcPoints(renderGoal, TalkReach), GameScene.WalkingBodyRadius, GroundRoute.Stretch, end);
        }

        if (_routeIndex >= _route.Count)
        {
            if (_ahead is { IsCompleted: true } next)
            {
                _ahead = null;
                var nextRoute = next.IsCompletedSuccessfully ? next.Result : null;
                LogRoute(nextRoute, next.Exception);
                if (nextRoute is { Points.Count: > 0 } && (nextRoute.ReachesGoal || Distance(nextRoute.End, here) >= StretchProgress))
                {
                    Follow(nextRoute, field);
                    return false;
                }
            }
            else if (_ahead is not null)
            {
                Show("Calculando a rota…");
                return false;
            }

            // The end of a route with nothing planned beyond it: search again from here.
            _route = [];
            _ahead = null;
            return false;
        }

        scene.WalkTo(_route[_routeIndex], PointReach);
        if (Vector3.Distance(pose.Position, _lastPosition) > StuckDistance)
        {
            _lastPosition = pose.Position;
            _lastProgressAt = now;
        }
        else if (now - _lastProgressAt > StuckSeconds)
        {
            // Something the search could not see (a monster, another player) is in the way: search again from here.
            _routeFailures++;
            _lastProgressAt = now;
            _route = [];
            _ahead = null;
            if (_routeFailures > MostRouteFailures)
            {
                _routeFailures = 0;
                _routeGoal = default;
            }
        }

        return false;
    }

    // Walks a route found: from its first point, its end kept when it stops short of the goal (a stretch) so the next
    // stretch is planned from there.
    private void Follow(GroundRoute routing, int field)
    {
        _route = routing.Points;
        _routeIndex = 0;
        _stretchEnd = routing.ReachesGoal ? null : routing.End;
        _lastProgressAt = _clock.Elapsed.TotalSeconds;
        Volatile.Write(ref _marks, new RouteMarks(field, _route.Select(point => new Vector2(point.X, -point.Y)).ToArray()));
    }

    // The planar distance between render-plane feet and a point of a field (its own planar x, z).
    private static float Distance(XnaVector3 feet, Vector2 point) => Vector2.Distance(new Vector2(feet.X, -feet.Z), point);

    // Mounts the travel mount for a long walk; the mount itself refuses in combat, and attacking leaves it.
    private void Ride(GameScene scene, float distance, double now)
    {
        if (scene.Mounted || distance < MountDistance || now < _mountRetryAt)
        {
            return;
        }

        // A mount the server refuses falls back after a moment; asking again only after a while keeps the moments ridden
        // too fast within the server's movement tolerance.
        scene.TryMount();
        _mountRetryAt = now + MountRetrySeconds;
    }

    // The status with "Montado!" before it, made again only when the status changes.
    private string RidingStatus(string status)
    {
        if (status != _ridingStatusOf)
        {
            _ridingStatusOf = status;
            _ridingStatus = $"Montado! {status}";
        }

        return _ridingStatus;
    }

    // One logcat line per planned route (tag Etervale.Route), to measure the search on the device.
    private static void LogRoute(GroundRoute route, AggregateException failure)
    {
        if (route is null)
        {
            Android.Util.Log.Warn(RouteLogTag, $"route search failed: {failure?.GetBaseException().Message}");
            return;
        }

        var timing = route.Timing;
        Android.Util.Log.Info(RouteLogTag, $"route {(route.ReachesGoal ? "reaches" : "short")}, {route.Points.Count} points: search "
            + $"{timing.SearchMilliseconds:0} ms, walks {timing.WalkMilliseconds:0} ms, {timing.Searches} searches, {timing.Cells} cells, "
            + $"{timing.LegsWalked} legs walked, {timing.LegsReused} reused");
    }

    private const string RouteLogTag = "Etervale.Route";

    // Drinks the first belt potion when life runs low.
    private void DrinkIfHurt()
    {
        if (session.Owner is not { Life: > 0 } owner || owner.MaxLife <= 0 || owner.Life > owner.MaxLife * DrinkBelow
            || session.Play is not { } play || !_drinking.IsCompleted || _clock.Elapsed.TotalSeconds - _drankAt < DrinkCooldownSeconds)
        {
            return;
        }

        var potion = (owner.Items ?? []).FirstOrDefault(item => item.BeltBox >= 0 && item.Kind == ItemKind.Potion);
        if (potion is null)
        {
            return;
        }

        _drankAt = _clock.Elapsed.TotalSeconds;
        // A potion is drunk through the inventory's potion command, as the desktop belt does; the item-use command is for
        // scrolls, crystals and the like, and refuses a potion.
        var command = new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.ConsumePotion, potion.InstanceId, 0, 1, default);
        _drinking = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await play.ApplyInventoryAsync(command, timeout.Token);
        });
    }

    private void ClearRoute()
    {
        _route = [];
        _routing = null;
        _ahead = null;
        _stretchEnd = null;
        _searchWhole = false;
        _routeField = 0;
        _routeFailures = 0;
        Volatile.Write(ref _marks, NoMarks);
    }

    // Puts a weapon in the hand and a potion in the belt when the guide asks for them; true while it does.
    private bool ReadyEquipment(QuestView quest)
    {
        if (session.Owner is not { } owner || session.Play is not { } play)
        {
            return false;
        }

        if (!_inventory.IsCompleted || _clock.Elapsed.TotalSeconds - _inventoryAt < InventoryCooldownSeconds)
        {
            Show("Preparando o equipamento");
            return true;
        }

        var items = owner.Items ?? [];
        OwnedItemView move = null;
        var action = InventoryAction.Equip;
        var destination = 0;
        var slot = EquipmentSlot.Weapon;
        if (!items.Any(item => item.Equipped && ItemEquipmentMetadata.SlotOf(item.Kind) == EquipmentSlot.Weapon))
        {
            move = items.FirstOrDefault(item => item.InBag && ItemEquipmentMetadata.SlotOf(item.Kind) == EquipmentSlot.Weapon);
            if (move is not null)
            {
                slot = ItemEquipmentMetadata.DefaultSlot(move.Kind, EquipmentSlot.Weapon,
                    candidate => OwnedItemView.InSlot(items, (int)candidate) is not null) ?? EquipmentSlot.Weapon;
            }
        }

        if (move is null && !items.Any(item => item.BeltBox >= 0))
        {
            move = items.FirstOrDefault(item => item.InBag && item.Kind == ItemKind.Potion);
            action = InventoryAction.BeltPut;
        }

        if (move is null)
        {
            return false;
        }

        _inventoryAt = _clock.Elapsed.TotalSeconds;
        Show(action == InventoryAction.Equip ? $"Equipando {move.Name}" : $"Colocando {move.Name} no cinto");
        var command = new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, action, move.InstanceId, destination, 0, slot);
        _inventory = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await play.ApplyInventoryAsync(command, timeout.Token);
        });
        return true;
    }

    private int CrystalCount() =>
        session.Owner?.Items?.Where(item => item.Code == AdventurerJourneyRoute.Crystal).Sum(item => item.Count) ?? 0;

    private static string GuideName(TribeRoute route) => route.GuideName.Replace("Adventurer ", "", StringComparison.Ordinal);

    private void Show(string status) => Volatile.Write(ref _status, status);
}

// A route's points in a field's own planar coordinates.
internal sealed record RouteMarks(int FieldCode, IReadOnlyList<Vector2> Points);
