import * as art from './hud-art.js';
import { table } from './page-language.js';
import { chip, element, format, gauge, pictureAddress } from './touch-dom.js';
// The touch HUD's Eventos & Recompensas, the Android client's: the reward tracks as tabs on the left with a red badge of
// what each has ready, a strip with the time left and the progress, the track's entries (the welcome days as cards, the
// weekly and daily goals as rows, the level race as a timeline of diamonds), a footer to claim everything ready and a
// help note. The menu's Eventos opens it (BrowserHost.TouchRewards counts the request); while open it asks the server
// again every refreshMs so the counts move, and claims through touch-rewards:claim|all.

const labels = {
    'pt-BR': { title: 'Eventos & Recompensas', help: 'Ajuda', close: 'Fechar', progress: 'PROGRESSO', loading: 'Carregando…',
        Welcome: '7 Dias de Boas-vindas', Weekly: 'Caçada da Semana', Daily: 'Tarefas do Dia', LevelRace: 'Corrida de Nível',
        'no-deadline': '∞  Sem prazo', ends: '⏱  Encerra em {0}', resets: '⏱  Reinicia em {0}', days: '{0}d {1}h', hours: '{0}h {1}min',
        minutes: '{0}min', 'ready-none': 'Nada pronto para receber agora.', 'ready-one': '1 recompensa pronta',
        'ready-many': '{0} recompensas prontas',
        'note-Welcome': 'Não precisa ser em dias seguidos: conta cada dia diferente em que o personagem entrou.',
        'note-Weekly': 'Reinicia toda segunda às 00:00. As tarefas contam a partir do nível {0}.',
        'note-Daily': 'Reinicia todo dia às 00:00. As tarefas contam a partir do nível {0}.',
        'note-LevelRace': 'Cada nível alcançado dá a sua recompensa uma única vez.',
        'help-Welcome': 'Entre no jogo em dias diferentes durante a campanha: cada dia libera a recompensa seguinte. Toque num dia '
            + 'disponível para recebê-lo ou use Receber tudo. O que não for recebido até o fim da campanha se perde.',
        'help-LevelRace': 'Ao alcançar cada nível, a recompensa fica pronta para este personagem, sem prazo.',
        'help-goals': 'As tarefas contam o que você faz no mundo e reiniciam no horário do servidor. Receba antes do reinício: o '
            + 'que não for recebido se perde. Itens precisam de espaço na bolsa.',
        'claim-all': 'RECEBER TUDO', claim: 'Receber', available: 'Disponível', day: 'DIA {0}', final: 'RECOMPENSA FINAL',
        reward: 'RECOMPENSA', 'state-Ready': '★ {0}', 'state-Next': '🔒 Próximo', 'state-Claimed': '✔ Recebido',
        'state-Locked': '🔒 Bloqueado', 'goal-MonsterKills': 'Caçador incansável', 'goal-MonsterKills-Daily': 'Caçada diária',
        'goal-BellatraEntries': 'Coragem na Bellatra', 'goal-MonsterDrops': 'Colecionador', 'goal-Sheltoms': 'Coletor',
        'goal-Potions': 'Alquimista', 'text-MonsterKills': 'Derrote {0} monstros em qualquer mapa.',
        'text-BellatraEntries': 'Entre na Bellatra {0} vezes.', 'text-MonsterDrops': 'Recolha {0} itens deixados por monstros.',
        'text-Sheltoms': 'Colete {0} sheltoms de qualquer tipo.', 'text-Potions': 'Beba {0} poções.',
        'level-title': 'Alcance o nível {0}', 'level-text': 'Recompensa única de cada personagem.', 'level-marker': 'NÍVEL',
        gold: '{0} de ouro' },
    en: { title: 'Events & Rewards', help: 'Help', close: 'Close', progress: 'PROGRESS', loading: 'Loading…',
        Welcome: '7 Welcome Days', Weekly: 'Weekly Hunt', Daily: 'Daily Tasks', LevelRace: 'Level Race',
        'no-deadline': '∞  No deadline', ends: '⏱  Ends in {0}', resets: '⏱  Resets in {0}', days: '{0}d {1}h', hours: '{0}h {1}min',
        minutes: '{0}min', 'ready-none': 'Nothing ready to claim now.', 'ready-one': '1 reward ready', 'ready-many': '{0} rewards ready',
        'note-Welcome': 'The days need not be in a row: each different day the character entered counts.',
        'note-Weekly': 'Resets every Monday at 00:00. The tasks count from level {0}.',
        'note-Daily': 'Resets every day at 00:00. The tasks count from level {0}.',
        'note-LevelRace': 'Each level reached gives its reward once.',
        'help-Welcome': 'Enter the game on different days during the campaign: each day unlocks the next reward. Tap an available '
            + 'day to claim it or use Claim all. What is not claimed by the end of the campaign is lost.',
        'help-LevelRace': 'Reaching each level makes its reward ready for this character, with no deadline.',
        'help-goals': 'The tasks count what you do in the world and reset on the server clock. Claim before the reset: what is not '
            + 'claimed is lost. Items need room in the bag.',
        'claim-all': 'CLAIM ALL', claim: 'Claim', available: 'Available', day: 'DAY {0}', final: 'FINAL REWARD', reward: 'REWARD',
        'state-Ready': '★ {0}', 'state-Next': '🔒 Next', 'state-Claimed': '✔ Claimed', 'state-Locked': '🔒 Locked',
        'goal-MonsterKills': 'Tireless hunter', 'goal-MonsterKills-Daily': 'Daily hunt', 'goal-BellatraEntries': 'Bellatra courage',
        'goal-MonsterDrops': 'Collector', 'goal-Sheltoms': 'Gatherer', 'goal-Potions': 'Alchemist',
        'text-MonsterKills': 'Defeat {0} monsters on any map.', 'text-BellatraEntries': 'Enter Bellatra {0} times.',
        'text-MonsterDrops': 'Pick up {0} items dropped by monsters.', 'text-Sheltoms': 'Gather {0} sheltoms of any kind.',
        'text-Potions': 'Drink {0} potions.', 'level-title': 'Reach level {0}', 'level-text': "Each character's reward, once.",
        'level-marker': 'LEVEL', gold: '{0} gold' }
};

