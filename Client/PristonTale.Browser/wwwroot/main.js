// Starts the browser client on the website's entry page ("/"). The page owns the markup (#jogo with the full-window
// canvas, the login form, the language picker and the progress line) and passes the game server's WebSocket address
// in data-endpoint. This module starts loading as soon as the page opens: it creates the WebGPU device, loads the .NET
// runtime, fetches the files the game reads and runs one game frame per animation frame. The login form is the page's:
// what is typed goes to BrowserHost.SubmitLogin, and the game reports each step back through reportLogin.

import { createHeroes } from "./heroes.js";

const root = document.getElementById("jogo");
const canvas = root.querySelector('canvas');
const part = role => root.querySelector(`[data-role="${role}"]`);
const loading = part('loading');
const progress = part('progress');
const statusLine = part('status');
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

const texts = {
    'pt-BR': {
        loadingClient: 'Carregando o jogo…',
        loadingFiles: 'Carregando o cenário…',
        openingMusic: 'Música da abertura',
        noWebGpu: 'Este navegador não tem WebGPU. Use um Chrome, Edge ou Safari recente.',
        failed: error => `O jogo não carregou: ${error}`,
        stopped: error => `O jogo parou: ${error}`,
        missing: 'Digite sua conta e sua senha.',
        accountPlaceholder: 'Sua ID',
        invalidCredentials: 'Conta ou senha incorreta.',
        sessionLimited: 'Muitas tentativas. Aguarde e tente novamente.',
        sessionUnavailable: 'Não foi possível acessar a sessão. Tente novamente.',
        sessionExpired: 'Sua sessão expirou. Entre com sua senha.',
        forgetAccount: 'Esquecer conta',
        recoveryUnavailable: 'O envio por e-mail ainda está indisponível.',
        filesLoadFailed: 'Não foi possível carregar a seleção. Escolha o mundo novamente.',
        recovery: {
            id: { label: 'E-mail', placeholder: 'voce@exemplo.com', submit: 'Enviar',
                info: 'Digite o e-mail da sua conta. Enviaremos a ID cadastrada com ele.' },
            password: { label: 'ID ou e-mail', placeholder: 'Sua ID ou e-mail', submit: 'Enviar código',
                info: 'Digite sua ID ou o e-mail da conta. Enviaremos um código de 6 dígitos para o e-mail cadastrado.' }
        },
        community: 'Abrir página do Avalon',
        accountLine: name => `Conta ${name}`,
        worldsFailed: 'Não foi possível carregar a lista de mundos.',
        checking: 'Verificando…',
        offline: 'Offline',
        worldDetail: (ping, players, capacity) => `Online · ${ping} ms · ${capacity ? `${players} / ${capacity}` : players} jogadores`,
        labels: {
            title: 'Bem-vindo', account: 'Conta', password: 'Senha', remember: 'Lembrar conta', submit: 'Entrar',
            forgotPassword: 'Esqueci a senha', forgotId: 'Esqueci o ID', register: 'Criar conta', leave: 'Sair',
            install: 'Instalar no PC', language: 'Idioma', servers: 'Servidores', chooseWorld: 'Escolha o mundo',
            switchAccount: 'Trocar de conta', recentAccounts: 'Contas recentes', connected: 'Conectado',
            stayConnected: 'Manter conectado', recoverAccount: 'Recuperar conta', cancel: 'Cancelar'
        }
    },
    en: {
        loadingClient: 'Loading the game…',
        loadingFiles: 'Loading the scene…',
        openingMusic: 'Opening music',
        noWebGpu: 'This browser has no WebGPU. Use a recent Chrome, Edge or Safari.',
        failed: error => `The game did not load: ${error}`,
        stopped: error => `The game stopped: ${error}`,
        missing: 'Enter your account and password.',
        accountPlaceholder: 'Your ID',
        invalidCredentials: 'Incorrect account or password.',
        sessionLimited: 'Too many attempts. Wait and try again.',
        sessionUnavailable: 'Could not access the session. Try again.',
        sessionExpired: 'Your session expired. Sign in with your password.',
        forgetAccount: 'Forget account',
        recoveryUnavailable: 'Email delivery is currently unavailable.',
        filesLoadFailed: 'Could not load character selection. Choose a world to retry.',
        recovery: {
            id: { label: 'Email', placeholder: 'you@example.com', submit: 'Send',
                info: 'Enter your account email. We’ll send the ID registered to that address.' },
            password: { label: 'ID or email', placeholder: 'Your ID or email', submit: 'Send code',
                info: 'Enter your ID or account email. We’ll send a 6-digit code to your registered email.' }
        },
        community: 'Open Avalon page',
        accountLine: name => `Account ${name}`,
        worldsFailed: 'The world list did not load.',
        checking: 'Checking…',
        offline: 'Offline',
        worldDetail: (ping, players, capacity) => `Online · ${ping} ms · ${capacity ? `${players} / ${capacity}` : players} players`,
        labels: {
            title: 'Welcome', account: 'Account', password: 'Password', remember: 'Remember account', submit: 'Log in',
            forgotPassword: 'Forgot password', forgotId: 'Forgot ID', register: 'Create account', leave: 'Leave',
            install: 'Install on PC', language: 'Language', servers: 'Servers', chooseWorld: 'Choose your world',
            switchAccount: 'Switch account', recentAccounts: 'Recent accounts', connected: 'Connected',
            stayConnected: 'Stay signed in', recoverAccount: 'Recover account', cancel: 'Cancel'
        }
    }
};

