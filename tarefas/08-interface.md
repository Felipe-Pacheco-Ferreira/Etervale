# Interface: mover chat, interface clássica e dicas do tutorial

**Objetivo:** três opções do Zephira que o Noxius não tem:
- "Mover Chat": a janela de chat pode ser arrastada para outro lugar da tela (e volta com "Redefinir interface").
- "Interface clássica": o HUD com o visual do jogo original em vez do visual novo (definir com o dono o que muda; ver
  passo 1).
- "Dicas do tutorial": balões que explicam o básico (andar, atacar, inventário, habilidades) nas primeiras horas.

**Hoje:** o chat é `Client/PristonTale.Gameplay/UI/Chat/HudChatWindow.cs` (posição fixa, `layout.Browser` muda a
posição no navegador). O HUD usa `HudLayout` (escala fracionária e `CenteredBar` já existem). Não há sistema de tutorial
no Gameplay; o Android tem um (`Client/PristonTale.VulkanAndroid/VulkanTutorial.cs`, `HudLayoutEditor.cs`), que serve de
referência. O painel web tem "Redefinir interface" (`settings.js`, ação `reset-interface`).

**Caminho sugerido:**
1. Perguntar ao dono o que "Interface clássica" deve mudar (proposta: barra de baixo e janelas com a arte original, sem
   os botões e cartões novos do Noxius) antes de implementar essa parte.
2. Mover chat: campo `ChatOffsetX/Y` (ou posição salva), arrastar pela borda do chat quando `MoveChat` está ligado,
   limitado à tela; "Redefinir interface" zera.
3. Dicas do tutorial: campo `TutorialTips` (padrão ligado), uma lista de dicas como dados (JSON), cada uma mostrada uma
   vez por personagem até ser fechada; reaproveitar a ideia do tutorial Android.
4. Linhas no painel web (aba Vídeo, cartão Interface; aba Outros, Missão guiada) e `docs/client-settings.md`.
5. Testes: a posição do chat se mantém e é limitada; uma dica aparece uma vez.

**Pronto quando:** as três opções funcionam no desktop e no navegador e voltam ao padrão com "Redefinir interface".
