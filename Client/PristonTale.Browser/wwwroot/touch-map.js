import { table } from './page-language.js';
import * as art from './hud-art.js';
import { element, format } from './touch-dom.js';
// The touch HUD's field map, the Android client's: a tap on the minimap opens it. The field's map fills the window and
// zooms (pinch, the wheel, + and −) and pans (drag); ◎ centres it on the player again. Over it: the guide's route, every
// NPC in view (a name is drawn only where it covers no other, so zooming in shows more), the player's arrow and the
// journey's marks (the tribe's guide as a gold star, the way out towards the hunting field, and there the red hunting
// area). The side lists the objective and the NPCs, nearest first. The game hands out the map (BrowserHost.TouchMap) in
// the map's units: x east and y south of its top left corner.

const labels = {
    'pt-BR': { map: 'Mapa', close: 'Fechar', objective: 'Objetivo', npcs: 'NPCs por perto', 'no-npcs': 'Nenhum NPC à vista.',
        'hunt-in': '{0}  {1}/{2} em {3}', hunt: 'Caça: {0} {1}/{2}', exit: 'Saída para {0}', guide: 'Guia: {0}',
        legend: '▲ Você    ★ Guia    ● NPC\n● Área de caça    • Rota', 'zoom-in': 'Aproximar', 'zoom-out': 'Afastar',
        centre: 'Centralizar em você' },
    en: { map: 'Map', close: 'Close', objective: 'Objective', npcs: 'NPCs nearby', 'no-npcs': 'No NPC in sight.',
        'hunt-in': '{0}  {1}/{2} in {3}', hunt: 'Hunt: {0} {1}/{2}', exit: 'Way to {0}', guide: 'Guide: {0}',
        legend: '▲ You    ★ Guide    ● NPC\n● Hunting area    • Route', 'zoom-in': 'Zoom in', 'zoom-out': 'Zoom out',
        centre: 'Centre on you' }
};

// Zoom 1 fits the whole field; the map opens at OpenZoom on the player and ◎ brings it at least to CentreZoom.
const mostZoom = 8;
const openZoom = 2;
const centreZoom = 2.5;
const zoomStep = 1.5;
// Dots along the route are this many map units apart.
const routeDotSpacing = 96;
const backdrop = 'rgb(10,12,16)';
const npcColor = 'rgb(120,200,255)';
const routeColor = 'rgb(255,214,64)';
const guideStar = 'rgb(255,214,64)';
const exitStar = 'rgb(255,120,80)';
const huntText = [255, 190, 170, 255];
const huntLine = [255, 150, 130];
const white = [255, 255, 255, 255];

export const clampZoom = zoom => Math.min(mostZoom, Math.max(1, zoom));

// Screen pixels per map unit: the whole field fits the view at zoom 1.
export const mapScale = (viewWidth, viewHeight, mapWidth, mapHeight, zoom) => Math.min(viewWidth / mapWidth, viewHeight / mapHeight) * zoom;

// The route's dots from the player along the points still ahead (flat x, y pairs), about spacing apart, the last one the
// route's end.
export function routeDots(from, flat, spacing = routeDotSpacing) {
    const dots = [];
    let x = from.x;
    let y = from.y;
    for (let index = 0; index + 1 < flat.length; index += 2) {
        const toX = flat[index];
        const toY = flat[index + 1];
        const count = Math.max(1, Math.floor(Math.hypot(toX - x, toY - y) / spacing));
        for (let dot = 1; dot <= count; dot++) dots.push({ x: x + (toX - x) * dot / count, y: y + (toY - y) * dot / count });
        x = toX;
        y = toY;
    }
    return dots;
}

// The NPCs nearest the player first.
export const nearestFirst = (npcs, player) =>
    [...npcs].sort((a, b) => Math.hypot(a.x - player.x, a.y - player.y) - Math.hypot(b.x - player.x, b.y - player.y));

// Whether a label's box covers none already placed.
export const labelFits = (placed, box) =>
    placed.every(other => box.right < other.left || box.left > other.right || box.bottom < other.top || box.top > other.bottom);

