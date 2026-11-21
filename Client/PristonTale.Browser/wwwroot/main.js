// Starts the browser client on the website's entry page ("/"). The page owns the markup (#jogo with the full-window
// canvas, the login form, the language picker and the progress line) and passes the game server's WebSocket address
// in data-endpoint. This module starts loading as soon as the page opens: it creates the WebGPU device, loads the .NET
// runtime, fetches the files the game reads and runs one game frame per animation frame. The login form is the page's:
// what is typed goes to BrowserHost.SubmitLogin, and the game reports each step back through reportLogin.

import { createHeroes } from "./heroes.js";
import { createSettings } from "./settings.js";
import { createNews } from "./news.js";
import { createWorldHud } from "./world-hud.js";
import { createPackedImports } from "./three-bridge.js";
import { createWorkerGraphics, createWorkerPackedImports } from "./three-worker.js";
import { createAdaptiveResolution } from './adaptive-resolution.js';
import { createWebAudio } from './web-audio.js';
import { loginTexts } from './login-texts.js';
import { defaultLanguage, isPageLanguage, pageLanguages, table } from './page-language.js';

const root = document.getElementById("jogo");
let canvas = root.querySelector('canvas');
const part = role => root.querySelector(`[data-role="${role}"]`);
const loading = part('loading');
const progress = part('progress');
const statusLine = part('status');
const worldLoading = part('world-loading');
const worldLoadingName = part('world-loading-name');
const worldLoadingProgress = part('world-loading-progress');
const worldLoadingPercent = part('world-loading-percent');
const worldLoadingStatus = part('world-loading-status');
const settingsPanel = part('settings');
const card = part('login');
const form = part('login-form');
const account = part('account');
const password = part('password');
const remember = part('remember');
const stayConnected = part('stay-connected');
const recentSession = part('recent-session');
const resumeSession = part('resume-session');
const forgetSession = part('forget-session');
const message = part('message');
const submit = part('submit');
const language = part('language');
const fullscreen = part('fullscreen');
const openingSound = part('opening-sound');
const openingMusic = part('opening-music');

const storageKeys = { language: 'avalon.language', account: 'avalon.account' };
const sessionUrl = '/api/play-session';

