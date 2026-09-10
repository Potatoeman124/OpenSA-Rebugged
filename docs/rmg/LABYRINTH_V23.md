# Labyrinth V23

Status: implemented on `codex/rmg-labyrinth`, based on the accepted starting-area
checkpoint `55f206b`. Pending user in-game review. No merge or push is included.
Player-settings schema 18; generator version 23; configuration 1.

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

- **Terrain Complexity:** Small through Ultra tighten the passages, increase bend
  amplitude, and raise the target coverage of slowing terrain. The seed's junctions,
  core route graph, colony alcoves and starting anchors remain fixed. Complexity is
  a geometry control, not a promise of monotonically increasing water percentage.
- **Passage Width:** Narrow, Standard (default), Wide. At Ultra the protected passage
  widths are 5, 7 and 10 native cells respectively; each earlier complexity step adds
  1.2 cells. Small junctions and colony alcoves have their own necessary clearance.
  Tile transitions can leave additional dry cells beside these minimum routes.
- **Extra Routes:** Few, Standard (default), Many. Respectively opens 3%, 15% or 40%
  of the remaining edges after constructing a connected maze tree. The same seed
  adds edges in a stable order. These are planned passage connections, not an exact
  count of every possible ground shortcut: low water can leave additional land, and
  slowing surfaces can be crossed.
- **Water Amount:** Low through Ultra fill 60%, 72%, 82%, 91% and 100% of the eligible
  space outside the protected network. Final water coverage is lower after native
  shoreline normalization. Scaling against available space prevents upper water
  levels from all hitting the same absolute map-area ceiling.
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

A seeded, slightly offset grid provides 3 x 3, 5 x 5, 11 x 11 or 22 x 22 junctions for
64, 128, 256 or 512 maps. A randomized depth-first spanning tree connects every
junction. Optional edges add flanking choices. Six-segment curves bend each route
according to complexity without moving its endpoints. Starts use separated junctions
and actual starting-colony combat margins. A fixed subset of junctions provides
small colony alcoves, distributed independently of density.

Native water and land transitions are applied before colony placement. Placement
uses finished native terrain and actual actor/exit coverage, retains two free cells
between blocked colony footprints and water, and follows the existing strict/relaxed
combat-spacing rules. The terrain hash is checked before and after placement. The
native validator independently checks connected actor access, production exits,
footprint overlaps, protected passage cells, shoreline clearance and movement costs.

The new report block is `regions.labyrinth_plan`. It records junctions, core and
extra edges, curved passages, alcoves, widths and available water space. The normal
report also contains realized terrain coverage, requested/placed colonies, timings,
world validation and package repeatability.

## Verification and reproduction

Verified on 2026-09-10:

- **222** native Labyrinth configurations generated, linted and repeated successfully;
  **53** historical packages reproduced exactly.
- **19** live skirmish scenarios initialized twice with identical ownership outcomes;
  actual previews, saved-map reloads, bots and production exits passed.
- Live lobby controls passed **252** size/player/width/route roundtrips, invalid-input
  rejection, family switching, persistence and the 64 x 64 player cap.
- Repository build/runtime-data validation passed. Release build has zero warnings
  and errors. Static checks retain the pre-existing 236 warnings with no additions.
- The PowerShell wrapper produced the exact same package as its native matrix case.

Evidence: `artifacts/rmg/labyrinth/matrix-02/verification.json`,
`runtime-01/verification.json`, `ui-final/verification.json`,
`validation-01.log`, `style-final.log`, and `release-final.log`.
Native previews were inspected across complexity/width/routes, water/surface
combinations, small and large maps, and multiple seeds at maximum placement pressure.

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
