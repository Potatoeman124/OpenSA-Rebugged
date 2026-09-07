# Regions V14: quantity levels and colony spacing

Status: implemented on `codex/rmg-extended-options`, 2026-09-07. The user accepted V13 configuration 4
before requesting this update. V14 preserves that terrain calibration, adds the new quantity levels and
integrates all controls in the lobby. Native-map verification is complete; live gameplay retesting is the
next user review.

## Controls and targets

The old **Gravel and Moss Amount** label is now **Surface Modifiers**. It still controls gravel and moss
coverage of non-water land; it does not alter doodad density or Terrain Complexity.

| Control | Low / Sparse | Standard | High / Dense | Extreme | Ultra |
| --- | ---: | ---: | ---: | ---: | ---: |
| Water (% of map) | 16 | 20 | 24 | 32 | 44 |
| Surface Modifiers: gravel (% of land) | 10 | 14 | 18 | 24 | 36 |
| Surface Modifiers: moss (% of land) | 5 | 8 | 11 | 16 | 25 |
| Neutral colony target, 2 players, 128 x 128 | 8 | 10 | 20 | 32 | 52 |
| Neutral colony target, 4 players, 128 x 128 | 12 | 16 | 24 | 40 | 64 |

Neutral-colony targets triple at 256 x 256: Extreme is 96/120 and Ultra is 156/192 for 2/4 players.
Tile transitions can reduce achieved water coverage and slightly change gravel/moss percentages.
Existing lower quantity levels are unchanged. All five Terrain Complexity values remain exactly at the
accepted V13 configuration-4 calibration, including Ultra broad/fine strengths **2.70 / 2.60**.

Defaults remain Balanced, NORMAL, 256 x 256, four players, Medium complexity, Standard quantities,
Original Surface Relations On, and **Prevent Colony Overlapping On**. Preset selection restores overlap
prevention to On. Grey neutral preview squares reflect actual placements. The first generated-map
selection still sets Explored Map On and Fog of War Off.

## Prevent Colony Overlapping

The existing separation rule protects colony centers from each other's turret ranges plus a one-cell
buffer; it is larger than a building footprint. The new checkbox controls this neutral-to-neutral spacing:

1. **On (default):** use the existing strict placement pass. If completed terrain cannot support the
   requested count with those clearances, report the actual count and the capacity shortfall.
2. **Off:** run exactly the same strict pass first. If a shortfall remains, enumerate existing sites that
   still satisfy terrain, building-footprint, exit-cell and player-start safety checks. Add the candidate
   with the smallest overlap penalty, update occupied space, and repeat until the target or capacity is
   reached. Previously placed colonies and starts are retained.

The penalty is ordered by maximum turret-envelope penetration, sum of squared penetration depths, then
number of overlapping neighbors. Depths use the existing native-cell combat margin. Exact ties use a
stable order from the seeded candidate list and actor types. A priority queue updates scores lazily;
penalties only increase as actors are added, so each selected site has the minimum current penalty over
all available candidates. This is a greedy local choice, not a claim of a globally optimal final packing.

Captured neutral colonies can fire on neighboring colonies when the checkbox is Off. Buildings and exit
cells never overlap, and every possible starting faction keeps its existing bidirectional turret and
production-path protection. See the explicit V14 exception in [Combat-space safety](COMBAT_SPACE_SAFETY.md).

The fallback never repaints terrain, moves player starts, relaxes Original Surface Relations, or creates
land connections. With Original Surface Relations On, even Ultra colony density can remain far below
target if usable dirt is scarce. The achieved count is visible in the lobby; the status also gives the
number placed by the closer-spacing fallback.

## Versioning and integration

Player-settings **schema 8** selects **Generator V14**, configuration 1. Profiles are
`normal-natural-regions-v14.yaml` and `normal-natural-regions-v14-256.yaml`. The boolean JSON field is
`prevent_colony_overlapping`; omission means true. The serialized surface field remains
`gravel_moss_amount` for compatibility. New quantity values are `extreme` and `ultra`.

