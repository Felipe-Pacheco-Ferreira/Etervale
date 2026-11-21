// The character screen's panels (the page's #jogo [data-role=heroes]): the list ("Seus heróis") and the creation's
// steps. CharacterSelectScreen.Web.cs reports its state as JSON (report below) whenever it changes; the panels are drawn
// again from it, and the player's choices go back as BrowserHost.CharacterCommand(kind, index, text).

import { table } from './page-language.js';

const classTexts = {
    'pt-BR': {
        Fighter: ['Corpo a corpo · Dano', 'Guerreiro de força bruta, mestre de machados e espadas pesadas. Aguenta a linha de frente e esmaga quem cruzar o seu caminho.'],
        Mechanician: ['Corpo a corpo · Defesa', 'Engenhoso e resistente, combina armaduras pesadas e garras mecânicas para proteger o grupo.'],
        Archer: ['Distância · Precisão', 'Atiradora certeira que derruba os inimigos antes que cheguem perto, com flechas rápidas e mortais.'],
        Pikeman: ['Corpo a corpo · Área', 'Lanceiro que gira a arma longa e atinge vários inimigos de uma vez.'],
        Knight: ['Corpo a corpo · Sagrado', 'Cavaleiro devoto que une espada e fé, firme na defesa e implacável contra mortos-vivos.'],
        Atalanta: ['Distância · Arremesso', 'Guerreira ágil que arremessa dardos e lanças encantadas com fúria e graça.'],
        Priestess: ['Magia · Suporte', 'Sacerdotisa que cura os aliados, fortalece o grupo e pune os inimigos com luz.'],
        Magician: ['Magia · Dano', 'Mago que domina os elementos e destrói grupos inteiros à distância.'],
        Assassin: ['Corpo a corpo · Precisão', 'Ataca com duas lâminas e usa sua agilidade para desferir golpes rápidos.'],
        Shaman: ['Magia · Suporte', 'Canaliza o poder dos espíritos para atacar inimigos e fortalecer os aliados.'],
        MartialArtist: ['Corpo a corpo · Dano', 'Usa manoplas e técnicas marciais para encadear golpes de curta distância.']
    },
    en: {
        Fighter: ['Melee · Damage', 'A warrior of raw strength, master of axes and heavy swords, who holds the front line.'],
        Mechanician: ['Melee · Defense', 'Ingenious and tough, combining heavy armor and mechanical claws to shield the group.'],
        Archer: ['Ranged · Precision', 'A sharpshooter who brings enemies down before they come close.'],
        Pikeman: ['Melee · Area', 'A spearman who sweeps the long weapon to hit many enemies at once.'],
        Knight: ['Melee · Holy', 'A devout knight of sword and faith, steady in defense and relentless against the undead.'],
        Atalanta: ['Ranged · Throwing', 'An agile warrior who throws enchanted javelins with fury and grace.'],
        Priestess: ['Magic · Support', 'A priestess who heals allies, strengthens the group and smites enemies with light.'],
        Magician: ['Magic · Damage', 'A mage who commands the elements and destroys whole groups from afar.'],
        Assassin: ['Melee · Precision', 'An agile fighter who strikes quickly with a pair of blades.'],
        Shaman: ['Magic · Support', 'Channels the power of spirits to strike enemies and strengthen allies.'],
        MartialArtist: ['Melee · Damage', 'Uses gauntlets and martial arts to chain close-range strikes.']
    }
};

