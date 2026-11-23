import { table } from './page-language.js';
import { chip, element, format, pictureAddress, section } from './touch-dom.js';
// The touch HUD's free auto-hunt, the Android client's panel: eight tabs on the left (Caça, Habilidades, Poções,
// Segurança, Coleta, Ganhos, Grupo, Log), a bar with the state, Iniciar or Pausar and the reset of every option, and the
// tab's cards. The game runs the hunt (FreeHuntSession) and hands out its state (BrowserHost.TouchHunt); the panel edits
// a copy of the options and sends it back whole (SetFreeHuntSettings), its buttons as touch-hunt:<action>. The loot
// catalog (auto-hunt-items.tsv) is classified by the game once (ClassifyHuntItems).

const labels = {
    'pt-BR': {
        title: 'Auto-caça', chip: 'AUTO-CAÇA', on: 'Ligado', off: 'Desligado', paused: 'Pausa manual', start: '▶  INICIAR', pause: 'Ⅱ  PAUSAR',
        reset: 'Restaurar todas as configurações de Auto-caça', 'reset-confirm': 'Restaurar a configuração de todas as abas?', close: 'Fechar',
        unavailable: 'Disponível fora da cidade, com o personagem vivo.', confirm: 'Confirmar', cancel: 'Cancelar',
        'tab-hunt': 'Caça', 'tab-skills': 'Habilidades', 'tab-potions': 'Poções', 'tab-safety': 'Segurança', 'tab-loot': 'Coleta',
        'tab-gains': 'Ganhos', 'tab-group': 'Grupo', 'tab-log': 'Log',
        area: 'Área', radius: 'Raio de caça', 'radius-note': '64 = 1 passo de chão. A caça começa onde você ligar.',
        'return-start': 'Voltar ao ponto inicial quando não houver alvo', relocate: 'Mover o ponto de caça quando travar',
        'relocate-0': 'Manter o ponto inicial', 'relocate-1': 'Automático', 'break-return': 'Parar de atacar para voltar ao ponto inicial',
        'set-origin': 'Definir ponto inicial aqui', monsters: 'Monstros', only: 'Só caçar', 'only-hint': 'vazio = todos (ex.: Hopy, Rabie)',
        ignore: 'Ignorar', 'ignore-hint': 'partes do nome, separadas por vírgula', 'min-level': 'Nível mínimo', 'max-level': 'Nível máximo',
        strict: 'Aplicar filtros mesmo a quem me atacar', 'avoid-claimed': 'Não roubar monstros de outros jogadores',
        'party-targets': 'Incluir monstros que membros do meu grupo estão atacando', 'prioritize-quest': 'Priorizar monstros de quest',
        'keep-target': 'Manter o alvo até ele morrer', ranged: 'Focar monstros de longe', boss: 'Priorizar o chefe',
        pursuit: 'Perseguir até (% além do raio)', unreachable: 'Alvo inalcançável', timeout: 'Desistir após (s sem progresso)',
        'ignore-seconds': 'Ignorar por (s)', panel: 'Painel', 'show-status': 'Mostrar o quadro de status durante a caça', account: 'Conta',
        'share-account': 'Usar a mesma configuração para a conta toda',
        'share-note': 'Os personagens da mesma conta e servidor neste navegador compartilham as opções. A lista de habilidades é por personagem.',
        profile: 'Copiar perfil para outro aparelho', 'profile-note': 'O código leva as opções da caça e a ordem das habilidades. Importe no aparelho de destino.',
        'copy-code': 'Copiar código', 'import-code': 'Importar código', 'paste-code': 'Cole o código do perfil', import: 'Importar',
        copied: 'Código do perfil copiado.', 'copy-failed': 'Copie o código abaixo.', 'too-long': 'Reduza os filtros de texto antes de copiar.',
        'invalid-code': 'Código inválido ou incompatível com esta versão.',
        'import-share': 'Substitui as opções compartilhadas da conta neste navegador e pausa a caça para você revisar.',
        'import-own': 'Substitui as opções do personagem e pausa a caça para você revisar.',
        used: 'Habilidades usadas', 'skill-rank': '{0} · Nv. {1}', 'mana-floor': 'MP mínimo (%) · {0}', up: '↑ Subir', down: '↓ Descer',
        'no-skills': 'Nenhuma habilidade ativa aprendida ainda.', attack: 'Ataque', basic: 'Ataque normal entre as habilidades',
        'area-range': 'Alcance das habilidades em área', 'area-skill': 'Skill de área', 'prefer-area': 'Priorizar skill de área',
        'area-min': 'Mínimo de monstros', 'area-radius': 'Raio (0 = área da habilidade)', swap: 'Troca de arma',
        'swap-shield': 'Trocar para o escudo para buffar', crystal: 'Cristal de invocação', 'use-crystals': 'Soltar cristal automaticamente',
        'crystal-item': 'Cristal', 'summon-count': 'Manter invocados', 'in-combat': 'Só durante a luta', companion: 'Alvo do companheiro',
        'companion-0': 'Por conta própria', 'companion-1': 'Meu alvo', 'companion-2': 'Defender personagem',
        'companion-note': 'O modo vale para monstros de cristal enquanto a Auto-caça estiver ligada.', force: 'Force Orb',
        'use-force': 'Usar Force automaticamente', 'force-item': 'Force', 'weak-first': 'Usar da mais fraca para a mais forte',
        premiums: 'Premiums', 'use-premiums': 'Usar premiums automaticamente', automatic: 'Automática',
        hp: 'HP (vida)', mp: 'MP (mana)', res: 'RES (estamina)', 'use-potion': 'Usar poção automaticamente', below: 'Quando abaixo de (%)',
        preferred: 'Poção preferida', 'adaptive-title': 'Poção adaptativa', adaptive: 'Adaptativo',
        'adaptive-note': 'Após dano intenso, antecipa a poção de HP. MP e RES usam o limite configurado.', belt: 'Cinto de poções',
        'refill-belt': 'Repor o cinto com poções da bolsa', rhythm: 'Ritmo', 'same-gap': 'Intervalo entre poções do mesmo tipo (ms)',
        'any-gap': 'Entre quaisquer poções (ms)', 'rhythm-note': 'Com dois recursos baixos, o menor percentual vai primeiro. Usa poções do cinto e da bolsa.',
        critical: 'HP crítico', 'critical-hp': 'HP crítico abaixo de (%)', 'no-potion': 'Sem poção', supply: 'Acabaram as poções',
        'use-core': 'Usar Core quando acabar as poções', 'no-hp': 'Sem poções de HP', 'full-bag': 'Bolsa cheia', action: 'Ação',
        'bag-note': 'Quando a coleta é recusada, Continuar limita a coleta a ouro por um minuto.', death: 'Morte',
        'town-death': 'Ir para a cidade ao morrer', 'town-deaths': 'Voltar para a cidade após 3 mortes em 10 min',
        'town-scrolls': 'Voltar para a cidade com poucos Pergaminhos de Renascimento', 'stop-party': 'Parar a caçada após ficar sem grupo',
        'death-note': 'Com o retorno desligado, aguarda ressurreição ou sua escolha no quadro.', manual: 'Controle manual',
        'pause-manual': 'Pausar quando eu tocar / usar habilidades', resume: 'Retomar após (ms sem comandos)',
        'safety-0': 'Continuar', 'safety-1': 'Pausar', 'safety-2': 'Voltar à cidade (Core) e parar',
        'loot-auto': 'Coleta automática', loot: 'Pegar itens do chão', 'loot-mode': 'O que pegar', 'mode-0': 'Tudo, com o filtro abaixo',
        'mode-1': 'Só ouro', 'mode-2': 'Ouro e poções', 'mode-3': 'Só a lista Sempre pegar', categories: 'Categorias',
        supplies: 'Itens e suprimentos', equipment: 'Equipamentos', 'mark-all': 'Marcar todas', 'unmark-all': 'Desmarcar todas',
        'categories-note': 'Itens de missão têm proteção própria. Nunca pegar e as cotas de poções têm prioridade sobre Sempre pegar.',
        'cat-Gold': 'Ouro', 'cat-Potions': 'Poções', 'cat-Sheltoms': 'Sheltoms', 'cat-Force': 'Force Orbs', 'cat-Crystals': 'Cristais de invocação',
        'cat-Materials': 'Materiais de fabricação', 'cat-Runes': 'Runas', 'cat-Travel': 'Núcleos de viagem', 'cat-Premium': 'Benefícios Premium',
        'cat-Castle': 'Itens de castelo', 'cat-Masteries': 'Mestres de skill e aging', 'cat-Events': 'Consumíveis de evento',
        'cat-OtherUsable': 'Outros consumíveis', 'cat-Weapons': 'Armas', 'cat-Armor': 'Armaduras e robes', 'cat-Shields': 'Escudos',
        'cat-Boots': 'Botas', 'cat-Gloves': 'Luvas', 'cat-Bracelets': 'Braceletes e orbes', 'cat-Amulets': 'Amuletos', 'cat-Rings': 'Anéis',
        'cat-Other': 'Outros itens', 'cat-Quest': 'Itens de missão', 'potions-card': 'Poções', 'stop-hp': 'Parar de coletar HP com',
        'stop-mp': 'Parar de coletar MP com', 'stop-res': 'Parar de coletar RES com', all: 'Todas',
        'potions-note': 'Conta Bolsa e Cinto. Pilhas que excederiam a cota ficam no chão. 0 = sem limite.', always: 'Sempre pegar',
        never: 'Nunca pegar', 'add-category': 'Adicionar categoria inteira', search: 'Buscar item pelo nome ou código...',
        'search-in': 'Buscar na categoria', contains: 'Nome ou código contém “{0}”', 'add-rule': 'Adicionar regra com “{0}”',
        previous: '‹ Anteriores', more: 'Mais resultados ›', 'search-hint': 'Digite um nome ou escolha uma categoria para explorar o catálogo.',
        'no-results': 'Nenhum item disponível para esta busca.', 'group-HP': 'Poções de vida', 'group-MP': 'Poções de mana',
        'group-RES': 'Poções de estamina', levels: 'Nível dos equipamentos', 'level-category': 'Categoria', 'level-all': 'Todos',
        from: 'A partir do nível (≥)', to: 'Até o nível (≤)', spec: 'Spec', 'spec-first': 'Priorizar itens da minha spec',
        'spec-only': 'Só equipamentos da minha spec', reach: 'Alcance', distance: 'Distância máxima', 'ignore-own': 'Não pegar o que eu larguei',
        'return-loot': 'Voltar ao ponto inicial depois de coletar', 'loot-break': 'Pausa para coletar',
        'loot-after': 'Parar de atacar após (s · 0 desliga)', default: 'Padrão', 'restore-default': 'Restaurar padrão',
        session: 'Sessão', rest: 'Descanso', 'resume-stats': 'Retomar', zero: 'Zerar', count: 'Contar', 'count-0': 'Só com a auto-caça ligada',
        'count-1': 'Sempre', 'session-time': 'Sessão {0}m{1}s', experience: 'Experiência', 'exp-hour': 'EXP por hora',
        'exp-recent': 'EXP/h (últimos 10 min)', 'exp-gained': 'EXP ganha', progress: 'Progresso atual', 'exp-lost': 'Perdida (mortes)',
        level: 'Nível atual', 'levels-gained': 'Níveis ganhos', 'next-level': 'Próximo nível em', hours: '{0} h', wallet: 'Balanço de ouro',
        balance: 'Saldo da sessão', 'balance-hour': 'Saldo por hora', income: 'Entradas', spending: 'Saídas', ground: 'Coletado do chão',
        'wallet-note': 'Entradas e saídas são variações do saldo da bolsa. O ouro do chão conta coletas confirmadas.', kills: 'Abates',
        killed: 'Monstros', 'kills-hour': 'Abates por hora', 'kills-recent': 'Abates (últimos 10 min)', deaths: 'Mortes', damage: 'Dano total',
        dps: 'DPS (30 s)', 'potions-used': 'Poções usadas', 'hp-short': 'HP', 'mp-short': 'MP', 'res-short': 'RES', collected: 'Itens coletados',
        units: 'Unidades', kinds: 'Tipos de item', 'no-items': 'Nenhum item coletado ainda.',
        'group-dps': 'DPS do grupo (30 s)', 'group-damage': 'Dano do grupo', 'group-kills': 'Abates do grupo', 'group-exp': 'EXP do grupo',
        'group-deaths': 'Mortes no grupo', members: 'Membros', 'no-members': 'Sem membros registrados nesta sessão.', 'member-dps': '{0} DPS',
        'member-left': 'Saiu do grupo', 'member-line': '{0} de dano · {1} abates · {2} EXP · {3} mortes',
        happened: 'O que aconteceu', show: 'Mostrar', 'log-0': 'Todos', 'log-1': 'Caça e segurança', 'log-2': 'Mortes', 'log-3': 'Grupo',
        'search-log': 'Buscar', 'search-log-hint': 'Ex.: perfil, cidade, nome', 'log-count': '{0} de {1} registros', 'clear-log': 'Limpar histórico',
        'clear-confirm': 'Apagar os registros de Auto-caça deste personagem?', 'no-log': 'Nenhum registro para esse filtro.',
        'log-Started': 'Auto-caça iniciada', 'log-Stopped': 'Auto-caça desligada · {0}', 'log-Death': 'Personagem morreu',
        'log-Death-by': 'Personagem morreu · {0}', 'log-Joined': '{0} entrou no grupo', 'log-Left': '{0} saiu do grupo',
        'reason-Manual': 'Comando manual', 'reason-MapChange': 'Mudança de mapa ou missão automática', 'reason-NoParty': 'Sem grupo',
        'reason-FullBag': 'Bolsa cheia', 'reason-NoLifePotion': 'Sem poção de vida', 'reason-PickupRefused': 'Coleta recusada',
        'reason-TownAfterDeath': 'Volta à cidade após morrer', 'reason-ProfileImported': 'Perfil importado',
        'reason-Interface': 'HUD de PC ou página em segundo plano'
    },
    en: {
        title: 'Auto-hunt', chip: 'AUTO-HUNT', on: 'On', off: 'Off', paused: 'Manual pause', start: '▶  START', pause: 'Ⅱ  PAUSE',
        reset: 'Restore every Auto-hunt option', 'reset-confirm': 'Restore the options of every tab?', close: 'Close',
        unavailable: 'Available out of town, with the character alive.', confirm: 'Confirm', cancel: 'Cancel',
        'tab-hunt': 'Hunt', 'tab-skills': 'Skills', 'tab-potions': 'Potions', 'tab-safety': 'Safety', 'tab-loot': 'Loot',
        'tab-gains': 'Gains', 'tab-group': 'Party', 'tab-log': 'Log',
        area: 'Area', radius: 'Hunting radius', 'radius-note': '64 = 1 ground step. The hunt starts where you switch it on.',
        'return-start': 'Walk back to the start when there is no target', relocate: 'Move the hunting spot when stuck',
        'relocate-0': 'Keep the start', 'relocate-1': 'Automatic', 'break-return': 'Stop attacking to walk back to the start',
        'set-origin': 'Set the start here', monsters: 'Monsters', only: 'Hunt only', 'only-hint': 'empty = all (e.g. Hopy, Rabie)',
        ignore: 'Ignore', 'ignore-hint': 'parts of the name, separated by commas', 'min-level': 'Minimum level', 'max-level': 'Maximum level',
        strict: 'Apply the filters even to those attacking me', 'avoid-claimed': "Don't steal other players' monsters",
        'party-targets': 'Include monsters my party members are attacking', 'prioritize-quest': 'Prioritize quest monsters',
        'keep-target': 'Keep the target until it dies', ranged: 'Focus monsters attacking from afar', boss: 'Prioritize the boss',
        pursuit: 'Chase up to (% past the radius)', unreachable: 'Unreachable target', timeout: 'Give up after (s without progress)',
        'ignore-seconds': 'Ignore for (s)', panel: 'Panel', 'show-status': 'Show the status board while hunting', account: 'Account',
        'share-account': 'Use the same options for the whole account',
        'share-note': "The account's characters on the same server in this browser share the options. The skill list is per character.",
        profile: 'Copy the profile to another device', 'profile-note': 'The code carries the hunt options and the skill order. Import it on the other device.',
        'copy-code': 'Copy code', 'import-code': 'Import code', 'paste-code': 'Paste the profile code', import: 'Import',
        copied: 'Profile code copied.', 'copy-failed': 'Copy the code below.', 'too-long': 'Shorten the text filters before copying.',
        'invalid-code': 'Invalid code, or not for this version.',
        'import-share': "Replaces the account's shared options in this browser and pauses the hunt for you to review.",
        'import-own': "Replaces the character's options and pauses the hunt for you to review.",
        used: 'Skills used', 'skill-rank': '{0} · Lv. {1}', 'mana-floor': 'Minimum MP (%) · {0}', up: '↑ Up', down: '↓ Down',
        'no-skills': 'No active skill learned yet.', attack: 'Attack', basic: 'Normal attack between the skills',
        'area-range': 'Range of area skills', 'area-skill': 'Area skill', 'prefer-area': 'Prioritize the area skill',
        'area-min': 'Minimum monsters', 'area-radius': "Radius (0 = the skill's area)", swap: 'Weapon swap',
        'swap-shield': 'Swap to the shield to buff', crystal: 'Summon crystal', 'use-crystals': 'Release a crystal automatically',
        'crystal-item': 'Crystal', 'summon-count': 'Keep summoned', 'in-combat': 'Only while fighting', companion: "Companion's target",
        'companion-0': 'On its own', 'companion-1': 'My target', 'companion-2': 'Defend the character',
        'companion-note': 'The mode applies to crystal monsters while Auto-hunt is on.', force: 'Force Orb',
        'use-force': 'Use Force automatically', 'force-item': 'Force', 'weak-first': 'Use from the weakest to the strongest',
        premiums: 'Premiums', 'use-premiums': 'Use premiums automatically', automatic: 'Automatic',
        hp: 'HP (life)', mp: 'MP (mana)', res: 'RES (stamina)', 'use-potion': 'Use potions automatically', below: 'When below (%)',
        preferred: 'Preferred potion', 'adaptive-title': 'Adaptive potion', adaptive: 'Adaptive',
        'adaptive-note': 'After heavy damage, the HP potion comes earlier. MP and RES use their set threshold.', belt: 'Potion belt',
        'refill-belt': 'Refill the belt with potions from the bag', rhythm: 'Rhythm', 'same-gap': 'Gap between potions of the same kind (ms)',
        'any-gap': 'Between any potions (ms)', 'rhythm-note': 'With two resources low, the lowest share goes first. Uses potions from the belt and the bag.',
        critical: 'Critical HP', 'critical-hp': 'Critical HP below (%)', 'no-potion': 'Without potions', supply: 'Out of potions',
        'use-core': 'Use a Core when the potions run out', 'no-hp': 'Without HP potions', 'full-bag': 'Full bag', action: 'Action',
        'bag-note': 'When a pickup is refused, Continue picks up only gold for a minute.', death: 'Death',
        'town-death': 'Go to town on dying', 'town-deaths': 'Go back to town after 3 deaths in 10 min',
        'town-scrolls': 'Go back to town with few Resurrection Scrolls', 'stop-party': 'Stop the hunt once out of a party',
        'death-note': 'With the return off, it waits for a resurrection or your choice on the board.', manual: 'Manual control',
        'pause-manual': 'Pause when I tap / use skills', resume: 'Resume after (ms without commands)',
        'safety-0': 'Continue', 'safety-1': 'Pause', 'safety-2': 'Return to town (Core) and stop',
        'loot-auto': 'Automatic loot', loot: 'Pick up items from the ground', 'loot-mode': 'What to pick up', 'mode-0': 'Everything, with the filter below',
        'mode-1': 'Gold only', 'mode-2': 'Gold and potions', 'mode-3': 'Only the Always pick up list', categories: 'Categories',
        supplies: 'Items and supplies', equipment: 'Equipment', 'mark-all': 'Mark all', 'unmark-all': 'Unmark all',
        'categories-note': 'Quest items have their own protection. Never pick up and the potion quotas come before Always pick up.',
        'cat-Gold': 'Gold', 'cat-Potions': 'Potions', 'cat-Sheltoms': 'Sheltoms', 'cat-Force': 'Force Orbs', 'cat-Crystals': 'Summon crystals',
        'cat-Materials': 'Crafting materials', 'cat-Runes': 'Runes', 'cat-Travel': 'Travel cores', 'cat-Premium': 'Premium benefits',
        'cat-Castle': 'Castle items', 'cat-Masteries': 'Skill and aging masters', 'cat-Events': 'Event consumables',
        'cat-OtherUsable': 'Other consumables', 'cat-Weapons': 'Weapons', 'cat-Armor': 'Armor and robes', 'cat-Shields': 'Shields',
        'cat-Boots': 'Boots', 'cat-Gloves': 'Gloves', 'cat-Bracelets': 'Bracelets and orbs', 'cat-Amulets': 'Amulets', 'cat-Rings': 'Rings',
        'cat-Other': 'Other items', 'cat-Quest': 'Quest items', 'potions-card': 'Potions', 'stop-hp': 'Stop picking up HP at',
        'stop-mp': 'Stop picking up MP at', 'stop-res': 'Stop picking up RES at', all: 'All',
        'potions-note': 'Counts the bag and the belt. Stacks that would pass the quota stay on the ground. 0 = no limit.', always: 'Always pick up',
        never: 'Never pick up', 'add-category': 'Add a whole category', search: 'Search an item by name or code...',
        'search-in': 'Search in the category', contains: 'Name or code contains “{0}”', 'add-rule': 'Add a rule with “{0}”',
        previous: '‹ Previous', more: 'More results ›', 'search-hint': 'Type a name or choose a category to explore the catalog.',
        'no-results': 'No item for this search.', 'group-HP': 'Life potions', 'group-MP': 'Mana potions',
        'group-RES': 'Stamina potions', levels: 'Equipment level', 'level-category': 'Category', 'level-all': 'All',
        from: 'From level (≥)', to: 'Up to level (≤)', spec: 'Spec', 'spec-first': 'Prioritize items of my spec',
        'spec-only': 'Only equipment of my spec', reach: 'Reach', distance: 'Greatest distance', 'ignore-own': "Don't pick up what I dropped",
        'return-loot': 'Walk back to the start after picking up', 'loot-break': 'Break to pick up',
        'loot-after': 'Stop attacking after (s · 0 turns it off)', default: 'Default', 'restore-default': 'Restore the default',
        session: 'Session', rest: 'Rest', 'resume-stats': 'Resume', zero: 'Reset', count: 'Count', 'count-0': 'Only with auto-hunt on',
        'count-1': 'Always', 'session-time': 'Session {0}m{1}s', experience: 'Experience', 'exp-hour': 'EXP per hour',
        'exp-recent': 'EXP/h (last 10 min)', 'exp-gained': 'EXP gained', progress: 'Current progress', 'exp-lost': 'Lost (deaths)',
        level: 'Current level', 'levels-gained': 'Levels gained', 'next-level': 'Next level in', hours: '{0} h', wallet: 'Gold balance',
        balance: 'Session balance', 'balance-hour': 'Balance per hour', income: 'Income', spending: 'Spending', ground: 'Picked up from the ground',
        'wallet-note': "Income and spending are changes of the bag's balance. The ground's gold counts confirmed pickups.", kills: 'Kills',
        killed: 'Monsters', 'kills-hour': 'Kills per hour', 'kills-recent': 'Kills (last 10 min)', deaths: 'Deaths', damage: 'Total damage',
        dps: 'DPS (30 s)', 'potions-used': 'Potions used', 'hp-short': 'HP', 'mp-short': 'MP', 'res-short': 'RES', collected: 'Items picked up',
        units: 'Units', kinds: 'Kinds of item', 'no-items': 'No item picked up yet.',
        'group-dps': 'Party DPS (30 s)', 'group-damage': 'Party damage', 'group-kills': 'Party kills', 'group-exp': 'Party EXP',
        'group-deaths': 'Party deaths', members: 'Members', 'no-members': 'No member recorded this session.', 'member-dps': '{0} DPS',
        'member-left': 'Left the party', 'member-line': '{0} damage · {1} kills · {2} EXP · {3} deaths',
        happened: 'What happened', show: 'Show', 'log-0': 'All', 'log-1': 'Hunt and safety', 'log-2': 'Deaths', 'log-3': 'Party',
        'search-log': 'Search', 'search-log-hint': 'E.g. profile, town, name', 'log-count': '{0} of {1} entries', 'clear-log': 'Clear the history',
        'clear-confirm': "Erase this character's Auto-hunt entries?", 'no-log': 'No entry for this filter.',
        'log-Started': 'Auto-hunt started', 'log-Stopped': 'Auto-hunt off · {0}', 'log-Death': 'Character died',
        'log-Death-by': 'Character died · {0}', 'log-Joined': '{0} joined the party', 'log-Left': '{0} left the party',
        'reason-Manual': 'Manual command', 'reason-MapChange': 'Map change or automatic quest', 'reason-NoParty': 'No party',
        'reason-FullBag': 'Full bag', 'reason-NoLifePotion': 'No life potion', 'reason-PickupRefused': 'Pickup refused',
        'reason-TownAfterDeath': 'Back to town after dying', 'reason-ProfileImported': 'Profile imported',
        'reason-Interface': 'PC HUD or page in the background'
    }
};