The new flag participates in requested/normalized settings and canonical identity. Both flag states use
the same initial placement random stream. Reports expose `neutral_colonies_strict`,
`neutral_colonies_fallback`, `fallback_pair_evaluations`, `neutral_overlapping_pairs` and
`maximum_neutral_overlap_native` as well as the target and actual count.

Schema 7 preserves accepted V13 configuration 4; schemas 1-6 retain their historical selections.
Historical generators reject new quantity values and relaxed spacing. Structured Competitive and
Artificial Battlefield keep their old ranges; changing to either family clamps any extended quantities
to High/Dense and uses strict spacing. The checkbox is shown only for Natural Landscape.

The lobby, presets, dropdown labels, tooltips, report, canonical identity, utility exporter and PowerShell
wrapper all support V14. The wrapper accepts `-SurfaceModifiers` as an alias of `-GravelMossAmount`:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 397716241463670640 `
    -MapSize 256 -Players 4 -TerrainComplexity ultra -WaterAmount ultra `
    -SurfaceModifiers ultra -NeutralColonyDensity ultra `
    -OriginalSurfaceRelations $false -PreventColonyOverlapping $false -VerifyRepeatability
```

Replay an older saved schema with `Invoke-MapGenerator.ps1 -PlayerSettingsPath ...`.

## Verification

Evidence is retained in `artifacts/rmg/regions-v14/`:

- `options-tests.log`: focused V14 contract passes. Tests cover 250 settings/identity round trips,
  default true, old-schema rejection, both sizes/player counts, exact V13 logical/actor/graph parity,
  unchanged output when fallback is unnecessary, strict-pass preservation, repeatability, physical and
  player-start protections, and an exhaustive first-placement check against every legal site's overlap
  penalty. The strict validator rejects a relaxed fixture, and relaxed validation still rejects a colony
  placed at a player start.
- `native-matrix/verification.json`: **44/44** saved native maps pass, covering both sizes, 2/4 players,
  independent Extreme/Ultra quantities, all-highest combinations and Original Surface Relations On/Off.
  Every map passes package lint, deterministic repeat, runtime terrain checks, building/exit checks and
  grey preview-marker count checks. All 22 checkbox pairs preserve terrain and their strict placements.
  Surface Modifiers preserves the water mask; colony density preserves terrain. Measured Ultra coverage
  is materially higher than Extreme. Eleven cases exercise fallback placement; fourteen cases retain a
  capacity shortfall (including strict-mode maps). The slowest total pipeline measured 1.83 seconds on
  this host during the matrix, including repeat generation, saving, reloading and validation.
- `v13-replay/verification.json`: **16/16** accepted revision-4 native maps reproduce their exact canonical
  settings, logical/actor/graph hashes, engine UID and canonical map hash. They also pass native checks
  and deterministic repeats.
- `self-tests.log`: the complete suite has the same three pre-existing V13 Small-to-Ultra water-geography
  tripwire failures at seed 0 / 256 and maximum unsigned seed / 128 and 256. All other groups, including
  the new V14 group, pass. The threshold and accepted terrain calibration were not changed.
- `runtime-validation.log`: supported repository build/runtime-data validation results.

Concrete same-seed examples at Medium complexity, Standard Water and Surface Modifiers, 4 players,
Ultra colony density: the 128 map improves from **26/64** colonies with prevention On to **64/64** Off;
the 256 map improves from **140/192** to **192/192**. At every control's Ultra setting with dirt-only
placement, the 256 map improves from **22/192** to **61/192**, then exhausts legal physical capacity.
Turning Original Surface Relations Off in that separate all-Ultra case allows **192/192**.

Run the focused suite with `OpenRA.Utility.exe sa --validate-sa-rmg --regions-options` in the configured
engine environment. Reproduce the native matrix with
`python scripts/rmg/Verify-RegionsOptions.py artifacts/rmg/regions-v14/new-matrix`.
Native validation uses loaded engine terrain/actor rules; it does not initialize a live match or prove
combat balance. In-game review remains the user's next step.
