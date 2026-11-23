import { table } from './page-language.js';
import * as art from './hud-art.js';
import { chip, element, format, gauge as countGauge, section } from './touch-dom.js';
// The touch HUD's quest card and journal, the Android client's: the card at the top right under the minimap (the step,
// the hunt's count on a bar, what the automatic quest does and the button that starts or pauses it; a tap folds it), and
// the journal window (Em andamento and Concluídas, a card per entry, the chosen entry's objectives, description and
// reward). The game hands out the quests (BrowserHost.TouchQuest) and runs Ir (touch-quest-go); opening the quest window
// in the game opens the journal here.

const labels = {
    'pt-BR': { quests: 'Missões', 'no-quest': 'Nenhuma missão ativa', journey: 'Jornada do Aventureiro',
        'start-auto': 'Iniciar auto-missão', 'pause-auto': 'Pausar auto-missão', 'go-auto': 'Ir  ·  auto-missão',
        'auto-on': 'Auto-missão ativa', hint: 'Toque em Iniciar auto-missão para continuar', hunting: 'Caçando {0}',
        guiding: 'A caminho do destino', running: 'Em andamento', finished: 'Concluídas', 'step-done': 'Etapa concluída',
        done: 'Concluída', 'none-running': 'Nenhuma missão em andamento.', 'none-finished': 'Nenhuma missão concluída ainda.',
        'hint-running': 'Fale com os NPCs das cidades para receber missões.', 'hint-finished': 'As missões que você concluir aparecem aqui.',
        objectives: 'Objetivos', description: 'Descrição', rewards: 'Recompensas', defeat: 'Derrote {0}',
        'time-left': 'Tempo restante  {0}', loading: 'Carregando…', 'no-description': 'Sem descrição.', close: 'Fechar',
        fold: 'Recolher missão', unfold: 'Abrir missão' },
    en: { quests: 'Quests', 'no-quest': 'No active quest', journey: 'Adventurer\'s Journey',
        'start-auto': 'Start auto-quest', 'pause-auto': 'Pause auto-quest', 'go-auto': 'Go  ·  auto-quest',
        'auto-on': 'Auto-quest on', hint: 'Tap Start auto-quest to continue', hunting: 'Hunting {0}',
        guiding: 'On the way', running: 'In progress', finished: 'Completed', 'step-done': 'Step completed',
        done: 'Completed', 'none-running': 'No quest in progress.', 'none-finished': 'No quest completed yet.',
        'hint-running': 'Talk to the town NPCs to receive quests.', 'hint-finished': 'The quests you complete show here.',
        objectives: 'Objectives', description: 'Description', rewards: 'Rewards', defeat: 'Defeat {0}',
        'time-left': 'Time left  {0}', loading: 'Loading…', 'no-description': 'No description.', close: 'Close',
        fold: 'Fold quest', unfold: 'Open quest' }
};

const foldedKey = 'avalon.touchQuestFolded';

// hh:mm:ss of a timer's milliseconds.
export function clock(ms) {
    const seconds = Math.max(0, Math.floor(ms / 1000));
    const two = value => String(value).padStart(2, '0');
    return two(Math.floor(seconds / 3600)) + ':' + two(Math.floor(seconds / 60) % 60) + ':' + two(seconds % 60);
}

// What the card shows for a quest state: its title, the step under it (none when it repeats the title), the hunt's bar,
// whether the count is met (the gold border), the automatic quest's button and its status line.
export function questCard(state, say) {
    const step = state?.step ?? '';
    const title = !step ? say('quests') : state.journey ? say('journey') : step;
    const counting = state?.killsNeeded > 0;
    const running = Boolean(state?.hunting || state?.guiding);
    const hasWork = Boolean(state?.canGuide || state?.canHunt);
    return {
        title,
        objective: !step ? say('no-quest') : step === title ? '' : step,
        bar: counting ? { label: state.target + '  ' + state.kills + '/' + state.killsNeeded, share: state.kills / state.killsNeeded } : null,
        met: counting && state.kills >= state.killsNeeded,
        button: running ? say('pause-auto') : hasWork ? say('start-auto') : '',
        status: state?.hunting ? format(say('hunting'), state.target) : state?.guiding ? say('guiding') : hasWork ? say('hint') : ''
    };
}

// The journal's cards in order (the current step first while running, the newest finished quest first otherwise), each
// with its index in the game's list and whether it is the current step.
export function journalOrder(entries, running) {
    return entries.map((entry, index) => ({ entry, index, current: running && index === entries.length - 1 }))
        .sort((a, b) => running ? b.index - a.index : a.index - b.index);
}