const texts = {
    'pt-BR': {
        heroes: 'Seus heróis', newCharacter: 'Novo personagem', chooseTribe: 'Escolha sua tribo', chooseClass: 'Escolha sua classe', appearance: 'Aparência e nome',
        account: name => `Conta ${name}`, characters: (count, max) => `Personagens ${count}/${max}`, level: 'Nível',
        create: 'Criar personagem', free: 'Vaga livre', cls: 'Classe', tribe: 'Tribo', style: 'Estilo', clan: 'Clã',
        weapon: 'Arma', armor: 'Armadura', offHand: 'Mão secundária', previousHero: 'Personagem anterior', nextHero: 'Próximo personagem',
        remove: 'Excluir', enter: 'Entrar no mundo', leave: 'Sair', back: 'Voltar', next: 'Próximo',
        steps: ['Tribo', 'Classe', 'Aparência'], name: 'Nome do personagem', namePlaceholder: 'Digite um nome',
        faceOption: index => `Rosto ${index}`, hint: 'Escolha o rosto e o nome do personagem', dragHint: 'Arraste o personagem para girá-lo',
        confirmRemove: name => `Excluir ${name}? Isso não pode ser desfeito.`,
        stats: ['Força', 'Espírito', 'Talento', 'Agilidade', 'Vitalidade'],
        tribes: {
            Tempskron: {
                motto: 'Tecnologia e aço',
                description: 'Os Tempskrons usam sua tecnologia avançada para criar máquinas e armamentos a favor do combate.',
                classes: ['Mecânico', 'Lutador', 'Piqueiro', 'Arqueira', 'Assassina', 'Artista Marcial']
            },
            Morion: {
                motto: 'Magia e fé',
                description: 'Os Morions dominam a magia e a fé, usando o poder divino e os feitiços arcanos a favor do combate.',
                classes: ['Cavaleiro', 'Atalanta', 'Sacerdotisa', 'Mago', 'Xamã']
            }
        },
        busy: { loading: 'Carregando os personagens…', preparing: 'Preparando as classes…', entering: 'Entrando no mundo…', deleting: 'Excluindo…', creating: 'Criando o personagem…' }
    },
    en: {
        heroes: 'Your heroes', newCharacter: 'New character', chooseTribe: 'Choose your tribe', chooseClass: 'Choose your class', appearance: 'Appearance and name',
        account: name => `Account ${name}`, characters: (count, max) => `Characters ${count}/${max}`, level: 'Level',
        create: 'Create character', free: 'Free slot', cls: 'Class', tribe: 'Tribe', style: 'Style', clan: 'Clan',
        weapon: 'Weapon', armor: 'Armor', offHand: 'Off hand', previousHero: 'Previous character', nextHero: 'Next character',
        remove: 'Delete', enter: 'Enter the world', leave: 'Leave', back: 'Back', next: 'Next',
        steps: ['Tribe', 'Class', 'Appearance'], name: 'Character name', namePlaceholder: 'Type a name',
        faceOption: index => `Face ${index}`, hint: 'Choose the face and the name', dragHint: 'Drag the character to rotate it',
        confirmRemove: name => `Delete ${name}? This cannot be undone.`,
        stats: ['Strength', 'Spirit', 'Talent', 'Agility', 'Vitality'],
        tribes: {
            Tempskron: {
                motto: 'Technology and steel',
                description: 'Tempskrons build machines and weapons with advanced technology to give them an edge in battle.',
                classes: ['Mechanician', 'Fighter', 'Pikeman', 'Archer', 'Assassin', 'Martial Artist']
            },
            Morion: {
                motto: 'Magic and faith',
                description: 'Morions bring both divine power and arcane spells to the battlefield.',
                classes: ['Knight', 'Atalanta', 'Priestess', 'Magician', 'Shaman']
            }
        },
        busy: { loading: 'Loading your characters…', preparing: 'Preparing the classes…', entering: 'Entering the world…', deleting: 'Deleting…', creating: 'Creating the character…' }
    }
};

const statMaximum = 30;
const classMarks = {
    Fighter: 'Ft', Mechanician: 'Mc', Archer: 'Ac', Pikeman: 'Pk', Knight: 'Kt', Atalanta: 'At',
    Priestess: 'Pt', Magician: 'Mg', Assassin: 'As', Shaman: 'Sh', MartialArtist: 'Ma'
};

// The client's face thumbnails are TGA files, which no browser shows; they are decoded here and drawn on canvases
// (the page's policy admits images from the site only, so no blob: or data: address is possible).
const decoded = new Map();
let itemIcons;

