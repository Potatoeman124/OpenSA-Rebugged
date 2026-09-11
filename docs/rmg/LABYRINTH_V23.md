# Labyrinth V23

Status: implemented on `codex/rmg-labyrinth`, based on the accepted starting-area
checkpoint `55f206b`. Configuration 2 was accepted by the user at `20ccaa0` on
2026-09-11. No merge or push is included.
Player-settings schema 18; generator version 23; configuration 2.

## Gameplay intent

Labyrinth is an asymmetric network of winding passages, small junctions and scattered
colony alcoves. Water makes detours necessary; gravel and moss slow movement along
and across the passages. Colonies are local objectives rather than a continuous siege
front. There is no mirrored geography, equal route-length or equal colony-opportunity
promise. All starting colonies and placed additional colonies have land access through
the completed map, including their actual footprints and production exits.

Terrain geometry has priority over colony requests. The network and its alcoves are
built first. Colony density, species weights, overlap prevention, starting safe area
and initial ownership cannot repaint terrain or create more alcoves.

## Controls

- **Terrain Complexity:** changes the internal maze topology. Each fixed large area
  is subdivided into a 1 x 1, 2 x 2, 3 x 2, 3 x 3 or 4 x 4 local maze from Small
  through Ultra. This adds junctions, length, turns and route density while narrowing
  passages. Starting anchors and the large-area connection tree remain fixed for the
  seed. Local junctions, paths and colony alcoves intentionally change with complexity.
- **Passage Width:** Narrow, Standard (default), Wide. The protected Narrow widths
  are 14, 11, 8.5, 6.5 and 5 native cells across the five complexity levels. Standard
  adds 2.5 cells; Wide adds 5. Junctions and colony alcoves have their own necessary
  clearance. Tile transitions can leave additional dry cells beside minimum routes.
- **Extra Routes:** Few, Standard (default), Many. Respectively opens 3%, 15% or 40%
  of the unused local and large-area connections, with deterministic ordering.
  Local extras are apportioned over the whole map so rounding does not erase their
  effect in smaller submazes. Water separators protect the boundaries between maze
  cells; the route mask cuts the intended passages through them. Native transitions,
  colony/start clearances and wider passages can still create additional local joins.
- **Water Amount:** Low through Ultra fill 60%, 72%, 82%, 91% and 100% of the eligible
  space outside the protected network, with a minimum imposed by maze separators.
  Native shoreline normalization also changes final coverage. Scaling against available
  space prevents upper water levels from all hitting the same absolute map-area ceiling.
- **Surface Modifiers:** existing quantity controls remain active. For complexity
  index `d = 0..4`, gravel target is `min(40, usual gravel + 6 + 3d)` percent of land;
  moss is `min(30, usual moss + 5 + 2d)`. The original transition rules still apply.
  Narrow passages and high water can restrict coverage, particularly moss, whose
  tiles need room inside gravel. The status reports actual coverage and explains
  when passages, alcoves or transitions limit it.
- **Neutral Colony Density:** one quarter of the ordinary layout target after size
  and player scaling, rounded up. At 256 x 256 with four players Standard requests
  12 colonies instead of 48. A request can stop below its target when suitable
  alcoves or combat spacing run out. Increasing density never clears the maze.

All current biomes, colony weights, Starting Ownership modes, colored ownership
previews, named saving, overlap prevention and Respect Starting Safe Area are
integrated. The whole-stronghold ownership rule and castle control remain specific
to Strongholds. Labyrinth supports 1-8 players, with the existing 1-4 cap at 64 x 64.
The 64 map necessarily offers fewer turns and alcoves because colonies retain their
native footprint size; crowded small maps may have no additional colonies with the
starting safe area enabled.

## Construction and validation

Configuration 1 fixed the entire maze graph and mainly changed width and bends.
The user rejected that interpretation after testing Small and Ultra with seed
`642188072337235576`, Desert, 256 x 256, six players, Narrow passages and Few extras.
Configuration 2 replaces that restriction with a hierarchy of connected local mazes.

