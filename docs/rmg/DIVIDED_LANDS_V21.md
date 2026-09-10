# Divided Lands V21

## Status

Implemented on `codex/rmg-divided-lands` from accepted Ring revision `ae6512c` (2026-09-10).
Divided Lands uses **generator V21 / configuration 2 / player-settings schema 15**. The user accepted revision `91f1594` in-game on 2026-09-10. Ordinary Natural Landscape V16 remains the default. Selecting Divided Lands initially
uses **One per border / Standard**. No merge or push is part of this step.

## Layout contract

Each player has a home territory separated from neighboring territories by water channels. Two
players occupy opposite banks; four occupy quarters; eight occupy mirrored sectors. The central
water junction on four/eight-player maps does not become a shared land plaza. Colonies are distributed
inside the territories, leaving clear channel space and usable crossing approaches.

Terrain, starts, neutral colony positions/types and passable doodads use exact native reflection
symmetry. All players have equal typed colony opportunities and terrain travel costs before faction,
team, starting ownership and hostiles are applied. Existing ownership percentages, Closest to Spawn /
Random assignment, colored previews, biome hostiles and Save Map remain available.

Supported sizes: 64, 128, 256 and 512. Supported players: 2/4/8, capped at four on 64. All four biomes
are supported: Normal, Desert, Swamp and Candy. Two-player channel orientation follows seed parity.

| Control | Effect |
|---|---|
| Land Crossings | None, One per border or Two per border. The count applies to every neighboring territory border, not to the whole map. |
| Crossing Width | Narrow, Standard or Wide sets the clear land passage across each channel. It has no terrain effect when Land Crossings is None. |
| Terrain Complexity | Small through Ultra increases variation in channel banks and the frequency of inland surface features. |
| Water Amount | Widens channels within a fixed envelope. Continuous separation creates a minimum; home areas and crossing passages create a maximum. |
| Surface Modifiers | Applies the usual gravel/moss quantities on eligible land, translated by biome. |
| Neutral Colony Density | Fills fixed home sites in complete mirrored groups. It cannot widen land into channels or create new crossings. |

None deliberately permits disconnected territories. Every colony must still have local production
access and connect to a home territory, but the generator does not require ground access between
players or check flying-unit availability. This follows the user's existing accessibility decision.
One and Two require a connected ground network through the selected crossings after placing actual
actor footprints. No global connectivity requirement is added to Natural Landscape.

With a fixed seed, size, player count, density and type weights, changing crossings, width, complexity,
water, modifiers or biome preserves starts and colony positions/types. Ownership is applied later.
Density never changes water; with Original Surface Relations disabled, it changes no terrain cells.
With relations enabled, colony footprints and exits receive local clear pads inside the fixed home area.

Prevent Colony Overlapping retains strict turret spacing by default. Disabling it permits the existing
least-overlap fallback only after strict sites are exhausted. Physical footprints, production exits and
starting combat clearance remain mandatory. Compact maps may have few or no neutral colonies; an
honest shortfall is preferable to joining territories or blocking a crossing.

## Geometry and validation

Maximum channel half-widths are 24%, 14.5% and 6% of map size for two, four and eight players.
Configuration 2 builds a tighter fixed potential-water mask from the union of all five complexity
levels. Colony placement uses this mask instead of excluding a straight maximum-width strip. It
requires two native cells between water and the **blocking** footprint; passable footprint cells and
production exits need valid land but do not add another two-cell margin. Shore candidates use a
one-cell search grid, with the coarser three-cell grid retained inland. Four-cell shore bands are tried
first, using seeded ordering within each band. No colony density input participates in water construction.

For two players, One crosses the center and Two cross at plus/minus 26% of map size. For four/eight,
One lies at square radius 32%; Two at 23% and 40%. At 64, Two uses 20% and 43% so native shoreline
construction cannot merge Wide crossings. Clear widths are 6/10/16 native cells, reduced to 4/6/8 at
64 and 6/8/10 for 128/eight players. Channel-bank variation constrains water eligibility as well as
priority, so maximum water cannot smooth away complexity.

The native validator checks:

- Local footprints, production exits, starting combat clearances, complete reflection orbits and terrain semantics.
- At least two native cells between every neutral colony blocking footprint and actual water, measured
  with eight-neighbor cell distance. Passable footprint cells can form part of this movement space.
- Equal terrain-cost profiles to typed colony pools. None allows unreachable external territories while
  requiring equal local opportunities; One and Two require finite routes to every objective.
- Exactly the selected number of clear crossings on every native border, both before and after actors.
- Separate home components when the intended crossing gates are temporarily closed. This catches
  accidental perimeter bypasses and routes around intended borders.

Synthetic negative controls introduce an extra crossing, remove a required crossing and add a map-edge
bypass; each must be rejected. Independent Python checks inspect exported native pixels, count border
crossings, flood representative closed-gate maps, and compare terrain/objectives across settings.

## Tools and evidence

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -DividedLands -Seed 397716241463670640 `
    -MapSize 256 -Players 4 -LandCrossings one -CrossingWidth standard `
    -TerrainComplexity medium -VerifyRepeatability
