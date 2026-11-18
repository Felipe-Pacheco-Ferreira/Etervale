export const say = (pt, en) => ({ 'pt-BR': pt, en });
const opt = (value, pt, en = pt, unavailable = false) => [value, say(pt, en), unavailable];
const card = (pt, en, rows, meta = {}) => ({ title: say(pt, en), rows, ...meta });
const toggle = (field, pt, en, extra = {}) => ({ field, label: say(pt, en), toggle: true, unavailable: !field, ...extra });
const select = (field, pt, en, options, extra = {}) => ({ field, label: say(pt, en), options, unavailable: !field, ...extra });
const caption = (pt, en) => ({ caption: say(pt, en) });
const action = (pt, en, helpPt, helpEn) => ({ action: say(pt, en), help: say(helpPt, helpEn) });
const volume = (field, fallback, unavailable = false) => ({
    field, label: say('Volume', 'Volume'), range: 'volume', fallback, unavailable
});
const yes = { fallback: true };
const info = (pt, en) => ({ help: say(pt, en) });
const off = opt('Off', 'Desligado', 'Off');
const levels = [opt('High', 'Alto', 'High'), opt('Medium', 'Médio', 'Medium'), opt('Low', 'Baixo', 'Low')];

export function setRendererOptions(capabilities) {
    if (!capabilities) return;
    const features = { autoExposure: 'autoExposure', volumetricLight: 'volumetricLight', contactShadows: 'contactShadows',
        colorGrading: 'colorGradingLut', realisticSky: 'proceduralSky', cloudQuality: 'proceduralSky', shadowUpdateRate: 'shadowUpdateRate' };
    for (const columns of Object.values(tabs)) for (const cards of columns) for (const entry of cards) {
        for (const row of entry.rows ?? []) {
            if (row.field === 'antiAliasing' && capabilities.antiAliasing) {
                for (const option of row.options) option[2] = !capabilities.antiAliasing.includes(option[0]);
            }
            if (capabilities.unsupported?.[features[row.field]]) row.unavailable = true;
        }
    }
}

export function normalizeRendererSettings(settings, capabilities) {
    const supported = capabilities?.antiAliasing;
    if (!supported || supported.includes(settings.antiAliasing)) return settings;
    return { ...settings, antiAliasing: supported.includes('Smaa') ? 'Smaa' : 'Off' };
}