function load(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

function store(key, value) {
    try {
        if (value === null) localStorage.removeItem(key);
        else localStorage.setItem(key, value);
    } catch {
        // Storage can be off (private windows, blocked site data); the page works without it.
    }
}

const savedLanguage = load(storageKeys.language);
let selectedLanguage = isPageLanguage(savedLanguage) ? savedLanguage : defaultLanguage;
let text = table(loginTexts, 'login', selectedLanguage);
let started = false;

function applyLanguage() {
    text = table(loginTexts, 'login', selectedLanguage);
    document.documentElement.lang = selectedLanguage;
    if (account) account.placeholder = text.accountPlaceholder;
    if (forgetSession) forgetSession.setAttribute('aria-label', text.forgetAccount);
    if (openingSound) {
        openingSound.setAttribute('aria-label', text.openingMusic);
        openingSound.title = text.openingMusic;
    }
    for (const element of root.querySelectorAll('[data-text]')) {
        element.textContent = text.labels[element.dataset.text];
    }
}

if (language) {
    // The page's languages, each named in itself, replace the options the page was served with.
    language.replaceChildren(...pageLanguages.map(([code, name]) => Object.assign(document.createElement('option'), { value: code, textContent: name })));
    language.value = selectedLanguage;
    language.addEventListener('change', () => setLanguage(language.value));
}
applyLanguage();

function setLanguage(value) {
    if (!isPageLanguage(value) || value === selectedLanguage) return;
    store(storageKeys.language, value);
    // The game's text resources take their language during Start.
    if (started) {
        location.reload();
        return;
    }
    if (language) language.value = value;
    selectedLanguage = value;
    applyLanguage();
}

const recovery = part('recovery');
if (recovery) {
    const input = part('recovery-input');
    const message = part('recovery-message');
    for (const link of card.querySelectorAll('[data-recovery]')) {
        link.addEventListener('click', event => {
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            const mode = link.dataset.recovery;
            const details = text.recovery[mode];
            recovery.dataset.mode = mode;
            part('recovery-title').textContent = link.textContent;
            part('recovery-info').textContent = details.info;
            part('recovery-label').textContent = details.label;
            part('recovery-submit').textContent = details.submit;
            input.type = mode === 'id' ? 'email' : 'text';
            input.autocomplete = mode === 'id' ? 'email' : 'username';
            input.maxLength = 254;
            input.placeholder = details.placeholder;
            input.value = mode === 'password' ? account.value : '';
            message.textContent = '';
            recovery.showModal();
        });
    }
    part('recovery-form').addEventListener('submit', event => {
        event.preventDefault();
        message.textContent = text.recoveryUnavailable;
    });
    part('recovery-cancel').addEventListener('click', () => recovery.close());
    recovery.addEventListener('close', () => {
        input.value = '';
        message.textContent = '';
    });
}

fullscreen?.addEventListener('click', async () => {
    if (document.fullscreenElement) {
        await document.exitFullscreen?.();
    } else {
        await document.documentElement.requestFullscreen?.();
    }
});

function showLoading(line, tone = 'busy', percent = null) {
    if (!loading) return;
    loading.hidden = false;
    loading.dataset.tone = tone;
    statusLine.textContent = line;
    if (percent === null) progress.removeAttribute('value');
    else progress.value = percent;
    progress.hidden = tone === 'error';
}

const mapLoadProfiling = new URLSearchParams(location.search).get('profile') === '1';
let mapLoadProfile = null;
let publicAssetCache = null;

function recordMapResources(profile, entries) {
    for (const entry of entries) {
        if (entry.entryType !== 'resource' || entry.startTime < profile.started) continue;
        const network = profile.network;
        network.requests++;
        network.transferBytes += entry.transferSize || 0;
        network.encodedBytes += entry.encodedBodySize || 0;
        network.decodedBytes += entry.decodedBodySize || 0;
        if (entry.transferSize === 0 && entry.decodedBodySize > 0) network.cachedResponses++;
        if (!entry.transferSize && !entry.encodedBodySize && !entry.decodedBodySize) network.unknownSizeResponses++;
    }
}

function profileWorldLoading(phase, percent, mapName) {
    if (!mapLoadProfiling) return;
    if (phase === 'loading' && percent === 0 && !mapLoadProfile) {
        const profile = mapLoadProfile = { started: performance.now(), map: '', milestones: { 0: 0 }, observer: null,
            cacheStart: publicAssetCache?.snapshot(),
            network: { observed: false, requests: 0, transferBytes: 0, encodedBytes: 0, decodedBytes: 0,
                cachedResponses: 0, unknownSizeResponses: 0 } };
        if (typeof PerformanceObserver === 'function') {
            try {
                profile.observer = new PerformanceObserver(list => recordMapResources(profile, list.getEntries()));
                profile.observer.observe({ type: 'resource', buffered: false });
                profile.network.observed = true;
            } catch {
                profile.observer?.disconnect();
                profile.observer = null;
            }
        }
    }
    const profile = mapLoadProfile;
    if (!profile) return;
    if (mapName) profile.map = mapName;
    const elapsed = Math.round(Math.max(0, performance.now() - profile.started) * 10) / 10;
    if (phase === 'loading') {
        if ((percent === 80 || percent === 85 || percent === 90 || percent === 95 || percent === 99)
            && profile.milestones[percent] === undefined) profile.milestones[percent] = elapsed;
        return;
    }
    if (phase === 'ready') profile.milestones[100] = elapsed;
    if (profile.observer) {
        recordMapResources(profile, profile.observer.takeRecords());
        profile.observer.disconnect();
    }
    console.info('[MapLoadProfile] ' + JSON.stringify({ map: profile.map, outcome: phase === 'ready' ? 'ready' : 'cancelled',
        milestones: profile.milestones, totalMs: elapsed, network: profile.network,
        assetCache: publicAssetCache?.snapshot(profile.cacheStart) ?? null }));
    mapLoadProfile = null;
}

let worldLoadingEpoch = 0;
let pendingWorldReady = null;
let adaptiveWorldReady = false;
let adaptiveWorldLoading = false;

function reportWorldLoading(phase, percent, mapName) {
    if ((phase === 'loading' && percent === 0) || (phase !== 'loading' && phase !== 'ready')) {
        worldLoadingEpoch++;
        pendingWorldReady = null;
    }
    if (phase === 'ready' && activeGraphics?.workerRendering) {
        pendingWorldReady ??= { epoch: worldLoadingEpoch, percent, mapName, waiting: false };
        return;
    }
    commitWorldLoading(phase, percent, mapName);
}

function fenceWorldReady(graphics, onError) {
    const pending = pendingWorldReady;
    if (!pending || pending.waiting) return;
    pending.waiting = true;
    graphics.waitForSubmittedFrame().then(() => {
        if (pendingWorldReady !== pending || pending.epoch !== worldLoadingEpoch || activeGraphics !== graphics) return;
        pendingWorldReady = null;
        commitWorldLoading('ready', pending.percent, pending.mapName);
    }, error => {
        if (pendingWorldReady === pending && pending.epoch === worldLoadingEpoch) onError(error);
    });
}

function commitWorldLoading(phase, percent, mapName) {
    adaptiveWorldReady = phase === 'ready';
    adaptiveWorldLoading = phase === 'loading';
    profileWorldLoading(phase, percent, mapName);
    if (!worldLoading) return;
    if (phase !== 'loading') {
        if (phase === 'ready') {
            worldLoadingProgress.value = percent;
            worldLoadingPercent.textContent = percent + '%';
            worldHud.show();
        } else {
            worldHud.hide();
        }
        worldLoading.hidden = true;
        delete root.dataset.worldLoading;
        return;
    }

    const value = Math.round(Math.max(0, Math.min(100, percent)));
    const map = mapName || text.loadingMap;
    if (worldLoadingName.textContent !== map) worldLoadingName.textContent = map;
    worldLoadingProgress.value = value;
    worldLoadingPercent.textContent = value + '%';
    if (worldLoading.hidden) {
        worldHud.hide();
        worldLoading.setAttribute('aria-label', text.mapProgress);
        worldLoadingProgress.setAttribute('aria-label', text.mapProgress);
        worldLoadingStatus.textContent = text.loadingWorld;
        root.dataset.worldLoading = 'true';
        worldLoading.hidden = false;
    }
}

function showMessage(line, tone = 'info') {
    message.textContent = line;
    message.dataset.tone = tone;
}

// The login: the form appears when the game reports its login screen (after the camera's descent); the account and
// password then lead to the world list, and choosing a world logs in on its game server. A choice made before the files
// up to the character list are in (FetchRemainingAsync) waits and goes in once they are.
let host = null;
let activeGraphics = null;
let gameReady = false;
let filesReady = false;
let filesRequest = null;
let loginPainted = false;
let charactersPrefetched = false;
let credentials = null;
let accountName = "";

// The character screen's panels (heroes.js): its commands go to the game after the current frame returns.
const heroes = createHeroes(root, new URL('./', import.meta.url).href,
    (kind, index = 0, value = '') => setTimeout(() => host?.CharacterCommand(kind, index, value)),
    () => selectedLanguage, () => accountName);
// The gear's Settings panel (settings.js).
const settings = createSettings(root, () => host, () => selectedLanguage, setLanguage, () => worldHud.updateQuality());
const news = createNews(root, () => host, () => selectedLanguage);
const worldHud = createWorldHud(root, () => host, () => selectedLanguage, () => settings.qualityLabel,
    () => activeGraphics?.workerRendering ? activeGraphics.workerStats.completedFrames : activeGraphics?.stats.frames ?? null,
    { get mode() { return settings.interfaceMode; }, choose: mode => settings.setInterfaceMode(mode) });
const adaptiveResolution = createAdaptiveResolution();
let adaptiveChangePending = false;
let nextAdaptiveLabelTime = 0;

// The runtime sizes the canvas at min(devicePixelRatio, graphics.maxPixelRatio) drawing pixels per CSS pixel and
// refits it on a window resize event.
function applyMaxPixelRatio(graphics) {
    const cap = settings.maxPixelRatio;
    if (graphics.maxPixelRatio === cap) return;
    graphics.maxPixelRatio = cap;
    window.dispatchEvent(new Event('resize'));
}

function updateAdaptiveResolution(graphics, time, frames, onError) {
    const sceneReady = frames >= 4 && loading.hidden && !adaptiveWorldLoading && !pendingWorldReady;
    const revision = settings.appliedRevision * 2 + Number(adaptiveWorldReady);
    const eligible = document.visibilityState === 'visible' && !settingsPanel?.open;
    if (!adaptiveChangePending && adaptiveResolution.update(time, frames, settings.dynamicResolutionEnabled,
        settings.renderScale, settings.maxFps, canvas.width, canvas.height, sceneReady, eligible, revision, settings.classicLighting)) {
        adaptiveChangePending = true;
        Promise.resolve(graphics.setDynamicResolutionScale(adaptiveResolution.scale, adaptiveResolution.shadowHz)).then(() => {
            adaptiveChangePending = false;
            nextAdaptiveLabelTime = 0;
        }, onError);
    }
    if (adaptiveChangePending || time < nextAdaptiveLabelTime) return;
    nextAdaptiveLabelTime = time + 500;
    const effective = graphics.getCapabilities().effective;
    if (!effective) return;
    const scale = effective.renderScale;
    settings.setDynamicResolutionState({ enabled: settings.dynamicResolutionEnabled && sceneReady,
        scale, width: effective.renderWidth ?? Math.max(1, Math.round(canvas.width * scale)),
        height: effective.renderHeight ?? Math.max(1, Math.round(canvas.height * scale)),
        shadowUpdateRate: effective.dynamicShadowUpdateRate ?? 0 });
}

if (openingMusic && openingSound) {
    openingSound.hidden = false;
    openingMusic.src = new URL('./assets/StartImage/Opening/Intro.wav', import.meta.url).href;
    openingMusic.addEventListener('playing', () => openingSound.setAttribute('aria-pressed', 'true'));
    openingMusic.addEventListener('pause', () => openingSound.setAttribute('aria-pressed', 'false'));
    async function playOpeningMusic() {
        try {
            await openingMusic.play();
        } catch (error) {
            if (error.name !== 'NotAllowedError' && error.name !== 'AbortError') console.warn('Opening music:', error);
        }
    }

    openingSound.addEventListener('click', () => {
        if (openingMusic.paused) {
            void playOpeningMusic();
        } else {
            openingMusic.pause();
        }
    });
    if (settings.openingMusicEnabled) void playOpeningMusic();
}
let pending = null;

const worldsCard = part('worlds');
const worldList = part('world-list');
const worldsAccount = part('worlds-account');
const worldsMessage = part('worlds-message');

const savedAccount = load(storageKeys.account);
if (savedAccount && account) {
    account.value = savedAccount;
    remember.checked = true;
}

let sessionAccount = null;
let sessionBusy = false;

function showSession(name) {
    sessionAccount = name;
    recentSession.hidden = !name;
    part('recent-account').textContent = name ?? '';
    part('recent-avatar').textContent = name?.charAt(0).toUpperCase() ?? '';
    stayConnected.checked = Boolean(name);
}

async function restoreSession() {
    try {
        const response = await fetch(sessionUrl, { cache: 'no-store' });
        if (!response.ok) return;
        const { account: name } = await response.json();
        if (name && !sessionBusy && !credentials && !sessionAccount) {
            showSession(name);
            if (!account.value) account.value = name;
        }
    } catch {
        // The password login is available when the browser session endpoint cannot be reached.
    }
}

if (recentSession) void restoreSession();

async function sessionRequest(method, input) {
    const response = await fetch(sessionUrl, {
        method,
        ...(input ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) } : {}),
        cache: 'no-store'
    });
    if (response.status === 401) throw new Error(text.invalidCredentials);
    if (response.status === 429) throw new Error(text.sessionLimited);
    if (!response.ok) throw new Error(text.sessionUnavailable);
    return response.status === 204 ? null : response.json();
}

