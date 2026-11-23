import { table } from './page-language.js';
import { element } from './touch-dom.js';
// The touch HUD's first-time tutorials, the Android client's: the first time something is needed (the HUD on arrival,
// wearing the starter kit, a potion on the belt, the automatic quest, the first attack, spending attribute points), the
// screen dims around the button to press, a pulsing gold ring marks it and a card explains what to do. Only that button
// answers taps; a step moves on by itself once its goal is met, or with Entendi on a step that only explains. Each
// lesson shows once (kept in this browser), none starts while the automatic quest runs, and Rever tutorial in the menu
// shows them all again. The game tells when lessons start and steps end (BrowserHost.TouchTutorial); the coach is a
// popover so it stands over the bag and status windows.

const labels = {
    'pt-BR': {
        ok: 'Entendi', skip: 'Pular tutorial',
        'hud-1-title': 'Seu personagem', 'hud-1': 'Aqui ficam sua vida (vermelho), mana (azul) e estamina (verde); o selo dourado é o seu nível.',
        'hud-2-title': 'Status do personagem', 'hud-2': 'Toque no seu retrato para ver força, espírito, talento, agilidade, vitalidade, ataque e defesa.',
        'hud-3-title': 'Sua missão', 'hud-3': 'O cartão mostra o objetivo da missão. Toque nele para minimizar e ver só o progresso.',
        'hud-4-title': 'Atacar', 'hud-4': 'Toque para atacar o monstro selecionado ou o mais próximo. Habilidades e poções ficam em volta.',
        'hud-5-title': 'Menu', 'hud-5': 'No Menu você abre o inventário, ajusta as configurações, edita a posição de cada parte da HUD e revê este tutorial.',
        'equip-1-title': 'Equipar seu kit', 'equip-1': 'O guia te deu arma, armadura e, se sua classe usa, escudo. Toque em Bolsa para abrir o inventário.',
        'equip-2-title': 'Escolha a peça', 'equip-2': 'Toque na peça destacada na sua bolsa.',
        'equip-3-title': 'Equipe', 'equip-3': 'Toque em Equipar para vestir a peça. Repita com as outras.',
        'belt-1-title': 'Poção no cinto', 'belt-1': 'Poções no cinto podem ser usadas em combate com um toque. Toque em Bolsa.',
        'belt-2-title': 'Escolha a poção', 'belt-2': 'Toque na poção na sua bolsa.',
        'belt-3-title': 'Guarde no cinto', 'belt-3': 'Toque em Cinto 1 para guardar a poção. Ela aparece ao lado do botão de ataque.',
        'auto-1-title': 'Auto-missão', 'auto-1': 'Toque aqui e o personagem segue a missão sozinho: anda até o NPC, caça e entrega. Toque de novo para pausar.',
        'attack-1-title': 'Hora de lutar', 'attack-1': 'Um monstro está por perto. Toque em Atacar para lutar com ele.',
        'points-1-title': 'Pontos de atributo', 'points-1': 'Você subiu de nível e ganhou pontos para fortalecer seu personagem. Toque no seu retrato para abrir o Status.',
        'points-2-title': 'Pontos livres', 'points-2': 'Aqui ficam os pontos que você ainda não usou.',
        'points-3-title': 'Reserve um ponto', 'points-3': 'Toque em + no atributo destacado, o que mais conta para o dano da sua classe. Cada toque reserva um ponto; o − devolve.',
        'points-4-title': 'Confirme', 'points-4': 'Toque em Confirmar para gastar os pontos reservados. Depois de confirmar não dá para voltar atrás.'
    },
    en: {
        ok: 'Got it', skip: 'Skip tutorial',
        'hud-1-title': 'Your character', 'hud-1': 'Here are your life (red), mana (blue) and stamina (green); the gold seal is your level.',
        'hud-2-title': 'Character status', 'hud-2': 'Tap your portrait to see strength, spirit, talent, agility, health, attack and defense.',
        'hud-3-title': 'Your quest', 'hud-3': "The card shows the quest's goal. Tap it to fold it and see only the progress.",
        'hud-4-title': 'Attack', 'hud-4': 'Tap to attack the selected monster or the nearest one. Skills and potions sit around it.',
        'hud-5-title': 'Menu', 'hud-5': 'The Menu opens the inventory and the settings, edits where each part of the HUD goes and shows this tutorial again.',
        'equip-1-title': 'Wear your kit', 'equip-1': 'The guide gave you a weapon, armor and, if your class uses one, a shield. Tap Bag to open the inventory.',
        'equip-2-title': 'Pick the piece', 'equip-2': 'Tap the marked piece in your bag.',
        'equip-3-title': 'Wear it', 'equip-3': 'Tap Equip to wear the piece. Do the same with the others.',
        'belt-1-title': 'Potion on the belt', 'belt-1': 'Potions on the belt are drunk in a fight with one tap. Tap Bag.',
        'belt-2-title': 'Pick the potion', 'belt-2': 'Tap the potion in your bag.',
        'belt-3-title': 'Put it on the belt', 'belt-3': 'Tap Belt 1 to keep the potion there. It shows beside the attack button.',
        'auto-1-title': 'Auto-quest', 'auto-1': 'Tap here and the character follows the quest alone: it walks to the NPC, hunts and hands in. Tap again to pause.',
        'attack-1-title': 'Time to fight', 'attack-1': 'A monster is near. Tap Attack to fight it.',
        'points-1-title': 'Attribute points', 'points-1': 'You leveled up and earned points to make your character stronger. Tap your portrait to open the Status.',
        'points-2-title': 'Free points', 'points-2': 'Here are the points you have not used yet.',
        'points-3-title': 'Plan a point', 'points-3': "Tap + on the marked attribute, the one that counts most for your class's damage. Each tap plans a point; − takes it back.",
        'points-4-title': 'Confirm', 'points-4': 'Tap Confirm to spend the planned points. Once confirmed they cannot be taken back.'
    }
};

