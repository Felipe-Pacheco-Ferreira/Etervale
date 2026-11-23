import { table } from './page-language.js';
import { chip, element, format, pictureAddress, section } from './touch-dom.js';
// The list a long press on a touch skill button opens, or a tap on an empty one, as the Android client's: a HUD plate
// in the middle of the screen titled with the button, the learned skills a button may take (each with its picture, name
// and the button holding it), and Fechar. A tap puts the skill on the button (touch-skill-button:<slot>:<index>, as the
// skills window does), taking it off the button that held it; the game hands out the list (BrowserHost.TouchSkillChoices).

const labels = {
    'pt-BR': { title: 'Habilidade do botão {0}', none: 'Nenhuma habilidade aprendida.', close: 'Fechar', button: 'Botão {0}' },
    en: { title: 'Skill for button {0}', none: 'No skill learned.', close: 'Close', button: 'Button {0}' }
};

// The list's rows: label, the action putting the skill on the slot and the button holding it now (-1 for none).
export function pickerRows(choices, slot) {
    return choices.map(choice => ({ label: choice.name, picture: choice.picture, holder: choice.button, current: choice.button === slot,
        action: 'touch-skill-button:' + slot + ':' + choice.index }));
}

export function createSkillPicker(layer, getHost, getLanguage, getPicture) {
    const plate = layer.querySelector('[data-role="touch-skill-picker"]');
    if (!plate) return { open() {}, close() {}, translate() {} };
    let slot = -1;

    const say = key => table(labels, 'picker', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);

    function close() {
        plate.hidden = true;
        slot = -1;
    }

    function render() {
        let choices;
        try {
            choices = JSON.parse(getHost()?.TouchSkillChoices?.() || '[]');
        } catch {
            choices = [];
        }
        const closer = element('button', 'touch-chat-close', '×');
        closer.type = 'button';
        closer.setAttribute('aria-label', say('close'));
        closer.addEventListener('click', close);
        const heading = element('div', 'touch-party-heading');
        heading.append(section(format(say('title'), slot + 1)), closer);
        const list = element('div', 'touch-skill-picker-list');
        const rows = pickerRows(Array.isArray(choices) ? choices : [], slot);
        if (rows.length === 0) list.append(element('p', 'touch-rewards-note', say('none')));
        for (const row of rows) {
            const button = element('button', 'touch-skill-picker-row');
            button.type = 'button';
            if (row.current) button.dataset.on = 'true';
            const canvas = row.picture ? getPicture(row.picture) : null;
            const icon = element('span', 'touch-skill-picker-icon');
            if (canvas) {
                const image = element('img');
                image.alt = '';
                image.src = pictureAddress(canvas);
                icon.append(image);
            }
            button.append(icon, element('span', 'touch-skill-picker-name', row.label));
            if (row.holder >= 0) button.append(chip(format(say('button'), row.holder + 1), row.current ? 'reward' : 'muted'));
            button.addEventListener('click', () => {
                run(row.action);
                close();
            });
            list.append(button);
        }
        plate.replaceChildren(heading, list);
    }

    plate.addEventListener('keydown', event => {
        event.stopPropagation();
        if (event.key === 'Escape') close();
    });

    return {
        open(chosen) {
            slot = chosen;
            render();
            plate.hidden = false;
        },
        close,
        translate() {
            if (!plate.hidden) render();
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const pickerTables = { picker: labels };
