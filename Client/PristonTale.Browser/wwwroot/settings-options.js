export const say = (pt, en) => ({ 'pt-BR': pt, en });
const opt = (value, pt, en = pt, unavailable = false) => [value, say(pt, en), unavailable];
const card = (pt, en, rows, meta = {}) => ({ title: say(pt, en), rows, ...meta });
const toggle = (field, pt, en, extra = {}) => ({ field, label: say(pt, en), toggle: true, unavailable: !field, ...extra });
const select = (field, pt, en, options, extra = {}) => ({ field, label: say(pt, en), options, unavailable: !field, ...extra });
const caption = (pt, en) => ({ caption: say(pt, en) });
const action = (pt, en, helpPt, helpEn, run) => ({ action: say(pt, en), help: say(helpPt, helpEn), run });
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
            // What this renderer cannot draw is not offered at all; the saved value stays for a renderer that can.
            if (capabilities.unsupported?.[features[row.field]]) row.unavailable = row.hidden = true;
        }
    }
}

// The browser draws one sun shadow map, so a shadow choice is its size; Simple keeps the shadows to characters.
const shadowSizes = { High: 4096, Medium: 2048, Low: 1024, Simple: 2048 };

export function shadowChoice(settings) {
    if (!settings.shadowsEnabled) return 'Off';
    if (settings.actorShadowsOnly) return 'Simple';
    const size = settings.shadowMapSize ?? 2048;
    return size > 2048 ? 'High' : size < 2048 ? 'Low' : 'Medium';
}

export function shadowSettings(choice) {
    if (choice === 'Off') return { shadowsEnabled: false, actorShadowsOnly: false };
    return { shadowsEnabled: true, shadowMapSize: shadowSizes[choice] ?? 2048, actorShadowsOnly: choice === 'Simple' };
}

// Auto picks by the device's memory in GB (navigator.deviceMemory, absent on some browsers).
export function textureQualityFor(choice, memory) {
    if (choice && choice !== 'Auto') return choice;
    if (Number.isFinite(memory) && memory <= 2) return 'VeryLight';
    if (Number.isFinite(memory) && memory <= 4) return 'Light';
    return 'Original';
}

// A row the current draft makes pointless: the finishing effects under the classic light, the fog distance without fog.
export function rowDisabled(row, settings) {
    return !!(row.unavailable || (row.defaultLight && settings.classicLighting === true)
        || (row.needsFog && settings.distanceFog === 'Off'));
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
        [2, say('Ver item perfeito (segurar)', 'Preview perfect item (hold)'), 'P']
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
        [0, say('Câmera 2D / 3D (dungeons)', '2D / 3D dungeon camera'), 'Shift + Z']
    ]
};

// The languages the game has texts for (ClientSettings.SupportedLanguages).
const languages = [opt('pt-BR', 'Português', 'Portuguese'), opt('en', 'English')];

// What a side mouse button does (ClientSettings.CameraMouse4/5, the CameraMouseAction values).
const mouseActions = [
    opt(0, 'Padrão', 'Default'), opt(1, 'Nada', 'Nothing'), opt(2, 'Girar à esquerda', 'Rotate left'), opt(3, 'Girar à direita', 'Rotate right'),
    opt(4, 'Aproximar', 'Zoom in'), opt(5, 'Afastar', 'Zoom out'), opt(6, 'Inclinar para cima', 'Tilt up'), opt(7, 'Inclinar para baixo', 'Tilt down')
];

