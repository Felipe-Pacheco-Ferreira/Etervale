import * as art from './hud-art.js';
import { table } from './page-language.js';
import { element, format, section } from './touch-dom.js';
// The touch HUD's party, the Android client's: a row of buttons under a reached player's target frame (trade, the
// highlighted party invite, friend, clan for a recruiter, duel), the other members' plates under the player frame (a
// crown before the leader's name, the level, the life gauge) with the party's commands on a tap, and the plate of a
// party, clan, trade or duel request with Recusar and Aceitar. The game hands out what shows (BrowserHost.TouchParty)
// and takes the answers (touch-party:<action>:<value>[:<member>]).

const labels = {
    'pt-BR': { Trade: 'Trocar', Party: 'Convidar p/ grupo', Friend: 'Amigo', Clan: 'Clã', Duel: 'Duelo', Delegate: 'Passar liderança',
        Kick: 'Expulsar do grupo', Disband: 'Desfazer o grupo', Leave: 'Sair do grupo', group: 'Grupo', level: 'Nível', short: 'Nv',
        'request-Party': 'Convite de grupo', 'request-Clan': 'Convite de clã', 'request-Trade': 'Pedido de troca',
        'request-Duel': 'Desafio de duelo', trade: 'Quer negociar com você.', accept: 'Aceitar', decline: 'Recusar', close: 'Fechar',
        member: '{0}, nível {1}' },
    en: { Trade: 'Trade', Party: 'Invite to party', Friend: 'Friend', Clan: 'Clan', Duel: 'Duel', Delegate: 'Pass the lead',
        Kick: 'Remove from party', Disband: 'End the party', Leave: 'Leave the party', group: 'Party', level: 'Level', short: 'Lv',
        'request-Party': 'Party invitation', 'request-Clan': 'Clan invitation', 'request-Trade': 'Trade request',
        'request-Duel': 'Duel challenge', trade: 'Wants to trade with you.', accept: 'Accept', decline: 'Decline', close: 'Close',
        member: '{0}, level {1}' }
};

// A member's plate, in CSS pixels: the Android client's 170 by 40.
const plateWidth = 170;
const plateHeight = 40;

// The buttons under the target frame, in the game's order, the party invite highlighted.
export function playerButtons(state, say) {
    return (state.buttons ?? []).map(name => ({ label: say(name), action: 'touch-party:player:' + name, prominent: name === 'Party' }));
}

// A member's commands: the leader's (the lead, removing the member, ending the party) then leaving, highlighted.
export function memberCommands(state, member, say) {
    const commands = state.leads ? ['Delegate', 'Kick', 'Disband'] : [];
    commands.push('Leave');
    return commands.map(name => ({ label: say(name), action: 'touch-party:command:' + name + ':' + (name === 'Delegate' || name === 'Kick' ? member.id : '0'),
        prominent: name === 'Leave' }));
}

// The request plate's heading and line: the game's line, or the page's for a trade, which has none.
export function requestText(request, say) {
    return { heading: say('request-' + request.kind), line: request.line || (request.kind === 'Trade' ? say('trade') : '') };
}

