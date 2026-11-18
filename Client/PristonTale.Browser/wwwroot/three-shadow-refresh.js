const targetJumpSquared = 64 * 64;
const directionJumpSquared = 0.01 * 0.01;
const timeEpsilon = 0.000001;

export function createShadowRefresh() {
    let dirty = true, wasEnabled = false, lastTime = null, nextTime = 0, rate = null;
    let targetKnown = false, targetX = 0, targetY = 0, targetZ = 0;
    let directionKnown = false, directionX = 0, directionY = 0, directionZ = 0;
    let requested = 0, skipped = 0, disabled = 0, invalidations = 0, teleports = 0, directionChanges = 0, timeResets = 0;

    return {
        invalidate() { dirty = true; invalidations++; },
        update(light, frame, hz, enabled) {
            const shadow = light?.shadow;
            if (!shadow) throw new TypeError('Shadow refresh requires a shadow-capable light.');
            shadow.autoUpdate = false;
            const nextRate = Number.isFinite(hz) && hz > 0 ? hz : 0;
            const time = frame.timeSeconds;
            const validTime = Number.isFinite(time) && time >= 0;
            const reset = !validTime || (lastTime !== null && time < lastTime);
            if (reset) timeResets++;
            if (validTime) lastTime = time;
            if (!enabled) {
                shadow.needsUpdate = false;
                wasEnabled = false;
                dirty = true;
                rate = nextRate;
                disabled++;
                return false;
            }
            const target = frame.cameraTarget, direction = frame.sunDirection;
            const validTarget = target && Number.isFinite(target[0]) && Number.isFinite(target[1]) && Number.isFinite(target[2]);
            const validDirection = direction && Number.isFinite(direction[0]) && Number.isFinite(direction[1]) && Number.isFinite(direction[2]);
            let jumped = false, turned = false;
            if (targetKnown && validTarget) {
                const x = target[0] - targetX, y = target[1] - targetY, z = target[2] - targetZ;
                jumped = x * x + y * y + z * z > targetJumpSquared;
            }
            if (directionKnown && validDirection) {
                const x = direction[0] - directionX, y = direction[1] - directionY, z = direction[2] - directionZ;
                turned = x * x + y * y + z * z > directionJumpSquared;
            }
            if (jumped) teleports++;
            if (turned) directionChanges++;
            const force = dirty || !wasEnabled || reset || rate !== nextRate || jumped || turned || shadow.needsUpdate === true;
            const due = force || nextRate === 0 || time + timeEpsilon >= nextTime;
            wasEnabled = true;
            rate = nextRate;
            shadow.needsUpdate = due;
            if (!due) {
                skipped++;
                return false;
            }
            // Carry deadlines instead of rounding every interval up to an application frame.
            const interval = nextRate > 0 ? 1 / nextRate : 0;
            if (force || interval === 0) nextTime = (validTime ? time : 0) + interval;
            else nextTime += (Math.floor((time + timeEpsilon - nextTime) / interval) + 1) * interval;
            if (validTarget) {
                targetX = target[0];
                targetY = target[1];
                targetZ = target[2];
                targetKnown = true;
            }
            if (validDirection) {
                directionX = direction[0];
                directionY = direction[1];
                directionZ = direction[2];
                directionKnown = true;
            }
            dirty = false;
            requested++;
            return true;
        },
        snapshot(reset = true) {
            const result = { requested, skipped, disabled, invalidations, teleports, directionChanges, timeResets,
                hz: rate, enabled: wasEnabled, measurement: 'Scheduled shadow refresh requests; not GPU pass timings or physical display frames' };
            if (reset) {
                requested = 0;
                skipped = 0;
                disabled = 0;
                invalidations = 0;
                teleports = 0;
                directionChanges = 0;
                timeResets = 0;
            }
            return result;
        }
    };
}
