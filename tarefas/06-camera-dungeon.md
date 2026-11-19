# Câmera 2D (vista de cima) em dungeons

**Objetivo:** como no Zephira: opção "Câmera em dungeons: 2D (vista de cima) / 3D (câmera normal)", que "vale só dentro
das dungeons", e o atalho "Câmera 2D / 3D (dungeons)" em Shift+Z. Na vista de cima a câmera olha quase de cima para
baixo, acompanha o personagem e não gira, para ver as salas apertadas.

**Hoje:** a câmera livre é `Client/PristonTale.Engine3D/Rendering/PristonCamera.cs` (`Update`, `View`, `Projection`),
guardada em `GameScene._camera` e criada em `GameScene.Fields.cs` (`ToEntryCameraSettings`, `ToCameraSettings`) e
`GameScene.Warps.cs`. Um campo é dungeon quando `FieldDefinition.Underground` (`FieldState.Dungeon`,
`Shared/PristonTale.Simulation/World/WorldDefinitions.cs`). Já existe uma câmera especial para campos de ação
(`GameScene.UpdateCamera`, `FieldActionCamera`), que serve de modelo. Os atalhos "Modo da câmera" (Z) e "Câmera 2D / 3D"
(Shift+Z) aparecem desabilitados na aba Teclas (`unavailableShortcuts` em `settings-options.js`).

**Caminho sugerido:**
1. Ler `docs/client.md` (câmera e campos de ação) e `CameraTests`.
2. Campo `DungeonCamera` (enum `TopDown`/`Free`, padrão `Free`) em `ClientSettings` + linha no painel web (cartão
   Câmera, aba Vídeo) + `docs/client-settings.md`.
3. Em dungeon com `TopDown`: inclinação fixa perto de 80 graus, distância fixa, sem giro; rolagem só aproxima e afasta;
   colisão da câmera com o teto continua valendo. Fora de dungeon nada muda.
4. Atalho Shift+Z alterna o campo (novo slot no fim de `HudSettingsPanel.Shortcuts`, como F12 e Backspace foram
   adicionados; tirar a linha de `unavailableShortcuts`). Se fizer também "Modo da câmera" (Z), definir o que ele alterna.
5. Testes de câmera (inclinação e seguimento na vista de cima, nada muda fora de dungeon).

**Pronto quando:** numa dungeon a vista de cima funciona e alterna com Shift+Z, os cliques no chão e nos monstros
continuam certos, e fora de dungeon a câmera é a de sempre.
