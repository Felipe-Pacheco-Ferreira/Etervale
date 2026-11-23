import { table } from './page-language.js';
import * as art from './hud-art.js';
import { chip, element, format, pictureAddress, section } from './touch-dom.js';
// The touch HUD's skills window, the Android client's: a pill tab per class tier (the class's title at it), the tier's
// skills as cards (the skill button, gray until learned, the name, the rank, the touch button holding it and a "+" when
// the next rank can be trained at the skill master) and the chosen skill's sheet: its description, cost and numbers,
// what the next rank needs and the four touch buttons to put it on. The game hands out the skills
// (BrowserHost.TouchSkills), trains a rank (touch-train:<index>) and puts a skill on a button
// (touch-skill-button:<slot>:<index>); opening the skills window in the game, or a skill master, opens it here.

const labels = {
    'pt-BR': { skills: 'Habilidades', close: 'Fechar', points: '{0} pontos', 'points-one': '1 ponto', elite: '{0} pontos de elite',
        'elite-one': '1 ponto de elite', rank: 'Rank {0}/{1}', unlearned: 'Não aprendida', active: 'Ativa', passive: 'Passiva',
        unavailable: 'Indisponível', button: 'Botão {0}', train: 'Treinar {0}', buttons: 'Botões de ação', 'put-on': 'Pôr no botão {0}',
        'passive-note': 'Passiva: vale sempre, sem botão.', Ready: 'Rank {0}: {1} de ouro e 1 ponto.', ReadyElite: 'Rank {0}: {1} de ouro e 1 ponto de elite.',
        Poor: 'Rank {0}: faltam {1} de ouro.', Unavailable: 'Indisponível.', TierLocked: 'Abre ao virar {0}.', MaxRank: 'Rank máximo.',
        NeedsPrevious: 'Aprenda antes {0}.', NeedsLevel: 'Rank {0} no nível {1}.', NoPoints: 'Sem pontos de habilidade: suba de nível.',
        NoElitePoints: 'Sem pontos de elite.',
        'note-master': 'Toque em + para treinar. Toque numa habilidade para ver os detalhes e pô-la num botão de ação.',
        'note-away': 'Para aprender ou treinar, fale com o Mestre de Habilidades da sua cidade e toque em + perto dele.' },
    en: { skills: 'Skills', close: 'Close', points: '{0} points', 'points-one': '1 point', elite: '{0} elite points',
        'elite-one': '1 elite point', rank: 'Rank {0}/{1}', unlearned: 'Not learned', active: 'Active', passive: 'Passive',
        unavailable: 'Unavailable', button: 'Button {0}', train: 'Train {0}', buttons: 'Action buttons', 'put-on': 'Put on button {0}',
        'passive-note': 'Passive: always on, no button.', Ready: 'Rank {0}: {1} gold and 1 point.', ReadyElite: 'Rank {0}: {1} gold and 1 elite point.',
        Poor: 'Rank {0}: {1} gold short.', Unavailable: 'Unavailable.', TierLocked: 'Opens as {0}.', MaxRank: 'Highest rank.',
        NeedsPrevious: 'Learn {0} first.', NeedsLevel: 'Rank {0} at level {1}.', NoPoints: 'No skill points: level up.',
        NoElitePoints: 'No elite points.',
        'note-master': 'Tap + to train. Tap a skill to see its details and put it on an action button.',
        'note-away': 'To learn or train, talk to your town\'s Skill Master and tap + near them.' }
};