const storageKey = 'etervale.touch-tutorials';
// Raised when the lessons change enough to be shown again to everyone.
const round = 1;

const shown = node => Boolean(node) && !node.closest('[hidden]') && node.getBoundingClientRect().width > 0;

// The lessons in the order they are offered. A step names what to press (a selector, or null for one that only
// explains) and when it is done (null: Entendi); a lesson may lose its place (its window closed midway) and start over.
export const lessons = [
    { id: 'hud', starts: c => c.state.idle, steps: [
        { target: () => '.touch-player' },
        { target: () => '.touch-player', done: c => c.statusSeen },
        { target: () => '[data-role="touch-quest"]' },
        { target: () => '[data-touch-action="touch-attack"]' },
        { target: () => '[data-touch-menu-open]' }
    ] },
    { id: 'equip', starts: c => c.state.idle && Boolean(c.state.piece), steps: [
        { target: () => '.touch-dock-orb[data-touch-action="inventory"]', done: c => c.open('touch-bag') },
        { target: c => '[data-role="touch-bag"] [data-item="' + c.state.piece + '"]', done: c => !c.state.piece || c.pressed(c.state.piece) },
        { target: () => '[data-run^="touch-bag:Equip:"]', done: c => !c.state.piece }
    ], lost: c => Boolean(c.state.piece) && (!c.open('touch-bag') || !c.pressed(c.state.piece)) },
    { id: 'belt', starts: c => c.state.idle && c.state.beltEmpty && Boolean(c.state.potion), steps: [
        { target: () => '.touch-dock-orb[data-touch-action="inventory"]', done: c => c.open('touch-bag') },
        { target: c => '[data-role="touch-bag"] [data-item="' + c.state.potion + '"]', done: c => c.pressed(c.state.potion) || !c.state.beltEmpty },
        { target: () => '[data-run^="touch-bag:BeltPut:"][data-run$=":0"]', done: c => !c.state.beltEmpty }
    ], lost: c => !c.open('touch-bag') && c.state.beltEmpty },
    { id: 'auto', starts: c => c.state.idle && c.visible('[data-role="touch-quest-auto"]'), steps: [
        { target: () => '[data-role="touch-quest-auto"]', done: c => !c.state.idle }
    ] },
    { id: 'attack', starts: c => c.state.idle && c.state.hunting && c.state.hostile, steps: [
        { target: () => '[data-touch-action="touch-attack"]', done: c => !c.state.hunting }
    ] },
    // Points to spend matter during the automatic quest too: this lesson waits only for a moment out of a fight.
    { id: 'points', starts: c => c.state.alive && c.state.statPoints > 0 && !c.state.attacking, steps: [
        { target: () => '.touch-player', done: c => c.open('touch-status') },
        { target: () => '[data-role="touch-status-free"]' },
        // The "+" ends the main attribute's row (touch-status.js).
        { target: () => '[data-role="touch-status"] [data-main] > .touch-status-point:last-child', done: c => c.planned() || c.spent() },
        { target: () => '.touch-status-confirm', done: c => c.spent() }
    ], lost: c => !c.open('touch-status') }
];

