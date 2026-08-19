# Phase 7C manual playtest corpus

## Purpose

This corpus verifies that the player-facing settings model produces the accepted Version 6 map families, records normalization correctly, and presents recognizable preset names. Phase 7C does not alter terrain generation, so the focus is settings-to-map fidelity rather than a new visual-quality gate.

## Installed maps

| Seed | Preset | Players | Symmetry request | Layout override | Colony override | Expected normalized layout | Expected colonies |
| ---: | --- | ---: | --- | --- | --- | --- | ---: |
| 3100009 | Balanced | 2 | Automatic | Preset | Preset | Contested Center | 10 |
| 3100000 | Open Conflict | 2 | Horizontal | Preset | Preset | Open Fields | 8 |
| 3100023 | Tactical Crossroads | 2 | Rotational | Preset | Preset | Contested Center | 14 |
| 3100025 | Balanced | 4 | Horizontal | Preset | Preset | Contested Center | 16 |
| 3100026 | Open Conflict | 4 | Vertical | Preset | Preset | Open Fields | 12 |
| 3100047 | Tactical Crossroads | 4 | Rotational | Preset | Preset | Contested Center | 20 |
| 3100016 | Tactical Crossroads | 2 | Rotational | Open Fields | Preset | Open Fields | 14 |
| 3100024 | Balanced | 4 | Horizontal | Open Fields | Sparse | Open Fields | 12 |

The six first cases cover every preset at both supported player counts. The final two cover visible preset overrides.

## Installation

From the repository root, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Prepare-Phase7cManualCorpus.ps1 `
    -InstallForPlay -ReplaceInstalledRmgCorpus -Overwrite
```

The replacement option removes only matching `OpenSA-RMG-*.oramap` packages from the development user-map directory before installing these eight maps.

## Review

Confirm that:

- the map browser contains exactly the eight newly installed generated maps;
- every title has the form `OpenSA RMG <Preset> <Seed>`;
- titles show all three preset names;
- every map advertises the expected 2 or 4 player slots;
- the two explicit layout overrides resemble Open Fields despite their selected preset;
- starting colonies, neutral colonies, spawn-fire safety, routes, terrain, shoreline, and doodads retain the accepted Phase 7B behavior; and
- a representative map can start and play normally.

Machine-readable reports and source settings are stored beneath the ignored `artifacts/rmg/phase-7c-player-settings/` directory. Compare each report's top-level values with `player_settings.requested`, `player_settings.normalized`, and `player_settings.overrides` when diagnosing a mismatch.
