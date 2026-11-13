# HUD design language (Android Vulkan client)

The visual concept of the Android Vulkan client's HUD and windows, so new screens keep the look the owner approved.
Read it before adding or restyling any HUD piece in `Client/PristonTale.VulkanAndroid`; the code map of each window is
in [android.md](android.md).

## Read this when

- Adding a window, a HUD frame, a button or a badge to the Android Vulkan client.
- Restyling a screen ported from the desktop client.
- Reviewing a HUD change: it should look as if it had always been part of the set.

## The concept

Dark, polished plates framed in gold, lit from above, over the live 3D world. Information is grouped on plates; what can
be acted on glows. The HUD should read as one ornate set (like a jewelled instrument panel), never as flat default
Android widgets.

- **Plates.** Every frame and window is a rounded dark plate (`VulkanHudArt.Plate`, `PlateDrawable`): a vertical
  gradient from `PlateTop` (232, 30·34·42) to `PlateBottom` (236, 10·12·17), a thin gold edge (`Edge`, 176·138·72) and
  rounded corners (10–14 dp). Windows are `VulkanPanel` with `VulkanPanelArt` sections, recessed insets
  (`VulkanPanelArt.Inset`, `WellDrawable`) for lists and item wells.
- **Gold.** Titles, edges and highlights use gold: `MobileHudTheme.Gold` (232·199·129) for text, `EdgeLight`
  (240·208·140) for bright lines and numerals, `Edge` for frames. Body text is `MobileHudTheme.Ink` (236·225·203) with a
  dark shadow; secondary text is the muted teal (168·199·193).
- **Type.** Serif bold for titles, names and numbers on medallions (`Typeface "serif"`); sans for body text. Text over
  the world always has a dark outline or shadow (`VulkanHudArt.Label`).
- **Round bezels.** Portraits sit under a round window in an ornate gold ring with notches and gem studs
  (`VulkanHudArt.OrnateRing`, `Ring`), with a breathing glow and orbiting sparks for the player (`Aura`). NPC portraits
  use the same bezel with blue gems (`NpcGem`). The 3D head is drawn by the game under the window (`HudPlacement`).
- **Medallions and badges.** The level is a dark medallion in a gold ring with a gold serif numeral
  (`VulkanHudArt.LevelBadge`); its stone colour climbs with the level (amber, emerald, sapphire, crimson) and the top
  level burns. Counters of things waiting for the player are small red badges (`BadgeRed`, `RedBadge`, "9+" past nine).
- **Gauges.** Bars are glassy: dark trough, colour gradient (`Gauge` life red, mana blue, stamina green, experience
  gold), a bright top edge, gloss, a lit tip, a fading trail where the value just dropped and a band of light sweeping
  every few seconds. Values ease toward their target (`EasedGauge`).
- **Buttons.** Round orbs with a gold ring and a glyph (`VulkanActionOrb`, `HudGlyph`) for the main actions; pill buttons
  (`MobileHudTheme.Button`) inside windows, the primary one highlighted. Every touch target is at least 40–44 dp.
- **Motion.** Small and constant, never distracting: breathing glows, pulsing "+" when points wait, a bobbing target
  marker, sweeping shine on bars. Animations redraw on a 40 ms timer (`VulkanHudArt.AnimationFrameMs`) unless a value is
  easing.
- **Placement.** Player frame top left, target frame top centre, minimap top right with the quest card under it, action
  wheel bottom right with the belt potions beside it, menu orbs bottom left, experience bar along the bottom edge. Windows
  open over the centre and never cover the action wheel's attack button; a window that points at a HUD piece (tutorial)
  puts its card beside it. The HUD editor lets the player move each piece.
- **Language.** All text the player reads is Brazilian Portuguese.
- **Weight.** It runs on a phone: no figure drawings or decorative layers that cost frames for nothing, no allocations
  per frame, shaders and gradients created once where possible. When in doubt, remove rather than add (the inventory's
  figure silhouette was removed for this reason).

## How to

- **A new window:** derive from the `VulkanPanel` pattern (title, close, sections), put lists in insets, numbers in
  serif gold, actions as pill buttons at the bottom right, refusals as a red line of text, successes in green. Look at
  `VulkanStatusPanel`, `VulkanInventoryPanel` and `VulkanRewardsPanel` first.
- **A new HUD frame:** a `Plate` with the gold edge; if it shows a 3D head, leave a round window and register its square
  so the game draws the head there.
- **A new counter:** a red badge on the orb that opens it.

## Related

- [android.md](android.md): the Vulkan client's windows, HUD editor and tutorials.
- [client.md](client.md): the desktop HUD the Android screens are ported from.
