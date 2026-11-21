import { table } from './page-language.js';
import * as art from './hud-art.js';
// The touch HUD of phones and tablets, the Android client's bottom HUD over the canvas: the canvas's fingers go to the
// game, which reads them with the Android client's gestures (joystick at the lower left, camera drag and pinch) and
// draws the joystick; the page draws the action wheel (attack, four skills, the potion belt), the Menu and Bolsa
// buttons, the menu and the experience bar with the Android client's art (hud-art.js).
// Settings' interface type decides whether it is used (BrowserHost.TouchInterface). On a phone or tablet the page
// plays lying down, as a mobile game: the first touch goes full screen and locks landscape where the browser lets it,
// a hint asks to turn the device while it stands, and the first world entry asks which HUD to use.

const labels = {
    'pt-BR': { attack: 'Atacar', skill: 'Habilidade', empty: 'Sem habilidade', potion: 'Poção', character: 'Personagem',
        inventory: 'Bolsa', skills: 'Habilidades', quests: 'Missões', menu: 'Menu', 'close-menu': 'Fechar menu', bag: 'Inventário',
        run: 'Corrida', mail: 'Correios', settings: 'Configurações', lobby: 'Trocar personagem', level: 'Nv.', experience: 'EXP',
        hunt: 'Auto-caça', 'pause-hunt': 'Pausar', 'hunt-on': 'Auto-caça · Ligado', chat: 'Chat', 'chat-feed': 'Mensagens na tela',
        'feed-shown': 'Mostrando', 'feed-hidden': 'Ocultas', send: 'Enviar', 'chat-empty': 'Nenhuma mensagem ainda.',
        'chat-hint': 'Escreva uma mensagem…', close: 'Fechar' },
    en: { attack: 'Attack', skill: 'Skill', empty: 'No skill', potion: 'Potion', character: 'Character',
        inventory: 'Bag', skills: 'Skills', quests: 'Quests', menu: 'Menu', 'close-menu': 'Close menu', bag: 'Inventory',
        run: 'Run', mail: 'Mail', settings: 'Settings', lobby: 'Change character', level: 'Lv.', experience: 'EXP',
        hunt: 'Auto-hunt', 'pause-hunt': 'Pause', 'hunt-on': 'Auto-hunt · On', chat: 'Chat', 'chat-feed': 'Messages on screen',
        'feed-shown': 'Shown', 'feed-hidden': 'Hidden', send: 'Send', 'chat-empty': 'No messages yet.',
        'chat-hint': 'Write a message…', close: 'Close' }
};

const statePeriodMs = 250;
// The light crosses each gauge of the player frame every this many seconds.
const playerShineSeconds = 3;
// The target frame reaches this far under its portrait, in fractions of the short side.
const targetFrameMargin = 0.025;
// Holding the Auto-caça button this long opens its panel.
const huntHoldMs = 500;
// A picture the game could not read yet (its file still downloading) is asked for again after this long.
const pictureRetryMs = 3000;

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

// The decoded pixels the game hands out (BrowserHost.TouchPicture: width, height, then RGBA rows) on a canvas.
export function pictureCanvas(bytes, makeCanvas = () => document.createElement('canvas')) {
    if (!bytes || bytes.length <= 4) return null;
    const width = bytes[0] | (bytes[1] << 8);
    const height = bytes[2] | (bytes[3] << 8);
    if (!width || !height || bytes.length < 4 + width * height * 4) return null;
    const canvas = makeCanvas();
    canvas.width = width;
    canvas.height = height;
    canvas.getContext('2d')?.putImageData(new ImageData(new Uint8ClampedArray(bytes.subarray(4, 4 + width * height * 4)), width, height), 0, 0);
    return canvas;
}

// A chat line's colours as the Android client's chat shows them: the line in its channel's colour and the speaker's
// name, up to a colon within the first 40 characters, brighter and bold.
const chatBody = { Party: 'rgb(121,224,255)', Whisper: 'rgb(244,177,255)', Clan: 'rgb(168,243,115)', Player: 'rgb(240,240,240)',
    Notice: 'rgb(255,214,120)' };
