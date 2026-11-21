// The gear's Settings panel on the entry page (Jogar.cshtml, dialog data-role="settings"). It edits the game's settings
// as client-settings.json's document: the game hands it out (BrowserHost.Settings), applies presets and shortcut keys
// with its own rules, and applies a saved document at once (BrowserHost.SaveSettings). The saved document lives in the
// page's storage and goes to the game before it starts (BrowserHost.LoadSettings), since the browser has no settings file.

import { makeDraggable, resetDraggables } from './draggable.js';
import { dynamicResolutionLabel, normalizeRendererSettings, originalTextures, qualityChoices, qualityLabel, rowDisabled, say, setRendererOptions,
    shadowChoice, shadowSettings, shortcutColumns, shortcutGroups, tabs, textureQualityFor, unavailableShortcuts } from './settings-options.js';
import { createSettingsEffects } from './settings-effects.js';
import { word as phraseIn } from './page-language.js';

const storageKey = 'avalon.settings';
const profileKey = 'avalon.settings.quality';

const texts = {
    title: say('Configurações', 'Settings'),
    video: say('Vídeo', 'Video'),
    audio: say('Áudio', 'Audio'),
    keys: say('Teclas', 'Keys'),
    other: say('Outros', 'Other'),
    cancel: say('Cancelar', 'Cancel'),
    save: say('Salvar', 'Save'),
    close: say('Fechar', 'Close'),
    waiting: say('As configurações abrem assim que o jogo terminar de carregar.', 'Settings open once the game has loaded.'),
    saved: say('Configurações salvas.', 'Settings saved.'),
    pressKey: say('Pressione uma tecla… (Esc cancela)', 'Press a key… (Esc cancels)'),
    keyRefused: say('Essa tecla não pode ser usada nesse atalho.', 'That key cannot take this shortcut.'),
    restoreKeys: say('Restaurar padrão', 'Restore defaults'),
    keysHint: say('Clique em um atalho e pressione a nova tecla. Uma tecla já usada troca de lugar com a anterior.',
        'Click a shortcut and press the new key. A key already in use swaps with the old one.'),
    help: say('Mostrar explicação', 'Show explanation'),
    unavailable: say('Opção ainda indisponível no cliente web.', 'This option is currently unavailable in the web client.'),
    detect: say('Detectar', 'Detect'),
    detecting: say('Detectando…', 'Detecting…'),
    pc: say('Este PC: Baixo', 'This PC: Low'),
    default: say('Padrão', 'Default'),
    reset: say('Redefinir', 'Reset'),
    resetKey: say('Restaurar tecla padrão', 'Restore default key'),
    textureHint: say('Usa texturas menores na placa de vídeo: Leve usa metade da largura e altura; Muito leve, um quarto. Interface e ícones mantêm a qualidade.',
        'Uses smaller textures on the graphics card: Light uses half the width and height; Very light uses a quarter. Interface and icons retain their quality.'),
    textureAutomatic: say('Automático neste aparelho:', 'Automatic on this device:'),
    textureMemory: say('memória', 'memory'),
    textureOriginal: say('Original', 'Original'),
    textureCurrent: say('Texturas nesta sessão:', 'Textures in this session:'),
    textureReload: say('Uma mudança vale ao recarregar o jogo.', 'A change applies when the game reloads.'),
    needsDefaultLight: say('Vale só com a iluminação Padrão (novo).', 'Applies only with the Default (new) lighting.'),
    needsFog: say('Ligue a névoa para escolher a distância.', 'Turn the fog on to choose its distance.'),
    died: say('Seu personagem morreu.', 'Your character died.'),
    windowsReset: say('As janelas voltaram ao lugar.', 'Windows are back in place.'),
    pvpAsked: say('Pedido enviado ao servidor: a resposta aparece no chat.', 'Sent to the server: the answer shows in the chat.'),
    pvpOutside: say('Entre no jogo para ligar ou desligar o PvP.', 'Enter the game to turn PvP on or off.')
};


function load(key = storageKey) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

function store(value, key = storageKey) {
    try {
        localStorage.setItem(key, value);
    } catch {
        // Storage can be off (private windows, blocked site data); the settings then last for this visit.
    }
}

