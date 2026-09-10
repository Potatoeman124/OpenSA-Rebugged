# Starting-area and stronghold ownership options

Implemented on `codex/rmg-starting-area-options`, based on accepted Strongholds `af971d7`
(2026-09-10). These are opt-in changes to current generators V16-V22, using player-settings
schema 17. Generator/configuration versions and default map identities remain unchanged.

## Player controls

| Option | Scope | Default | Behavior |
|---|---|---|---|
| Respect Starting Safe Area | All seven current layouts | On | Keeps extra colonies outside the starting colony's combat buffer. Off allows colonies within that buffer. |
| Own Starting Stronghold | Strongholds only | Off | On assigns all additional colonies in each occupied starting fort to the player who actually spawns there. Castles and unoccupied forts stay neutral. |

With whole-stronghold ownership Off, existing Starting Ownership percentages/weights and
Closest to Spawn / Random work as before, including castles. On overrides both the shares and
choice mode, even with all sliders at zero. The ownership button shows **Whole Stronghold Owned**
and is disabled while overridden. Slider values and mode are retained and restored when the
checkbox is switched Off. The setting is retained when leaving Strongholds but has no effect
on other families. The global safe-area choice persists across family and preset changes.

Safe-area Off removes the starting turret/production-path combat-distance exclusion. It keeps
actual building coverage and exit cells reserved for every possible starting faction. Player
starts remain mutually separated; shoreline passage, connected-layout routing and terrain
constraints still apply. Prevent Colony Overlapping remains a separate control for separation
between additional colonies. Turning safe-area protection Off does not force overlap or add
colonies beyond the selected density target.

Starting positions remain fixed when changing either new option. Changing ownership never
changes terrain, colony positions or types. Safe-area changes can redistribute additional
colonies. Natural families retain their existing terrain; planned families retain their own
terrain contracts. In particular, Artificial Battlefield derives route/plaza geometry from
colony positions, and Original Surface Relations can reserve dirt around newly selected sites.
The safe-area switch is not a promise of terrain identity for those cases.

## Saved maps, preview and runtime

Generated actors are still saved neutral. Whole-fort ownership is applied once during match
setup using the engine's actual resolved spawn number, including manually swapped starts and
random starts. Each planned Strongholds colony carries its fort's one-based spawn number;
zero identifies a smaller castle. Closed or unused spawn locations receive no owner and their
colonies are not redistributed. Ownership uses normal actor owner-change notifications, so
production, capture and rendering traits refresh correctly. It is not reapplied after capture.

The lobby forecast resolves the same spawn assignments and uses actual player colors.
The override is previewed even with zero shares. Named Save Map copies preserve the rule.

Schema 17 fields:

```json
{
  "respect_starting_safe_area": false,
  "own_starting_stronghold": true
}
```

The second field is legal only for Strongholds. Both require actual JSON booleans. The lobby
and PowerShell wrapper use schema 17 when either option differs from its default. Default
values are omitted from map identity and serialization, preserving historical replay output.
The native validator checks persisted colony membership and ownership rules against the plan.
The logical validator records `starting_safe_area_intrusions` when protection is disabled.

Example:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 `
  -Strongholds -Seed 825300756769842102 -MapSize 256 -Players 4 `
  -RespectStartingSafeArea $false -OwnStartingStronghold $true `
  -NeutralColonyDensity ultra -PreventColonyOverlapping $false `
  -VerifyRepeatability
```

## Verification

Evidence is under `artifacts/rmg/starting-area/` and remains untracked.

- `matrix-final/verification.json`: 181 generated/reloaded/linted/repeated native maps across
  all seven families, four sizes, all biomes, both overlap settings, individual species and
  the empty pool; 44 byte-for-byte historical replays. Every family places additional colonies
  inside the released starting buffer in the paired 256-map test. Starting coordinates stay fixed.
- Two 128x128 Natural PVP settings cannot fit eight starts with Ultra modifiers and original
  surface relations. Repeated failures match the accepted schema's capacity rejection with
  either safe-area value; no incomplete map is installed.
- `runtime-01/verification.json`: 30 actual world-startup cases, each repeated identically.
  Covers all layouts, 64-512 sizes, whole-fort ownership with zero/partial/full shares, Random
  mode overridden, swapped/random spawns, closed slots, all biomes, AI ticks, named save copies,
  production queues, capture behavior and exact agreement between preview and world ownership.
- Actual native previews were inspected across every layout and all Strongholds sizes, along
  with the rendered lobby controls. `scripts/rmg/Verify-StartingArea.py` reproduces the map matrix;
  `--validate-sa-rmg-runtime OUTPUT --wide --starting-area` reproduces runtime checks.

Final repository validation passed. The subsequent style check reports the existing 236-warning
baseline with no new warning categories; the final Release build has zero warnings/errors.
`ui-final/verification.json` records the final control/serialization checks after the caption fit
adjustment. `release-verification.json` confirms the public PowerShell wrapper produces exactly
the same saved package as the corresponding matrix case with both new options enabled.
The full-fort allocation report lists counts in map-spawn order; live lobby-slot ownership
follows the engine-resolved spawn assignments, as independently checked in runtime tests.