// What the next rank of the chosen skill needs, or why it cannot be trained yet.
export function nextRank(sheet, say, language) {
    const gold = value => Number(value).toLocaleString(language);
    switch (sheet.check) {
        case 'Ready':
            return sheet.missing > 0 ? format(say('Poor'), sheet.rank + 1, gold(sheet.missing))
                : format(say(sheet.elite ? 'ReadyElite' : 'Ready'), sheet.rank + 1, gold(sheet.price));
        case 'TierLocked':
            return format(say('TierLocked'), sheet.tierTitle);
        case 'NeedsPrevious':
            return format(say('NeedsPrevious'), sheet.previous);
        case 'NeedsLevel':
            return format(say('NeedsLevel'), sheet.rank + 1, sheet.levelFor);
        case 'NoPoints':
            return say(sheet.elite ? 'NoElitePoints' : 'NoPoints');
        case 'MaxRank':
            return say('MaxRank');
        default:
            return say('Unavailable');
    }
}

export function createTouchSkills(layer, getHost, getLanguage, picture) {
    const dialog = layer.querySelector('[data-role="touch-skills"]');
    if (!dialog) return { frame() {}, close() {}, translate() {} };
    const title = dialog.querySelector('[data-role="touch-skills-title"]');
    const tabs = dialog.querySelector('[data-role="touch-skills-tabs"]');
    const cards = dialog.querySelector('[data-role="touch-skills-cards"]');
    const sheetNode = dialog.querySelector('[data-role="touch-skills-sheet"]');
    const note = dialog.querySelector('[data-role="touch-skills-note"]');
    let requests = -1;
    let tier = -1;
    let chosen = -1;
    let skills = null;
    let shownKey = '';
    let waiting = false;

    const say = key => table(labels, 'skills', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);
    const rankText = (rank, max) => rank > 0 ? format(say('rank'), rank, max) : say('unlearned');

    function well(card) {
        const node = element('span', 'touch-skills-well');
        if (card.rank > 0) node.dataset.learned = 'true';
        const canvas = card.picture ? picture(card.picture) : null;
        waiting = waiting || Boolean(card.picture && !canvas);
        if (canvas) {
            const image = element('img', 'touch-bag-picture');
            image.src = pictureAddress(canvas);
            image.alt = '';
            node.append(image);
        }
        const ring = element('canvas', 'touch-skills-ring');
        const size = 46;
        const ratio = globalThis.devicePixelRatio || 1;
        ring.width = ring.height = Math.round(size * ratio);
        const context = ring.getContext('2d');
        if (context && !card.unavailable) {
            context.scale(ratio, ratio);
            art.skillBorder(context, size / 2, size / 2, size / 2 - 3, card.ring, card.rank, skills.maxRank, performance.now() / 1000);
        }
        node.append(ring);
        return node;
    }

    function renderTabs() {
        tabs.replaceChildren(...skills.tiers.map((entry, index) => {
            const tab = element('button', 'touch-journal-tab', entry.title);
            tab.type = 'button';
            tab.setAttribute('aria-pressed', String(index === skills.tier));
            if (!entry.reached) tab.dataset.dim = 'true';
            tab.addEventListener('click', () => {
                tier = index;
                chosen = -1;
                poll(true);
            });
            return tab;
        }));
        tabs.append(element('span', 'touch-journal-spacer'),
            chip(skills.points === 1 ? say('points-one') : format(say('points'), skills.points), skills.points > 0 ? 'open' : 'muted'));
        if (skills.eliteShown) tabs.append(chip(skills.elite === 1 ? say('elite-one') : format(say('elite'), skills.elite), 'done'));
    }

    function renderCards() {
        cards.replaceChildren(...skills.cards.map(card => {
            const node = element('div', 'touch-skills-card');
            node.setAttribute('role', 'button');
            node.tabIndex = 0;
            node.setAttribute('aria-pressed', String(card.index === chosen));
            const text = element('div', 'touch-skills-text');
            const name = element('strong', 'touch-skills-name', card.name);
            if (card.rank > 0) name.dataset.learned = 'true';
            text.append(name, element('span', 'touch-skills-line',
                card.unavailable ? say('unavailable') : rankText(card.rank, skills.maxRank) + '  ·  ' + say(card.passive ? 'passive' : 'active')));
            node.append(well(card), text);
            if (card.button >= 0) node.append(chip(format(say('button'), card.button + 1), 'open'));
            if (card.trainable) {
                const train = element('button', 'touch-status-point', '+');
                train.type = 'button';
                train.disabled = skills.busy;
                train.setAttribute('aria-label', format(say('train'), card.name));
                train.addEventListener('click', event => {
                    event.stopPropagation();
                    run('touch-train:' + card.index);
                    poll(true);
                });
                node.append(train);
            }
            node.addEventListener('click', () => {
                chosen = card.index;
                poll(true);
            });
            return node;
        }));
    }

    function renderSheet() {
        const sheet = skills.sheet;
        if (!sheet) {
            sheetNode.replaceChildren();
            return;
        }
        const heading = section(sheet.name);
        heading.append(chip(rankText(sheet.rank, skills.maxRank), sheet.rank > 0 ? 'open' : 'muted'));
        const nodes = [heading];
        if (sheet.unavailable) {
            nodes.push(element('p', 'touch-skills-muted', say('unavailable')));
            sheetNode.replaceChildren(...nodes);
            return;
        }
        nodes.push(element('p', 'touch-skills-description', sheet.description),
            element('p', 'touch-skills-muted', say(sheet.passive ? 'passive' : 'active') + '  ·  ' + sheet.cost));
        for (const detail of sheet.details) nodes.push(element('p', 'touch-skills-detail', detail));
        const next = element('p', 'touch-skills-next', nextRank(sheet, say, getLanguage()));
        if (sheet.trainable) next.dataset.ready = 'true';
        nodes.push(next);
        if (sheet.buttons) {
            nodes.push(section(say('buttons')));
            const row = element('div', 'touch-status-actions');
            const holder = skills.cards.find(card => card.index === sheet.index)?.button ?? -1;
            for (let slot = 0; slot < 4; slot++) {
                const button = element('button', 'touch-bag-action', String(slot + 1));
                button.type = 'button';
                button.setAttribute('aria-label', format(say('put-on'), slot + 1));
                if (slot === holder) button.dataset.on = 'true';
                button.addEventListener('click', () => {
                    run('touch-skill-button:' + slot + ':' + sheet.index);
                    poll(true);
                });
                row.append(button);
            }
            nodes.push(row);
        } else if (sheet.rank > 0 && sheet.passive) {
            nodes.push(element('p', 'touch-skills-muted', say('passive-note')));
        }
        sheetNode.replaceChildren(...nodes);
    }

    function poll(force = false) {
        let state;
        try {
            const text = getHost()?.TouchSkills?.(dialog.open, tier, chosen) || '';
            state = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!state) return;
        if (state.requests !== requests) {
            const asked = requests >= 0 && state.requests > requests;
            requests = state.requests;
            if (asked) {
                if (!dialog.open) open();
                return;
            }
        }
        if (!dialog.open || !state.cards) return;
        skills = state;
        tier = state.tier;
        if (!state.sheet && state.cards.length > 0) {
            chosen = state.cards[0].index;
            poll(true);
            return;
        }
        const key = JSON.stringify([getLanguage(), state]);
        if (!force && !waiting && key === shownKey) return;
        shownKey = key;
        waiting = false;
        const listTop = cards.scrollTop;
        renderTabs();
        renderCards();
        renderSheet();
        cards.scrollTop = listTop;
        note.textContent = say(state.master ? 'note-master' : 'note-away');
    }

    function open() {
        tier = -1;
        chosen = -1;
        shownKey = '';
        dialog.showModal();
        poll(true);
    }

    dialog.querySelector('[data-touch-skills-close]')?.addEventListener('click', () => dialog.close());
    dialog.addEventListener('keydown', event => event.stopPropagation());

    return {
        frame: () => poll(),
        close() {
            if (dialog.open) dialog.close();
        },
        translate() {
            title.textContent = say('skills');
            dialog.querySelector('[data-touch-skills-close]')?.setAttribute('aria-label', say('close'));
            shownKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const skillsTables = { skills: labels };
