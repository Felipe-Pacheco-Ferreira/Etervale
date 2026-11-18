export const workerSmaaLookupDimensions = Object.freeze({ area: [160, 560], search: [66, 33] });
const lookupTimeoutMs = 20000;
const bitmapOptions = Object.freeze({ premultiplyAlpha: 'none', colorSpaceConversion: 'none', imageOrientation: 'none' });

export async function loadWorkerSmaaLookups(options = {}) {
    const environment = options.environment ?? globalThis;
    const base = new URL(options.baseUrl ?? import.meta.url);
    if (!['https:', 'http:'].includes(base.protocol) || base.origin !== environment.location?.origin) {
        throw new Error('Worker lookup images must use the page origin.');
    }
    if (typeof environment.fetch !== 'function' || typeof environment.createImageBitmap !== 'function') {
        throw new Error('Worker lookup image decoding is unavailable.');
    }
    const abort = new environment.AbortController();
    const images = {};
    let released = false;
    const release = () => {
        if (released) return;
        released = true;
        for (const bitmap of Object.values(images)) bitmap.close();
    };
    let timeout;
    const deadline = new Promise((resolve, reject) => {
        timeout = environment.setTimeout(() => {
            const error = new Error('Worker lookup image loading timed out.');
            abort.abort(error);
            reject(error);
        }, lookupTimeoutMs);
    });
    const load = async name => {
        const url = new URL(`./vendor/three/addons/tsl/display/smaa-${name}.png`, base);
        const response = await environment.fetch(url.href, { signal: abort.signal, credentials: 'same-origin',
            mode: 'same-origin', redirect: 'error' });
        if (response.status !== 200 || response.redirected) throw new Error(`Worker SMAA ${name} lookup could not be loaded.`);
        const blob = await response.blob();
        if (abort.signal.aborted) throw abort.signal.reason;
        const bitmap = await environment.createImageBitmap(blob, bitmapOptions);
        if (released || abort.signal.aborted) {
            bitmap.close();
            throw abort.signal.reason ?? new Error('Worker lookup images were released.');
        }
        images[name] = bitmap;
        const [width, height] = workerSmaaLookupDimensions[name];
        if (bitmap.width !== width || bitmap.height !== height) throw new Error(`Worker SMAA ${name} lookup has unexpected dimensions.`);
    };
    try {
        await Promise.race([Promise.all(Object.keys(workerSmaaLookupDimensions).map(load)), deadline]);
        return { workerInit: { smaaLookups: images }, transferList: [images.area, images.search], release };
    } catch (error) {
        abort.abort(error);
        release();
        throw error;
    } finally {
        environment.clearTimeout(timeout);
    }
}