A seeded large-area tree connects 1 x 1, 2 x 2, 3 x 3 or 6 x 6 areas on 64, 128, 256
or 512 maps. The same inter-area gates are retained across complexity levels. Each
area receives a local maze whose subdivision depends on complexity. Routes to fixed
starting anchors join that network. Starting anchors are selected using the previous
reference lattice, preserving their positions from configuration 1.

Alcoves are chosen from local junctions before colony settings are considered, capped
by map area. Higher complexity can therefore offer more sites; higher colony density
cannot. Real water separator strips stop coverage ranking from removing maze walls.
Some native geometry joins remain possible around protected starts/alcoves. There is
no guarantee that every particular point-to-point route gets longer: the total network
length and density grow, while changed branches and optional connections can shorten
individual journeys.

Native water and land transitions are applied before colony placement. Placement
uses finished native terrain and actual actor/exit coverage, retains two free cells
between blocked colony footprints and water, and follows the existing strict/relaxed
combat-spacing rules. The terrain hash is checked before and after placement. The
native validator independently checks connected actor access, production exits,
footprint overlaps, protected passage cells, shoreline clearance and movement costs.

The new report block is `regions.labyrinth_plan`. It records local junctions, the fixed large-area tree, subdivisions, extra
edges, passage lengths, alcoves, widths, separator cells and available water space. The normal
report also contains realized terrain coverage, requested/placed colonies, timings,
world validation and package repeatability.

## Verification and reproduction

Configuration 2 passed **272 native configurations** and **53 exact historical
package replays** on 2026-09-10. **24 live skirmish scenarios** initialized twice,
including all five complexity levels of the user's six-player Desert setup. The
final wrapper package matches the native Ultra review case exactly. Repository
validation passed; Release build has zero warnings/errors, and static analysis has
no additions to its existing warnings.

Evidence is under `artifacts/rmg/labyrinth/revision/`: `matrix-01/verification.json`,
`runtime-final/verification.json`, `validation-01.log`, `style-final.log`,
`release-final.log` and `wrapper-final/`. Previews in `matrix-01/` cover the exact
user seed, all sizes, passage widths, extra routes and water/surface combinations.
The matrix includes the exact user seed and settings at all five levels, expanded
complexity/width tests on 64, 128 and 512 maps, and historical package comparisons.
It independently counts boundaries on the exported native terrain and measures
four-neighbour shortest walks between the user's unchanged starting centers. The
latter measures terrain routes, not a unit's travel time or runtime pathfinding cost.
Building footprints and production access are checked separately by the native validator.

For the user's 256-map example:

| Complexity | Local route nodes | Total planned passage length (cells) | Protected Narrow width (cells) |
|---|---:|---:|---:|
| Small | 9 | 895 | 14 |
| Medium | 36 | 1670 | 11 |
| High | 54 | 1996 | 8.5 |
| Extreme | 81 | 2336 | 6.5 |
| Ultra | 144 | 3146 | 5 |

These are geometry measures, not promises of equal journey distances or gameplay parity.
The prior configuration's 222-case/19-world validation is historical evidence; it did not
establish that complexity changed topology, and its fixed-graph assertion has been replaced.

The native matrix in `scripts/rmg/Verify-Labyrinth.py` covers all five complexity
levels crossed with passage width and extra routes; all 25 water/surface combinations
with both original-relations states; every allowed size/player count; dense placement
with safety and overlap toggles; species-only and empty pools; ownership; all biomes;
and additional seeds at maximum terrain/colony pressure. Historical package replays
include every accepted family and both starting-area options.

```powershell
python scripts/rmg/Verify-Labyrinth.py --output artifacts/rmg/labyrinth/new-matrix --workers 4

.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Labyrinth `
    -Seed 825300756769842102 -MapSize 256 -Players 4 `
    -TerrainComplexity ultra -PassageWidth standard -ExtraRoutes standard `
    -VerifyRepeatability -OutputDirectory artifacts/rmg/labyrinth/example
```

From the configured engine directory, native live validation is available as:

```text
OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --labyrinth
```

This exercises actual lobby widgets and serialization, saved-map loading, ownership
preview agreement, repeated world initialization, bots and a hostile-enabled biome.
Artifacts remain local under `artifacts/rmg/labyrinth/`; no game assets are committed.
