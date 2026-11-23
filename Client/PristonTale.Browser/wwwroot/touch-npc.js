import { table } from './page-language.js';
import { element, format, section } from './touch-dom.js';
// The touch HUD's NPC plate, the Android client's: one HUD plate at the bottom, between the Menu and Bolsa buttons and
// the action wheel (centred over the HUD when that gap is too narrow), with the NPC's name as a section heading, the
// line in a recessed card that scrolls when long and the choices as buttons, the first one highlighted. It shows an
// NPC's dialog with its services or destinations, a quest's dialog with its choices or browsed rewards, and the yes/no
// boxes after a quest dialog or offering a skill reset. The game hands out what shows (BrowserHost.TouchNpc) and takes
// the answers (touch-npc:<action>[:<value>]).

const labels = {
    'pt-BR': { close: 'Fechar', ok: 'OK', Proceed: 'Continuar', Confirm: 'Aceitar', ConfirmChoice: 'Escolher', yes: 'Sim', no: 'Não',
        loading: 'Carregando…', previous: 'Recompensa anterior', next: 'Próxima recompensa', reward: 'Recompensa {0} de {1}' },
    en: { close: 'Close', ok: 'OK', Proceed: 'Continue', Confirm: 'Accept', ConfirmChoice: 'Choose', yes: 'Yes', no: 'No',
        loading: 'Loading…', previous: 'Previous reward', next: 'Next reward', reward: 'Reward {0} of {1}' }
};

// In CSS pixels, the Android plate's: its widest and narrowest width and its gap to the HUD beside it.
const maxWidth = 420;
const minWidth = 300;
const clearance = 12;
const answering = ['Proceed', 'Confirm', 'ConfirmChoice'];

// The plate's buttons, in order: label, the action it sends, whether it is the highlighted first one and whether it is
// off (an unavailable service, or a quest answer still in flight).
export function plateButtons(state, say) {
    const buttons = [];
    const add = (label, action, disabled = false) => buttons.push({ label, action, prominent: buttons.length === 0, disabled });
    switch (state.kind) {
        case 'npc':
            state.buttons.forEach((button, index) =>
                add(button.label, (state.destinations ? 'touch-npc:destination:' : 'touch-npc:service:') + index, !button.available));
            add(say('close'), 'touch-npc:close');
            break;
        case 'quest':
            state.choices.forEach((choice, index) => add(choice, 'touch-npc:choose:' + index, state.pending));
            // A dialog whose OK answers the quest also offers closing it unanswered; one whose OK only closes it does not.
            if (answering.includes(state.followup)) {
                add(say(state.followup), 'touch-npc:ok', state.pending);
                add(say('close'), 'touch-npc:close');
            } else {
                add(say('ok'), 'touch-npc:ok');
            }
            break;
        case 'confirm':
            add(say('yes'), 'touch-npc:yes', state.pending);
            add(say('no'), 'touch-npc:no');
            break;
    }
    return buttons;
}

// Where the plate goes across a layer width wide, between leftClear and rightClear pixels kept by the HUD.
export function placePlate(width, leftClear, rightClear) {
    const free = width - leftClear - rightClear - clearance * 2;
    const plate = Math.min(maxWidth, free);
    if (plate >= minWidth) return { left: leftClear + clearance + (free - plate) / 2, width: plate };
    const wide = Math.min(maxWidth, width - clearance * 2);
    return { left: (width - wide) / 2, width: wide };
}