// The Android window asks for the board this often while it stays open.
const refreshMs = 5000;
const stateKinds = { Ready: 'reward', Next: 'enhanced', Claimed: 'done', Locked: 'muted' };

// The strip's clock: the time to the campaign's end or the next reset.
export function rewardsClock(track, say) {
    if (track.secondsLeft < 0) return say('no-deadline');
    const left = Math.max(0, track.secondsLeft);
    const days = Math.floor(left / 86400);
    const hours = Math.floor(left % 86400 / 3600);
    const minutes = Math.floor(left % 3600 / 60);
    const span = days > 0 ? format(say('days'), days, hours) : hours > 0 ? format(say('hours'), hours, minutes) : format(say('minutes'), minutes);
    return format(say(track.track === 'Welcome' ? 'ends' : 'resets'), span);
}

export function readyText(ready, say) {
    return ready === 0 ? say('ready-none') : ready === 1 ? say('ready-one') : format(say('ready-many'), ready);
}

export function rewardsNote(track, say) {
    return format(say('note-' + track.track), track.minLevel);
}

export function rewardsHelp(track, say) {
    return say(track === 'Welcome' || track === 'LevelRace' ? 'help-' + track : 'help-goals');
}

// A goal's title and line; the daily hunt has its own title.
export function goalWords(trackName, entry, say, language) {
    const number = new Intl.NumberFormat(language);
    const titleKey = entry.goal === 'MonsterKills' && trackName === 'Daily' ? 'goal-MonsterKills-Daily' : 'goal-' + entry.goal;
    return { title: say(titleKey) ?? '', text: format(say('text-' + entry.goal) ?? '', number.format(entry.target)) };
}

// The chip at the bottom of an entry: ready (with its word), next, received or locked, and its colour.
export function stateChip(state, readyWord, say) {
    return { text: state === 'Ready' ? format(say('state-Ready'), readyWord) : say('state-' + state), kind: stateKinds[state] ?? 'muted' };
}