forgetSession?.addEventListener('click', async () => {
    if (sessionBusy) return;
    sessionBusy = true;
    const name = sessionAccount;
    forgetSession.disabled = true;
    try {
        await sessionRequest('DELETE');
        if (load(storageKeys.account) === name) store(storageKeys.account, null);
        if (account.value === name) {
            account.value = '';
            password.value = '';
            remember.checked = false;
        }
        showSession(null);
        showMessage('');
        account.focus();
    } catch (error) {
        showMessage(error instanceof TypeError ? text.sessionUnavailable : error.message, 'error');
    } finally {
        sessionBusy = false;
        forgetSession.disabled = false;
    }
});

// The opening plays behind the page; the login fades in over it a moment after the live scene starts, and the flight
// goes on behind it.
const loginDelayMs = 4200;
let atLogin = true;

function revealLogin() {
    if (!atLogin || !card.hidden || !worldsCard.hidden || !registerCard.hidden) return;
    card.hidden = false;
    (account.value ? password : account).focus({ preventScroll: true });
}

function showWorldsMessage(line, tone = 'info') {
    worldsMessage.textContent = line;
    worldsMessage.dataset.tone = tone;
}

function fetchRemaining() {
    if (filesReady || filesRequest) return;
    filesRequest = host.FetchRemainingAsync().then(() => {
        filesReady = true;
        filesRequest = null;
        sendPending();
        prefetchCharacters();
    }, error => {
        filesRequest = null;
        console.error('Falha ao carregar a seleção de personagens:', error);
        if (!pending) return;
        pending = null;
        for (const button of worldList.querySelectorAll('button:not([data-online="false"])')) {
            button.disabled = false;
        }
        showWorldsMessage(text.filesLoadFailed, 'error');
    });
}

function prefetchCharacters() {
    const connection = navigator.connection;
    if (charactersPrefetched || !loginPainted || !filesReady || document.hidden || connection?.saveData
        || connection?.effectiveType === 'slow-2g' || connection?.effectiveType === '2g') return;
    charactersPrefetched = true;
    host.PrefetchCharacterAssetsAsync().catch(error => console.warn('Falha ao adiantar os modelos de criação:', error));
}

document.addEventListener('visibilitychange', prefetchCharacters);

function sendPending() {
    if (!pending || !gameReady || !filesReady) return false;
    const { selected, endpoint } = pending;
    pending = null;
    // Possibly called from inside the game's frame: hand the form in after it returns.
    setTimeout(() => submitToWorld(selected, endpoint));
    return true;
}

async function submitToWorld(selected, endpoint) {
    if (credentials !== selected) return;
    let secret = selected.secret;
    if (secret === null) {
        try {
            const response = await fetch(sessionUrl + '/ticket', { method: 'POST', cache: 'no-store' });
            if (credentials !== selected) return;
            if (response.status === 401) {
                showSession(null);
                showLogin(text.sessionExpired);
                return;
            }
            if (!response.ok) throw new Error();
            const ticket = await response.json();
            if (ticket.account !== selected.name) {
                showSession(null);
                showLogin(text.sessionExpired);
                return;
            }
            secret = ticket.ticket;
        } catch {
            if (credentials === selected) showLogin(text.sessionUnavailable);
            return;
        }
    }
    if (credentials === selected) host.SubmitLogin(selected.name, secret, endpoint);
}

function showLogin(line = '') {
    atLogin = true;
    credentials = null;
    pending = null;
    worldsCard.hidden = true;
    card.hidden = false;
    if (openingSound) openingSound.hidden = false;
    submit.disabled = false;
    showMessage(line, line ? 'error' : 'info');
    if (line) password.value = '';
    (account.value ? password : account).focus();
}

function openWorlds(name, secret) {
    credentials = { name, secret };
    accountName = name;
    showMessage('');
    card.hidden = true;
    worldsCard.hidden = false;
    worldsAccount.textContent = text.accountLine(name);
    showWorlds();
}

resumeSession?.addEventListener('click', () => {
    if (!sessionBusy && sessionAccount) openWorlds(sessionAccount, null);
});

form?.addEventListener('submit', async event => {
    event.preventDefault();
    if (sessionBusy) return;
    const name = account.value.trim();
    if (name === sessionAccount && stayConnected.checked && !password.value) {
        openWorlds(name, null);
        return;
    }
    if (!name || !password.value) {
        showMessage(text.missing, 'error');
        (name ? password : account).focus();
        return;
    }

    const persist = stayConnected.checked;
    const rememberName = remember.checked;
    let secret = password.value;
    let authenticatedName = name;
    if (persist || name === sessionAccount) {
        sessionBusy = true;
        submit.disabled = true;
        showMessage('');
        try {
            if (persist) {
                const session = await sessionRequest('POST', { account: name, password: secret });
                authenticatedName = session.account;
                showSession(authenticatedName);
                secret = null;
                password.value = '';
            } else {
                await sessionRequest('DELETE');
                showSession(null);
            }
        } catch (error) {
            showMessage(error instanceof TypeError ? text.sessionUnavailable : error.message, 'error');
            return;
        } finally {
            sessionBusy = false;
            submit.disabled = false;
        }
    }
    store(storageKeys.account, rememberName ? authenticatedName : null);
    openWorlds(authenticatedName, secret);
});