const tabs = ['hunt', 'skills', 'potions', 'safety', 'loot', 'gains', 'group', 'log'];
const tabIcons = ['◎', '✦', '◈', '⛨', '⬡', '↗', '♟', '▤'];
// The loot categories in the panel's order (FreeHuntSettings' LootHuntCategory flags), the equipment ones last.
export const lootCategories = [
    ['Gold', 1], ['Potions', 8192], ['Sheltoms', 512], ['Force', 1024], ['Crystals', 2048], ['Materials', 4096], ['Runes', 65536],
    ['Travel', 131072], ['Premium', 262144], ['Castle', 524288], ['Masteries', 1048576], ['Events', 2097152], ['OtherUsable', 4194304],
    ['Weapons', 2], ['Armor', 4], ['Shields', 8], ['Boots', 16], ['Gloves', 32], ['Bracelets', 64], ['Amulets', 128], ['Rings', 256], ['Other', 16384]
];
const equipmentMask = 2 | 4 | 8 | 16 | 32 | 64 | 128 | 256;
const allCategories = lootCategories.reduce((mask, [, flag]) => mask | flag, 0);
const unitsPerStep = 64;
const maximumRadius = 30 * unitsPerStep;
const petSlots = 10;
const pageSize = 6;
const pollMs = 500;

