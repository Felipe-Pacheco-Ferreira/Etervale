# HUD de celular no navegador: janelas do Android

**Objetivo:** terminar de trazer o HUD do Android 0.86 para o jogo no site em modo celular. Hoje a parte de baixo,
os quadros do personagem e do alvo, o menu e o chat já são os do Android. Faltam as janelas, que ainda são as do PC
desenhadas no canvas.

**Referência:** o Android 0.86 é o commit `2e05dbea` do branch `continuar-android-060` deste mesmo repositório. Faça
checkout dele numa pasta à parte, só para leitura. O HUD do Android fica em `Client/PristonTale.VulkanAndroid/`:
`VulkanHudLifecycle.cs` monta tudo, e cada janela tem o seu arquivo. As janelas do Android são Views e Canvas do
Android; no navegador elas viram HTML e Canvas 2D por cima do jogo.

**Hoje no navegador:** o mapa do código está em `docs/web-client.md`, seção "Phones and tablets". Leia antes de tudo.
- `Client/PristonTale.Browser/wwwroot/touch-hud.js`: o HUD de toque da página. Lê o estado do jogo a cada 250 ms
  (`TouchState`, JSON) e manda ações (`RunWorldAction`, `TouchChatSend`).
- `Client/PristonTale.Browser/wwwroot/hud-art.js`: a arte do Android passada para Canvas 2D: placas, orbes, bordas
  das habilidades, barras, emblema de nível e placa do título. Reaproveite daqui em vez de desenhar de novo.
- `Web/PristonTale.Web/Pages/Jogar.cshtml` (bloco `touch-hud`) e `Web/PristonTale.Web/wwwroot/css/play.css` (tamanhos
  em `--touch-unit`, 1% do lado menor da tela, como os dp do Android).
- `Client/PristonTale.Gameplay/UI/GameplayHud.Touch.cs`: o estado que a página lê. `BrowserHost.Touch.cs`: as
  funções `[JSExport]` que a página chama.
- `WebWorldHud.TouchActive` decide se o HUD de toque está em uso. Com ele em uso, `GameplayHud.cs` e
  `GameplayHud.Input.cs` deixam de desenhar o que a página substitui, e o minimapa do canvas vai para o canto de cima
  (`HudLayout.Touch`, `HudMinimap.Box`). Cada janela nova que a página assumir sai do canvas do mesmo jeito.

**O que falta (uma janela por vez, cada uma testável sozinha):**
1. ~~Cartão de missão e diário~~: pronto (`touch-quests.js`, `GameplayHud.TouchQuests.cs`, `docs/web-client.md`).
2. ~~Minimapa e mapa do mundo~~: pronto (`touch-map.js`, `GameplayHud.TouchMap.cs`, `HudMinimap` com `HudLayout.Touch`).
3. ~~Bolsa~~: pronto (`touch-bag.js`, `GameplayHud.TouchBag.cs`, `HudItemActions`).
4. ~~Status e habilidades~~: pronto (`touch-status.js`, `touch-skills.js`, `GameplayHud.TouchStatus.cs`, `GameplayHud.TouchSkills.cs`).
5. ~~Diálogo com NPC~~: pronto (`touch-npc.js`, `GameplayHud.TouchNpc.cs`); mostra também o diálogo de missão e as caixas de
   sim/não.
6. ~~Registro de combate~~: pronto (`touch-feed.js`, `GameplayHud.TouchFeed.cs`, `HudCombatFeed`).
7. ~~Grupo e botões do jogador alcançado~~: pronto (`touch-party.js`, `GameplayHud.TouchParty.cs`); traz também os
   pedidos de grupo, clã, troca e duelo numa placa só.
8. ~~Recompensas e morte~~: pronto (`touch-rewards.js`, `touch-restart.js`, `GameplayHud.TouchRewards.cs`,
   `GameplayHud.TouchRestart.cs`).
9. ~~Segurar o botão de uma habilidade~~: pronto (`touch-skill-picker.js`, `GameplayHud.TouchSkillChoices`,
   `TouchSkillSlots.Eligible`); tocar num botão vazio abre a mesma lista.
10. Editor de HUD e tutoriais.
    - ~~Editor de HUD~~: pronto (`touch-layout.js`, entrada Editar HUD no menu, `BrowserHost.SetTouchFrameShift`).
      Mapas baixados e Sair do jogo não entram: o site baixa os pacotes sozinho e a página não fecha a própria aba.
    - ~~Tutoriais~~: pronto (`touch-tutorial.js`, `GameplayHud.TouchTutorial.cs`, entrada Rever tutorial no menu).

**Como fazer cada janela:**
1. Ler a janela do Android e o que ela lê da sessão; achar no navegador quem já tem esses dados (`GameplayHud`,
   `HudCombat`, `HudQuestWindow`...) e expor só o que falta pelo `TouchState` ou por uma função nova em
   `BrowserHost.Touch.cs`. A regra do jogo continua num lugar só (Simulation ou Gameplay); a página só mostra.
2. Desenhar em HTML e Canvas 2D com a arte de `hud-art.js`, textos nas tabelas de `touch-hud.js` (pt-BR, en) e em
   `languages/*.js` (es, fil, vi, zh-CN, ja).
3. Tirar a janela do PC do canvas enquanto `TouchHudShown` vale, como foi feito com a barra de baixo e o chat.
4. Caso de regressão (`tests/PristonTale.Tests/TouchInputTests.cs` para o C#, `tests/touch-hud.test.mjs` para a
   página), atualizar `docs/web-client.md` e conferir com capturas em tamanho de celular deitado (844x390).

**Como ver no jogo de verdade:** o caminho está em `docs/web-client.md`, seção "Run it". Os passos que deram certo
com o bot do dono rodando:
- Compilar servidor, `tools/PristonTale.WorldImport`, `tools/PristonTale.DevAccounts` e o site com
  `-p:BaseOutputPath=<pasta temporária>/`.
- Importar o mundo num banco descartável na pasta temporária: `ptworld import --sqlite <banco> --source
  assets/GameServer --assets assets`. Criar uma conta de teste e um personagem: `ptdevaccounts --sqlite <banco>
  create-account <conta> <senha>` e `add-char <conta> <nome> Fighter 60`.
- Publicar o cliente com AOT (`dotnet publish Client/PristonTale.Browser -c Release -warnaserror -m:1
  -p:BaseOutputPath=... -o <pasta>`, 15 a 35 minutos). Sem AOT (`EtervaleAot=false`) o runtime não carrega.
  Depois rodar `tools/PristonTale.WebAssetPacks` sobre `<pasta>/wwwroot` e juntar tudo numa pasta `play`, como faz
  `publish.ps1`.
- Rodar o servidor com `--Database:SqlitePath=<banco> --Network:WebSocket:Enabled=true
  --Network:WebSocket:Url=https://localhost:10010 --Network:WebSocket:AllowedOrigins:0=https://localhost:5443` e o
  site com `--Database:SqlitePath=<cópia do banco> --Play:ClientPath=<pasta play>
  --Play:GameEndpoint=wss://localhost:10010/game`, a partir de `Web/PristonTale.Web`.
- Abrir `https://localhost:5443/`, pôr Configurações, Outros, Tipo de interface em "Celular / tablet (toque)", entrar,
  e olhar em 844x390.
- Ao terminar, parar os dois e apagar a pasta temporária.

**Pronto quando:** em modo celular nenhuma janela do PC aparece no canvas, cada janela do Android abre, funciona e
fecha no navegador como no 0.86, com o mesmo visual, e foi vista no jogo local e, se possível, num celular de verdade.