function element(tag, className, content) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (content !== undefined) node.textContent = content;
    return node;
}

export function createSettings(root, getHost, getLanguage, setLanguage, onApplied) {
    const dialog = root.querySelector('[data-role="settings"]');
    makeDraggable(dialog, dialog?.querySelector('.settings-head'));
    const opener = root.querySelector('[data-role="settings-open"]');
    if (!dialog || !opener) return { openingMusicEnabled: true, maxFps: 0, qualityLabel: qualityLabel('Auto', null, getLanguage()),
        dynamicResolutionEnabled: true, renderScale: 1, appliedRevision: 0,
        setAdapterInfo() {}, setRendererCapabilities() {}, setDynamicResolutionState() {}, start() {} };

    const part = role => dialog.querySelector(`[data-role="${role}"]`);
    const body = part('settings-body');
    const tabButtons = dialog.querySelectorAll('[role="tab"][data-tab]');
    const note = part('settings-note');
    const save = part('settings-save');
    const site = part('settings-site');
    const music = root.querySelector('[data-role="opening-music"]');
    const openingSound = root.querySelector('[data-role="opening-sound"]');
    const word = value => phraseIn(value, getLanguage());
    let tab = 'video';
    let draft = null;
    let active = parse(load()) ?? {};
    let appliedRevision = 0;
    let profile = 'Auto';
    let appliedProfile = readAppliedProfile(active);
    let capturing = -1;
    let adapterInfo = null;
    let rendererCapabilities;
    let detecting = false;
    const dynamicResolutionState = { enabled: false, scale: 0, width: 0, height: 0, shadowUpdateRate: 0 };
    let sessionTextures = 'Original';
    const effects = createSettingsEffects(root, getHost, () => active, () => word(texts.died));
    // The panel's reset buttons: the page's windows back to their places, and with them the HUD scale, the chat's place
    // and the interface options (ClientSettings.WithInterfaceReset).
    const actions = {
        'reset-windows': () => {
            resetDraggables();
            note.textContent = word(texts.windowsReset);
        },
        'reset-interface': () => {
            resetDraggables();
            draft = JSON.parse(getHost().ResetInterface(JSON.stringify(draft)));
            render();
        },
        'switch-pvp': () => {
            note.textContent = word(getHost()?.SwitchPvp?.() ? texts.pvpAsked : texts.pvpOutside);
        }
    };

    function shownQualityLabel() {
        return qualityLabel(appliedProfile, active.quality, getLanguage())
            + dynamicResolutionLabel(dynamicResolutionState, active.renderScale ?? 1, getLanguage(), active.shadowUpdateRate ?? 60);
    }

    function applyMusic(settings, wasDisabled = false) {
        if (!music || !settings) return;
        music.volume = 0.6 * Math.min(Math.max(settings.musicVolumePercent ?? 100, 0), 100) / 100;
        if (settings.musicEnabled === false) music.pause();
        else if (wasDisabled && openingSound && !openingSound.hidden) music.play().catch(() => {});
    }

    function parse(json) {
        try {
            return json ? JSON.parse(json) : null;
        } catch {
            return null;
        }
    }

    function readDraft(host) {
        if (!host) return null;
        const settings = parse(host.Settings());
        profile = appliedProfile;
        return normalizeRendererSettings({ ...settings, language: getLanguage() }, rendererCapabilities);
    }

    function readAppliedProfile(settings) {
        const saved = load(profileKey);
        if (qualityChoices.some(([value]) => value === saved)) return saved;
        if (!settings.quality || settings.quality === 'Low') return 'Auto';
        return settings.quality === 'Ultra' ? 'High' : settings.quality;
    }

    function selectProfile(chosen) {
        if (chosen === 'Custom') {
            draft.quality = 'Custom';
        } else {
            const preset = chosen === 'Auto' || chosen === 'VeryLow' ? 'Low' : chosen;
            const settings = JSON.parse(getHost().SettingsWithQuality(JSON.stringify(draft), preset));
            draft = normalizeRendererSettings({ ...settings, language: draft.language }, rendererCapabilities);
            if (chosen === 'VeryLow') {
                Object.assign(draft, { quality: 'Custom', shadowsEnabled: false, anisotropicFilter: 2 });
            }
        }
        profile = chosen;
        render();
    }

    function shadowSelection() {
        return shadowChoice(draft);
    }

    function change(row, value) {
        if (row.kind === 'shadow') {
            Object.assign(draft, shadowSettings(value));
        } else if (row.kind === 'window') {
            draft.windowMode = value ? 'Windowed' : 'Fullscreen';
        } else if (row.kind === 'fog') {
            draft.distanceFog = value ? 'Atmospheric' : 'Off';
            render();
        } else if (row.kind === 'reflection') {
            draft.screenSpaceReflections = value !== 'Off';
            draft.sceneryOnlyReflections = value === 'Scenery';
        } else if (row.kind === 'side-buttons') {
            draft.cameraMouseButtons = value ? (draft.cameraMouseButtons === 2 ? 0 : draft.cameraMouseButtons) : 2;
        } else {
            draft[row.field] = value;
        }

        if (!row.preset) return;
        draft.quality = 'Custom';
        profile = 'Custom';
        const quality = body.querySelector('.settings-card-quality select');
        if (quality) quality.value = String(qualityChoices.findIndex(([value]) => value === 'Custom'));
    }

    function selectRow(label, choices, current, onChange, { wide = false, unavailable = false } = {}) {
        const line = element('label', wide ? 'settings-row settings-row-wide' : 'settings-row');
        if (!wide) line.append(element('span', 'settings-label', label));
        const select = element('select', 'settings-select');
        select.setAttribute('aria-label', label);
        if (unavailable) {
            select.disabled = true;
            line.title = word(texts.unavailable);
        }
        choices.forEach(([value, text, disabled], index) => {
            // A choice this browser cannot use is left out, unless it is the saved one.
            if (disabled && value !== current) return;
            const option = element('option', '', word(text));
            option.value = String(index);
            option.selected = value === current;
            option.disabled = !!disabled;
            select.append(option);
        });
        select.addEventListener('change', () => {
            const choice = choices[Number(select.value)];
            if (choice && !choice[2] && !unavailable) onChange(choice[0]);
        });
        line.append(select);
        return line;
    }

    function rowFor(row) {
        if (row.hidden || row.serverTranslation && !getHost()?.ChatTranslationAvailable?.()) return null;
        if (row.caption) return element('p', 'settings-group-caption', word(row.caption));
        if (row.pair) {
            const pair = element('div', 'settings-option-pair');
            pair.append(...row.pair.map(rowFor).filter(Boolean));
            return pair;
        }
        if (row.action || row.keyDisplay) {
            const line = element('div', row.action ? 'settings-action' : 'settings-row settings-key');
            const description = element('div', 'settings-label', word(row.action ?? row.label));
            if (row.help) description.append(element('p', 'settings-hint', word(row.help)));
            const button = element('button', row.action ? 'settings-mini-button' : 'settings-keycap',
                row.action ? word(texts.reset) : row.keyDisplay);
            button.type = 'button';
            const run = actions[row.run];
            button.disabled = !run;
            if (run) button.addEventListener('click', () => run());
            else line.title = word(texts.unavailable);
            line.append(description, button);
            return line;
        }
        const disabled = rowDisabled(row, draft);
        let value = draft[row.field] ?? row.fallback;
        if (row.kind === 'shadow') value = shadowSelection();
        if (row.kind === 'reflection') value = !value ? 'Off' : draft.sceneryOnlyReflections ? 'Scenery' : true;
        if (row.kind === 'fog') value = value !== 'Off';
        if (row.kind === 'side-buttons') value = value !== 2;
        if (row.kind === 'window') value = value !== 'Fullscreen';
        if (row.toggle) {
            const line = element('label', 'settings-row settings-toggle');
            const input = element('input');
            input.type = 'checkbox';
            input.setAttribute('role', 'switch');
            input.setAttribute('aria-label', word(row.label));
            input.checked = row.invert ? !value : !!value;
            input.disabled = disabled;
            if (disabled) {
                line.title = word(row.unavailable ? texts.unavailable : row.defaultLight ? texts.needsDefaultLight : texts.needsFog);
                line.classList.add('settings-unavailable');
            }
            input.addEventListener('change', () => change(row, row.invert ? !input.checked : input.checked));
            line.append(input, element('span', 'settings-switch'), element('span', 'settings-label', word(row.label)));
            if (!row.help) return line;
            const help = element('button', 'settings-help', '?');
            const hint = element('p', 'settings-help-text', word(row.help === true ? texts.unavailable : row.help));
            help.type = 'button';
            help.setAttribute('aria-label', word(texts.help));
            help.setAttribute('aria-expanded', 'false');
            hint.hidden = true;
            // The button sits inside the switch's label, so a click must not reach the label and flip the switch.
            help.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                hint.hidden = !hint.hidden;
                help.setAttribute('aria-expanded', String(!hint.hidden));
            });
            line.append(help);
            const group = element('div', 'settings-help-group');
            group.append(line, hint);
            return group;
        }

        if (row.hudScale) {
            const line = element('div', 'settings-row settings-range settings-range-hud');
            const label = word(row.label);
            const input = element('input');
            input.type = 'range';
            input.min = '75';
            input.max = '150';
            input.step = '5';
            input.value = String(Math.round((value ?? 1) * 100));
            input.setAttribute('aria-label', label);
            const shown = element('output', 'settings-range-value');
            const show = () => {
                shown.textContent = `${input.value}%`;
                input.style.setProperty('--settings-range-fill', `${(Number(input.value) - 75) / 0.75}%`);
            };
            show();
            input.addEventListener('input', () => {
                show();
                change(row, Number(input.value) / 100);
            });
            const reset = element('button', 'settings-reset', word(texts.default));
            reset.type = 'button';
            reset.addEventListener('click', () => {
                input.value = '100';
                show();
                change(row, 1);
            });
            line.append(element('span', 'settings-label', label), input, shown, reset);
            return line;
        }

        if (row.range) {
            const volume = row.range === 'volume';
            const percent = volume || row.range === 'percent';
            const line = element('label', 'settings-row settings-range settings-range-simple');
            const input = element('input');
            const min = percent ? 0 : 1000;
            const max = volume ? 100 : percent ? 200 : 4000;
            input.type = 'range';
            input.min = String(min);
            input.max = String(max);
            input.step = percent ? '10' : '200';
            input.value = String(value ?? row.fallback ?? max);
            input.disabled = disabled;
            input.setAttribute('aria-label', word(row.label));
            if (disabled) line.title = word(row.unavailable ? texts.unavailable : row.defaultLight ? texts.needsDefaultLight : texts.needsFog);
            if (!volume) {
                line.classList.add('settings-range-caption');
                line.append(element('span', 'settings-label', word(row.label)));
            }
            const shown = element('output', 'settings-range-value');
            function updateRange() {
                shown.textContent = percent ? input.value + '%' : input.value;
                input.style.setProperty('--settings-range-fill', (100 * (Number(input.value) - min) / (max - min)) + '%');
            }
            updateRange();
            input.addEventListener('input', () => {
                updateRange();
                change(row, Number(input.value));
            });
            line.append(input, shown);
            return line;
        }

        const current = row.options.some(([option]) => option === value) ? value : row.options[0][0];
        return selectRow(word(row.label), row.options, current, chosen => {
            change(row, chosen);
            if (row.rerender) render();
        }, { ...row, unavailable: disabled });
    }

    function texturesFor(box, title) {
        box.append(selectRow(title, originalTextures, draft.textureQuality ?? 'Auto', chosen => {
            draft.textureQuality = chosen;
        }, { wide: true }));
        const memory = navigator.deviceMemory;
        const automatic = textureQualityFor('Auto', memory);
        const name = quality => word(originalTextures.find(([value]) => value === quality)?.[1] ?? texts.textureOriginal);
        const device = word(texts.textureAutomatic) + ' ' + name(automatic) + (memory ? ` (${word(texts.textureMemory)} ${memory} GB).` : '.');
        box.append(...[word(texts.textureHint), device, word(texts.textureCurrent) + ' ' + name(sessionTextures) + '.', word(texts.textureReload)]
            .map(hint => element('p', 'settings-hint', hint)));
    }

    function adapterLabel(info) {
        return info?.description || [info?.vendor, info?.architecture].filter(Boolean).join(' ');
    }

    function showAdapter(label) {
        const name = adapterLabel(adapterInfo);
        label.textContent = word(texts.pc) + (name ? ` — ${name}` : '');
    }

    function deviceDiagnostics() {
        const line = element('div', 'settings-diagnostics');
        const label = element('p', 'settings-hint');
        const button = element('button', 'settings-mini-button', word(detecting ? texts.detecting : texts.detect));
        button.type = 'button';
        button.disabled = detecting || !navigator.gpu;
        showAdapter(label);
        button.addEventListener('click', async () => {
            detecting = true;
            button.disabled = true;
            button.textContent = word(texts.detecting);
            try {
                adapterInfo = (await navigator.gpu.requestAdapter())?.info ?? adapterInfo;
            } catch {
                // Adapter details may be restricted by the browser.
            } finally {
                detecting = false;
                const current = body.querySelector('.settings-diagnostics');
                if (current) current.replaceWith(deviceDiagnostics());
            }
        });
        line.append(label, button);
        return line;
    }

    function cardFor(card) {
        const box = element('section', 'settings-card');
        if (card.className) box.classList.add(`settings-card-${card.className}`);
        const title = element('h3', 'settings-card-title');
        title.append(element('span', 'settings-diamond'), element('span', '', word(card.title)));
        box.append(title);
        if (card.textures) texturesFor(box, word(card.title));
        if (card.quality) {
            const chosen = qualityChoices.some(([key]) => key === profile) ? profile : 'Custom';
            const line = selectRow(word(card.title), qualityChoices, chosen, selectProfile, { wide: true });
            box.append(line, deviceDiagnostics());
        }
        if (card.hint) box.append(element('p', 'settings-hint', word(card.hint)));
        // A card whose every row this browser cannot offer is left out.
        if (card.rows.length > 0 && card.rows.every(row => row.hidden || row.caption)) return null;
        box.append(...card.rows.map(rowFor).filter(Boolean));
        if (card.site) {
            const links = element('nav', 'settings-links');
            links.setAttribute('aria-label', word(card.title));
            for (const link of site.querySelectorAll('a')) links.append(link.cloneNode(true));
            box.append(links);
        }
        return box;
    }

    function keysTab() {
        const host = getHost();
        const shortcuts = JSON.parse(host.Shortcuts(JSON.stringify(draft), getLanguage()));
        const top = element('div', 'settings-key-top');
        const restore = element('button', 'settings-mini-button settings-restore', word(texts.restoreKeys));
        restore.type = 'button';
        restore.addEventListener('click', () => {
            capturing = -1;
            draft = JSON.parse(host.RestoreShortcuts(JSON.stringify(draft)));
            render();
        });
        top.append(element('p', 'settings-hint', word(texts.keysHint)), restore);
        const stacks = element('div', 'settings-key-groups');

        for (const groups of shortcutColumns) {
            const stack = element('div', 'settings-column');
            for (const group of groups) {
                const box = cardFor({ title: shortcutGroups[group], rows: [], className: 'keys' });
                const list = element('div', 'settings-keys');
                const rows = shortcuts.filter(row => row.group === group);
                let inserted = 0;
                for (const [position, label, key] of unavailableShortcuts[group] ?? []) {
                    rows.splice(position + inserted, 0, { name: word(label), key, defaultKey: key, available: false });
                    inserted++;
                }
                for (const shortcut of rows) {
                    list.append(shortcutRow(shortcut, host));
                }
                box.append(list);
                stack.append(box);
            }
            stacks.append(stack);
        }
        return [top, stacks];
    }

    function shortcutRow(shortcut, host) {
        const { slot, name, key, defaultKey, available } = shortcut;
        const line = element('div', 'settings-row settings-key');
        const label = element('span', 'settings-label', name);
        const button = element('button', 'settings-keycap', available && slot === capturing ? '…' : key);
        button.type = 'button';
        button.disabled = !available;
        button.dataset.capturing = String(available && slot === capturing);
        button.setAttribute('aria-label', name + ' · ' + key);
        button.addEventListener('click', () => {
            capturing = slot;
            note.textContent = word(texts.pressKey);
            render();
            body.querySelector('[data-capturing="true"]')?.focus();
        });
        if (!available) line.title = word(texts.unavailable);

        const reset = element('button', 'settings-key-reset', '↺');
        reset.type = 'button';
        reset.title = word(texts.default) + ' · ' + defaultKey;
        reset.setAttribute('aria-label', word(texts.resetKey) + ' · ' + name);
        reset.disabled = !available || key === defaultKey;
        reset.addEventListener('click', () => {
            const restored = host.ResetShortcut(JSON.stringify(draft), slot);
            if (!restored) {
                note.textContent = word(texts.keyRefused);
                return;
            }
            capturing = -1;
            draft = JSON.parse(restored);
            render();
        });
        line.append(label, button, reset);
        return line;
    }

    function render() {
        const scroll = body.scrollTop;
        body.replaceChildren();
        for (const button of tabButtons) {
            button.setAttribute('aria-selected', String(button.dataset.tab === tab));
        }
        if (!draft) {
            body.append(element('p', 'settings-hint settings-waiting', word(texts.waiting)));
        } else if (tab === 'keys') {
            body.append(...keysTab());
        } else {
            for (const column of tabs[tab]) {
                const stack = element('div', 'settings-column');
                for (const card of column) {
                    const box = cardFor(card);
                    if (box) stack.append(box);
                }
                body.append(stack);
            }
        }
        body.dataset.tab = tab;
        body.scrollTop = scroll;
        save.disabled = !draft;
    }

    function translate() {
        for (const node of dialog.querySelectorAll('[data-settings-text]')) {
            node.textContent = word(texts[node.dataset.settingsText]);
        }
        part('settings-close').setAttribute('aria-label', word(texts.close));
        for (const button of tabButtons) {
            button.setAttribute('aria-label', word(texts[button.dataset.tab]));
        }
    }

    function open() {
        const host = getHost();
        draft = readDraft(host);
        capturing = -1;
        tab = 'video';
        body.scrollTop = 0;
        note.textContent = '';
        translate();
        render();
        dialog.showModal();
    }

    opener.addEventListener('click', open);
    part('settings-close').addEventListener('click', () => dialog.close());
    part('settings-cancel').addEventListener('click', () => dialog.close());
    save.addEventListener('click', () => {
        const host = getHost();
        if (!host || !draft) return;
        const wasDisabled = parse(host.Settings())?.musicEnabled === false;
        const chosenLanguage = draft.language;
        const saved = host.SaveSettings(JSON.stringify(normalizeRendererSettings(draft, rendererCapabilities)));
        store(saved);
        store(profile, profileKey);
        active = parse(saved) ?? {};
        appliedRevision++;
        appliedProfile = profile;
        applyMusic(active, wasDisabled);
        effects.applied(active);
        onApplied?.();
        note.textContent = word(texts.saved);
        dialog.close();
        if (chosenLanguage !== getLanguage()) setLanguage(chosenLanguage);
    });
    for (const button of tabButtons) {
        button.addEventListener('click', () => {
            tab = button.dataset.tab;
            capturing = -1;
            note.textContent = '';
            body.scrollTop = 0;
            render();
        });
    }

    // While a shortcut waits for its key, the keys go to it; Escape cancels the wait instead of closing the panel.
    dialog.addEventListener('keydown', event => {
        if (capturing < 0) return;
        event.preventDefault();
        event.stopPropagation();
        if (event.key === 'Escape') {
            capturing = -1;
            note.textContent = '';
            render();
            return;
        }
        if (['Shift', 'Alt', 'Control', 'Meta', 'AltGraph'].includes(event.key)) return;
        const keyCode = { 59: 186, 61: 187, 173: 189 }[event.keyCode] ?? event.keyCode;
        const rebound = event.ctrlKey || event.metaKey ? '' :
            getHost().BindShortcut(JSON.stringify(draft), capturing, keyCode, event.shiftKey, event.altKey);
        if (!rebound) {
            note.textContent = word(texts.keyRefused);
            return;
        }
        draft = JSON.parse(rebound);
        capturing = -1;
        note.textContent = '';
        render();
    });
    dialog.addEventListener('cancel', event => {
        if (capturing >= 0) event.preventDefault();
    });
    dialog.addEventListener('close', () => {
        capturing = -1;
        canvasFocus();
    });

    function canvasFocus() {
        root.querySelector('canvas')?.focus({ preventScroll: true });
    }

    const startSettings = parse(load());
    applyMusic(startSettings ?? {});

    return {
        openingMusicEnabled: startSettings?.musicEnabled !== false,
        get maxFps() { return active.maxFps ?? 0; },
        get appliedRevision() { return appliedRevision; },
        get dynamicResolutionEnabled() { return active.dynamicResolution !== false; },
        get renderScale() { return active.renderScale ?? 1; },
        get maxPixelRatio() { return active.maxPixelRatio ?? 2; },
        get classicLighting() { return active.classicLighting === true; },
        get qualityLabel() { return shownQualityLabel(); },
        get interfaceMode() { return active.interfaceMode ?? 'Auto'; },
        // The interface type answered in the touch HUD's first question, saved as the panel saves it.
        setInterfaceMode(mode) {
            const host = getHost();
            const current = host && parse(host.Settings());
            if (!current) return;
            const saved = host.SaveSettings(JSON.stringify({ ...current, interfaceMode: mode }));
            store(saved);
            active = parse(saved) ?? active;
            appliedRevision++;
            onApplied?.();
        },
        setDynamicResolutionState(state) {
            const enabled = state?.enabled === true;
            const valid = enabled && Number.isFinite(state.scale) && state.scale > 0
                && Number.isInteger(state.width) && state.width > 0 && Number.isInteger(state.height) && state.height > 0;
            const scale = valid ? state.scale : 0, width = valid ? state.width : 0, height = valid ? state.height : 0;
            const shadowUpdateRate = enabled && Number.isInteger(state.shadowUpdateRate) && state.shadowUpdateRate > 0 ? state.shadowUpdateRate : 0;
            if (dynamicResolutionState.enabled === enabled && dynamicResolutionState.scale === scale
                && dynamicResolutionState.width === width && dynamicResolutionState.height === height
                && dynamicResolutionState.shadowUpdateRate === shadowUpdateRate) return;
            const before = shownQualityLabel();
            dynamicResolutionState.enabled = enabled;
            dynamicResolutionState.scale = scale;
            dynamicResolutionState.width = width;
            dynamicResolutionState.height = height;
            dynamicResolutionState.shadowUpdateRate = shadowUpdateRate;
            if (shownQualityLabel() !== before) onApplied?.();
        },
        setRendererCapabilities(capabilities) {
            rendererCapabilities = capabilities;
            setRendererOptions(capabilities);
        },
        setAdapterInfo(info) {
            adapterInfo = info;
            const label = body.querySelector('.settings-diagnostics .settings-hint');
            if (label) showAdapter(label);
        },
        // Before Start: the saved settings become the ones the game starts with.
        start(host) {
            appliedRevision++;
            const saved = load();
            if (saved) host.LoadSettings(saved);
            active = parse(host.Settings()) ?? {};
            sessionTextures = textureQualityFor(active.textureQuality, navigator.deviceMemory);
            host.SetTextureQuality(sessionTextures);
            effects.start();
            const supported = normalizeRendererSettings(active, rendererCapabilities);
            if (supported !== active) {
                const applied = host.SaveSettings(JSON.stringify(supported));
                store(applied);
                active = parse(applied) ?? supported;
            }
            appliedProfile = readAppliedProfile(active);
            onApplied?.();
            if (dialog.open && !draft) {
                draft = readDraft(host);
                render();
            }
            // A change made in the game's own panel is kept too.
            addEventListener('pagehide', () => store(host.Settings()));
        }
    };
}
