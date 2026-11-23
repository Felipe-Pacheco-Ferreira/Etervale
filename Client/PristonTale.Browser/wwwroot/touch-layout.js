import { table } from './page-language.js';
import { element } from './touch-dom.js';
// The touch HUD's editor, the Android client's: Editar HUD in the menu dims the screen, outlines each part of the HUD
// with its name, and lets the player drag a part by its outline (the smallest one under the finger) and, for a round
// button, change its size and transparency with sliders on a panel dragged by its title, then Salvar, Padrão (every part
// back where it started) or Cancelar. Each part moves by a CSS translate in short sides (--touch-unit), kept on the
// screen; a button also scales and fades. The layout stays in this browser (localStorage), and the game hears where the
// player and target frames went (BrowserHost.SetTouchFrameShift) to draw the 3D heads in them.

const labels = {
    'pt-BR': { hint: 'Toque para selecionar. Arraste para mover.', size: 'Tamanho {0}%', transparency: 'Transparência {0}%', save: 'Salvar',
        reset: 'Padrão', cancel: 'Cancelar', player: 'Personagem', target: 'Alvo', chat: 'Chat', chatFeed: 'Histórico do chat', menu: 'Menu',
        bag: 'Bolsa', freeHunt: 'Auto-caça', quest: 'Missão', attack: 'Atacar', skill: 'Habilidade {0}', potion: 'Poção {0}',
        feed: 'Abates', experience: 'Experiência', party: 'Grupo' },
    en: { hint: 'Tap to select. Drag to move.', size: 'Size {0}%', transparency: 'Transparency {0}%', save: 'Save', reset: 'Default',
        cancel: 'Cancel', player: 'Character', target: 'Target', chat: 'Chat', chatFeed: 'Chat history', menu: 'Menu', bag: 'Bag',
        freeHunt: 'Auto-hunt', quest: 'Quest', attack: 'Attack', skill: 'Skill {0}', potion: 'Potion {0}', feed: 'Kills',
        experience: 'Experience', party: 'Party' }
};

const storageKey = 'etervale.touch-layout';
export const defaultSize = 100;
export const minimumSize = 65;
export const maximumSize = 160;
export const maximumTransparency = 80;
// The farthest a part may move, in short sides (TouchPortraitPlacement.MaxShift); the screen's edges stop it first.
const maximumShift = 2;

// The parts the editor moves, in the Android client's order: their key, the label key (with its number), the element
// and whether it is a round button that also scales and fades.
export const layoutParts = [
    { key: 'player', label: 'player', selector: '.touch-player' },
    { key: 'target', label: 'target', selector: '[data-role="touch-target"]' },
    { key: 'chat', label: 'chat', selector: '[data-touch-chat]', button: true },
    { key: 'chatFeed', label: 'chatFeed', selector: '[data-role="touch-chat-feed"]' },
    { key: 'menu', label: 'menu', selector: '[data-touch-menu-open]', button: true },
    { key: 'bag', label: 'bag', selector: '.touch-dock-orb[data-touch-action="inventory"]', button: true },
    { key: 'freeHunt', label: 'freeHunt', selector: '[data-touch-hunt]', button: true },
    { key: 'quest', label: 'quest', selector: '[data-role="touch-quest"]' },
    { key: 'attack', label: 'attack', selector: '[data-touch-action="touch-attack"]', button: true },
    ...[0, 1, 2, 3].map(slot => ({ key: 'skill-' + slot, label: 'skill', number: slot + 1, selector: '[data-touch-slot="' + slot + '"]', button: true })),
    ...[0, 1, 2].map(box => ({ key: 'potion-' + box, label: 'potion', number: box + 1, selector: '[data-touch-box="' + box + '"]', button: true })),
    { key: 'feed', label: 'feed', selector: '[data-role="touch-feed"]' },
    { key: 'experience', label: 'experience', selector: '[data-role="touch-experience"]' },
    { key: 'party', label: 'party', selector: '[data-role="touch-party"]' }
];

const clamp = (value, low, high) => Math.min(high, Math.max(low, value));
const finite = (value, fallback) => (Number.isFinite(value) ? value : fallback);

export const defaultPlacement = Object.freeze({ x: 0, y: 0, size: defaultSize, transparency: 0 });

