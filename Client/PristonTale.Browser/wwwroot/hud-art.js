// The Android client's HUD art (VulkanHudArt, VulkanSkillBorder, HudIcon) drawn on 2D canvases, so the browser's touch
// HUD looks as the phone's: the player frame's plate, crest, aura and level medallion, dark spheres in gold rings, the
// attack button's crossed swords on steel, the skill borders by tier with their rank marks, glassy gauges, outlined
// text and red badges. Sizes are in canvas pixels.

const edge = [176, 138, 72, 235];
export const edgeLight = [240, 208, 140, 255];
const gold = [232, 199, 129, 255];
const trough = [6, 7, 10, 235];
const plateTop = [30, 34, 42, 232];
const plateBottom = [10, 12, 17, 236];
const crimson = [214, 40, 52];

// Animated art redraws every animationFrameMs; its phase, in seconds, wraps after a loop so it keeps its precision.
export const animationFrameMs = 40;
const animationLoopSeconds = 600;

export const gauges = {
    life: { top: [236, 74, 62], bottom: [138, 18, 22] },
    mana: { top: [84, 156, 255], bottom: [24, 58, 150] },
    stamina: { top: [140, 214, 86], bottom: [42, 112, 38] },
    experience: { top: [250, 214, 110], bottom: [176, 112, 24] }
};

// The ring colour of a skill's tier, 1 to 5 (silver, emerald, blue, violet, gold).
const tierTints = [[204, 211, 223], [108, 231, 162], [116, 201, 255], [217, 142, 255], [255, 192, 82]];

export const mostBadge = 9;
const badgeRed = [200, 40, 40, 255];

// Each glyph as SVG path data on a 24-unit square, stroked in gold (HudIcon's lines, arcs and circles).
const circle = (x, y, r) => `M${x - r} ${y}a${r} ${r} 0 1 0 ${r * 2} 0a${r} ${r} 0 1 0 ${-r * 2} 0`;
export const glyphs = {
    attack: 'M5 19L19 5L21 3L21 9L11 19M5 14L10 19L7 22M6 17L3 20',
    potion: 'M9 4L15 4L15 9L19 14L18 20L6 20L5 14L9 9L9 4M6 14L18 14',
    shield: 'M12 3Q15 5 20 6L20 12C20 16 16 20 12 22C8 20 4 16 4 12L4 6Q9 5 12 3ZM12 8L12 17M8 12L16 12',
    ring: 'M16.01 9.27A7 7 0 1 1 7.99 9.27M12 3L18 6L12 12L6 6L12 3M6 6L18 6',
    skill: 'M12 2L14 9L22 12L14 14L12 22L10 14L2 12L10 9L12 2',
    journal: 'M4 4L18 4L18 19L5 19L4 17L4 4M8 8L15 8M8 12L15 12',
    chat: 'M3 5L21 5L21 16L12 16L7 20L7 16L3 16L3 5M7 10L17 10',
    run: circle(15, 4, 2) + 'M5 9L10 7L14 8L17 12L21 10M14 8L10 14L15 16L13 21M10 14L7 18L3 19',
    auto: circle(12, 12, 8) + 'M12 3L12 8M12 16L12 21M3 12L8 12M16 12L21 12',
    menu: 'M4 6L20 6M4 12L20 12M4 18L20 18',
    bag: 'M9 8L8 4L16 4L15 8M7 8L17 8M9 8C3 11 3 21 12 21C21 21 21 11 15 8' + circle(12, 14.5, 2),
    calendar: 'M4 6L20 6L20 20L4 20L4 6M4 10L20 10M8 3L8 7M16 3L16 7M7 14L9 14M11 14L13 14M15 14L17 14M7 17L9 17',
    gift: 'M5 11L19 11L19 20L5 20L5 11M4 8L20 8L20 11L4 11L4 8M12 8L12 20M12 8L8 4L7 6L12 8M12 8L16 4L17 6L12 8',
    exit: 'M11 4L5 4L5 20L11 20M10 12L21 12L17 8M21 12L17 16'
};
const glyphPaths = new Map();

const rgba = ([r, g, b, a = 255], alpha = 1) => `rgba(${r},${g},${b},${(a / 255) * alpha})`;

export function phase(time) {
    return (time / 1000) % animationLoopSeconds;
}