// The chosen entry's objectives: while it is the current step, every step so far (all but the last done), the step's own
// line or the hunt with its count, and the timer; a finished entry is one done line.
export function objectives(state, entries, chosen, running, say) {
    if (!running || chosen !== entries.length - 1) return [{ text: entries[chosen]?.title ?? '', done: true }];
    const list = entries.map((entry, index) => ({ text: entry.title, done: index < entries.length - 1 }));
    if (state.killsNeeded > 0) {
        list.push({ text: format(say('defeat'), state.target), done: state.kills >= state.killsNeeded, gauge: [state.kills, state.killsNeeded] });
    } else if (state.objective) {
        list.push({ text: state.objective, done: false });
    }
    if (state.timerMs > 0) list.push({ text: format(say('time-left'), clock(state.timerMs)), done: false });
    return list;
}

export function createTouchQuests(layer, getHost, getLanguage) {
    const card = layer.querySelector('[data-role="touch-quest"]');
    const journal = layer.querySelector('[data-role="touch-journal"]');
    if (!card || !journal) return { frame() {}, close() {}, translate() {} };
    const cardTitle = card.querySelector('[data-role="touch-quest-title"]');
    const cardObjective = card.querySelector('[data-role="touch-quest-objective"]');
    const cardBar = card.querySelector('[data-role="touch-quest-bar"]');
    const cardStatus = card.querySelector('[data-role="touch-quest-status"]');
    const cardButton = card.querySelector('[data-role="touch-quest-auto"]');
    const tabs = journal.querySelector('[data-role="touch-journal-tabs"]');
    const list = journal.querySelector('[data-role="touch-journal-list"]');
    const detail = journal.querySelector('[data-role="touch-journal-detail"]');
    let folded = false;
    try {
        folded = globalThis.localStorage?.getItem(foldedKey) === 'folded';
    } catch {
        // No storage here: the card starts open.
    }
    let state = null;
    let shownCard = '';
    let requests = -1;
    let entries = { running: [], finished: [] };
    let journalKey = '';
    let running = true;
    let chosen = -1;

    const say = key => table(labels, 'quest', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);

    // The hunt's count on the life gauge, as the Android journal shows it.
    const gauge = (done, needed, width, height) => countGauge(needed > 0 ? done / needed : 0, done + '/' + needed, width, height);

    function stateOf(current) {
        return running ? current ? [say('running'), 'open'] : [say('step-done'), 'done'] : [say('done'), 'done'];
    }

    function goButton() {
        const active = state.hunting || state.guiding;
        if (!active && !state.canGuide && !state.canHunt) return null;
        const button = element('button', 'touch-journal-go', active ? say('pause-auto') : say('go-auto'));
        button.type = 'button';
        button.addEventListener('click', () => {
            run('touch-quest-go');
            if (!active) journal.close();
            else poll(true);
        });
        return button;
    }

    function renderTabs() {
        tabs.replaceChildren();
        for (const [name, isRunning, count] of [[say('running'), true, entries.running.length], [say('finished'), false, entries.finished.length]]) {
            const tab = element('button', 'touch-journal-tab', count > 0 ? name + '  ' + count : name);
            tab.type = 'button';
            tab.setAttribute('aria-pressed', String(isRunning === running));
            tab.addEventListener('click', () => {
                if (running === isRunning) return;
                running = isRunning;
                chosen = -1;
                render();
                list.scrollTop = 0;
            });
            tabs.append(tab);
        }
        tabs.append(element('span', 'touch-journal-spacer'));
        if (state.hunting || state.guiding) tabs.append(chip(say('auto-on'), 'done'));
    }

    function renderList(shown) {
        if (shown.length === 0) {
            list.replaceChildren(element('p', 'touch-journal-empty', say(running ? 'none-running' : 'none-finished')));
            return;
        }
        list.replaceChildren(...journalOrder(shown, running).map(({ entry, index, current }) => {
            const item = element('button', 'touch-journal-card');
            item.type = 'button';
            item.setAttribute('aria-pressed', String(index === chosen));
            if (current) item.dataset.current = 'true';
            const top = element('span', 'touch-journal-card-top');
            const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            icon.setAttribute('viewBox', '0 0 24 24');
            icon.setAttribute('aria-hidden', 'true');
            const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('d', art.glyphs[current ? 'journal' : 'run'] ?? '');
            icon.append(path);
            top.append(icon, element('span', 'touch-journal-card-title', entry.title));
            const bottom = element('span', 'touch-journal-card-bottom');
            const [text, kind] = stateOf(current);
            bottom.append(chip(text, kind));
            if (current && state.killsNeeded > 0) bottom.append(gauge(state.kills, state.killsNeeded, 96, 14));
            item.append(top, bottom);
            item.addEventListener('click', () => {
                chosen = index;
                render();
            });
            return item;
        }));
    }

    function renderDetail(shown) {
        detail.replaceChildren();
        if (shown.length === 0) {
            detail.append(element('p', 'touch-journal-hint', say(running ? 'hint-running' : 'hint-finished')));
            return;
        }
        const entry = shown[chosen];
        const current = running && chosen === shown.length - 1;
        const header = element('header', 'touch-journal-detail-head');
        const [text, kind] = stateOf(current);
        header.append(element('h3', 'touch-journal-detail-title', entry.title), chip(text, kind));
        const content = element('div', 'touch-journal-detail-body');
        content.append(section(say('objectives')));
        const checklist = element('ul', 'touch-journal-objectives');
        for (const objective of objectives(state, shown, chosen, running, say)) {
            const row = element('li', '', objective.text);
            row.dataset.done = String(objective.done);
            if (objective.gauge) row.append(gauge(objective.gauge[0], objective.gauge[1], 110, 16));
            checklist.append(row);
        }
        content.append(checklist, section(say('description')));
        const paragraphs = entry.paragraphs;
        if (!paragraphs) {
            content.append(element('p', 'touch-journal-text', say('loading')));
        } else if (paragraphs.every(line => !line)) {
            content.append(element('p', 'touch-journal-text', say('no-description')));
        } else {
            for (const line of paragraphs) content.append(element('p', 'touch-journal-text', line || ' '));
        }
        if (entry.reward) {
            content.append(section(say('rewards')));
            const shelf = element('div', 'touch-journal-rewards');
            shelf.append(chip(entry.reward, 'reward'));
            content.append(shelf);
        }
        detail.append(header, content);
        const go = current ? goButton() : null;
        if (go) detail.append(go);
    }

    function render() {
        const shown = running ? entries.running : entries.finished;
        if (chosen < 0 || chosen >= shown.length) chosen = running ? shown.length - 1 : 0;
        const listTop = list.scrollTop;
        const detailTop = detail.querySelector('.touch-journal-detail-body')?.scrollTop ?? 0;
        renderTabs();
        renderList(shown);
        renderDetail(shown);
        list.scrollTop = listTop;
        const body = detail.querySelector('.touch-journal-detail-body');
        if (body) body.scrollTop = detailTop;
    }

    function showCard(text) {
        if (text === shownCard) return;
        shownCard = text;
        card.hidden = !state.shown;
        card.style.top = 'calc(' + (state.top * 100) + 'dvh)';
        const view = questCard(state, say);
        cardTitle.textContent = view.title + (folded ? '  ▸' : '  ▾');
        card.setAttribute('aria-label', say(folded ? 'unfold' : 'fold'));
        card.dataset.met = String(view.met);
        cardObjective.textContent = view.objective;
        cardObjective.hidden = folded || !view.objective;
        cardBar.hidden = !view.bar;
        if (view.bar) {
            cardBar.style.setProperty('--share', String(Math.max(0, Math.min(1, view.bar.share))));
            cardBar.textContent = view.bar.label;
        }
        cardStatus.textContent = view.status;
        cardStatus.hidden = folded || !view.status;
        cardButton.textContent = view.button;
        cardButton.hidden = folded || !view.button;
    }

    // The card every state period; the journal's entries only while it is open and the quests, language or a loading
    // description changed.
    function poll(force = false) {
        let text;
        try {
            text = getHost()?.TouchQuest?.(journal.open) || '';
            state = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!state) return;
        if (state.requests !== requests) {
            const asked = requests >= 0 && state.requests > requests;
            requests = state.requests;
            if (asked && !journal.open) open();
        }
        showCard(JSON.stringify({ ...state, running: undefined, finished: undefined, folded }));
        if (!journal.open) return;
        const loading = (state.running ?? []).concat(state.finished ?? []).some(entry => !entry.paragraphs);
        const key = JSON.stringify({ ...state, timerMs: Math.ceil((state.timerMs ?? 0) / 1000), top: 0, running: undefined, finished: undefined });
        if (state.running) {
            if (entries.running.length !== state.running.length || entries.finished.length !== (state.finished?.length ?? 0)) chosen = -1;
            entries = { running: state.running, finished: state.finished ?? [] };
        }
        if (force || loading || key !== journalKey) {
            journalKey = key;
            render();
        }
    }

    function open() {
        running = true;
        chosen = -1;
        journalKey = '';
        journal.showModal();
        poll(true);
    }

    card.addEventListener('click', event => {
        if (event.target.closest('[data-role="touch-quest-auto"]')) {
            run('touch-quest-go');
            poll(true);
            return;
        }
        folded = !folded;
        try {
            globalThis.localStorage?.setItem(foldedKey, folded ? 'folded' : 'open');
        } catch {
            // Not kept beyond this page.
        }
        shownCard = '';
        poll();
    });
    journal.querySelector('[data-touch-journal-close]')?.addEventListener('click', () => journal.close());
    journal.addEventListener('keydown', event => event.stopPropagation());

    return {
        frame: () => poll(),
        close() {
            if (journal.open) journal.close();
        },
        translate() {
            journal.querySelector('[data-role="touch-journal-title"]').textContent = say('quests');
            journal.querySelector('[data-touch-journal-close]')?.setAttribute('aria-label', say('close'));
            shownCard = '';
            journalKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const questTables = { quest: labels };