// The step a lesson stands at: back to the first once it lost its place, then past every step already done.
export function advance(lesson, step, context) {
    let at = step > 0 && lesson.lost?.(context) ? 0 : step;
    while (at < lesson.steps.length && lesson.steps[at].done?.(context)) at++;
    return at;
}

// The first lesson not done yet whose moment came.
export function nextLesson(done, context) {
    return lessons.find(lesson => !done.includes(lesson.id) && lesson.starts(context)) ?? null;
}

// The lessons already shown, read from their text; an older round is forgotten so they show again.
export function readDone(text) {
    try {
        const saved = JSON.parse(text || '{}');
        return saved?.round === round && Array.isArray(saved.done) ? saved.done.filter(id => typeof id === 'string') : [];
    } catch {
        return [];
    }
}

export function createTouchTutorial(layer, getHost, getLanguage) {
    const coach = layer.querySelector('[data-role="touch-coach"]');
    if (!coach) return { poll() {}, replay() {}, close() {}, translate() {} };
    const hole = element('div', 'touch-coach-hole');
    const card = element('div', 'touch-coach-card');
    const heading = element('div', 'touch-coach-heading');
    const title = element('strong', 'touch-coach-title');
    const count = element('span', 'touch-coach-count');
    const text = element('p', 'touch-coach-text');
    const buttons = element('div', 'touch-coach-buttons');
    const skip = element('button', 'touch-bag-action touch-coach-button');
    const ok = element('button', 'touch-bag-action touch-coach-button');
    skip.type = 'button';
    ok.type = 'button';
    ok.dataset.on = 'true';
    heading.append(title, count);
    buttons.append(skip, ok);
    card.append(heading, text, buttons);
    coach.append(hole, card);
    let done = load();
    let lesson = null;
    let step = 0;
    let target = null;
    let statusSeen = false;
    let points = 0;

    const say = key => table(labels, 'tutorial', getLanguage())[key] ?? labels.en[key];

    function load() {
        try {
            return readDone(globalThis.localStorage?.getItem(storageKey));
        } catch {
            return [];
        }
    }

    function store() {
        try {
            globalThis.localStorage?.setItem(storageKey, JSON.stringify({ round, done }));
        } catch {
            // A private window or blocked storage keeps the lessons for this visit only.
        }
    }

    function context(state) {
        const status = layer.querySelector('[data-role="touch-status"]');
        return {
            state,
            statusSeen,
            open: role => Boolean(layer.querySelector('[data-role="' + role + '"]')?.open),
            pressed: id => layer.querySelector('[data-role="touch-bag"] [data-item="' + id + '"]')?.getAttribute('aria-pressed') === 'true',
            visible: selector => shown(layer.querySelector(selector)),
            planned: () => Boolean(status?.querySelector('.touch-status-confirm:not(:disabled)')),
            spent: () => state.statPoints < points
        };
    }

    function showCoach(visible) {
        if (visible === !coach.hidden) return;
        coach.hidden = !visible;
        try {
            if (visible) coach.showPopover?.();
            else coach.hidePopover?.();
        } catch {
            // Without popovers the coach still shows, under any window open.
        }
    }

    function finish() {
        if (lesson && !done.includes(lesson.id)) {
            done.push(lesson.id);
            store();
        }
        lesson = null;
        target = null;
        showCoach(false);
    }

    // The window over the target and the card beside it: below when there is room, else above, else beside.
    function place() {
        const bounds = coach.getBoundingClientRect();
        const box = target?.getBoundingClientRect();
        if (!box || box.width === 0) {
            hole.hidden = true;
            card.style.left = Math.max(12, (bounds.width - card.offsetWidth) / 2) + 'px';
            card.style.top = Math.max(12, (bounds.height - card.offsetHeight) / 2) + 'px';
            return;
        }
        hole.hidden = false;
        const radius = Math.max(box.width, box.height) / 2 + 14;
        const x = box.left - bounds.left + box.width / 2;
        const y = box.top - bounds.top + box.height / 2;
        hole.style.left = x - radius + 'px';
        hole.style.top = y - radius + 'px';
        hole.style.width = hole.style.height = radius * 2 + 'px';
        const width = card.offsetWidth;
        const height = card.offsetHeight;
        const below = y + radius + 16;
        const above = y - radius - 16 - height;
        const clampX = value => Math.min(Math.max(12, value), Math.max(12, bounds.width - width - 12));
        const clampY = value => Math.min(Math.max(12, value), Math.max(12, bounds.height - height - 12));
        if (below + height < bounds.height || above >= 12) {
            card.style.left = clampX(x - width / 2) + 'px';
            card.style.top = (below + height < bounds.height ? below : above) + 'px';
        } else {
            card.style.left = clampX(x > bounds.width / 2 ? x - radius - 16 - width : x + radius + 16) + 'px';
            card.style.top = clampY(y - height / 2) + 'px';
        }
    }

    function show(current, state) {
        const entry = lesson.steps[current];
        const key = lesson.id + '-' + (current + 1);
        const selector = entry.target(context(state));
        target = selector ? layer.querySelector(selector) : null;
        if (target && !shown(target)) target = null;
        title.textContent = say(key + '-title');
        text.textContent = say(key);
        count.textContent = lesson.steps.length > 1 ? current + 1 + '/' + lesson.steps.length : '';
        skip.textContent = say('skip');
        ok.textContent = say('ok');
        ok.hidden = Boolean(entry.done);
        showCoach(true);
        place();
    }

    function poll() {
        let state;
        try {
            const textState = getHost()?.TouchTutorial?.() || '';
            state = textState ? JSON.parse(textState) : null;
        } catch {
            return;
        }
        if (!state) return;
        if (layer.querySelector('[data-role="touch-status"]')?.open) statusSeen = true;
        const current = context(state);
        if (!lesson) {
            lesson = nextLesson(done, current);
            step = 0;
            statusSeen = false;
            points = state.statPoints;
            if (!lesson) return;
        }
        step = advance(lesson, step, current);
        if (step >= lesson.steps.length) {
            finish();
            return;
        }
        show(step, state);
    }

    // Only the target answers: a tap in its window reaches it, one on the card works the card, the rest waits.
    coach.addEventListener('click', event => {
        event.stopPropagation();
        if (card.contains(event.target)) return;
        const box = target?.getBoundingClientRect();
        if (!box || event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) return;
        target.click();
        poll();
    });
    coach.addEventListener('pointerdown', event => event.stopPropagation());
    skip.addEventListener('click', finish);
    ok.addEventListener('click', () => {
        step++;
        poll();
    });
    globalThis.addEventListener?.('resize', () => {
        if (lesson) place();
    });

    return {
        poll,
        // Rever tutorial: every lesson shows again.
        replay() {
            done = [];
            store();
            lesson = null;
            showCoach(false);
        },
        close() {
            lesson = null;
            target = null;
            showCoach(false);
        },
        translate() {
            if (lesson) poll();
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const tutorialTables = { tutorial: labels };