function linear(context, x0, y0, x1, y1, colors, stops) {
    const gradient = context.createLinearGradient(x0, y0, x1, y1);
    colors.forEach((color, index) => gradient.addColorStop(stops ? stops[index] : index / (colors.length - 1), rgba(color)));
    return gradient;
}

function radial(context, x, y, radius, colors, stops) {
    const gradient = context.createRadialGradient(x, y, 0, x, y, Math.max(0.001, radius));
    colors.forEach((color, index) => gradient.addColorStop(stops ? stops[index] : index / (colors.length - 1), rgba(color)));
    return gradient;
}

function disc(context, x, y, radius) {
    context.beginPath();
    context.arc(x, y, Math.max(0, radius), 0, Math.PI * 2);
}

function arc(context, x, y, radius, startDegrees, sweepDegrees) {
    context.beginPath();
    context.arc(x, y, radius, startDegrees * Math.PI / 180, (startDegrees + sweepDegrees) * Math.PI / 180);
}

function strokeCircle(context, x, y, radius, width, style) {
    context.lineWidth = width;
    context.strokeStyle = style;
    disc(context, x, y, radius);
    context.stroke();
}

function roundRect(context, left, top, right, bottom, radius) {
    context.beginPath();
    context.roundRect(left, top, Math.max(0, right - left), Math.max(0, bottom - top), Math.min(radius, (right - left) / 2, (bottom - top) / 2));
}

// Text with a dark outline, its middle at y.
export function label(context, text, x, y, size, color, align = 'center') {
    if (!text) return;
    context.font = `bold ${size}px sans-serif`;
    context.textAlign = align;
    context.textBaseline = 'middle';
    context.lineJoin = 'round';
    context.lineWidth = Math.max(2, size * 0.16);
    context.strokeStyle = rgba([0, 0, 0, color[3] ?? 255], 220 / 255);
    context.strokeText(text, x, y);
    context.fillStyle = rgba(color);
    context.fillText(text, x, y);
}

// A gold ring, lighter at the top.
function ring(context, x, y, radius, width) {
    strokeCircle(context, x, y, radius + width / 2, width, linear(context, 0, y - radius, 0, y + radius, [edgeLight, edge]));
}

function diamondPath(context, x, y, size) {
    context.beginPath();
    context.moveTo(x, y - size);
    context.lineTo(x + size * 0.7, y);
    context.lineTo(x, y + size);
    context.lineTo(x - size * 0.7, y);
    context.closePath();
}

// A gold diamond with a gem in it.
function diamond(context, x, y, size, gem) {
    diamondPath(context, x, y, size);
    context.fillStyle = linear(context, x, y - size, x, y + size, [edgeLight, [120, 78, 24]]);
    context.fill();
    context.lineWidth = Math.max(1, size * 0.12);
    context.strokeStyle = rgba([40, 26, 10]);
    context.stroke();
    diamondPath(context, x, y, size * 0.55);
    const gradient = context.createRadialGradient(x - size * 0.15, y - size * 0.2, 0, x - size * 0.15, y - size * 0.2, size * 0.7);
    gradient.addColorStop(0, '#fff');
    gradient.addColorStop(0.45, rgba(gem));
    gradient.addColorStop(1, rgba([Math.floor(gem[0] / 3), Math.floor(gem[1] / 3), Math.floor(gem[2] / 3)]));
    context.fillStyle = gradient;
    context.fill();
}

const notches = 32;
const studs = 4;

// A bezel around a round window of this radius: a dark backing band, a broad gold ring with a light inner line and
// notches all round, and diamond studs set with gems at the top, right, bottom and left.
function ornateRing(context, x, y, radius, gem = [214, 40, 52]) {
    const band = Math.max(3, radius * 0.16);
    strokeCircle(context, x, y, radius + band * 0.6, band * 1.35, rgba([8, 6, 4, 235]));
    ring(context, x, y, radius, band * 0.82);
    const inner = Math.max(1, radius * 0.025);
    strokeCircle(context, x, y, radius + inner / 2, inner, rgba(edgeLight));
    context.lineWidth = Math.max(1, radius * 0.022);
    context.strokeStyle = rgba([70, 44, 12, 170]);
    context.beginPath();
    for (let notch = 0; notch < notches; notch++) {
        if (notch % (notches / studs) === 0) continue;
        const angle = notch * Math.PI * 2 / notches;
        const cos = Math.cos(angle);
        const sin = Math.sin(angle);
        context.moveTo(x + cos * (radius + band * 0.22), y + sin * (radius + band * 0.22));
        context.lineTo(x + cos * (radius + band * 0.68), y + sin * (radius + band * 0.68));
    }
    context.stroke();
    for (let stud = 0; stud < studs; stud++) {
        const angle = stud * Math.PI * 2 / studs - Math.PI / 2;
        diamond(context, x + Math.cos(angle) * (radius + band * 0.42), y + Math.sin(angle) * (radius + band * 0.42), band * 0.95, gem);
    }
}

