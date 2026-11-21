// The game's sound in the browser: the MonoGame fork's audio voices (monogame/runtime/src/web/MGA_web.cpp) call these
// functions through the module's noxiusAudio. A sound buffer is an AudioBuffer converted from the PCM the framework
// hands over; a voice is a buffer source through its own gain and stereo panner. The AudioContext starts at the page's
// first user gesture (the autoplay policy): until it runs, a one-shot sound ends at once and a loop waits for it.

export const SoundState = Object.freeze({ Playing: 0, Paused: 1, Stopped: 2 });
export const SampleFormat = Object.freeze({ Pcm: 1, Float: 3 });

const GestureEvents = ['pointerdown', 'keydown', 'touchend'];

// Interleaved little-endian samples to one Float32Array per channel: 8-bit unsigned, 16 and 24-bit signed integers,
// 32-bit floats. Returns null for any other layout.
export function decodeSamples(bytes, format, bits, channels) {
    const size = bits / 8;
    if (channels < 1 || !(format === SampleFormat.Pcm && (bits === 8 || bits === 16 || bits === 24)
        || format === SampleFormat.Float && bits === 32)) {
        return null;
    }

    const frames = Math.floor(bytes.length / (size * channels));
    const data = Array.from({ length: channels }, () => new Float32Array(frames));
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    let at = 0;
    for (let frame = 0; frame < frames; frame++) {
        for (let channel = 0; channel < channels; channel++, at += size) {
            let sample;
            if (format === SampleFormat.Float) sample = view.getFloat32(at, true);
            else if (bits === 8) sample = (bytes[at] - 128) / 128;
            else if (bits === 16) sample = view.getInt16(at, true) / 32768;
            else sample = ((bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16) << 8 >> 8) / 8388608;
            data[channel][frame] = sample;
        }
    }

    return data;
}