// A log entry worded: what happened, why it stopped, whom it concerns.
export function logText(entry, say) {
    if (entry.kind === 'Stopped') return format(say('log-Stopped'), say('reason-' + entry.reason) ?? entry.reason);
    if (entry.kind === 'Death') return entry.name ? format(say('log-Death-by'), entry.name) : say('log-Death');
    if (entry.kind === 'Joined' || entry.kind === 'Left') return format(say('log-' + entry.kind), entry.name);
    return say('log-' + entry.kind) ?? entry.kind;
}

// The log's filters: every entry, the hunt's starts and stops, deaths, the party's comings and goings.
export function logMatches(entry, category) {
    switch (category) {
        case 1: return entry.kind === 'Started' || entry.kind === 'Stopped';
        case 2: return entry.kind === 'Death';
        case 3: return entry.kind === 'Joined' || entry.kind === 'Left';
        default: return true;
    }
}

// A comma list of loot terms with one added or taken away, case aside.
export function addTerm(terms, term) {
    const list = splitTerms(terms);
    return list.some(entry => entry.toLowerCase() === term.toLowerCase()) ? list.join(',') : [...list, term].join(',');
}

export function removeTerm(terms, term) {
    return splitTerms(terms).filter(entry => entry.toLowerCase() !== term.toLowerCase()).join(',');
}