// A sword across a disc of this radius, turned by degrees, its tip up before the turn.
function sword(context, x, y, s, degrees, enabled) {
    context.save();
    context.translate(x, y);
    context.rotate(degrees * Math.PI / 180);
    context.beginPath();
    context.moveTo(0, -0.78 * s);
    context.lineTo(0.09 * s, -0.6 * s);
    context.lineTo(0.09 * s, 0.2 * s);
    context.lineTo(-0.09 * s, 0.2 * s);
    context.lineTo(-0.09 * s, -0.6 * s);
    context.closePath();
    context.roundRect(-0.3 * s, 0.2 * s, 0.6 * s, 0.1 * s, 0.05 * s);
    context.rect(-0.05 * s, 0.3 * s, 0.1 * s, 0.26 * s);
    context.moveTo(0.08 * s, 0.63 * s);
    context.arc(0, 0.63 * s, 0.08 * s, 0, Math.PI * 2);
    context.globalAlpha = enabled ? 1 : 130 / 255;
    context.fillStyle = linear(context, -0.1 * s, 0, 0.1 * s, 0, [[255, 250, 235], [196, 158, 86]]);
    context.fill();
    context.globalAlpha = 1;
    context.lineWidth = Math.max(1.5, s * 0.03);
    context.strokeStyle = rgba([30, 18, 8, 230]);
    context.stroke();
    context.restore();
}

// The attack button's face: a red glow behind a steel disc in an ornate bezel, with two crossed swords on it. Darker
// and smaller while pressed, faded while disabled.
export function attackFace(context, x, y, radius, enabled, pressed) {
    const face = radius * (pressed ? 0.8 : 0.84);
    context.fillStyle = radial(context, x, y, radius, [[255, 90, 40, 150], [255, 60, 20, 0]], [0.7, 1]);
    disc(context, x, y, radius);
    context.fill();
    const light = pressed ? [54, 58, 70] : [92, 100, 118];
    const steel = context.createRadialGradient(x, y - face * 0.5, 0, x, y - face * 0.5, face * 1.6);
    steel.addColorStop(0, rgba(light));
    steel.addColorStop(1, rgba([12, 14, 22]));
    context.globalAlpha = enabled ? 1 : 140 / 255;
    context.fillStyle = steel;
    disc(context, x, y, face);
    context.fill();
    context.globalAlpha = 1;
    ornateRing(context, x, y, face * 0.86, [255, 120, 40]);
    sword(context, x, y, face * 0.86, 45, enabled);
    sword(context, x, y, face * 0.86, -45, enabled);
}

// A round button face: a dark sphere, dimmed while disabled, darker while pressed, in a gold ring.
export function orb(context, x, y, radius, enabled, pressed) {
    let light = [58, 66, 82];
    let dark = [10, 12, 18];
    if (pressed) [light, dark] = [dark, light];
    const sphere = context.createRadialGradient(x, y - radius * 0.45, 0, x, y - radius * 0.45, radius * 1.5);
    sphere.addColorStop(0, rgba(light));
    sphere.addColorStop(1, rgba(dark));
    context.globalAlpha = (enabled ? 235 : 130) / 255;
    context.fillStyle = sphere;
    disc(context, x, y, radius);
    context.fill();
    context.globalAlpha = 1;
    const band = Math.max(2, radius * 0.07);
    ring(context, x, y, radius - band, band);
}

function goldSpark(context, x, y, size, alpha) {
    context.beginPath();
    context.moveTo(x, y - size);
    context.lineTo(x + size * 0.48, y - size * 0.23);
    context.lineTo(x + size, y);
    context.lineTo(x + size * 0.48, y + size * 0.23);
    context.lineTo(x, y + size);
    context.lineTo(x - size * 0.48, y + size * 0.23);
    context.lineTo(x - size, y);
    context.lineTo(x - size * 0.48, y - size * 0.23);
    context.closePath();
    context.fillStyle = rgba([255, 245, 185, alpha]);
    context.fill();
}

