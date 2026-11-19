# Chat de voz e microfone no navegador

**Objetivo:** o jogador do navegador ouve e fala no chat de voz, como no desktop. Painel do Zephira (aba Áudio):
"Ouvir voz dos jogadores" + volume; canais Perto, Grupo, Clã, Todos no mapa; "Falar para" (Perto + grupo, Só o grupo,
Só o clã, Todos no mapa); modo "Pressionar para falar" ou "Microfone aberto (liga/desliga no botão ou B)"; tecla de
falar G; escolha do dispositivo.

**Hoje:** a voz existe no desktop: `Client/PristonTale.Gameplay/Audio/DesktopVoice.cs` (captura PCM 16 kHz em quadros de
640 bytes, `Shared/PristonTale.Contracts/Social/VoiceChannel.cs` `VoiceFormat`), linhas de chat `ChatKind.Voice`
recebidas em `HudChat.Receive` (`VoiceFrame`), servidor em `Shared/PristonTale.Application/World/WorldServer.Chat.cs`.
Campos já existentes: `voiceEnabled`, `voiceReceiveMask`, `voiceVolumePercent`, `voiceTransmitChannel`,
`voiceMicrophone`; atalho "Pressionar para falar" (`HudSettingsPanel.VoiceShortcut`, fora da página web hoje:
`HudSettingsPanel.OnPage`). As linhas de voz e microfone foram retiradas do painel web.
**Depende de:** [01-audio-navegador.md](01-audio-navegador.md) para tocar as vozes recebidas.

**Caminho sugerido:**
1. Ler `docs/client.md` (voz), `docs/protocol.md` (linha de voz) e o `DesktopVoice` inteiro.
2. Captura no navegador: `getUserMedia` + `AudioWorklet` reamostrando para 16 kHz mono em quadros de 640 bytes,
   entregues ao C# por um `[JSExport]` em `BrowserHost` (arquivo novo `BrowserHost.Voice.cs`); o C# envia como o
   desktop. Permissão do microfone pedida no clique de ativar.
3. Reprodução: os quadros recebidos tocam pelo backend Web Audio da tarefa 01 (ou por um `AudioWorklet` próprio).
4. Uma implementação comum: extrair de `DesktopVoice` a parte que não é do SDL para uma interface (`IVoiceDevice`) com
   implementação desktop e navegador (AGENTS.md: código de plataforma atrás de uma interface).
5. Novo campo `VoiceOpenMicrophone` (modo microfone aberto) e atalho B para ligar/desligar; lista de dispositivos via
   `enumerateDevices`.
6. Devolver ao painel web os cartões "Chat de voz" e "Microfone" ligados aos campos, e o atalho de voz em `OnPage`.

**Pronto quando:** dois navegadores (e navegador + desktop) conversam em cada canal, o volume e os canais funcionam, o
modo aberto e o pressionar-para-falar funcionam, a captura para quando a aba perde o foco, e há testes da reamostragem
e do enquadramento.
