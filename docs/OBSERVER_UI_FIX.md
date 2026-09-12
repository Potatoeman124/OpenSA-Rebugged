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
