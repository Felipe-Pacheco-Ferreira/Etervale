import { table } from './page-language.js';
import * as art from './hud-art.js';
import { chip, element, format, gauge, section } from './touch-dom.js';
// The touch HUD's status window, the Android client's: on the left the level medallion, the class, experience and the
// vitals; in the middle the five attributes, each with its emblem, what it raises, a bar against the strongest one and
// "+" and "−" planning free points (the class's damage attribute marked); on the right the combat values as tiles with
// what the planned points add, and Desfazer and Confirmar. The game hands out the character (BrowserHost.TouchStatus)
// and sends the confirmed points (touch-points:<five amounts>); opening the status window in the game opens it here.

const labels = {
    'pt-BR': { close: 'Fechar', level: 'Nível {0}', experience: 'Experiência {0}', 'max-level': 'Nível máximo', vitals: 'Vitais',
        life: 'Vida {0} / {1}', mana: 'Mana {0} / {1}', stamina: 'Estamina {0} / {1}', weight: 'Carga {0} / {1}',
        attributes: 'Atributos', free: '{0} pontos livres', 'free-one': '1 ponto livre', damage: 'Dano', combat: 'Combate',
        undo: 'Desfazer', confirm: 'Confirmar', 'confirm-count': 'Confirmar ({0})', plus: 'Somar um ponto em {0}',
        minus: 'Tirar um ponto de {0}', strength: 'Força', spirit: 'Espírito', talent: 'Talento', agility: 'Agilidade',
        health: 'Vitalidade', 'strength-effect': 'Dano corpo a corpo, absorção, carga', 'spirit-effect': 'Mana, dano mágico',
        'talent-effect': 'Precisão, defesa, absorção', 'agility-effect': 'Precisão, defesa, dano à distância',
        'health-effect': 'Vida, estamina, carga', attack: 'Ataque', 'attack-rating': 'Precisão', defence: 'Defesa',
        absorption: 'Absorção', critical: 'Crítico', block: 'Bloqueio', movement: 'Movimento', 'skill-points': 'Pts. de habilidade' },
    en: { close: 'Close', level: 'Level {0}', experience: 'Experience {0}', 'max-level': 'Highest level', vitals: 'Vitals',
        life: 'Life {0} / {1}', mana: 'Mana {0} / {1}', stamina: 'Stamina {0} / {1}', weight: 'Load {0} / {1}',
        attributes: 'Attributes', free: '{0} free points', 'free-one': '1 free point', damage: 'Damage', combat: 'Combat',
        undo: 'Undo', confirm: 'Confirm', 'confirm-count': 'Confirm ({0})', plus: 'Add a point to {0}',
        minus: 'Take a point from {0}', strength: 'Strength', spirit: 'Spirit', talent: 'Talent', agility: 'Agility',
        health: 'Vitality', 'strength-effect': 'Melee damage, absorption, load', 'spirit-effect': 'Mana, magic damage',
        'talent-effect': 'Attack rating, defence, absorption', 'agility-effect': 'Attack rating, defence, ranged damage',
        'health-effect': 'Life, stamina, load', attack: 'Attack', 'attack-rating': 'Attack rating', defence: 'Defence',
        absorption: 'Absorption', critical: 'Critical', block: 'Block', movement: 'Movement', 'skill-points': 'Skill points' }
};

// The attributes in the game's order: their key, their gauge and their emblem on a 24-unit grid (a sword, a flame, a
// star, a bolt, a heart).
export const attributes = [
    { key: 'strength', gauge: { top: [240, 112, 78], bottom: [136, 32, 18] },
        sign: 'M12 2.5L14.2 5.5V14.5H17.5V16.8H13.3V21.5H10.7V16.8H6.5V14.5H9.8V5.5Z' },
    { key: 'spirit', gauge: { top: [120, 172, 255], bottom: [30, 64, 160] },
        sign: 'M12 2.5C17 8 19 12 17 16.5C15.5 20 8.5 20.5 7 16.5C6 13.5 8 11 9.5 9.5C9.5 12 10.5 13.5 12 14C13 10.5 11 7 12 2.5Z' },
    { key: 'talent', gauge: { top: [204, 150, 255], bottom: [84, 36, 150] },
        sign: 'M12 3L14.35 9.26L21.03 9.56L15.8 13.73L17.58 20.19L12 16.5L6.42 20.19L8.2 13.73L2.97 9.56L9.65 9.26Z' },
    { key: 'agility', gauge: { top: [146, 222, 110], bottom: [36, 112, 40] }, sign: 'M14 2.5L5.5 13.5H11.5L9.5 21.5L18.5 10H12.5Z' },
    { key: 'health', gauge: { top: [255, 196, 92], bottom: [160, 92, 16] },
        sign: 'M12 20.5C4 15 2.5 10 5.5 6.5C8 4 11 5.5 12 8C13 5.5 16 4 18.5 6.5C21.5 10 20 15 12 20.5Z' }
];