// Two bright arcs and a spark running round a circle, over a breathing gold line (a skill at its highest rank).
function goldOrbit(context, x, y, radius, seconds, alpha = 255) {
    const angle = (seconds / 2.4) % 1 * 360;
    const breath = 0.5 + 0.5 * Math.sin(seconds * 3.6);
    const line = Math.max(1, radius * 0.054);
    context.lineCap = 'butt';
    strokeCircle(context, x, y, radius, line, rgba([241, 164, 48, Math.floor((0.53 + 0.33 * breath) * alpha)]));
    context.lineWidth = line * 2.7;
    context.strokeStyle = rgba([255, 159, 36, Math.floor(alpha * 120 / 255)]);
    arc(context, x, y, radius, angle - 54, 54);
    context.stroke();
    arc(context, x, y, radius, angle + 146, 34);
    context.stroke();
    context.lineWidth = line;
    context.strokeStyle = rgba([255, 242, 163, alpha]);
    arc(context, x, y, radius, angle - 36, 36);
    context.stroke();
    arc(context, x, y, radius, angle + 156, 24);
    context.stroke();
    const radians = angle * Math.PI / 180;
    goldSpark(context, x + Math.cos(radians) * radius, y + Math.sin(radians) * radius, line * (1.3 + breath), alpha);
}

// A skill's border: a ring in its tier's colour and its trained rank as marks along the bottom; the highest rank adds
// the gold orbit. True while it animates.
export function skillBorder(context, x, y, radius, tier, rank, maxRank, seconds) {
    if (!tier || radius <= 0 || maxRank <= 0) return false;
    const tint = tierTints[Math.min(tierTints.length, tier) - 1];
    const marks = Math.max(0, Math.min(maxRank, rank));
    context.lineCap = 'round';
    strokeCircle(context, x, y, radius * 0.86, Math.max(1, radius * 0.045), rgba([...tint, marks > 0 ? 215 : 105]));
    const inset = radius * 0.78;
    context.lineWidth = Math.max(1, radius * 0.085);
    const rankArc = 154;
    const step = rankArc / maxRank;
    for (let mark = 0; mark < maxRank; mark++) {
        context.strokeStyle = rgba([...tint, mark < marks ? 255 : 55]);
        arc(context, x, y, inset, 90 + rankArc / 2 - (mark + 0.8) * step, step * 0.55);
        context.stroke();
    }
    context.lineCap = 'butt';
    if (marks < maxRank) return false;
    goldOrbit(context, x, y, radius * 0.93, seconds);
    return true;
}

// A round red badge with a white edge and its count (none for 0, a plain dot): something waits for the player.
export function redBadge(context, x, y, radius, count) {
    const text = !count ? '' : count > mostBadge ? mostBadge + '+' : String(count);
    context.fillStyle = rgba(badgeRed);
    disc(context, x, y, radius);
    context.fill();
    strokeCircle(context, x, y, radius, Math.max(1.5, radius * 0.14), '#fff');
    label(context, text, x, y, radius * 1.1, [255, 255, 255, 255]);
}

// A glyph in gold on a box of this size at (x, y), faded while disabled.
export function glyph(context, name, x, y, size, enabled = true) {
    const data = glyphs[name];
    if (!data) return;
    let path = glyphPaths.get(name);
    if (!path) glyphPaths.set(name, path = new Path2D(data));
    context.save();
    context.translate(x, y);
    context.scale(size / 24, size / 24);
    context.globalAlpha = enabled ? 1 : 120 / 255;
    context.lineWidth = 1.6;
    context.lineCap = 'round';
    context.lineJoin = 'round';
    context.strokeStyle = rgba(gold);
    context.stroke(path);
    context.restore();
}

// A gauge: the trough, the trail (where the value was a moment ago) and the fill up to fraction, glassy (a bright top
// edge, a soft gloss, a lit tip) with the text centred.
const rays = 16;
const leaves = 5;