// A part's placement kept within its limits: a frame or panel never scales or fades.
export function placement(saved, button) {
    const x = clamp(finite(saved?.x, 0), -maximumShift, maximumShift);
    const y = clamp(finite(saved?.y, 0), -maximumShift, maximumShift);
    if (!button) return { x, y, size: defaultSize, transparency: 0 };
    return { x, y, size: clamp(Math.round(finite(saved?.size, defaultSize)), minimumSize, maximumSize),
        transparency: clamp(Math.round(finite(saved?.transparency, 0)), 0, maximumTransparency) };
}

// The saved layout, read from its text: every known part's placement, the rest dropped.
export function readLayout(text) {
    let saved = {};
    try {
        saved = JSON.parse(text || '{}')?.parts ?? {};
    } catch {
        saved = {};
    }
    const layout = {};
    for (const part of layoutParts) layout[part.key] = placement(saved[part.key], part.button);
    return layout;
}

export function writeLayout(layout) {
    return JSON.stringify({ version: 1, parts: layout });
}

export function createTouchLayout(layer, getHost, getLanguage) {
    const editor = layer.querySelector('[data-role="touch-layout"]');
    const parts = layoutParts.map(part => ({ ...part, node: layer.querySelector(part.selector) })).filter(part => part.node);
    let saved = load();
    let draft = null;
    let selected = null;
    let held = null;
    let last = null;

    const say = key => table(labels, 'layout', getLanguage())[key] ?? labels.en[key];
    const name = part => say(part.label).replace('{0}', String(part.number ?? ''));

    function load() {
        try {
            return readLayout(globalThis.localStorage?.getItem(storageKey));
        } catch {
            return readLayout('');
        }
    }

    function store() {
        try {
            globalThis.localStorage?.setItem(storageKey, writeLayout(saved));
        } catch {
            // A private window or blocked storage keeps the layout for this visit only.
        }
    }

    function apply(part, place) {
        const style = part.node.style;
        style.translate = place.x || place.y ? 'calc(var(--touch-unit) * ' + place.x * 100 + ') calc(var(--touch-unit) * ' + place.y * 100 + ')' : '';
        style.scale = part.button && place.size !== defaultSize ? String(place.size / 100) : '';
        style.opacity = part.button && place.transparency > 0 ? String(1 - place.transparency / 100) : '';
    }

    function current() {
        return draft ?? saved;
    }

    // Every part where the layout puts it, and the frames' shifts for the game.
    function applyAll() {
        const layout = current();
        for (const part of parts) apply(part, layout[part.key]);
        getHost()?.SetTouchFrameShift?.(layout.player.x, layout.player.y, layout.target.x, layout.target.y);
    }

    // The short side in CSS pixels: --touch-unit is a hundredth of it.
    function side() {
        const bounds = layer.getBoundingClientRect();
        return Math.max(1, Math.min(bounds.width, bounds.height));
    }

    // Moves a part by dx, dy pixels, stopping at the screen's edges.
    function drag(part, dx, dy) {
        const bounds = layer.getBoundingClientRect();
        const box = part.node.getBoundingClientRect();
        const moveX = clamp(dx, bounds.left - box.left, bounds.right - box.right);
        const moveY = clamp(dy, bounds.top - box.top, bounds.bottom - box.bottom);
        const place = draft[part.key];
        draft[part.key] = placement({ ...place, x: place.x + moveX / side(), y: place.y + moveY / side() }, part.button);
        apply(part, draft[part.key]);
    }

    const outlines = element('div', 'touch-layout-outlines');
    const panel = element('div', 'touch-layout-panel');
    const title = element('p', 'touch-layout-title');
    const sliders = element('div', 'touch-layout-sliders');
    const actions = element('div', 'touch-layout-actions');

    function slider(key, minimum, maximum) {
        const row = element('label', 'touch-layout-slider');
        const text = element('span');
        const input = element('input');
        input.type = 'range';
        input.min = String(minimum);
        input.max = String(maximum);
        input.addEventListener('input', () => {
            if (!selected?.button) return;
            draft[selected.key] = placement({ ...draft[selected.key], [key]: Number(input.value) }, true);
            apply(selected, draft[selected.key]);
            showSelection();
            drawOutlines();
        });
        row.append(text, input);
        sliders.append(row);
        return { text, input };
    }
    const sizeSlider = slider('size', minimumSize, maximumSize);
    const transparencySlider = slider('transparency', 0, maximumTransparency);

    function action(key, run) {
        const button = element('button', 'touch-bag-action touch-layout-action');
        button.type = 'button';
        button.dataset.label = key;
        button.addEventListener('click', run);
        actions.append(button);
        return button;
    }
    action('save', () => {
        saved = draft;
        store();
        close();
    });
    action('reset', () => {
        for (const part of parts) draft[part.key] = { ...defaultPlacement };
        applyAll();
        showSelection();
        drawOutlines();
    });
    action('cancel', () => {
        draft = null;
        close();
    });
    panel.append(title, sliders, actions);

    function showSelection() {
        title.textContent = selected ? name(selected) : say('hint');
        sliders.hidden = !selected?.button;
        if (selected?.button) {
            const place = draft[selected.key];
            sizeSlider.input.value = String(place.size);
            transparencySlider.input.value = String(place.transparency);
            sizeSlider.text.textContent = say('size').replace('{0}', String(place.size));
            transparencySlider.text.textContent = say('transparency').replace('{0}', String(place.transparency));
        }
        for (const button of actions.children) button.textContent = say(button.dataset.label);
    }

    // Each shown part's outline and name over it, the selected one shaded.
    function drawOutlines() {
        const bounds = layer.getBoundingClientRect();
        outlines.replaceChildren(...parts.flatMap(part => {
            const box = part.node.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) return [];
            const outline = element('div', 'touch-layout-outline', name(part));
            if (part === selected) outline.dataset.on = 'true';
            outline.style.left = box.left - bounds.left + 'px';
            outline.style.top = box.top - bounds.top + 'px';
            outline.style.width = box.width + 'px';
            outline.style.height = box.height + 'px';
            return [outline];
        }));
    }

    // The smallest shown part under the point, as the Android editor picks.
    function partAt(x, y) {
        let found = null;
        let smallest = Infinity;
        for (const part of parts) {
            const box = part.node.getBoundingClientRect();
            const area = box.width * box.height;
            if (area > 0 && x >= box.left && x <= box.right && y >= box.top && y <= box.bottom && area < smallest) {
                found = part;
                smallest = area;
            }
        }
        return found;
    }

    function dragPanel(dx, dy) {
        const bounds = layer.getBoundingClientRect();
        const box = panel.getBoundingClientRect();
        const left = clamp(box.left + dx, bounds.left, bounds.right - box.width) - bounds.left;
        const top = clamp(box.top + dy, bounds.top, bounds.bottom - box.height) - bounds.top;
        panel.style.left = left + 'px';
        panel.style.top = top + 'px';
        panel.style.transform = 'none';
    }

    if (editor) {
        editor.append(outlines, panel);
        editor.addEventListener('pointerdown', event => {
            event.stopPropagation();
            if (panel.contains(event.target) && event.target !== title) return;
            held = event.target === title ? 'panel' : partAt(event.clientX, event.clientY);
            last = { x: event.clientX, y: event.clientY };
            if (held !== 'panel') {
                selected = held;
                showSelection();
                drawOutlines();
            }
            editor.setPointerCapture?.(event.pointerId);
        });
        editor.addEventListener('pointermove', event => {
            if (!held || !last) return;
            const dx = event.clientX - last.x;
            const dy = event.clientY - last.y;
            last = { x: event.clientX, y: event.clientY };
            if (held === 'panel') dragPanel(dx, dy);
            else {
                drag(held, dx, dy);
                drawOutlines();
            }
        });
        for (const type of ['pointerup', 'pointercancel']) {
            editor.addEventListener(type, () => {
                if (held && held !== 'panel') getHost()?.SetTouchFrameShift?.(draft.player.x, draft.player.y, draft.target.x, draft.target.y);
                held = null;
                last = null;
            });
        }
        editor.addEventListener('keydown', event => {
            event.stopPropagation();
            if (event.key === 'Escape') {
                draft = null;
                close();
            }
        });
    }

    function open() {
        if (!editor || !editor.hidden) return;
        draft = structuredClone(saved);
        selected = null;
        panel.style.left = '';
        panel.style.top = '';
        panel.style.transform = '';
        editor.hidden = false;
        showSelection();
        drawOutlines();
    }

    function close() {
        if (!editor || editor.hidden) return;
        editor.hidden = true;
        draft = null;
        selected = null;
        applyAll();
    }

    applyAll();

    return {
        open,
        close() {
            if (editor && !editor.hidden) {
                draft = null;
                close();
            }
        },
        // The HUD showed again (a world entered): the game hears the frames' shifts again.
        refresh: applyAll,
        translate() {
            if (editor && !editor.hidden) {
                showSelection();
                drawOutlines();
            }
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const layoutTables = { layout: labels };