export function createTouchParty(layer, getHost, getLanguage) {
    const row = layer.querySelector('[data-role="touch-player-buttons"]');
    const members = layer.querySelector('[data-role="touch-party"]');
    const commands = layer.querySelector('[data-role="touch-party-commands"]');
    const request = layer.querySelector('[data-role="touch-request"]');
    const targetCanvas = layer.querySelector('[data-role="touch-target"]');
    if (!row || !members || !commands || !request) return { poll() {}, close() {}, translate() {} };
    let state = null;
    let shownKey = '';
    let chosen = '';

    const say = key => table(labels, 'party', getLanguage())[key] ?? labels.en[key];
    const run = action => getHost()?.RunWorldAction?.(action);

    function button(entry, className, after) {
        const node = element('button', className, entry.label);
        node.type = 'button';
        if (entry.prominent) node.dataset.on = 'true';
        node.addEventListener('click', () => {
            run(entry.action);
            after?.();
            poll(true);
        });
        return node;
    }

    function renderRow() {
        const entries = playerButtons(state, say);
        row.hidden = entries.length === 0;
        if (row.hidden) return;
        // Under the target frame, which touch-hud.js sizes from the touch state.
        if (targetCanvas && !targetCanvas.hidden) row.style.top = targetCanvas.offsetTop + targetCanvas.offsetHeight + 'px';
        row.replaceChildren(...entries.map(entry => button(entry, 'touch-bag-action touch-player-button')));
    }

    function drawPlate(canvas, member) {
        const ratio = globalThis.devicePixelRatio || 1;
        canvas.width = Math.round(plateWidth * ratio);
        canvas.height = Math.round(plateHeight * ratio);
        const context = canvas.getContext('2d');
        if (!context) return;
        const w = canvas.width;
        const h = canvas.height;
        context.clearRect(0, 0, w, h);
        art.plate(context, 0, 0, w, h, h * 0.22);
        const name = (member.leader ? '♛ ' : '') + member.name + '  ' + say('short') + ' ' + member.level;
        art.label(context, name, h * 0.2, h * 0.3, h * 0.28, art.edgeLight, 'left');
        const share = member.maxLife > 0 ? Math.max(0, Math.min(1, member.life / member.maxLife)) : 0;
        const text = member.maxLife > 0 ? member.life + ' / ' + member.maxLife : '';
        art.bar(context, h * 0.2, h * 0.55, w - h * 0.2, h * 0.85, art.gauges.life, share, share, text, h * 0.22);
    }

    function renderMembers() {
        members.hidden = state.members.length === 0;
        while (members.children.length > state.members.length) members.lastChild.remove();
        while (members.children.length < state.members.length) {
            const plate = element('button', 'touch-party-member');
            plate.type = 'button';
            plate.append(element('canvas'));
            plate.addEventListener('click', () => {
                chosen = plate.dataset.member;
                renderCommands();
            });
            members.append(plate);
        }
        state.members.forEach((member, index) => {
            const plate = members.children[index];
            plate.dataset.member = member.id;
            plate.setAttribute('aria-label', format(say('member'), member.name, member.level));
            drawPlate(plate.firstChild, member);
        });
    }

    // Open while its member stays in the party; the commands follow who leads.
    function renderCommands() {
        const member = state?.members.find(entry => entry.id === chosen);
        commands.hidden = !member;
        if (!member) {
            chosen = '';
            return;
        }
        const close = () => {
            chosen = '';
            commands.hidden = true;
        };
        const actions = element('div', 'touch-party-actions');
        actions.append(...memberCommands(state, member, say).map(entry => button(entry, 'touch-bag-action touch-party-button', close)));
        const closer = element('button', 'touch-chat-close', '×');
        closer.type = 'button';
        closer.setAttribute('aria-label', say('close'));
        closer.addEventListener('click', close);
        const heading = element('div', 'touch-party-heading');
        heading.append(section(say('group')), closer);
        commands.replaceChildren(heading, element('p', 'touch-party-line', member.name + '  ·  ' + say('level') + ' ' + member.level), actions);
    }

    function renderRequest() {
        const asked = state.request;
        request.hidden = !asked;
        if (!asked) return;
        const text = requestText(asked, say);
        const actions = element('div', 'touch-party-actions');
        actions.append(button({ label: say('decline'), action: 'touch-party:no:' + asked.kind }, 'touch-bag-action touch-party-button'),
            button({ label: say('accept'), action: 'touch-party:yes:' + asked.kind, prominent: true }, 'touch-bag-action touch-party-button'));
        const nodes = [section(text.heading), element('p', 'touch-party-name', asked.name)];
        if (text.line) nodes.push(element('p', 'touch-party-line', text.line));
        request.replaceChildren(...nodes, actions);
    }

    function poll(force = false) {
        let next;
        try {
            const text = getHost()?.TouchParty?.() || '';
            next = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!next) return;
        const key = JSON.stringify([getLanguage(), next]);
        if (!force && key === shownKey) return;
        shownKey = key;
        state = next;
        renderRow();
        renderMembers();
        renderCommands();
        renderRequest();
    }

    for (const node of [commands, request]) node.addEventListener('keydown', event => event.stopPropagation());

    return {
        poll: () => poll(),
        close() {
            row.hidden = true;
            members.hidden = true;
            commands.hidden = true;
            request.hidden = true;
            members.replaceChildren();
            state = null;
            shownKey = '';
            chosen = '';
        },
        translate() {
            shownKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const partyTables = { party: labels };
