const words = {
    'pt-BR': {
        hunting: 'Ligado', idle: 'Desligado', 'start-hunt': 'Iniciar', 'pause-hunt': 'Pausar',
        unavailable: 'Este recurso ainda está indisponível no Noxius.'
    },
    en: {
        news: 'News & Support', 'auto-hunt-panel': 'Auto-hunt', 'close-hunt': 'Close',
        hunting: 'On', idle: 'Off', more: 'More', interface: 'Interface',
        calendar: 'Calendar', auction: 'Auction', security: 'Security', drops: 'Recent drops',
        'coin-history': 'Coin history', events: 'Events', eragon: 'Eragon Lair',
        ranking: 'Rankings', rewards: 'Item distributor', 'item-shop': 'Item Shop', 'community-buffs': 'Community buffs',
        character: 'Character', inventory: 'Inventory', skills: 'Skills', quests: 'Quests', party: 'Party',
        lobby: 'Character selection', settings: 'Settings', 'start-hunt': 'Start', 'pause-hunt': 'Pause',
        'hunt-requirement': 'Requires an active hunting quest outside town.',
        unavailable: 'This feature is currently unavailable in Noxius.'
    }
};

export function createWorldHud(root, getHost, getLanguage, getQualityLabel = () => '', getRenderCount = () => null) {
    const part = role => root.querySelector('[data-role="' + role + '"]');
    const hud = part('world-hud');
    if (!hud) return { enable() {}, selectWorld() {}, show() {}, hide() {}, frame() {}, updateQuality() {}, setCanvas() {} };

    const quality = document.createElement('span');
    quality.dataset.role = 'world-quality';
    quality.setAttribute('role', 'status');
    Object.assign(quality.style, { position: 'absolute', left: 'max(8px, env(safe-area-inset-left))',
        bottom: 'max(6px, env(safe-area-inset-bottom))', pointerEvents: 'none', padding: '3px 6px',
        border: '1px solid rgba(188,157,93,.45)', borderRadius: '3px', background: 'rgba(14,13,9,.78)',
        color: '#e5cf9d', font: '11px/1.2 var(--sans, sans-serif)', whiteSpace: 'nowrap', textShadow: '0 1px 2px #000' });
    hud.append(quality);
    function updateQuality() {
        quality.textContent = (getLanguage() === 'en' ? 'Quality: ' : 'Qualidade: ') + getQualityLabel();
    }

    const realm = part('world-realm');
    const fps = part('world-fps');
    const ping = part('world-ping');
    const more = part('world-more');
    const huntPanel = part('world-hunt-panel');
    const huntStatus = part('world-hunt-status');
    const huntButtons = root.querySelectorAll('[data-world-action="auto-hunt"]');
    let canvas = null;
    const tools = part('game-tools');
    const utilitySlot = part('world-utility-slot');
    const toolsAnchor = tools ? document.createComment('game-tools') : null;
    if (toolsAnchor) tools.before(toolsAnchor);
    let world = null;
    let frameStart = null;
    let frames = 0;
    let renderStart = null;
    let pingTime = 0;
    let measuringPing = false;
    let active = false;
    let overCanvas = false;

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
        active = false;
        setCanvasPointer(false);
        hud.dataset.state = 'inactive';
        if (toolsAnchor && tools) toolsAnchor.after(tools);
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
        if (action === 'settings') {
            part('settings-open')?.click();
        } else if (action === 'auto-hunt-panel') {
            updateHunt();
            if (huntPanel && !huntPanel.open) huntPanel.showModal();
        } else if (action === 'close-hunt') {
            huntPanel?.close();
        } else {
            if (getHost()?.RunWorldAction(action)) {
                if (action === 'auto-hunt') updateHunt();
                if (!huntPanel?.open && !root.querySelector('[data-community-link]:focus')) {
                    canvas?.focus({ preventScroll: true });
                }
            }
        }
        if (more) more.open = false;
    });

    return {
        enable(host) { host.EnableWorldHud(); },
        selectWorld(selected) { world = selected; },
        updateQuality,
        setCanvas,
        show() {
            if (active) return;
            active = true;
            hud.dataset.state = 'active';
            updateQuality();
            if (utilitySlot && tools) utilitySlot.append(tools);
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
        },
        hide,
        frame(time) {
            if (!active) return;
            const count = getRenderCount();
            const rendered = Number.isFinite(count) && count >= 0 ? count : null;
            const reset = frameStart === null || (rendered === null) !== (renderStart === null) || (rendered !== null && rendered < renderStart);
            if (reset) {
                frameStart = time;
                renderStart = rendered;
                frames = 0;
            }
            if (rendered === null && !reset) frames++;
            if (time - frameStart >= 1000) {
                const count = rendered === null ? frames : rendered - renderStart;
                fps.textContent = Math.round(count * 1000 / (time - frameStart)) + ' FPS';
                frames = 0;
                frameStart = time;
                renderStart = rendered;
                updateHunt();
            }
            if (time - pingTime >= 12000) {
                pingTime = time;
                void measurePing();
            }
        }
    };
}