```

The wrapper accepts `-LandCrossings none|one|two` and `-CrossingWidth narrow|standard|wide` only
with Divided Lands. The lobby keeps separate layout-specific choices when changing families.

`scripts/rmg/Verify-DividedLands.py OUTPUT --wide` covers the full size/player/crossing/width/complexity
product, water/modifier combinations, strict and relaxed density, all biomes, zero/single-species weights,
ownership modes and exact historical package replays. Historical checks use local accepted artifact corpora.

`OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --divided-lands` checks real lobby
controls, 288 setting combinations (99 valid), invalid fields, negative topology controls, previews,
saved copies, repeated world initialization, AI, hostiles and loopback server startup. Runtime settings
and maps are isolated from the user's normal settings and custom maps.

Generated artifacts and images are under `artifacts/rmg/divided-lands/` and remain ignored.

## Configuration 1 verification (2026-09-10)

- `matrix-final/verification.json`: **809 Divided Lands maps passed**, each generated twice, native
  exported and map linted; **33 accepted historical packages replayed exactly**, including Ring V20.
- **94 density comparisons** preserve water; free-surface comparisons preserve every native terrain
  cell. **746 objective continuity comparisons** preserve starts and colony plans across terrain controls.
- All five complexity levels remain distinct for two/four/eight players with every crossing count,
  including Ultra water. Small-to-Ultra changes at least **3.37% of map cells between water and land**
  in the direct maximum-water response cases.
- `runtime-final/verification.json`: **13 live scenarios passed**, each initialized twice with identical
  results. Covers every biome and size, all crossing modes, real UI/schema checks, saved copies,
  dynamic ownership/recolor/spawn previews, AI, hostiles, loopback server startup and 512/eight-player
  maps with **792 neutral colonies**.
- Full build/runtime-data validation passed. Static analysis retains the existing **236 warnings**;
  Divided Lands adds none. The final Release build has **zero warnings and errors**. The focused
  `regions-ownership-contract` passed, and the public wrapper generated an exact replay of the
  matching matrix package on the final Release binaries.

Visual review covered default territories, every crossing width, Small-to-Ultra complexity under
maximum water, sparse-to-Ultra density, and compact/512-map stress cases. The final sheets include
`comparison-final.jpg`, `review-density.jpg`, `review-stress.jpg` and `review-water-complexity.jpg`
inside `matrix-final/`.

Review caught two issues before delivery: excessive home-site reservation reduced the eight-player
reference to only eight colonies, and native shoreline construction merged two Wide crossings on
64 maps. The home envelope was revised (the reference now fits 64 colonies), and 64-map crossing
positions were separated. All 15 affected compact combinations were regenerated; the final verifier
then passed the full 842-package corpus. The original five failed cases are retained under
`before-compact-fix/`, with their initial campaign log in `matrix-final.log`.

This is generation, native topology and startup evidence, not a claim about long-match competitive
balance. The user's in-game review remains the next acceptance step.

## Configuration 2: tighter shore placement

The user requested less unused shore space while retaining at least two cells for unit movement.
Configuration 2 replaces the straight maximum-channel exclusion with the union of potentially wet
native cells across all five complexity levels. It searches shore sites at native-cell resolution and
prioritizes four-cell shore bands before filling inland sites. The same seed still fixes starts and
colony positions/types across complexity, water, width, crossing and surface choices.

The two-cell rule applies to the actor's blocking footprint. Native `=` footprint cells are passable
and can contribute to movement space; they and production exits must still remain on land. Placement
uses a conservative potential-water check, followed by validation against the actual emitted water.
Every colony in the native validation corpus is checked; representative maps also compare against an
independent pixel/footprint measurement in Python. The clearance check includes diagonal banks.

For screenshot seed `825300756769842102`, 256/eight players, Ultra water/complexity, Low modifiers,
Extreme colonies, None/Narrow crossings, free surface relations and relaxed spacing:

| Measurement | Configuration 1 | Configuration 2 |
|---|---:|---:|
| Neutral colonies placed | 160/168 | 168/168 |
| Average blocking-footprint-to-water gap | 9.34 cells | 7.46 cells |
| Colonies within four cells of water | 8 | 42 |
| Minimum gap | 3 cells | 2 cells |

The complete native terrain is identical for this comparison. The tighter fixed envelope preserves
continuity, so a coast that recedes under a lower water or complexity setting can still leave more
than two cells of open land. Two cells is a minimum clearance, not a target distance for every colony.

Evidence and native before/after previews are under `artifacts/rmg/divided-lands-shore/`:
`shore-comparison.jpg`, `shore-layout-review.jpg`, `clearance-comparison.json` and `after-03/report.json`.

Configuration 2 validation: `matrix-final/verification.json` passed **824 Divided Lands maps** and
**33 exact older-layout replays**. It checks **94 density comparisons**, **760 objective-continuity
comparisons**, and **809 exact water-mask comparisons against configuration 1**; free-surface cases
also preserve all native terrain. Every colony passes the new two-cell clearance check.
`runtime-final/verification.json` passed **13 live scenarios**, each initialized twice. Full build and
runtime-data validation passed; the final static check retains 236 baseline warnings and the Release
build has zero warnings/errors. The public-wrapper replay of the screenshot case is checked against
the native matrix output. In-game acceptance of this refinement remains pending.
