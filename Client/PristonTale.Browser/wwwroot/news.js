import { makeDraggable } from './draggable.js';

// News & Support drawn as the Settings panel: the news come from the site's feed; the game keeps the tickets'
// conversation with the server (BrowserHost.NewsState, NewsAction) and the community links, and opens the window.
const say = (pt, en) => ({ 'pt-BR': pt, en });
const texts = {
    title: say('Novidades & Suporte', 'News & Support'),
    news: say('Novidades', 'News'),
    tickets: say('Meus tickets', 'My tickets'),
    newTicket: say('Novo ticket', 'New ticket'),
    send: say('Enviar ticket', 'Send ticket'),
    save: say('Salvar alterações', 'Save changes'),
    cancelEdit: say('Cancelar', 'Cancel'),
    cancel: say('Cancelar', 'Cancel'),
    open: say('Abrir', 'Open'),
    openLink: say('Abrir link?', 'Open link?'),
    loading: say('Carregando…', 'Loading…'),
    unavailable: say('Feed de notícias indisponível.', 'News feed unavailable.'),
    empty: say('Sem notícias.', 'No news.'),
    noTickets: say('Você ainda não abriu nenhum ticket.', 'You have not opened any ticket yet.'),
    refresh: say('Atualizar', 'Refresh'),
    create: say('+ Novo ticket', '+ New ticket'),
    back: say('‹ Voltar', '‹ Back'),
    edit: say('Editar', 'Edit'),
    cancelTicket: say('Cancelar ticket', 'Cancel ticket'),
    reply: say('Responder', 'Reply'),
    replyPlaceholder: say('Escreva sua resposta…', 'Write your reply…'),
    sendReply: say('Enviar', 'Send'),
    subject: say('Título', 'Subject'),
    description: say('Descrição', 'Description'),
    attachments: say('Anexos (opcional)', 'Attachments (optional)'),
    attach: say('Anexar imagem ou vídeo', 'Attach image or video'),
    attachTypes: say('PNG, JPEG, WebP, MP4 ou WebM, até 20 MB.', 'PNG, JPEG, WebP, MP4 or WebM, up to 20 MB.'),
    attachOpen: say('Anexar arquivo', 'Attach file'),
    attachRemove: say('Remover', 'Remove'),
    attaching: say('Enviando anexo…', 'Uploading attachment…'),
    attached: say('Anexo enviado.', 'Attachment uploaded.'),
    attachFailed: say('Não foi possível enviar o anexo (um por ticket, até 20 MB).', 'The attachment was not uploaded (one per ticket, up to 20 MB).'),
    waiting: say('Aguardando servidor…', 'Waiting for server…'),
    invalid: say('Preencha título e descrição.', 'Fill in subject and description.'),
    error: say('Suporte indisponível.', 'Support unavailable.'),
    saved: say('Ticket enviado.', 'Ticket sent.'),
    replied: say('Resposta enviada.', 'Reply sent.'),
    edited: say('Ticket alterado.', 'Ticket changed.'),
    closed: say('Ticket cancelado.', 'Ticket cancelled.'),
    team: say('Equipe Noxius', 'Noxius Team')
};
// The site's emblem, beside the team's signature at the end of every post.
const teamLogo = '/img/emblem-256.png';
const kinds = { News: say('Novidade', 'News'), Event: say('Evento', 'Event'), Improvement: say('Melhoria', 'Improvement') };
const statuses = {
    Open: say('Aberto', 'Open'), Answered: say('Respondido', 'Answered'), Closed: say('Fechado', 'Closed'),
    Cancelled: say('Cancelado', 'Cancelled')
};
// The server's category keys, in the order of the game's form.
const categories = [
    { key: 'Bug', icon: '🐞', label: say('Reportar bug', 'Report bug'), badge: say('Bug', 'Bug'),
        subject: say('Ex.: Fico preso na ponte de Ricarten', 'E.g.: I get stuck on the Ricarten bridge'),
        description: say('O que aconteceu? O que você estava fazendo? Acontece sempre?', 'What happened? What were you doing? Does it always happen?') },
    { key: 'Melhoria', icon: '💡', label: say('Pedir melhoria', 'Suggest improvement'), badge: say('Melhoria', 'Improvement'),
        subject: say('Ex.: Mostrar o tempo dos buffs', 'E.g.: Show buff timers'),
        description: say('O que você gostaria que mudasse? Por quê?', 'What would you like to change? Why?') },
    { key: 'Outro', icon: '💬', label: say('Outro assunto', 'Other'), badge: say('Outro', 'Other'),
        subject: say('Ex.: Dúvida sobre a minha conta', 'E.g.: A question about my account'),
        description: say('Conte para a equipe como podemos ajudar.', 'Tell the team how we can help.') }
];
const questions = {
    discord: say('Abrir o convite do Discord da comunidade em uma nova aba?', 'Open the community’s Discord invite in a new tab?'),
    instagram: say('Abrir o Instagram da comunidade em uma nova aba?', 'Open the community’s Instagram in a new tab?'),
    x: say('Abrir o X da comunidade em uma nova aba?', 'Open the community’s X in a new tab?')
};
const tabActions = { news: 'news', tickets: 'list', new: 'new' };
const attachmentTypes = '.png,.jpg,.jpeg,.webp,.mp4,.webm';
const attachmentBytes = 20 * 1024 * 1024;
const descriptionLimit = 1500;
const subjectLimit = 55;
const refreshMs = 500;

