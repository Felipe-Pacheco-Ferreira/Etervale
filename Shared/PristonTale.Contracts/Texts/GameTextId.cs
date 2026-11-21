namespace PristonTale.Contracts.Texts;

// The phrases of GameText, carried on the wire as a uint16; the client words each as "Game.<name>" of its texts.
// Append only. The placeholders a phrase takes follow its name.
public enum GameTextId : ushort
{
    None = 0,

    // Item rules shared by inventory, use, NPC services, trade, personal shops, quests and gates.
    OverWeight = 1,
    CannotUseYet = 2,
    CannotDrop = 3,
    PotionNotStored = 4,
    NotEnoughGold = 5,
    NoSpace = 6,
    GoldLimit = 7,
    StorageRejected = 8,
    StaleInventory = 9,

    // The answer to a retried operation the server had already applied.
    AlreadyApplied = 10,

    // Refusals of any character operation before its domain rules.
    NoSession = 11,
    CharacterBusy = 12,
    WarehouseChanged = 13,
    NotPlaying = 14,
    TooManyRequests = 15,
    RequestRefused = 16,

    // The client could not reach the server.
    ConnectionError = 17,

    // {0} a name the server words (an item, a destination).
    Name = 18,

    // A class title the client words from its data: {0} the PlayerClass number, {1} the tier.
    ClassTitle = 19,

    // Item use.
    ItemUsed = 100,
    ItemNotUsable = 101,
    InventoryChanged = 102,
    ActionPending = 103,
    ItemAlreadyRunning = 104,
    SameAttributeRunning = 105,
    ItemNotHere = 106,
    LevelTooLow = 107,
    NothingToChange = 108,
    DeadCannotUse = 109,
    UseDelay = 110,
    OperationConflict = 111,
    CharacterUnchanged = 112,
    ClanOnly = 113,
    MaleCannotUse = 114,
    FemaleCannotUse = 115,
    CrystalRefused = 116,

    // {0} crystal summons at a time.
    SummonLimit = 117,
    ItemNotUsed = 118,

    // {0} the item's name.
    ItemNamedUsed = 119,
    StatsReset = 120,
    SkillsReset = 121,
    SkillsMastered = 122,
    AgedItemMatured = 123,
    GravityStoneUsed = 124,

    // {0} the item's name.
    ItemReceived = 125,
    LuckyBagHeld = 126,

    // {0} the item's name, {1} the count.
    ItemsReceived = 127,

    // Inventory and ground.
    DropOutOfReach = 200,
    PickupNoRoom = 201,
    NoRoomOnGround = 202,
    PotionNotNow = 203,
    ItemInTrade = 204,
    CloseShopFirst = 205,

    // Progression.
    SkillMasterAway = 250,
    ProgressionRefused = 251,

    // NPCs and their services.
    NpcOutOfReach = 300,
    NpcNoSuchService = 301,
    ServiceNotAvailable = 302,
    OtherTownMaster = 303,
    WarehouseNotOpen = 304,
    ServiceStale = 305,
    NpcRefused = 306,
    WrongPuzzle = 307,
    WrongMaterials = 308,
    NoShopHere = 309,
    ShopBuysAndRepairs = 310,
    ShopRefused = 311,
    WarehouseUnreadable = 312,
    EnterWorldFirst = 313,
    CloseTradeFirst = 314,
    AlreadyTalking = 315,

    // Personal shops.
    ShopNotInTown = 400,
    ShopNotListable = 401,
    SellerShopClosed = 402,
    ShopSoldOut = 403,
    ShopCountChanged = 404,
    ShopPriceChanged = 405,
    ShopTradePending = 406,
    SellerNotFound = 407,
    PersonalShopRefused = 408,

