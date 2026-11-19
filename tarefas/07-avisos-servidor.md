# Avisos do servidor: aging, level up, loot do grupo e abertura do Bellatra

**Objetivo:** os avisos automáticos do Zephira (aba Outros, Notificações do chat) que o Noxius ainda não envia, cada um
com sua opção para esconder:
- "Aging de outros jogadores": aviso global quando alguém faz aging alto (proposta: a partir de +10, configurável).
- "Level up de outros jogadores": aviso quando alguém chega a um nível marco (proposta: múltiplos de 10 a partir de 100,
  configurável) — não a cada nível.
- "Alertas de loot do grupo: Todos os itens / Só itens raros / Desligado": aviso no chat do grupo quando um membro pega
  um item.
- "Aviso de abertura do Bellatra: Chat e tela / Só chat / Desligado": quando as inscrições do Bellatra abrem.

**Hoje:** aging em `Shared/PristonTale.Application/World/NpcServiceTransactions.cs` (`AgingWork.TryAge`), level up em
`FieldInstance.cs` (`CombatEventKind.LevelUp`), coleta de itens em `GroundPickupTransactions.cs`, Bellatra em
`BellatraGame.cs` / `WorldServer.Bellatra.cs`. Avisos chegam como `ChatLineInfo` com `GameText`
(`Shared/PristonTale.Contracts/Texts/GameTextId.cs`), traduzidos por `ClientText` (`Game.<id>` nos `.resx`). O cliente
filtra avisos numa regra só: `Client/PristonTale.Gameplay/UI/Chat/ChatLineFilter.cs`. "No topo da tela" = o letreiro de
avisos do HUD (ver como `CastleTaken`/`Siege*` aparecem).

**Caminho sugerido:**
1. Ler `docs/server.md`, `docs/protocol.md` (chat, `GameText`) e `ChatLineFilter`.
2. Para cada aviso: um `GameTextId` novo (com as frases em en e pt-BR, `LocalizationTests`), emitido pelo servidor no
   ponto certo (limites configuráveis no arquivo de configuração do servidor), e o campo do cliente para esconder
   (`HideAgingNotices`, `HideLevelUpNotices`, `PartyLootAlerts` enum All/Rare/Off, `BellatraOpeningNotice` enum
   Both/Chat/Off) aplicado em `ChatLineFilter`.
3. Linhas no cartão "Notificações do chat" de `settings-options.js` e em `docs/client-settings.md`.
4. Testes: servidor emite (com `WorldPump`/`ScriptedRng`), cliente esconde conforme a opção.

**Pronto quando:** os quatro avisos aparecem e cada opção os esconde, sem enviar aviso para cada nível ou aging comum.
