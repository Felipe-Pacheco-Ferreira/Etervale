import { makeDraggable } from './draggable.js';
import { createTouchHud, touchOnlyDevice } from './touch-hud.js';

const words = {
    'pt-BR': {
        hunting: 'Ligado', idle: 'Desligado', 'start-hunt': 'Iniciar', 'pause-hunt': 'Pausar',
        unavailable: 'Este recurso ainda está indisponível no Noxius.'
    },
    en: {
        news: 'News & Support', 'auto-hunt-panel': 'Auto-hunt', 'close-hunt': 'Close',
        hunting: 'On', idle: 'Off', more: 'More',
        calendar: 'Calendar', auction: 'Auction', security: 'Security', drops: 'Recent drops',
        'coin-history': 'Coin history', events: 'Events', 'tower-defender': 'Tower Defender', eragon: 'Eragon Lair',
        ranking: 'Rankings', rewards: 'Item distributor', 'item-shop': 'Item Shop', 'community-buffs': 'Community buffs',
        'start-hunt': 'Start', 'pause-hunt': 'Pause',
        'hunt-requirement': 'Requires an active hunting quest outside town.',
        unavailable: 'This feature is currently unavailable in Noxius.'
    }
};

// What each shortcut opens, shown beside it while the pointer is on it.
const tips = {
    'pt-BR': {
        news: 'As novidades do jogo e o suporte: abra e acompanhe seus tickets.',
        'auto-hunt-panel': 'Configure e ligue a caça automática.',
        calendar: 'Os eventos e horários da semana.',
        auction: 'Compre e venda itens entre jogadores.',
        security: 'Proteja sua conta: senha, e-mail e acessos.',
        drops: 'Os itens raros que acabaram de cair no servidor.',
        'coin-history': 'Suas compras e gastos de Coins.',
        events: 'Os eventos ativos e os próximos.',
        'tower-defender': 'O evento de defesa de torres entre duas equipes.',
        eragon: 'Entre no Lar do Eragon.',
        ranking: 'Os melhores jogadores e clãs.',
        rewards: 'Itens enviados para você: resgate-os no distribuidor.',
        'item-shop': 'A loja de itens do Noxius.',
        'community-buffs': 'Veja quanto falta para cada buff global e doe qualquer valor em Coins: a meta cheia ativa o buff para todos.'
    },
    en: {
        news: 'Game news and support: open and follow your tickets.',
        'auto-hunt-panel': 'Set up and start auto-hunt.',
        calendar: 'This week\u2019s events and times.',
        auction: 'Buy and sell items between players.',
        security: 'Protect your account: password, e-mail and sign-ins.',
        drops: 'The rare items that just dropped on the server.',
        'coin-history': 'Your Coin purchases and spending.',
        events: 'The running and upcoming events.',
        'tower-defender': 'The tower defense event between two teams.',
        eragon: 'Enter the Eragon Lair.',
        ranking: 'The best players and clans.',
        rewards: 'Items sent to you: claim them at the distributor.',
        'item-shop': 'The Noxius item shop.',
        'community-buffs': 'See how much each global buff still needs and donate any amount of Coins: a full goal turns the buff on for everyone.'
    }
};