// createContext and createAudioBuffer are the browser's AudioContext and AudioBuffer constructors unless a test
// supplies its own; target receives the gesture listeners.
export function createWebAudio({
    createContext = () => new AudioContext(),
    createAudioBuffer = options => new AudioBuffer(options),
    target = globalThis
} = {}) {
    const buffers = new Map();
    const voices = new Map();
    let nextId = 1;
    let context = null;

    function unlock() {
        if (!context) {
            try {
                context = createContext();
            } catch (error) {
                console.warn('Web Audio:', error);
                return;
            }

            for (const voice of voices.values()) {
                if (voice.state === SoundState.Playing) begin(voice);
            }
        }

        if (context.state === 'suspended') context.resume().catch(() => {});
    }

    for (const name of GestureEvents) target?.addEventListener?.(name, unlock, { capture: true, passive: true });

    const running = () => context?.state === 'running';
    const rate = voice => 2 ** voice.pitch;

    function output(voice) {
        if (!voice.gain) {
            voice.gain = context.createGain();
            voice.gain.gain.value = voice.volume;
            voice.panner = context.createStereoPanner();
            voice.panner.pan.value = voice.pan;
            voice.gain.connect(voice.panner);
            voice.panner.connect(context.destination);
        }

        return voice.gain;
    }

    // Starts what a playing voice has to play: its buffer from the kept offset, or its queued blocks.
    function begin(voice) {
        if (!context) return;
        if (voice.dynamic) {
            schedule(voice);
            return;
        }

        const entry = voice.buffer;
        const source = context.createBufferSource();
        source.buffer = entry.audio;
        source.loop = voice.looped;
        if (voice.looped && entry.loopEnd > entry.loopStart) {
            source.loopStart = entry.loopStart;
            source.loopEnd = entry.loopEnd;
        }

        source.playbackRate.value = rate(voice);
        source.connect(output(voice));
        source.onended = () => {
            if (voice.source === source) voice.ended = true;
        };
        voice.source = source;
        voice.ended = false;
        voice.startedAt = context.currentTime;
        source.start(0, voice.offset);
    }

    function schedule(voice) {
        while (context && voice.queue.length > 0) {
            const audio = voice.queue.shift();
            const source = context.createBufferSource();
            source.buffer = audio;
            source.playbackRate.value = rate(voice);
            source.connect(output(voice));
            const block = { source, audio };
            source.onended = () => {
                const index = voice.scheduled.indexOf(block);
                if (index < 0) return;
                voice.scheduled.splice(index, 1);
                voice.finished++;
                voice.played += audio.duration;
            };
            const when = Math.max(voice.nextTime, context.currentTime);
            source.start(when);
            voice.nextTime = when + audio.duration / rate(voice);
            voice.scheduled.push(block);
        }
    }

    // Seconds into the buffer a playing voice has reached, within its loop.
    function offset(voice) {
        if (!voice.source || !context) return voice.offset;
        const duration = voice.buffer.audio.duration;
        let at = voice.offset + (context.currentTime - voice.startedAt) * rate(voice);
        const { loopStart, loopEnd } = voice.buffer;
        const end = loopEnd > loopStart ? loopEnd : duration;
        if (voice.looped && at >= end) {
            const start = loopEnd > loopStart ? loopStart : 0;
            at = start + (at - start) % (end - start);
        }

        return Math.min(at, duration);
    }

    function halt(voice) {
        if (voice.source) {
            const source = voice.source;
            voice.source = null;
            source.onended = null;
            try {
                source.stop();
            } catch {
                // A source that never started cannot stop.
            }
            source.disconnect();
        }

        for (const { source } of voice.scheduled) {
            source.onended = null;
            source.stop();
            source.disconnect();
        }

        voice.scheduled = [];
        voice.nextTime = 0;
    }

    function voiceOf(id) {
        return voices.get(id);
    }

    return {
        createBuffer(bytes, format, bits, sampleRate, channels, loopStart, loopLength) {
            const data = decodeSamples(bytes, format, bits, channels);
            if (!data || data[0].length === 0 || sampleRate <= 0) return 0;
            const audio = createAudioBuffer({ length: data[0].length, numberOfChannels: channels, sampleRate });
            data.forEach((samples, channel) => audio.copyToChannel(samples, channel));
            const id = nextId++;
            buffers.set(id, {
                audio,
                loopStart: loopLength > 0 ? loopStart / sampleRate : 0,
                loopEnd: loopLength > 0 ? (loopStart + loopLength) / sampleRate : 0
            });
            return id;
        },

        destroyBuffer(id) {
            buffers.delete(id);
        },

        // sampleRate and channels name the format of the blocks a streaming voice receives; 0 for a sound's voice.
        createVoice(sampleRate, channels) {
            const id = nextId++;
            voices.set(id, {
                dynamic: sampleRate > 0 && channels > 0, sampleRate, channels, buffer: null,
                state: SoundState.Stopped, looped: false, volume: 1, pitch: 0, pan: 0,
                source: null, ended: false, offset: 0, startedAt: 0,
                queue: [], scheduled: [], finished: 0, played: 0, nextTime: 0,
                gain: null, panner: null
            });
            return id;
        },

        destroyVoice(id) {
            const voice = voiceOf(id);
            if (!voice) return;
            halt(voice);
            voice.gain?.disconnect();
            voice.panner?.disconnect();
            voices.delete(id);
        },

        setBuffer(id, bufferId) {
            const voice = voiceOf(id);
            if (!voice) return;
            halt(voice);
            voice.buffer = buffers.get(bufferId) ?? null;
            voice.state = SoundState.Stopped;
            voice.offset = 0;
        },

        // A block of 16-bit samples in the voice's format.
        appendBuffer(id, bytes) {
            const voice = voiceOf(id);
            if (!voice?.dynamic) return;
            const data = decodeSamples(bytes, SampleFormat.Pcm, 16, voice.channels);
            if (!data || data[0].length === 0) return;
            const audio = createAudioBuffer({ length: data[0].length, numberOfChannels: voice.channels, sampleRate: voice.sampleRate });
            data.forEach((samples, channel) => audio.copyToChannel(samples, channel));
            voice.queue.push(audio);
            if (voice.state === SoundState.Playing) schedule(voice);
        },

        bufferCount(id) {
            const voice = voiceOf(id);
            return voice ? voice.queue.length + voice.scheduled.length : 0;
        },

        finishedCount(id) {
            const voice = voiceOf(id);
            if (!voice) return 0;
            const finished = voice.finished;
            voice.finished = 0;
            return finished;
        },

        play(id, looped) {
            const voice = voiceOf(id);
            if (!voice) return;
            halt(voice);
            voice.offset = 0;
            voice.finished = 0;
            voice.looped = !!looped;
            if (!voice.dynamic && (!voice.buffer || !voice.looped && !running())) {
                voice.state = SoundState.Stopped;
                return;
            }

            voice.state = SoundState.Playing;
            begin(voice);
        },

        pause(id) {
            const voice = voiceOf(id);
            if (voice?.state !== SoundState.Playing) return;
            if (voice.dynamic) {
                voice.queue.unshift(...voice.scheduled.map(block => block.audio));
            } else {
                voice.offset = offset(voice);
            }

            halt(voice);
            voice.state = SoundState.Paused;
        },

        resume(id) {
            const voice = voiceOf(id);
            if (!voice) return;
            if (voice.state !== SoundState.Paused) {
                this.play(id, voice.looped);
                return;
            }

            voice.state = SoundState.Playing;
            begin(voice);
        },

        stop(id) {
            const voice = voiceOf(id);
            if (!voice) return;
            halt(voice);
            voice.queue = [];
            voice.finished = 0;
            voice.played = 0;
            voice.offset = 0;
            voice.state = SoundState.Stopped;
        },

        state(id) {
            const voice = voiceOf(id);
            if (!voice) return SoundState.Stopped;
            if (voice.state === SoundState.Playing && !voice.dynamic && voice.ended) {
                halt(voice);
                voice.offset = 0;
                voice.state = SoundState.Stopped;
            }

            // As FAudio reports it: a streaming voice with nothing queued has stopped.
            if (voice.state === SoundState.Playing && voice.dynamic && voice.queue.length + voice.scheduled.length === 0) {
                return SoundState.Stopped;
            }

            return voice.state;
        },

        position(id) {
            const voice = voiceOf(id);
            if (!voice) return 0;
            if (voice.dynamic) return Math.floor(voice.played * 1000);
            return voice.buffer ? Math.floor(offset(voice) * 1000) : 0;
        },

        setVolume(id, volume) {
            const voice = voiceOf(id);
            if (!voice) return;
            voice.volume = volume;
            if (voice.gain) voice.gain.gain.value = volume;
        },

        // In octaves, as SoundEffectInstance.Pitch.
        setPitch(id, pitch) {
            const voice = voiceOf(id);
            if (!voice || voice.pitch === pitch) return;
            if (voice.source && context) {
                voice.offset = offset(voice);
                voice.startedAt = context.currentTime;
            }

            voice.pitch = pitch;
            if (voice.source) voice.source.playbackRate.value = rate(voice);
            for (const { source } of voice.scheduled) source.playbackRate.value = rate(voice);
        },

        setPan(id, pan) {
            const voice = voiceOf(id);
            if (!voice) return;
            voice.pan = Math.min(Math.max(pan, -1), 1);
            if (voice.panner) voice.panner.pan.value = voice.pan;
        }
    };
}
