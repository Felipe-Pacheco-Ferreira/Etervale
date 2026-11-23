import { table } from './page-language.js';
import { element, format, section } from './touch-dom.js';
// The touch HUD's death plate, the Android client's: a HUD plate in the middle of the screen with what the death costs,
// standing up in the field (its extra cost, or why it is closed) and in town (highlighted), and the seconds before the
// town is chosen by itself. The game opens and closes it (BrowserHost.TouchRestart) and takes the choice
// (touch-restart:Field or touch-restart:Town); a tap outside does not close it.

const labels = {
    'pt-BR': { title: 'Você morreu', scroll: 'O Pergaminho de Ressurreição será usado: sem penalidade.',
        penalty: 'Penalidade da morte: -{0} XP e -{1} de ouro.', none: 'Sem penalidade de morte no seu nível.',
        field: 'Recomeçar no mapa', 'field-hint': 'No mesmo lugar: penalidade extra de 1% (ouro e XP)',
        'field-scroll': 'No mesmo lugar, sem penalidade', 'field-closed': 'XP insuficiente para ficar no mapa',
        town: 'Recomeçar na cidade', 'town-hint': 'Na cidade, em segurança', countdown: 'Volta à cidade sozinho em {0} s' },
    en: { title: 'You died', scroll: 'The Resurrection Scroll will be used: no penalty.',
        penalty: 'Death penalty: -{0} EXP and -{1} gold.', none: 'No death penalty at your level.',
        field: 'Restart on the map', 'field-hint': 'Right here: an extra 1% penalty (gold and EXP)',
        'field-scroll': 'Right here, no penalty', 'field-closed': 'Not enough EXP to stay on the map',
        town: 'Restart in town', 'town-hint': 'In town, safe', countdown: 'Back to town by itself in {0} s' }
};

// What the plate says: the cost line (empty while the offer is unknown), the two choices and the countdown. Experience
// comes as text, being 64-bit.
export function restartView(state, say, language) {
    const number = new Intl.NumberFormat(language);
    let cost = '';
    if (state.scroll) cost = say('scroll');
    else if (state.known && (BigInt(state.experience || '0') > 0n || BigInt(state.gold || '0') > 0n)) {
        cost = format(say('penalty'), number.format(BigInt(state.experience || '0')), number.format(BigInt(state.gold || '0')));
    } else if (state.known) cost = say('none');
    const fieldHint = state.scroll ? 'field-scroll' : state.fieldOpen ? 'field-hint' : 'field-closed';
    return {
        cost,
        choices: [
            { title: say('field'), hint: say(fieldHint), action: 'touch-restart:Field', disabled: !state.fieldOpen, prominent: false },
            { title: say('town'), hint: say('town-hint'), action: 'touch-restart:Town', disabled: false, prominent: true }
        ],
        countdown: format(say('countdown'), state.secondsLeft)
    };
}

export function createTouchRestart(layer, getHost, getLanguage) {
    const plate = layer.querySelector('[data-role="touch-restart"]');
    if (!plate) return { poll() {}, close() {}, translate() {} };
    let shownKey = '';

    const say = key => table(labels, 'restart', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);

    function render(state) {
        const view = restartView(state, say, getLanguage());
        const nodes = [section(say('title'))];
        if (view.cost) nodes.push(element('p', 'touch-restart-cost', view.cost));
        for (const choice of view.choices) {
            const button = element('button', 'touch-bag-action touch-restart-choice');
            button.type = 'button';
            button.disabled = choice.disabled;
            if (choice.prominent) button.dataset.on = 'true';
            button.append(element('span', 'touch-restart-title', choice.title), element('span', 'touch-restart-hint', choice.hint));
            button.addEventListener('click', () => {
                run(choice.action);
                poll(true);
            });
            nodes.push(button);
        }
        nodes.push(element('p', 'touch-restart-countdown', view.countdown));
        plate.replaceChildren(...nodes);
    }

    function poll(force = false) {
        let state;
        try {
            const text = getHost()?.TouchRestart?.() || '';
            state = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!state) return;
        const key = JSON.stringify([getLanguage(), state]);
        if (!force && key === shownKey) return;
        shownKey = key;
        plate.hidden = !state.open;
        if (state.open) render(state);
    }

    return {
        poll: () => poll(),
        close() {
            plate.hidden = true;
            shownKey = '';
        },
        translate() {
            shownKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const restartTables = { restart: labels };