    // Quests: notices, dialog titles, confirmation lines and refusals. QuestKillCount: {0} the monster, {1} the kills
    // left; QuestHunt and QuestHuntFinished: {0} the count, {1} the monster; QuestItemFrom: {0} the monster;
    // QuestItemGone: {0} the item.
    QuestInProgress = 500,
    StatPointsFive = 501,
    SkillPointOne = 502,
    StatFiveSkillOne = 503,
    StatFiveSkillTwo = 504,
    DeathPenaltyReduced = 505,
    VitalityForty = 506,
    SevenStatPoints = 507,
    TenStatPoints = 508,
    TrainingReset = 509,
    MissionTimedOut = 510,
    LifeFifteen = 511,
    QuestItemLevelUp = 512,
    QuestItemSlowed = 513,
    FuryWon = 514,
    FuryLost = 515,
    HestianWon = 516,
    HestianLost = 517,
    RewardReceived = 518,
    TwentyBargons = 519,
    SkillsRedistributed = 520,
    QuestTitle = 521,
    FuryTitle = 522,
    ConfirmDoYouWanna = 523,
    ConfirmDoYouWantTo = 524,
    ConfirmCarryOut = 525,
    ConfirmRankUp = 526,
    ConfirmRankUpFifth = 527,
    ConfirmWantToDo = 528,
    ConfirmProcessQuest = 529,
    ConfirmTheQuest = 530,
    ConfirmSearchQuest = 531,
    ConfirmGetQuest = 532,
    ConfirmReceiveItem = 533,
    ConfirmBattleZone = 534,
    ConfirmRedistribute = 535,
    SkillPointName = 536,
    QuestKillCount = 537,
    QuestHunt = 538,
    QuestItemFrom = 539,
    QuestHuntFinished = 540,
    QuestItemGone = 541,
    InvalidQuestRequest = 542,
    TalkToNpcFirst = 543,
    QuestMovedOn = 544,
    NoQuestWeapon = 545,
    NoClassChange = 546,
    NoSkillRedistribution = 547,
    ConfirmExchangeItem = 548,

    // Elite quests. QuestName, EliteQuestProgress and EliteQuestDone take {0} a quest window title the client words from
    // its data; EliteQuestProgress {1} done of {2}; the points texts {0} the points.
    QuestName = 549,
    ConfirmAcceptQuest = 550,
    ConfirmWantToAccept = 551,
    EliteQuestProgress = 552,
    EliteQuestDone = 553,
    EliteQuestExpired = 554,
    EliteSkillPointsGained = 555,
    StatPointsGained = 556,

    // Trades: TradeRequested and TradeTooFar take {0} the player.
    TradeRequested = 600,
    TradeTooFar = 601,
    TradeOutOfReach = 602,
    TradeBusy = 603,
    TradeNotTradable = 604,
    TradeTooMany = 605,
    TradeChanged = 606,
    TradeCancelled = 607,
    TradeRefused = 608,

    // Parties: {0} a player; PartyRecommended {0} the leader, {1} the player.
    PartyRecommended = 620,
    PartyRequested = 621,
    PartyAlreadyMember = 622,
    PartyInOther = 623,
    PartyLevelGap = 624,
    PlayerNotFound = 625,
    PartyYouLeft = 626,
    PartyExpelled = 627,
    PartyDisbanded = 628,
    PartyDelegated = 629,
    PartyLeaderNow = 630,
    PartyReleased = 631,
    PartyEntered = 632,
    PartyCannotEnter = 633,
    PartyLeft = 634,
    PartyRequest = 635,
    PartyRecommends = 636,

    // Contacts: ContactListed and ContactNotListed take {0} the name.
    WhisperRefused = 650,
    ContactListFull = 651,
    ContactListed = 652,
    ContactNotListed = 653,
    ContactsUnavailable = 654,
    ContactRefused = 655,

    // Chat: PlayerOnline {0} the player; BossTime {0} the minute.
    PlayerOnline = 670,
    BossTime = 671,
    WhispersDenied = 672,
    WhispersAccepted = 673,
    WhisperUnable = 674,

    // StarterGift {0} the item; ShopSold {0} the buyer, {1} the count, {2} the item, {3} the price.
    StarterGift = 680,
    ShopSold = 681,

