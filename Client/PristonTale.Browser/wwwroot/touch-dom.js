import * as art from './hud-art.js';
// The pieces the touch HUD's windows (touch-quests.js, touch-map.js, touch-bag.js) build their plates from, in the
// Android client's make: chips, section headings with a diamond and a rule, and gauges drawn by hud-art.js.

export function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

// A small rounded label; kind picks its colour (touch-journal-chip in play.css).
export function chip(text, kind) {
    const node = element('span', 'touch-journal-chip', text);
    node.dataset.kind = kind;
    return node;
}

export function section(text) {
    const heading = element('h3', 'touch-journal-section');
    heading.append(element('span', 'touch-journal-diamond'), element('span', '', text), element('span', 'touch-journal-rule'));
    return heading;
}

// A gauge with its text (the Android client's VulkanGaugeView), width x height CSS pixels; colours from art.gauges.
export function gauge(fraction, text, width, height, colours = art.gauges.life) {
    const canvas = element('canvas', 'touch-journal-gauge');
    canvas.setAttribute('aria-hidden', 'true');
    const ratio = globalThis.devicePixelRatio || 1;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    canvas.style.width = width + 'px';
    canvas.style.height = height + 'px';
    const context = canvas.getContext('2d');
    if (context) {
        const share = Math.max(0, Math.min(1, fraction));
        art.bar(context, 1, 1, canvas.width - 1, canvas.height - 1, colours, share, share, text, canvas.height * 0.62);
    }
    return canvas;
}

// A line with its numbered blanks ({0}, {1}, …) filled.
export const format = (text, ...values) => text.replace(/\{(\d)\}/g, (match, index) => String(values[Number(index)] ?? match));

// A decoded picture (a canvas) as an image address for an <img>, made once per picture.
const addresses = new WeakMap();
export function pictureAddress(canvas) {
    if (!addresses.has(canvas)) addresses.set(canvas, canvas.toDataURL());
    return addresses.get(canvas);
}
