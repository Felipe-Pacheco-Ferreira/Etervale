using Android.App;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.Localization;
using PristonTale.Mobile;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;
using Orientation = Android.Widget.Orientation;

internal sealed partial class VulkanFreeHuntPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session,
    HudLayoutEditor editor, VulkanItemIcons icons)
{
    private static readonly string[] Tabs = ["Caça", "Habilidades", "Poções", "Segurança", "Coleta", "Ganhos", "Grupo", "Log"];
    private static readonly string[] TabIcons = ["◎", "✦", "◈", "⛨", "⬡", "↗", "♟", "▤"];
    private static readonly Color Blue = Color.Rgb(131, 189, 249);
    private static readonly Color Green = Color.Rgb(174, 227, 158);
    private static readonly Color Gold = MobileHudTheme.Gold;

    private VulkanPanel panel;
    private LinearLayout tabMenu;
    private LinearLayout page;
    private ScrollView scroll;
    private TextView status;
    private TextView toggle;
    private TextView statistics;
    private TextView recentLog;
    private TextView logCount;
    private TextView group;
    private string logQuery = "";
    private int logCategory;
    private readonly Dictionary<string, TextView> readings = [];
    private int tab;
    private int lastLog;
    private bool lastActive;
    private bool lastAllowed;
    private bool lastPause;
    private VulkanWorldSession.HuntTotals lastTotals;
    private long nextTotalsSave;
    private VulkanWorldSession.HuntTotals lastSaved;
    private bool builtStatus;

    private Activity Activity => activity;
    private FreeHuntSettings Settings => session.HuntSettings;

    public void Open()
    {
        if (panel is not null)
        {
            return;
        }

        panel = new VulkanPanel(activity, overlay, "◆ Auto-caça", 3000, 3000);
        icons.Loaded += RefreshLootIcons;
        panel.Closed += () =>
        {
            icons.Loaded -= RefreshLootIcons;
            pendingLootIcons.Clear();
            editor.KeepFreeHuntSettings(session.Character.Name, session.HuntProfileScope, Settings);
            SaveTotals(session.HuntStatistics);
            panel = null;
        };
        var layout = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        panel.Content.AddView(layout, new LinearLayout.LayoutParams(-1, 0, 1));
        tabMenu = Column();
        layout.AddView(tabMenu, new LinearLayout.LayoutParams(Dp(150), -1) { RightMargin = Dp(8) });
        var content = Column();
        layout.AddView(content, new LinearLayout.LayoutParams(0, -1, 1));
        var bar = new LinearLayout(activity) { Background = Plate() };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(8), Dp(3), Dp(5), Dp(3));
        content.AddView(bar, new LinearLayout.LayoutParams(-1, Dp(48)) { BottomMargin = Dp(6) });
        bar.AddView(VulkanPanelArt.Chip(activity, "AUTO-CAÇA", Blue));
        status = VulkanPanel.Heading(activity, "", 13);
        bar.AddView(status, new LinearLayout.LayoutParams(0, -2, 1) { LeftMargin = Dp(8) });
        toggle = Button("");
        toggle.Click += (_, _) => session.SetFreeHunt(!session.FreeHuntEnabled && session.FreeHuntAvailable);
        bar.AddView(toggle, new LinearLayout.LayoutParams(Dp(124), -1));
        var reset = Button("↻");
        reset.ContentDescription = "Restaurar todas as configurações de Auto-caça";
        reset.Click += (_, _) => ConfirmReset();
        bar.AddView(reset, new LinearLayout.LayoutParams(Dp(42), -1) { LeftMargin = Dp(5) });
        scroll = new ScrollView(activity) { FillViewport = true, VerticalScrollBarEnabled = true };
        content.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        page = Column();
        scroll.AddView(page, new FrameLayout.LayoutParams(-1, -2));
        builtStatus = false;
        Show(tab);
    }

    public void Refresh()
    {
        if (lastLog != session.HuntLogVersion)
        {
            lastLog = session.HuntLogVersion;
            var lines = session.ReadHuntLog();
            editor.KeepFreeHuntLog(session.Character.Name, lines);
            if (recentLog is not null && panel is not null)
            {
                RenderLog(lines);
            }
        }

        var totals = session.HuntStatistics;
        var now = Environment.TickCount64;
        if (now >= nextTotalsSave && !ReferenceEquals(lastSaved, totals) && (totals.Seconds > 0 || lastSaved is { Seconds: > 0 }))
        {
            SaveTotals(totals);
            nextTotalsSave = now + 30000;
        }

        if (panel is null)
        {
            return;
        }

        var active = session.FreeHuntEnabled;
        var allowed = session.FreeHuntAvailable;
        var pause = session.HuntManuallyPaused;
        if (!builtStatus || lastActive != active || lastAllowed != allowed || lastPause != pause)
        {
            builtStatus = true;
            (lastActive, lastAllowed, lastPause) = (active, allowed, pause);
            status.Text = pause ? "Pausa manual" : active ? "Ligado" : "Desligado";
            toggle.Text = active ? "Ⅱ  PAUSAR" : "▶  INICIAR";
            toggle.Enabled = active || allowed;
            toggle.Background = Plate(active ? Gold : Green);
            toggle.Alpha = active || allowed ? 1f : .55f;
        }

        if (!ReferenceEquals(lastTotals, totals))
        {
            lastTotals = totals;
            ShowTotals(totals);
        }
    }

    private void SaveTotals(VulkanWorldSession.HuntTotals totals)
    {
        editor.KeepFreeHuntTotals(session.Character.Name, totals);
        lastSaved = totals;
    }

    private void Show(int index)
    {
        tab = index;
        tabMenu.RemoveAllViews();
        for (var i = 0; i < Tabs.Length; i += 2)
        {
            var row = new LinearLayout(activity);
            tabMenu.AddView(row, new LinearLayout.LayoutParams(-1, Dp(49)) { BottomMargin = Dp(4) });
            for (var column = 0; column < 2; column++)
            {
                var value = i + column;
                var cell = Button($"{TabIcons[value]}\n{Tabs[value]}", selected: tab == value);
                cell.TextSize = 11;
                cell.SetMaxLines(2);
                cell.SetPadding(0, 0, 0, 0);
                cell.ContentDescription = Tabs[value];
                cell.Click += (_, _) => Show(value);
                row.AddView(cell, new LinearLayout.LayoutParams(0, -1, 1) { RightMargin = Dp(3) });
            }
        }

        page.RemoveAllViews();
        pendingLootIcons.Clear();
        statistics = group = recentLog = logCount = null;
        readings.Clear();
        switch (tab)
        {
            case 0: HuntPage(); break;
            case 1: SkillsPage(); break;
            case 2: PotionsPage(); break;
            case 3: SafetyPage(); break;
            case 4: LootPage(); break;
            case 5: GainsPage(); break;
            case 6: GroupPage(); break;
            case 7: LogPage(); break;
        }

        scroll.ScrollTo(0, 0);
        ShowTotals(session.HuntStatistics);
        Refresh();
    }

    private void HuntPage()
    {
        var area = Card("Área");
        Range(area, "Raio de caça", Settings.Radius, FreeHuntSettings.UnitsPerStep, FreeHuntSettings.MaximumRadius,
            v => { Change(s => s with { Radius = v }); session.PreviewHuntRange(loot: false); });
        Note(area, "64 = 1 passo de chão. A caça começa onde você ligar.");
        Check(area, "Voltar ao ponto inicial quando não houver alvo", Settings.ReturnToStart, v => Change(s => s with { ReturnToStart = v }));
        Choice(area, "Mover o ponto de caça quando travar", ["Manter o ponto inicial", "Automático"],
            Settings.RelocateWhenBlocked ? 1 : 0, v => Change(s => s with { RelocateWhenBlocked = v == 1 }));
        Check(area, "Parar de atacar para voltar ao ponto inicial", Settings.BreakCombatToReturn, v => Change(s => s with { BreakCombatToReturn = v }));
        Action(area, "Definir ponto inicial aqui", session.SetHuntOrigin);

        var monsters = Card("Monstros");
        Text(monsters, "Só caçar", Settings.OnlyMonsters, "vazio = todos (ex.: Hopy, Rabie)", v => Change(s => s with { OnlyMonsters = v }));
        Text(monsters, "Ignorar", Settings.IgnoredMonsters, "partes do nome, separadas por vírgula", v => Change(s => s with { IgnoredMonsters = v }));
        Number(monsters, "Nível mínimo", Settings.MinimumLevel, 0, 500, v => Change(s => s with { MinimumLevel = v }));
        Number(monsters, "Nível máximo", Settings.MaximumLevel, 0, 500, v => Change(s => s with { MaximumLevel = v }));
        Check(monsters, "Aplicar filtros mesmo a quem me atacar", Settings.StrictMonsterFilter, v => Change(s => s with { StrictMonsterFilter = v }));
        Check(monsters, "Não roubar monstros de outros jogadores", Settings.AvoidClaimed, v => Change(s => s with { AvoidClaimed = v }));
        Check(monsters, "Incluir monstros que membros do meu grupo estão atacando", Settings.IncludePartyTargets,
            v => Change(s => s with { IncludePartyTargets = v }));
        Check(monsters, "Priorizar monstros de quest", Settings.PrioritizeQuest, v => Change(s => s with { PrioritizeQuest = v }));
        Check(monsters, "Manter o alvo até ele morrer", Settings.KeepTarget, v => Change(s => s with { KeepTarget = v }));
        Check(monsters, "Focar monstros de longe", Settings.PrioritizeRanged, v => Change(s => s with { PrioritizeRanged = v }));
        Check(monsters, "Priorizar o chefe", Settings.PrioritizeBoss, v => Change(s => s with { PrioritizeBoss = v }));
        Number(monsters, "Perseguir até (% além do raio)", Settings.PursuitPercent, 0, 120, v => Change(s => s with { PursuitPercent = v }));

        var unreachable = Card("Alvo inalcançável");
        Number(unreachable, "Desistir após (s sem progresso)", Settings.TargetTimeout, 2, 90, v => Change(s => s with { TargetTimeout = v }));
        Number(unreachable, "Ignorar por (s)", Settings.IgnoreSeconds, 1, 300, v => Change(s => s with { IgnoreSeconds = v }));
        var hud = Card("Painel");
        Check(hud, "Mostrar o quadro de status durante a caça", Settings.ShowStatus, v => Change(s => s with { ShowStatus = v }));
        var account = Card("Conta");
        Check(account, "Usar a mesma configuração para a conta toda", Settings.ShareAccount, v => Change(s => s with { ShareAccount = v }));
        Note(account, "Os personagens da mesma conta e servidor neste aparelho compartilham as opções. A lista de habilidades é por personagem.");
        ProfileTransfer();
    }

    private void SkillsPage()
    {
        var used = Card("Habilidades usadas");
        var learned = session.Owner?.Skills;
        var count = 0;
        if (learned is not null)
        {
            foreach (var owned in learned)
            {
                if (owned.Rank <= 0 || !VulkanActionWheel.ShortcutEligible(owned.Code) || !ClassSkillCatalog.TryGet(owned.Code, out var skill))
                {
                    continue;
                }

                count++;
                var code = owned.Code;
                Check(used, $"{DataText.SkillName(skill)} · Nv. {owned.Rank}", Settings.Skills.Contains(code), v =>
                    Change(s => s with { Skills = v ? [.. s.Skills, code] : s.Skills.Where(x => x != code).ToArray() }));
            }
        }
        foreach (var code in Settings.Skills)
        {
            if (!ClassSkillCatalog.TryGet(code, out var skill))
            {
                continue;
            }

            var selected = code;
            Number(used, $"MP mínimo (%) · {DataText.SkillName(skill)}", Settings.SkillManaFloor.GetValueOrDefault(code), 0, 100, value =>
                Change(s => s with { SkillManaFloor = new Dictionary<uint, int>(s.SkillManaFloor) { [selected] = value } }));
            var buttons = new LinearLayout(activity);
            used.AddView(buttons);
            Action(buttons, "↑ Subir", () => MoveSkill(selected, -1));
            Action(buttons, "↓ Descer", () => MoveSkill(selected, 1));
        }

        if (count == 0)
        {
            Note(used, "Nenhuma habilidade ativa aprendida ainda.");
        }

        var attack = Card("Ataque");
        Check(attack, "Ataque normal entre as habilidades", Settings.BasicAttacks, v => Change(s => s with { BasicAttacks = v }));
        Number(attack, "Alcance das habilidades em área", Settings.AreaCastRange, 0, 2048, v => Change(s => s with { AreaCastRange = v }));
        var area = Card("Skill de área");
        Check(area, "Priorizar skill de área", Settings.PreferArea, v => Change(s => s with { PreferArea = v }));
        Number(area, "Mínimo de monstros", Settings.AreaMinimum, 1, 40, v => Change(s => s with { AreaMinimum = v }));
        Number(area, "Raio (0 = área da habilidade)", Settings.AreaRadius, 0, 2048, v => Change(s => s with { AreaRadius = v }));
        var swap = Card("Troca de arma");
        Check(swap, "Trocar para o escudo para buffar", Settings.SwapShieldForBuff, v => Change(s => s with { SwapShieldForBuff = v }));
        var summon = Card("Cristal de invocação");
        Check(summon, "Soltar cristal automaticamente", Settings.UseCrystals, v => Change(s => s with { UseCrystals = v }));
        InventoryChoice(summon, "Cristal", Settings.Crystal,
            item => MonsterCrystals.TryGet(item.CatalogKey, out var crystal) && crystal.Kind == CrystalKind.Pet,
            v => Change(s => s with { Crystal = v }));
        Number(summon, "Manter invocados", Settings.SummonCount, 1, MonsterCrystals.PetSlots, v => Change(s => s with { SummonCount = v }));
        Check(summon, "Só durante a luta", Settings.CrystalInCombat, v => Change(s => s with { CrystalInCombat = v }));
        Choice(summon, "Alvo do companheiro", ["Por conta própria", "Meu alvo", "Defender personagem"], (int)Settings.CompanionTarget,
            v => Change(s => s with { CompanionTarget = (CrystalPetTargeting)v }));
        Note(summon, "O modo vale para monstros de cristal enquanto a Auto-caça estiver ligada.");
        var force = Card("Force Orb");
        Check(force, "Usar Force automaticamente", Settings.UseForce, v => Change(s => s with { UseForce = v }));
        InventoryChoice(force, "Force", Settings.ForceKey, item => ForceOrbs.TryGet(item.CatalogKey, out _), v => Change(s => s with { ForceKey = v }));
        Check(force, "Usar da mais fraca para a mais forte", Settings.WeakForceFirst, v => Change(s => s with { WeakForceFirst = v }));
        Check(force, "Só durante a luta", Settings.ForceInCombat, v => Change(s => s with { ForceInCombat = v }));
        Check(Card("Premiums"), "Usar premiums automaticamente", Settings.UsePremiums, v => Change(s => s with { UsePremiums = v }));
    }

    private void MoveSkill(uint code, int offset)
    {
        var order = (uint[])Settings.Skills.Clone();
        var start = Array.IndexOf(order, code);
        var end = start + offset;
        if (start >= 0 && end >= 0 && end < order.Length)
        {
            (order[start], order[end]) = (order[end], order[start]);
            Change(s => s with { Skills = order });
            Show(tab);
        }
    }

    private void PotionsPage()
    {
        Potion("HP (vida)", PotionResource.Life, Settings.HP, (s, v) => s with { HP = v }, s => s.HP);
        Potion("MP (mana)", PotionResource.Mana, Settings.MP, (s, v) => s with { MP = v }, s => s.MP);
        Potion("RES (estamina)", PotionResource.Stamina, Settings.RES, (s, v) => s with { RES = v }, s => s.RES);
        var adaptive = Card("Poção adaptativa");
        Check(adaptive, "Adaptativo", Settings.AdaptivePotion, v => Change(s => s with { AdaptivePotion = v }));
        Note(adaptive, "Após dano intenso, antecipa a poção de HP. MP e RES usam o limite configurado.");
        var belt = Card("Cinto de poções");
        Check(belt, "Repor o cinto com poções da bolsa", Settings.RefillBelt, v => Change(s => s with { RefillBelt = v }));
        var rhythm = Card("Ritmo");
        Number(rhythm, "Intervalo entre poções do mesmo tipo (ms)", Settings.SamePotionMs, 500, 10000, v => Change(s => s with { SamePotionMs = v }));
        Number(rhythm, "Entre quaisquer poções (ms)", Settings.AnyPotionMs, 500, 10000, v => Change(s => s with { AnyPotionMs = v }));
        Note(rhythm, "Com dois recursos baixos, o menor percentual vai primeiro. Usa poções do cinto e da bolsa aberta.");
    }

    private void Potion(string name, PotionResource family, PotionHuntRule selected, Func<FreeHuntSettings, PotionHuntRule, FreeHuntSettings> write,
        Func<FreeHuntSettings, PotionHuntRule> read)
    {
        var card = Card(name);
        Check(card, "Usar poção automaticamente", selected.Enabled, v => Change(s => write(s, read(s) with { Enabled = v })));
        Number(card, "Quando abaixo de (%)", selected.Threshold, 0, 100, v => Change(s => write(s, read(s) with { Threshold = v })));
        InventoryChoice(card, "Poção preferida", selected.Key, item => PotionDefinition.FamilyOf(item.CatalogKey) == family,
            v => Change(s => write(s, read(s) with { Key = v })));
    }

    private void SafetyPage()
    {
        var critical = Card("HP crítico");
        Number(critical, "HP crítico abaixo de (%)", Settings.CriticalHp, 0, 100, v => Change(s => s with { CriticalHp = v }));
        Safety(critical, "Sem poção", Settings.CriticalAction, v => Change(s => s with { CriticalAction = v }));
        var supply = Card("Acabaram as poções");
        Check(supply, "Usar Core quando acabar as poções", Settings.UseCoreWhenOutOfPotions, v => Change(s => s with { UseCoreWhenOutOfPotions = v }));
        Safety(supply, "Sem poções de HP", Settings.NoHealthPotion, v => Change(s => s with { NoHealthPotion = v }));
        var bag = Card("Bolsa cheia");
        Safety(bag, "Ação", Settings.FullBag, v => Change(s => s with { FullBag = v }));
        Note(bag, "Quando a coleta é recusada, Continuar limita a coleta a ouro por um minuto.");
        var death = Card("Morte");
        Check(death, "Ir para a cidade ao morrer", Settings.RestartInTown, v => Change(s => s with { RestartInTown = v }));
        Check(death, "Voltar para a cidade após 3 mortes em 10 min", Settings.TownAfterDeaths, v => Change(s => s with { TownAfterDeaths = v }));
        Check(death, "Voltar para a cidade com poucos Pergaminhos de Renascimento", Settings.TownWhenLowScrolls,
            v => Change(s => s with { TownWhenLowScrolls = v }));
        Check(death, "Parar a caçada após ficar sem grupo", Settings.StopOutsideParty, v => Change(s => s with { StopOutsideParty = v }));
        Note(death, "Com o retorno desligado, aguarda ressurreição ou sua escolha no quadro.");
        var manual = Card("Controle manual");
        Check(manual, "Pausar quando eu clicar / usar habilidades", Settings.PauseForManual, v => Change(s => s with { PauseForManual = v }));
        Number(manual, "Retomar após (ms sem comandos)", Settings.ResumeMs, 0, 60000, v => Change(s => s with { ResumeMs = v }));
    }

    private void Safety(LinearLayout place, string label, HuntSafety value, Action<HuntSafety> set) =>
        Choice(place, label, ["Continuar", "Pausar", "Voltar à cidade (Core) e parar"], (int)value, i => set((HuntSafety)i));

    private void LootPage()
    {
        var loot = Card("Coleta automática");
        Check(loot, "Pegar itens do chão", Settings.Loot, v => Change(s => s with { Loot = v }));
        Choice(loot, "O que pegar", ["Tudo, com o filtro abaixo", "Só ouro", "Ouro e poções", "Só a lista Sempre pegar"],
            (int)Settings.LootMode, v => Change(s => s with { LootMode = (LootHuntMode)v }));
        LootCategories();
        var potions = Card("Poções");
        PotionLoot(potions, "HP (vida)", PotionResource.Life);
        Number(potions, "Parar de coletar HP com", Settings.MaxLifePotions, 0, 9999, v => Change(s => s with { MaxLifePotions = v }));
        PotionLoot(potions, "MP (mana)", PotionResource.Mana);
        Number(potions, "Parar de coletar MP com", Settings.MaxManaPotions, 0, 9999, v => Change(s => s with { MaxManaPotions = v }));
        PotionLoot(potions, "RES (estamina)", PotionResource.Stamina);
        Number(potions, "Parar de coletar RES com", Settings.MaxStaminaPotions, 0, 9999, v => Change(s => s with { MaxStaminaPotions = v }));
        Note(potions, "Conta Bolsa e Cinto. Pilhas que excederiam a cota ficam no chão. 0 = sem limite.");
        LootList("Sempre pegar", Settings.AlwaysLoot, v => Change(s => s with { AlwaysLoot = v }));
        LootList("Nunca pegar", Settings.NeverLoot, v => Change(s => s with { NeverLoot = v }));
        ItemLevels();
        var spec = Card("Spec");
        Check(spec, "Priorizar itens da minha spec", Settings.PrioritizeOwnSpec, v => Change(s => s with { PrioritizeOwnSpec = v }));
        Check(spec, "Só equipamentos da minha spec", Settings.OnlyOwnSpec, v => Change(s => s with { OnlyOwnSpec = v }));
        var reach = Card("Alcance");
        Range(reach, "Distância máxima", Settings.LootRadius, FreeHuntSettings.UnitsPerStep, FreeHuntSettings.MaximumRadius, v => { Change(s => s with { LootRadius = v }); session.PreviewHuntRange(loot: true); });
        Check(reach, "Não pegar o que eu larguei", Settings.IgnoreOwnDrops, v => Change(s => s with { IgnoreOwnDrops = v }));
        Check(reach, "Voltar ao ponto inicial depois de coletar", Settings.ReturnAfterLoot, v => Change(s => s with { ReturnAfterLoot = v }));
        var lootBreak = Card("Pausa para coletar");
        Number(lootBreak, "Parar de atacar após (s · 0 desliga)", Settings.LootAfterSeconds, 0, 600, v => Change(s => s with { LootAfterSeconds = v }));
        Action(Card("Padrão"), "Restaurar padrão", () =>
        {
            var defaults = new FreeHuntSettings();
            Change(s => s with { Loot = defaults.Loot, LootRadius = defaults.LootRadius, LootMode = defaults.LootMode, Excluded = 0,
                AlwaysLoot = "", NeverLoot = "", MaxLifePotions = 0, MaxManaPotions = 0, MaxStaminaPotions = 0, ReturnAfterLoot = false,
                LootAfterSeconds = 0, LevelByCategory = [], MinimumItemLevel = 0, MaximumItemLevel = 0, IgnoreOwnDrops = true, ExcludedPotions = "",
                PrioritizeOwnSpec = false, OnlyOwnSpec = false });
            Show(tab);
        });
    }

    private void GainsPage()
    {
        var control = Card("Sessão");
        var row = new LinearLayout(activity);
        control.AddView(row);
        Action(row, session.HuntStatisticsResting ? "Retomar" : "Descanso", () => { session.RestHuntStatistics(!session.HuntStatisticsResting); Show(tab); });
        Action(row, "Zerar", session.ResetHuntStatistics);
        Choice(control, "Contar", ["Só com a auto-caça ligada", "Sempre"], Settings.CountOnlyHunting ? 0 : 1,
            v => Change(s => s with { CountOnlyHunting = v == 0 }));
        statistics = VulkanPanel.Body(activity, "", 14);
        control.AddView(statistics);
        Metrics(Card("Experiência"), ["EXP por hora", "EXP/h (últimos 10 min)", "EXP ganha", "Progresso atual",
            "Perdida (mortes)", "Nível atual", "Níveis ganhos", "Próximo nível em"]);
        var wallet = Card("Balanço de ouro");
        Metrics(wallet, ["Saldo da sessão", "Saldo por hora", "Entradas", "Saídas", "Coletado do chão"]);
        Note(wallet, "Entradas e saídas são variações do saldo da bolsa. O ouro do chão conta coletas confirmadas.");
        Metrics(Card("Abates"), ["Monstros", "Abates por hora", "Abates (últimos 10 min)", "Mortes", "Dano total", "DPS (30 s)"]);
        Metrics(Card("Poções usadas"), ["HP", "MP", "RES"]);
        var drops = Card("Itens coletados");
        Metrics(drops, ["Unidades", "Tipos de item"]);
        var text = VulkanPanel.Body(activity, "", 12);
        readings["Itens"] = text;
        drops.AddView(text);
    }

    private void GroupPage()
    {
        var card = Card("Grupo");
        Action(card, "Zerar", session.ResetHuntGroup);
        Metrics(card, ["DPS do grupo (30 s)", "Dano do grupo", "Abates do grupo", "EXP do grupo", "Mortes no grupo"]);
        group = VulkanPanel.Body(activity, "", 14);
        Card("Membros").AddView(group);
    }

    private void LogPage()
    {
        var card = Card("O que aconteceu");
        Choice(card, "Mostrar", ["Todos", "Caça e segurança", "Mortes", "Grupo"], logCategory, value =>
        {
            logCategory = value;
            RenderLog(session.ReadHuntLog());
        });
        Text(card, "Buscar", logQuery, "Ex.: perfil, cidade, nome", value =>
        {
            logQuery = value;
            RenderLog(session.ReadHuntLog());
        });
        logCount = VulkanPanel.Body(activity, "", 12);
        card.AddView(logCount);
        Action(card, "Limpar histórico", ConfirmClearLog);
        recentLog = VulkanPanel.Body(activity, "", 13);
        card.AddView(recentLog);
        RenderLog(session.ReadHuntLog());
    }

    private void RenderLog(string[] lines)
    {
        if (recentLog is null || logCount is null)
        {
            return;
        }

        var filtered = lines.Reverse().Where(line => MatchesLogCategory(line) && line.Contains(logQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        logCount.Text = $"{filtered.Length} de {lines.Length} registros";
        recentLog.Text = filtered.Length > 0 ? string.Join("\n", filtered) : "Nenhum registro para esse filtro.";
    }

    private bool MatchesLogCategory(string line) => logCategory switch
    {
        1 => line.Contains(" · Auto-caça", StringComparison.OrdinalIgnoreCase),
        2 => line.Contains("morreu", StringComparison.OrdinalIgnoreCase),
        3 => line.Contains(" no grupo", StringComparison.OrdinalIgnoreCase) || line.Contains(" do grupo", StringComparison.OrdinalIgnoreCase),
        _ => true
    };

    private void ConfirmClearLog()
    {
        using var dialog = new AlertDialog.Builder(activity, Android.Resource.Style.ThemeMaterialDialogAlert);
        dialog.SetTitle("Limpar histórico");
        dialog.SetMessage("Apagar os registros de Auto-caça deste personagem?");
        dialog.SetPositiveButton("Limpar", (_, _) => session.ClearHuntLog());
        dialog.SetNegativeButton("Cancelar", (_, _) => { });
        dialog.Show();
    }

    private void ShowTotals(VulkanWorldSession.HuntTotals totals)
    {
        var hours = totals.Seconds / 3600d;
        if (statistics is not null)
        {
            statistics.Text = $"Sessão {totals.Seconds / 60}m{totals.Seconds % 60:00}s";
            var owner = session.Owner;
            var remaining = owner is not null && owner.NextLevel > owner.Experience ? owner.NextLevel - owner.Experience : 0;
            var span = owner is not null && owner.NextLevel > owner.LevelStart ? owner.NextLevel - owner.LevelStart : 0;
            Reading("EXP por hora", Rate(totals.Experience, hours));
            Reading("EXP/h (últimos 10 min)", totals.RecentExperiencePerHour > 0 ? totals.RecentExperiencePerHour.ToString("N0") : "—");
            Reading("EXP ganha", totals.Experience.ToString("N0"));
            var levelProgress = owner is not null && owner.Experience > owner.LevelStart ? owner.Experience - owner.LevelStart : 0;
            Reading("Progresso atual", span > 0 ? $"{Math.Min(levelProgress, span) * 100d / span:N1}%" : "—");
            Reading("Perdida (mortes)", totals.ExperienceLost.ToString("N0"));
            Reading("Nível atual", owner?.Level.ToString() ?? "—");
            Reading("Níveis ganhos", totals.Levels.ToString("N0"));
            Reading("Próximo nível em", remaining > 0 && totals.Experience > 0 && hours > 0 ? $"{remaining * hours / totals.Experience:N1} h" : "—");
            Reading("Saldo da sessão", (totals.Gold - totals.GoldSpent).ToString("N0"));
            Reading("Saldo por hora", Rate(totals.Gold - totals.GoldSpent, hours));
            Reading("Coletado do chão", totals.DroppedGold.ToString("N0"));
            Reading("Entradas", totals.Gold.ToString("N0"));
            Reading("Saídas", totals.GoldSpent.ToString("N0"));
            Reading("Monstros", totals.Kills.ToString("N0"));
            Reading("Abates por hora", Rate(totals.Kills, hours));
            Reading("Abates (últimos 10 min)", totals.RecentKills.ToString("N0"));
            Reading("Mortes", totals.Deaths.ToString("N0"));
            Reading("Dano total", totals.Damage.ToString("N0"));
            Reading("DPS (30 s)", totals.RecentDamagePerSecond.ToString("N1"));
            Reading("HP", totals.HP.ToString("N0"));
            Reading("MP", totals.MP.ToString("N0"));
            Reading("RES", totals.RES.ToString("N0"));
            Reading("Unidades", totals.Collected.ToString("N0"));
            Reading("Tipos de item", totals.Items.Count.ToString("N0"));
            Reading("Itens", totals.Items.Count == 0 ? "Nenhum item coletado ainda."
                : string.Join("\n", totals.Items.OrderByDescending(item => item.Count).Take(40).Select(item => $"{item.Name}  ×{item.Count:N0}")));
        }

        if (group is not null)
        {
            Reading("Dano do grupo", totals.Members.Sum(x => x.Damage).ToString("N0"));
            Reading("DPS do grupo (30 s)", totals.Members.Sum(x => x.Active ? x.RecentDamagePerSecond : 0).ToString("N1"));
            Reading("Abates do grupo", totals.Members.Sum(x => x.Kills).ToString("N0"));
            Reading("EXP do grupo", totals.Members.Aggregate(0UL, (sum, member) => sum + member.Experience).ToString("N0"));
            Reading("Mortes no grupo", totals.Members.Sum(x => x.Deaths).ToString("N0"));
            group.Text = totals.Members.Count == 0 ? "Sem membros registrados nesta sessão."
                : string.Join("\n\n", totals.Members.OrderByDescending(member => member.Active).Select(member =>
                    $"{member.Name} · {(member.Active ? $"{member.RecentDamagePerSecond:N1} DPS" : "Saiu do grupo")}\n"
                    + $"{member.Damage:N0} de dano · {member.Kills:N0} abates · {member.Experience:N0} EXP · {member.Deaths:N0} mortes"));
        }
    }

    private static string Rate(double value, double hours) => hours > 0 ? (value / hours).ToString("N0") : "—";

    private void Reading(string key, string text)
    {
        if (readings.TryGetValue(key, out var target) && target.Text != text)
        {
            target.Text = text;
        }
    }

    private void Metrics(LinearLayout card, string[] names)
    {
        for (var first = 0; first < names.Length; first += 4)
        {
            var row = new LinearLayout(activity);
            card.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(5) });
            for (var index = first; index < Math.Min(names.Length, first + 4); index++)
            {
                var cell = Column();
                cell.Background = Plate();
                cell.SetPadding(Dp(6), Dp(5), Dp(6), Dp(5));
                cell.AddView(VulkanPanel.Body(activity, names[index].ToUpperInvariant(), 9, VulkanPanel.Muted));
                var number = VulkanPanel.Heading(activity, "—", 13);
                cell.AddView(number);
                readings.Add(names[index], number);
                row.AddView(cell, new LinearLayout.LayoutParams(0, -1, 1) { RightMargin = Dp(3) });
            }
        }
    }

    private void InventoryChoice(LinearLayout card, string title, string key,
        Func<PristonTale.Simulation.Characters.OwnedItemView, bool> predicate, Action<string> set)
    {
        var available = session.Owner?.Items.Where(item => (item.InBag || item.BeltBox >= 0) && predicate(item))
            .DistinctBy(item => item.CatalogKey).ToArray() ?? [];
        var keys = new[] { "" }.Concat(available.Select(item => item.CatalogKey)).ToArray();
        var labels = new[] { "Automática" }.Concat(available.Select(item => item.Name)).ToArray();
        if (key.Length > 0 && !keys.Contains(key))
        {
            keys = [.. keys, key];
            labels = [.. labels, key];
        }

        Choice(card, title, labels, Array.IndexOf(keys, key), v => set(keys[v]));
    }

    private void Change(Func<FreeHuntSettings, FreeHuntSettings> change) => session.HuntSettings = change(Settings);

    private LinearLayout Card(string title)
    {
        var card = Column();
        card.Background = Plate();
        card.SetPadding(Dp(10), Dp(6), Dp(10), Dp(8));
        card.AddView(VulkanPanelArt.Section(activity, title));
        page.AddView(card, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(6) });
        return card;
    }

    private LinearLayout Row(LinearLayout card, string label)
    {
        var row = new LinearLayout(activity);
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(VulkanPanel.Body(activity, label, 13), new LinearLayout.LayoutParams(0, -2, 1) { RightMargin = Dp(8) });
        card.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(4) });
        return row;
    }

    private void Check(LinearLayout card, string label, bool value, Action<bool> set)
    {
        var control = new Switch(activity) { Checked = value, ContentDescription = label };
        control.ThumbTintList = ButtonColors(Gold, Color.Gray);
        control.TrackTintList = ButtonColors(Color.Rgb(61, 157, 86), Color.Rgb(79, 67, 51));
        var row = Row(card, label);
        row.AddView(control, new LinearLayout.LayoutParams(Dp(48), Dp(40)));
        row.Click += (_, _) => control.Checked = !control.Checked;
        control.CheckedChange += (_, e) => set(e.IsChecked);
    }

    private static ColorStateList ButtonColors(Color selected, Color unselected) =>
        new([[Android.Resource.Attribute.StateChecked], []], [selected.ToArgb(), unselected.ToArgb()]);

    private void Number(LinearLayout card, string label, int value, int min, int max, Action<int> set)
    {
        var row = Row(card, label);
        var input = Editor(value.ToString(), "", InputTypes.ClassNumber);
        row.AddView(input, new LinearLayout.LayoutParams(Dp(92), Dp(40)));
        input.TextChanged += (_, _) =>
        {
            if (int.TryParse(input.Text, out var number))
            {
                set(Math.Clamp(number, min, max));
            }
        };
    }

    private void Text(LinearLayout card, string label, string value, string hint, Action<string> set)
    {
        var row = Row(card, label);
        var input = Editor(value, hint, InputTypes.ClassText);
        row.AddView(input, new LinearLayout.LayoutParams(0, Dp(40), 2));
        input.TextChanged += (_, _) => set(input.Text ?? "");
    }

    private EditText Editor(string value, string hint, InputTypes type)
    {
        var field = new EditText(activity) { Text = value, Hint = hint, TextSize = 13, InputType = type, Background = Plate() };
        field.SetSingleLine(true);
        field.SetTextColor(MobileHudTheme.Ink);
        field.SetHintTextColor(VulkanPanel.Muted);
        field.SetPadding(Dp(8), 0, Dp(8), 0);
        return field;
    }

    private void Choice(LinearLayout card, string label, string[] choices, int selected, Action<int> set)
    {
        var row = Row(card, label);
        var value = Button(choices[Math.Clamp(selected, 0, choices.Length - 1)] + "  ▾");
        row.AddView(value, new LinearLayout.LayoutParams(0, Dp(40), 2));
        value.Click += (_, _) =>
        {
            using var dialog = new AlertDialog.Builder(activity, Android.Resource.Style.ThemeMaterialDialogAlert);
            dialog.SetTitle(label);
            dialog.SetItems(choices, (_, e) => { set(e.Which); value.Text = choices[e.Which] + "  ▾"; });
            dialog.SetNegativeButton("Fechar", (_, _) => { });
            dialog.Show();
        };
    }

    private void Range(LinearLayout card, string label, int value, int min, int max, Action<int> set)
    {
        var row = Row(card, label);
        var scale = FreeHuntSettings.UnitsPerStep;
        var output = Button(value.ToString());
        var slider = new SeekBar(activity) { Max = (max - min) / scale, Progress = Math.Clamp((value - min) / scale, 0, (max - min) / scale),
            ProgressTintList = ColorStateList.ValueOf(Gold), ThumbTintList = ColorStateList.ValueOf(Gold), ContentDescription = label };
        row.AddView(slider, new LinearLayout.LayoutParams(0, Dp(44), 2));
        row.AddView(output, new LinearLayout.LayoutParams(Dp(62), Dp(39)));
        slider.ProgressChanged += (_, args) =>
        {
            var next = min + args.Progress * scale;
            output.Text = next.ToString();
            set(next);
        };
    }

    private TextView Button(string text, bool selected = false)
    {
        var label = VulkanPanel.Text(activity, text, 12, MobileHudTheme.Ink, serif: false);
        label.Gravity = GravityFlags.Center;
        label.Background = Plate(selected ? Blue : null);
        label.Clickable = true;
        label.SetPadding(Dp(5), 0, Dp(5), 0);
        return label;
    }

    private void Action(LinearLayout place, string title, Action invoke)
    {
        var button = Button(title);
        var width = place.Orientation == Orientation.Horizontal ? 0 : -1;
        place.AddView(button, new LinearLayout.LayoutParams(width, Dp(41), width == 0 ? 1 : 0) { TopMargin = Dp(4), RightMargin = Dp(4) });
        button.Click += (_, _) => invoke();
    }

    private void Note(LinearLayout card, string text)
    {
        var note = VulkanPanel.Body(activity, text, 11, VulkanPanel.Muted);
        card.AddView(note, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(4) });
    }

    private void ConfirmReset()
    {
        using var dialog = new AlertDialog.Builder(activity, Android.Resource.Style.ThemeMaterialDialogAlert);
        dialog.SetTitle("Restaurar padrão");
        dialog.SetMessage("Restaurar a configuração de todas as abas?");
        dialog.SetPositiveButton("Restaurar", (_, _) => { session.HuntSettings = new FreeHuntSettings(); Show(tab); });
        dialog.SetNegativeButton("Cancelar", (_, _) => { });
        dialog.Show();
    }

    private GradientDrawable Plate(Color? light = null)
    {
        var green = light == Green;
        var shape = new GradientDrawable(GradientDrawable.Orientation.TopBottom,
            green ? [Color.Rgb(63, 127, 62).ToArgb(), Color.Rgb(26, 64, 36).ToArgb()]
                : [Color.Rgb(41, 34, 24).ToArgb(), Color.Rgb(16, 14, 11).ToArgb()]);
        shape.SetCornerRadius(Dp(5));
        shape.SetStroke(Dp(light.HasValue ? 2 : 1), light ?? Color.Rgb(112, 88, 53));
        return shape;
    }

    private LinearLayout Column() => new(activity) { Orientation = Orientation.Vertical };
    private int Dp(int value) => (int)MathF.Round(value * (activity.Resources?.DisplayMetrics?.Density ?? 1f));
}