part('switch-account')?.addEventListener('click', () => showLogin());

// "Criar conta": the site's registration (RegistrationApi, /api/conta) in a panel over the scene. The terms are
// accepted only after their box was scrolled to the end; a created account comes back to the login with its name.
const registerCard = part('register');
const registerForm = part('register-form');
const registerMessage = part('register-message');
const registerSubmit = part('register-submit');
const termsBox = part('terms');
const termsAccept = part('terms-accept');
const termsHint = part('terms-hint');

function registerField(name) {
    return registerForm.querySelector(`[data-field="${name}"]`);
}

function showRegisterMessage(line, tone = 'info') {
    registerMessage.textContent = line;
    registerMessage.dataset.tone = tone;
}

function openRegister(event) {
    event?.preventDefault();
    card.hidden = true;
    worldsCard.hidden = true;
    registerCard.hidden = false;
    showRegisterMessage('');
    registerField('account').focus();
}

function closeRegister() {
    registerCard.hidden = true;
    card.hidden = false;
}

part('open-register')?.addEventListener('click', openRegister);
part('register-cancel')?.addEventListener('click', () => {
    closeRegister();
    (account.value ? password : account).focus();
});

termsBox?.addEventListener('scroll', () => {
    if (termsBox.scrollTop + termsBox.clientHeight >= termsBox.scrollHeight - 4) {
        termsAccept.disabled = false;
        termsHint.hidden = true;
    }
});

registerForm?.addEventListener('submit', async event => {
    event.preventDefault();
    for (const input of registerForm.querySelectorAll('[aria-invalid]')) input.removeAttribute('aria-invalid');
    const value = name => registerField(name)?.value ?? '';
    if (value('password') !== value('confirmation')) {
        registerField('confirmation').setAttribute('aria-invalid', 'true');
        showRegisterMessage('As senhas não são iguais.', 'error');
        registerField('confirmation').focus();
        return;
    }

    if (!termsAccept.checked) {
        showRegisterMessage(termsAccept.disabled ? 'Role os Termos de Uso até o fim e aceite-os.' : 'Aceite os Termos de Uso.', 'error');
        return;
    }

    registerSubmit.disabled = true;
    showRegisterMessage('Criando a conta…');
    try {
        const response = await fetch('/api/conta', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                account: value('account').trim(),
                email: value('email').trim(),
                password: value('password'),
                confirmation: value('confirmation'),
                referral: value('referral').trim(),
                terms: termsAccept.checked,
                turnstile: registerForm.querySelector('[name="cf-turnstile-response"]')?.value ?? '',
                site: value('site')
            })
        });
        const answer = await response.json();
        if (answer.ok) {
            account.value = value('account').trim();
            password.value = '';
            registerForm.reset();
            termsAccept.disabled = true;
            termsHint.hidden = false;
            closeRegister();
            showMessage(answer.message);
            password.focus();
            return;
        }

        const field = answer.field && registerField(answer.field);
        field?.setAttribute('aria-invalid', 'true');
        field?.focus?.();
        showRegisterMessage(answer.message, 'error');
    } catch {
        showRegisterMessage('Não foi possível criar a conta agora. Tente de novo.', 'error');
    } finally {
        registerSubmit.disabled = false;
        window.turnstile?.reset?.();
    }
});

// The worlds of /api/worlds; each one's game server answers /status with its players and cap, timed as the ping.
async function showWorlds() {
    showWorldsMessage('');
    worldList.replaceChildren();
    let worlds = [];
    try {
        worlds = await (await fetch('/api/worlds', { cache: 'no-store' })).json();
    } catch {
        showWorldsMessage(text.worldsFailed, 'error');
        return;
    }

    for (const world of worlds) {
        const item = document.createElement('li');
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'world';
        const light = document.createElement('span');
        light.className = 'world-light';
        const name = document.createElement('span');
        name.className = 'world-name';
        name.append(world.name, ' ');
        const region = document.createElement('small');
        region.textContent = `(${world.region})`;
        const badge = document.createElement('span');
        badge.className = 'world-badge';
        badge.dataset.kind = world.kind;
        badge.textContent = world.kind === 'AntiKs' ? '◆ ANTI KS' : '◆ NORMAL';
        name.append(region, badge);
        const detail = document.createElement('span');
        detail.className = 'world-detail';
        detail.textContent = text.checking;
        const arrow = document.createElement('span');
        arrow.className = 'world-arrow';
        arrow.textContent = '›';
        button.append(light, name, arrow, detail);
        button.addEventListener('click', () => chooseWorld(world));
        item.append(button);
        worldList.append(item);
        measureWorld(world, button, detail);
    }

    worldList.querySelector('button')?.focus();
}

async function measureWorld(world, button, detail) {
    const started = performance.now();
    try {
        const response = await fetch(world.status, { cache: 'no-store', mode: 'cors' });
        const load = await response.json();
        const ping = Math.round(performance.now() - started);
        world.ping = ping;
        button.dataset.online = 'true';
        detail.textContent = text.worldDetail(ping, load.players, load.capacity);
    } catch {
        button.dataset.online = 'false';
        button.disabled = true;
        detail.textContent = text.offline;
    }
}

function chooseWorld(world) {
    if (!credentials || pending) return;
    worldHud.selectWorld(world);
    for (const other of worldList.querySelectorAll('button')) other.disabled = true;
    pending = { selected: credentials, endpoint: world.endpoint };
    showWorldsMessage('');
    if (!sendPending()) fetchRemaining();
}

function reportLogin(state, line) {
    if (state === 'form') {
        adaptiveWorldReady = false;
        worldHud.hide();
        const first = !gameReady;
        gameReady = true;
        if (sendPending()) return;
        // A refused login, or the way back from the character list, returns to the form; the first report finds the
        // form already on screen (it is up from the page's start) and leaves the player's typing alone.
        if (line || (!first && !credentials)) showLogin(line);
    } else if (state === 'done') {
        atLogin = false;
        openingMusic?.pause();
        if (openingSound) openingSound.hidden = true;
        password.value = '';
        credentials = null;
        showWorldsMessage('');
        worldsCard.hidden = true;
        card.hidden = true;
        canvas.focus();
    }
}