export function createTouchRewards(layer, getHost, getLanguage, getPicture) {
    const dialog = layer.querySelector('[data-role="touch-rewards"]');
    if (!dialog) return { poll() {}, close() {}, translate() {} };
    const tabs = dialog.querySelector('[data-role="touch-rewards-tabs"]');
    const strip = dialog.querySelector('[data-role="touch-rewards-strip"]');
    const list = dialog.querySelector('[data-role="touch-rewards-list"]');
    const footer = dialog.querySelector('[data-role="touch-rewards-footer"]');
    const helpButton = dialog.querySelector('[data-touch-rewards-help]');
    let requests = -1;
    let state = null;
    let chosen = 'Welcome';
    let help = false;
    let listKey = '';
    let asked = 0;

    const say = key => table(labels, 'rewards', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);
    const number = value => new Intl.NumberFormat(getLanguage()).format(value);

    function button(text, className, action, prominent, disabled = false) {
        const node = element('button', className, text);
        node.type = 'button';
        node.disabled = disabled;
        if (prominent) node.dataset.on = 'true';
        if (action) {
            node.addEventListener('click', () => {
                run(action);
                poll(true);
            });
        }
        return node;
    }

    // An item's picture in a well, its count at the lower right and, once received, a check over it; true when the
    // picture is still loading.
    function well(item, className, claimed) {
        const node = element('span', className);
        const canvas = item?.picture ? getPicture(item.picture) : null;
        if (canvas) {
            const image = element('img');
            image.alt = '';
            image.src = pictureAddress(canvas);
            node.append(image);
        }
        if (item?.count > 1) node.append(element('span', 'touch-rewards-count', '×' + item.count));
        if (claimed) node.append(element('span', 'touch-rewards-check', '✔'));
        return { node, loading: Boolean(item?.picture) && !canvas };
    }

    function renderTabs() {
        tabs.replaceChildren(...state.tracks.map(track => {
            const tab = button(say(track.track), 'touch-journal-tab touch-rewards-tab');
            tab.setAttribute('aria-pressed', String(track.track === chosen));
            if (track.ready > 0) {
                const badge = element('span', 'touch-rewards-badge', track.ready > art.mostBadge ? art.mostBadge + '+' : String(track.ready));
                tab.append(badge);
            }
            tab.addEventListener('click', () => {
                if (chosen === track.track && !help) return;
                chosen = track.track;
                help = false;
                listKey = '';
                list.scrollTop = 0;
                ask(true);
                poll(true);
            });
            return tab;
        }));
    }

    function renderStrip(track) {
        const reached = track?.reached ?? 0;
        const total = track?.entries.length ?? 0;
        const title = element('div', 'touch-rewards-progress-title');
        title.append(element('span', 'touch-rewards-muted', say('progress')), element('span', 'touch-rewards-progress-count', reached + ' / ' + total));
        const progress = element('div', 'touch-rewards-progress');
        progress.append(title, gauge(total === 0 ? 0 : reached / total, '', 220, 10, art.gauges.experience));
        strip.replaceChildren(chip(track ? rewardsClock(track, say) : '…', 'reward'), progress);
    }

    function renderFooter(track) {
        const ready = track?.ready ?? 0;
        const texts = element('div', 'touch-rewards-footer-texts');
        // The server's answer to the last claim takes the note's place, as the Android client's toast would show it.
        const note = state.message ? element('p', 'touch-rewards-answer', state.message) : element('p', 'touch-rewards-muted', track ? rewardsNote(track, say) : '');
        texts.append(element('p', 'touch-rewards-ready', readyText(ready, say)), note);
        const claimAll = button(say('claim-all'), 'touch-bag-action touch-rewards-claim-all', 'touch-rewards:all:' + chosen, true, ready === 0 || state.claiming);
        footer.replaceChildren(element('span', 'touch-rewards-medal', ready > 0 ? '★' : '!'), texts, claimAll);
    }

    function rewardChips(entry) {
        const chips = element('div', 'touch-rewards-chips');
        let loading = false;
        for (const item of entry.items) {
            const piece = element('span', 'touch-rewards-chip');
            const picture = well(item, 'touch-rewards-well', false);
            loading ||= picture.loading;
            piece.append(picture.node, element('span', '', item.name));
            chips.append(piece);
        }
        if (entry.gold > 0) {
            const piece = element('span', 'touch-rewards-chip');
            piece.append(element('span', 'touch-rewards-coin'), element('span', '', format(say('gold'), number(entry.gold))));
            chips.append(piece);
        }
        return { chips, loading };
    }

    function claimControl(index, entry) {
        if (entry.state !== 'Ready') {
            const shown = stateChip(entry.state, say('claim'), say);
            return chip(shown.text, shown.kind);
        }
        return button(say('claim'), 'touch-bag-action touch-rewards-claim', 'touch-rewards:claim:' + chosen + ':' + index, true, state.claiming);
    }

    function details(entry, title, text, progress) {
        const node = element('div', 'touch-rewards-details');
        node.append(element('p', 'touch-rewards-entry-title', title), element('p', 'touch-rewards-muted', text));
        if (progress) {
            const bar = element('div', 'touch-rewards-bar');
            bar.append(gauge(entry.target === 0 ? 0 : Math.min(1, entry.progress / entry.target), '', 180, 10, art.gauges.experience),
                element('span', 'touch-rewards-progress-count', number(entry.progress) + ' / ' + number(entry.target)));
            node.append(bar);
        }
        const rewards = rewardChips(entry);
        node.append(element('p', 'touch-rewards-section', say('reward')), rewards.chips);
        return { node, loading: rewards.loading };
    }

    function renderList(track) {
        let loading = false;
        const nodes = [];
        if (help) nodes.push(element('p', 'touch-rewards-note', rewardsHelp(chosen, say)));
        else if (!track) nodes.push(element('p', 'touch-rewards-note', say('loading')));
        else if (track.track === 'Welcome') {
            const row = element('div', 'touch-rewards-days');
            track.entries.forEach((entry, index) => {
                const final = index === track.entries.length - 1;
                const card = element(entry.state === 'Ready' ? 'button' : 'div', 'touch-rewards-day');
                card.dataset.state = entry.state;
                if (final) card.dataset.final = 'true';
                const item = entry.items[0];
                const picture = well(item, 'touch-rewards-well touch-rewards-well-round', entry.state === 'Claimed');
                loading ||= picture.loading;
                card.append(element('span', 'touch-rewards-day-title', (final ? '★ ' : '') + format(say('day'), index + 1)), picture.node);
                if (final) card.append(element('span', 'touch-rewards-final', say('final')));
                const shown = stateChip(entry.state, say('available'), say);
                card.append(element('span', 'touch-rewards-day-name', item?.name ?? ''), chip(shown.text, shown.kind));
                if (entry.state === 'Ready') {
                    card.type = 'button';
                    card.disabled = state.claiming;
                    card.addEventListener('click', () => {
                        run('touch-rewards:claim:Welcome:' + index);
                        poll(true);
                    });
                }
                row.append(card);
            });
            nodes.push(row);
        } else {
            track.entries.forEach((entry, index) => {
                const row = element('div', 'touch-rewards-entry');
                row.dataset.state = entry.state;
                if (track.track === 'LevelRace') {
                    const marker = element('span', 'touch-rewards-level');
                    marker.append(element('span', '', say('level-marker')), element('strong', '', String(entry.target)));
                    row.append(marker);
                    const shown = details(entry, format(say('level-title'), entry.target), say('level-text'), false);
                    loading ||= shown.loading;
                    row.append(shown.node);
                } else {
                    const words = goalWords(track.track, entry, say, getLanguage());
                    const shown = details(entry, words.title, words.text, true);
                    loading ||= shown.loading;
                    row.append(shown.node);
                }
                row.append(claimControl(index, entry));
                nodes.push(row);
            });
        }
        const scroll = list.scrollTop;
        list.replaceChildren(...nodes);
        list.scrollTop = scroll;
        return loading;
    }

    function render(force) {
        if (!state.tracks.some(track => track.track === chosen) && state.tracks.length > 0) chosen = state.tracks[0].track;
        const track = state.tracks.find(entry => entry.track === chosen);
        helpButton?.setAttribute('aria-pressed', String(help));
        renderStrip(track);
        // The list and tabs keep the place the player scrolled to: rebuilt only when what they show changes.
        const key = JSON.stringify([getLanguage(), chosen, help, state.claiming, state.message, state.tracks.map(entry => ({ ...entry, secondsLeft: 0 }))]);
        if (!force && key === listKey) return;
        listKey = renderList(track) ? '' : key;
        renderTabs();
        renderFooter(track);
    }

    // The board again, at once (a new tab) or every refreshMs while open.
    function ask(now = false) {
        const time = Date.now();
        if (!now && time - asked < refreshMs) return;
        asked = time;
        run('touch-rewards:open:' + chosen);
    }

    function poll(force = false) {
        let next;
        try {
            const text = getHost()?.TouchRewards?.() || '';
            next = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!next) return;
        state = next;
        if (state.requests !== requests) {
            const opened = requests >= 0 && state.requests > requests;
            requests = state.requests;
            if (opened && !dialog.open) {
                help = false;
                listKey = '';
                asked = Date.now();
                dialog.showModal();
            }
        }
        if (!dialog.open) return;
        ask();
        render(force);
    }

    dialog.querySelector('[data-touch-rewards-close]')?.addEventListener('click', () => dialog.close());
    helpButton?.addEventListener('click', () => {
        help = !help;
        poll(true);
    });
    dialog.addEventListener('keydown', event => event.stopPropagation());

    return {
        poll: () => poll(),
        close() {
            if (dialog.open) dialog.close();
            listKey = '';
        },
        translate() {
            listKey = '';
            dialog.querySelector('[data-touch-rewards-title]')?.replaceChildren(say('title'));
            helpButton?.setAttribute('aria-label', say('help'));
            dialog.querySelector('[data-touch-rewards-close]')?.setAttribute('aria-label', say('close'));
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const rewardsTables = { rewards: labels };