const savedLanguage = load(storageKeys.language);
let selectedLanguage = savedLanguage in texts ? savedLanguage : 'pt-BR';
let text = texts[selectedLanguage];
let started = false;

function applyLanguage() {
    text = texts[selectedLanguage];
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
    language.value = selectedLanguage;
    language.addEventListener('change', () => {
        store(storageKeys.language, language.value);
        // The game takes its language when it starts; once it runs, a new language needs a fresh start.
        if (started) {
            location.reload();
            return;
        }

        selectedLanguage = language.value;
        applyLanguage();
    });
}
applyLanguage();

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

if (openingMusic && openingSound) {
    openingSound.hidden = false;
    openingMusic.volume = 0.6;
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
    void playOpeningMusic();
}

function showLoading(line, tone = 'busy', percent = null) {
    if (!loading) return;
    loading.hidden = false;
    loading.dataset.tone = tone;
    statusLine.textContent = line;
    if (percent === null) progress.removeAttribute('value');
    else progress.value = percent;
    progress.hidden = tone === 'error';
}

function showMessage(line, tone = 'info') {
    message.textContent = line;
    message.dataset.tone = tone;
}

// The login: the form appears when the game reports its login screen (after the camera's descent); the account and
// password then lead to the world list, and choosing a world logs in on its game server. A choice made before the files
// up to the character list are in (FetchRemainingAsync) waits and goes in once they are.
let host = null;
let gameReady = false;
let filesReady = false;
let filesRequest = null;
let credentials = null;
let accountName = "";

// The character screen's panels (heroes.js): its commands go to the game after the current frame returns.
const heroes = createHeroes(root, new URL('./', import.meta.url).href,
    (kind, index = 0, value = '') => setTimeout(() => host?.CharacterCommand(kind, index, value)),
    () => selectedLanguage, () => accountName);
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
    for (const other of worldList.querySelectorAll('button')) other.disabled = true;
    pending = { selected: credentials, endpoint: world.endpoint };
    showWorldsMessage('');
    if (!sendPending()) fetchRemaining();
}