// The player's crest around a portrait bezel of this radius: gold rays fanning out behind it, a laurel branch up each
// side and a crown with a gem on top. Drawn before the bezel, which covers the rays' roots.
function crest(context, x, y, radius, gem) {
    context.beginPath();
    for (let ray = 0; ray < rays; ray++) {
        const angle = (ray + 0.5) * Math.PI * 2 / rays;
        const reach = radius * (ray % 2 === 0 ? 1.42 : 1.3);
        context.moveTo(x + Math.cos(angle - 0.1) * radius, y + Math.sin(angle - 0.1) * radius);
        context.lineTo(x + Math.cos(angle) * reach, y + Math.sin(angle) * reach);
        context.lineTo(x + Math.cos(angle + 0.1) * radius, y + Math.sin(angle + 0.1) * radius);
        context.closePath();
    }
    context.fillStyle = radial(context, x, y, radius * 1.42, [edgeLight, edge, [120, 78, 24, 0]], [0.6, 0.85, 1]);
    context.fill();

    // Laurel leaves climb from the bottom of the bezel up each side.
    const leafFill = linear(context, x, y - radius, x, y + radius, [[250, 226, 150], [150, 104, 36]]);
    context.lineWidth = Math.max(1, radius * 0.025);
    context.strokeStyle = rgba([50, 32, 10, 220]);
    for (let side = -1; side <= 1; side += 2) {
        for (let leaf = 0; leaf < leaves; leaf++) {
            const degrees = 100 + leaf * 17;
            const radians = degrees * Math.PI / 180;
            const leafX = x + side * -Math.cos(radians) * radius * 1.22;
            const leafY = y + Math.sin(radians) * radius * 1.22;
            context.save();
            context.translate(leafX, leafY);
            context.rotate(side * (degrees - 90 + 35) * Math.PI / 180);
            context.beginPath();
            context.ellipse(0, 0, radius * 0.07, radius * 0.17, 0, 0, Math.PI * 2);
            context.fillStyle = leafFill;
            context.fill();
            context.stroke();
            context.restore();
        }
    }

    // The crown: a band with three points over the top of the bezel.
    const base = y - radius * 1.1;
    const width = radius * 0.5;
    context.beginPath();
    context.moveTo(x - width, base);
    context.lineTo(x - width * 1.05, base - radius * 0.24);
    context.lineTo(x - width * 0.5, base - radius * 0.1);
    context.lineTo(x, base - radius * 0.32);
    context.lineTo(x + width * 0.5, base - radius * 0.1);
    context.lineTo(x + width * 1.05, base - radius * 0.24);
    context.lineTo(x + width, base);
    context.closePath();
    context.fillStyle = linear(context, x, base - radius * 0.32, x, base, [edgeLight, [150, 98, 28]]);
    context.fill();
    context.stroke();
    diamond(context, x, base - radius * 0.1, radius * 0.09, gem);
}

// The gems studding a portrait bezel: crimson for the player and what can be fought, blue for an NPC.
export const gems = { crimson, npc: [70, 160, 255] };

// A rounded plate; with a hole it leaves a round window centred at (holeX, holeY) in the ornate bezel set with gems of
// that colour, with the player's crest around it when asked.
export function plate(context, left, top, right, bottom, radius, holeX = 0, holeY = 0, holeRadius = 0, gem = crimson, withCrest = false) {
    context.beginPath();
    context.roundRect(left, top, right - left, bottom - top, radius);
    if (holeRadius > 0) {
        context.moveTo(holeX + holeRadius, holeY);
        context.arc(holeX, holeY, holeRadius, 0, Math.PI * 2);
    }
    context.fillStyle = linear(context, 0, top, 0, bottom, [plateTop, plateBottom]);
    context.fill('evenodd');
    const line = Math.max(1, radius * 0.08);
    context.lineWidth = line;
    context.strokeStyle = rgba(edge);
    roundRect(context, left + line / 2, top + line / 2, right - line / 2, bottom - line / 2, radius);
    context.stroke();
    if (holeRadius > 0) {
        if (withCrest) crest(context, holeX, holeY, holeRadius, gem);
        ornateRing(context, holeX, holeY, holeRadius, gem);
    }
}

const auraSparks = 3;
const auraTail = 8;

