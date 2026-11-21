import test from 'node:test';
import assert from 'node:assert/strict';
import { createWebAudio, decodeSamples, SampleFormat, SoundState } from '../wwwroot/web-audio.js';

function param(value) {
    return { value };
}

class FakeBuffer {
    constructor({ length, numberOfChannels, sampleRate }) {
        this.length = length;
        this.numberOfChannels = numberOfChannels;
        this.sampleRate = sampleRate;
        this.duration = length / sampleRate;
        this.channels = [];
    }

    copyToChannel(samples, channel) {
        this.channels[channel] = Float32Array.from(samples);
    }
}

class FakeContext {
    constructor() {
        this.state = 'running';
        this.currentTime = 0;
        this.destination = {};
        this.sources = [];
        this.gains = [];
        this.panners = [];
    }

    resume() {
        this.state = 'running';
        return Promise.resolve();
    }

    close() {
        return Promise.resolve();
    }

    createGain() {
        const node = { gain: param(1), connect() {}, disconnect() {} };
        this.gains.push(node);
        return node;
    }

    createStereoPanner() {
        const node = { pan: param(0), connect() {}, disconnect() {} };
        this.panners.push(node);
        return node;
    }

    createBufferSource() {
        const source = {
            playbackRate: param(1), loop: false, started: null, stopped: false, onended: null,
            connect() {},
            disconnect() {},
            start(when = 0, offset = 0) {
                source.started = { when, offset };
            },
            stop() {
                source.stopped = true;
            },
            // What the browser does when the source runs out.
            end() {
                source.onended?.();
            }
        };
        this.sources.push(source);
        return source;
    }
}

function setup() {
    const listeners = {};
    const target = { addEventListener(name, listener) { listeners[name] = listener; } };
    let context;
    const audio = createWebAudio({
        createContext: () => (context = new FakeContext()),
        createAudioBuffer: options => new FakeBuffer(options),
        target
    });
    return { audio, gesture: () => listeners.pointerdown(), context: () => context };
}

// One second of 16-bit mono silence at 8 kHz.
function sound(audio, frames = 8000) {
    return audio.createBuffer(new Uint8Array(frames * 2), SampleFormat.Pcm, 16, 8000, 1, 0, 0);
}

test('samples convert from 8, 16 and 24-bit integers and 32-bit floats per channel', () => {
    assert.deepEqual([...decodeSamples(new Uint8Array([0, 128, 255, 128]), SampleFormat.Pcm, 8, 2)[0]], [-1, 127 / 128]);
    const sixteen = decodeSamples(new Uint8Array([0x00, 0x80, 0xff, 0x7f]), SampleFormat.Pcm, 16, 1)[0];
    assert.deepEqual([...sixteen], [-1, 32767 / 32768]);
    const twentyFour = decodeSamples(new Uint8Array([0x00, 0x00, 0x80, 0xff, 0xff, 0x7f]), SampleFormat.Pcm, 24, 1)[0];
    assert.deepEqual([...twentyFour], [-1, 8388607 / 8388608]);
    const float = new Uint8Array(new Float32Array([0.5]).buffer);
    assert.deepEqual([...decodeSamples(float, SampleFormat.Float, 32, 1)[0]], [0.5]);
    assert.equal(decodeSamples(new Uint8Array(4), 2, 4, 1), null, 'ADPCM has no conversion');
});

test('before the first gesture a one-shot ends at once and a loop waits for the context', () => {
    const { audio, gesture, context } = setup();
    const buffer = sound(audio);
    const shot = audio.createVoice(0, 0);
    const loop = audio.createVoice(0, 0);
    audio.setBuffer(shot, buffer);
    audio.setBuffer(loop, buffer);
    audio.play(shot, 0);
    audio.play(loop, 1);
    assert.equal(audio.state(shot), SoundState.Stopped);
    assert.equal(audio.state(loop), SoundState.Playing);
    assert.equal(context(), undefined, 'no context before a gesture');

    gesture();
    assert.equal(context().sources.length, 1, 'only the loop starts with the context');
    assert.equal(context().sources[0].loop, true);
    assert.equal(audio.state(loop), SoundState.Playing);
});