function reportLogin(state, line) {
    if (state === 'form') {
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

function enableTouchTaps() {
    let start;
    canvas.addEventListener('pointerdown', event => {
        if (event.pointerType !== 'touch' || !event.isPrimary) return;
        event.preventDefault();
        start = { id: event.pointerId, x: event.clientX, y: event.clientY };
        canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointercancel', () => { start = undefined; });
    canvas.addEventListener('pointerup', event => {
        if (start?.id !== event.pointerId) return;
        const distance = Math.hypot(event.clientX - start.x, event.clientY - start.y);
        start = undefined;
        if (distance > 12) return;

        canvas.focus({ preventScroll: true });
        const position = { clientX: event.clientX, clientY: event.clientY, button: 0, bubbles: true };
        canvas.dispatchEvent(new MouseEvent('mousedown', { ...position, buttons: 1 }));
        requestAnimationFrame(() => requestAnimationFrame(() => {
            canvas.dispatchEvent(new MouseEvent('mouseup', position));
        }));
    });
}

// Device creation is asynchronous in the browser, so it happens here, before the runtime starts; Emscripten hands the
// device to the MonoGame runtime (preinitializedWebGPUDevice).
async function createDevice() {
    const adapter = navigator.gpu ? await navigator.gpu.requestAdapter() : null;
    if (!adapter) {
        return null;
    }

    const requiredFeatures = adapter.features.has("float32-filterable") ? ["float32-filterable"] : [];
    const device = await adapter.requestDevice({ requiredFeatures });
    const errors = new Set();
    device.addEventListener("uncapturederror", ({ error }) => {
        if (errors.size < 40) errors.add(error.message);
        canvas.dataset.webgpuErrors = JSON.stringify([...errors]);
    });
    device.addEventListener('uncapturederror', event => console.error(`WebGPU: ${event.error.message}`));
    return device;
}

showLoading(text.loadingClient);
try {
    const device = await createDevice();
    if (!device) {
        showLoading(text.noWebGpu, 'error');
        showMessage(text.noWebGpu, 'error');
    } else {
        const { dotnet } = await import("./_framework/dotnet.js");
        const runtime = await dotnet
            .withModuleConfig({ preinitializedWebGPUDevice: device })
            .withConfig({ loadAllSatelliteResources: true, maxParallelDownloads: 4 })
            .create();
        runtime.setModuleImports('avalon-page', {
            reportLoginProgress(done, total) {
                const percent = total ? Math.floor(100 * done / total) : 100;
                root.dataset.progress = String(percent);
                if (root.dataset.state === 'loading') progress.value = percent;
            },
            reportLogin,
            reportCharacters: state => heroes.report(state),
            showCommunityLink(address) {
                let link = root.querySelector('[data-community-link]');
                if (!link) {
                    link = document.createElement('a');
                    link.className = 'game-community';
                    link.dataset.communityLink = '';
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    root.append(link);
                }
                link.textContent = text.community;
                link.href = address;
                link.focus();
            }
        });
        const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
        host = exports.PristonTale.Browser.BrowserHost;
        // Read-only diagnostic for the live opening's position.
        window.avalonOpeningSeconds = () => host.OpeningSeconds();
        // How long each part of the start takes, logged once the scene is up (the console's "[Partida]" line).
        const phases = { runtime: Math.round(performance.now()) };
        showLoading(text.loadingFiles);
        await host.PrepareAsync(new URL('./', import.meta.url).href);
        phases.files = Math.round(performance.now());
        // The right button belongs to the game.
        canvas.addEventListener('contextmenu', event => event.preventDefault());
        started = true;
        host.Start(root.dataset.endpoint, selectedLanguage, location.origin);
        phases.start = Math.round(performance.now());
        enableTouchTaps();
        // The files up to the character list wait until the opening has run smoothly for a moment: fetched and written
        // on the game's thread, they would otherwise stall its first seconds.
        const remainingDelayMs = 1000;
        let drawn = 0;
        const frame = () => {
            try {
                host.Frame();
                drawn++;
                if (drawn === 4) {
                    loading.hidden = true;
                    setTimeout(revealLogin, loginDelayMs);
                    setTimeout(fetchRemaining, remainingDelayMs);
                    phases.scene = Math.round(performance.now());
                    console.info(`[Partida] runtime ${phases.runtime} ms; arquivos da cena +${phases.files - phases.runtime} ms; `
                        + `início do jogo +${phases.start - phases.files} ms; primeiros quadros +${phases.scene - phases.start} ms; total ${phases.scene} ms`);
                    root.dataset.state = 'running';
                }
                requestAnimationFrame(frame);
            } catch (error) {
                console.error('Falha durante o jogo:', error);
                root.dataset.state = 'failed';
                showLoading(text.stopped(error), 'error');
            }
        };
        requestAnimationFrame(frame);
    }
} catch (error) {
    showLoading(text.failed(error), 'error');
}