function decodeTga(buffer) {
    const bytes = new Uint8Array(buffer);
    const headerSize = 18;
    if (bytes.length < headerSize) return null;

    // Match AssetMask's XOR wrapper and the G8 thumbnail header in PristonImageDecoder.
    const isTrueColor = (colorMap, type) => colorMap === 0 && (type === 2 || type === 10);
    const mask = 0x82;
    if (!isTrueColor(bytes[1], bytes[2]) && isTrueColor(bytes[1] ^ mask, bytes[2] ^ mask)) {
        for (let i = 0; i < bytes.length; i++) bytes[i] ^= mask;
    }

    const maskedHeader = bytes[0] === 0x47 && bytes[1] === 0x38;
    if (maskedHeader) {
        for (let i = 0; i < headerSize; i++) bytes[i] -= i * i;
    }

    const idLength = bytes[0];
    const type = bytes[2];
    const width = bytes[12] | (bytes[13] << 8);
    const height = bytes[14] | (bytes[15] << 8);
    const depth = bytes[16];
    const topDown = (bytes[17] & 0x20) !== 0;
    const size = depth >> 3;
    if ((!maskedHeader && ![2, 10].includes(type)) || ![3, 4].includes(size) || !width || !height) return null;
    const count = width * height;
    const runLength = type === 10;
    let source = maskedHeader ? headerSize : headerSize + idLength + (bytes[1] ? (bytes[5] | (bytes[6] << 8)) * (bytes[7] >> 3) : 0);
    const minimumSize = runLength ? Math.ceil(count / 128) * (size + 1) : count * size;
    if (source + minimumSize > bytes.length) return null;
    const pixels = new Uint8ClampedArray(count * 4);
    const put = (index) => {
        const row = Math.floor(index / width);
        const column = index % width;
        const target = ((topDown ? row : height - 1 - row) * width + column) * 4;
        pixels[target] = bytes[source + 2];
        pixels[target + 1] = bytes[source + 1];
        pixels[target + 2] = bytes[source];
        pixels[target + 3] = size === 4 ? bytes[source + 3] : 255;
    };
    for (let index = 0; index < count;) {
        if (!runLength) {
            put(index++);
            source += size;
            continue;
        }

        const header = bytes[source++];
        const run = Math.min((header & 0x7f) + 1, count - index);
        const repeated = (header & 0x80) !== 0;
        if (source + (repeated ? 1 : run) * size > bytes.length) return null;
        if (repeated) {
            for (let repeat = 0; repeat < run; repeat++) put(index++);
            source += size;
        } else {
            for (let raw = 0; raw < run; raw++) {
                put(index++);
                source += size;
            }
        }
    }

    return new ImageData(pixels, width, height);
}

function decodeItemBmp(buffer) {
    const bytes = new Uint8Array(buffer);
    if (bytes.length < 54) return null;
    if (bytes[0] === 0x41 && bytes[1] === 0x38) {
        for (let i = 0; i < 14; i++) bytes[i] -= i * i;
    } else if (bytes[0] !== 0x42 || bytes[1] !== 0x4d) {
        return null;
    }

    const view = new DataView(buffer);
    const offset = view.getUint32(10, true);
    const width = view.getInt32(18, true);
    const signedHeight = view.getInt32(22, true);
    const height = Math.abs(signedHeight);
    const depth = view.getUint16(28, true);
    if (width <= 0 || width > 1024 || height === 0 || height > 1024 ||
        (depth !== 24 && depth !== 32) || view.getUint32(30, true) !== 0) return null;
    const stride = Math.ceil(width * depth / 32) * 4;
    if (offset < 54 || offset + height * stride > bytes.length) return null;

    const pixels = new Uint8ClampedArray(width * height * 4);
    const pixelSize = depth / 8;
    let hasCoverage = false;
    let hasTransparency = false;
    for (let y = 0; y < height; y++) {
        const row = offset + (signedHeight < 0 ? y : height - 1 - y) * stride;
        for (let x = 0; x < width; x++) {
            const from = row + x * pixelSize;
            const to = (y * width + x) * 4;
            pixels[to] = bytes[from + 2];
            pixels[to + 1] = bytes[from + 1];
            pixels[to + 2] = bytes[from];
            const alpha = pixelSize === 4 ? bytes[from + 3] : 255;
            pixels[to + 3] = alpha;
            hasCoverage ||= alpha > 0;
            hasTransparency ||= alpha < 255;
        }
    }

    // The HUD uses a black colour key when the BMP's reserved alpha channel carries no coverage.
    if (!hasCoverage || !hasTransparency) {
        for (let i = 0; i < pixels.length; i += 4) {
            pixels[i + 3] = pixels[i] || pixels[i + 1] || pixels[i + 2] ? 255 : 0;
        }
    }
    return new ImageData(pixels, width, height);
}