export const tabs = {
    video: [
        [
            card('Texturas', 'Textures', [], { className: 'textures', textures: true }),
            card('Qualidade gráfica', 'Graphics quality', [
                select('classicLighting', 'Iluminação', 'Lighting', [opt(false, 'Padrão (novo)', 'Default (new)'), opt(true, 'Clássico', 'Classic')],
                    { rerender: true }),
                select('renderScale', 'Escala de renderização', 'Render scale',
                    [100, 90, 80, 75, 70, 60, 50].map(value => [value / 100, value + '%']), { preset: true }),
                select('maxPixelRatio', 'Resolução máxima', 'Max. resolution', [
                    opt(2, '2x (tela retina)', '2x (retina display)'), opt(1.5, '1,5x', '1.5x'), [1, '1x']
                ], { preset: true }),
                select('shadowsEnabled', 'Sombras', 'Shadows', [
                    opt('High', 'Alta', 'High'), opt('Medium', 'Média', 'Medium'), opt('Low', 'Baixa', 'Low'),
                    opt('Simple', 'Simples (só personagens)', 'Simple (characters only)'), opt('Off', 'Desligadas', 'Off')
                ], { kind: 'shadow', preset: true }),
                select('screenSpaceReflections', 'Reflexo na água', 'Water reflection', [
                    opt(true, 'Completo', 'Full'), opt('Scenery', 'Só cenário', 'Scenery only'), off
                ], { kind: 'reflection', preset: true }),
                select('anisotropicFilter', 'Filtro anisotrópico', 'Anisotropic filter',
                    [[16, '16x'], [8, '8x'], [4, '4x'], [2, '2x'], opt(1, 'Desligado', 'Off')], { preset: true }),
                select('characterQuality', 'Qualidade dos personagens', 'Character quality', [opt('VeryHigh', 'Muito alta', 'Very high'),
                    opt('High', 'Alta', 'High'), opt('Medium', 'Média', 'Medium'), opt('Low', 'Baixa', 'Low')], { preset: true }),
                select('antiAliasing', 'Antisserrilhado', 'Anti-aliasing', [
                    off, opt('Msaa', 'MSAA', 'MSAA', true), ['Fxaa', 'FXAA'], ['Smaa', 'SMAA'], ['Taa', 'TAA']
                ], { preset: true }),
                toggle('reflectionsEveryOtherFrame', 'Reflexo a cada 2 quadros', 'Reflect every two frames', { ...yes, preset: true,
                    ...info('Calcula o reflexo na água em quadros alternados: custa metade e atrasa um quadro ao girar a câmera.',
                        'Traces the water reflection on alternate frames: half the cost, one frame behind while the camera turns.') }),
                toggle('dynamicResolution', 'Resolução dinâmica', 'Dynamic resolution', { ...yes,
                    ...info('Ajusta a resolução 3D para buscar 60 FPS (mínimo de 50%). A interface mantém a resolução da tela.',
                        'Adjusts 3D resolution toward 60 FPS (50% minimum). The interface retains the display resolution.') }),
                toggle('outlineGlow', 'Brilho de contorno', 'Outline glow', { ...yes,
                    ...info('Contorno do personagem, monstro ou NPC sob o cursor.', 'Outlines the character, monster or NPC under the cursor.') }),
                toggle('agingMixGlow', 'Brilho de aging e mix', 'Aging and mix glow', { ...yes,
                    ...info('Brilho colorido das armas e escudos com aging ou mix.', 'Coloured glow of weapons and shields with aging or mix.') }),
                toggle('limitOtherPlayerEffects', 'Limitar efeitos de outros jogadores', 'Limit effects from other players', { ...yes,
                    ...info('Não desenha habilidades de outros jogadores longe da câmera ou em excesso. As suas, as do seu alvo e as de monstros sempre aparecem.',
                        'Skips other players’ skills far from the camera or in excess. Yours, your target’s and monsters’ always show.') })
            ], { className: 'quality', quality: true }),
            card('Gráficos aprimorados', 'Enhanced graphics', [
                toggle('toneMapping', 'Mapeamento de tons', 'Tone mapping', { defaultLight: true }),
                toggle('colorGrading', 'Correção de cor (LUT)', 'Color grading', { preset: true, defaultLight: true }),
                toggle('vignette', 'Vinheta', 'Vignette', { defaultLight: true }),
                toggle('filmGrain', 'Granulação', 'Film grain', { defaultLight: true }),
                select('bloom', 'Bloom', 'Bloom', [off, opt('Low', 'Baixo', 'Low'), opt('High', 'Alto', 'High')], { preset: true, defaultLight: true }),
                { field: 'bloomIntensityPercent', label: say('Intensidade', 'Intensity'), range: 'percent', fallback: 100, defaultLight: true },
                caption('Valem com a iluminação Padrão (novo); a Clássico mantém a imagem do jogo original.',
                    'Apply with the Default (new) lighting; Classic keeps the original game\'s image.')
            ]),
            card('Limite de FPS', 'Frame limit', [
                select('maxFps', 'Limite de FPS', 'Frame limit', [
                    opt(0, 'Sem limite (VSync)', 'Unlimited (VSync)'), [120, '120 FPS'], [60, '60 FPS'], opt(30, '30 FPS (economia)', '30 FPS (economy)')
                ], { wide: true })
            ]),
            card('Exibição', 'Display', [
                toggle('windowMode', 'Modo janela', 'Window mode', { kind: 'window',
                    ...info('Desligado, o jogo ocupa a tela inteira (Esc sai).', 'Off, the game fills the whole screen (Esc leaves).') })
            ]),
            card('Efeitos', 'Effects', [
                toggle('hideSkillEffects', 'Forces, habilidades e emotes', 'Forces, skills and emotes', { invert: true,
                    ...info('Desligado, as habilidades de outros jogadores não são desenhadas; as suas e as de monstros sempre aparecem.',
                        'Off, other players’ skills are not drawn; yours and monsters’ always show.') }),
                toggle('grayscaleOnDeath', 'Tela cinza', 'Grayscale screen', info('A tela fica cinza enquanto o personagem está morto.',
                    'The screen turns grey while your character is dead.')),
                toggle('monsterDissolve', 'Efeito de dissolver', 'Dissolve effect', info('Monstros mortos somem aos poucos.',
                    'Dead monsters fade away.')),
                toggle('distanceFog', 'Névoa ativa', 'Enable fog', { kind: 'fog', ...info('Cobre o horizonte gradualmente.', 'Fades distant scenery into the horizon.') }),
                { field: 'fogDistance', label: say('Distância da névoa', 'Fog distance'), range: 'fog', fallback: 2208, needsFog: true },
                toggle('hideStealthEffect', 'Sem o efeito visual do Furtivo (Pikeman)', 'No Pikeman stealth visual', info(
                    'Personagens furtivos aparecem sem a transparência.', 'Stealthed characters show without the transparency.')),
                toggle('lowHealthWarning', 'Alerta de HP baixo', 'Low HP warning', info('A borda da tela pulsa em vermelho abaixo de 30% de vida.',
                    'The screen edge pulses red below 30% life.'))
            ])
        ],
        [
            card('Interface', 'Interface', [
                { field: 'uiScale', label: say('Escala do HUD', 'HUD scale'), hudScale: true },
                toggle('centeredInterface', 'Interface centralizada', 'Centered interface', info(
                    'A barra de baixo fica no centro da tela larga; desligada, à esquerda.', 'The bottom bar sits in the middle of a wide screen; off, at the left.')),
                toggle('chatFilter', 'Censura de chat', 'Chat filter', info('Palavrões no que os jogadores escrevem aparecem como ***.',
                    'Swear words in what players write show as ***.')),
                toggle('hideOtherPlayers', 'Esconder outros jogadores', 'Hide other players', info(
                    'Jogadores fora do seu grupo somem da tela e não podem ser clicados.', 'Players outside your party disappear and cannot be clicked.')),
                toggle('alwaysShowRareGroundLabels', 'Nomes dos itens raros no chão', 'Rare ground item names',
                    info('Exibe os nomes dos itens raros próximos no chão.', 'Shows nearby rare item names on the ground.')),
                toggle('alwaysShowGroundLabels', 'Nomes de todos os itens no chão', 'Names of all ground items',
                    info('Exibe os nomes dos itens próximos no chão.', 'Shows nearby item names on the ground.')),
                toggle('hideOnlineCount', 'Mostrar jogadores online', 'Show online players', { invert: true, help: say('Mostra a contagem no HUD.', 'Shows the count on the HUD.') }),
                toggle('showPartyDps', 'Mostrar DPS do grupo no alvo', 'Show party DPS on the target', info(
                    'O dano por segundo que você e seu grupo causam no alvo, nos últimos 5 segundos.', 'The damage per second you and your party deal to the target over the last 5 seconds.')),
                toggle('hideHudIcons', 'Ocultar ícones do HUD', 'Hide HUD icons', info('Esconde os atalhos do canto (Novidades, Auto-caça, Mais).',
                    'Hides the corner shortcuts (News, Auto-hunt, More).')),
                toggle('persistentCharacterInfo', 'Informações fixas sobre os personagens (Backspace)', 'Persistent character information (Backspace)', info(
                    'Mostra o nome de todos os jogadores, não só o do jogador sob o cursor.', 'Shows every player’s name, not only the hovered one’s.'))
            ], { className: 'interface' }),
            card('Nomes', 'Names', [
                toggle('hideUnfocusedNames', 'Mostrar nomes dos personagens', 'Show character names', {
                    invert: true, ...info('Desligado, só mostra o nome sob o cursor, do alvo e do grupo.', 'Off, only the hovered, the target’s and the party’s names show.')
                }),
                toggle('hidePlayerNames', 'Jogadores', 'Players', { invert: true }),
                toggle('hideNpcNames', 'NPCs', 'NPCs', { invert: true }),
                toggle('hideMonsterNames', 'Monstros e mascotes', 'Monsters and pets', { invert: true }),
                toggle('showOwnName', 'Mostrar meu nome sobre o personagem', 'Show my character name'),
                select('ownNameColor', 'Cor do meu nome', 'My name color', [
                    opt('White', 'Branco', 'White'), opt('Gold', 'Dourado', 'Gold'), opt('Green', 'Verde', 'Green'),
                    opt('Blue', 'Azul', 'Blue'), opt('Pink', 'Rosa', 'Pink'), opt('Orange', 'Laranja', 'Orange')
                ]),
                toggle('hideDamageNumbers', 'Mostrar dano', 'Show damage', { invert: true })
            ]),
            card('Números de dano', 'Damage numbers', [
                select('damageNumberSize', 'Tamanho dos números', 'Number size', [
                    opt('Small', 'Tamanho: pequeno', 'Size: small'), opt('Normal', 'Tamanho: normal', 'Size: normal'),
                    opt('Large', 'Tamanho: grande', 'Size: large'), opt('Huge', 'Tamanho: muito grande', 'Size: very large')
                ], { wide: true, fallback: 'Normal' }),
                toggle('showOtherPlayersDamage', 'Dano de outros jogadores', 'Damage from other players', { ...yes,
                    ...info('Mostra, menores e mais claros, os golpes entre outros jogadores e monstros.', 'Shows, smaller and lighter, hits between other players and monsters.') }),
                toggle('spreadDamageNumbers', 'Espalhar números', 'Spread numbers'),
                toggle('hidePotionHeals', 'Mostrar cura de poções', 'Show healing from potions', { invert: true })
            ])
        ]
    ],
    audio: [
        [
            card('Música de fundo', 'Background music', [
                toggle('musicEnabled', 'Ativar música', 'Enable music'), volume('musicVolumePercent', 100),
                caption('No navegador, vale para a música de abertura; o som dentro do jogo ainda não toca no navegador.',
                    'In the browser it applies to the opening music; in-game sound does not play in the browser yet.')
            ])
        ]
    ],
    other: [
        [
            card('Idioma', 'Language', [
                select('language', 'Idioma do jogo', 'Game language', languages, { wide: true })
            ]),
            card('Jogo', 'Game', [
                toggle('showMinimap', 'Mostrar minimapa', 'Show minimap', yes),
                toggle('runByDefault', 'Correr ao andar (R)', 'Run when moving (R)', { ...yes,
                    ...info('O personagem entra no mapa correndo; R troca entre correr e andar.', 'Your character enters each map running; R switches between running and walking.') }),
                toggle('prioritizePlayers', 'Prioridade a jogadores (PvP)', 'Prioritize players (PvP)', info(
                    'Um jogador sob o cursor tem prioridade sobre um monstro mais perto.', 'A player under the cursor wins over a nearer monster.')),
                toggle('confirmClose', 'Confirmar antes de fechar o jogo', 'Confirm before closing the game', { ...yes,
                    ...info('O navegador pergunta antes de fechar ou recarregar a aba durante o jogo.', 'The browser asks before closing or reloading the tab while you play.') }),
                toggle('notifyOnDeath', 'Avisar quando meu personagem morrer', 'Notify me when my character dies', info(
                    'Uma notificação do sistema quando o personagem morre com a aba em segundo plano.', 'A system notification when your character dies while the tab is in the background.'))
            ]),
            card('Interface', 'Interface', [
                select('interfaceMode', 'Tipo de interface', 'Interface type', [
                    opt('Auto', 'Automática', 'Automatic'), opt('Pc', 'PC (mouse e teclado)', 'PC (mouse and keyboard)'),
                    opt('Touch', 'Celular / tablet (toque)', 'Phone / tablet (touch)')
                ], { wide: true }),
                caption('Automática usa o HUD de toque em celulares e tablets e o de PC com mouse.',
                    'Automatic uses the touch HUD on phones and tablets and the PC one with a mouse.')
            ]),
            card('Missão guiada', 'Guided quests', [
                toggle('levelGuide', 'Alertas da missão guiada', 'Guided quest alerts', {
                    ...info('Mostra avisos do guia de nível do Noxius.', 'Shows notices from Noxius’s level guide.')
                }),
                toggle('minimapGuide', 'Guia no minimapa', 'Minimap guide', { ...yes,
                    ...info('Uma seta no minimapa aponta para o destino da missão que você está seguindo.', 'An arrow on the minimap points to the quest destination you follow.') })
            ]),
            card('Modo streamer', 'Streamer mode', [
                toggle('streamerMode', 'Modo streamer', 'Streamer mode', info('Esconde do chat as mensagens escolhidas abaixo.',
                    'Hides the messages chosen below from the chat.')),
                caption('Ocultar no chat:', 'Hide from chat:'),
                select('streamerHidesWholeChat', 'Ocultar no chat', 'Hide from chat', [
                    opt(false, 'Só os sussurros', 'Whispers only'), opt(true, 'O chat inteiro', 'Entire chat')
                ], { wide: true })
            ]),
            card('Janelas', 'Windows', [
                action('Redefinir posição das janelas', 'Reset window positions', 'Arraste as janelas pelo título; clique duplo no título volta uma janela ao lugar.',
                    'Drag windows by their title; double-click a title to put a window back.', 'reset-windows'),
                action('Redefinir interface', 'Reset interface', 'As janelas e a escala do HUD voltam ao padrão (salve para aplicar a escala).',
                    'Windows and the HUD scale return to default (save to apply the scale).', 'reset-interface')
            ])
        ],
        [
            card('Câmera', 'Camera', [
                toggle('cameraMouseButtons', 'Girar câmera com o mouse (Mouse 4 / Mouse 5)', 'Rotate camera with mouse buttons 4 and 5', {
                    kind: 'side-buttons', ...info('Ativa os atalhos padrão de giro nos botões laterais.', 'Enables default rotation on the side buttons.')
                }),
                select('cameraMouse4', 'Mouse 4', 'Mouse 4', mouseActions),
                select('cameraMouse5', 'Mouse 5', 'Mouse 5', mouseActions),
                toggle('invertCameraOrbit', 'Inverter giro da câmera', 'Invert camera rotation', info('Inverte os controles de giro.', 'Reverses camera rotation controls.')),
                toggle('invertCameraScroll', 'Inverter rolagem da câmera', 'Invert camera scroll', info('Inverte o sentido da roda do mouse.', 'Reverses the mouse wheel direction.'))
            ]),
            card('Notificações do chat', 'Chat notifications', [
                caption('Escolha quais avisos aparecem no chat e no topo da tela.', 'Choose which notices appear in chat and at the top of the screen.'),
                toggle('hideBossNotices', 'Bosses: aviso e nascimento', 'Boss warnings and spawns', { invert: true }),
                toggle('hideGlobalBuffNotices', 'Buffs globais', 'Global buffs', { invert: true }),
                toggle('showPremiumBuffNotices', 'Ativação de buffs premium', 'Premium buff activation'),
                toggle('hideCastleNotices', 'Castelo Bless', 'Bless Castle', { invert: true }),
                toggle('hideCalendarNotices', 'Eventos e calendário', 'Events and calendar', { invert: true }),
                toggle('hideEventRewardNotices', 'Recompensas de evento', 'Event rewards', { invert: true }),
                toggle('hideClanMembershipNotices', 'Clã: membro entrou no jogo', 'Clan member joined', { invert: true }),
                toggle('showExperienceNotices', 'EXP ganha (monstros e missões)', 'Experience gained (monsters and quests)'),
                toggle('hideBellatraResults', 'Bellatra: resultados', 'Bellatra results', { invert: true }),
                toggle('hideBellatraBossNotices', 'Bellatra: boss derrotado', 'Bellatra boss defeated', { invert: true }),
                toggle('hideBellatraTeamNotices', 'Bellatra: avisos da equipe', 'Bellatra team notices', { invert: true }),
                caption('Avisos de GM, de manutenção e da sua conta sempre aparecem.', 'GM, maintenance and account notices always show.')
            ]),
            card('Itens no chão', 'Ground items', [
                toggle('disableGroundLootFilter', 'Filtro de itens no chão', 'Ground item filter', { invert: true,
                    ...info('Desligado, todos os itens aparecem no chão.', 'Off, every item shows on the ground.') }),
                toggle('hideGroundGold', 'Ouro', 'Gold', { invert: true }),
                toggle('hideGroundPotions', 'Poções', 'Potions', { invert: true }),
                toggle('hideGroundWeapons', 'Armas', 'Weapons', { invert: true }),
                toggle('alwaysShowRareGroundEquipment', 'Sempre mostrar equipamentos raros', 'Always show rare equipment'),
                toggle('hideGroundEquipmentWithoutSpec', 'Esconder equipamentos sem especialização', 'Hide equipment without a spec')
            ]),
            card('Teclado', 'Keyboard', [
                caption('Clique no chão para andar ou atacar. Enter abre o chat; Esc fecha janelas.',
                    'Click the ground to move or attack. Enter opens chat; Esc closes windows.'),
                caption('Botão direito: câmera. Roda: zoom. Botões laterais (4 / 5): girar a câmera.',
                    'Right button: camera. Wheel: zoom. Side buttons (4 / 5): rotate the camera.'),
                caption('F12 esconde todo o HUD para prints; Backspace mostra o nome de todos os jogadores.',
                    'F12 hides the whole HUD for screenshots; Backspace shows every player’s name.'),
                caption('Consulte a aba Teclas para ver os demais atalhos.', 'See the Keys tab for the other shortcuts.')
            ]),
            card('Gráficos avançados do Noxius', 'Noxius advanced graphics', [
                toggle('realisticSky', 'Céu realista', 'Realistic sky'),
                select('cloudQuality', 'Nuvens', 'Clouds', [
                    opt(0, 'Desligado', 'Off'), opt(1, 'Baixa', 'Low'), opt(2, 'Média', 'Medium'), opt(3, 'Alta', 'High')
                ], { preset: true }),
                toggle('weatherEnabled', 'Clima (chuva e trovões)', 'Weather (rain and thunder)'),
                select('shadowUpdateRate', 'Animação das sombras', 'Shadow animation',
                    [[30, '30 Hz'], [60, '60 Hz'], [120, '120 Hz'], opt(0, 'Todo quadro', 'Every frame')], { preset: true }),
                toggle('ambientOcclusion', 'Oclusão de ambiente', 'Ambient occlusion', { preset: true }),
                toggle('contactShadows', 'Sombras de contato', 'Contact shadows', { preset: true }),
                toggle('volumetricLight', 'Luz volumétrica', 'Volumetric light', { preset: true }),
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