const chatName = { Party: 'rgb(170,238,255)', Whisper: 'rgb(255,205,255)', Clan: 'rgb(205,255,150)', Player: 'rgb(255,222,120)' };
const chatFeedLines = 5;
const chatFeedKey = 'avalon.touchChatFeed';

export function chatLineParts(text, kind) {
    const colon = text.indexOf(':');
    const named = colon > 0 && colon < 40;
    return {
        name: named ? text.slice(0, colon + 1) : '',
        rest: named ? text.slice(colon + 1) : text,
        body: chatBody[kind] ?? 'rgb(236,225,203)',
        nameColor: chatName[kind] ?? 'rgb(220,228,226)'
    };
}

// The seconds a recovering skill shows and the share of its cooldown left, at time now (page milliseconds).
export function cooldownAt(end, duration, now) {
    const remaining = Math.max(0, end - now);
    return { seconds: Math.ceil(remaining / 1000), fraction: duration > 0 ? Math.min(1, remaining / duration) : 0 };
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

    const menu = layer.querySelector('[data-role="touch-menu"]');
    const menuName = layer.querySelector('[data-role="touch-menu-name"]');
    const eventsEntry = menu?.querySelector('[data-world-action="events"]');
    const targetCanvas = layer.querySelector('[data-role="touch-target"]');
    const target = { shown: false, id: 0, name: '', level: 0, lifeValue: 0, maxLife: 0, frame: [0.7, 0.015, 0.02, 0.03, 0.11],
        life: art.easedGauge(), eased: 0, dirty: true };
    const playerCanvas = layer.querySelector('[data-role="touch-player"]');
    const player = { name: '', shownTitle: '', shownTitleColor: [255, 255, 255, 255], level: 0, maxLevel: 1, title: '', statPoints: 0, portrait: [0.035, 0.035, 0.15],
        life: art.easedGauge(), mana: art.easedGauge(), stamina: art.easedGauge(), lifeText: '', manaText: '', staminaText: '',
        drawn: 0, eased: 0 };
    // The world HUD's server, FPS and ping line moves beside the shortcuts as the Android client's readout.
    const status = root.querySelector('.world-hud-status');
    const statusHome = status && { parent: status.parentNode, next: status.nextSibling };
    const controls = root.querySelector('.world-hud-controls');
    const experienceCanvas = layer.querySelector('[data-role="touch-experience"]');
    const experience = art.easedGauge();
    let experienceText = '';
    let experienceShare = null;
    let experienceFrame = 0;
    let experienceDirty = true;

    // One drawn button: its canvas, what it shows and whether it must be drawn again.
    function orbOf(button, kind, glyph) {
        const face = document.createElement('canvas');
        face.setAttribute('aria-hidden', 'true');
        button.replaceChildren(face);
        const orb = { button, face, kind, glyph, picture: null, path: '', caption: '', count: '', badge: 0, tier: 0, rank: 0,
            maxRank: 0, cooldownEnd: 0, cooldownDuration: 0, selected: false, dirty: true, animated: false, drawn: 0 };
        const press = down => () => {
            if (button.dataset.pressed === String(down)) return;
            button.dataset.pressed = String(down);
            orb.dirty = true;
        };
        button.addEventListener('pointerdown', press(true));
        for (const name of ['pointerup', 'pointercancel', 'pointerleave']) button.addEventListener(name, press(false));
        return orb;
    }
    const attack = orbOf(layer.querySelector('[data-touch-action="touch-attack"]'), 'attack', 'attack');
    const skills = [...layer.querySelectorAll('[data-touch-slot]')].map(button => orbOf(button, 'skill', 'skill'));
    const potions = [...layer.querySelectorAll('[data-touch-box]')].map(button => orbOf(button, 'potion', 'potion'));
    const menuOrb = orbOf(layer.querySelector('[data-touch-menu-open]'), 'menu', 'menu');
    const bagOrb = orbOf(layer.querySelector('[data-touch-action="inventory"].touch-dock-orb'), 'menu', 'bag');
    const huntOrb = orbOf(layer.querySelector('[data-touch-hunt]'), 'menu', 'auto');
    const huntChip = layer.querySelector('[data-role="touch-hunt-chip"]');
    const orbs = [attack, ...skills, ...potions, menuOrb, bagOrb, huntOrb];
    let hunting = false;
    // A long press on the Auto-caça button opens its panel; the click that ends it then does nothing.
    let huntHold = 0;
    let huntHeld = false;
    const openHuntPanel = () => root.querySelector('.world-hud-hunt [data-world-action="auto-hunt-panel"]')?.click();
    huntOrb.button.addEventListener('pointerdown', () => {
        huntHeld = false;
        clearTimeout(huntHold);
        huntHold = setTimeout(() => {
            huntHeld = true;
            closeMenu();
            openHuntPanel();
        }, huntHoldMs);
    });
    for (const name of ['pointerup', 'pointercancel', 'pointerleave']) huntOrb.button.addEventListener(name, () => clearTimeout(huntHold));

    // The chat as the Android client's: a round button under the player frame, the last lines under it, and a window
    // with every line kept, the switch for the lines on screen and a field to write in.
    const chatOrb = orbOf(layer.querySelector('[data-touch-chat]'), 'menu', 'chat');
    orbs.push(chatOrb);
    const chatFeed = layer.querySelector('[data-role="touch-chat-feed"]');
    const chatWindow = layer.querySelector('[data-role="touch-chat-window"]');
    const chatLog = chatWindow?.querySelector('[data-role="touch-chat-log"]');
    const chatInput = chatWindow?.querySelector('[data-role="touch-chat-input"]');
    const chatFeedToggle = chatWindow?.querySelector('[data-touch-chat-feed]');
    let chatAdded = -1;
    let chatFeedShown = true;
    try {
        chatFeedShown = globalThis.localStorage?.getItem(chatFeedKey) !== 'hidden';
    } catch {
        // No storage here: the lines show.
    }

    function chatLines(target, lines) {
        target.replaceChildren(...lines.map(({ text, kind }) => {
            const parts = chatLineParts(text, kind);
            const row = document.createElement('div');
            row.style.color = parts.body;
            if (parts.name) {
                const name = document.createElement('b');
                name.style.color = parts.nameColor;
                name.textContent = parts.name;
                row.append(name);
            }
            row.append(parts.rest);
            return row;
        }));
    }

    function showChatFeedToggle() {
        if (chatFeed) chatFeed.hidden = !chatFeedShown;
        if (!chatFeedToggle) return;
        chatFeedToggle.setAttribute('aria-pressed', String(chatFeedShown));
        chatFeedToggle.textContent = say(chatFeedShown ? 'feed-shown' : 'feed-hidden');
    }

    function refreshChat(force = false) {
        const open = Boolean(chatWindow?.open);
        let chat;
        try {
            chat = JSON.parse(getHost()?.TouchChat?.(open ? 64 : chatFeedLines) || 'null');
        } catch {
            return;
        }
        if (!chat || (!force && chat.added === chatAdded)) return;
        chatAdded = chat.added;
        if (chatFeed) chatLines(chatFeed, chat.lines.slice(-chatFeedLines));
        if (open && chatLog) {
            if (chat.lines.length === 0) {
                const empty = document.createElement('p');
                empty.className = 'touch-chat-empty';
                empty.textContent = say('chat-empty');
                chatLog.replaceChildren(empty);
            } else {
                chatLines(chatLog, chat.lines);
            }
            chatLog.scrollTop = chatLog.scrollHeight;
        }
    }

    function closeChat() {
        if (chatWindow?.open) chatWindow.close();
        refreshChat(true);
    }

    chatOrb.button.addEventListener('click', () => {
        if (!used || !chatWindow) return;
        closeMenu();
        showChatFeedToggle();
        chatWindow.showModal();
        refreshChat(true);
    });
    chatWindow?.querySelector('[data-touch-chat-close]')?.addEventListener('click', closeChat);
    chatWindow?.addEventListener('close', () => refreshChat(true));
    // The game's keys stay out while the window has the keyboard.
    chatWindow?.addEventListener('keydown', event => event.stopPropagation());
    chatFeedToggle?.addEventListener('click', () => {
        chatFeedShown = !chatFeedShown;
        try {
            globalThis.localStorage?.setItem(chatFeedKey, chatFeedShown ? 'shown' : 'hidden');
        } catch {
            // Not kept beyond this page.
        }
        showChatFeedToggle();
    });
    chatWindow?.querySelector('[data-role="touch-chat-form"]')?.addEventListener('submit', event => {
        event.preventDefault();
        const text = chatInput?.value.trim();
        if (!text) return;
        getHost()?.TouchChatSend?.(text);
        chatInput.value = '';
        refreshChat(true);
    });
    showChatFeedToggle();

    function showHunt(active) {
        if (active === hunting && huntOrb.caption) return;
        hunting = active;
        huntOrb.selected = active;
        huntOrb.caption = say(active ? 'pause-hunt' : 'hunt');
        huntOrb.button.setAttribute('aria-label', active ? say('pause-hunt') + ' ' + say('hunt') : say('hunt'));
        huntOrb.dirty = true;
        if (huntChip) huntChip.hidden = !active;
    }

    // The menu's entries carry their glyph as an inline drawing, in the Android menu's make.
    for (const entry of menu?.querySelectorAll('[data-touch-glyph]') ?? []) {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('aria-hidden', 'true');
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', art.glyphs[entry.dataset.touchGlyph] ?? '');
        svg.append(path);
        entry.prepend(svg);
    }

    const pictures = new Map();
    function picture(path, now) {
        if (!path) return null;
        let entry = pictures.get(path);
        if (!entry || (!entry.canvas && now >= entry.retry)) {
            const canvas = pictureCanvas(getHost()?.TouchPicture?.(path));
            entry = { canvas, retry: now + pictureRetryMs };
            pictures.set(path, entry);
        }
        return entry.canvas;
    }

    function drawOrb(orb, now) {
        const ratio = globalThis.devicePixelRatio || 1;
        const size = Math.round(orb.button.clientWidth * ratio);
        if (size <= 0) return;
        if (orb.face.width !== size || orb.face.height !== size) {
            orb.face.width = size;
            orb.face.height = size;
        }
        const context = orb.face.getContext('2d');
        if (!context) return;
        context.clearRect(0, 0, size, size);
        const radius = size / 2;
        const x = radius;
        const y = radius;
        const enabled = !orb.button.disabled;
        const pressed = orb.button.dataset.pressed === 'true';
        orb.drawn = now;
        orb.animated = false;
        if (orb.kind === 'attack') {
            art.attackFace(context, x, y, radius, enabled, pressed);
            return;
        }

        art.orb(context, x, y, radius, enabled, pressed);
        const icon = radius * (orb.caption ? 0.9 : 1.1);
        if (orb.picture) {
            const scale = Math.min(icon * 1.2 / orb.picture.width, icon * 1.2 / orb.picture.height);
            const width = orb.picture.width * scale;
            const height = orb.picture.height * scale;
            context.globalAlpha = enabled ? 1 : 0.5;
            context.drawImage(orb.picture, x - width / 2, y - height / 2 - radius * 0.05, width, height);
            context.globalAlpha = 1;
        } else {
            const top = y - icon / 2 - (orb.caption ? radius * 0.14 : 0);
            art.glyph(context, orb.glyph, x - icon / 2, top, icon, enabled);
        }
        if (orb.caption) art.label(context, orb.caption, x, y + radius * 0.5, radius * 0.3, orb.selected ? art.edgeLight : [255, 255, 255, 255]);
        if (orb.count) art.label(context, orb.count, x + radius * 0.55, y + radius * 0.62, radius * 0.36, [255, 255, 255, 255]);
        const cooldown = cooldownAt(orb.cooldownEnd, orb.cooldownDuration, now);
        if (cooldown.fraction > 0) {
            const inner = radius * 0.83;
            context.fillStyle = 'rgba(0,0,0,0.745)';
            context.beginPath();
            context.moveTo(x, y);
            context.arc(x, y, inner, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * cooldown.fraction);
            context.closePath();
            context.fill();
            art.label(context, String(cooldown.seconds), x, y + radius * 0.21, radius * 0.66, [255, 255, 255, 255]);
            orb.animated = true;
        }
        if (art.skillBorder(context, x, y, radius, orb.tier, orb.rank, orb.maxRank, art.phase(now))) orb.animated = true;
        if (orb.badge > 0) art.redBadge(context, x + radius * 0.68, y - radius * 0.68, radius * 0.3, orb.badge);
    }

    // The player frame at the top left (the Android client's): a plate with a round window over the 3D head the game
    // draws there, the crest, the breathing aura, the level medallion, a dot while there are points to spend, the name
    // with the title the player shows (in its rarity's colour),
    // the life, mana and stamina gauges with a light sweeping along them, and at the highest level the class title.
    // Sizes are fractions of the screen's short side.
    function drawPlayer(now) {
        if (!playerCanvas) return;
        const elapsed = player.eased ? Math.min(0.1, (now - player.eased) / 1000) : 0;
        player.eased = now;
        const moving = player.life.step(elapsed) | player.mana.step(elapsed) | player.stamina.step(elapsed);
        if (!moving) player.eased = 0;
        player.drawn = now;
        const ratio = globalThis.devicePixelRatio || 1;
        const h = Math.min(innerWidth, innerHeight) * ratio;
        const width = Math.round(playerCanvas.clientWidth * ratio);
        const height = Math.round(playerCanvas.clientHeight * ratio);
        if (width <= 0 || height <= 0) return;
        if (playerCanvas.width !== width || playerCanvas.height !== height) {
            playerCanvas.width = width;
            playerCanvas.height = height;
        }
        const context = playerCanvas.getContext('2d');
        if (!context) return;
        context.clearRect(0, 0, width, height);
        const seconds = art.phase(now);
        const [portraitX, portraitY, portraitSize] = player.portrait;
        const size = portraitSize * h;
        const centerX = portraitX * h + size / 2;
        const centerY = portraitY * h + size / 2;
        art.plate(context, 0.012 * h, 0.012 * h, width - 2, 0.241 * h, 0.05 * h, centerX, centerY, size / 2, art.gems.crimson, true);
        art.aura(context, centerX, centerY, size / 2, seconds);
        const top = player.level >= player.maxLevel;
        art.levelBadge(context, centerX - size * (top ? 0.27 : 0.36), centerY + size * 0.38, (top ? 0.043 : 0.03) * h, player.level, player.maxLevel, seconds);
        if (player.statPoints > 0) art.redBadge(context, centerX + size * 0.38, centerY - size * 0.36, 0.02 * h, 0);
        const left = centerX + size / 2 + 0.03 * h;
        const right = width - 0.03 * h;
        art.label(context, player.name, left, 0.06 * h, 0.036 * h, art.edgeLight, 'left');
        if (player.shownTitle) {
            const nameEnd = left + context.measureText(player.name).width + 0.015 * h;
            art.titlePlaque(context, player.shownTitle, nameEnd, right, 0.058 * h, 0.034 * h, player.shownTitleColor, seconds);
        }
        const shine = seconds / playerShineSeconds % 1;
        art.bar(context, left, 0.085 * h, right, 0.125 * h, art.gauges.life, player.life.shown, player.life.trail, player.lifeText, 0.026 * h, shine);
        art.bar(context, left, 0.134 * h, right, 0.158 * h, art.gauges.mana, player.mana.shown, player.mana.trail, player.manaText, 0.019 * h,
            (shine + 0.33) % 1);
        art.bar(context, left, 0.166 * h, right - 0.08 * h, 0.186 * h, art.gauges.stamina, player.stamina.shown, player.stamina.trail,
            player.staminaText, 0.016 * h, (shine + 0.66) % 1);
        if (top) art.maxLevelTitle(context, player.title, left, right, 0.213 * h, 0.016 * h, seconds);
    }

    // The target's frame at the top centre (the Android client's): a plate with a round window over the 3D head the
    // game draws there (blue gems for an NPC), the name and level, and for what can be fought the life gauge with the
    // percentage left. Nothing without a target.
    function drawTarget(now) {
        if (!targetCanvas) return;
        const elapsed = target.eased ? Math.min(0.1, (now - target.eased) / 1000) : 0;
        target.eased = now;
        if (!target.life.step(elapsed)) target.eased = 0;
        const ratio = globalThis.devicePixelRatio || 1;
        const h = Math.min(innerWidth, innerHeight) * ratio;
        const width = Math.round(targetCanvas.clientWidth * ratio);
        const height = Math.round(targetCanvas.clientHeight * ratio);
        if (width <= 0 || height <= 0) return;
        if (targetCanvas.width !== width || targetCanvas.height !== height) {
            targetCanvas.width = width;
            targetCanvas.height = height;
        }
        const context = targetCanvas.getContext('2d');
        if (!context) return;
        context.clearRect(0, 0, width, height);
        if (!target.shown) return;
        const [, frameTop, inset, portraitY, portraitSize] = target.frame;
        const size = portraitSize * h;
        const centerX = inset * h + size / 2;
        const centerY = portraitY * h + size / 2;
        const hostile = target.maxLife > 0;
        art.plate(context, 0, frameTop * h, width, height - 2, 0.045 * h, centerX, centerY, size / 2, hostile ? art.gems.crimson : art.gems.npc);
        const textLeft = centerX + size / 2 + 0.03 * h;
        const textRight = width - 0.03 * h;
        const title = target.level > 0 ? target.name + '  ·  ' + say('level') + ' ' + target.level : target.name;
        art.label(context, title, textLeft, (hostile ? 0.06 : 0.09) * h, 0.034 * h, hostile ? [255, 170, 150, 255] : [170, 230, 255, 255], 'left');
        if (hostile) {
            const percent = Math.ceil(Math.max(0, Math.min(target.lifeValue, target.maxLife)) * 100 / target.maxLife) + '%';
            art.bar(context, textLeft, 0.088 * h, textRight, 0.13 * h, art.gauges.life, target.life.shown, target.life.trail, percent, 0.026 * h);
        }
    }

    // The experience bar across the bottom: the golden fill with a tick every tenth of the level, the level and the
    // percentage in the middle. True while its fill still eases.
    function drawExperience(now) {
        if (!experienceCanvas) return false;
        const elapsed = experienceFrame ? Math.min(0.1, (now - experienceFrame) / 1000) : 0;
        experienceFrame = now;
        const moving = experience.step(elapsed);
        const ratio = globalThis.devicePixelRatio || 1;
        const width = Math.round(experienceCanvas.clientWidth * ratio);
        const height = Math.round(experienceCanvas.clientHeight * ratio);
        if (width <= 0 || height <= 0) return moving;
        if (experienceCanvas.width !== width || experienceCanvas.height !== height) {
            experienceCanvas.width = width;
            experienceCanvas.height = height;
        }
        const context = experienceCanvas.getContext('2d');
        if (!context) return moving;
        context.clearRect(0, 0, width, height);
        const inset = height * 0.18;
        art.bar(context, inset, inset, width - inset, height - inset, art.gauges.experience, experience.shown, experience.shown, '', 0);
        const ticks = 10;
        context.strokeStyle = 'rgba(0,0,0,0.47)';
        context.lineWidth = Math.max(1, height * 0.06);
        context.beginPath();
        for (let tick = 1; tick < ticks; tick++) {
            const tickX = inset + (width - inset * 2) * tick / ticks;
            context.moveTo(tickX, inset * 1.5);
            context.lineTo(tickX, height - inset * 1.5);
        }
        context.stroke();
        art.label(context, experienceText, width / 2, height / 2, height * 0.5, [255, 255, 255, 255]);
        if (!moving) experienceFrame = 0;
        return moving;
    }

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

    // A phrase of the touch table, in English where the language's catalog does not word it yet.
    function say(key) {
        return table(labels, 'touch', getLanguage())[key] ?? labels.en[key];
    }

    function translate() {
        for (const button of layer.querySelectorAll('.touch-orb[data-touch-action]')) {
            const action = button.dataset.touchAction;
            if (labels.en[action]) button.setAttribute('aria-label', say(action));
        }
        attack.button.setAttribute('aria-label', say('attack'));
        menuOrb.button.setAttribute('aria-label', say('menu'));
        menuOrb.caption = say('menu');
        bagOrb.caption = say('inventory');
        huntOrb.caption = '';
        showHunt(hunting);
        for (const text of layer.querySelectorAll('[data-touch-label]')) text.textContent = say(text.dataset.touchLabel);
        menu?.querySelector('[data-touch-menu-close]')?.setAttribute('aria-label', say('close-menu'));
        chatOrb.button.setAttribute('aria-label', say('chat'));
        chatWindow?.querySelector('[data-touch-chat-close]')?.setAttribute('aria-label', say('close'));
        chatInput?.setAttribute('placeholder', say('chat-hint'));
        showChatFeedToggle();
        for (const orb of orbs) orb.dirty = true;
    }

    function showState(text, now) {
        if (!text) return;
        let state;
        try {
            state = JSON.parse(text);
        } catch {
            return;
        }
        const set = (orb, key, value) => {
            if (orb[key] !== value) {
                orb[key] = value;
                orb.dirty = true;
            }
        };
        const enable = (orb, enabled) => {
            if (orb.button.disabled === !enabled) return;
            orb.button.disabled = !enabled;
            orb.dirty = true;
        };
        state.skills?.forEach((skill, slot) => {
            const orb = skills[slot];
            if (!orb) return;
            set(orb, 'picture', picture(skill.icon, now));
            set(orb, 'caption', orb.picture || !skill.code ? '' : skill.caption ?? '');
            set(orb, 'tier', skill.tier ?? 0);
            set(orb, 'rank', skill.rank ?? 0);
            set(orb, 'maxRank', state.maxRank ?? 0);
            const end = skill.remaining > 0 ? now + skill.remaining : 0;
            if (Math.abs(end - orb.cooldownEnd) > statePeriodMs || (end === 0) !== (orb.cooldownEnd === 0)) {
                orb.cooldownEnd = end;
                orb.cooldownDuration = skill.duration ?? 0;
                orb.dirty = true;
            }
            enable(orb, state.alive && skill.code);
            orb.button.setAttribute('aria-label', skill.code ? say('skill') + ': ' + skill.name : say('empty'));
        });
        state.belt?.forEach((item, box) => {
            const orb = potions[box];
            if (!orb) return;
            const filled = item.count > 0;
            set(orb, 'picture', filled ? picture(item.icon, now) : null);
            set(orb, 'caption', filled ? '' : String(box + 1));
            set(orb, 'count', filled ? String(item.count) : '');
            enable(orb, state.alive && filled);
            orb.button.setAttribute('aria-label', filled ? item.name + ', ' + item.count : say('potion') + ' ' + (box + 1));
        });
        enable(attack, state.alive);
        showHunt(Boolean(getHost()?.WorldAutoHuntActive?.()));
        set(bagOrb, 'badge', state.bag ?? 0);
        set(menuOrb, 'badge', state.rewards ?? 0);
        if (eventsEntry) {
            if (state.rewards > 0) eventsEntry.dataset.unread = state.rewards > art.mostBadge ? art.mostBadge + '+' : String(state.rewards);
            else delete eventsEntry.dataset.unread;
        }
        if (menuName && menuName.textContent !== state.name) menuName.textContent = state.name ?? '';
        player.name = state.name ?? '';
        player.level = state.level ?? 0;
        player.maxLevel = state.maxLevel ?? 1;
        player.title = state.title ?? '';
        player.shownTitle = state.shownTitle ?? '';
        player.shownTitleColor = [...(state.shownTitleColor ?? [255, 255, 255]), 255];
        player.statPoints = state.statPoints ?? 0;
        if (state.portrait?.length === 3) player.portrait = state.portrait;
        player.life.set(state.maxLife > 0 ? state.life / state.maxLife : 0);
        player.mana.set(state.maxMana > 0 ? state.mana / state.maxMana : 0);
        player.stamina.set(state.maxStamina > 0 ? state.stamina / state.maxStamina : 0);
        player.lifeText = (state.life ?? 0) + ' / ' + (state.maxLife ?? 0);
        player.manaText = (state.mana ?? 0) + ' / ' + (state.maxMana ?? 0);
        player.staminaText = (state.stamina ?? 0) + ' / ' + (state.maxStamina ?? 0);
        player.eased ||= now;
        if (state.targetFrame?.length === 5 && targetCanvas) {
            target.frame = state.targetFrame;
            const [frameWidth, , , portraitY, portraitSize] = target.frame;
            targetCanvas.style.width = 'calc(var(--touch-unit) * ' + frameWidth * 100 + ')';
            targetCanvas.style.height = 'calc(var(--touch-unit) * ' + (portraitY + portraitSize + targetFrameMargin) * 100 + ' + 2px)';
        }
        const next = state.target;
        if (!next !== !target.shown || next?.id !== target.id || next?.life !== target.lifeValue || next?.maxLife !== target.maxLife
            || next?.name !== target.name || next?.level !== target.level) {
            // A new target starts with a full gauge, then eases to its life.
            if (next && next.id !== target.id) {
                target.life.set(1);
                target.life.step(1);
            }
            target.shown = Boolean(next);
            target.id = next?.id ?? 0;
            target.name = next?.name ?? '';
            target.level = next?.level ?? 0;
            target.lifeValue = next?.life ?? 0;
            target.maxLife = next?.maxLife ?? 0;
            target.life.set(target.maxLife > 0 ? target.lifeValue / target.maxLife : 0);
            target.eased ||= now;
            target.dirty = true;
            if (targetCanvas) targetCanvas.hidden = !target.shown;
        }
        if (state.experience !== experienceShare) {
            experienceShare = state.experience;
            experience.set(experienceShare ?? 0);
            experienceDirty = true;
        }
        const percent = new Intl.NumberFormat(getLanguage(), { style: 'percent', minimumFractionDigits: 2, maximumFractionDigits: 2 })
            .format(state.experience ?? 0);
        const line = say('level') + ' ' + (state.level ?? 0) + '   ·   ' + say('experience') + ' ' + percent;
        if (line !== experienceText) {
            experienceText = line;
            experienceDirty = true;
        }
    }

    function closeMenu() {
        if (menu) menu.hidden = true;
        menuOrb.selected = false;
        menuOrb.dirty = true;
    }

    // Whether Settings' interface type (or, on Auto, the device) wants the touch HUD; applied on entering the world and
    // after Settings are saved.
    function refresh() {
        const host = getHost();
        used = active && Boolean(host?.TouchInterface?.());
        layer.hidden = !used;
        root.dataset.touchHud = String(used);
        if (used && status && controls && status.parentNode !== controls) controls.append(status);
        else if (!used && statusHome && status.parentNode !== statusHome.parent) statusHome.parent.insertBefore(status, statusHome.next);
        if (!used) {
            host?.ClearTouches?.();
            closeMenu();
            if (chatWindow?.open) chatWindow.close();
        }
        lastState = -Infinity;
        experienceDirty = true;
        for (const orb of orbs) orb.dirty = true;
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
        if (!used) return;
        if (event.target.closest('[data-touch-menu-open]')) {
            if (menu) menu.hidden = !menu.hidden;
            menuOrb.selected = !menu?.hidden;
            menuOrb.dirty = true;
            return;
        }
        // Auto-caça: a tap while hunting pauses it, otherwise opens its panel (as the Android client's button).
        if (event.target.closest('[data-touch-hunt]')) {
            closeMenu();
            if (huntHeld) return;
            if (hunting) {
                if (getHost()?.RunWorldAction('auto-hunt')) showHunt(false);
            } else {
                openHuntPanel();
            }
            return;
        }
        if (event.target.closest('[data-touch-menu-close]')) {
            closeMenu();
            return;
        }
        // Every entry of the menu closes it; the world shortcuts among them run through the world HUD's handler.
        if (menu?.contains(event.target) && event.target.closest('button')) closeMenu();
        if (event.target.closest('[data-touch-menu="settings"]')) {
            root.querySelector('[data-role="settings-open"]')?.click();
            return;
        }
        const button = event.target.closest('[data-touch-action]');
        if (!button || button.disabled) return;
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
            if (!used) return;
            if (time - lastState >= statePeriodMs) {
                lastState = time;
                showState(getHost()?.TouchState(), time);
                refreshChat();
            }
            for (const orb of orbs) {
                if (orb.dirty || (orb.animated && time - orb.drawn >= art.animationFrameMs)) {
                    orb.dirty = false;
                    drawOrb(orb, time);
                }
            }
            // The frame moves all the time: smoothly while a gauge eases, every animation frame of the art otherwise.
            if (player.eased || time - player.drawn >= art.animationFrameMs) drawPlayer(time);
            if (target.dirty || target.eased) {
                target.dirty = false;
                drawTarget(time);
            }
            if (experienceDirty || experienceFrame) {
                experienceDirty = false;
                drawExperience(time);
            }
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const touchTables = { touch: labels };