function drawAsset(canvas, address, decode) {
    if (!decoded.has(address)) {
        decoded.set(address, fetch(address)
            .then(response => response.ok ? response.arrayBuffer() : null)
            .then(buffer => buffer && decode(buffer)).catch(() => null));
    }
    return decoded.get(address).then(image => {
        if (!image) return false;
        canvas.width = image.width;
        canvas.height = image.height;
        canvas.getContext('2d')?.putImageData(image, 0, 0);
        return true;
    });
}

function itemIconIndex(base) {
    itemIcons ??= fetch(new URL('bootstrap.txt', base))
        .then(response => response.ok ? response.text() : '')
        .then(list => {
            const icons = new Map();
            for (const entry of list.trim().split('\n').sort()) {
                const [path, hash] = entry.trim().split('\t');
                const match = /^assets\/image\/Sinimage\/Items\/(?!DropItem\/)(?:[^/]+\/)*it([^/]+)\.bmp$/i.exec(path);
                if (match && !icons.has(match[1].toUpperCase())) {
                    const url = new URL(path, base);
                    if (hash) url.searchParams.set('v', hash);
                    icons.set(match[1].toUpperCase(), url.href);
                }
            }
            return icons;
        }).catch(() => new Map());
    return itemIcons;
}

function faceCanvas(address, base) {
    const canvas = document.createElement('canvas');
    canvas.width = 64;
    canvas.height = 64;
    const context = canvas.getContext('2d');
    if (!context) return canvas;
    context.fillStyle = 'rgba(232, 199, 125, 0.42)';
    context.beginPath();
    context.arc(32, 24, 8, 0, Math.PI * 2);
    context.moveTo(16, 48);
    context.quadraticCurveTo(18, 36, 32, 36);
    context.quadraticCurveTo(46, 36, 48, 48);
    context.closePath();
    context.fill();
    if (address) void drawAsset(canvas, new URL(address, base).href, decodeTga);
    return canvas;
}

function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

function button(className, label, action, disabled = false) {
    const node = element('button', className, label);
    node.type = 'button';
    node.disabled = disabled;
    node.addEventListener('click', action);
    return node;
}

function creationOrnament() {
    const ornament = element('span', 'creation-ornament');
    ornament.setAttribute('aria-hidden', 'true');
    return ornament;
}

