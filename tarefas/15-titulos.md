# Títulos

**Regras: aguardam aprovação do dono.**

**Objetivo:** o Zephira tem o aviso "Títulos ganhos e perdidos": títulos que o jogador conquista e que aparecem sobre o
nome, alguns permanentes, outros que passam para quem tomar a posição.

**Hoje:** não há sistema de títulos de jogador (só títulos de classe, `GameTextId.ClassTitle`, e de missão). As caixas de
nome são desenhadas por `Client/PristonTale.Gameplay/UI/Widgets/HudCombat.NamePlates.cs` (já tem a linha do clã acima do
nome, que serve de modelo para a linha do título).

**Proposta de regras:**
- Títulos permanentes por conquista: nível 100/120/140, primeiro aging +14, 1000 monstros de um tipo, completar a
  Jornada do Aventureiro. Ficam guardados; o jogador escolhe um para mostrar (ou nenhum) na janela de status.
- Títulos de posição, que passam de mão: "Rei da Arena" (1º do ranking da arena), "Sobrevivente" (vencedor do último
  Battle Royale), "Senhor do Castelo" (líder do clã dono do castelo), "Mais rico" etc. Quando outro jogador assume a
  posição, o título passa para ele e o anterior perde.
- Título mostrado numa linha acima do nome, com cor própria por raridade.
- Avisos no chat: "X conquistou o título Y" e "X perdeu o título Y para Z" (opção "Títulos ganhos e perdidos").
- Títulos não dão atributos (só visual), para não desequilibrar.

**Caminho sugerido (depois do ok):** catálogo de títulos como dados (JSON na Simulation), estado e regras no servidor,
persistência (migração estrutural), presença leva o título escolhido, linha do título nas caixas de nome, seletor na
janela de status, `GameTextId` dos avisos + `HideTitleNotices` em `ChatLineFilter`, linha no painel web. Testes de
conquista, troca de posição e persistência.

**Pronto quando:** títulos são conquistados, escolhidos, mostrados e trocados conforme as regras aprovadas.
