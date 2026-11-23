import { table } from './page-language.js';
import * as art from './hud-art.js';
import { format } from './touch-dom.js';
// The touch HUD's combat feed, the Android client's: on the right above the action wheel, each kill, experience and
// gold gained, level reached and quest count grown rises and fades over a few seconds, at most five at once, under a
// counter of the kills since entering the world that flares at each one. The game keeps the lines
// (BrowserHost.TouchFeed); the page words them and draws them on its canvas, and draws nothing while there is nothing
// to show.

const labels = {
    'pt-BR': { Kill: '{0} derrotado', monster: 'Monstro derrotado', Experience: '+{0} EXP', Gold: '+{0} ouro', Level: 'Nível {0}!',
        Quest: '{0}  {1}/{2}', kills: '⚔ {0} abates', 'kills-one': '⚔ 1 abate' },
    en: { Kill: '{0} defeated', monster: 'Monster defeated', Experience: '+{0} EXP', Gold: '+{0} gold', Level: 'Level {0}!',
        Quest: '{0}  {1}/{2}', kills: '⚔ {0} kills', 'kills-one': '⚔ 1 kill' }
};

const mostLines = 5;
const lineSeconds = 3.2;
const flareSeconds = 0.6;
const colours = {
    Kill: [255, 196, 120],
    Experience: [250, 224, 120],
    Gold: [255, 214, 80],
    Level: art.edgeLight,
    Quest: [150, 230, 190]
};

export function feedText(line, say, language) {
    const number = value => Number(value).toLocaleString(language);
    switch (line.kind) {
        case 'Kill':
            return line.name ? format(say('Kill'), line.name) : say('monster');
        case 'Experience':
        case 'Gold':
            return format(say(line.kind), number(line.amount));
        case 'Level':
            return format(say('Level'), line.amount);
        case 'Quest':
            return format(say('Quest'), line.name, line.count, line.needed);
        default:
            return '';
    }
}

// The lines past the count last seen; the first look only takes the count, so the feed starts empty.
export function newLines(state, seen) {
    return seen < 0 ? [] : state.lines.filter(line => line.sequence > seen);
}

export function createTouchFeed(layer, getHost, getLanguage) {
    const canvas = layer.querySelector('[data-role="touch-feed"]');
    if (!canvas) return { poll() {}, frame() {}, close() {} };
    const context = canvas.getContext('2d');
    const shown = [];
    let seen = -1;
    let kills = 0;
    let flare = -Infinity;
    let settling = true;

    const say = key => table(labels, 'feed', getLanguage())[key] ?? labels.en[key];

    function poll() {
        let state;
        try {
            const text = getHost()?.TouchFeed?.() || '';
            state = text ? JSON.parse(text) : null;
        } catch {
            return;
        }
        if (!state || state.added === seen) return;
        const now = performance.now();
        for (const line of newLines(state, seen)) {
            if (shown.length === mostLines) shown.shift();
            shown.push({ text: feedText(line, say, getLanguage()), colour: colours[line.kind] ?? art.edgeLight, born: now });
        }
        if (seen >= 0 && state.kills > kills) flare = now;
        seen = state.added;
        kills = state.kills;
    }

    function frame(now) {
        while (shown.length > 0 && (now - shown[0].born) / 1000 > lineSeconds) shown.shift();
        const ratio = globalThis.devicePixelRatio || 1;
        const width = Math.round(canvas.clientWidth * ratio);
        const height = Math.round(canvas.clientHeight * ratio);
        const resized = canvas.width !== width || canvas.height !== height;
        // While lines move or the counter flares it redraws every frame, and once more after, to settle.
        const moving = shown.length > 0 || now - flare < flareSeconds * 1000;
        if (!moving && !settling && !resized) return;
        settling = moving;
        if (resized) {
            canvas.width = width;
            canvas.height = height;
        }
        context.clearRect(0, 0, width, height);
        // Sizes follow the screen's height, as the Android feed's: the canvas is 30% of it.
        const size = height / 0.3 * 0.034;
        const lineHeight = size * 1.35;
        const right = width - height / 0.3 * 0.01;
        if (kills > 0) {
            const glow = Math.max(0, Math.min(1, 1 - (now - flare) / 1000 / flareSeconds));
            const chip = size * (1 + glow * 0.35);
            art.plate(context, right - chip * 5.6, 0, right, chip * 1.5, chip * 0.75);
            const text = kills === 1 ? say('kills-one') : format(say('kills'), kills.toLocaleString(getLanguage()));
            art.label(context, text, right - chip * 2.8, chip * 0.75, chip * 0.78, glow > 0 ? art.edgeLight : colours.Kill);
        }
        const bottom = height - lineHeight * 0.5;
        for (let index = shown.length - 1; index >= 0; index--) {
            const line = shown[index];
            const age = (now - line.born) / 1000 / lineSeconds;
            const alpha = Math.round(255 * Math.max(0, Math.min(1, (1 - age) * 3)));
            const y = bottom - (shown.length - 1 - index) * lineHeight - age * lineHeight * 0.8;
            art.label(context, line.text, right, y, size, [...line.colour.slice(0, 3), alpha], 'right');
        }
    }

    return {
        poll,
        frame,
        close() {
            shown.length = 0;
            flare = -Infinity;
            settling = true;
        },
        translate() {
            settling = true;
        }
    };
}

// The language tables, for the catalogs of the other languages and their test.
export const feedTables = { feed: labels };
