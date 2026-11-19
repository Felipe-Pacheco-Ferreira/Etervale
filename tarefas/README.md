# Tarefas prontas para começar

Cada arquivo desta pasta é um briefing completo para uma IA (ou pessoa) construir um sistema que o painel de
Configurações do Zephira tem e o Noxius ainda não. Abra uma sessão nova, cole o conteúdo do arquivo e peça para começar.
Os sistemas são independentes: cada um pode ser feito num branch próprio, a partir do commit que trouxe esta pasta.

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
| [01-audio-navegador.md](01-audio-navegador.md) | Som do jogo (música e efeitos) no navegador | técnico |
| [02-voz-navegador.md](02-voz-navegador.md) | Chat de voz e microfone no navegador | técnico |
| [03-idiomas.md](03-idiomas.md) | Espanhol, japonês, vietnamita, chinês e filipino | técnico |
| [04-traducao-chat.md](04-traducao-chat.md) | "Traduzir para": tradução automática do chat | técnico (chave do dono) |
| [05-elemento-dano.md](05-elemento-dano.md) | Ícone e cor do elemento nos números de dano | técnico |
| [06-camera-dungeon.md](06-camera-dungeon.md) | Câmera 2D (vista de cima) em dungeons, Shift+Z | técnico |
| [07-avisos-servidor.md](07-avisos-servidor.md) | Avisos de aging, level up, loot do grupo e abertura do Bellatra | técnico |
| [08-interface.md](08-interface.md) | Mover chat, interface clássica e dicas do tutorial | técnico |
| [09-chat-global.md](09-chat-global.md) | Chat global entre servidores | técnico |
| [10-modo-pvp.md](10-modo-pvp.md) | Modo PvP (PK) e prioridade a jogadores | aguardam aprovação |
| [11-duelos.md](11-duelos.md) | Duelos entre jogadores | aguardam aprovação |
| [12-arenas.md](12-arenas.md) | Arenas | aguardam aprovação |
| [13-battle-royale.md](13-battle-royale.md) | Battle Royale | aguardam aprovação |
| [14-roleta-boss.md](14-roleta-boss.md) | Roleta de boss | aguardam aprovação |
| [15-titulos.md](15-titulos.md) | Títulos ganhos e perdidos | aguardam aprovação |

Ordem sugerida: 01 e 07 primeiro (mais visíveis e sem dependências), depois 05, 06, 08, 03 e 04; 10 antes de 11, 12 e
13 (duelos, arenas e Battle Royale usam as regras de combate entre jogadores do modo PvP); 02 e 09 quando houver tempo.
