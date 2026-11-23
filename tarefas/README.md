# Tarefas prontas para começar

Cada arquivo desta pasta é um briefing completo para uma IA (ou pessoa) construir um sistema que o painel de
Configurações do Zephira tem e o Noxius ainda não. Abra uma sessão nova, cole o conteúdo do arquivo e peça para começar.
Os sistemas são independentes: cada um pode ser feito num branch próprio, a partir do branch `claude/web-client-85f760`.

Regras que valem para todos (resumo de [AGENTS.md](../AGENTS.md), que a IA deve ler inteiro antes de começar):
- Ler [docs/README.md](../docs/README.md) e o doc da área antes de mexer; atualizar o doc no mesmo trabalho.
- Build Release com `-warnaserror`; nunca ler variáveis de ambiente para configurar nada (configuração fica nos arquivos
  de settings ao lado do executável); código em inglês, conversa com o dono em português.
- Cada mudança de comportamento ganha um caso de regressão (`tests/PristonTale.Tests`, runner de console, ou
  `node --test tests/*.test.mjs` em `Client/PristonTale.Browser`).
- Uma opção nova do jogador vira um campo em `Client/PristonTale.Engine3D/Rendering/ClientSettings.cs` (com `Validate`),
  uma linha em `Client/PristonTale.Browser/wwwroot/settings-options.js` (o teste `SettingsOptionsTests` confere que o
  campo existe) e uma linha em `docs/client-settings.md`. Opções que o painel já mostrou ficam listadas em cada briefing.
- Um sistema que os jogadores vão ver ganha nota em `devnotes/` (perguntar ao dono o tipo antes de escrever).
- Não fazer commit nem push sem o dono pedir. Ao terminar, apagar o arquivo da tarefa desta pasta no mesmo trabalho.
- Para rodar os testes JS sem Node instalado, existe um Node 24 em
  `C:\Users\Felipe\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`. Depois de mudar
  arquivos do renderer (`three-*.js`), regenerar o bundle com `node bundle-worker.mjs` em `Client/PristonTale.Browser`.
- Os sistemas marcados **Regras: aguardam aprovação** começam pela proposta de regras: a IA apresenta as regras ao dono
  (pode ajustar a proposta do briefing) e só implementa depois do ok dele.

| Arquivo | Sistema | Regras |
|---|---|---|
| [02-voz-navegador.md](02-voz-navegador.md) | Chat de voz e microfone no navegador | técnico |
| [09-chat-global.md](09-chat-global.md) | Chat global entre servidores | técnico |
| [16-hud-celular-janelas.md](16-hud-celular-janelas.md) | HUD de celular no navegador: as janelas do Android 0.86 | técnico |
| [17-auto-caca-livre-navegador.md](17-auto-caca-livre-navegador.md) | Auto-caça livre do Android 0.86 no navegador | aprovadas; feita, falta ver no jogo |

Ordem sugerida: 16 e depois 17 (as duas mexem no HUD de celular; uma de cada vez), 02 e 09 quando houver tempo.

## Como continuar em outra IA

O trabalho está publicado no branch `claude/web-client-85f760` do repositório. Para continuar:
1. Fazer checkout desse branch e ler [AGENTS.md](../AGENTS.md) inteiro (as regras do projeto valem para qualquer IA).
2. Preparar as fontes da interface, que ficam fora do git em `assets/Hud/Fonts/` (o build do conteúdo falha sem elas):
   `tools/ui/build_font.py` com a DejaVu Sans 2.37 e, para chinês e japonês, com a Noto Sans SC e a Noto Sans JP do
   repositório notofonts/noto-cjk (o passo a passo está em `docs/tools.md`, seção "UI font").
3. Escolher uma tarefa da tabela acima, abrir uma sessão nova, colar o briefing e pedir para começar.
4. Antes de mexer num sistema que já existe, ler [limites.md](limites.md): a lista do que ficou aquém e onde ajustar.

## Estado do trabalho

Prontas no branch: 01 (som no navegador), 07 (avisos do servidor), 05 (elemento nos números de dano), 06 (câmera 2D
nas dungeons), 08 (mover chat, interface clássica e dicas do tutorial), 04 (tradução do chat; falta o dono pôr a chave
em `ChatTranslation` no `appsettings.json` do servidor), 10 (modo PvP, `docs/application.md#pvp-mode`), 11 (duelos,
`docs/application.md#duels`), 12 (arenas, `docs/application.md#arena`), 13 (Battle Royale,
`docs/application.md#battle-royale`), 15 (títulos, `docs/application.md#titles`), 14 (roleta de boss,
`docs/application.md#boss-roulette`), a 03 inteira (espanhol, filipino, vietnamita, chinês simplificado e japonês, com
fontes próprias para chinês e japonês) e o HUD de celular no navegador. Os briefings delas foram apagados; cada uma tem
a sua nota em `devnotes/` (pastas de 2026-11-19 a 2026-11-21). Na 08 o dono decidiu que a interface clássica vale só
no navegador (o desktop já usa a arte original). Arquivos que mais de uma tarefa mexeu (`ClientSettings.cs`,
`settings-options.js`, `settings.js`, `HudSettingsPanel.cs`, `GameplayHud*.cs`, `docs/client-settings.md`,
`docs/client.md`, `ClientText*.resx`): editar linha a linha, nunca sobrescrever o arquivo inteiro.

Como compilar e testar enquanto o bot do Discord do dono estiver rodando de `Bot/PristonTale.DiscordBot/bin/Release`
(ele trava as DLLs e o build normal falha com MSB3021; não encerrar o processo):
- Compilar os testes numa pasta temporária: `dotnet build tests/PristonTale.Tests -c Release -warnaserror
  -p:BaseOutputPath=<pasta temporária>/`, copiar `<pasta temporária>/Release/net10.0/` para
  `tests/PristonTale.Tests/bin/Release/net10.0/` e rodar `dotnet run --project tests/PristonTale.Tests -c Release
  --no-build`. Os outros projetos (`Client/PristonTale`, `Client/PristonTale.Browser`) compilam do mesmo jeito.
- Resultado esperado hoje: 1072 de 1074. Os dois que falham não são de nenhuma tarefa: o endereço `wss://` em
  `BinaryProtocolTests` e a posição da janela em `WindowPlacementTests`. Às vezes um terceiro falha por tempo (o aviso de
  desligamento do servidor ou a poção consumida só no commit) e passa ao rodar de novo. Testes JS: todos passam. As capturas do HUD
  (`--capture-hud`, em docs/testing.md) usam o Content do build de `Client/PristonTale`.
- Apagar a pasta temporária ao terminar.
