# 025. The browser runtime plays sound through Web Audio

Part: runtime (audio, browser build).

## Need

The browser build's audio (`src/web/MGA_web.cpp`, issue 021) kept the state the framework reads back and consumed
every buffer at once: sound effects and streamed buffers ran their course in silence, so the browser client had no
music and no sound effects inside the game.

## Cause

FAudio, the desktop backend, needs an audio device and threads; the browser offers neither to WebAssembly, only the
page's Web Audio API on the page's own thread.

## Change

- `src/web/MGA_web.cpp` implements the `MGA_*` exports over the page's Web Audio. The page supplies it to the .NET
  module as `Module.noxiusAudio` (in Etervale, `Client/PristonTale.Browser/wwwroot/web-audio.js`); the runtime calls
  it through `EM_JS` functions and keeps only ids.
- A buffer becomes an `AudioBuffer` at once: 8-bit unsigned, 16 and 24-bit signed PCM and 32-bit float samples are
  converted to one float array per channel, and the WebAssembly heap keeps no copy. Its loop region becomes the
  source's `loopStart`/`loopEnd`. Other formats (MS-ADPCM, IMA ADPCM, XACT) give an empty buffer (no sound,
  duration 0); the game's sound files are all PCM.
- A voice is a buffer source through its own `GainNode` (volume) and `StereoPannerNode` (pan); pitch in octaves is the
  source's `playbackRate` (2 to the pitch). Play, pause (the position is kept and the next source starts from it),
  resume and stop report Playing, Paused and Stopped as the framework reads them; a one-shot voice reports Stopped
  once its source has ended.
- A streaming voice (`DynamicSoundEffectInstance`) schedules each submitted 16-bit block right after the previous one,
  counts the blocks queued and finished, and, as FAudio does, reports Stopped when nothing is queued.
- The browser allows an `AudioContext` to run only after a user gesture: the page creates it at the first pointer,
  key or touch. Until it runs, a one-shot sound ends at once (it would otherwise play late, all together) and a loop
  stays Playing and starts with the context.
- Reverb, filters and `Apply3D` are not applied (no-ops), as before.
- A page without `Module.noxiusAudio` (`repro/WebGpuRepro`) keeps the previous silent voices.

No exported function changes: the interface version stays 7. `Song` playback still needs the decoder thread of
`common/MGM.cpp`, which a single-threaded WebAssembly build cannot start; Etervale plays its music as sound effects.

## Test

- `Client/PristonTale.Browser/tests/web-audio.test.mjs` (Node, with a fake `AudioContext`): sample conversion, voices
  before and after the first gesture, one-shot end, loop, pause and resume position, volume, pan and pitch, a streaming
  voice's queue and finished count, a buffer that cannot be converted.
- The browser client's Release build links the runtime (`dotnet build PristonTale.sln -c Release -warnaserror`).
