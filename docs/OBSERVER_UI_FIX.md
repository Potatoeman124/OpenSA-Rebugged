# Observer dropdown crash fix

Implemented on `codex/observer-chrome-fix`, 2026-09-12.

Opening the observer camera dropdown crashed while drawing its first group header:
`Panel observer-scrollheader was not found`. The common spectator templates use
observer-prefixed chrome names, but OpenSA did not define those panels. The
selected rows could likewise fail on `observer-scrollitem-highlighted`; the
scrollbar and panel background silently lacked artwork.

`mods/sa/chrome.yaml` now supplies aliases to the existing OpenSA header, row and
scrollbar artwork, including hover, pressed, selected and disabled states where
applicable. Both shared spectator templates are covered. No engine changes or new
art assets are required, and existing maps can be used without regeneration.

The native visual check also exposed an oversized menu with eight separate teams
at 1024x600. `ObserverDropdownBoundsLogic`, attached after the common shroud-selector
logic, limits an overflowing menu to the available space above or below its button
and retains scrolling to the selected view. It leaves menus that already fit in
place. The common camera-selection logic remains unchanged.

## Validation

The new opt-in native check loads the real observer UI in an isolated eight-player
world. It opens the camera dropdown with nine group headers, draws normal, hover,
pressed, selected and disabled header/row states, scrolls the list, selects all ten
camera views, opens the statistics menu, and exercises the second shared spectator
template. It also asserts menu bounds and the presence of scrollbar artwork.

```powershell
# From engine/, using the environment for the existing native RMG checks:
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime <empty-output-directory> --wide --observer-ui
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime <another-empty-directory> --observer-ui
```

The test reproduced the exact reported exception before the fix. Both 1280x800
and 1024x600 runs pass after the fix; the rendered menus were visually inspected.
Evidence is in ignored `artifacts/observer-ui/`: `reproduction-02.log`,
`final-wide/`, and `final-compact/` (screenshots and `verification.json`).

Full build/runtime-data validation passed with the existing style-warning baseline
unchanged. The final Release build passed with zero warnings and errors.
The harness does not save user settings or interact with an existing game session.

## Follow-up: Army totals and composition

The Army panel was empty because `^CoreUnit` inherited
`UpdatesPlayerStatistics` without enabling `AddToArmyValue`. In the pinned engine,
that flag enables the per-type unit counters as well as the optional value total.
It is now enabled for units; their existing creation, death, disposal and ownership
notifications keep the counters current. Buildings and colonies are not included.
No prices, combat statistics, production settings or AI rules were changed.

Army now shows **Player**, **Total Units**, and **Composition**. Composition uses
the existing production icons with a current count for each owned unit type;
hovering shows its name and description. Empty armies show zero. Totals follow the
owner's complete army, independent of the observer's chosen camera view or fog.
The existing event-maintained statistics are reused, avoiding a scan of every
world actor on each UI frame.

Rows have enough height for the icons, and the narrower icon spacing fits all
fifteen regular unit types beside the minimap at 1024x600. Scrollbar availability
now uses the real template heights, including team headers, instead of assuming
all rows are 25 pixels high.

The `--observer-ui` native check now also covers the Army view. It compares UI
and cached counts against living actors in an eight-player world, renders all
fifteen types, checks icon counts and hover tooltips, and tests creation, colony
production, death, disposal, transfer to another player, empty armies and reopening
the panel. It exercises free-for-all, two teams and eight teams. UI drawing leaves
the world synchronization hash unchanged.

Both 1280x800 and 1024x600 runs passed and were visually inspected. Evidence is in
ignored `artifacts/observer-army/final-wide/` and `final-compact/`, including
`army-verification.json` and `army-teams-0.png`. The recurring Chaos/Fractured and
four ordinary-biome pirate checks also passed (152 completed ant-hole cycles,
238 pirates), covering the neutral units that inherit the same statistics trait.

Full validation passed. Source-style warnings remain at the existing baseline;
the final Release build completed with zero warnings and errors.
