import { table } from './page-language.js';
import * as art from './hud-art.js';
import { chip, element, format, gauge, pictureAddress, section } from './touch-dom.js';
// The touch HUD's bag, the Android client's character window: on the left the equipment slots around the figure with
// gold and weight under them, in the middle the bag as a field of item wells with each item's picture at its footprint
// and the potion belt under it, on the right the chosen item's sheet with its buttons. Wells take the item's tint
// (unique, enhanced, not for this class). The game hands out the bag (BrowserHost.TouchBag) and runs the buttons
// (touch-bag:<action>:<item>[:<box>]); opening the inventory in the game opens the bag here.

const labels = {
    'pt-BR': { inventory: 'Inventário', close: 'Fechar', equipment: 'Equipamento', bag: 'Bolsa', belt: 'Cinto', gold: 'Ouro',
        weight: 'Peso {0}/{1}', 'weight-none': 'Peso', used: 'Peso {0} / {1}  ·  {2}/{3}', Weapon: 'Arma', OffHand: 'Escudo',
        Armor: 'Armadura', Gloves: 'Luvas', Boots: 'Botas', Bracelet: 'Bracelete', Amulet: 'Amuleto', RingLeft: 'Anel',
        RingRight: 'Anel', Sheltom: 'Sheltom', potion: 'Poção', item: 'Item', unique: 'Único', mixed: 'Misturado',
        upgraded: 'Aprimorado +{0}', aging: 'Em maturação', blocked: 'Sua classe não pode usar', maturity: 'Maturação +{0} · {1}/{2}',
        count: 'Quantidade', Equip: 'Equipar', Unequip: 'Retirar', BeltTake: 'Guardar na bolsa', BeltPut: 'Cinto {0}',
        Drink: 'Beber', Use: 'Usar' },
    en: { inventory: 'Inventory', close: 'Close', equipment: 'Equipment', bag: 'Bag', belt: 'Belt', gold: 'Gold',
        weight: 'Weight {0}/{1}', 'weight-none': 'Weight', used: 'Weight {0} / {1}  ·  {2}/{3}', Weapon: 'Weapon', OffHand: 'Shield',
        Armor: 'Armour', Gloves: 'Gloves', Boots: 'Boots', Bracelet: 'Bracelet', Amulet: 'Amulet', RingLeft: 'Ring',
        RingRight: 'Ring', Sheltom: 'Sheltom', potion: 'Potion', item: 'Item', unique: 'Unique', mixed: 'Mixed',
        upgraded: 'Upgraded +{0}', aging: 'Maturing', blocked: 'Your class cannot use it', maturity: 'Maturing +{0} · {1}/{2}',
        count: 'Quantity', Equip: 'Equip', Unequip: 'Take off', BeltTake: 'Back to the bag', BeltPut: 'Belt {0}',
        Drink: 'Drink', Use: 'Use' }
};

// The equipment wells around the figure, in slots of SlotPitch: weapon, ring, gloves and bracelet down the left, shield,
// ring, boots and sheltom down the right, the amulet at the neck and the armour on the chest (rows may fall between).
export const doll = [
    ['Weapon', 0, 0], ['RingLeft', 0, 1], ['Gloves', 0, 2], ['Bracelet', 0, 3], ['OffHand', 2, 0], ['RingRight', 2, 1],
    ['Boots', 2, 2], ['Sheltom', 2, 3], ['Amulet', 1, 0.85], ['Armor', 1, 1.85]
];
export const slotSize = 44;
export const slotPitch = 53;
export const figureMargin = 6;
// The equipment column's width: the figure's three columns of wells.
const purseWidth = figureMargin * 2 + slotPitch * 2 + slotSize;

// The cells used in the bag: every bag item's footprint.
export const cellsUsed = items => items.reduce((sum, item) => sum + (item.width > 0 ? item.width * item.height : 0), 0);

// A tooltip line ("Label:  value") as the sheet's row: the label and the value, or the whole line as the label.
export function sheetRow(text) {
    const colon = text.indexOf(':');
    return colon < 0 ? { label: text, value: '' } : { label: text.slice(0, colon).trim(), value: text.slice(colon + 1).trim() };
}

// An item sheet's buttons: each action's label and what it runs; a belt put is one button per belt box.
export function sheetButtons(sheet, boxes, say) {
    const buttons = [];
    for (const action of sheet?.actions ?? []) {
        if (action === 'BeltPut') {
            for (let box = 0; box < boxes; box++) {
                buttons.push({ label: format(say('BeltPut'), box + 1), run: 'touch-bag:BeltPut:' + sheet.id + ':' + box, small: true });
            }
        } else {
            buttons.push({ label: say(action), run: 'touch-bag:' + action + ':' + sheet.id, closes: action === 'Use' });
        }
    }
    return buttons;
}