async function installPublicAssetCache(clientAddress, environment = globalThis, fill = false) {
    const counts = { requests: 0, hits: 0, hitHeaderBytes: 0, hitsWithoutLength: 0, misses: 0,
        puts: 0, readErrors: 0, writeErrors: 0, quotaErrors: 0, busyWriteSkips: 0, initializationErrors: 0 };
    const state = { enabled: false, mode: fill ? 'diagnostic-fill' : 'read-only', readsDisabled: false, writesDisabled: !fill, pendingPuts: 0 };
    const controller = { snapshot(previous) {
        const result = { ...state };
        for (const key of Object.keys(counts)) result[key] = counts[key] - (previous?.[key] ?? 0);
        return result;
    } };
    try {
        const storage = environment.caches;
        if (!storage || typeof environment.fetch !== 'function') return controller;
        const cacheName = 'noxius-public-assets-v1';
        if (!fill && !await storage.has(cacheName)) return controller;
        const original = environment.fetch.bind(environment);
        const base = new environment.URL(clientAddress);
        const manifestUrl = new environment.URL('bootstrap.txt', base).href;
        const [manifest, cache] = await Promise.all([
            original(manifestUrl, { cache: 'no-cache', credentials: 'same-origin' }),
            storage.open(cacheName)
        ]);
        if (manifest.status !== 200 || manifest.type !== 'basic' || manifest.redirected || manifest.url !== manifestUrl) return controller;
        const allowed = new Map();
        for (const line of (await manifest.text()).split(/\r?\n/)) {
            const columns = line.split('\t');
            if (columns.length !== 2 || !/^[a-f0-9]{64}$/i.test(columns[1])) continue;
            const relative = columns[0];
            if (!/^(assets|Content)\//.test(relative) || /[\\%?#]/.test(relative)
                || relative.split('/').some(part => !part || part === '.' || part === '..')) continue;
            const url = new environment.URL(relative.split('/').map(encodeURIComponent).join('/'), base);
            const hash = columns[1].toUpperCase();
            if (allowed.has(url.pathname) && allowed.get(url.pathname) !== hash) throw new Error('Conflicting public asset versions.');
            allowed.set(url.pathname, hash);
        }
        if (!allowed.size) return controller;
        function publicRequest(input, options) {
            try {
                const method = options?.method ?? input?.method ?? 'GET';
                const body = options?.body ?? input?.body;
                const signal = options?.signal ?? input?.signal;
                const integrity = options?.integrity ?? input?.integrity;
                if (String(method).toUpperCase() !== 'GET' || body != null || signal?.aborted || integrity) return null;
                const headers = new environment.Headers(options?.headers ?? input?.headers);
                for (const name of ['authorization', 'proxy-authorization', 'cookie', 'range', 'if-range', 'if-none-match', 'if-modified-since', 'if-match', 'if-unmodified-since']) {
                    if (headers.has(name)) return null;
                }
                const url = new environment.URL(input?.url ?? input, base);
                if (url.origin !== base.origin || url.hash || url.searchParams.size !== 1 || url.searchParams.getAll('v').length !== 1) return null;
                const hash = url.searchParams.get('v');
                if (!/^[a-f0-9]{64}$/i.test(hash ?? '') || allowed.get(url.pathname) !== hash.toUpperCase()) return null;
                const policy = options?.cache ?? input?.cache;
                return { url: url.href, signal, lookup: policy !== 'no-store' && policy !== 'reload' && policy !== 'no-cache', store: policy !== 'no-store' };
            } catch {
                return null;
            }
        }
        function publicResponse(response, url) {
            return response?.status === 200 && response.type === 'basic' && !response.redirected && response.url === url
                && !response.headers.has('content-range') && !(response.headers.get('vary') ?? '').split(',').some(value => value.trim() === '*')
                && !/(?:^|,)\s*(?:private|no-store|no-cache)(?:\s|,|=|$)/i.test(response.headers.get('cache-control') ?? '');
        }
        function writeFailure(error) {
            counts.writeErrors++;
            if (error?.name === 'QuotaExceededError') counts.quotaErrors++;
            state.writesDisabled = true;
        }
        function abortableHit(response, signal) {
            if (!signal || !response.body) return response;
            const reader = response.body.getReader();
            let terminal = false;
            let controller;
            const release = () => { try { reader.releaseLock(); } catch {} };
            const cleanup = () => signal.removeEventListener('abort', abort);
            const abort = () => {
                if (terminal) return;
                terminal = true;
                cleanup();
                const reason = signal.reason ?? new environment.DOMException('Asset request cancelled', 'AbortError');
                controller.error(reason);
                Promise.resolve(reader.cancel(reason)).catch(() => {}).finally(release);
            };
            const stream = new environment.ReadableStream({
                start(value) {
                    controller = value;
                    signal.addEventListener('abort', abort, { once: true });
                    if (signal.aborted) abort();
                },
                async pull(value) {
                    if (terminal) return;
                    try {
                        const chunk = await reader.read();
                        if (terminal) return;
                        if (chunk.done) {
                            terminal = true;
                            cleanup();
                            value.close();
                            release();
                        } else value.enqueue(chunk.value);
                    } catch (error) {
                        if (!terminal) {
                            terminal = true;
                            cleanup();
                            value.error(error);
                        }
                        release();
                    }
                },
                cancel(reason) {
                    if (terminal) return;
                    terminal = true;
                    cleanup();
                    return Promise.resolve(reader.cancel(reason)).finally(release);
                }
            });
            function metadata(result) {
                Object.defineProperties(result, { url: { value: response.url }, type: { value: response.type }, redirected: { value: response.redirected } });
                const clone = result.clone.bind(result);
                result.clone = () => metadata(clone());
                return result;
            }
            return metadata(new environment.Response(stream, { status: response.status, statusText: response.statusText, headers: response.headers }));
        }
        environment.fetch = async function publicAssetFetch(input, options) {
            const request = publicRequest(input, options);
            if (!request) return original(input, options);
            counts.requests++;
            if (request.lookup && !state.readsDisabled) {
                try {
                    const cached = await cache.match(request.url, { ignoreSearch: false, ignoreVary: false });
                    if (request.signal?.aborted) return original(input, options);
                    if (publicResponse(cached, request.url)) {
                        counts.hits++;
                        const header = cached.headers.get('content-length');
                        const bytes = header === null ? NaN : Number(header);
                        if (Number.isFinite(bytes) && bytes >= 0) counts.hitHeaderBytes += bytes;
                        else counts.hitsWithoutLength++;
                        return abortableHit(cached, request.signal);
                    }
                } catch {
                    counts.readErrors++;
                    state.readsDisabled = true;
                    state.writesDisabled = true;
                }
            }
            counts.misses++;
            const response = await original(input, options);
            if (request.store && !state.writesDisabled && publicResponse(response, request.url)) {
                // Bound background tees; cache storage never gates the loader's response or readiness.
                if (state.pendingPuts >= 16) counts.busyWriteSkips++;
                else {
                    try {
                        const copy = response.clone();
                        state.pendingPuts++;
                        Promise.resolve().then(() => cache.put(request.url, copy)).then(() => counts.puts++, writeFailure)
                            .finally(() => state.pendingPuts--);
                    } catch (error) {
                        writeFailure(error);
                    }
                }
            }
            return response;
        };
        state.enabled = true;
    } catch {
        counts.initializationErrors++;
    }
    return controller;
}

function startPublicAssetCache(clientAddress, environment = globalThis) {
    const diagnostics = new URLSearchParams(environment.location.search);
    if (diagnostics.get('profile') !== '1' || diagnostics.get('asset-cache') !== '1') return null;
    return installPublicAssetCache(clientAddress, environment, diagnostics.get('asset-cache-fill') === '1');
}

