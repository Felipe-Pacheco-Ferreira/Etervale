// The login, world list and loading texts of the page (main.js), per language; other languages are in languages/<code>.js.
export const loginTexts = {
    'pt-BR': {
        loadingClient: 'Carregando o jogo…',
        loadingFiles: 'Carregando o cenário…',
        loadingMap: 'Preparando o mapa',
        mapProgress: 'Carregamento do mapa',
        loadingWorld: 'Carregando...',
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
        loadingMap: 'Preparing the map',
        mapProgress: 'Map loading',
        loadingWorld: 'Loading...',
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