// A glow that breathes around a portrait bezel of this radius and three sparks that circle it.
export function aura(context, x, y, radius, seconds) {
    const breath = 0.5 + 0.5 * Math.sin(seconds * 2.2);
    const outer = radius * (1.5 + 0.06 * breath);
    context.fillStyle = radial(context, x, y, outer, [[255, 210, 120, 0], [255, 196, 90, Math.floor(70 + 60 * breath)], [255, 160, 40, 0]],
        [0.62, 0.78, 1]);
    disc(context, x, y, outer);
    context.fill();
    const orbit = radius * 1.2;
    for (let spark = 0; spark < auraSparks; spark++) {
        for (let tail = 0; tail < auraTail; tail++) {
            const angle = seconds * 1.4 + spark * Math.PI * 2 / auraSparks - tail * 0.07;
            const fade = 1 - tail / auraTail;
            context.fillStyle = rgba([255, 240, 190, Math.floor(230 * fade)]);
            disc(context, x + Math.cos(angle) * orbit, y + Math.sin(angle) * orbit, Math.max(1, radius * 0.05 * fade));
            context.fill();
        }
    }
}

const flameTongues = 7;
const flameEmbers = 6;

// Tongues of fire around a circle, each rising and swaying on its own beat.
function flames(context, x, y, radius, seconds) {
    for (let layer = 0; layer < 2; layer++) {
        const reach = radius * (layer === 0 ? 2.1 : 1.6);
        context.beginPath();
        for (let tongue = 0; tongue < flameTongues; tongue++) {
            const beat = seconds * (5 + tongue % 3) + tongue * 1.7 + layer;
            const lift = reach * (0.75 + 0.25 * Math.sin(beat));
            const angle = -Math.PI / 2 + (tongue - (flameTongues - 1) / 2) * 0.42 + 0.08 * Math.sin(beat * 1.3);
            const sin = Math.sin(angle);
            const cos = Math.cos(angle);
            const sway = radius * 0.25 * Math.sin(beat * 0.9);
            context.moveTo(x + Math.cos(angle - 0.5) * radius * 0.9, y + Math.sin(angle - 0.5) * radius * 0.9);
            context.quadraticCurveTo(x + cos * lift * 0.6 - sin * sway, y + sin * lift * 0.6 + cos * sway, x + cos * lift, y + sin * lift);
            context.quadraticCurveTo(x + cos * lift * 0.6 + sin * sway, y + sin * lift * 0.6 - cos * sway,
                x + Math.cos(angle + 0.5) * radius * 0.9, y + Math.sin(angle + 0.5) * radius * 0.9);
            context.closePath();
        }
        const colors = layer === 0
            ? [[255, 120, 20, 230], [220, 40, 10, 160], [160, 20, 0, 0]]
            : [[255, 250, 200, 255], [255, 200, 60, 230], [255, 140, 20, 0]];
        context.fillStyle = radial(context, x, y, reach, colors, [0.35, 0.7, 1]);
        context.fill();
    }
    for (let ember = 0; ember < flameEmbers; ember++) {
        const rise = (seconds * 0.8 + ember / flameEmbers) % 1;
        const drift = Math.sin(seconds * 2 + ember * 2.3) * radius * 0.8;
        context.fillStyle = rgba([255, 200 - Math.floor(120 * rise), 60, Math.floor(220 * (1 - rise))]);
        disc(context, x + drift, y - radius - rise * radius * 2.4, Math.max(1, radius * 0.09 * (1 - rise)));
        context.fill();
    }
}

