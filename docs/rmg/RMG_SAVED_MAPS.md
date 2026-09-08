# Saving generated maps from the RMG

## Player workflow

On `codex/rmg-512-map-size`, **Save Map...** sits beside **Return to Skirmish**.
Generate a preview, click Save Map, enter a name and press **Save** (or Enter).
The persistent copy immediately appears under **Change Map -> Custom Maps**.
Cancel or Escape closes the dialog without writing anything.

The button is available when the selected generated preview is current. Changing RMG
parameters requires generating the new preview before saving. Saving is a local action
and also works after the player marks themselves ready. It leaves the selected preview
and lobby readiness unchanged; the status line confirms the saved filename.

Names are trimmed and limited to 96 characters. The displayed title preserves Unicode
and punctuation, including `#`. Characters unavailable in filenames become underscores
in the filename only. Files use `RMG-<name>.oramap`, with ` (2)`, ` (3)`, etc. when the
filename already exists. Existing maps are never overwritten. A save failure keeps the
dialog and name open with an error so the player can retry.

## Storage and preservation

The destination is the same loaded **User** map folder used by the engine's map editor
and Custom Maps chooser, as configured in `mods/sa/mod.yaml`:
`^SupportDir|maps/sa/{DEV_VERSION}`. It is resolved at runtime, not hard-coded to a
particular user's Windows directory.

The saver copies the current package and changes only the `Title` in `map.yaml`.
Terrain, actors, embedded rules (including starting ownership shares, mode and seed),
author, metadata and preview image remain intact. Lobby-only choices such as player
names/colors, AI opponents and later hostile-option changes remain lobby settings.
The existing RMG author identification remains available to the skirmish startup fix.

A completed temporary archive is reopened through the native map loader before it is
moved to its final filename without replacement and registered as a User map. Temporary
files are cleaned up. Named files are separate from the two rotating
`OpenSA-RMG-preview-a.oramap` / `OpenSA-RMG-preview-b.oramap` previews and survive later
preview generations. Identical maps saved under the same title can share a content UID
and one chooser entry even when multiple numbered files exist, following engine behavior.

## Verification

The opt-in runtime check redirects saves into an isolated artifact directory. It never
writes the player's maps or settings. From `engine/`, with the normal local runtime and
mod-search environment:

```powershell
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT_DIRECTORY --wide --save
```

Omit `--wide` for the 1024 x 600 layout; `--wide` uses 1280 x 800. The output directory
must be empty. The check uses real lobby/save/chooser widgets and native game worlds.

Covered cases:

- Normal 64 solo, Desert 128 with three players, Swamp 256 with four players, and
  Candy 512 with eight players; both starting-ownership modes.
- Byte-identical package members and YAML equality except the requested title.
- Immediate Custom Maps selection, ownership-preview agreement with runtime, AI ticks,
  and actual loopback server startup on the renamed four-player map.
- Empty/invalid names, Unicode and punctuation, duplicate protection, source preservation,
  Cancel/Escape/Enter, readiness/stale-preview guards, failure/retry, directory-watcher
  updates, and successful loading into a fresh cache after overwriting both preview files.

Both tested resolutions pass all four cases and preserve seven named files per run.
Evidence is under `artifacts/rmg/rmg-save-map/runtime-final/` (1280 x 800) and
`runtime-compact/` (1024 x 600). Each `verification.json` records the outcome, and
screenshots show the dialog, error feedback, chooser and live ownership previews.
Repository validation passed with the existing 237 Debug style warnings and no new
warnings; the playable Release build passed with zero warnings/errors. Logs:
`artifacts/rmg/rmg-save-map-final-validation.log` and
`artifacts/rmg/rmg-save-map-final-release.log`.
The user accepted the save addition and authorized freeze, merge to main and push on 2026-09-08,
together with the 512 size extension. The implementation is `71f2b51`, developed on
`codex/rmg-512-map-size` after `55341a1`.
