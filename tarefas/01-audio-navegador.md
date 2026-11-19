# Som do jogo no navegador

**Objetivo:** o cliente web toca a música dos mapas e os efeitos sonoros (golpes, habilidades, passos, chuva, NPCs) como
o cliente desktop, respeitando "Ativar música", "Ativar efeitos sonoros" e os volumes do painel.

**Hoje:** o navegador é mudo dentro do jogo. O fork do MonoGame usa um backend de áudio silencioso,
`monogame/runtime/src/web/MGA_web.cpp` ("until the Web Audio backend exists"): as vozes consomem os buffers sem tocar.
Só a música de abertura toca, por HTML audio, na página (`main.js`). No painel web (`settings-options.js`, aba Áudio) há
apenas a música, com uma legenda dizendo que vale só para a abertura; as linhas "Efeitos sonoros" (campos
`soundEnabled`, `soundVolumePercent`) foram retiradas porque não faziam nada.

**Caminho sugerido:**
1. Ler `monogame/issues/021-webgpu-backend.md` e `monogame/issues/README.md` (como estender o fork: documento da issue,
   versão da interface, patch regenerado) e `docs/web-client.md` (seção do runtime web e as limitações do áudio).
2. Implementar `MGA_web.cpp` sobre Web Audio: um `AudioContext` criado na primeira interação do usuário (política de
   autoplay), `MGA_Buffer` como `AudioBuffer` decodificado do PCM que o framework entrega, `MGA_Voice` como
   `AudioBufferSourceNode` + `GainNode` + `StereoPannerNode` (volume, pitch via `playbackRate`, pan, loop), estados
   Playing/Paused/Stopped devolvidos ao framework e a fila de buffers dinâmicos (`DynamicSoundEffectInstance`, usado por
   `Song`/streaming e pela voz). Chamadas C++ -> JS via `EM_JS`/`EM_ASM`, como o resto de `src/web`.
3. Conferir em `Client/PristonTale.Gameplay/Audio/` (`PristonAudioLibrary`, `FieldAudioController`, `SkillSoundBank`)
   que nada pula o áudio no navegador (`OperatingSystem.IsBrowser()`), e que os arquivos `.wav` chegam ao sistema de
   arquivos do navegador (`BootstrapFiles`, packs web em `tools/PristonTale.WebAssetPacks`).
4. Remover a música de abertura duplicada se o jogo passar a tocá-la, ou manter a da página só até o jogo carregar.
5. Devolver ao painel web o cartão "Efeitos sonoros" (`toggle('soundEnabled', ...)`, `volume('soundVolumePercent', 100)`)
   e tirar a legenda "vale só para a abertura".

**Pronto quando:** música e efeitos tocam no Chrome, Edge e Firefox (Windows; citar Linux/macOS não verificados),
os quatro controles do painel mudam o som na hora, o desempenho do quadro não piora (medir com o benchmark de
`docs/testing.md`) e os testes cobrem o estado das vozes (teste JS do lado da página e/ou C#).