test('a one-shot plays until its source ends, and a loop until it is stopped', () => {
    const { audio, gesture, context } = setup();
    gesture();
    const buffer = sound(audio);
    const shot = audio.createVoice(0, 0);
    audio.setBuffer(shot, buffer);
    audio.play(shot, 0);
    assert.equal(audio.state(shot), SoundState.Playing);
    context().sources[0].end();
    assert.equal(audio.state(shot), SoundState.Stopped);

    const loop = audio.createVoice(0, 0);
    audio.setBuffer(loop, buffer);
    audio.play(loop, 1);
    context().currentTime = 5;
    assert.equal(audio.state(loop), SoundState.Playing);
    audio.stop(loop);
    assert.equal(audio.state(loop), SoundState.Stopped);
    assert.equal(context().sources[1].stopped, true);
});

test('pause keeps the position and resume starts from it', () => {
    const { audio, gesture, context } = setup();
    gesture();
    const voice = audio.createVoice(0, 0);
    audio.setBuffer(voice, sound(audio, 16000));
    audio.play(voice, 0);
    context().currentTime = 0.5;
    audio.pause(voice);
    assert.equal(audio.state(voice), SoundState.Paused);
    assert.equal(audio.position(voice), 500);
    context().currentTime = 3;
    audio.resume(voice);
    assert.equal(audio.state(voice), SoundState.Playing);
    assert.deepEqual(context().sources[1].started, { when: 0, offset: 0.5 });
});

test('volume, pan and pitch reach the voice nodes', () => {
    const { audio, gesture, context } = setup();
    gesture();
    const voice = audio.createVoice(0, 0);
    audio.setBuffer(voice, sound(audio));
    audio.setVolume(voice, 0.25);
    audio.setPan(voice, -2);
    audio.setPitch(voice, 1);
    audio.play(voice, 1);
    const source = context().sources[0];
    assert.equal(context().gains[0].gain.value, 0.25);
    assert.equal(context().panners[0].pan.value, -1, 'pan is clamped');
    assert.equal(source.playbackRate.value, 2, 'one octave up doubles the rate');
    audio.setPitch(voice, -1);
    assert.equal(source.playbackRate.value, 0.5);
    audio.setVolume(voice, 0.75);
    audio.setPan(voice, 0.5);
    assert.equal(context().gains[0].gain.value, 0.75);
    assert.equal(context().panners[0].pan.value, 0.5);
    audio.play(voice, 1);
    assert.equal(context().sources[1].playbackRate.value, 0.5);
});

test('a streaming voice queues blocks, plays them in turn and counts the finished ones', () => {
    const { audio, gesture, context } = setup();
    const voice = audio.createVoice(8000, 1);
    audio.appendBuffer(voice, new Uint8Array(1600));
    audio.appendBuffer(voice, new Uint8Array(1600));
    assert.equal(audio.bufferCount(voice), 2);
    audio.play(voice, 1);
    assert.equal(audio.state(voice), SoundState.Playing, 'blocks wait for the context');

    gesture();
    const [first, second] = context().sources;
    assert.deepEqual(first.started, { when: 0, offset: 0 });
    assert.deepEqual(second.started, { when: 0.1, offset: 0 }, 'the second block follows the first');
    first.end();
    assert.equal(audio.finishedCount(voice), 1);
    assert.equal(audio.finishedCount(voice), 0, 'the count is handed over once');
    assert.equal(audio.bufferCount(voice), 1);
    second.end();
    assert.equal(audio.state(voice), SoundState.Stopped, 'a starved stream reports stopped, as FAudio does');
    audio.appendBuffer(voice, new Uint8Array(1600));
    assert.equal(audio.state(voice), SoundState.Playing);
    audio.stop(voice);
    assert.equal(audio.bufferCount(voice), 0);
    assert.equal(audio.state(voice), SoundState.Stopped);
});

test('a voice without a usable buffer stays stopped', () => {
    const { audio, gesture } = setup();
    gesture();
    const voice = audio.createVoice(0, 0);
    assert.equal(audio.createBuffer(new Uint8Array(4), 2, 4, 8000, 1, 0, 0), 0);
    audio.setBuffer(voice, 0);
    audio.play(voice, 1);
    assert.equal(audio.state(voice), SoundState.Stopped);
    audio.destroyVoice(voice);
    assert.equal(audio.state(voice), SoundState.Stopped);
});