export function createTouchNpc(layer, getHost, getLanguage) {
    const plate = layer.querySelector('[data-role="touch-npc"]');
    if (!plate) return { frame() {}, translate() {} };
    const heading = plate.querySelector('[data-role="touch-npc-heading"]');
    const line = plate.querySelector('[data-role="touch-npc-line"]');
    const browse = plate.querySelector('[data-role="touch-npc-browse"]');
    const actions = plate.querySelector('[data-role="touch-npc-actions"]');
    const dock = layer.querySelector('.touch-dock');
    const wheel = [...layer.querySelectorAll('.touch-potion, .touch-skill')];
    let shownKey = '';
    let shownKind = '';
    let lineKey = '';

    const say = key => table(labels, 'npc', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);

    function place() {
        const bounds = layer.getBoundingClientRect();
        if (bounds.width === 0) return;
        const leftClear = dock ? dock.getBoundingClientRect().right - bounds.left : 0;
        const wheelLeft = wheel.reduce((left, node) => Math.min(left, node.getBoundingClientRect().left), bounds.right);
        const spot = placePlate(bounds.width, Math.max(0, leftClear), Math.max(0, bounds.right - wheelLeft));
        plate.style.left = spot.left + 'px';
        plate.style.width = spot.width + 'px';
    }

    function renderHeading(state) {
        const name = state.name || state.title || '';
        const nodes = [section(name)];
        if (state.name && state.title && state.title !== state.name) nodes.push(element('p', 'touch-npc-title', state.title));
        if (state.kind === 'confirm' && state.detail) nodes.push(element('p', 'touch-npc-title', state.detail));
        heading.replaceChildren(...nodes);
    }

    // Kept, and scrolled where the player left it, while the text stays the same.
    function renderLine(state) {
        const lines = state.kind === 'confirm' ? [state.question] : state.lines;
        const key = JSON.stringify(lines);
        if (key === lineKey) return;
        lineKey = key;
        if (!lines) {
            line.replaceChildren(element('p', 'touch-npc-muted', say('loading')));
        } else {
            line.replaceChildren(...lines.map(text => text.trim() ? element('p', '', text) : element('p', 'touch-npc-gap')));
        }
        line.scrollTop = 0;
    }

    function renderBrowse(state) {
        browse.hidden = !(state.browseCount > 1);
        if (browse.hidden) return;
        const arrow = (text, step, label) => {
            const button = element('button', 'touch-chat-close', text);
            button.type = 'button';
            button.setAttribute('aria-label', say(label));
            button.addEventListener('click', () => {
                run('touch-npc:browse:' + step);
                poll(true);
            });
            return button;
        };
        const reward = element('span', 'touch-npc-reward', state.reward);
        reward.title = format(say('reward'), state.browse, state.browseCount);
        browse.replaceChildren(arrow('‹', -1, 'previous'), reward, element('span', 'touch-npc-count', state.browse + '/' + state.browseCount),
            arrow('›', 1, 'next'));
    }

    function renderActions(state) {
        actions.replaceChildren(...plateButtons(state, say).map(entry => {
            const button = element('button', 'touch-bag-action touch-npc-button', entry.label);
            button.type = 'button';
            button.disabled = entry.disabled;
            if (entry.prominent) button.dataset.on = 'true';
            button.addEventListener('click', () => {
                run(entry.action);
                poll(true);
            });
            return button;
        }));
    }

    function poll(force = false) {
        let state;
        try {
            const text = getHost()?.TouchNpc?.() || '';
            state = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!state) return;
        const key = JSON.stringify([getLanguage(), state]);
        if (!force && key === shownKey) return;
        shownKey = key;
        const opened = plate.hidden || state.kind !== shownKind;
        shownKind = state.kind;
        plate.hidden = !state.kind;
        if (plate.hidden) {
            lineKey = '';
            return;
        }
        if (opened) place();
        renderHeading(state);
        renderLine(state);
        renderBrowse(state);
        renderActions(state);
    }

    plate.addEventListener('keydown', event => event.stopPropagation());
    globalThis.addEventListener?.('resize', () => {
        if (!plate.hidden) place();
    });

    return {
        frame: () => poll(),
        close() {
            plate.hidden = true;
            shownKey = '';
            lineKey = '';
        },
        translate() {
            shownKey = '';
            lineKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const npcTables = { npc: labels };