    // Wing gates.
    GateNotAtStone = 700,
    GateNotReached = 701,
    GateRefused = 702,

    // Clans: refusals, then what members are told. {0} a name (member or clan), {1} a second name or a count.
    ClanNotInClan = 720,
    ClanAlreadyInClan = 721,
    ClanNotChief = 722,
    ClanNotRecruiter = 723,
    ClanLevelTooLow = 724,
    ClanNameInvalid = 726,
    ClanNameTaken = 727,
    ClanFull = 728,
    ClanJoinCooldown = 729,
    ClanChiefOnAccount = 730,
    ClanTargetNotFound = 731,
    ClanTargetInClan = 732,
    ClanNotRegistered = 733,
    ClanTargetLevelTooLow = 734,
    ClanCapacityTooLow = 735,
    ClanViceExists = 736,
    ClanNotVice = 737,
    ClanChiefCannotLeave = 738,
    ClanUnavailable = 739,
    ClanNoInvitation = 740,
    ClanInviteRefused = 741,
    ClanNotMember = 742,
    ClanFounded = 750,
    ClanJoined = 751,
    ClanLeft = 752,
    ClanExpelled = 753,
    ClanYouWereExpelled = 754,
    ClanDisbanded = 755,
    ClanRegistered = 756,
    ClanBelowSix = 757,
    ClanChiefChanged = 758,
    ClanViceAppointed = 759,
    ClanViceDischarged = 760,
    ClanInviteSent = 761,
    ClanBusy = 762,
    ClanMarkUnavailable = 763,
    ClanMarkChanged = 764,
    RegisteredClanOnly = 765,
    UnionCoreOwn = 766,
    UnionCoreAway = 767,
    UnionCoreWrongClan = 768,
    UnionCoreBound = 769,
    ClanPetOwned = 770,
    ClanPetsNearby = 771,
    SiegeInAnHour = 772,
    SiegeInHalfAnHour = 773,
    SiegeStarted = 774,
    SiegeOver = 775,
    SiegeRestartsSoon = 776,
    SiegeLeague = 777,
    CastleTaken = 778,
    FountainPotion = 779,
    MercenaryLimit = 780,
    BellatraClosed = 781,
    BellatraLevel = 782,
    BellatraNoTickets = 783,
    BellatraTeamFull = 784,
    BellatraEntered = 785,
    BellatraTicket = 786,
    BellatraOut = 787,
    BellatraCleared = 788,
    BellatraBossDown = 789,

    // A seal an entrant broke.
    BellatraQuake = 790,
    BellatraStun = 791,
    BellatraFreeze = 792,
    BellatraRabbit = 793,
    BellatraStygian = 794,
    BellatraGuardian = 795,
    BellatraPoints = 796,
    BellatraSealIdle = 797,

    // The answers of a claim in the events and rewards window.
    RewardClaimed = 800,
    RewardAlreadyClaimed = 801,
    RewardExpired = 802,
    RewardGoldLimit = 803,
    RewardLevelTooLow = 804,
    RewardBusy = 805,
    RewardNotReady = 806,

    // What the server tells of other players: AgingNotice {0} the player, {1} the item, {2} the level; LevelNotice {0}
    // the player, {1} the level; PartyLoot and PartyRareLoot (a unique or set item) {0} the member, {1} the item.
    AgingNotice = 820,
    LevelNotice = 821,
    PartyLoot = 822,
    PartyRareLoot = 823,
    BellatraOpening = 824,

    // A chat line translated for its receiver (ChatTranslation): the line's text is the translated line, which the client
    // shows as {0} with the language's "(translated)" mark.
    ChatTranslated = 830,

    // The PvP mode's answers (PvpRules): PvpLevel {0} the level it needs, PvpInCombat {0} the seconds left of the hold,
    // PvpMurderer {0} the seconds left of the mark; PvpMurder {0} the player the reader killed, {1} the minutes the reader
    // is now a murderer.
    PvpOn = 840,
    PvpOff = 841,
    PvpLevel = 842,
    PvpInCombat = 843,
    PvpMurderer = 844,
    PvpMurder = 845,