// The level on a medallion: a dark plate in a gold ring with a gold numeral. Its stone climbs with the level against
// the highest (amber, emerald from a quarter, sapphire from half, crimson from three quarters) as an inner line and a
// breathing glow; the highest level burns, with two gold orbits.
export function levelBadge(context, x, y, radius, level, highest, seconds) {
    const top = level >= highest;
    const share = level / Math.max(1, highest);
    const [light, dark] = top ? [[255, 236, 140], [200, 60, 10]]
        : share >= 0.75 ? [[255, 130, 120], [120, 10, 20]]
        : share >= 0.5 ? [[150, 190, 255], [30, 50, 150]]
        : share >= 0.25 ? [[140, 240, 170], [16, 110, 60]]
        : [edgeLight, [120, 78, 24]];
    if (top) {
        flames(context, x, y, radius, seconds);
        const pulse = 0.5 + 0.5 * Math.sin(seconds * 2.6);
        goldOrbit(context, x, y, radius * (1.08 + pulse * 0.04), seconds);
        goldOrbit(context, x, y, radius * 1.19, -seconds * 0.64, 165);
    } else if (share >= 0.25) {
        const pulse = 0.5 + 0.5 * Math.sin(seconds * 3);
        context.fillStyle = radial(context, x, y, radius * 1.7, [[...light.slice(0, 3), Math.floor(120 * pulse)], [...light.slice(0, 3), 0]], [0.55, 1]);
        disc(context, x, y, radius * 1.7);
        context.fill();
    }
    const band = Math.max(2, radius * 0.2);
    const inner = radius - band;
    strokeCircle(context, x, y, radius + band * 0.2, band * 0.7, rgba([8, 6, 4, 235]));
    const stone = context.createRadialGradient(x, y - inner * 0.5, 0, x, y - inner * 0.5, inner * 1.6);
    stone.addColorStop(0, rgba([Math.floor(dark[0] / 3) + 24, Math.floor(dark[1] / 3) + 22, Math.floor(dark[2] / 3) + 26, 250]));
    stone.addColorStop(1, rgba(plateBottom));
    context.fillStyle = stone;
    disc(context, x, y, inner);
    context.fill();
    ring(context, x, y, inner, band);
    const line = Math.max(1, radius * 0.07);
    strokeCircle(context, x, y, inner - line, line, rgba([...light.slice(0, 3), 230]));
    context.fillStyle = rgba([255, 240, 200, 40]);
    context.beginPath();
    context.ellipse(x, y - inner * 0.515, inner * 0.7, inner * 0.365, 0, 0, Math.PI * 2);
    context.fill();
    const text = String(level);
    label(context, text, x, y, inner * (text.length > 2 ? 1.05 : 1.3), top ? [255, 246, 210, 255] : edgeLight);
}

// The class title on a crimson plate with pulsing gold ornaments and a highlight passing through it (the highest level).
export function maxLevelTitle(context, title, left, right, y, radius, seconds) {
    const pulse = 0.5 + 0.5 * Math.sin(seconds * 2.7);
    context.fillStyle = rgba([67 + Math.floor(29 * pulse), 22, 24, 240]);
    roundRect(context, left, y - radius, right, y + radius, radius);
    context.fill();
    context.lineWidth = Math.max(1, radius * 0.09);
    context.strokeStyle = rgba(edge);
    context.stroke();
    const glint = left + radius + (right - left - radius * 2) * ((seconds / 2.35) % 1);
    const center = (left + right) / 2;
    goldSpark(context, left + radius, y, radius * (0.55 + 0.19 * pulse), 235);
    goldSpark(context, right - radius, y, radius * (0.74 - 0.19 * pulse), 235);
    goldSpark(context, glint, y - radius * 0.82, radius * 0.45, 255);
    label(context, title, center, y, radius * 1.37, edgeLight);
    context.save();
    context.beginPath();
    context.rect(glint - radius * 1.35, y - radius, radius * 2.7, radius * 2);
    context.clip();
    label(context, title, center, y, radius * 1.37, [255, 251, 220, 255]);
    context.restore();
}

// The title the player shows, on a small plaque ending at right and centred on y: a dark plate edged in the title's
// rarity colour with a soft glow, a gold diamond at each end and the name in that colour, a highlight passing through
// it. The text shrinks to fit between left and right; returns the plaque's left edge.
export function titlePlaque(context, text, left, right, y, height, color, seconds) {
    const room = right - left;
    let size = height * 0.62;
    context.font = `bold ${size}px sans-serif`;
    const pad = height * 0.9;
    let width = context.measureText(text).width + pad * 2;
    if (width > room && room > pad * 2) {
        size *= (room - pad * 2) / (width - pad * 2);
        width = room;
    }
    const start = right - width;
    const tint = color.slice(0, 3);
    const half = height / 2;
    context.save();
    context.shadowColor = rgba([...tint, 150]);
    context.shadowBlur = height * 0.5;
    context.fillStyle = linear(context, 0, y - half, 0, y + half, [[...tint.map(channel => Math.floor(channel * 0.22)), 245], plateBottom]);
    roundRect(context, start, y - half, right, y + half, half);
    context.fill();
    context.restore();
    context.lineWidth = Math.max(1, height * 0.08);
    context.strokeStyle = rgba([...tint, 220]);
    roundRect(context, start, y - half, right, y + half, half);
    context.stroke();
    diamond(context, start + half * 0.55, y, half * 0.55, tint);
    diamond(context, right - half * 0.55, y, half * 0.55, tint);
    const center = (start + right) / 2;
    label(context, text, center, y, size, color);
    const glint = start + pad + (width - pad * 2) * ((seconds / 3.1) % 1);
    context.save();
    context.beginPath();
    context.rect(glint - height * 0.5, y - half, height, height);
    context.clip();
    label(context, text, center, y, size, [255, 250, 225, 255]);
    context.restore();
    return start;
}