export function createTouchMap(layer, getHost, getLanguage, picture) {
    const dialog = layer.querySelector('[data-role="touch-map"]');
    if (!dialog) return { frame() {}, close() {}, translate() {} };
    const title = dialog.querySelector('[data-role="touch-map-title"]');
    const canvas = dialog.querySelector('[data-role="touch-map-canvas"]');
    const side = dialog.querySelector('[data-role="touch-map-side"]');
    const controls = dialog.querySelectorAll('[data-touch-map-zoom], [data-touch-map-centre]');
    let requests = -1;
    let map = null;
    let image = null;
    let zoom = openZoom;
    let centre = { x: 0, y: 0 };
    let centred = false;
    let sideKey = '';
    let drawQueued = false;
    const pointers = new Map();
    let pinch = 0;

    const say = key => table(labels, 'map', getLanguage())[key] ?? labels.en[key];

    function viewSize() {
        return { width: canvas.clientWidth || 1, height: canvas.clientHeight || 1 };
    }

    function scale() {
        const { width, height } = viewSize();
        return map ? mapScale(width, height, map.width, map.height, zoom) : 1;
    }

    function queueDraw() {
        if (drawQueued) return;
        drawQueued = true;
        requestAnimationFrame(() => {
            drawQueued = false;
            draw();
        });
    }

    function star(context, x, y, radius, color) {
        context.beginPath();
        for (let point = 0; point < 10; point++) {
            const reach = point % 2 === 0 ? radius : radius * 0.45;
            const angle = point * Math.PI / 5 - Math.PI / 2;
            context.lineTo(x + Math.cos(angle) * reach, y + Math.sin(angle) * reach);
        }
        context.closePath();
        context.fillStyle = color;
        context.fill();
        context.lineWidth = 1.5;
        context.strokeStyle = 'rgba(30,18,6,0.9)';
        context.stroke();
    }

    function draw() {
        if (!dialog.open || !map) return;
        const { width, height } = viewSize();
        const ratio = globalThis.devicePixelRatio || 1;
        if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) {
            canvas.width = Math.round(width * ratio);
            canvas.height = Math.round(height * ratio);
        }
        const context = canvas.getContext('2d');
        if (!context) return;
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        const pixels = scale();
        const X = x => width / 2 + (x - centre.x) * pixels;
        const Y = y => height / 2 + (y - centre.y) * pixels;
        context.fillStyle = backdrop;
        context.fillRect(0, 0, width, height);
        if (image) {
            context.imageSmoothingEnabled = true;
            context.drawImage(image, X(0), Y(0), map.width * pixels, map.height * pixels);
        }
        const size = Math.max(11, height * 0.035);
        const placed = [];
        const label = (text, x, y, textSize, color, always = false) => {
            context.font = `bold ${textSize}px sans-serif`;
            const half = context.measureText(text).width / 2 + 2;
            const box = { left: x - half, right: x + half, top: y - textSize * 0.6, bottom: y + textSize * 0.6 };
            if (!always && !labelFits(placed, box)) return;
            placed.push(box);
            art.label(context, text, x, y, textSize, color);
        };

        if (map.hunt) {
            const x = X(map.hunt.x);
            const y = Y(map.hunt.y);
            const radius = map.hunt.radius * pixels;
            const fill = context.createRadialGradient(x, y, 0, x, y, radius);
            fill.addColorStop(0, 'rgba(255,60,40,0.43)');
            fill.addColorStop(1, 'rgba(220,30,20,0.235)');
            context.fillStyle = fill;
            context.beginPath();
            context.arc(x, y, radius, 0, Math.PI * 2);
            context.fill();
            context.setLineDash([9, 5]);
            context.lineWidth = 2;
            context.strokeStyle = 'rgb(255,90,70)';
            context.stroke();
            context.setLineDash([]);
            label(format(say('hunt'), map.target, map.kills, map.killsNeeded), x, y, size, huntText, true);
        } else if (map.exit) {
            star(context, X(map.exit.x), Y(map.exit.y), size * 0.7, exitStar);
            label(format(say('exit'), map.exit.to), X(map.exit.x), Y(map.exit.y) - size * 1.1, size, huntText, true);
        }
        if (map.guide) {
            star(context, X(map.guide.x), Y(map.guide.y), size * 0.8, guideStar);
            label(format(say('guide'), map.guide.name), X(map.guide.x), Y(map.guide.y) - size * 1.2, size, art.edgeLight, true);
        }

        context.fillStyle = routeColor;
        for (const dot of routeDots(map.player, map.route ?? [])) {
            context.beginPath();
            context.arc(X(dot.x), Y(dot.y), 2, 0, Math.PI * 2);
            context.fill();
        }

        for (const npc of map.npcs ?? []) {
            if (npc.guide && map.guide) continue;
            context.beginPath();
            context.arc(X(npc.x), Y(npc.y), 3.5, 0, Math.PI * 2);
            context.fillStyle = npcColor;
            context.fill();
            context.lineWidth = 1;
            context.strokeStyle = 'rgba(0,0,0,0.86)';
            context.stroke();
            label(npc.name, X(npc.x), Y(npc.y) - size, size * 0.85, white);
        }

        const playerX = X(map.player.x);
        const playerY = Y(map.player.y);
        context.save();
        context.translate(playerX, playerY);
        context.rotate(map.player.facing);
        context.beginPath();
        context.moveTo(0, -size * 0.7);
        context.lineTo(size * 0.5, size * 0.6);
        context.lineTo(0, size * 0.3);
        context.lineTo(-size * 0.5, size * 0.6);
        context.closePath();
        context.fillStyle = 'rgb(255,120,60)';
        context.fill();
        context.lineWidth = 1.5;
        context.strokeStyle = 'white';
        context.stroke();
        context.restore();
    }

    function line(text, className, color) {
        const node = element('p', className, text);
        if (color) node.style.color = color;
        return node;
    }

    function renderSide() {
        const key = JSON.stringify([getLanguage(), map.step, map.kills, map.killsNeeded, map.huntField,
            nearestFirst(map.npcs ?? [], map.player).map(npc => npc.name + (npc.guide ? '*' : ''))]);
        if (key === sideKey) return;
        sideKey = key;
        const nodes = [];
        if (map.step) {
            nodes.push(line(say('objective'), 'touch-map-heading'), line(map.step, 'touch-map-step'));
            if (map.killsNeeded > 0) {
                const text = map.huntField ? format(say('hunt-in'), map.target, map.kills, map.killsNeeded, map.huntField)
                    : map.target + '  ' + map.kills + '/' + map.killsNeeded;
                nodes.push(line(text, 'touch-map-line', 'rgb(' + huntLine.join(',') + ')'));
            }
            nodes.push(element('hr', 'touch-map-rule'));
        }
        nodes.push(line(say('npcs'), 'touch-map-heading'));
        const npcs = nearestFirst(map.npcs ?? [], map.player);
        for (const npc of npcs) {
            const node = line((npc.guide ? '★' : '•') + '  ' + npc.name, 'touch-map-line');
            if (npc.guide) node.dataset.guide = 'true';
            nodes.push(node);
        }
        if (npcs.length === 0) nodes.push(line(say('no-npcs'), 'touch-map-line touch-map-muted'));
        nodes.push(element('hr', 'touch-map-rule'), line(say('legend'), 'touch-map-legend touch-map-muted'));
        side.replaceChildren(...nodes);
    }

    function centreOnPlayer(least) {
        if (!map) return;
        centre = { x: map.player.x, y: map.player.y };
        zoom = clampZoom(Math.max(zoom, least));
        queueDraw();
    }

    function poll() {
        let state;
        try {
            const text = getHost()?.TouchMap?.(dialog.open) || '';
            state = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!state) return;
        if (state.requests !== requests) {
            const asked = requests >= 0 && state.requests > requests;
            requests = state.requests;
            if (asked && state.shown && !dialog.open) {
                open();
                return;
            }
        }
        if (!dialog.open) return;
        if (!state.shown) {
            dialog.close();
            return;
        }
        if (map && map.field !== state.field) centred = false;
        map = state;
        title.textContent = map.title || say('map');
        image = picture(map.picture);
        if (!centred) {
            centred = true;
            zoom = openZoom;
            centre = { x: map.player.x, y: map.player.y };
        }
        renderSide();
        queueDraw();
    }

    function open() {
        centred = false;
        sideKey = '';
        map = null;
        dialog.showModal();
        poll();
    }

    function zoomBy(factor) {
        zoom = clampZoom(zoom * factor);
        queueDraw();
    }

    canvas.addEventListener('pointerdown', event => {
        canvas.setPointerCapture?.(event.pointerId);
        pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        if (pointers.size === 2) {
            const [a, b] = [...pointers.values()];
            pinch = Math.hypot(a.x - b.x, a.y - b.y);
        }
    });
    canvas.addEventListener('pointermove', event => {
        const last = pointers.get(event.pointerId);
        if (!last || !map) return;
        const now = { x: event.clientX, y: event.clientY };
        pointers.set(event.pointerId, now);
        if (pointers.size === 1) {
            const pixels = scale();
            centre = { x: centre.x - (now.x - last.x) / pixels, y: centre.y - (now.y - last.y) / pixels };
            queueDraw();
        } else if (pointers.size === 2) {
            const [a, b] = [...pointers.values()];
            const distance = Math.hypot(a.x - b.x, a.y - b.y);
            if (pinch > 0) zoomBy(distance / pinch);
            pinch = distance;
        }
    });
    const lift = event => {
        pointers.delete(event.pointerId);
        pinch = 0;
    };
    canvas.addEventListener('pointerup', lift);
    canvas.addEventListener('pointercancel', lift);
    canvas.addEventListener('wheel', event => {
        event.preventDefault();
        zoomBy(event.deltaY < 0 ? zoomStep : 1 / zoomStep);
    }, { passive: false });
    for (const control of controls) {
        control.addEventListener('click', () => {
            if (control.hasAttribute('data-touch-map-centre')) centreOnPlayer(centreZoom);
            else zoomBy(control.dataset.touchMapZoom === 'in' ? zoomStep : 1 / zoomStep);
        });
    }
    dialog.querySelector('[data-touch-map-close]')?.addEventListener('click', () => dialog.close());
    dialog.addEventListener('keydown', event => event.stopPropagation());
    globalThis.addEventListener?.('resize', () => queueDraw());

    return {
        frame: () => poll(),
        close() {
            if (dialog.open) dialog.close();
        },
        translate() {
            if (!map) title.textContent = say('map');
            dialog.querySelector('[data-touch-map-close]')?.setAttribute('aria-label', say('close'));
            dialog.querySelector('[data-touch-map-zoom="in"]')?.setAttribute('aria-label', say('zoom-in'));
            dialog.querySelector('[data-touch-map-zoom="out"]')?.setAttribute('aria-label', say('zoom-out'));
            dialog.querySelector('[data-touch-map-centre]')?.setAttribute('aria-label', say('centre'));
            sideKey = '';
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const mapTables = { map: labels };