export const qualityChoices = [
    opt('Auto', 'Automático (recomendado)', 'Automatic (recommended)'),
    opt('VeryLow', 'Muito baixo', 'Very low'),
    ...levels.slice().reverse(),
    opt('Custom', 'Personalizado', 'Custom')
];
export function qualityLabel(profile, effectiveQuality, language = 'pt-BR') {
    const label = key => {
        const phrase = qualityChoices.find(([value]) => value === key)?.[1];
        return phrase?.[language] ?? phrase?.['pt-BR'] ?? '';
    };
    if (profile === 'Auto') {
        const automatic = language === 'en' ? 'Automatic' : 'Automático';
        const effective = effectiveQuality === 'Ultra' ? 'High' : effectiveQuality;
        return ['VeryLow', 'Low', 'Medium', 'High'].includes(effective) ? automatic + ' · ' + label(effective) : automatic;
    }
    return label(profile === 'Ultra' ? 'High' : profile) || label('Custom');
}
export function dynamicResolutionLabel(state, requestedScale = 1, language = 'pt-BR', requestedShadowRate = 60) {
    if (!state?.enabled) return '';
    let label = '';
    if (Number.isFinite(state.scale) && state.scale > 0 && Number.isFinite(requestedScale) && state.scale < requestedScale - 0.0001
        && Number.isInteger(state.width) && state.width > 0 && Number.isInteger(state.height) && state.height > 0) {
        label = ` · 3D ${state.width}×${state.height} (${Math.round(state.scale * 100)}%, ${language === 'en' ? 'dynamic' : 'dinâmica'})`;
    }
    const rate = state.shadowUpdateRate;
    const cap = Number.isFinite(requestedShadowRate) && requestedShadowRate >= 0 ? requestedShadowRate : 60;
    if (Number.isInteger(rate) && rate > 0 && (cap === 0 || rate < cap)) {
        label += ` · ${language === 'en' ? 'shadows' : 'sombras'} ${rate} Hz`;
    }
    return label;
}
export const originalTextures = [
    opt('Auto', 'Automático (Original)', 'Automatic (Original)'),
    opt('Original', 'Original'),
    opt('Light', 'Leve (½ resolução)', 'Light (half resolution)'),
    opt('VeryLight', 'Muito leve (¼ resolução)', 'Very light (quarter resolution)')
];
export const shortcutGroups = {
    windows: say('Janelas', 'Windows'),
    interface: say('Interface', 'Interface'),
    character: say('Personagem', 'Character'),
    voice: say('Voz', 'Voice'),
    skills: say('Habilidades', 'Skills'),
    potions: say('Poções', 'Potions'),
    camera: say('Câmera', 'Camera')
};
export const shortcutColumns = [
    ['windows', 'interface', 'character'], ['voice', 'skills', 'potions', 'camera']
];
export const unavailableShortcuts = {
    windows: [
        [5, say('Busca de grupo', 'Find a party'), 'I'],
        [5, say('Loja de itens', 'Item shop'), 'F9'],
        [8, say('Auto-caça (painel)', 'Auto hunt panel'), 'O']
    ],
    interface: [
        [1, say('Mapa do mundo', 'World map'), 'M'],
        [2, say('Ver item perfeito (segurar)', 'Preview perfect item (hold)'), 'P'],
        [2, say('Informações fixas sobre os personagens', 'Persistent character information'), 'Backspace']
    ],
    character: [
        [3, say('Traje', 'Costume'), 'T'],
        [3, say('Pontos de status +10 (segurar)', 'Stat points +10 (hold)'), 'B'],
        [3, say('Auto-caça: ligar / desligar', 'Toggle auto hunt'), 'P'],
        [3, say('Modo PK: ligar / desligar', 'Toggle PK mode'), 'K']
    ],
    voice: [[0, say('Ligar / desligar microfone', 'Toggle microphone'), 'B']],
    camera: [
        [0, say('Modo da câmera', 'Camera mode'), 'Z'],
        [0, say('Câmera 2D / 3D (dungeons)', '2D / 3D dungeon camera'), 'Shift + Z'],
        [2, say('Girar câmera para a esquerda', 'Rotate camera left'), 'Mouse 4'],
        [2, say('Girar câmera para a direita', 'Rotate camera right'), 'Mouse 5']
    ]
};

const languages = [
    opt('pt-BR', 'Português', 'Portuguese'), opt('en', 'English'),
    opt('es', 'Español'), opt('ja', '日本語'), opt('vi', 'Tiếng Việt'), opt('zh', '简体中文'), opt('fil', 'Filipino')
];

