# Duelos entre jogadores

**Regras: aguardam aprovação do dono.** Depende de [10-modo-pvp.md](10-modo-pvp.md) (combate entre jogadores).

**Objetivo:** um jogador desafia outro para um duelo consensual; o Zephira tem o aviso "Duelos de outros jogadores" nas
notificações do chat.

**Proposta de regras:**
- Desafio pelo menu do jogador (botões que aparecem ao clicar em outro jogador, `HudPlayerButtons`) ou comando
  `/duelo Nome`; o outro aceita ou recusa em 20 s.
- O duelo acontece onde os dois estão (fora da cidade), numa área de 15 metros marcada no chão; sair da área por 5 s perde.
- Vence quem deixar o outro com 1 de vida (ninguém morre de verdade, sem perda de experiência ou itens); 3 minutos no
  máximo, empate no tempo.
- Ninguém de fora pode atacar os duelistas nem ser atacado por eles durante o duelo; poções liberadas.
- Aviso no chat do mapa: "X venceu Y em duelo" (opção "Duelos de outros jogadores" para esconder).
- Placar de vitórias e derrotas no perfil (opcional, para os rankings do site).

**Caminho sugerido (depois do ok):** estado do duelo no servidor (`Shared/PristonTale.Application`), regra de quem pode
atacar quem estendendo a do modo PvP, pacotes de convite/aceite/fim, HUD de convite e da área, `GameTextId` do aviso e
`HideDuelNotices` em `ChatLineFilter`, linha no painel web. Testes com `WorldPump`.

**Pronto quando:** dois jogadores duelam do convite ao resultado, ninguém morre de fato, terceiros não interferem e o
aviso respeita a opção.
