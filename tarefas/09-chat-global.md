# Chat global entre servidores

**Objetivo:** como no Zephira ("Chat global de todos os servidores"), um canal de chat que reúne os jogadores de todos
os servidores do Noxius, com a opção de ouvir ou não.

**Hoje:** cada servidor de jogo tem seu chat (`Shared/PristonTale.Application/World/WorldServer.Chat.cs`); o cliente
conhece outros servidores pela lista `GameServers` (`ClientSettings`, tela de seleção de servidor). Não há ligação entre
servidores. O bot do Discord já conversa com o servidor por um canal próprio (`docs/discord-bot.md`, `BotLink`), que
mostra um padrão de serviço externo ligado ao servidor.

**Caminho sugerido:**
1. Ler `docs/server.md`, `docs/protocol.md` (chat) e `docs/discord-bot.md` (ligação bot-servidor).
2. Decidir com o dono a topologia (proposta: um serviço pequeno de "relay" de chat, ou um servidor eleito, com TLS e
   segredo compartilhado no arquivo de configuração de cada servidor).
3. Novo `ChatKind.Global` (ou comando `/g`), com nome do servidor de origem na linha; limites anti-spam e silenciamento
   que valem em todos os servidores.
4. Campo `GlobalChat` (padrão ligado) para ouvir o canal, linha no painel web e docs.
5. Testes com dois servidores em memória trocando linhas.

**Pronto quando:** dois servidores trocam mensagens do canal global, quem desliga a opção não as vê, e moderação e
limites funcionam.
