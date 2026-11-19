# Modo PvP (PK)

**Regras: aguardam aprovação do dono.** A IA apresenta a proposta abaixo (ajustada se precisar) e só implementa depois
do ok.

**Objetivo:** como no Zephira ("Modo PvP" na aba Vídeo, "Modo PK: ligar / desligar" na tecla K), o jogador escolhe se
pode atacar e ser atacado por outros jogadores fora do castelo. Base para duelos, arenas e Battle Royale.

**Hoje:** jogador contra jogador só existe no Castelo Bless (`Client/PristonTale.Gameplay/World/GameScene.cs` `Fights`,
`Simulation.Social.Castle.CastleSiegeRules.MayFight`). "Prioridade a jogadores" já existe e escolhe um jogador sob o
cursor antes de um monstro (`RemoteEntityManager.TryPick`).

**Proposta de regras:**
- Liga e desliga em K ou no painel; ligar vale na hora; desligar só depois de 60 s sem atacar ou ser atacado por jogador.
- Só em mapas de caça (nunca em cidades, `FieldState.Village`, nem em mapas marcados seguros); nível mínimo 50.
- Dois jogadores se atacam só se os dois estão com PvP ligado; membros do mesmo grupo e do mesmo clã não se atacam.
- Nome com marca vermelha para quem está com PvP ligado; quem mata outro jogador fica "assassino" por 10 minutos
  (nome vermelho) e pode ser atacado por qualquer jogador com PvP ligado.
- Morte em PvP: sem perda de experiência e sem queda de itens; volta na cidade como numa morte comum.
- Dano entre jogadores reduzido a 50% do dano contra monstros (configurável no servidor).

**Caminho sugerido (depois do ok):** regras na Simulation (`Shared/PristonTale.Simulation`, uma regra só, usada pelo
servidor e pelo cliente), autoridade no servidor (`Shared/PristonTale.Application`), estado no protocolo e na presença
dos jogadores, campo do cliente só para a tecla (o estado vive no servidor), linha "Modo PvP" no painel web e atalho K
(tirar de `unavailableShortcuts`). Testes com `WorldPump`: quem pode atacar quem, cooldown de desligar, cidade segura.

**Pronto quando:** as regras aprovadas valem no servidor, o cliente mostra quem está com PvP e quem é assassino, e há
regressão para cada regra.
