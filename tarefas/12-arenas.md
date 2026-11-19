# Arenas

**Regras: aguardam aprovação do dono.** Depende de [10-modo-pvp.md](10-modo-pvp.md).

**Objetivo:** partidas de arena entre jogadores com horários; o Zephira tem o aviso "Arenas: abertura e resultados".

**Proposta de regras:**
- Arena 1x1 e 3x3, inscrição pelo NPC da arena em Ricarten ou pelo calendário de eventos; abre 3 vezes por dia (horários
  no arquivo de configuração do servidor, como o calendário de eventos `WorldServer.Calendar.cs`).
- Pareamento por faixa de nível (100-119, 120-139, 140+); partida de 5 minutos num mapa de arena separado (instância).
- Vence o time que eliminar o outro; no tempo, vence quem causou mais dano. Morte na arena não tem penalidade.
- Pontos de arena por vitória (+20) e derrota (-10, mínimo 0); ranking semanal no site com prêmio em Coins para os
  3 primeiros de cada faixa.
- Avisos: abertura das inscrições, início e resultado das partidas (opção "Arenas: abertura e resultados").

**Caminho sugerido (depois do ok):** usar o Bellatra como modelo de evento instanciado (`BellatraGame.cs`,
`FieldInstance.Bellatra.cs`, `WorldServer.Bellatra.cs`, `BellatraBooth.cs`), mapa novo importado por
`tools/PristonTale.WorldImport` (ou reaproveitado), persistência dos pontos (migração estrutural, `Persistence/`),
ranking no site (`Web/PristonTale.Web`), avisos com `GameTextId` + `HideArenaNotices` em `ChatLineFilter`. Testes com
`WorldPump` e banco descartável.

**Pronto quando:** inscrição, pareamento, partida, resultado, pontos e ranking funcionam conforme as regras aprovadas.