function configureWeatherDiagnostic(host, search = location.search) {
    const query = new URLSearchParams(search);
    const enabled = query.get('profile') === '1' && query.get('qa-weather') === 'rain-full';
    const accepted = host.SetDiagnosticFullRain(enabled);
    if (enabled && !accepted) throw new Error('Client weather diagnostics require frame profiling.');
    if (enabled) console.info('[WeatherDiagnostic] Full rain requested for this client; FrameProfile reports actual application and drawing.');
    return enabled;
}

function enableCanvasPicking(profile = false) {
    const heroesPanel = root.querySelector('[data-role="heroes"]');
    let start;
    let suppressMouseClick = false;
    let previousPickMs;

    function trace(action, event, reason, x, y) {
        if (!profile) return;
        console.info('[StageInput] ' + JSON.stringify({ action, type: event?.type, detail: event?.detail,
            eventMs: event?.timeStamp, deltaMs: previousPickMs === undefined ? null : event?.timeStamp - previousPickMs,
            screen: heroesPanel.dataset.screen, visible: !heroesPanel.hidden, pointerType: event?.pointerType,
            button: event?.button, pointerId: event?.pointerId, primary: event?.isPrimary, trusted: event?.isTrusted,
            clientX: event?.clientX, clientY: event?.clientY, dragged: start?.dragged ?? false,
            suppressed: suppressMouseClick, reason, x, y }));
    }

    function resetStagePick(reason, event) {
        trace('reset', event, reason);
        const screen = heroesPanel.dataset.screen;
        if (!heroesPanel.hidden && (screen === 'roster' || screen === 'class')) {
            host.CharacterCommand('ResetStagePick', -1, '');
        }
    }

    function pickStage(event) {
        const screen = heroesPanel.dataset.screen;
        if (heroesPanel.hidden || screen !== 'class' && screen !== 'roster') {
            trace('ignored', event, 'screen');
            return false;
        }
        const rect = canvas.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) {
            trace('ignored', event, 'canvas-size');
            return false;
        }
        const x = (event.clientX - rect.left) / rect.width;
        const y = (event.clientY - rect.top) / rect.height;
        trace('submitted', event, 'pick', x, y);
        previousPickMs = event.timeStamp;
        host.PickOnStage(screen, x, y, event.timeStamp / 1000);
        return true;
    }

    canvas.addEventListener('click', event => {
        trace('click', event, 'received');
        if (event.button !== 0 || event.pointerType === 'touch') {
            trace('ignored', event, 'button-or-touch');
            return;
        }
        if (suppressMouseClick) {
            trace('ignored', event, 'suppressed');
            suppressMouseClick = false;
            return;
        }
        pickStage(event);
    });
    if (profile) canvas.addEventListener('dblclick', event => trace('dblclick', event, 'browser-recognized'));
    canvas.addEventListener('pointerdown', event => {
        trace('pointerdown', event, 'received');
        if (!event.isPrimary || event.button !== 0) {
            if (start) start.dragged = true;
            resetStagePick('non-primary', event);
            return;
        }
        suppressMouseClick = false;
        start = { id: event.pointerId, x: event.clientX, y: event.clientY, dragged: false };
        if (event.pointerType === 'touch') {
            event.preventDefault();
            canvas.setPointerCapture(event.pointerId);
        }
    });
    window.addEventListener('pointermove', event => {
        if (start?.id === event.pointerId && Math.hypot(event.clientX - start.x, event.clientY - start.y) > 12) {
            start.dragged = true;
        }
    });
    canvas.addEventListener('pointercancel', event => {
        trace('pointercancel', event, 'received');
        start = undefined;
        suppressMouseClick = true;
        resetStagePick('cancel', event);
    });
    canvas.addEventListener('pointerup', event => {
        trace('pointerup', event, 'received');
        if (start?.id !== event.pointerId) return;
        const dragged = start.dragged || Math.hypot(event.clientX - start.x, event.clientY - start.y) > 12;
        start = undefined;
        if (dragged) {
            suppressMouseClick = true;
            resetStagePick('drag', event);
            return;
        }
        if (event.pointerType !== 'touch') return;

        canvas.focus({ preventScroll: true });
        if (pickStage(event)) return;
        const position = { clientX: event.clientX, clientY: event.clientY, button: 0, bubbles: true };
        canvas.dispatchEvent(new MouseEvent('mousedown', { ...position, buttons: 1 }));
        requestAnimationFrame(() => requestAnimationFrame(() => {
            canvas.dispatchEvent(new MouseEvent('mouseup', position));
        }));
    });
}

async function createMainGraphics(targetCanvas, options) {
    const { createThreeGraphics } = await import('./three-scene.js');
    return createThreeGraphics(targetCanvas, options);
}

async function createWorkerDevice(options) {
    const { loadWorkerSmaaLookups } = await import('./three-worker-assets.js');
    const lookups = await loadWorkerSmaaLookups();
    try {
        return await createWorkerGraphics(canvas, { ...options, workerInit: lookups.workerInit,
            workerInitTransferList: lookups.transferList });
    } catch (error) {
        lookups.release();
        throw error;
    }
}

// Three owns rendering, including the native UI batches. The C# runtime keeps CPU resources and game state.
async function createDevice() {
    const diagnostics = new URLSearchParams(location.search);
    const options = { forceWebGL: diagnostics.get('renderer') === 'webgl2', profile: diagnostics.has('profile'),
        textureArrays: diagnostics.get('texture-arrays') !== '0',
        cutoutArrays: diagnostics.get('profile') === '1' && diagnostics.get('cutout-arrays') === '1',
        internalPost: diagnostics.get('internal-post') === '0' ? false
            : diagnostics.get('profile') === '1' && diagnostics.get('internal-post') === '1' ? true : 'auto',
        fusedSharpen: diagnostics.get('fused-sharpen') === '0' ? false
            : diagnostics.get('profile') === '1' && diagnostics.get('fused-sharpen') === '1' ? true : 'auto',
        zeroPointLights: diagnostics.get('zero-point-lights') === '0' ? false
            : diagnostics.get('profile') === '1' && diagnostics.get('zero-point-lights') === '1' ? true : 'auto',
        stageSkinCulling: diagnostics.get('stage-culling') !== '0',
        renderBundles: diagnostics.get('profile') === '1' && diagnostics.get('render-bundles') === '1',
        staticArrayObservers: diagnostics.get('profile') === '1' && diagnostics.get('observer-cache') === '1',
        frameSlots: diagnostics.get('frame-slots') === '3' ? 3 : 4,
        gpuTiming: diagnostics.get('profile') === '1' && diagnostics.get('gpu-timing') === '1' };
    if (diagnostics.get('render-worker') !== '0' && typeof Worker === 'function'
        && typeof canvas.transferControlToOffscreen === 'function') {
        try {
            return await createWorkerDevice(options);
        } catch (error) {
            const replacement = document.createElement('canvas');
            for (const attribute of canvas.attributes) {
                if (!attribute.name.startsWith('data-renderer') && !attribute.name.startsWith('data-webgpu')) {
                    replacement.setAttribute(attribute.name, attribute.value);
                }
            }
            canvas.replaceWith(replacement);
            canvas = replacement;
            worldHud.setCanvas(canvas);
            console.warn('A renderização paralela ficou indisponível. Usando o renderizador da página:', error.message ?? String(error));
        }
    }
    return createMainGraphics(canvas, options);
}

