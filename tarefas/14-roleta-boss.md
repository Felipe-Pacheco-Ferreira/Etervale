# Roleta de boss

**Regras: aguardam aprovação do dono.**

**Objetivo:** o Zephira tem o aviso "Vencedores da Roleta de boss": quando um boss morre, um sorteio entre quem lutou
dá um prêmio extra, anunciado no chat.

**Hoje:** bosses e o aviso de horário de boss existem (`GameTextId.BossTime`, filtrado por `HideBossNotices` em
`ChatLineFilter`); o loot de boss vem das tabelas de drop do servidor.

**Proposta de regras:**
- Ao morrer um boss de mapa (os que têm horário), entram na roleta todos os jogadores que causaram pelo menos 1% da vida
  do boss em dano ou curaram quem lutava.
- A chance de cada um é proporcional à contribuição, com teto de 30% para que todos tenham chance.
- Um sorteado ganha um prêmio de uma tabela própria da roleta (dados por boss, no banco ou JSON): pedras de aging, poções
  raras ou Coins; o item vai para o distribuidor de itens (já existe no site/jogo) para não cair no chão.
- Aviso no chat de todos os mapas: "X ganhou Y na roleta do boss Z" (opção "Vencedores da Roleta de boss").
- O sorteio usa o gerador do servidor (`ISimRng`), registrado em log para auditoria.

**Caminho sugerido (depois do ok):** registrar contribuição por boss no servidor (dano já é resolvido em `FieldCombat`),
sorteio na morte, tabela de prêmios como dados, entrega pelo distribuidor, `GameTextId` do aviso e
`HideBossRouletteNotices` em `ChatLineFilter`, linha no painel web. Testes com `ScriptedRng`/`SeededSimRng`.

**Pronto quando:** a morte de um boss sorteia conforme as regras aprovadas, o prêmio chega ao distribuidor e o aviso
respeita a opção.