export function bar(context, left, top, right, bottom, gauge, fraction, trail, text, textSize, shine = -1) {
    const radius = (bottom - top) / 2;
    context.fillStyle = rgba(trough);
    roundRect(context, left, top, right, bottom, radius);
    context.fill();
    const inner = Math.max(1, (bottom - top) * 0.12);
    const width = right - left - inner * 2;
    const clamp = value => Math.max(0, Math.min(1, value));
    if (trail > fraction) {
        context.fillStyle = rgba([255, 236, 190, 210]);
        roundRect(context, left + inner, top + inner, left + inner + width * clamp(trail), bottom - inner, radius);
        context.fill();
    }
    if (fraction > 0) {
        const fillLeft = left + inner;
        const fillRight = left + inner + Math.max(width * clamp(fraction), radius);
        const fillTop = top + inner;
        const fillBottom = bottom - inner;
        const lit = gauge.top.map(channel => Math.min(255, channel + 70));
        context.fillStyle = linear(context, 0, fillTop, 0, fillBottom, [lit, gauge.top, gauge.bottom, gauge.top], [0, 0.3, 0.8, 1]);
        roundRect(context, fillLeft, fillTop, fillRight, fillBottom, radius);
        context.fill();
        const glossBottom = fillTop + (fillBottom - fillTop) * 0.45;
        context.fillStyle = linear(context, 0, fillTop, 0, glossBottom, [[255, 255, 255, 150], [255, 255, 255, 10]]);
        roundRect(context, fillLeft + radius * 0.4, fillTop + (fillBottom - fillTop) * 0.08, fillRight - radius * 0.4, glossBottom, radius * 0.6);
        context.fill();
        if (shine >= 0) {
            const band = (fillBottom - fillTop) * 2.5;
            const at = fillLeft - band + (fillRight - fillLeft + band * 2) * shine;
            context.save();
            context.beginPath();
            context.rect(fillLeft, fillTop, fillRight - fillLeft, fillBottom - fillTop);
            context.clip();
            context.fillStyle = linear(context, at - band, 0, at + band, 0, [[255, 255, 255, 0], [255, 255, 255, 120], [255, 255, 255, 0]]);
            context.fillRect(fillLeft, fillTop, fillRight - fillLeft, fillBottom - fillTop);
            context.restore();
        }
        const tipX = fillRight - radius * 0.5;
        const tipY = (fillTop + fillBottom) / 2;
        context.fillStyle = radial(context, tipX, tipY, radius * 1.6, [[255, 255, 240, 200], [...lit, 0]]);
        disc(context, tipX, tipY, radius * 1.6);
        context.fill();
    }
    context.lineWidth = Math.max(1, inner * 0.6);
    context.strokeStyle = rgba([176, 138, 72, 150]);
    roundRect(context, left, top, right, bottom, radius);
    context.stroke();
    if (text) label(context, text, (left + right) / 2, (top + bottom) / 2, textSize, [255, 255, 255, 255]);
}

// A gauge value that eases toward the value it is given, with a trail that follows a drop more slowly.
export function easedGauge() {
    const ease = 10;
    const trailEase = 2.5;
    let target = 0;
    return {
        shown: 0,
        trail: 0,
        set(value) { target = Math.max(0, Math.min(1, value)); },
        // Moves by elapsed seconds; true while still moving.
        step(elapsed) {
            this.shown += (target - this.shown) * Math.min(1, ease * elapsed);
            this.trail = target >= this.trail ? this.shown : this.trail + (target - this.trail) * Math.min(1, trailEase * elapsed);
            if (Math.abs(target - this.shown) < 0.001 && Math.abs(target - this.trail) < 0.001) {
                this.shown = target;
                this.trail = target;
                return false;
            }
            return true;
        }
    };
}

