# Auto-caça livre no navegador (motor do Android 0.86)

**Objetivo:** o botão Auto-caça do HUD de celular no site já tem o visual do Android 0.86, mas por baixo ainda roda a
caça de missão. Falta trazer a caça livre do Android, com as suas oito abas (Caça, Habilidades, Poções, Segurança,
Coleta, Ganhos, Grupo, Log), para o jogo no navegador. O dono escolheu "visual agora, motor depois": esta tarefa é o
motor.

**Referência:** o Android 0.86 é o commit `2e05dbea` do branch `continuar-android-060` deste mesmo repositório. Faça
checkout dele numa pasta à parte, só para leitura. Leia lá:
- `docs/android.md`, seção "Free auto-hunt": o comportamento de cada aba, o perfil por conta ou por personagem e o
  código de perfil para copiar entre aparelhos.
- As regras, que não dependem do Android: `Client/PristonTale.Gameplay/Combat/FreeHuntSettings.cs`,
  `FreeHuntProfile.cs`, `FreeHuntRange.cs` e as partes de caça em `World/GameScene.AutoMove.cs`,
  `GameScene.Skills.cs`, `GameScene.TargetRing.cs` e `World/Remote/GroundLootPresentation.cs`.
- O lado Android: `Client/PristonTale.VulkanAndroid/VulkanWorldSession.FreeHunt.cs`,
  `VulkanWorldSession.HuntStatistics.cs` e o painel `VulkanFreeHuntPanel*.cs`. São cerca de 2.500 linhas no total.
- A tabela de itens da Coleta, `Assets/auto-hunt-items.tsv`.

**Hoje no navegador:** `docs/web-client.md`, seção "Phones and tablets" (Auto-caça).
- O orbe Auto-caça (`touch-hud.js`, `data-touch-hunt`): um toque abre o painel ou pausa a caça; segurar por 500 ms abre
  o painel. Enquanto caça, o orbe diz Pausar e um aviso verde "Auto-caça · Ligado" fica acima dos orbes.
- O painel (`world-hud.js`, bloco `world-hunt-panel` em `Web/PristonTale.Web/Pages/Jogar.cshtml`, estilo em
  `Web/PristonTale.Web/wwwroot/css/play.css`) tem a cara do painel do Android, mas mostra só a caça de missão.
- O estado vem de `BrowserHost.WorldAutoHuntActive` (`WebWorldHud.Current.AutoHuntEnabled`) e as ações de
  `RunWorldAction("auto-hunt")`, tratada em `GameplayHud.Web.cs`. A caça de missão está em `GameplayHud.Quests.cs` e
  `GameplayScreen.cs`.
- Nenhum dos arquivos `FreeHunt*.cs` existe neste branch.

**Caminho sugerido:**
1. Ler `AGENTS.md`, `docs/web-client.md` e `docs/client.md`. Comparar o `Client/PristonTale.Gameplay` dos dois
   branches para ver o que a caça livre precisa e o que já existe aqui.
2. Trazer as regras para `Client/PristonTale.Gameplay` (ou `Shared/PristonTale.Simulation`, se forem regras do jogo),
   sem nada de Android, com casos de regressão: escolha de alvo pelos filtros, raio, volta ao ponto de partida, ordem
   das habilidades e mana mínima, limites de poção, regras de coleta.
3. Ligar o motor ao jogo no navegador, rodando no `Update` sem bloquear nem alocar por quadro, e usando os mesmos
   comandos do jogo manual. O servidor continua validando tudo (alvo, uso de item, coleta).
4. Guardar os perfis junto dos ajustes do cliente (`docs/client-settings.md`): no desktop ficam ao lado do executável,
   e no navegador ficam no documento `client-settings.json`, que a página guarda (`BrowserHost.Settings.cs`,
   `settings.js`). Nunca usar variáveis de ambiente.
5. Fazer as oito abas no painel da página, com a arte de `hud-art.js` e textos em todos os idiomas da página
   (`touch-hud.js`, `world-hud.js`, `languages/*.js`).
6. Decidir com o dono se a caça livre também vale no HUD de PC do navegador ou só no de celular, e se o código de
   perfil do Android deve ser aceito no site.

**Atenção:** a caça livre mexe em combate, poções e coleta. Antes de começar, apresente ao dono o que vai entrar e o
que fica de fora, e só implemente depois do ok dele.

**Pronto quando:** no site em modo celular, Auto-caça caça sozinha num raio, usa habilidades e poções como configurado,
coleta pelos filtros, mostra ganhos, grupo e registro, pausa e volta como no Android 0.86. Precisa ter sido visto no
jogo local (os passos estão em [16-hud-celular-janelas.md](16-hud-celular-janelas.md), "Como ver no jogo de verdade"),
com casos de regressão para as regras e docs atualizados. Ganha uma nota em `devnotes/`: perguntar o tipo ao dono.

**Estado:** o motor, as oito abas e as peças do servidor estão no branch; falta ver no jogo local (e num celular).
- Decidido com o dono: tudo de uma vez, só no HUD de celular, e o site aceita o código de perfil do Android.
- Regras: `Client/PristonTale.Gameplay/Combat/FreeHunt*.cs`, `HuntPotionSelection.cs`, `FreeHuntProfileStore.cs`, com os casos
  em `tests/PristonTale.Tests/FreeHuntTests.cs`.
- Motor: `World/FreeHuntSession.cs` e `.Statistics.cs`, ligado em `GameplayScreen.FreeHunt.cs` e `GameplayHud.TouchHunt.cs`.
- Servidor e protocolo: `PotionPotency`, `CrystalPetTargeting`, `PartyHunt` (código `0x45540706`, porque `0x45540705` já
  era a lista de grupos neste branch) e a bandeira de item jogado pelo próprio jogador em `GroundItems`.
- Painel: `touch-hunt.js` com o catálogo `auto-hunt-items.tsv`; perfis e registros em `client-settings.json`.
- Antes de apagar este arquivo: ver no jogo local caçando, coletando, usando poções e habilidades, pausando e voltando,
  com um segundo jogador em grupo para a aba Grupo, e escrever a nota em `devnotes/` (perguntar o tipo ao dono).

**Onde parou (para continuar):**
- Verificado: build Release com `-warnaserror` sem avisos dos testes, do cliente do navegador e do servidor (só Windows);
  regressão C# 1094/1096 (as duas falhas conhecidas: endereço `wss://` e posição da janela); testes JS 349/349.
- A revisão independente só de leitura que o AGENTS.md pede para mudança grande foi interrompida antes de terminar:
  refazer, sobre os arquivos listados acima e os trechos de servidor (`PresenceEncoder`, `WorldPlayer`, `FieldCombat`,
  `Party`, `FieldGroundLoot`, `FieldSkills.Summons`) e de cena (`GameScene.Skills`, `AutoMove`, `Items`, `TargetRing`).
  Conferir em especial o formato do JSON entre `TouchHunt`/`FreeHuntSettings` e `touch-hunt.js`.
- Depois: ver no jogo local (passos em [16-hud-celular-janelas.md](16-hud-celular-janelas.md), "Como ver no jogo de
  verdade"), corrigir o que aparecer, perguntar o tipo da nota ao dono e escrever a nota em `devnotes/`.
- Nada foi para commit.
