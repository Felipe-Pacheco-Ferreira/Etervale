# "Traduzir para": tradução automática do chat

**Objetivo:** como no Zephira (aba Outros, Idioma, "Traduzir para: Idioma do jogo / Português / English / ..."), as
mensagens de outros jogadores aparecem traduzidas para o idioma escolhido. O dono decidiu usar um serviço pago (DeepL ou
Google Cloud Translation) com a chave dele.

**Decisões já tomadas:**
- A chave fica no arquivo de configuração do **servidor** (o mesmo de onde vêm as outras opções do servidor, ver
  `docs/server.md`), nunca no cliente e nunca em variável de ambiente. O cliente nunca vê a chave.
- O dono preenche a chave; sem chave, a opção some do painel (o servidor diz ao cliente que a tradução está desligada).

**Caminho sugerido:**
1. Ler `docs/server.md`, `docs/protocol.md` (chat) e `Shared/PristonTale.Application/World/WorldServer.Chat.cs`.
2. Novo campo do cliente `ChatTranslateTo` (string: vazio = idioma do jogo, ou um código) + linha em
   `settings-options.js` (`select('chatTranslateTo', 'Traduzir para', ...)`), enviado ao servidor no login e quando muda.
3. No servidor, um serviço de tradução atrás de uma interface (`IChatTranslator`, implementação DeepL ou Google por
   configuração), chamado fora do tick (nada de rede bloqueando o loop: AGENTS.md), com cache por (texto, idioma) e limite
   de caracteres por minuto por jogador para controlar o custo.
4. A linha traduzida chega como uma segunda linha ou substitui o texto, com um marcador "(traduzido)" e o original ao
   passar o mouse/tocar; sussurros, grupo, clã e fala comum; nunca avisos do sistema.
5. Testes com um tradutor falso (sem rede): cache, limite, idioma igual não traduz, falha do serviço mostra o original.

**Pronto quando:** com a chave preenchida, duas contas em idiomas diferentes conversam traduzidas; sem chave a opção não
aparece; o custo é limitado pelo cache e pelo limite; docs e testes atualizados.
