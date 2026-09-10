# Ring V20

## Status

Implemented on `codex/rmg-ring` from accepted Crossroads revision `cfa6f9e` (2026-09-10).
Ring uses generator **V20 / configuration 1 / player-settings schema 14**. The user accepted
revision `ae6512c` on 2026-09-10 and requested Divided Lands next. No merge or push was requested.
Ordinary Natural Landscape V16 remains the default; selecting Ring initially uses Round / Standard.

## Layout contract

Ring surrounds an enclosed central lake with a continuous land circuit. Players start around that
circuit and compete for colonies along its shoulders. There is no central junction or land crossing
through the center. Terrain, starts, neutral colony positions/types and passable doodads use exact
native reflection symmetry. Ownership allocation follows afterward using the existing lobby rules.

Supported sizes are 64, 128, 256 and 512, in Normal, Desert, Swamp and Candy. Player counts are 2/4/8,
with the existing four-player cap at 64. Two players occupy opposite sides, four occupy diagonal
positions, and eight surround the circuit. On 128 maps with eight players, starting plazas sit farther
out to give neutral colonies more room. All players are symmetry-equivalent before faction, team,
ownership and hostile-unit choices.

| Control | Effect |
|---|---|
| Ring Shape | Round, Octagonal or Square changes the central lake, land belt and starting geometry. |
| Ring Width | Narrow, Standard or Wide expands the protected land belt and reduces the central lake. |
| Terrain Complexity | Small through Ultra increases shoreline indentation depth and frequency on both sides of the ring, plus modifier detail. |
| Water Amount | Controls water outside the belt. The central lake is set by geometry and complexity; it creates a minimum, while protected land creates a maximum. |
| Surface Modifiers | Selects the usual gravel/moss percentages on eligible land, translated by biome. |
| Colony Density | Fills fixed shoulder sites in complete mirrored groups, prioritizing contested stretches between starting positions. |

Width, complexity, amounts and surface-relation settings preserve the same seed's starts and colony
plan. Shape is a structural setting and may change those locations. Density never changes water.
With Original Surface Relations disabled, density leaves every native terrain cell unchanged. With
it enabled, occupied colony footprints/exits receive small clear pads within the fixed land belt.
No pads can extend into the central lake. The minimum guaranteed clear loop stays present even at
Ultra quantities. On maps of 256 or larger, colony footprints are excluded from a central travel strip.
Smaller maps instead prove the surviving route after placing real actor footprints.

The usual strict spacing rule remains the default. Disabling Prevent Colony Overlapping relaxes turret
spacing only after strict placement is exhausted; physical footprints, production exits and starting
combat clearances remain mandatory. The generator reports a shortfall instead of filling the lake or
interrupting the circuit. Compact crowded cases can have few or no neutral colonies: in the validation
set, 64/four-player strict settings and 128/eight-player strict settings can place zero. All-zero species
weights deliberately produce no neutral colonies.

## Native geometry and validation

The nominal ring radius is 34% of map size. Its fixed colony half-band is 10% of map size clamped to
12-48 native cells. Standard and Wide add 2.5% and 5.5% of map size to the protected half-width; Narrow
uses the fixed half-band. Shore detail grows from 0.3% to 9.5% of map size, capped to preserve the
central water core on small maps. The detailed inner and outer shorelines also constrain water eligibility, so Ultra water cannot fill
past the detail envelope and smooth away complexity. The fields are evaluated on canonical reflection orbits before
native shoreline and land-transition construction.

The native validator checks local footprint/exits, complete ground access to all objectives, and equal
weighted terrain costs from symmetry-equivalent starts to every typed colony pool. Ring additionally:

- Floods the actual central water component and rejects any lake that reaches the map edge.
- Checks actual native clear cells around the complete nominal loop.
- Checks the occupied ground component for a cycle with nonzero winding around the lake. This uses
  cardinal movement edges, so a route that depends on squeezing diagonally between touching actor
  corners is insufficient. A synthetic broken-ring negative control must fail this test.

Water-floor and terrain-capacity warnings are visible in the lobby status. They are expected when the
chosen geometry cannot meet an amount target. Low water therefore does not remove the central lake;
Wide Square can limit high water strongly. No global land-connectivity rule is added to Natural layouts.

## Tools and evidence

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Ring -Seed 397716241463670640 `
    -MapSize 256 -Players 4 -RingShape round -RingWidth standard `
    -TerrainComplexity medium -VerifyRepeatability
```

The public wrapper accepts `-RingShape round|octagonal|square` and
`-RingWidth narrow|standard|wide`. It rejects controls belonging to other layout families.
The lobby retains separate choices for each layout when switching between them, and all existing
colony type/ownership controls and Save Map remain available.

`scripts/rmg/Verify-Ring.py OUTPUT --wide` generates the full size/player/shape/width/complexity product,
water/modifier cross-products, strict and relaxed density comparisons, all biomes, zero/species-only
weights, ownership modes and historical exact package replays. Native semantic exports are checked
independently of the planning masks. Its historical replays reference the local accepted artifact corpora.

`OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --ring` checks actual lobby controls,
288 setting combinations (99 valid), invalid fields, ownership previews, saved copies, repeated world
initialization, AI, hostiles and loopback server startup. It runs in isolated runtime settings/map folders.

Validation artifacts: `artifacts/rmg/ring/`. Generated packages and preview images remain ignored.

## Delivery verification (2026-09-10)

- `matrix-final/verification.json`: **806 Ring maps passed**, each generated twice, native exported and
  map linted; **27 accepted older packages replayed exactly**, including Crossroads configuration 2.
- **97 density comparisons** preserve water; free-surface variants preserve every native terrain cell.
  **669 objective continuity comparisons** preserve starts and colony plans across terrain controls.
- Direct same-seed Small/Medium/High/Extreme/Ultra comparisons cover every shape and width under
  Standard and Ultra water. Small-to-Ultra changes the water boundary by at least **7.98% of map cells**
  in those response cases; the high-water detail cannot disappear behind water saturation.
- `runtime-delivery/verification.json`: **13 live scenarios passed**, each with two identical world
  initializations, across all four biomes and sizes. Includes UI/schema tests, saved copies, dynamic
  ownership/recolor/spawn previews, AI, hostiles, loopback server startup and 512/eight-player maps
  with **792 neutral colonies** for all three shapes.
- Full build/runtime-data validation passed. Static analysis retains the existing 236 warnings with
  no additions from Ring; the final Release build has zero warnings and errors. The focused
  `regions-ownership-contract` and public wrapper repeatability check also pass.

The final comparison sheets are `matrix-final/comparison-final.jpg`, `review-density.jpg`,
`review-stress.jpg` and `review-high-water-detail.jpg`. The last specifically checks the Wide/Ultra
water interaction caught during visual review. Compact-map detail and colony capacity remain limited
by native tiles and actor clearances; they are not hidden by changing density or breaking the loop.