export function createHeroes(root, base, send, language, accountName) {
    const part = role => root.querySelector(`[data-role="${role}"]`);
    const panel = part('heroes');
    const kicker = part('heroes-kicker');
    const title = part('heroes-title');
    const steps = part('heroes-steps');
    const rosterPanel = part('roster-panel');
    const infoPanel = part('info-panel');
    const tribeRow = part('tribe-row');
    const classRow = part('class-row');
    const appearancePanel = part('appearance-panel');
    const message = part('heroes-message');
    const actions = part('heroes-actions');
    const browse = element('div', 'hero-browse');
    browse.hidden = true;
    const previous = button('', '‹', () => browseRoster(-1));
    const next = button('', '›', () => browseRoster(1));
    browse.append(previous, next);
    panel.append(browse);
    const nameInput = element('input');
    nameInput.name = 'characterName';
    nameInput.maxLength = 16;
    nameInput.autocomplete = 'off';
    nameInput.spellcheck = false;

    let state = null;
    let classCardsKey = '';
    let departingControls;
    let enteringControls = [];
    let transitionTimer;

    const creationControls = {
        tribe: [tribeRow],
        class: [classRow, infoPanel],
        appearance: [appearancePanel]
    };

    const text = () => table(texts, 'heroes', language());
    const classText = cls => table(classTexts, 'heroClasses', language())[cls] ?? ['', ''];
    const busy = () => Boolean(state?.busy);

    function browseRoster(direction) {
        const count = state?.roster.length ?? 0;
        if (count < 2 || busy()) return;
        send('Select', (state.selected + direction + count) % count);
    }

    function drawSteps(current) {
        steps.replaceChildren();
        if (!current) return;
        text().steps.forEach((label, index) => {
            const item = element('li');
            item.dataset.state = index + 1 < current ? 'done' : index + 1 === current ? 'current' : 'next';
            if (index + 1 === current) item.setAttribute('aria-current', 'step');
            const mark = element('span', 'step-mark', index + 1 < current ? '✓' : String(index + 1));
            mark.setAttribute('aria-hidden', 'true');
            item.append(mark, label);
            steps.append(item);
        });
    }

    function drawRoster() {
        const t = text();
        rosterPanel.append(element('p', 'panel-heading', t.characters(state.roster.length, state.max)));
        state.roster.forEach((hero, index) => {
            const row = button('hero-row', '', () => send('Select', index), busy());
            row.setAttribute('aria-pressed', String(index === state.selected));
            row.addEventListener('dblclick', () => send('Enter', index));
            const face = faceCanvas(hero.face, base);
            face.className = 'hero-face';
            const name = element('span', 'hero-name', hero.name);
            name.append(element('small', '', hero.title));
            const level = element('span', 'hero-level', String(hero.level));
            level.append(element('small', '', t.level));
            const arrow = element('span', 'hero-go', '➜');
            arrow.setAttribute('aria-hidden', 'true');
            row.append(face, name, level, arrow);
            rosterPanel.append(row);
        });

        let free = state.max - state.roster.length;
        if (free > 0) {
            rosterPanel.append(button('hero-new', `+ ${t.create}`, () => send('NewCharacter'), busy()));
            free--;
        }

        for (; free > 0; free--) rosterPanel.append(element('div', 'hero-free', t.free));
    }

    function drawHeroInfo(hero) {
        const t = text();
        infoPanel.append(element('h2', 'info-name', hero.name), element('p', 'info-class', hero.title));
        const medal = element('div', 'info-medal', String(hero.level));
        medal.append(element('small', '', t.level));
        const medalRow = element('div', 'info-medal-row');
        medalRow.append(medal);
        if (classMarks[hero.cls]) {
            const mark = element('canvas', 'info-class-emblem');
            mark.setAttribute('aria-hidden', 'true');
            const path = `assets/image/Sinimage/skill/keep/${classMarks[hero.cls]}_R.tga`;
            void drawAsset(mark, new URL(path, base).href, decodeTga);
            medalRow.append(mark);
        }
        infoPanel.append(medalRow);
        const rows = element('dl', 'info-rows');
        for (const [label, value] of [[t.cls, hero.title], [t.tribe, hero.brood], [t.style, classText(hero.cls)[0]], [t.clan, hero.clan || '—']]) {
            rows.append(element('dt', '', label), element('dd', '', value));
        }

        infoPanel.append(rows);
        const equipment = element('div', 'info-equipment');
        const keys = hero.equipment ?? {};
        for (const [label, key] of [[t.weapon, keys.weapon], [t.armor, keys.armor], [t.offHand, keys.offHand]]) {
            const card = element('div', 'info-equipment-item');
            card.dataset.empty = String(!key);
            const iconWrap = element('span', 'info-equipment-icon', '—');
            iconWrap.setAttribute('aria-hidden', 'true');
            card.append(iconWrap, element('span', '', label));
            equipment.append(card);
            if (!key) continue;
            const icon = element('canvas');
            void itemIconIndex(base).then(async icons => {
                const address = icons.get(key.toUpperCase());
                if (address && await drawAsset(icon, address, decodeItemBmp)) iconWrap.replaceChildren(icon);
            });
        }
        infoPanel.append(equipment);
    }

    function drawClassInfo(job) {
        const t = text();
        const [style, description] = classText(job.cls);
        infoPanel.append(
            element('p', 'info-tribe', job.brood),
            element('h2', 'info-name', job.title),
            creationOrnament(),
            element('p', 'info-class', style),
            element('p', 'info-text', description));
        job.stats.forEach((value, index) => {
            const row = element('div', 'stat');
            const bar = element('span', 'stat-bar');
            const fill = element('span');
            fill.style.width = `${Math.min(100, Math.round(100 * value / statMaximum))}%`;
            bar.append(fill);
            row.append(element('span', '', t.stats[index]), bar, element('b', '', String(value)));
            infoPanel.append(row);
        });
    }

    function drawTribes() {
        const tribes = text().tribes;
        for (const [index, tribe] of ['Tempskron', 'Morion'].entries()) {
            const details = tribes[tribe];
            const card = button('card tribe', '', () => send('Tribe', index), busy());
            const mark = element('canvas', 'tribe-mark');
            mark.setAttribute('aria-hidden', 'true');
            const path = `assets/StartImage/login/CharSelect/mark_${index === 0 ? 'T' : 'M'}.tga`;
            void drawAsset(mark, new URL(path, base).href, decodeTga);
            const classes = element('span', 'tribe-classes');
            for (const name of details.classes) classes.append(element('span', '', name));
            card.append(
                mark,
                element('span', 'tribe-motto', details.motto),
                element('strong', 'tribe-name', tribe),
                creationOrnament(),
                element('span', 'tribe-description', details.description),
                classes);
            tribeRow.append(card);
        }
    }

    function drawClasses() {
        // A selection report updates the same buttons, preserving focus and the browser's double click target.
        const key = JSON.stringify([language(), state.jobs.map(job => [job.cls, job.title, job.faces[0]])]);
        if (key !== classCardsKey) {
            classCardsKey = key;
            classRow.replaceChildren(...state.jobs.map((job, index) => {
                const card = button('class-card', '', () => send('Class', index));
                card.addEventListener('dblclick', () => {
                    if (state?.screen === 'class' && !busy()) send('Next');
                });
                card.append(faceCanvas(job.faces[0], base), element('strong', '', job.title), element('small', '', classText(job.cls)[0]));
                return card;
            }));
        }

        for (const [index, card] of Array.from(classRow.children).entries()) {
            card.disabled = busy();
            card.setAttribute('aria-pressed', String(index === state.job));
        }
    }

    function drawAppearance(job) {
        const t = text();
        appearancePanel.append(element('p', 'info-tribe', job.brood), element('h2', 'info-name', job.title), creationOrnament());
        const faces = element('div', 'faces');
        job.faces.forEach((address, index) => {
            const face = button('face', '', () => send('Face', index), busy());
            face.setAttribute('aria-label', t.faceOption(index + 1));
            face.setAttribute('aria-pressed', String(index === state.face));
            face.append(faceCanvas(address, base));
            faces.append(face);
        });
        const field = element('label', 'field');
        nameInput.placeholder = t.namePlaceholder;
        field.append(element('span', '', t.name), nameInput);
        const form = element('form', 'creation-form');
        form.addEventListener('submit', event => {
            event.preventDefault();
            if (!busy()) send('Create', 0, nameInput.value.trim());
        });
        const create = element('button', 'card-button', t.create);
        create.type = 'submit';
        create.disabled = busy();
        form.append(field, create);
        const hint = element('p', 'heroes-hint');
        hint.append(element('span', 'heroes-hint-touch', t.hint), element('span', 'heroes-hint-mouse', t.dragHint));
        appearancePanel.append(faces, form, hint);
    }

    function finishCreationTransition() {
        clearTimeout(transitionTimer);
        departingControls?.remove();
        departingControls = null;
        for (const control of enteringControls) control.classList.remove('creation-controls-entering');
        enteringControls = [];
    }

    function transitionCreation(previousScreen, screen) {
        finishCreationTransition();
        const outgoing = creationControls[previousScreen];
        const incoming = creationControls[screen];
        if (!outgoing || !incoming || globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;

        // Keep outgoing canvases intact while the incoming controls fade in. The outgoing layer keeps its old layout.
        const layer = element('div', 'heroes creation-controls-exiting');
        layer.dataset.screen = previousScreen;
        layer.dataset.retainedLineup = panel.dataset.retainedLineup;
        layer.inert = true;
        layer.setAttribute('aria-hidden', 'true');
        for (const control of outgoing) {
            if (!control.childElementCount) continue;
            const shell = control.cloneNode();
            shell.removeAttribute('data-role');
            shell.append(...control.childNodes);
            layer.append(shell);
        }

        if (!layer.childElementCount) return;
        departingControls = layer;
        enteringControls = incoming;
        for (const control of enteringControls) control.classList.add('creation-controls-entering');
        panel.append(layer);
        transitionTimer = setTimeout(finishCreationTransition, 225);
    }

    function draw() {
        const t = text();
        const screen = state.screen;
        if (panel.dataset.screen !== screen) transitionCreation(panel.dataset.screen, screen);
        panel.dataset.screen = screen;
        panel.dataset.retainedLineup = String(screen === 'tribe' && state.retainedLineup === true);
        browse.hidden = screen !== 'roster' || state.roster.length < 2;
        previous.disabled = next.disabled = busy();
        previous.setAttribute('aria-label', t.previousHero);
        next.setAttribute('aria-label', t.nextHero);
        for (const container of [rosterPanel, infoPanel, tribeRow, appearancePanel, actions]) container.replaceChildren();
        if (screen !== 'class') {
            classRow.replaceChildren();
            classCardsKey = '';
        }
        const job = state.jobs[state.job];
        if (screen === 'roster') {
            kicker.textContent = t.account(accountName());
            title.textContent = t.heroes;
            drawSteps(0);
            drawRoster();
            const hero = state.roster[state.selected];
            if (hero) drawHeroInfo(hero);
            actions.append(
                button('hero-button hero-button-danger', t.remove, () => {
                    if (hero && confirm(t.confirmRemove(hero.name))) send('Delete');
                }, busy() || !hero),
                button('hero-button hero-button-primary', t.enter, () => send('Enter'), busy() || !hero),
                button('hero-button', t.leave, () => send('Exit'), busy()));
        } else if (screen === 'tribe') {
            kicker.textContent = t.newCharacter;
            title.textContent = t.chooseTribe;
            drawSteps(1);
            drawTribes();
            actions.append(button('hero-button', `‹ ${t.back}`, () => send('Back'), busy()));
        } else if (screen === 'class') {
            kicker.textContent = state.tribe;
            title.textContent = t.chooseClass;
            drawSteps(2);
            drawClasses();
            if (job) drawClassInfo(job);
            actions.append(
                button('hero-button', `‹ ${t.back}`, () => send('Back'), busy()),
                button('hero-button hero-button-next', `${t.next} ›`, () => send('Next'), busy() || !job));
        } else if (screen === 'appearance' && job) {
            kicker.textContent = job.title;
            title.textContent = t.appearance;
            drawSteps(3);
            drawAppearance(job);
            actions.append(button('hero-button', `‹ ${t.back}`, () => send('Back'), busy()));
        }

        const line = state.busy ? t.busy[state.busy] : state.message;
        message.textContent = line ?? '';
        message.dataset.tone = !state.busy && state.message ? 'error' : 'info';
    }

    return {
        report(json) {
            const next = JSON.parse(json);
            if (next.screen === 'closed') {
                finishCreationTransition();
                state = null;
                panel.hidden = true;
                panel.dataset.screen = 'roster';
                classCardsKey = '';
                return;
            }

            if (next.screen !== 'appearance') nameInput.value = '';
            state = next;
            panel.hidden = false;
            draw();
            if (state.screen === 'appearance' && !busy()) nameInput.focus({ preventScroll: true });
        },
        redraw() {
            if (state) draw();
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const heroTables = { heroes: texts, heroClasses: classTexts };