export const splitTerms = terms => (terms ?? '').split(',').map(entry => entry.trim()).filter(Boolean);

// The catalog's items a search shows: in the category (0 for all), whose code or name holds the query, not chosen yet.
export function searchCatalog(catalog, query, category, chosen) {
    const text = query.trim().toLocaleLowerCase();
    const taken = new Set(splitTerms(chosen).map(entry => entry.toLowerCase()));
    const plain = value => value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLocaleLowerCase();
    return catalog.filter(item => (!category || item.category === category)
        && (!text || item.code.toLowerCase().includes(text) || plain(item.name).includes(plain(text)))
        && !taken.has('#' + item.code.toLowerCase()));
}

// The Ganhos tab's figures from the totals and the owner's level, in the player's number format.
export function gainReadings(totals, owner, say, language) {
    const number = value => new Intl.NumberFormat(language).format(value);
    const decimal = value => new Intl.NumberFormat(language, { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value);
    const hours = totals.Seconds / 3600;
    const rate = value => (hours > 0 ? number(Math.round(value / hours)) : '—');
    const remaining = owner.nextLevel > owner.experience ? owner.nextLevel - owner.experience : 0;
    const span = owner.nextLevel > owner.levelStart ? owner.nextLevel - owner.levelStart : 0;
    const progress = owner.experience > owner.levelStart ? owner.experience - owner.levelStart : 0;
    return {
        'session-time': format(say('session-time'), Math.floor(totals.Seconds / 60), String(totals.Seconds % 60).padStart(2, '0')),
        'exp-hour': rate(totals.Experience), 'exp-recent': totals.RecentExperiencePerHour > 0 ? number(Math.round(totals.RecentExperiencePerHour)) : '—',
        'exp-gained': number(totals.Experience), progress: span > 0 ? decimal(Math.min(progress, span) * 100 / span) + '%' : '—',
        'exp-lost': number(totals.ExperienceLost), level: owner.level ? String(owner.level) : '—', 'levels-gained': number(totals.Levels),
        'next-level': remaining > 0 && totals.Experience > 0 && hours > 0 ? format(say('hours'), decimal(remaining * hours / totals.Experience)) : '—',
        balance: number(totals.Gold - totals.GoldSpent), 'balance-hour': rate(totals.Gold - totals.GoldSpent), income: number(totals.Gold),
        spending: number(totals.GoldSpent), ground: number(totals.DroppedGold), killed: number(totals.Kills), 'kills-hour': rate(totals.Kills),
        'kills-recent': number(totals.RecentKills), deaths: number(totals.Deaths), damage: number(totals.Damage), dps: decimal(totals.RecentDamagePerSecond),
        'hp-short': number(totals.HP), 'mp-short': number(totals.MP), 'res-short': number(totals.RES), units: number(totals.Collected),
        kinds: number(totals.Items.length)
    };
}

export function createTouchHunt(layer, getHost, getLanguage, getPicture, persist) {
    const dialog = layer.querySelector('[data-role="touch-hunt"]');
    if (!dialog) return { open() {}, poll() {}, close() {}, translate() {} };
    const tabMenu = dialog.querySelector('[data-role="touch-hunt-tabs"]');
    const status = dialog.querySelector('[data-role="touch-hunt-status"]');
    const toggle = dialog.querySelector('[data-role="touch-hunt-toggle"]');
    const resetButton = dialog.querySelector('[data-role="touch-hunt-reset"]');
    const page = dialog.querySelector('[data-role="touch-hunt-page"]');
    let tab = 0;
    let state = null;
    let settings = null;
    let readings = {};
    let shownStatus = '';
    let logVersion = -1;
    let logEntries = [];
    let logCategory = 0;
    let logQuery = '';
    let lastPoll = 0;
    let catalog = null;
    let catalogLoading = null;

    const say = key => table(labels, 'hunt', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);
    const number = value => new Intl.NumberFormat(getLanguage()).format(value);

    function read(full) {
        try {
            const text = getHost()?.TouchHunt?.(full) || '';
            return text ? JSON.parse(text) : null;
        } catch {
            return null;
        }
    }

    // A change of the options goes to the game whole; the settings document is kept at once.
    function change(update) {
        const next = update(structuredClone(settings));
        if (getHost()?.SetFreeHuntSettings?.(JSON.stringify(next))) {
            settings = next;
            persist?.();
        }
    }

    function card(title) {
        const node = element('div', 'touch-hunt-card');
        node.append(section(title));
        page.append(node);
        return node;
    }

    function row(place, label) {
        const node = element('label', 'touch-hunt-row');
        node.append(element('span', 'touch-hunt-label', label));
        place.append(node);
        return node;
    }

    function checkText(place, label, value, set) {
        const input = element('input', 'touch-hunt-switch');
        input.type = 'checkbox';
        input.checked = Boolean(value);
        input.addEventListener('change', () => change(next => set(next, input.checked)));
        row(place, label).append(input);
    }

    const check = (place, key, value, set) => checkText(place, say(key), value, set);

    function numberField(place, label, value, minimum, maximum, set) {
        const input = element('input', 'touch-hunt-input touch-hunt-number');
        input.type = 'number';
        input.inputMode = 'numeric';
        input.min = String(minimum);
        input.max = String(maximum);
        input.value = String(value ?? 0);
        input.addEventListener('change', () => {
            const parsed = Number.parseInt(input.value, 10);
            const clamped = Number.isFinite(parsed) ? Math.min(maximum, Math.max(minimum, parsed)) : minimum;
            input.value = String(clamped);
            change(next => set(next, clamped));
        });
        row(place, label).append(input);
        return input;
    }

    const numeric = (place, key, value, minimum, maximum, set) => numberField(place, say(key), value, minimum, maximum, set);

    function text(place, key, value, hint, set) {
        const input = element('input', 'touch-hunt-input touch-hunt-text');
        input.type = 'text';
        input.value = value ?? '';
        input.placeholder = say(hint);
        input.addEventListener('change', () => change(next => set(next, input.value)));
        row(place, say(key)).append(input);
    }

    function choice(place, label, options, selected, set) {
        const select = element('select', 'touch-hunt-input touch-hunt-select');
        options.forEach((option, index) => {
            const node = element('option', '', option);
            node.value = String(index);
            select.append(node);
        });
        select.value = String(Math.max(0, Math.min(options.length - 1, selected)));
        select.addEventListener('change', () => set(Number(select.value)));
        row(place, label).append(select);
        return select;
    }

    function range(place, key, value, preview, set) {
        const slider = element('input', 'touch-hunt-range');
        slider.type = 'range';
        slider.min = String(unitsPerStep);
        slider.max = String(maximumRadius);
        slider.step = String(unitsPerStep);
        slider.value = String(value);
        const output = element('span', 'touch-hunt-range-value', String(value));
        slider.addEventListener('input', () => {
            output.textContent = slider.value;
            run(preview);
        });
        slider.addEventListener('change', () => change(next => set(next, Number(slider.value))));
        row(place, say(key)).append(slider, output);
    }

    function button(place, label, onClick, on = false) {
        const node = element('button', 'touch-bag-action touch-hunt-button', label);
        node.type = 'button';
        if (on) node.dataset.on = 'true';
        node.addEventListener('click', onClick);
        place.append(node);
        return node;
    }

    function actions(place) {
        const node = element('div', 'touch-hunt-actions');
        place.append(node);
        return node;
    }

    const note = (place, key) => place.append(element('p', 'touch-hunt-note', say(key)));

    // An item the bag offers for a choice (a crystal, a Force Orb, a potion), or Automática.
    function itemChoice(place, key, kind, value, set) {
        const items = (state?.items ?? []).filter(item => item.kind === kind);
        const keys = ['', ...items.map(item => item.key)];
        const names = [say('automatic'), ...items.map(item => item.name)];
        if (value && !keys.includes(value)) {
            keys.push(value);
            names.push(value);
        }
        choice(place, say(key), names, keys.indexOf(value ?? ''), index => change(next => set(next, keys[index])));
    }

    function confirm(question, then) {
        const box = card(question);
        const row = actions(box);
        button(row, say('confirm'), () => {
            box.remove();
            then();
        }, true);
        button(row, say('cancel'), () => box.remove());
        page.prepend(box);
        page.scrollTop = 0;
    }

    function huntPage() {
        const area = card(say('area'));
        range(area, 'radius', settings.Radius, 'touch-hunt:PreviewHunt', (next, value) => ({ ...next, Radius: value }));
        note(area, 'radius-note');
        check(area, 'return-start', settings.ReturnToStart, (next, value) => ({ ...next, ReturnToStart: value }));
        choice(area, say('relocate'), [say('relocate-0'), say('relocate-1')], settings.RelocateWhenBlocked ? 1 : 0,
            index => change(next => ({ ...next, RelocateWhenBlocked: index === 1 })));
        check(area, 'break-return', settings.BreakCombatToReturn, (next, value) => ({ ...next, BreakCombatToReturn: value }));
        button(actions(area), say('set-origin'), () => run('touch-hunt:Origin'));
        const monsters = card(say('monsters'));
        text(monsters, 'only', settings.OnlyMonsters, 'only-hint', (next, value) => ({ ...next, OnlyMonsters: value }));
        text(monsters, 'ignore', settings.IgnoredMonsters, 'ignore-hint', (next, value) => ({ ...next, IgnoredMonsters: value }));
        numeric(monsters, 'min-level', settings.MinimumLevel, 0, 500, (next, value) => ({ ...next, MinimumLevel: value }));
        numeric(monsters, 'max-level', settings.MaximumLevel, 0, 500, (next, value) => ({ ...next, MaximumLevel: value }));
        check(monsters, 'strict', settings.StrictMonsterFilter, (next, value) => ({ ...next, StrictMonsterFilter: value }));
        check(monsters, 'avoid-claimed', settings.AvoidClaimed, (next, value) => ({ ...next, AvoidClaimed: value }));
        check(monsters, 'party-targets', settings.IncludePartyTargets, (next, value) => ({ ...next, IncludePartyTargets: value }));
        check(monsters, 'prioritize-quest', settings.PrioritizeQuest, (next, value) => ({ ...next, PrioritizeQuest: value }));
        check(monsters, 'keep-target', settings.KeepTarget, (next, value) => ({ ...next, KeepTarget: value }));
        check(monsters, 'ranged', settings.PrioritizeRanged, (next, value) => ({ ...next, PrioritizeRanged: value }));
        check(monsters, 'boss', settings.PrioritizeBoss, (next, value) => ({ ...next, PrioritizeBoss: value }));
        numeric(monsters, 'pursuit', settings.PursuitPercent, 0, 120, (next, value) => ({ ...next, PursuitPercent: value }));
        const unreachable = card(say('unreachable'));
        numeric(unreachable, 'timeout', settings.TargetTimeout, 2, 90, (next, value) => ({ ...next, TargetTimeout: value }));
        numeric(unreachable, 'ignore-seconds', settings.IgnoreSeconds, 1, 300, (next, value) => ({ ...next, IgnoreSeconds: value }));
        check(card(say('panel')), 'show-status', settings.ShowStatus, (next, value) => ({ ...next, ShowStatus: value }));
        const account = card(say('account'));
        check(account, 'share-account', settings.ShareAccount, (next, value) => ({ ...next, ShareAccount: value }));
        note(account, 'share-note');
        profilePage();
    }

    function profilePage() {
        const profile = card(say('profile'));
        note(profile, 'profile-note');
        const message = element('p', 'touch-hunt-note');
        const field = element('textarea', 'touch-hunt-input touch-hunt-code');
        field.rows = 3;
        field.placeholder = say('paste-code');
        const row = actions(profile);
        button(row, say('copy-code'), async () => {
            const code = getHost()?.FreeHuntProfileCode?.() ?? '';
            if (code.length > 32768) {
                message.textContent = say('too-long');
                return;
            }
            try {
                await navigator.clipboard.writeText(code);
                message.textContent = say('copied');
            } catch {
                field.value = code;
                field.select();
                message.textContent = say('copy-failed');
            }
        });
        button(row, say('import-code'), () => {
            message.textContent = settings.ShareAccount ? say('import-share') : say('import-own');
            if (!field.value.trim()) {
                field.focus();
                return;
            }
            if (getHost()?.ImportFreeHuntProfile?.(field.value)) {
                persist?.();
                field.value = '';
                reload();
            } else {
                message.textContent = say('invalid-code');
            }
        });
        profile.append(field, message);
    }

    function skillsPage() {
        const used = card(say('used'));
        const learned = state?.skills ?? [];
        for (const skill of learned) {
            checkText(used, format(say('skill-rank'), skill.name, skill.rank), settings.Skills.includes(skill.code), (next, value) => ({
                ...next,
                Skills: value ? [...next.Skills.filter(code => code !== skill.code), skill.code] : next.Skills.filter(code => code !== skill.code)
            }));
        }
        settings.Skills.forEach((code, index) => {
            const name = learned.find(skill => skill.code === code)?.name ?? String(code);
            numberField(used, format(say('mana-floor'), name), settings.SkillManaFloor[code] ?? 0, 0, 100,
                (next, value) => ({ ...next, SkillManaFloor: { ...next.SkillManaFloor, [code]: value } }));
            const row = actions(used);
            button(row, say('up'), () => moveSkill(index, -1));
            button(row, say('down'), () => moveSkill(index, 1));
        });
        if (learned.length === 0) note(used, 'no-skills');
        const attack = card(say('attack'));
        check(attack, 'basic', settings.BasicAttacks, (next, value) => ({ ...next, BasicAttacks: value }));
        numeric(attack, 'area-range', settings.AreaCastRange, 0, 2048, (next, value) => ({ ...next, AreaCastRange: value }));
        const area = card(say('area-skill'));
        check(area, 'prefer-area', settings.PreferArea, (next, value) => ({ ...next, PreferArea: value }));
        numeric(area, 'area-min', settings.AreaMinimum, 1, 40, (next, value) => ({ ...next, AreaMinimum: value }));
        numeric(area, 'area-radius', settings.AreaRadius, 0, 2048, (next, value) => ({ ...next, AreaRadius: value }));
        check(card(say('swap')), 'swap-shield', settings.SwapShieldForBuff, (next, value) => ({ ...next, SwapShieldForBuff: value }));
        const summon = card(say('crystal'));
        check(summon, 'use-crystals', settings.UseCrystals, (next, value) => ({ ...next, UseCrystals: value }));
        itemChoice(summon, 'crystal-item', 'crystal', settings.Crystal, (next, value) => ({ ...next, Crystal: value }));
        numeric(summon, 'summon-count', settings.SummonCount, 1, petSlots, (next, value) => ({ ...next, SummonCount: value }));
        check(summon, 'in-combat', settings.CrystalInCombat, (next, value) => ({ ...next, CrystalInCombat: value }));
        choice(summon, say('companion'), [say('companion-0'), say('companion-1'), say('companion-2')], settings.CompanionTarget,
            index => change(next => ({ ...next, CompanionTarget: index })));
        note(summon, 'companion-note');
        const force = card(say('force'));
        check(force, 'use-force', settings.UseForce, (next, value) => ({ ...next, UseForce: value }));
        itemChoice(force, 'force-item', 'force', settings.ForceKey, (next, value) => ({ ...next, ForceKey: value }));
        check(force, 'weak-first', settings.WeakForceFirst, (next, value) => ({ ...next, WeakForceFirst: value }));
        check(force, 'in-combat', settings.ForceInCombat, (next, value) => ({ ...next, ForceInCombat: value }));
        check(card(say('premiums')), 'use-premiums', settings.UsePremiums, (next, value) => ({ ...next, UsePremiums: value }));
    }

    function moveSkill(index, offset) {
        const to = index + offset;
        if (to < 0 || to >= settings.Skills.length) return;
        change(next => {
            const order = [...next.Skills];
            [order[index], order[to]] = [order[to], order[index]];
            return { ...next, Skills: order };
        });
        render();
    }

    function potionsPage() {
        for (const [key, field, kind] of [['hp', 'HP', 'hp'], ['mp', 'MP', 'mp'], ['res', 'RES', 'res']]) {
            const potion = card(say(key));
            check(potion, 'use-potion', settings[field].Enabled, (next, value) => ({ ...next, [field]: { ...next[field], Enabled: value } }));
            numeric(potion, 'below', settings[field].Threshold, 0, 100, (next, value) => ({ ...next, [field]: { ...next[field], Threshold: value } }));
            itemChoice(potion, 'preferred', kind, settings[field].Key, (next, value) => ({ ...next, [field]: { ...next[field], Key: value } }));
        }
        const adaptive = card(say('adaptive-title'));
        check(adaptive, 'adaptive', settings.AdaptivePotion, (next, value) => ({ ...next, AdaptivePotion: value }));
        note(adaptive, 'adaptive-note');
        check(card(say('belt')), 'refill-belt', settings.RefillBelt, (next, value) => ({ ...next, RefillBelt: value }));
        const rhythm = card(say('rhythm'));
        numeric(rhythm, 'same-gap', settings.SamePotionMs, 500, 10000, (next, value) => ({ ...next, SamePotionMs: value }));
        numeric(rhythm, 'any-gap', settings.AnyPotionMs, 500, 10000, (next, value) => ({ ...next, AnyPotionMs: value }));
        note(rhythm, 'rhythm-note');
    }

    function safety(place, key, value, set) {
        choice(place, say(key), [say('safety-0'), say('safety-1'), say('safety-2')], value, index => change(next => set(next, index)));
    }

    function safetyPage() {
        const critical = card(say('critical'));
        numeric(critical, 'critical-hp', settings.CriticalHp, 0, 100, (next, value) => ({ ...next, CriticalHp: value }));
        safety(critical, 'no-potion', settings.CriticalAction, (next, value) => ({ ...next, CriticalAction: value }));
        const supply = card(say('supply'));
        check(supply, 'use-core', settings.UseCoreWhenOutOfPotions, (next, value) => ({ ...next, UseCoreWhenOutOfPotions: value }));
        safety(supply, 'no-hp', settings.NoHealthPotion, (next, value) => ({ ...next, NoHealthPotion: value }));
        const bag = card(say('full-bag'));
        safety(bag, 'action', settings.FullBag, (next, value) => ({ ...next, FullBag: value }));
        note(bag, 'bag-note');
        const death = card(say('death'));
        check(death, 'town-death', settings.RestartInTown, (next, value) => ({ ...next, RestartInTown: value }));
        check(death, 'town-deaths', settings.TownAfterDeaths, (next, value) => ({ ...next, TownAfterDeaths: value }));
        check(death, 'town-scrolls', settings.TownWhenLowScrolls, (next, value) => ({ ...next, TownWhenLowScrolls: value }));
        check(death, 'stop-party', settings.StopOutsideParty, (next, value) => ({ ...next, StopOutsideParty: value }));
        note(death, 'death-note');
        const manual = card(say('manual'));
        check(manual, 'pause-manual', settings.PauseForManual, (next, value) => ({ ...next, PauseForManual: value }));
        numeric(manual, 'resume', settings.ResumeMs, 0, 60000, (next, value) => ({ ...next, ResumeMs: value }));
    }

    // The game classifies the catalog once; until then the loot lists search nothing.
    function loadCatalog() {
        if (catalog || catalogLoading) return;
        catalogLoading = fetch(new URL('./auto-hunt-items.tsv', import.meta.url))
            .then(response => (response.ok ? response.text() : ''))
            .then(textValue => {
                catalog = JSON.parse(getHost()?.ClassifyHuntItems?.(textValue) || '[]');
                if (dialog.open && tabs[tab] === 'loot') render();
            })
            .catch(() => {
                catalog = [];
            });
    }

    function lootPage() {
        loadCatalog();
        const loot = card(say('loot-auto'));
        check(loot, 'loot', settings.Loot, (next, value) => ({ ...next, Loot: value }));
        choice(loot, say('loot-mode'), [0, 1, 2, 3].map(index => say('mode-' + index)), settings.LootMode,
            index => change(next => ({ ...next, LootMode: index })));
        categoriesCard();
        const potions = card(say('potions-card'));
        for (const [key, group, field] of [['hp', '@HP', 'MaxLifePotions'], ['mp', '@MP', 'MaxManaPotions'], ['res', '@RES', 'MaxStaminaPotions']]) {
            potionLoot(potions, key, group);
            numeric(potions, 'stop-' + key, settings[field], 0, 9999, (next, value) => ({ ...next, [field]: value }));
        }
        note(potions, 'potions-note');
        lootList('always', 'AlwaysLoot');
        lootList('never', 'NeverLoot');
        levelsCard();
        const spec = card(say('spec'));
        check(spec, 'spec-first', settings.PrioritizeOwnSpec, (next, value) => ({ ...next, PrioritizeOwnSpec: value }));
        check(spec, 'spec-only', settings.OnlyOwnSpec, (next, value) => ({ ...next, OnlyOwnSpec: value }));
        const reach = card(say('reach'));
        range(reach, 'distance', settings.LootRadius, 'touch-hunt:PreviewLoot', (next, value) => ({ ...next, LootRadius: value }));
        check(reach, 'ignore-own', settings.IgnoreOwnDrops, (next, value) => ({ ...next, IgnoreOwnDrops: value }));
        check(reach, 'return-loot', settings.ReturnAfterLoot, (next, value) => ({ ...next, ReturnAfterLoot: value }));
        numeric(card(say('loot-break')), 'loot-after', settings.LootAfterSeconds, 0, 600, (next, value) => ({ ...next, LootAfterSeconds: value }));
        button(actions(card(say('default'))), say('restore-default'), () => {
            change(next => ({ ...next, Loot: true, LootRadius: 320, LootMode: 0, Excluded: 0, AlwaysLoot: '', NeverLoot: '', MaxLifePotions: 0,
                MaxManaPotions: 0, MaxStaminaPotions: 0, ReturnAfterLoot: false, LootAfterSeconds: 0, LevelByCategory: {}, MinimumItemLevel: 0,
                MaximumItemLevel: 0, IgnoreOwnDrops: true, ExcludedPotions: '', PrioritizeOwnSpec: false, OnlyOwnSpec: false }));
            render();
        });
    }

    function categoriesCard() {
        const place = card(say('categories'));
        const tiles = [];
        const setExcluded = excluded => {
            change(next => ({ ...next, Excluded: excluded, HasExpandedLootCategories: true }));
            for (const [flag, tile] of tiles) tile.dataset.on = String((settings.Excluded & flag) === 0);
        };
        for (const [heading, equipment] of [['supplies', false], ['equipment', true]]) {
            note(place, heading);
            const grid = element('div', 'touch-hunt-tiles');
            for (const [name, flag] of lootCategories.filter(([, flag]) => ((flag & equipmentMask) !== 0) === equipment)) {
                const tile = button(grid, say('cat-' + name), () => setExcluded(settings.Excluded ^ flag));
                tile.dataset.on = String((settings.Excluded & flag) === 0);
                tiles.push([flag, tile]);
            }
            place.append(grid);
        }
        const row = actions(place);
        button(row, say('mark-all'), () => setExcluded(0));
        button(row, say('unmark-all'), () => setExcluded(allCategories));
        note(place, 'categories-note');
    }

    function potionLoot(place, key, group) {
        place.append(element('p', 'touch-hunt-note', say(key)));
        const grid = element('div', 'touch-hunt-tiles');
        const potions = (catalog ?? []).filter(item => item.potion === group);
        button(grid, say('all'), () => {
            change(next => ({ ...next, ExcludedPotions: splitTerms(next.ExcludedPotions).filter(code => !potions.some(item => item.code === code)).join(',') }));
            render();
        });
        for (const item of potions) {
            const tile = button(grid, item.name, () => {
                const collect = tile.dataset.on === 'true';
                change(next => ({ ...next, ExcludedPotions: collect ? addTerm(next.ExcludedPotions, item.code) : removeTerm(next.ExcludedPotions, item.code) }));
                tile.dataset.on = String(!collect);
            });
            tile.dataset.on = String(!splitTerms(settings.ExcludedPotions).some(code => code.toLowerCase() === item.code.toLowerCase()));
        }
        place.append(grid);
    }

    function picture(code) {
        const item = catalog?.find(entry => entry.code.toLowerCase() === code.toLowerCase());
        const canvas = item?.picture ? getPicture(item.picture) : null;
        const icon = element('span', 'touch-hunt-icon');
        if (canvas) {
            const image = element('img');
            image.alt = '';
            image.src = pictureAddress(canvas);
            icon.append(image);
        }
        return icon;
    }

    function lootList(key, field) {
        const place = card(say(key));
        const chosen = element('div', 'touch-hunt-chosen');
        const groups = [['@HP', say('group-HP')], ['@MP', say('group-MP')], ['@RES', say('group-RES')],
            ...lootCategories.map(([name]) => ['@' + name, say('cat-' + name)])];
        const results = element('div', 'touch-hunt-results');
        let category = '';
        let pageIndex = 0;
        const renderChosen = () => {
            chosen.replaceChildren(...splitTerms(settings[field]).map(term => {
                const code = term.startsWith('#') ? term.slice(1) : '';
                const item = code && catalog?.find(entry => entry.code.toLowerCase() === code.toLowerCase());
                const group = groups.find(([value]) => value.toLowerCase() === term.toLowerCase());
                const node = element('button', 'touch-hunt-option');
                node.type = 'button';
                if (code) node.append(picture(code));
                node.append(element('span', '', (item?.name ?? group?.[1] ?? format(say('contains'), term)) + '   ×'));
                node.addEventListener('click', () => {
                    change(next => ({ ...next, [field]: removeTerm(next[field], term) }));
                    renderChosen();
                    search();
                });
                return node;
            }));
        };
        const add = term => {
            change(next => ({ ...next, [field]: addTerm(next[field], term) }));
            renderChosen();
            search();
        };
        const groupSelect = choice(place, say('add-category'), ['—', ...groups.map(([, name]) => name)], 0, index => {
            if (index > 0) add(groups[index - 1][0]);
            groupSelect.value = '0';
        });
        const query = element('input', 'touch-hunt-input touch-hunt-text');
        query.type = 'search';
        query.placeholder = say('search');
        choice(place, say('search-in'), [say('all'), ...lootCategories.map(([name]) => say('cat-' + name))], 0, index => {
            category = index === 0 ? '' : lootCategories[index - 1][0];
            pageIndex = 0;
            search();
        });
        place.append(query, results);
        place.insertBefore(chosen, place.children[1]);
        function search() {
            const textValue = query.value.trim();
            const nodes = [];
            if (!textValue && !category) {
                nodes.push(element('p', 'touch-hunt-note', say('search-hint')));
            } else {
                const options = searchCatalog(catalog ?? [], textValue, category, settings[field]);
                pageIndex = Math.max(0, Math.min(pageIndex, Math.floor(Math.max(0, options.length - 1) / pageSize)));
                for (const item of options.slice(pageIndex * pageSize, (pageIndex + 1) * pageSize)) {
                    const node = element('button', 'touch-hunt-option');
                    node.type = 'button';
                    node.append(picture(item.code), element('span', '', item.name + '  ·  ' + item.code));
                    node.addEventListener('click', () => add('#' + item.code));
                    nodes.push(node);
                }
                const move = element('div', 'touch-hunt-actions');
                if (pageIndex > 0) button(move, say('previous'), () => { pageIndex--; search(); });
                if ((pageIndex + 1) * pageSize < options.length) button(move, say('more'), () => { pageIndex++; search(); });
                if (move.children.length) nodes.push(move);
                if (options.length === 0) nodes.push(element('p', 'touch-hunt-note', say('no-results')));
            }
            if (textValue && !'#@'.includes(textValue[0]) && !textValue.includes(',')) {
                const rule = element('div', 'touch-hunt-actions');
                button(rule, format(say('add-rule'), textValue), () => add(textValue));
                nodes.push(rule);
            }
            results.replaceChildren(...nodes);
        }
        query.addEventListener('input', () => {
            pageIndex = 0;
            search();
        });
        renderChosen();
        search();
    }

    function levelsCard() {
        const place = card(say('levels'));
        const kinds = lootCategories.filter(([, flag]) => (flag & equipmentMask) !== 0);
        const bounds = element('div', 'touch-hunt-bounds');
        let selected = 0;
        const build = () => {
            bounds.replaceChildren();
            const global = { Minimum: settings.MinimumItemLevel, Maximum: settings.MaximumItemLevel };
            const kind = selected === 0 ? '' : kinds[selected - 1][0];
            const levels = selected === 0 ? global : settings.LevelByCategory[kind] ?? global;
            // Every equipment's bounds, or one category's, which start from every equipment's.
            const level = (next, minimum, value) => {
                if (selected === 0) return minimum ? { ...next, MinimumItemLevel: value } : { ...next, MaximumItemLevel: value };
                const before = next.LevelByCategory[kind] ?? { Minimum: next.MinimumItemLevel, Maximum: next.MaximumItemLevel };
                return { ...next, LevelByCategory: { ...next.LevelByCategory, [kind]: minimum ? { ...before, Minimum: value } : { ...before, Maximum: value } } };
            };
            numberField(bounds, say('from'), levels.Minimum, 0, 500, (next, value) => level(next, true, value));
            numberField(bounds, say('to'), levels.Maximum, 0, 500, (next, value) => level(next, false, value));
        };
        choice(place, say('level-category'), [say('level-all'), ...kinds.map(([name]) => say('cat-' + name))], 0, index => {
            selected = index;
            build();
        });
        place.append(bounds);
        build();
    }

    function metrics(place, keys) {
        const grid = element('div', 'touch-hunt-metrics');
        for (const key of keys) {
            const cell = element('div', 'touch-hunt-metric');
            const value = element('strong', '', '—');
            cell.append(element('span', '', say(key).toUpperCase()), value);
            readings[key] = value;
            grid.append(cell);
        }
        place.append(grid);
    }

    function gainsPage() {
        const control = card(say('session'));
        const row = actions(control);
        button(row, state?.resting ? say('resume-stats') : say('rest'), () => {
            run('touch-hunt:Rest');
            reload();
        });
        button(row, say('zero'), () => run('touch-hunt:ResetTotals'));
        choice(control, say('count'), [say('count-0'), say('count-1')], settings.CountOnlyHunting ? 0 : 1,
            index => change(next => ({ ...next, CountOnlyHunting: index === 0 })));
        const time = element('p', 'touch-hunt-session');
        readings['session-time'] = time;
        control.append(time);
        metrics(card(say('experience')), ['exp-hour', 'exp-recent', 'exp-gained', 'progress', 'exp-lost', 'level', 'levels-gained', 'next-level']);
        const wallet = card(say('wallet'));
        metrics(wallet, ['balance', 'balance-hour', 'income', 'spending', 'ground']);
        note(wallet, 'wallet-note');
        metrics(card(say('kills')), ['killed', 'kills-hour', 'kills-recent', 'deaths', 'damage', 'dps']);
        metrics(card(say('potions-used')), ['hp-short', 'mp-short', 'res-short']);
        const drops = card(say('collected'));
        metrics(drops, ['units', 'kinds']);
        const items = element('p', 'touch-hunt-list');
        readings.items = items;
        drops.append(items);
    }

    function groupPage() {
        const place = card(say('tab-group'));
        button(actions(place), say('zero'), () => run('touch-hunt:ResetGroup'));
        metrics(place, ['group-dps', 'group-damage', 'group-kills', 'group-exp', 'group-deaths']);
        const members = element('div', 'touch-hunt-list');
        readings.members = members;
        card(say('members')).append(members);
    }

    function logPage() {
        const place = card(say('happened'));
        choice(place, say('show'), [0, 1, 2, 3].map(index => say('log-' + index)), logCategory, index => {
            logCategory = index;
            renderLog();
        });
        const query = element('input', 'touch-hunt-input touch-hunt-text');
        query.type = 'search';
        query.value = logQuery;
        query.placeholder = say('search-log-hint');
        query.addEventListener('input', () => {
            logQuery = query.value;
            renderLog();
        });
        row(place, say('search-log')).append(query);
        const count = element('p', 'touch-hunt-note');
        readings.logCount = count;
        place.append(count);
        button(actions(place), say('clear-log'), () => confirm(say('clear-confirm'), () => {
            run('touch-hunt:ClearLog');
            logVersion = -1;
            readLog();
        }));
        const list = element('div', 'touch-hunt-log');
        readings.log = list;
        place.append(list);
        renderLog();
    }

    function renderLog() {
        if (!readings.log) return;
        const query = logQuery.trim().toLocaleLowerCase();
        const lines = [...logEntries].reverse().map(entry => ({ entry, line: entry.time + ' · ' + logText(entry, say) }))
            .filter(({ entry, line }) => logMatches(entry, logCategory) && line.toLocaleLowerCase().includes(query));
        readings.logCount.textContent = format(say('log-count'), lines.length, logEntries.length);
        readings.log.replaceChildren(...(lines.length ? lines.map(({ line }) => element('p', '', line)) : [element('p', 'touch-hunt-note', say('no-log'))]));
    }

    function readLog() {
        if (!state || state.logVersion === logVersion) return;
        logVersion = state.logVersion;
        try {
            logEntries = JSON.parse(getHost()?.TouchHuntLog?.() || '[]');
        } catch {
            logEntries = [];
        }
        renderLog();
    }

    function showTotals() {
        if (!state) return;
        const totals = state.totals;
        if (readings['session-time']) {
            const values = gainReadings(totals, state, say, getLanguage());
            for (const [key, value] of Object.entries(values)) {
                if (readings[key] && readings[key].textContent !== value) readings[key].textContent = value;
            }
            const items = [...totals.Items].sort((first, second) => second.Count - first.Count).slice(0, 40);
            readings.items.textContent = items.length ? items.map(item => item.Name + '  ×' + number(item.Count)).join('\n') : say('no-items');
        }
        if (readings.members) {
            const members = totals.Members;
            const sum = pick => members.reduce((total, member) => total + pick(member), 0);
            readings['group-dps'].textContent = new Intl.NumberFormat(getLanguage(), { maximumFractionDigits: 1, minimumFractionDigits: 1 })
                .format(sum(member => (member.Active ? member.RecentDamagePerSecond : 0)));
            readings['group-damage'].textContent = number(sum(member => member.Damage));
            readings['group-kills'].textContent = number(sum(member => member.Kills));
            readings['group-exp'].textContent = number(sum(member => member.Experience));
            readings['group-deaths'].textContent = number(sum(member => member.Deaths));
            readings.members.replaceChildren(...(members.length === 0 ? [element('p', 'touch-hunt-note', say('no-members'))]
                : [...members].sort((first, second) => Number(second.Active) - Number(first.Active)).map(member => {
                    const node = element('div', 'touch-hunt-member');
                    node.append(element('strong', '', member.Name + ' · '
                        + (member.Active ? format(say('member-dps'), member.RecentDamagePerSecond.toFixed(1)) : say('member-left'))),
                    element('span', '', format(say('member-line'), number(member.Damage), number(member.Kills), number(member.Experience), number(member.Deaths))));
                    return node;
                })));
        }
    }

    function showStatus() {
        if (!state) return;
        const key = [state.active, state.available, state.paused, getLanguage()].join();
        if (key === shownStatus) return;
        shownStatus = key;
        status.textContent = state.paused ? say('paused') : state.active ? say('on') : say('off');
        toggle.textContent = state.active ? say('pause') : say('start');
        toggle.disabled = !state.active && !state.available;
        toggle.dataset.on = String(state.active);
        toggle.title = toggle.disabled ? say('unavailable') : '';
    }

    function renderTabs() {
        tabMenu.replaceChildren(...tabs.map((name, index) => {
            const node = element('button', 'touch-journal-tab touch-hunt-tab');
            node.type = 'button';
            node.setAttribute('aria-pressed', String(index === tab));
            node.append(element('span', 'touch-hunt-tab-icon', tabIcons[index]), element('span', '', say('tab-' + name)));
            node.addEventListener('click', () => {
                tab = index;
                render();
            });
            return node;
        }));
    }

    function render() {
        if (!settings) return;
        readings = {};
        page.replaceChildren();
        renderTabs();
        [huntPage, skillsPage, potionsPage, safetyPage, lootPage, gainsPage, groupPage, logPage][tab]();
        page.scrollTop = 0;
        showTotals();
        readLog();
    }

    // The options and choices read again (after an import or a reset).
    function reload() {
        state = read(true) ?? state;
        settings = state?.settings ?? settings;
        shownStatus = '';
        showStatus();
        render();
    }

    function open() {
        if (dialog.open) return;
        tab = 0;
        logVersion = -1;
        state = read(true);
        settings = state?.settings ? structuredClone(state.settings) : null;
        shownStatus = '';
        dialog.showModal();
        showStatus();
        render();
    }

    toggle?.addEventListener('click', () => {
        run('touch-hunt:Toggle');
        poll(true);
    });
    resetButton?.addEventListener('click', () => confirm(say('reset-confirm'), () => {
        run('touch-hunt:ResetSettings');
        persist?.();
        reload();
    }));
    dialog.querySelector('[data-touch-hunt-close]')?.addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => persist?.());
    dialog.addEventListener('keydown', event => event.stopPropagation());

    function poll(force = false) {
        if (!dialog.open) return;
        const now = Date.now();
        if (!force && now - lastPoll < pollMs) return;
        lastPoll = now;
        const next = read(false);
        if (!next) return;
        state = { ...state, ...next };
        showStatus();
        showTotals();
        readLog();
    }

    return {
        open,
        poll: () => poll(),
        close() {
            if (dialog.open) dialog.close();
        },
        translate() {
            dialog.querySelector('[data-touch-hunt-title]')?.replaceChildren(say('title'));
            dialog.querySelector('[data-touch-hunt-chip]')?.replaceChildren(say('chip'));
            resetButton?.setAttribute('aria-label', say('reset'));
            dialog.querySelector('[data-touch-hunt-close]')?.setAttribute('aria-label', say('close'));
            shownStatus = '';
            if (dialog.open) {
                showStatus();
                render();
            }
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const huntTables = { hunt: labels };
