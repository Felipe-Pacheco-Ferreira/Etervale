# Battle Royale

**Regras: aguardam aprovação do dono.** Depende de [10-modo-pvp.md](10-modo-pvp.md).

**Objetivo:** um evento de último sobrevivente; o Zephira tem o aviso "Battle Royale" nas notificações do chat.

**Proposta de regras:**
- Evento 2 vezes por dia (horários no arquivo de configuração do servidor, aparece no calendário); inscrição 10 minutos
  antes no NPC do evento; de 10 a 50 jogadores, todos a partir do nível 80.
- Todos entram num mapa fechado com o mesmo equipamento padrão por classe (o equipamento próprio fica guardado), para
  ser justo entre níveis; status nivelados no nível 100.
- Baús espalhados pelo mapa dão poções e itens temporários do evento.
- A área segura encolhe a cada 2 minutos; fora dela o jogador perde 5% da vida por segundo.
- Todos contra todos; morreu, sai do evento e assiste; vence o último vivo. Sem penalidade de morte.
- Prêmios: 1º, 2º e 3º recebem Coins e um título temporário ("Sobrevivente", ver [15-titulos.md](15-titulos.md)); todos
  os que participaram ganham um pouco de experiência.
- Avisos: inscrições abertas, início, vencedor (opção "Battle Royale").

**Caminho sugerido (depois do ok):** mesmo modelo de evento instanciado do Bellatra (`BellatraGame.cs` etc.), um mapa
importado ou adaptado, kit de equipamento por classe como dados (JSON), zona segura na Simulation (regra única), HUD da
zona no minimapa, avisos `GameTextId` + `HideBattleRoyaleNotices` em `ChatLineFilter`. Testes com `WorldPump`.

**Pronto quando:** um evento completo roda do anúncio ao prêmio com as regras aprovadas e cada regra tem regressão.
