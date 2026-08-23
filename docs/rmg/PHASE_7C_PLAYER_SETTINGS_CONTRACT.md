# Phase 7C player settings contract

## Status

Phase 7C implements the first serializable player-facing settings layer over the accepted Generator Version 6 `battlefield-layout` profile. It does not add an in-game dialog or map-cache integration. The low-level generator command remains available for development and regression work.

The contract intentionally exposes only choices already supported by the automated and manual Version 6 gates. Unsupported choices are rejected instead of being approximated.

## Scope

The implemented player settings are:

- generation preset: `balanced`, `open-conflict`, or `tactical-crossroads`;
- seed: an unsigned integer encoded as a JSON string or integer;
- players: `2` or `4`;
- symmetry: `automatic`, `horizontal`, `vertical`, or `rotational`;
- layout override: `preset`, `open-fields`, or `contested-center`; and
- neutral colony density override: `preset`, `sparse`, `standard`, or `dense`.

Every accepted document normalizes to:

- Generator Version 6;
- topology `battlefield-layout`;
- tileset `NORMAL`; and
- native playable size `128,128`.

Map size, tileset or biome, tactical-terrain intensity, Water amount, chokepoint count, cosmetic density, starting distance, slow-terrain character, and custom map title remain deferred. Fairness, connectivity, movement, combat-space, production-exit, transition, capacity, repair, and validation controls are never player settings.

## JSON schema version 1

```json
{
  "schema_version": 1,
  "preset": "balanced",
  "seed": "3100009",
  "players": 2,
  "symmetry": "automatic",
  "layout": "preset",
  "neutral_colony_density": "preset"
}
```

`schema_version`, `preset`, `seed`, and `players` are required. The remaining fields default to `automatic` or `preset`. Field names are case-sensitive and unknown fields are rejected. Choice values are case-insensitive when parsed by the current command-line implementation.

This strict allow-list prevents misspelled settings and accidental exposure of internal safety switches.

## Preset normalization

| Preset | Layout | 2-player colonies | 4-player colonies |
| --- | --- | ---: | ---: |
| Balanced | Contested Center | 10 | 16 |
| Open Conflict | Open Fields | 8 | 12 |
| Tactical Crossroads | Contested Center | 14 | 20 |

Explicit layout and colony-density choices override the corresponding preset value:

| Colony density | 2 players | 4 players |
| --- | ---: | ---: |
| Sparse | 8 | 12 |
| Standard | 10 | 16 |
| Dense | 20 | 24 |

These values are requested targets. Version 6 may reduce an infeasible target in complete player-symmetric groups, never below the Sparse value for the selected player count. The achieved count is authoritative for the generated package and is reported separately from the requested count.

`automatic` symmetry deterministically selects one legal symmetry from the schema version, seed, preset, and player count. Repeating the same settings therefore resolves to the same symmetry and canonical map.

The preset name is descriptive metadata. Random generation consumes only the normalized low-level settings, so selecting a preset that normalizes to an existing Version 6 tuple produces the same logical map as the low-level command.

## Command-line entry points

The player-facing development wrapper creates an ignored JSON settings document and generates a map:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-PlayerMapGenerator.ps1 `
    -Seed 3100009 -Preset balanced -Players 2 -Symmetry automatic -InstallForPlay -Overwrite
```

An existing settings document can be supplied to the low-level wrapper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -PlayerSettingsPath .\artifacts\rmg\phase-7c-player-settings\settings\player-3100009.json `
    -InstallForPlay -Overwrite
```

`-PlayerSettingsPath` cannot be combined with seed, players, symmetry, archetype, topology, colony count, or other normalized low-level generator options. Report path, output path, movement-validation mode, installation, and overwrite behavior remain operational controls.

## Metadata and failure behavior

Version 6 reports use report schema version 8 and contain `player_settings` with:

- the requested settings;
- the normalized Version 6 settings;
- a list of explicit preset overrides; and
- a reserved warnings array.

The report also records:

- `neutral_colonies_requested`, the normalized target;
- `neutral_colonies`, the achieved package count;
- validation metrics for the requested and achieved colony counts, colony-search work, obstacle density, and whether the density target was met; and
- `COLONY_TARGET_REDUCED` or `OBSTACLE_DENSITY_TARGET_MISSED` warnings when capacity prevents an exact soft target.

Player-generated map titles use `OpenSA RMG <Preset> <Seed>`. The report remains the authoritative record of resolved symmetry, layout archetype, requested neutral-colony target, and achieved neutral-colony count.

Version 6 makes two capacity-sensitive quality settings adaptive. Obstacle density remains a best-effort target across four deterministic topology attempts, and neutral colonies may reduce as described above after a bounded search. Neither fallback changes the seed.

Connectivity, route width, symmetry, shoreline semantics, spawn and production safety, colony combat space, movement validation, package reload, and map lint remain hard gates. A seed that cannot satisfy any of those gates still fails with a diagnostic; adaptive quality behavior never relaxes safety.

## Automated gate

Phase 7C adds self-tests for:

- JSON round-trip stability;
- each preset's normalization;
- deterministic automatic symmetry;
- explicit override handling;
- rejection of deferred `narrow-passages`; and
- rejection of unknown safety controls.

The normal build/runtime-data gate, complete V1-V6 RMG self-test matrix, native movement validation, package reload validation, and map lint remain mandatory.

## Deferred integration boundary

Phase 7C stops at a UI-neutral, serializable contract. The next integration phase must audit the map chooser, background generation behavior, progress and cancellation, error presentation, user-map naming and cleanup, map-cache refresh, preview selection, and multiplayer ownership before adding `Generate Map...` to the game.
