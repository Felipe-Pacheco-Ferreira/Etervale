# Ícone e cor do elemento nos números de dano

**Objetivo:** como no Zephira (aba Vídeo, Números de dano): "Ícone do elemento" mostra o ícone do elemento ao lado do
número e "Colorir número pelo elemento" pinta o número (fogo, gelo, raio, veneno, ...).

**Hoje:** o servidor não envia o elemento do golpe. `Client/PristonTale.Client.Net/Presence/CombatEventInfo.cs` tem
atacante, alvo, tipo, dano, crítico etc., sem elemento. Os elementos existem na simulação:
`Shared/PristonTale.Simulation/Combat/Elements.cs` (`Element`: Bionic, Earth, Fire, Ice, Lighting, Poison, Water, Wind)
e os dados de habilidade têm `"element"` (por exemplo `Skills/Data/Classes/Archer.json`). Os números são desenhados por
`Client/PristonTale.Gameplay/Effects/FloatingCombatText.cs` (já tem tamanho, espalhar e dano de outros) a partir de
`SkillPresentation.ObserveCombat`.

**Caminho sugerido:**
1. Ler `docs/protocol.md` (eventos de combate), `docs/skills.md` e a resolução do golpe em
   `Shared/PristonTale.Application/World/FieldCombat.cs` / `FieldSkills*.cs` para saber de onde vem o elemento do dano.
2. Acrescentar o elemento (um byte, "nenhum" para golpe físico) ao evento de combate: `CombatEventView` no servidor, o
   pacote em `Shared/PristonTale.Protocol`, `PresenceDecoder` e `CombatEventInfo` no cliente. Protocolo próprio do
   Etervale: pode crescer (AGENTS.md).
3. `FloatingCombatText.Add` recebe o elemento; cor por elemento numa tabela (dados, não `switch` repetido); ícone pequeno
   desenhado ao lado (usar a arte de elemento do jogo, se houver, via `AssetFiles`).
4. Campos `ShowElementIcon` (padrão ligado) e `ColorDamageByElement` (padrão desligado) em `ClientSettings` + linhas em
   `settings-options.js` no cartão "Números de dano" + `docs/client-settings.md`.
5. Testes: o pacote leva e traz o elemento; a cor e o ícone seguem as opções.

**Pronto quando:** golpes de fogo/gelo/raio/veneno mostram a cor e o ícone certos no desktop e no navegador, golpes
físicos não mudam, e o protocolo tem teste de ida e volta.