function enableTextureArrayBenchmark(graphics, resetProfiles, diagnostics = false) {
    if (!diagnostics) return;
    const initial = graphics.getProfile();
    if (!graphics.textureArraysRequested && !initial?.renderBundles?.requested
        && !initial?.staticArrayObservers?.requested && !initial?.gpu?.requested && !initial?.zeroPointLights?.requested
        && !initial?.internalPost?.requested) return;
    const panel = document.createElement('section');
    panel.setAttribute('aria-label', 'Teste de desempenho');
    Object.assign(panel.style, { position: 'absolute', top: '12px', left: '50%', transform: 'translateX(-50%)',
        zIndex: '1000', padding: '10px 12px', border: '1px solid #a58b4e', borderRadius: '6px',
        background: 'rgba(15,19,25,.94)', color: '#ece3cc', font: '12px sans-serif', textAlign: 'center' });
    const title = document.createElement('strong');
    title.textContent = 'Teste de desempenho';
    title.style.display = 'block';
    title.style.marginBottom = '7px';
    panel.append(title);
    const controls = [];
    function addToggle(kind, labels, supported, enabled, apply, describe) {
        const row = document.createElement('div');
        row.style.marginTop = '7px';
        const plain = document.createElement('button');
        const grouped = document.createElement('button');
        plain.type = grouped.type = 'button';
        plain.textContent = labels[0];
        grouped.textContent = labels[1];
        for (const button of [plain, grouped]) {
            Object.assign(button.style, { padding: '6px 9px', margin: '0 3px', border: '1px solid #a58b4e',
                borderRadius: '3px', color: '#ece3cc', cursor: 'pointer', font: 'inherit' });
            button.addEventListener('keydown', event => event.stopPropagation());
        }
        const status = document.createElement('div');
        status.setAttribute('role', 'status');
        status.style.marginTop = '5px';
        const refresh = census => {
            const active = enabled(census);
            plain.setAttribute('aria-pressed', String(!active));
            grouped.setAttribute('aria-pressed', String(active));
            plain.style.background = active ? '#1c232c' : '#725b2e';
            grouped.style.background = active ? '#725b2e' : '#1c232c';
            grouped.disabled = !supported(census);
            status.textContent = describe(active, census);
        };
        const select = async active => {
            try {
                const applied = apply(active);
                if (applied && typeof applied.then === 'function') await applied;
                if (graphics.workerRendering) await graphics.requestProfile();
                const profile = resetProfiles();
                for (const control of controls) control.refresh(profile?.[control.kind]);
                console.info('[' + (kind === 'renderBundles' ? 'RenderBundleBenchmark'
                    : kind === 'zeroPointLights' ? 'ZeroPointLightsBenchmark'
                    : kind === 'internalPost' ? 'InternalPostBenchmark' : 'TextureArrayBenchmark') + '] ' + JSON.stringify(profile?.[kind]));
            } catch (error) {
                status.textContent = 'Falha ao alternar: ' + error.message;
            }
        };
        plain.addEventListener('click', () => select(false));
        grouped.addEventListener('click', () => select(true));
        row.append(plain, grouped, status);
        panel.append(row);
        controls.push({ kind, refresh });
        refresh(initial?.[kind]);
    }
    if (graphics.textureArraysRequested) addToggle('textureArrays', ['Sem agrupamento', 'Com agrupamento'],
        () => graphics.textureArrayLayers >= 2, () => graphics.textureArraysEnabled, active => graphics.setTextureArraysEnabled(active),
        (active, census) => (active ? 'Agrupamento ligado' : 'Agrupamento desligado')
            + (census ? ` · ${census.outputObjects}/${census.baselineObjects} objetos` : ''));
    if (initial?.renderBundles?.requested) addToggle('renderBundles', ['Sem pacotes', 'Com pacotes'],
        census => !!census?.supported, census => !!census?.enabled, active => graphics.setRenderBundlesEnabled(active),
        (active, census) => (active ? 'Pacotes ligados' : 'Pacotes desligados') + (census ? ` · ${census.chunks} blocos` : ''));
    if (initial?.staticArrayObservers?.requested) addToggle('staticArrayObservers', ['Materiais originais', 'Materiais otimizados'],
        census => !!census?.requested, census => !!census?.enabled, active => graphics.setStaticArrayObserversEnabled(active),
        (active, census) => (active ? 'Atualização seletiva ligada' : 'Atualização seletiva desligada')
            + (census ? ` · ${census.branded} materiais` : ''));
    if (initial?.zeroPointLights?.requested) addToggle('zeroPointLights', ['Luzes originais', 'Luzes otimizadas'],
        census => !!census?.requested, census => !!census?.enabled, active => graphics.setZeroPointLightsEnabled(active),
        active => active ? 'Descarte de contribuição zero ligado' : 'Descarte de contribuição zero desligado');
    if (initial?.internalPost?.requested) addToggle('internalPost', ['Pós original', 'Pós interno'],
        census => !!census?.requested, census => !!census?.enabled, active => graphics.setInternalPostEnabled(active),
        (active, census) => (active ? 'Pós interno ligado' : 'Pós original')
            + (census?.width ? ` · ${census.width}×${census.height}` : ''));
    if (initial?.gpu?.requested) {
        const row = document.createElement('div');
        row.style.marginTop = '7px';
        const status = document.createElement('div');
        status.setAttribute('role', 'status');
        const choices = [['off', 'GPU desligada'], ['sampled', 'GPU por amostra'], ['continuous', 'GPU contínua']];
        const buttons = [];
        const refresh = census => {
            for (const [mode, button] of buttons) {
                const active = census?.mode === mode;
                button.setAttribute('aria-pressed', String(active));
                button.style.background = active ? '#725b2e' : '#1c232c';
                button.disabled = mode !== 'off' && !census?.supported;
            }
            status.textContent = 'Temporização GPU: ' + (census?.mode === 'continuous' ? 'todos os quadros'
                : census?.mode === 'sampled' ? '1 a cada 10 quadros' : 'desligada');
        };
        for (const [mode, label] of choices) {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = label;
            Object.assign(button.style, { padding: '6px 9px', margin: '0 3px', border: '1px solid #a58b4e',
                borderRadius: '3px', color: '#ece3cc', cursor: 'pointer', font: 'inherit' });
            button.addEventListener('keydown', event => event.stopPropagation());
            button.addEventListener('click', async () => {
                try {
                    await graphics.setGpuTimingMode(mode);
                    const profile = resetProfiles();
                    for (const control of controls) control.refresh(profile?.[control.kind]);
                    console.info('[GpuTimingBenchmark] ' + JSON.stringify(profile?.gpu));
                } catch (error) {
                    status.textContent = 'Falha na temporização: ' + error.message;
                }
            });
            row.append(button);
            buttons.push([mode, button]);
        }
        row.append(status);
        panel.append(row);
        controls.push({ kind: 'gpu', refresh });
        refresh(initial.gpu);
    }
    root.append(panel);
}

