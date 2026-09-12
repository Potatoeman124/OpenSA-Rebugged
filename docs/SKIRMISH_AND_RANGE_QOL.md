# Skirmish opponents and selected-unit ranges

Implemented on `codex/skirmish-fill-and-range-toggle`, 2026-09-12.

## Fill Opponents

The **Fill Opponents** dropdown appears beside **Random Map Generator** in the skirmish Players view. Choose Easy AI, Medium AI or Hard AI to fill all AI-capable slots with that difficulty. The choices come from the selected map's bot definitions, so custom maps can supply their own AI types.

- Empty and closed bot-capable slots are filled. Existing bots change to the selected difficulty.
- Human occupants and slots that disallow bots are preserved.
- Existing bot faction, color, team, handicap and spawn settings are preserved by the existing server command.
- Only the host can use the control, while the lobby is configurable. Ready/start/map restrictions remain enforced.
- The dropdown is hidden in multiplayer lobbies, other skirmish tabs, and the RMG view.

## Selected Unit Ranges

A stone-framed green range icon follows the stance controls at the bottom left. Click it or **tap Alt** to toggle the display. It starts off for each game, and its state persists as the selection changes during that game. The frame and icon brighten when enabled.

The overlay draws a dashed circle in the actor owner's color for each selected, visible armed unit or colony. Its radius is the maximum current weapon range across the actor's attack traits, including runtime range modifiers. The circle follows movement; unavailable weapons, dead/removed actors, and actors hidden by fog do not produce circles. This shows maximum reach, not line of sight, firing arcs, target compatibility, or guaranteed shots.

A standalone left or right Alt tap toggles on release. Key repeats do not toggle repeatedly. Alt combined with mouse input or another key is not a tap: existing Alt-click Force Move and other Alt shortcuts retain their meaning. Chat/text focus, mouse capture, hidden controls and focus loss cancel a pending tap. The input listener never consumes commands.

The toggle is local presentation state only. It does not issue gameplay orders, change weapons, or enter synchronization hashes. The icon is drawn in code using the existing command button frame; no external image assets were added.

## Validation

`build-pipeline.cmd validate` passed build, rules/chrome, Lua and asset-policy checks. The existing style-warning baseline is retained; Release compilation has zero warnings and errors.

The opt-in integration check reuses the existing native utility harness:

```powershell
# From engine/, with the same environment used by the other native RMG checks:
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime <empty-output-directory> --wide --qol
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime <another-empty-output-directory> --qol
```

The two runs use 1280x800 and 1024x600. They load real lobby/game widgets and a generated eight-player map, exercise every AI difficulty against an isolated loopback server, verify exceptional slots and UI guards, then start the server. Range checks use real melee/ranged/flying units and a colony, verify weapon radii, movement, paused weapons, selection changes and unchanged world synchronization hashes. Keyboard events are injected through the widget tree, including SDL focus-state fixtures, rather than sent to the user's desktop.

Screenshots and machine-readable results are in ignored local artifacts:

- `artifacts/qol/runtime-wide/`
- `artifacts/qol/runtime-compact/`

The user should still review the controls in a normal game, including their usual Alt command combinations.