export const tabs = {
    video: [
        [
            card('Texturas', 'Textures', [], { className: 'textures', textures: true }),
            card('Qualidade gráfica', 'Graphics quality', [
                select(null, 'Iluminação', 'Lighting', [opt('Default', 'Padrão (novo)', 'Default (new)'), opt('Classic', 'Clássico', 'Classic')]),
                select('renderScale', 'Escala de render.', 'Render scale',
                    [100, 90, 80, 75, 70, 60, 50].map(value => [value / 100, value + '%']), { preset: true }),
                select(null, 'Resolução máx.', 'Max. resolution', [
                    opt(2, '2x (tela retina)', '2x (retina display)'), opt(1.5, '1,5x', '1.5x'), [1, '1x']
                ], { fallback: 1 }),
                select('shadowsEnabled', 'Sombras', 'Shadows', [
                    opt('High', 'Alta', 'High'), opt('Medium', 'Média', 'Medium'), opt('Low', 'Baixa', 'Low'),
                    opt('Simple', 'Simples', 'Simple'), opt('Off', 'Desligadas', 'Off')
                ], { kind: 'shadow', preset: true }),
                select('screenSpaceReflections', 'Reflexo na água', 'Water reflection', [
                    opt(true, 'Completo', 'Full'), opt('Scenery', 'Só cenário', 'Scenery only', true), off
                ], { kind: 'reflection', preset: true }),
                select(null, 'Filtro anisotróp.', 'Anisotropic filter', [[8, '8x'], [4, '4x'], [2, '2x'], off], { fallback: 2 }),
                select(null, 'Rend. personagens', 'Character rendering', [opt('VeryHigh', 'Muito alto', 'Very high'), ...levels], { fallback: 'High' }),
                select('antiAliasing', 'Anti-serrilhado', 'Anti-aliasing', [
                    off, opt('Msaa', 'MSAA', 'MSAA', true), ['Fxaa', 'FXAA'], ['Smaa', 'SMAA'], ['Taa', 'TAA']
                ], { preset: true }),
                toggle(null, 'Reflexo a cada 2 quadros', 'Reflect every two frames', yes),
                toggle('dynamicResolution', 'Resolução dinâmica', 'Dynamic resolution', yes),
                toggle(null, 'Glow', 'Glow', info('Contorno do alvo ou do que está sob o cursor.', 'Outlines the target or the object under the cursor.')),
                toggle(null, 'Cor do aging/mix', 'Aging and mix colors', yes),
                toggle(null, 'Limitar efeitos de outros jogadores', 'Limit effects from other players', yes),
                caption('Ajusta a resolução 3D para buscar 60 FPS (mínimo de 50%). A interface mantém a resolução da tela.',
                    'Adjusts 3D resolution toward 60 FPS (50% minimum). The interface retains the display resolution.')
            ], { className: 'quality', quality: true }),
            card('Limite de FPS', 'Frame limit', [
                select('maxFps', 'Limite de FPS', 'Frame limit', [
                    opt(0, 'Sem limite (VSync)', 'Unlimited (VSync)'), [120, '120 FPS'], [60, '60 FPS'], opt(30, '30 FPS (economia)', '30 FPS (economy)')
                ], { wide: true })
            ]),
            card('Exibição', 'Display', [
                toggle(null, 'Modo Janela', 'Window mode', yes),
                toggle(null, 'Auto Ajuste', 'Automatic sizing', yes)
            ]),
            card('Resolução', 'Resolution', [
                { pair: [
                    select(null, 'Proporção', 'Aspect ratio', [['4:3', '4:3'], ['16:9', '16:9'], ['16:10', '16:10']], { wide: true }),
                    select(null, 'Tamanho', 'Size', ['800×600', '1024×768', '1280×720', '1366×768', '1920×1080'].map(value => [value, value]), { wide: true })
                ] }
            ]),
            card('Efeitos', 'Effects', [
                toggle(null, 'Force/Skill/Emotes', 'Force/Skill/Emotes', yes),
                toggle(null, 'Tela cinza', 'Grayscale screen', yes),
                toggle(null, 'Efeito de Dissolver', 'Dissolve effect', yes),
                toggle('distanceFog', 'Fog Ativo', 'Enable fog', { kind: 'fog', ...info('Cobre o horizonte gradualmente.', 'Fades distant scenery into the horizon.') }),
                { label: say('Distância do fog', 'Fog distance'), range: 'fog', fallback: 2600, unavailable: true },
                toggle(null, 'Alerta de baixo HP', 'Low HP warning', yes)
            ])
        ],
        [
            card('Interface', 'Interface', [
                { field: 'uiScale', label: say('Escala do HUD', 'HUD scale'), hudScale: true, unavailable: true },
                toggle(null, 'Interface centralizada', 'Centered interface', yes),
                toggle(null, 'Mover Chat', 'Move chat'),
                toggle(null, 'Interface clássica', 'Classic interface', { help: true }),
                toggle(null, 'Censura de chat', 'Chat filter'),
                toggle(null, 'Chat global de todos os servidores', 'Global chat across all servers', { ...yes, help: true }),
                toggle(null, 'Esconder outros jogadores', 'Hide other players'),
                toggle(null, 'Modo PvP', 'PvP mode', { help: true }),
                toggle('alwaysShowRareGroundLabels', 'Nomes dos itens raros no chão', 'Rare ground item names',
                    info('Exibe os nomes dos itens raros próximos no chão.', 'Shows nearby rare item names on the ground.')),
                toggle('alwaysShowGroundLabels', 'Nomes de todos os itens no chão', 'Names of all ground items',
                    info('Exibe os nomes dos itens próximos no chão.', 'Shows nearby item names on the ground.')),
                toggle('hideOnlineCount', 'Mostrar jogadores online', 'Show online players', { invert: true, help: say('Mostra a contagem no HUD.', 'Shows the count on the HUD.') }),
                toggle(null, 'Mostrar DPS do grupo no alvo', 'Show party DPS on the target', { ...yes, help: true }),
                toggle(null, 'Ocultar ícones do HUD', 'Hide HUD icons', { help: true }),
                toggle(null, 'Informações fixas sobre os personagens (Backspace)', 'Persistent character information (Backspace)', { ...yes, help: true })
            ], { className: 'interface' }),
            card('Nomes', 'Names', [
                toggle('hideUnfocusedNames', 'Mostrar nomes dos personagens', 'Show character names', {
                    invert: true, ...info('Controla os nomes de personagens sem foco.', 'Controls names for characters that are not focused.')
                }),
                toggle(null, 'Jogadores', 'Players', yes),
                toggle(null, 'NPCs', 'NPCs', yes),
                toggle(null, 'Monstros e mascotes', 'Monsters and pets', yes),
                toggle(null, 'Mostrar meu nome sobre o personagem', 'Show my character name', { help: true }),
                select(null, 'Cor do meu nome', 'My name color', [
                    opt('White', 'Branco', 'White'), opt('Gold', 'Dourado', 'Gold'), opt('Green', 'Verde', 'Green'),
                    opt('Blue', 'Azul', 'Blue'), opt('Pink', 'Rosa', 'Pink'), opt('Orange', 'Laranja', 'Orange')
                ]),
                toggle('hideDamageNumbers', 'Mostrar Dano', 'Show damage', { invert: true })
            ]),
            card('Números de dano', 'Damage numbers', [
                select(null, 'Tamanho dos números', 'Number size', [
                    opt('Small', 'Tamanho: pequeno', 'Size: small'), opt('Normal', 'Tamanho: normal', 'Size: normal'),
                    opt('Large', 'Tamanho: grande', 'Size: large'), opt('Huge', 'Tamanho: muito grande', 'Size: very large')
                ], { wide: true, fallback: 'Normal' }),
                toggle(null, 'Ícone do elemento', 'Element icon', yes),
                toggle(null, 'Dano de outros jogadores', 'Damage from other players', yes),
                toggle(null, 'Colorir número pelo elemento', 'Color numbers by element'),
                toggle(null, 'Espalhar números', 'Spread numbers'),
                toggle(null, 'Mostrar cura de poções', 'Show healing from potions', yes),
                toggle(null, 'Mostrar Item Equipado', 'Show equipped item', yes)
            ]),
            card('Câmera', 'Camera', [
                caption('Câmera em dungeons', 'Dungeon camera'),
                select(null, 'Câmera em dungeons', 'Dungeon camera', [
                    opt('2D', '2D (vista de cima)', '2D (top-down)'), opt('3D', '3D (câmera normal)', '3D (normal camera)')
                ], { wide: true, fallback: '3D' }),
                caption('Vale só dentro das dungeons.', 'Applies only inside dungeons.')
            ])
        ]
    ],
    audio: [
        [
            card('Música de fundo', 'Background music', [
                toggle('musicEnabled', 'Ativar música', 'Enable music'), volume('musicVolumePercent', 100)
            ]),
            card('Chat de voz', 'Voice chat', [
                toggle(null, 'Ouvir voz dos jogadores', 'Hear player voice', yes), volume(null, 80, true),
                caption('Ouvir os canais:', 'Listen to channels:'),
                toggle(null, 'Perto', 'Nearby', yes), toggle(null, 'Grupo', 'Party', yes),
                toggle(null, 'Clã', 'Clan', yes), toggle(null, 'Todos no mapa', 'Everyone on the map', yes)
            ])
        ],
        [
            card('Efeitos sonoros', 'Sound effects', [
                toggle('soundEnabled', 'Ativar efeitos sonoros', 'Enable sound effects', { unavailable: true }),
                volume('soundVolumePercent', 100, true)
            ]),
            card('Microfone', 'Microphone', [
                caption('Falar para:', 'Speak to:'),
                select(null, 'Falar para', 'Speak to', [
                    opt('Nearby', 'Perto (+ grupo)', 'Nearby (+ party)'), opt('Party', 'Só o grupo', 'Party only'),
                    opt('Clan', 'Só o clã', 'Clan only'), opt('Map', 'Todos no mapa', 'Everyone on the map')
                ], { wide: true }),
                caption('Modo:', 'Mode:'),
                select(null, 'Modo do microfone', 'Microphone mode', [
                    opt('Hold', 'Pressionar para falar (padrão)', 'Push to talk (default)'), opt('Open', 'Microfone aberto', 'Open microphone')
                ], { wide: true }),
                { keyDisplay: 'G', label: say('Tecla de falar:', 'Speak key:') },
                caption('Dispositivo:', 'Device:'),
                select(null, 'Dispositivo', 'Device', [opt('Default', 'Padrão do sistema', 'System default')], { wide: true })
            ])
        ]
    ],
    other: [
        [
            card('Idioma', 'Language', [
                select('language', 'Idioma do jogo', 'Game language', languages.map((entry, index) =>
                    index < 2 ? entry : [entry[0], entry[1], true]), { wide: true }),
                caption('Traduzir para:', 'Translate to:'),
                select(null, 'Traduzir para', 'Translate to', [opt('Game', 'Idioma do jogo', 'Game language'), ...languages], { wide: true })
            ]),
            card('Jogo', 'Game', [
                toggle('showMinimap', 'Mostrar minimapa', 'Show minimap', yes),
                toggle(null, 'Correr ao andar (R)', 'Run when moving (R)', yes),
                toggle(null, 'Prioridade jogador (PvP)', 'Prioritize players (PvP)', { help: true }),
                toggle(null, 'Confirmar antes de fechar o jogo', 'Confirm before closing the game', { ...yes, help: true }),
                toggle(null, 'Avisar quando meu personagem morrer', 'Notify me when my character dies', { help: true })
            ]),
            card('Interface', 'Interface', [
                select(null, 'Tipo de interface', 'Interface type', [
                    opt('Auto', 'Automática (PC)', 'Automatic (PC)'), opt('PC', 'PC (mouse e teclado)', 'PC (mouse and keyboard)'),
                    opt('Touch', 'Celular / tablet (toque)', 'Phone / tablet (touch)')
                ], { wide: true }),
                caption('Perfil de interface para mouse e teclado ou para toque.', 'Interface layout for mouse and keyboard or touch.')
            ]),
            card('Missão guiada', 'Guided quests', [
                toggle('levelGuide', 'Alertas da missão guiada', 'Guided quest alerts', {
                    ...info('Mostra avisos do guia de nível do Noxius.', 'Shows notices from Noxius’s level guide.')
                }),
                toggle(null, 'Dicas do tutorial', 'Tutorial tips', { ...yes, help: true }),
                toggle(null, 'Guia no minimapa', 'Minimap guide', { ...yes, help: true })
            ]),
            card('Modo streamer', 'Streamer mode', [
                toggle(null, 'Modo streamer', 'Streamer mode', { help: true }),
                caption('Ocultar no chat:', 'Hide from chat:'),
                select(null, 'Ocultar no chat', 'Hide from chat', [
                    opt('Whispers', 'Só os sussurros', 'Whispers only'), opt('All', 'O chat inteiro', 'Entire chat')
                ], { wide: true })
            ]),
            card('Janelas', 'Windows', [
                action('Resetar posição das janelas', 'Reset window positions', 'Volta as janelas à posição padrão.', 'Returns windows to their default positions.'),
                action('Resetar interface', 'Reset interface', 'Restaura a posição e a escala dos elementos do HUD.', 'Resets HUD layout and scale.')
            ])
        ],
        [
            card('Câmera', 'Camera', [
                toggle('cameraMouseButtons', 'Girar câmera com o mouse (Mouse 4 / Mouse 5)', 'Rotate camera with mouse buttons 4 and 5', {
                    kind: 'side-buttons', ...info('Ativa os atalhos padrão de giro nos botões laterais.', 'Enables default rotation on the side buttons.')
                }),
                toggle('invertCameraOrbit', 'Inverter giro da câmera', 'Invert camera rotation', info('Inverte os controles de giro.', 'Reverses camera rotation controls.')),
                toggle('invertCameraScroll', 'Inverter rolagem da câmera', 'Invert camera scroll', info('Inverte o sentido da roda do mouse.', 'Reverses the mouse wheel direction.'))
            ]),
            card('Notificações do chat', 'Chat notifications', [
                caption('Escolha quais avisos aparecem no chat e no topo da tela.', 'Choose which notices appear in chat and at the top of the screen.'),
                ...[
                    ['Aging de outros jogadores', 'Aging from other players'],
                    ['Vencedores da Roleta de boss', 'Boss roulette winners'],
                    ['Bosses: aviso e nascimento', 'Boss warnings and spawns']
                ].map(([pt, en]) => toggle(null, pt, en, { ...yes, help: true })),
                toggle('hideGlobalBuffNotices', 'Buffs globais', 'Global buffs', { invert: true }),
                toggle('hideCastleNotices', 'Castelo Bless', 'Bless Castle', { invert: true }),
                ...[
                    ['Arenas: abertura e resultados', 'Arena opening and results'],
                    ['Battle Royale', 'Battle Royale']
                ].map(([pt, en]) => toggle(null, pt, en, { ...yes, help: true })),
                toggle('hideCalendarNotices', 'Eventos e calendário', 'Events and calendar', { invert: true }),
                toggle(null, 'Duelos de outros jogadores', 'Duels from other players', { ...yes, help: true }),
                toggle('hideClanMembershipNotices', 'Clã: membro entrou no jogo', 'Clan member joined', { invert: true }),
                ...[
                    ['Títulos ganhos e perdidos', 'Titles gained and lost'],
                    ['Level up de outros jogadores', 'Other players leveling up']
                ].map(([pt, en]) => toggle(null, pt, en, { ...yes, help: true })),
                toggle('showExperienceNotices', 'EXP ganha (monstros e quests)', 'Experience gained (monsters and quests)'),
                caption('Aviso de abertura do Bellatra:', 'Bellatra opening notice:'),
                select(null, 'Aviso de abertura do Bellatra', 'Bellatra opening notice', [
                    opt('Both', 'Chat e tela', 'Chat and screen'), opt('Chat', 'Só chat', 'Chat only'), off
                ], { wide: true }),
                caption('Alertas de loot do grupo:', 'Party loot alerts:'),
                select(null, 'Alertas de loot do grupo', 'Party loot alerts', [
                    opt('All', 'Todos os itens', 'All items'), opt('Rare', 'Só itens raros', 'Rare items only'), off
                ], { wide: true })
            ]),
            card('Teclado', 'Keyboard', [
                caption('Clique no chão para andar ou atacar. Enter abre o chat; Esc fecha janelas.',
                    'Click the ground to move or attack. Enter opens chat; Esc closes windows.'),
                caption('Botão direito e roda do mouse controlam a câmera.', 'Use the right mouse button and wheel to control the camera.'),
                caption('Consulte a aba Teclas para ver os atalhos do Noxius.', 'See the Keys tab for Noxius shortcuts.')
            ]),
            card('Gráficos avançados do Noxius', 'Noxius advanced graphics', [
                toggle('realisticSky', 'Céu realista', 'Realistic sky'),
                select('cloudQuality', 'Nuvens', 'Clouds', [
                    opt(0, 'Desligado', 'Off'), opt(1, 'Baixa', 'Low'), opt(2, 'Média', 'Medium'), opt(3, 'Alta', 'High')
                ], { preset: true }),
                toggle('weatherEnabled', 'Clima (chuva e trovões)', 'Weather (rain and thunder)'),
                select('shadowUpdateRate', 'Animação das sombras', 'Shadow animation',
                    [[30, '30 Hz'], [60, '60 Hz'], [120, '120 Hz'], opt(0, 'Todo quadro', 'Every frame')], { preset: true }),
                select('bloom', 'Brilho (bloom)', 'Bloom', [off, opt('Low', 'Baixo', 'Low'), opt('High', 'Alto', 'High')], { preset: true }),
                toggle('ambientOcclusion', 'Oclusão de ambiente', 'Ambient occlusion', { preset: true }),
                toggle('contactShadows', 'Sombras de contato', 'Contact shadows', { preset: true }),
                toggle('volumetricLight', 'Luz volumétrica', 'Volumetric light', { preset: true }),
                toggle('colorGrading', 'Cores (LUT)', 'Color grading', { preset: true }),
                toggle('autoExposure', 'Exposição automática', 'Auto exposure', { preset: true })
            ]),
            card('Interface do Noxius', 'Noxius interface', [
                toggle('hideQuestTracker', 'Rastreador de missões', 'Quest tracker', { invert: true }),
                select('classicTargetHealthBar', 'Vida do alvo', 'Target HP', [
                    opt(false, 'Sempre visível', 'Always shown'), opt(true, 'Clássica', 'Classic')
                ]),
                select('classicNamePlates', 'Nomes sobre monstros', 'Name plates', [
                    opt(false, 'Compactos', 'Compact'), opt(true, 'Clássicos', 'Classic')
                ]),
                toggle('hideTradeChat', 'Chat de comércio', 'Trade chat', { invert: true })
            ]),
            card('Site do Noxius', 'Noxius website', [], { site: true })
        ]
    ]
};
