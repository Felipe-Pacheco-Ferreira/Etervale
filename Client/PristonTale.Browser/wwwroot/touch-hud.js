// The touch HUD of phones and tablets, the Android client's layout over the canvas: the canvas's fingers go to the
// game, which reads them with the Android client's gestures (joystick at the lower left, camera drag and pinch) and
// draws the joystick; the page draws the action wheel (attack, four skills, the potion belt) and the window dock.
// Settings' interface type decides whether it is used (BrowserHost.TouchInterface). On a phone or tablet the page
// plays lying down, as a mobile game: the first touch goes full screen and locks landscape where the browser lets it,
// a hint asks to turn the device while it stands, and the first world entry asks which HUD to use.

const labels = {
    'pt-BR': { attack: 'Atacar', skill: 'Habilidade', empty: 'Sem habilidade', potion: 'Poção', character: 'Personagem',
        inventory: 'Bolsa', skills: 'Habilidades', quests: 'Missões' },
    en: { attack: 'Attack', skill: 'Skill', empty: 'No skill', potion: 'Potion', character: 'Character',
        inventory: 'Bag', skills: 'Skills', quests: 'Quests' }
};

const statePeriodMs = 500;

// A phone or tablet: no fine pointer (mouse or trackpad) anywhere, and a touch screen.
export function touchOnlyDevice(media = globalThis.matchMedia?.bind(globalThis), touchPoints = globalThis.navigator?.maxTouchPoints ?? 0) {
    if (!media) return false;
    return touchPoints > 0 && media('(pointer: coarse)').matches && !media('(any-pointer: fine)').matches;
}

// Full screen and landscape, as far as the browser allows (a user gesture must call it; iOS Safari allows neither, and
// an app opened from the home screen is already full screen in landscape by its manifest).
export async function enterLandscape(doc = globalThis.document, screenInfo = globalThis.screen) {
    try {
        if (doc && !doc.fullscreenElement && doc.documentElement?.requestFullscreen) {
            await doc.documentElement.requestFullscreen({ navigationUI: 'hide' });
        }
    } catch {
        // Refused (no gesture, or not allowed): the page stays as it is.
    }
    try {
        await screenInfo?.orientation?.lock?.('landscape');
    } catch {
        // Not supported outside full screen or on this browser: the turn hint stays the guide.
    }
}

export function createTouchHud(root, getHost, getLanguage, interfaceChoice = {}) {
    const layer = root.querySelector('[data-role="touch-hud"]');
    const choice = root.querySelector('[data-role="touch-choice"]');
    const turnHint = root.querySelector('[data-role="turn-hint"]');
    let canvas = null;
    let active = false;
    let used = false;
    let lastState = -Infinity;
    const mobile = touchOnlyDevice();
    if (mobile) {
        root.dataset.mobile = 'true';
        root.addEventListener('pointerdown', () => void enterLandscape(), { capture: true, once: true });
    }
    turnHint?.querySelector('[data-turn-hint-dismiss]')?.addEventListener('click', () => {
        root.dataset.portraitAllowed = 'true';
    });
    if (!layer) return { setCanvas() {}, show() {}, hide() {}, frame() {}, refresh() {} };

    const skills = [...layer.querySelectorAll('[data-touch-slot]')];
    const potions = [...layer.querySelectorAll('[data-touch-box]')];

    function report(event, down) {
        if (event.pointerType !== 'touch' || !used || !canvas) return;
        const rect = canvas.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        getHost()?.ReportTouch(event.pointerId, (event.clientX - rect.left) / rect.width, (event.clientY - rect.top) / rect.height, down);
    }
    const down = event => report(event, true);
    const move = event => report(event, true);
    const up = event => report(event, false);

    function setCanvas(value) {
        if (value === canvas) return;
        canvas?.removeEventListener('pointerdown', down);
        canvas?.removeEventListener('pointermove', move);
        canvas?.removeEventListener('pointerup', up);
        canvas?.removeEventListener('pointercancel', up);
        getHost()?.ClearTouches();
        canvas = value;
        canvas?.addEventListener('pointerdown', down);
        canvas?.addEventListener('pointermove', move);
        canvas?.addEventListener('pointerup', up);
        canvas?.addEventListener('pointercancel', up);
    }

    function translate() {
        const words = labels[getLanguage()] ?? labels['pt-BR'];
        for (const button of layer.querySelectorAll('[data-touch-action]')) {
            const action = button.dataset.touchAction;
            if (words[action]) button.setAttribute('aria-label', words[action]);
        }
        layer.querySelector('[data-touch-action="touch-attack"]')?.setAttribute('aria-label', words.attack);
    }

    function showState(text) {
        if (!text) return;
        let state;
        try {
            state = JSON.parse(text);
        } catch {
            return;
        }
        const words = labels[getLanguage()] ?? labels['pt-BR'];
        state.skills?.forEach((skill, slot) => {
            const button = skills[slot];
            if (!button) return;
            const caption = skill.caption ?? '';
            if (button.firstElementChild.textContent !== caption) button.firstElementChild.textContent = caption;
            button.disabled = !state.alive || !skill.code;
            button.setAttribute('aria-label', skill.code ? words.skill + ': ' + skill.name : words.empty);
        });
        state.belt?.forEach((item, box) => {
            const button = potions[box];
            if (!button) return;
            const caption = item.count > 0 ? String(item.count) : String(box + 1);
            if (button.firstElementChild.textContent !== caption) button.firstElementChild.textContent = caption;
            button.dataset.filled = String(item.count > 0);
            button.disabled = !state.alive || item.count <= 0;
            button.setAttribute('aria-label', item.count > 0 ? item.name + ', ' + item.count : words.potion + ' ' + (box + 1));
        });
        layer.querySelector('[data-touch-action="touch-attack"]').disabled = !state.alive;
    }

    // Whether Settings' interface type (or, on Auto, the device) wants the touch HUD; applied on entering the world and
    // after Settings are saved.
    function refresh() {
        const host = getHost();
        used = active && Boolean(host?.TouchInterface?.());
        layer.hidden = !used;
        root.dataset.touchHud = String(used);
        if (!used) host?.ClearTouches?.();
        lastState = -Infinity;
    }

    // On a phone or tablet whose interface type was never chosen, the first world entry asks: phone HUD or PC HUD.
    function ask() {
        if (!mobile || !choice || interfaceChoice.mode !== 'Auto' || choice.open) return;
        choice.showModal?.();
    }
    choice?.addEventListener('click', event => {
        const button = event.target.closest('[data-touch-choice]');
        if (!button) return;
        const mode = button.dataset.touchChoice;
        interfaceChoice.choose?.(mode);
        choice.close();
        if (mode === 'Touch') void enterLandscape();
        refresh();
    });
    // Escape or the back gesture leaves the question for the next entry and keeps the automatic choice.
    choice?.addEventListener('cancel', () => refresh());

    layer.addEventListener('click', event => {
        const button = event.target.closest('[data-touch-action]');
        if (!button || button.disabled || !used) return;
        getHost()?.RunWorldAction(button.dataset.touchAction);
    });
    // A finger on a button never becomes a mouse press or a gesture on the canvas.
    layer.addEventListener('pointerdown', event => event.stopPropagation());

    return {
        setCanvas,
        refresh,
        show() {
            active = true;
            translate();
            refresh();
            ask();
        },
        hide() {
            active = false;
            if (choice?.open) choice.close();
            refresh();
        },
        frame(time) {
            if (!used || time - lastState < statePeriodMs) return;
            lastState = time;
            showState(getHost()?.TouchState());
        }
    };
}