export function createTouchBag(layer, getHost, getLanguage, picture) {
    const dialog = layer.querySelector('[data-role="touch-bag"]');
    if (!dialog) return { frame() {}, close() {}, translate() {} };
    const title = dialog.querySelector('[data-role="touch-bag-title"]');
    const equipment = dialog.querySelector('[data-role="touch-bag-equipment"]');
    const grid = dialog.querySelector('[data-role="touch-bag-grid"]');
    const sheetNode = dialog.querySelector('[data-role="touch-bag-sheet"]');
    const purse = dialog.querySelector('[data-role="touch-bag-purse"]');
    const belt = dialog.querySelector('[data-role="touch-bag-belt"]');
    const note = dialog.querySelector('[data-role="touch-bag-note"]');
    let requests = -1;
    let bag = null;
    let chosen = '';
    let shownKey = '';
    // A picture was not there yet when the bag was drawn: it is drawn again until it is.
    let waiting = false;

    const say = key => table(labels, 'bag', getLanguage())[key] ?? labels.en[key] ?? key;
    const run = action => getHost()?.RunWorldAction?.(action);

    // An item's picture as an image address; empty while its file loads (the picture cache asks again later).
    function pictureUrl(path) {
        const canvas = path ? picture(path) : null;
        if (!canvas) {
            waiting = waiting || Boolean(path);
            return '';
        }
        return pictureAddress(canvas);
    }

    function tile(item) {
        const node = element('button', 'touch-bag-well');
        node.type = 'button';
        node.dataset.tint = item.tint;
        // The tutorials point at a tile by its item (touch-tutorial.js).
        node.dataset.item = item.id;
        node.setAttribute('aria-label', item.name);
        node.setAttribute('aria-pressed', String(item.id === chosen));
        if (item.most) node.dataset.most = 'true';
        const url = pictureUrl(item.picture);
        if (url) {
            const image = element('img', 'touch-bag-picture');
            image.src = url;
            image.alt = '';
            node.append(image);
        }
        if (item.upgrade > 0) node.append(element('span', 'touch-bag-upgrade', '+' + item.upgrade));
        if (item.count > 1) node.append(element('span', 'touch-bag-count', String(item.count)));
        node.addEventListener('click', () => {
            chosen = chosen === item.id ? '' : item.id;
            poll(true);
        });
        return node;
    }

    function emptyWell(text) {
        const node = element('span', 'touch-bag-well', text);
        node.dataset.empty = 'true';
        return node;
    }

    function place(node, left, top, width, height) {
        node.style.left = left + 'px';
        node.style.top = top + 'px';
        node.style.width = width + 'px';
        node.style.height = height + 'px';
        return node;
    }

    function renderEquipment() {
        equipment.replaceChildren(...doll.map(([slot, column, row]) => {
            const item = bag.items.find(entry => entry.slot === slot);
            return place(item ? tile(item) : emptyWell(say(slot)), figureMargin + column * slotPitch, figureMargin + row * slotPitch, slotSize, slotSize);
        }));
        const coin = element('span', 'touch-bag-coin');
        purse.replaceChildren(coin, element('span', 'touch-bag-muted', say('gold')),
            element('strong', 'touch-bag-gold', bag.money.toLocaleString(getLanguage())));
        const weight = bag.maxWeight > 0
            ? gauge(bag.weight / bag.maxWeight, format(say('weight'), bag.weight, bag.maxWeight), purseWidth, 16, bag.overweight ? art.gauges.life : art.gauges.stamina)
            : gauge(0, say('weight-none'), purseWidth, 16, art.gauges.stamina);
        purse.append(weight);
    }

    function renderGrid() {
        const inBag = bag.items.filter(item => item.width > 0);
        note.textContent = bag.maxWeight > 0 ? format(say('used'), bag.weight, bag.maxWeight, cellsUsed(inBag), bag.columns * bag.rows) : '';
        const cell = Math.max(8, Math.floor(Math.min(grid.clientWidth / bag.columns, grid.clientHeight / bag.rows)));
        const field = place(element('div', 'touch-bag-field'), 0, 0, cell * bag.columns, cell * bag.rows);
        field.style.setProperty('--cell', cell + 'px');
        for (const item of inBag) {
            field.append(place(tile(item), item.column * cell + 1, item.row * cell + 1, cell * item.width - 2, cell * item.height - 2));
        }
        grid.replaceChildren(field);
        const boxes = [];
        for (let box = 0; box < bag.belt; box++) {
            const item = bag.items.find(entry => entry.box === box);
            boxes.push(item ? tile(item) : emptyWell(String(box + 1)));
        }
        belt.replaceChildren(...boxes);
    }

    function renderSheet() {
        const sheet = bag.sheet;
        sheetNode.hidden = !sheet;
        if (!sheet) {
            sheetNode.replaceChildren();
            return;
        }
        const item = bag.items.find(entry => entry.id === sheet.id);
        const heading = element('header', 'touch-bag-sheet-head');
        const well = element('span', 'touch-bag-well touch-bag-sheet-picture');
        well.dataset.tint = item?.tint ?? '';
        if (item?.most) well.dataset.most = 'true';
        const url = pictureUrl(item?.picture);
        if (url) {
            const image = element('img', 'touch-bag-picture');
            image.src = url;
            image.alt = '';
            well.append(image);
        }
        const identity = element('div', 'touch-bag-identity');
        const name = element('h3', 'touch-bag-name', item?.name ?? '');
        name.dataset.tint = item?.tint ?? '';
        const chips = element('div', 'touch-bag-chips');
        chips.append(chip(say(sheet.kind), 'muted'));
        if (sheet.rank) chips.append(chip(format(say(sheet.rank), item?.upgrade ?? 0), item?.tint || 'open'));
        identity.append(name, chips);
        heading.append(well, identity);
        const content = [heading];
        if (sheet.blocked) content.push(chip(say('blocked'), 'blocked'));
        if (sheet.maturity) {
            const { level, count, goal } = sheet.maturity;
            content.push(gauge(count / goal, format(say('maturity'), level, count, goal), 190, 18, art.gauges.mana));
        }
        const rows = element('div', 'touch-bag-rows');
        const lines = (item?.count > 1 ? [{ text: say('count') + ':  ' + item.count, color: '' }] : []).concat(sheet.lines);
        for (const line of lines) {
            const { label, value } = sheetRow(line.text);
            const row = element('p', 'touch-bag-row');
            row.style.color = line.color;
            row.append(element('span', '', label), element('strong', '', value));
            rows.append(row);
        }
        content.push(rows);
        const actions = element('div', 'touch-bag-actions');
        for (const button of sheetButtons(sheet, bag.belt, say)) {
            const node = element('button', button.small ? 'touch-bag-action touch-bag-action-small' : 'touch-bag-action', button.label);
            node.dataset.run = button.run;
            node.type = 'button';
            node.disabled = bag.busy;
            node.addEventListener('click', () => {
                run(button.run);
                if (button.closes) dialog.close();
                else poll(true);
            });
            actions.append(node);
        }
        content.push(actions);
        sheetNode.replaceChildren(...content);
    }

    function render() {
        waiting = false;
        renderEquipment();
        renderSheet();
        renderGrid();
    }

    function poll(force = false) {
        let state;
        try {
            const text = getHost()?.TouchBag?.(dialog.open, chosen) || '';
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
        if (!dialog.open || !state.items) return;
        bag = state;
        if (chosen && !bag.sheet) chosen = '';
        const key = JSON.stringify([getLanguage(), state]);
        if (force || waiting || key !== shownKey) {
            shownKey = key;
            render();
        }
    }

    function open() {
        chosen = '';
        shownKey = '';
        dialog.showModal();
        poll(true);
    }

    dialog.querySelector('[data-touch-bag-close]')?.addEventListener('click', () => dialog.close());
    dialog.addEventListener('keydown', event => event.stopPropagation());
    if (globalThis.ResizeObserver) {
        new ResizeObserver(() => {
            if (dialog.open && bag) renderGrid();
        }).observe(grid);
    }

    return {
        frame: () => poll(),
        close() {
            if (dialog.open) dialog.close();
        },
        translate() {
            title.textContent = say('inventory');
            dialog.querySelector('[data-touch-bag-close]')?.setAttribute('aria-label', say('close'));
            dialog.querySelector('[data-role="touch-bag-equipment-title"]').replaceChildren(section(say('equipment')));
            dialog.querySelector('[data-role="touch-bag-bag-title"]').textContent = say('bag');
            dialog.querySelector('[data-role="touch-bag-belt-title"]').textContent = say('belt');
            shownKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const bagTables = { bag: labels };