showLoading(text.loadingClient);
try {
    const graphics = await createDevice();
    activeGraphics = graphics;
    if (!graphics) {
        showLoading(text.noWebGpu, 'error');
        showMessage(text.noWebGpu, 'error');
    } else {
        const clientAddress = new URL('./', import.meta.url);
        const cacheReady = startPublicAssetCache(clientAddress.href);
        const { dotnet } = await import("./_framework/dotnet.js");
        const runtime = await dotnet
            .withModuleConfig({ noxiusThreeGraphics: graphics, noxiusAudio: createWebAudio() })
            .withConfig({ loadAllSatelliteResources: true, maxParallelDownloads: 4 })
            .create();
        runtime.setModuleImports('noxius-three-scene', graphics.workerRendering ? createWorkerPackedImports(graphics) : createPackedImports(graphics));
        if (graphics.adapterInfo) settings.setAdapterInfo(graphics.adapterInfo);
        settings.setRendererCapabilities(graphics.getCapabilities());
        runtime.setModuleImports('avalon-page', {
            reportLoginProgress(done, total) {
                const percent = total ? Math.floor(100 * done / total) : 100;
                root.dataset.progress = String(percent);
                if (root.dataset.state === 'loading') progress.value = percent;
            },
            reportLogin,
            reportCharacters(state) {
                heroes.report(state);
                if (!part('heroes').hidden) {
                    adaptiveWorldReady = false;
                    worldHud.hide();
                }
            },
            reportWorldLoading,
            openWorldSettings() {
                queueMicrotask(() => part('settings-open')?.click());
            },
            openNews() {
                queueMicrotask(() => news.open());
            },
            showCommunityLink(address) {
                // The game asks on the click that confirms it, within the browser's user activation, so the tab opens at once.
                const opened = window.open(address, '_blank');
                if (opened) opened.opener = null;
            }
        });
        const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
        host = exports.PristonTale.Browser.BrowserHost;
        settings.start(host);
        worldHud.enable(host);
        // Read-only diagnostic for the live opening's position.
        window.avalonOpeningSeconds = () => host.OpeningSeconds();
        // How long each part of the start takes, logged once the scene is up (the console's "[Partida]" line).
        const phases = { runtime: Math.round(performance.now()) };
        showLoading(text.loadingFiles);
        publicAssetCache = await cacheReady;
        await host.PrepareAsync(clientAddress.href, selectedLanguage);
        phases.files = Math.round(performance.now());
        // The right button belongs to the game.
        canvas.addEventListener('contextmenu', event => event.preventDefault());
        started = true;
        host.Start(root.dataset.endpoint, selectedLanguage, location.origin);
        const profileFrames = new URLSearchParams(location.search).has('profile');
        if (profileFrames) host.EnableFrameProfiling();
        configureWeatherDiagnostic(host);
        phases.start = Math.round(performance.now());
        enableCanvasPicking(profileFrames);
        // The files up to the character list wait until the opening has run smoothly for a moment: fetched and written
        // on the game's thread, they would otherwise stall its first seconds.
        const remainingDelayMs = 1000;
        let drawn = 0;
        let lastLimit = 0;
        let nextFrameTime = 0;
        let nextProfileTime = 0;
        let openingShown = false;
        let frameScheduled = false;
        let waitingForSlot = false;
        let profilePending = false;
        let frameStopped = false;
        enableTextureArrayBenchmark(graphics, () => {
            host.EnableFrameProfiling();
            const profile = graphics.getProfile();
            nextProfileTime = performance.now() + 5000;
            return profile;
        }, profileFrames);
        const stopFrames = error => {
            if (frameStopped) return;
            frameStopped = true;
            console.error('Falha durante o jogo:', error);
            reportWorldLoading('cancelled');
            root.dataset.state = 'failed';
            showLoading(text.stopped(error), 'error');
        };
        const requestFrame = () => {
            if (frameStopped || frameScheduled) return;
            frameScheduled = true;
            requestAnimationFrame(frame);
        };
        const showOpening = () => {
            if (openingShown || drawn < 4) return;
            openingShown = true;
            loading.hidden = true;
            setTimeout(() => {
                revealLogin();
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    loginPainted = true;
                    prefetchCharacters();
                }));
            }, loginDelayMs);
            setTimeout(fetchRemaining, remainingDelayMs);
            phases.scene = Math.round(performance.now());
            console.info(`[Partida] runtime ${phases.runtime} ms; arquivos da cena +${phases.files - phases.runtime} ms; `
                + `início do jogo +${phases.start - phases.files} ms; primeiros quadros +${phases.scene - phases.start} ms; total ${phases.scene} ms`);
            root.dataset.state = 'running';
        };
        const frame = time => {
            frameScheduled = false;
            if (frameStopped) return;
            if (graphics.workerRendering) drawn = graphics.workerStats.completedFrames;
            showOpening();
            const limit = drawn >= 4 ? settings.maxFps : 0;
            if (lastLimit !== limit) {
                lastLimit = limit;
                nextFrameTime = time;
            }
            if (limit > 0) {
                if (time + 0.75 < nextFrameTime) {
                    requestFrame();
                    return;
                }
                nextFrameTime = Math.max(nextFrameTime + 1000 / limit, time);
            }
            try {
                if (graphics.workerRendering && !graphics.canAdvanceFrame) {
                    if (!waitingForSlot) {
                        waitingForSlot = true;
                        graphics.waitForFrameSlot().then(() => {
                            waitingForSlot = false;
                            requestFrame();
                        }, stopFrames);
                    }
                    return;
                }
                host.Frame();
                if (graphics.workerRendering) fenceWorldReady(graphics, stopFrames);
                worldHud.frame(time);
                if (profileFrames && !profilePending && time >= nextProfileTime) {
                    nextProfileTime = time + 5000;
                    console.info('[FrameProfile] ' + host.FrameProfile());
                    if (graphics.workerRendering) {
                        profilePending = true;
                        graphics.requestProfile().then(profile => {
                            profilePending = false;
                            if (frameStopped) return;
                            settings.setRendererCapabilities(graphics.getCapabilities());
                            console.info('[ThreeProfile] ' + JSON.stringify(profile));
                        }, stopFrames);
                    } else console.info('[ThreeProfile] ' + JSON.stringify(graphics.getProfile()));
                    if (publicAssetCache) console.info('[AssetCacheProfile] ' + JSON.stringify(publicAssetCache.snapshot()));
                }
                if (graphics.workerRendering) drawn = graphics.workerStats.completedFrames;
                else drawn = graphics.stats.frames;
                applyMaxPixelRatio(graphics);
                updateAdaptiveResolution(graphics, time, drawn, stopFrames);
                showOpening();
                requestFrame();
            } catch (error) {
                stopFrames(error);
            }
        };
        requestFrame();
    }
} catch (error) {
    reportWorldLoading('cancelled');
    showLoading(text.failed(error), 'error');
}
