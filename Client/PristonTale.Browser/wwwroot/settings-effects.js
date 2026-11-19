// What the saved settings do on the page rather than in the game: the window mode (the browser's full screen), the
// prompt before leaving the game, the grey screen while dead, the low-life warning, the death notification, the corner
// shortcuts (hideHudIcons) and the page's HUD while F12 hides the game's. The game reports the local player's life
// through BrowserHost.PlayerLife: -1 outside the world, 0 dead, else the life fraction.

export const lowLifeFraction = 0.3;
const pollMilliseconds = 250;

// The page state each life reading leads to: the overlays shown and whether a death just happened.
export function lifeEffects(settings, life, wasDead) {
    const inWorld = life >= 0;
    const dead = inWorld && life === 0;
    return {
        grey: dead && settings.grayscaleOnDeath !== false,
        lowLife: inWorld && !dead && life < lowLifeFraction && settings.lowHealthWarning !== false,
        died: dead && !wasDead,
        dead
    };
}

export function wantsFullScreen(settings) {
    return settings.windowMode === 'Fullscreen';
}

export function createSettingsEffects(root, getHost, getSettings, deathText, view = globalThis) {
    const document = view.document;
    let wasDead = false;
    let started = false;
    let timer = 0;
    const warning = document.createElement('div');
    warning.setAttribute('aria-hidden', 'true');
    Object.assign(warning.style, {
        position: 'fixed', inset: '0', pointerEvents: 'none', zIndex: '5', display: 'none',
        boxShadow: 'inset 0 0 160px 40px rgba(190, 20, 20, 0.65)', animation: 'none'
    });
    root.append?.(warning);

    function canvas() {
        return root.querySelector('canvas');
    }

    function life() {
        try {
            return getHost()?.PlayerLife?.() ?? -1;
        } catch {
            return -1;
        }
    }

    function hudHidden() {
        try {
            return getHost()?.HudHidden?.() === true;
        } catch {
            return false;
        }
    }

    function tick() {
        const settings = getSettings();
        const effects = lifeEffects(settings, life(), wasDead);
        wasDead = effects.dead;
        const target = canvas();
        if (target) target.style.filter = effects.grey ? 'grayscale(1)' : '';
        const shortcuts = root.querySelector('.world-hud-controls');
        if (shortcuts) shortcuts.style.display = settings.hideHudIcons === true ? 'none' : '';
        const hud = root.querySelector('[data-role="world-hud"]');
        if (hud) hud.style.visibility = hudHidden() ? 'hidden' : '';
        // The warning pulses by its opacity, a second a beat.
        warning.style.display = effects.lowLife ? 'block' : 'none';
        if (effects.lowLife) warning.style.opacity = String(0.55 + 0.45 * Math.abs(Math.sin(Date.now() / 1000 * Math.PI)));
        if (effects.died && settings.notifyOnDeath === true && document.hidden && view.Notification?.permission === 'granted') {
            try {
                new view.Notification(deathText());
            } catch {
                // A browser that only notifies from a service worker shows nothing.
            }
        }
    }

    // F12 belongs to the game in the world (hide the HUD): the browser's developer tools keep their other shortcuts.
    function gameKey(event) {
        if (event.key === 'F12' && life() >= 0) event.preventDefault();
    }

    function beforeUnload(event) {
        if (!started || getSettings().confirmClose === false || life() < 0) return;
        event.preventDefault();
        event.returnValue = '';
    }

    return {
        start() {
            if (started) return;
            started = true;
            view.addEventListener('beforeunload', beforeUnload);
            view.addEventListener('keydown', gameKey);
            timer = view.setInterval(tick, pollMilliseconds);
            // Node's timers keep a test process alive; a browser's interval id is a number without unref.
            timer?.unref?.();
        },
        stop() {
            view.clearInterval(timer);
            view.removeEventListener('beforeunload', beforeUnload);
            view.removeEventListener('keydown', gameKey);
            started = false;
        },
        // Called from the Save click, the user gesture full screen and notifications need.
        applied(settings) {
            const full = !!document.fullscreenElement;
            if (wantsFullScreen(settings) && !full) document.documentElement.requestFullscreen?.()?.catch?.(() => {});
            else if (!wantsFullScreen(settings) && full) document.exitFullscreen?.()?.catch?.(() => {});
            if (settings.notifyOnDeath === true && view.Notification?.permission === 'default') {
                view.Notification.requestPermission?.()?.catch?.(() => {});
            }
        },
        tick
    };
}