    // Duels (DuelRules): {0} the other player, except DuelField; DuelNotice {0} the winner, {1} the loser, and
    // DuelDrawNotice {0}, {1} the two, which the other players of the field read (the client may hide them).
    DuelChallenged = 850,
    DuelNotFound = 851,
    DuelTooFar = 852,
    DuelField = 853,
    DuelBusy = 854,
    DuelSelf = 855,
    DuelRefused = 856,
    DuelStarted = 857,
    DuelWon = 858,
    DuelLost = 859,
    DuelDraw = 860,
    DuelNotice = 861,
    DuelDrawNotice = 862,

    // The arena (ArenaRules): ArenaOpen {0} the minutes it stays open; ArenaQueued {0} the mode ("1x1", "3x3");
    // ArenaClosed {0} its hours; ArenaLevel {0} the level it needs; ArenaParty {0} the party's size; ArenaStarted {0} the
    // mode, {1} the other side; ArenaWon and ArenaLost {0} the points won or lost. ArenaOpen, ArenaNotice ({0} the mode,
    // {1} the winners, {2} the losers) and ArenaDrawNotice ({0} the mode, {1} and {2} the sides) go to every player, who
    // may hide them.
    ArenaOpen = 870,
    ArenaQueued = 871,
    ArenaLeft = 872,
    ArenaClosed = 873,
    ArenaLevel = 874,
    ArenaParty = 875,
    ArenaBusy = 876,
    ArenaNotQueued = 877,
    ArenaQueueClosed = 878,
    ArenaStarted = 879,
    ArenaOut = 880,
    ArenaWon = 881,
    ArenaLost = 882,
    ArenaDraw = 883,
    ArenaNotice = 884,
    ArenaDrawNotice = 885,

    // The Battle Royale (RoyaleRules): RoyaleOpen {0} the minutes until it starts, {1} the level it needs; RoyaleRegistered
    // {0} how many are registered; RoyaleClosed {0} its hours; RoyaleLevel {0} the level it needs; RoyaleFull {0} the most
    // players; RoyaleCancelled {0} how many registered, {1} the fewest it needs; RoyaleStarted {0} how many fight;
    // RoyaleOut {0} the players still standing; RoyalePlace {0} the place, {1} the experience gained; RoyaleWinner {0}
    // the winner. RoyaleOpen, RoyaleCancelled, RoyaleStarted and RoyaleWinner go to every player, who may hide them.
    RoyaleOpen = 890,
    RoyaleRegistered = 891,
    RoyaleLeft = 892,
    RoyaleClosed = 893,
    RoyaleLevel = 894,
    RoyaleFull = 895,
    RoyaleBusy = 896,
    RoyaleNotRegistered = 897,
    RoyaleCancelled = 898,
    RoyaleStarted = 899,
    RoyaleOut = 900,
    RoyalePlace = 901,
    RoyaleWinner = 902,
    RoyaleSkipped = 903,

    // Titles (TitleRules): a title is its code, worded by the client. TitleGained {0} the player, {1} the title, to every
    // player; TitleLost {0} the one who lost it, {1} the title, {2} who took it, to every player; TitleEarned {0} the title,
    // to its owner whenever it gets one. TitleGained and TitleLost may be hidden.
    TitleGained = 904,
    TitleLost = 905,
    TitleEarned = 906,

    // TitleTaken {0} the title, {1} who took it: to the one who lost a position title.
    TitleTaken = 907,

    // The boss roulette (BossRoulette): BossRouletteWinner {0} the winner, {1} the prize, {2} the boss, to every player, who
    // may hide it; BossRouletteWon {0} the prize, {1} the boss, to the winner.
    BossRouletteWinner = 908,
    BossRouletteWon = 909
}