export function createWorldHud(root, getHost, getLanguage, getQualityLabel = () => '', getRenderCount = () => null, interfaceChoice = {}) {
    const part = role => root.querySelector('[data-role="' + role + '"]');
    const hud = part('world-hud');
    if (!hud) return { enable() {}, selectWorld() {}, show() {}, hide() {}, frame() {}, updateQuality() {}, setCanvas() {} };
    const touch = createTouchHud(root, getHost, getLanguage, interfaceChoice);

    const badgeStyle = { position: 'absolute', left: 'max(8px, env(safe-area-inset-left))',
        pointerEvents: 'none', padding: '3px 6px', border: '1px solid rgba(188,157,93,.45)', borderRadius: '3px',
        background: 'rgba(14,13,9,.78)', color: '#e5cf9d', font: '11px/1.2 var(--sans, sans-serif)',
        whiteSpace: 'nowrap', textShadow: '0 1px 2px #000' };
    const quality = document.createElement('span');
    quality.dataset.role = 'world-quality';
    quality.setAttribute('role', 'status');
    Object.assign(quality.style, badgeStyle, { bottom: 'max(6px, env(safe-area-inset-bottom))' });
    hud.append(quality);
    function updateQuality() {
        quality.textContent = (getLanguage() === 'en' ? 'Quality: ' : 'Qualidade: ') + getQualityLabel();
        sceneQuality.textContent = quality.textContent;
        touch.refresh();
    }

    // One box beside the shortcut under the pointer: its name and what it opens.
    const tip = document.createElement('div');
    tip.className = 'world-hud-tip';
    tip.hidden = true;
    const tipTitle = document.createElement('strong');
    const tipText = document.createElement('span');
    tip.append(tipTitle, tipText);
    hud.prepend?.(tip);
    const controls = hud.querySelector?.('.world-hud-controls');
    controls?.addEventListener('pointerover', event => {
        const button = event.target.closest('[data-world-action]');
        const text = button && (tips[getLanguage()] ?? tips['pt-BR'])[button.dataset.worldAction];
        if (!text) return;
        tipTitle.textContent = button.querySelector('[data-world-label]')?.textContent ?? '';
        tipText.textContent = text;
        const rect = button.getBoundingClientRect();
        tip.style.right = (innerWidth - rect.left + 8) + 'px';
        tip.style.top = (rect.top + rect.height / 2) + 'px';
        tip.hidden = false;
    });
    controls?.addEventListener('pointerout', event => {
        if (!event.relatedTarget || !controls.contains(event.relatedTarget) || !event.relatedTarget.closest('[data-world-action]')) tip.hidden = true;
    });

    // The players online, as the game hears them from the server: hidden while unknown or turned off in Settings.
    const online = part('world-online');
    const onlineCount = part('world-online-count');
    function updateOnline() {
        if (!online || !onlineCount) return;
        const players = getHost()?.OnlinePlayers?.() ?? -1;
        online.hidden = !(players >= 0);
        if (players >= 0) onlineCount.textContent = new Intl.NumberFormat(getLanguage()).format(players) + ' online';
    }

    // The mark on News & Support: a news post the player has not seen or a ticket the staff answered.
    const newsButton = controls?.querySelector?.('[data-world-action="news"]');
    function updateNews() {
        if (!newsButton) return;
        const unread = getHost()?.NewsUnread?.() ?? 0;
        if (unread > 0) newsButton.dataset.unread = unread > 9 ? '9+' : String(unread);
        else delete newsButton.dataset.unread;
    }

    const realm = part('world-realm');
    const fps = part('world-fps');
    const characters = part('heroes');
    const sceneFps = document.createElement('span');
    const sceneQuality = document.createElement('span');
    sceneFps.dataset.role = 'scene-fps';
    sceneQuality.dataset.role = 'scene-quality';
    for (const badge of [sceneFps, sceneQuality]) {
        badge.hidden = true;
        Object.assign(badge.style, badgeStyle, { zIndex: '3', maxWidth: 'calc(100% - 16px)',
            overflow: 'hidden', textOverflow: 'ellipsis' });
    }
    sceneFps.style.top = 'max(8px, env(safe-area-inset-top))';
    sceneFps.style.fontVariantNumeric = 'tabular-nums';
    sceneQuality.style.bottom = 'max(54px, calc(env(safe-area-inset-bottom) + 48px))';
    root.append(sceneFps, sceneQuality);
    const ping = part('world-ping');
    const more = part('world-more');
    const huntPanel = part('world-hunt-panel');
    makeDraggable(huntPanel, huntPanel?.querySelector('.world-hunt-head'));
    const huntStatus = part('world-hunt-status');
    const huntButtons = root.querySelectorAll('[data-world-action="auto-hunt"]');
    let canvas = null;
    let world = null;
    let frameStart = null;
    let frames = 0;
    let renderStart = null;
    let pingTime = 0;
    let measuringPing = false;
    let active = false;
    let overCanvas = false;
    let sampleScope = null;

    function setCanvasPointer(inside) {
        if (inside === overCanvas) return;
        overCanvas = inside;
        getHost()?.SetWorldCanvasPointer(inside);
    }

    const canvasMove = () => {
        if (active) setCanvasPointer(true);
    };
    const canvasLeave = () => setCanvasPointer(false);
    function setCanvas(value) {
        if (value === canvas) return;
        canvas?.removeEventListener('pointermove', canvasMove);
        canvas?.removeEventListener('pointerleave', canvasLeave);
        setCanvasPointer(false);
        canvas = value;
        canvas?.addEventListener('pointermove', canvasMove);
        canvas?.addEventListener('pointerleave', canvasLeave);
        touch.setCanvas(canvas);
    }
    setCanvas(root.querySelector('canvas'));

    function updateHunt() {
        const running = getHost()?.WorldAutoHuntActive() ?? false;
        const labels = words[getLanguage()] ?? words['pt-BR'];
        for (const button of huntButtons) {
            button.setAttribute('aria-pressed', String(running));
            button.setAttribute('aria-label', labels[running ? 'pause-hunt' : 'start-hunt']);
        }
        if (huntStatus) huntStatus.textContent = running ? labels.hunting : labels.idle;
    }

    function showPing(value) {
        ping.textContent = Number.isFinite(value) ? value + ' ms' : '';
    }

    async function measurePing() {
        const selected = world;
        if (!selected?.status || measuringPing) return;
        measuringPing = true;
        const started = performance.now();
        try {
            const response = await fetch(selected.status, { cache: 'no-store', mode: 'cors' });
            await response.json();
            if (response.ok && active && selected === world) {
                selected.ping = Math.round(performance.now() - started);
                showPing(selected.ping);
            }
        } catch {
            // Keep the last valid sample while status is temporarily unavailable.
        } finally {
            measuringPing = false;
        }
    }

    function hide() {
        touch.hide();
        tip.hidden = true;
        if (online) online.hidden = true;
        active = false;
        setCanvasPointer(false);
        hud.dataset.state = 'inactive';
        if (more) more.open = false;
        if (huntPanel?.open) huntPanel.close();
    }

    root.addEventListener('click', event => {
        if (!active) return;
        const trigger = event.target.closest('[data-world-action]');
        if (!trigger) {
            if (more?.open && !more.contains(event.target)) more.open = false;
            return;
        }
        if (trigger.disabled) return;
        const action = trigger.dataset.worldAction;
        if (action === 'auto-hunt-panel') {
            updateHunt();
            if (huntPanel && !huntPanel.open) huntPanel.showModal();
        } else if (action === 'close-hunt') {
            huntPanel?.close();
        } else {
            if (getHost()?.RunWorldAction(action)) {
                if (action === 'auto-hunt') updateHunt();
                if (!huntPanel?.open) {
                    canvas?.focus({ preventScroll: true });
                }
            }
        }
        if (more) more.open = false;
    });

    return {
        enable(host) {
            host.EnableWorldHud();
            host.SetTouchDevice?.(touchOnlyDevice());
        },
        selectWorld(selected) { world = selected; },
        updateQuality,
        setCanvas,
        show() {
            if (active) return;
            active = true;
            sceneFps.hidden = true;
            sceneQuality.hidden = true;
            hud.dataset.state = 'active';
            updateQuality();
            realm.textContent = world ? world.name + ' (' + world.region + ')' : '';
            fps.textContent = '';
            showPing(world?.ping);
            frameStart = null;
            frames = 0;
            renderStart = null;
            pingTime = 0;
            const actions = new Set((getHost()?.WorldActions() ?? '').split(','));
            const labels = words[getLanguage()] ?? words['pt-BR'];
            for (const button of hud.querySelectorAll('[data-world-optional]')) {
                button.hidden = false;
                button.disabled = !actions.has(button.dataset.worldAction);
                button.title = button.disabled ? labels.unavailable : '';
            }
            for (const label of root.querySelectorAll('[data-world-label]')) {
                const phrase = labels[label.dataset.worldLabel];
                if (phrase) label.textContent = phrase;
            }
            updateHunt();
            touch.show();
        },
        hide,
        frame(time) {
            const sceneVisible = !active && characters && !characters.hidden && root.dataset.worldLoading !== 'true';
            const scope = active ? 'world' : sceneVisible ? characters.dataset.screen ?? 'characters' : null;
            if (scope !== sampleScope) {
                sampleScope = scope;
                frameStart = null;
                frames = 0;
                renderStart = null;
                sceneFps.textContent = '— FPS';
                if (sceneVisible) updateQuality();
            }
            const badgesHidden = !sceneVisible;
            if (sceneFps.hidden !== badgesHidden) sceneFps.hidden = badgesHidden;
            if (sceneQuality.hidden !== badgesHidden) sceneQuality.hidden = badgesHidden;
            if (!active && !sceneVisible) return;
            if (active) touch.frame(time);
            const count = getRenderCount();
            const rendered = Number.isFinite(count) && count >= 0 ? count : null;
            const reset = frameStart === null || (rendered === null) !== (renderStart === null) || (rendered !== null && rendered < renderStart);
            if (reset) {
                frameStart = time;
                renderStart = rendered;
                frames = 0;
                if (sceneVisible) sceneFps.textContent = '— FPS';
            }
            if (rendered === null && !reset && active) frames++;
            if (time - frameStart >= 1000) {
                const count = rendered === null ? frames : rendered - renderStart;
                const label = Math.round(count * 1000 / (time - frameStart)) + ' FPS';
                if (active) fps.textContent = label;
                else sceneFps.textContent = rendered === null ? '— FPS' : label;
                frames = 0;
                frameStart = time;
                renderStart = rendered;
                if (active) {
                    updateHunt();
                    updateOnline();
                    updateNews();
                }
            }
            if (active && time - pingTime >= 12000) {
                pingTime = time;
                void measurePing();
            }
        }
    };
}