// The combat tiles: their label, the value from the game's combat values and what the planned points add (null: none).
export const combatTiles = [
    ['attack', combat => combat.damageMin + ' – ' + combat.damageMax,
        gains => gains.damageMin || gains.damageMax ? '+' + gains.damageMin + ' – +' + gains.damageMax : null],
    ['attack-rating', combat => String(combat.attackRating), gains => plus(gains.attackRating)],
    ['defence', combat => String(combat.defence), gains => plus(gains.defence)],
    ['absorption', combat => String(combat.absorption), gains => plus(gains.absorption)],
    ['critical', combat => combat.critical + '%', () => null],
    ['block', combat => combat.block + '%', () => null],
    ['movement', combat => String(combat.moveSpeed), gains => plus(gains.moveSpeed)],
    ['skill-points', combat => String(combat.skillPoints), () => null]
];

function plus(value) {
    return value ? '+' + value : null;
}

// A point planned on (step 1) or taken back from (step −1) an attribute, within the free points; the plan unchanged
// otherwise.
export function plan(planned, index, step, free) {
    const total = planned.reduce((sum, value) => sum + value, 0);
    if (planned[index] + step < 0 || (step > 0 && total >= free)) return planned;
    return planned.map((value, at) => at === index ? value + step : value);
}

export function createTouchStatus(layer, getHost, getLanguage) {
    const dialog = layer.querySelector('[data-role="touch-status"]');
    if (!dialog) return { frame() {}, close() {}, translate() {} };
    const title = dialog.querySelector('[data-role="touch-status-title"]');
    const hero = dialog.querySelector('[data-role="touch-status-hero"]');
    const rows = dialog.querySelector('[data-role="touch-status-attributes"]');
    const combat = dialog.querySelector('[data-role="touch-status-combat"]');
    let requests = -1;
    let status = null;
    let planned = [0, 0, 0, 0, 0];
    let shownKey = '';

    const say = key => table(labels, 'status', getLanguage())[key] ?? labels.en[key];
    const total = () => planned.reduce((sum, value) => sum + value, 0);
    const suffix = value => value ? '  (+' + value + ')' : '';

    function medallion() {
        const size = 62;
        const canvas = element('canvas', 'touch-status-medallion');
        const ratio = globalThis.devicePixelRatio || 1;
        canvas.width = canvas.height = Math.round(size * ratio);
        canvas.style.width = canvas.style.height = size + 'px';
        const context = canvas.getContext('2d');
        if (context) {
            context.scale(ratio, ratio);
            art.levelBadge(context, size / 2, size / 2, size / 2 / 1.75, status.level, status.maxLevel, performance.now() / 1000);
        }
        return canvas;
    }

    function emblem(attribute) {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '-3 -3 30 30');
        svg.setAttribute('class', 'touch-status-emblem');
        svg.setAttribute('aria-hidden', 'true');
        const fill = 'rgb(' + attribute.gauge.top.map(value => Math.min(255, value + 60)).join(',') + ')';
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', attribute.sign);
        path.setAttribute('fill', fill);
        path.setAttribute('stroke', 'rgba(20,12,6,0.86)');
        svg.append(path);
        return svg;
    }

    function renderHero() {
        const gains = status.gains ?? {};
        const identity = element('div', 'touch-status-identity');
        const names = element('div', '');
        names.append(element('strong', 'touch-status-class', status.title), element('span', 'touch-status-level', format(say('level'), status.level)));
        identity.append(medallion(), names);
        const experience = status.level >= status.maxLevel ? say('max-level')
            : format(say('experience'), (status.experience * 100).toLocaleString(getLanguage(), { maximumFractionDigits: 1 }) + '%');
        const width = 188;
        const vital = (key, [value, max], colours, height) =>
            gauge(max > 0 ? value / max : 0, format(say(key), value, max) + suffix(gains[key]), width, height, colours);
        hero.replaceChildren(identity, gauge(status.experience, experience, width, 16, art.gauges.experience), section(say('vitals')),
            vital('life', status.life, art.gauges.life, 18), vital('mana', status.mana, art.gauges.mana, 18),
            vital('stamina', status.stamina, art.gauges.stamina, 18),
            vital('weight', status.weight, status.overweight ? art.gauges.life : art.gauges.experience, 14));
    }

    function pointButton(text, label, enabled, onClick) {
        const button = element('button', 'touch-status-point', text);
        button.type = 'button';
        button.setAttribute('aria-label', label);
        button.disabled = !enabled;
        button.addEventListener('click', onClick);
        return button;
    }

    function renderAttributes() {
        const free = Math.max(0, status.points - total());
        const ready = !status.busy;
        const strongest = Math.max(1, ...status.attributes.map((value, index) => value + planned[index]));
        const heading = section(say('attributes'));
        const counter = chip(free === 1 ? say('free-one') : format(say('free'), free), free > 0 ? 'open' : 'muted');
        counter.dataset.role = 'touch-status-free';
        heading.append(counter);
        rows.replaceChildren(heading, ...attributes.map((attribute, index) => {
            const name = say(attribute.key);
            const row = element('div', 'touch-status-row');
            if (index === status.main) row.dataset.main = 'true';
            const text = element('div', 'touch-status-text');
            const line = element('div', 'touch-status-line');
            line.append(element('span', 'touch-status-name', name));
            if (index === status.main) line.append(chip(say('damage'), 'open'));
            const value = element('strong', 'touch-status-value', planned[index] > 0 ? status.attributes[index] + ' +' + planned[index] : String(status.attributes[index]));
            if (planned[index] > 0) value.dataset.planned = 'true';
            line.append(element('span', 'touch-journal-spacer'), value);
            text.append(line, gauge((status.attributes[index] + planned[index]) / strongest, '', 200, 6, attribute.gauge),
                element('span', 'touch-status-effect', say(attribute.key + '-effect')));
            const minus = pointButton('−', format(say('minus'), name), ready && planned[index] > 0, () => change(index, -1));
            minus.style.visibility = planned[index] > 0 ? 'visible' : 'hidden';
            row.append(emblem(attribute), text, minus, pointButton('+', format(say('plus'), name), ready && free > 0, () => change(index, 1)));
            return row;
        }));
    }

    function renderCombat() {
        const gains = status.gains;
        const tiles = element('div', 'touch-status-tiles');
        for (const [key, value, gain] of combatTiles) {
            const tile = element('div', 'touch-status-tile');
            const line = element('div', 'touch-status-tile-line');
            line.append(element('strong', '', value(status.combat)));
            const added = gains ? gain(gains) : null;
            if (added) line.append(element('span', 'touch-status-gain', added));
            tile.append(element('span', 'touch-status-tile-label', say(key)), line);
            tiles.append(tile);
        }
        const actions = element('div', 'touch-status-actions');
        const count = total();
        const undo = element('button', 'touch-bag-action', say('undo'));
        undo.type = 'button';
        undo.disabled = status.busy || count === 0;
        undo.addEventListener('click', () => {
            planned = [0, 0, 0, 0, 0];
            poll(true);
        });
        const confirm = element('button', 'touch-bag-action touch-status-confirm', count > 0 ? format(say('confirm-count'), count) : say('confirm'));
        confirm.type = 'button';
        confirm.disabled = status.busy || count === 0;
        confirm.addEventListener('click', () => {
            getHost()?.RunWorldAction?.('touch-points:' + planned.join(','));
            planned = [0, 0, 0, 0, 0];
            poll(true);
        });
        actions.append(undo, confirm);
        combat.replaceChildren(section(say('combat')), tiles, actions);
    }

    function change(index, step) {
        planned = plan(planned, index, step, status?.points ?? 0);
        poll(true);
    }

    function poll(force = false) {
        let state;
        try {
            const text = getHost()?.TouchStatus?.(dialog.open, planned.join(',')) || '';
            state = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!state) return;
        if (state.requests !== requests) {
            const asked = requests >= 0 && state.requests > requests;
            requests = state.requests;
            if (asked) {
                if (dialog.open) dialog.close();
                else open();
                return;
            }
        }
        if (!dialog.open || !state.attributes) return;
        status = state;
        if (total() > status.points) planned = [0, 0, 0, 0, 0];
        const key = JSON.stringify([getLanguage(), planned, state]);
        if (!force && key === shownKey) return;
        shownKey = key;
        title.textContent = status.name;
        renderHero();
        renderAttributes();
        renderCombat();
    }

    function open() {
        planned = [0, 0, 0, 0, 0];
        shownKey = '';
        dialog.showModal();
        poll(true);
    }

    dialog.querySelector('[data-touch-status-close]')?.addEventListener('click', () => dialog.close());
    dialog.addEventListener('keydown', event => event.stopPropagation());

    return {
        frame: () => poll(),
        close() {
            if (dialog.open) dialog.close();
        },
        translate() {
            dialog.querySelector('[data-touch-status-close]')?.setAttribute('aria-label', say('close'));
            shownKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const statusTables = { status: labels };