function element(tag, className, content) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (content !== undefined) node.textContent = content;
    return node;
}

// A line of a post with its **bold** runs.
export function richText(node, line) {
    line.split('**').forEach((part, index) => {
        if (!part) return;
        node.append(index % 2 ? element('strong', '', part) : document.createTextNode(part));
    });
    return node;
}

// The day as players read it: 2026-11-18 becomes 18/11/2026.
export function newsDay(date) {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date ?? '');
    return match ? `${match[3]}/${match[2]}/${match[1]}` : date ?? '';
}

// The address as players read it: no scheme and no closing slash.
export function shownAddress(address) {
    return String(address).replace(/^[a-z]+:\/\//i, '').replace(/\/$/, '');
}

export function createNews(root, getHost, getLanguage) {
    const dialog = root.querySelector('[data-role="news"]');
    if (!dialog) return { open() {} };
    const part = role => dialog.querySelector(`[data-role="${role}"]`);
    const body = part('news-body');
    const foot = part('news-foot');
    const note = part('news-note');
    const send = part('news-send');
    const formCancel = part('news-form-cancel');
    const confirm = part('news-confirm');
    const word = phrase => phrase?.[getLanguage()] ?? phrase?.['pt-BR'] ?? '';
    let tab = 'news';
    let state = null;
    let feed = null;
    let timer = 0;
    let asked = '';
    let shown = '';
    const form = { category: 0, subject: '', description: '', file: null, editing: '' };
    let upload = '';
    // A ticket sent from the form: the game opens it (its tab 1) once the server keeps it.
    let sent = false;
    // One file picker for the form and an open ticket; the browser opens it on the button's click.
    const picker = element('input');
    picker.type = 'file';
    picker.accept = attachmentTypes;
    picker.hidden = true;
    dialog.append(picker);
    let picked = null;
    picker.addEventListener('cancel', event => event.stopPropagation());
    picker.addEventListener('change', () => {
        const file = picker.files?.[0] ?? null;
        picker.value = '';
        if (file && picked) picked(file);
    });
    makeDraggable(dialog, dialog.querySelector('.settings-head'));

    function pick(onFile) {
        picked = onFile;
        picker.click();
    }

    async function attach(ticket, file) {
        if (!ticket || !file) return;
        if (file.size > attachmentBytes) {
            upload = 'attachFailed';
            render();
            return;
        }
        upload = 'attaching';
        render();
        const body = new FormData();
        body.append('arquivo', file, file.name);
        try {
            const response = await fetch('/api/play-session/attachment/' + ticket, { method: 'POST', body, credentials: 'same-origin' });
            upload = response.ok ? 'attached' : 'attachFailed';
        } catch {
            upload = 'attachFailed';
        }
        render();
    }

    function host() {
        return getHost();
    }

    function readState() {
        try {
            return JSON.parse(host()?.NewsState() || 'null');
        } catch {
            return null;
        }
    }

    function act(action, id = '', subject = '', category = 0, text = '') {
        const done = host()?.NewsAction(action, id, subject, category, text) ?? false;
        refresh();
        return done;
    }

    function translate() {
        for (const node of dialog.querySelectorAll('[data-news-text]')) {
            node.textContent = word(texts[node.dataset.newsText]);
        }
    }

    async function loadFeed() {
        feed = null;
        render();
        try {
            const response = await fetch('/api/client/news', { cache: 'no-store' });
            feed = response.ok ? await response.json() : [];
            if (!response.ok) feed = 'error';
        } catch {
            feed = 'error';
        }
        if (tab === 'news') render();
    }

    function renderNews() {
        if (feed === null) return [element('p', 'news-status', word(texts.loading))];
        if (feed === 'error') return [element('p', 'news-status', word(texts.unavailable))];
        if (!feed.length) return [element('p', 'news-status', word(texts.empty))];
        return feed.map(post => {
            const kind = kinds[post.kind] ? post.kind : 'News';
            const article = element('article', 'news-post');
            article.dataset.kind = kind;
            const head = element('header', 'news-post-head');
            head.append(element('span', 'news-badge', word(kinds[kind])), element('h3', 'news-post-title', post.title));
            article.append(head, element('p', 'news-stamp', post.author ? `${newsDay(post.date)} · ${post.author}` : newsDay(post.date)));
            let list = null;
            for (const line of [post.summary, ...(post.body ?? [])]) {
                if (!line?.trim()) {
                    list = null;
                    continue;
                }
                if (line.startsWith('- ')) {
                    list ??= article.appendChild(element('ul'));
                    list.append(richText(element('li'), line.slice(2)));
                    continue;
                }
                list = null;
                article.append(richText(element('p', 'news-line'), line));
            }
            article.append(postFoot());
            return article;
        });
    }

    // The end of a post, small in its corner: the team's signature and emblem, and the community's Discord.
    function postFoot() {
        const foot = element('footer', 'news-post-foot');
        const logo = element('img', 'news-post-logo');
        logo.src = teamLogo;
        logo.alt = '';
        foot.append(element('span', '', word(texts.team)), logo);
        if (state?.discord) foot.append(button('news-post-link', 'Discord', () => ask('discord')));
        return foot;
    }

    function button(className, label, onClick) {
        const node = element('button', className, label);
        node.type = 'button';
        node.addEventListener('click', onClick);
        return node;
    }

    function renderList() {
        const tools = element('div', 'news-list-tools');
        tools.append(button('settings-cancel', word(texts.refresh), () => act('list')),
            button('settings-save', word(texts.create), () => select('new')));
        const nodes = [tools];
        if (!state?.list?.length) nodes.push(element('p', 'news-status', state?.state === 'waiting' ? word(texts.waiting) : word(texts.noTickets)));
        for (const item of state?.list ?? []) {
            nodes.push(ticketCard(item, () => act('open', item.id)));
        }
        return nodes;
    }

    // A ticket as the list shows it: its category's icon, number and badge, the subject, the status and the dates.
    function ticketCard(item, onClick) {
        const category = categories.find(entry => entry.key === item.category);
        const card = onClick ? button('news-ticket', '', onClick) : element('div', 'news-ticket');
        const head = element('span', 'news-ticket-head');
        head.append(element('span', 'news-ticket-icon', category?.icon ?? '💬'), element('span', 'news-ticket-number', item.number));
        if (category) {
            const badge = element('span', 'news-ticket-category', word(category.badge));
            badge.dataset.category = category.key;
            head.append(badge);
        }
        head.append(element('span', 'news-ticket-subject', item.subject));
        const status = element('span', 'news-ticket-status', word(statuses[item.status]) || item.status);
        status.dataset.status = item.status;
        card.append(head, status, element('span', 'news-ticket-details', item.details ?? item.opened));
        return card;
    }

    function renderDetail(detail) {
        const actions = element('div', 'news-detail-actions');
        actions.append(button('settings-cancel', word(texts.back), () => act('list')));
        if (detail.editable) {
            actions.append(button('settings-cancel', word(texts.edit), () => startEdit(detail)),
                button('settings-cancel', word(texts.cancelTicket), () => act('cancel', detail.id)));
        }
        if (detail.status !== 'Closed' && detail.status !== 'Cancelled') {
            actions.append(button('news-attach', '📎 ' + word(texts.attachOpen), () => pick(file => attach(detail.id, file))));
        }
        const nodes = [actions, ticketCard(detail, null)];
        for (const message of detail.messages) {
            const bubble = element('div', 'news-message');
            bubble.dataset.staff = String(message.staff);
            bubble.append(element('span', 'news-message-header', message.header), document.createTextNode(message.text));
            nodes.push(bubble);
        }
        if (detail.status !== 'Closed' && detail.status !== 'Cancelled') {
            const reply = element('div', 'news-form');
            const field = element('textarea');
            field.placeholder = word(texts.replyPlaceholder);
            field.maxLength = descriptionLimit;
            reply.append(element('label', '', word(texts.reply)), field,
                button('settings-save', word(texts.sendReply), () => { if (field.value.trim()) act('reply', detail.id, '', 0, field.value); }));
            nodes.push(reply);
        }
        return nodes;
    }

    function startEdit(detail) {
        const match = /^\[([^\]]+)\]\s*(.*)$/.exec(detail.subject);
        const index = categories.findIndex(category => category.key === (match?.[1] ?? detail.category));
        form.category = index >= 0 ? index : 0;
        form.subject = match ? match[2] : detail.subject;
        form.description = detail.messages[0]?.text ?? '';
        form.editing = detail.id;
        act('edit', detail.id);
        select('new', false);
    }

    function renderForm() {
        const kinds = element('div', 'news-categories');
        categories.forEach((category, index) => {
            const choice = button('news-category', '', () => { form.category = index; render(); });
            choice.setAttribute('aria-pressed', String(form.category === index));
            choice.append(element('span', 'news-category-icon', category.icon), element('span', '', word(category.label)));
            kinds.append(choice);
        });
        const box = element('div', 'news-form');
        const subject = element('input');
        subject.maxLength = subjectLimit;
        subject.placeholder = word(categories[form.category].subject);
        subject.value = form.subject;
        subject.addEventListener('input', () => { form.subject = subject.value; });
        const description = element('textarea');
        description.maxLength = descriptionLimit;
        description.placeholder = word(categories[form.category].description);
        description.value = form.description;
        const count = element('span', 'news-count', `${form.description.length}/${descriptionLimit}`);
        description.addEventListener('input', () => {
            form.description = description.value;
            count.textContent = `${form.description.length}/${descriptionLimit}`;
        });
        box.append(element('label', '', word(texts.subject)), subject, element('label', '', word(texts.description)), description, count);
        if (!form.editing) {
            const row = element('div', 'news-attach-row');
            row.append(button('news-attach', '📎 ' + word(texts.attach), () => pick(file => { form.file = file; render(); })));
            if (form.file) {
                row.append(element('span', 'news-attach-name', form.file.name),
                    button('news-attach-remove', word(texts.attachRemove), () => { form.file = null; render(); }));
            }
            box.append(element('label', '', word(texts.attachments)), row, element('span', 'news-attach-types', word(texts.attachTypes)));
        }
        return [kinds, box];
    }

    function render() {
        for (const node of dialog.querySelectorAll('[data-news-tab]')) {
            node.setAttribute('aria-selected', String(node.dataset.newsTab === tab));
        }
        const view = tab === 'news' ? 'news' : tab === 'new' ? 'new' : state?.detail ? 'detail:' + state.detail.id : 'list';
        const keepScroll = view === shown;
        const scroll = body.scrollTop;
        const nodes = tab === 'news' ? renderNews() : tab === 'new' ? renderForm() : state?.detail ? renderDetail(state.detail) : renderList();
        if (tab !== 'new' || !keepScroll || !body.childElementCount) {
            body.replaceChildren(...nodes);
            body.scrollTop = keepScroll ? scroll : 0;
        }
        shown = view;
        foot.hidden = tab !== 'new';
        send.textContent = word(form.editing ? texts.save : texts.send);
        formCancel.hidden = !form.editing;
        send.disabled = state?.state === 'waiting';
        note.textContent = word(texts[upload]) || word(texts[state?.state]) || '';
    }

    function refresh() {
        const next = readState();
        if (!next) return;
        const changed = JSON.stringify(next) !== JSON.stringify(state);
        state = next;
        for (const network of ['discord', 'instagram', 'x']) {
            const link = dialog.querySelector(`[data-news-link="${network}"]`);
            if (link) link.hidden = !state[network];
        }
        if (sent && state.tab === 1 && state.open) {
            sent = false;
            const file = form.editing ? null : form.file;
            Object.assign(form, { subject: '', description: '', file: null, editing: '' });
            tab = 'tickets';
            if (file) attach(state.open, file);
            render();
        } else if (sent && (state.state === 'invalid' || state.state === 'error')) {
            sent = false;
        }
        if (changed && tab !== 'news') render();
        else if (changed) note.textContent = '';
    }

    function select(next, reset = true) {
        if (reset && next === 'new' && form.editing) Object.assign(form, { subject: '', description: '', editing: '' });
        tab = next;
        if (reset) host()?.NewsAction(tabActions[next], '', '', 0, '');
        refresh();
        render();
    }

    function ask(network) {
        if (!state?.[network]) return;
        asked = network;
        part('news-confirm-question').textContent = word(questions[network]);
        part('news-confirm-address').textContent = shownAddress(state[network]);
        confirm.hidden = false;
        part('news-confirm-open').focus();
    }

    function answer(open) {
        if (open && asked && state?.[asked]) {
            const opened = window.open(state[asked], '_blank');
            if (opened) opened.opener = null;
        }
        asked = '';
        confirm.hidden = true;
    }

    function close() {
        clearInterval(timer);
        timer = 0;
        answer(false);
        if (dialog.open) dialog.close();
        host()?.NewsClosed();
    }

    for (const node of dialog.querySelectorAll('[data-news-tab]')) {
        node.addEventListener('click', () => select(node.dataset.newsTab));
    }
    for (const node of dialog.querySelectorAll('[data-news-link]')) {
        node.addEventListener('click', () => ask(node.dataset.newsLink));
    }
    part('news-close').addEventListener('click', close);
    part('news-confirm-cancel').addEventListener('click', () => answer(false));
    part('news-confirm-open').addEventListener('click', () => answer(true));
    formCancel.addEventListener('click', () => {
        const id = form.editing;
        Object.assign(form, { subject: '', description: '', editing: '' });
        tab = 'tickets';
        act('open', id);
        render();
    });
    send.addEventListener('click', () => {
        upload = '';
        sent = act('send', '', form.subject, form.category, form.description);
        render();
    });
    dialog.addEventListener('cancel', event => {
        // A file picker closed without a file sends its own cancel, which bubbles here: only Esc on the window closes it.
        if (event.target !== dialog) return;
        event.preventDefault();
        if (!confirm.hidden) answer(false);
        else close();
    });
    dialog.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !confirm.hidden) {
            event.preventDefault();
            answer(true);
        }
        // The game's keys stay out while the window has the keyboard.
        event.stopPropagation();
    });

    return {
        open() {
            translate();
            tab = 'news';
            Object.assign(form, { category: 0, subject: '', description: '', file: null, editing: '' });
            upload = '';
            refresh();
            if (!dialog.open) dialog.showModal();
            render();
            loadFeed();
            clearInterval(timer);
            timer = setInterval(refresh, refreshMs);
        }
    };
}
