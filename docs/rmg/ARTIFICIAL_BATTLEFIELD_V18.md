# Artificial Battlefield V18

## Scope and status (2026-09-09)

Branch: `codex/rmg-artificial-battlefield`, based on accepted Natural Landscape PVP `15f4882`.
The current implementation is **generator 18 / configuration 2 / player schema 12**, pending renewed
user in-game review. No merge or push was requested. Ordinary Natural Landscape V16 and Natural
Landscape PVP V17 retain their accepted output; historical V7 remains available through older schemas.

The user requested deliberate geometric terrain, pre-planned colonies and fair PvP opportunities.
Their review of configuration 1 rejected the weak response to Block Shape, Lane Width and Complexity:
seed `104842342679145068`, 256x256, eight players kept four large reservoirs around a straight cross.
Hash differences and successful gameplay validation did not establish sufficient design variation.
Configuration 2 replaces that plan, rather than tuning only the old scalar detail strengths.

## Controls

Select **Layout Family -> Artificial Battlefield**. Defaults remain **Cut Corners / Standard lanes**;
ordinary Natural Landscape remains the global RMG default.

| Control | Effect |
|---|---|
| Block Shape: Rectangles | Rectangular terrain compounds and right-angle route corners |
| Block Shape: Cut Corners | Chamfered terrain compounds and beveled route corners |
| Block Shape: Diamonds | Diamond distance fields and diagonal route corners |
| Lane Width: Narrow / Standard / Wide | Nominal protected ground corridors of 6 / 10 / 16 native cells |
| Terrain Complexity: Small / Medium / High / Extreme / Ultra | 0 / 1 / 2 / 3 / 4 recursive district splits, yielding 1 / 2 / 4 / 8 / 16 blocks per full district; increasing route offsets |
| Water Amount | Selects water coverage from those blocks; retains 16 / 20 / 24 / 32 / 44 percent targets |
| Surface Modifiers | Selects gravel/moss coverage, including travel lanes outside colony plazas |

The nominal block count describes construction fields, not a guarantee of that many water bodies:
coverage, reflection, map edges, routes and native tile transitions can split or join visible regions.

Normal, Desert, Swamp and Candy support 64, 128, 256 and 512 square maps. Battlefield supports 2, 4
or 8 players, with a four-player cap at 64. Two players use one reflection axis (vertical for even
seeds, horizontal for odd), four use horizontal/vertical reflection, and eight also use diagonals.
All starts belong to one symmetry group. Natural families retain their own player limits and controls.

All five quantity levels, colony species weights, overlap prevention, original surface relations,
starting ownership shares and modes, colored previews, biome hostiles and named map saving apply.

## Construction and seed continuity

1. **Player plazas:** fix opposite ends for two players, corners for four, or an approximately regular
   eight-position octagon. Validate the union of possible starting species' footprints, exits and
   combat spacing. These starting positions match configuration 1.
2. **Distributed colony plan:** rank complete player-sized groups on an eight-native-cell lattice by
   distance from starts and previously ranked groups. A seeded tie order preserves repeatability.
   This spreads candidate opportunities into the interior instead of prioritizing perimeter streets.
   The existing weighted-species, strict spacing and optional minimum-overlap placement run afterward.
   Physical footprints, exits and starting protection never relax. Targets round down to complete
   player-sized groups; actual placement can still be below target.
3. **Ground routes:** build a minimum spanning network between all objectives and a central junction.
   Canonicalize each undirected edge under the selected reflections, then route it once and reflect
   the result. This avoids accumulating multiple independently bent copies of the same connection.
   Complexity adds local doglegs with offsets up to 0 / 4 / 8 / 12 / 16 native cells, also capped at
   one quarter of an edge's length. Block Shape controls square, chamfered or diagonal corners.
   Reflection introduces matching connections and local loops.
4. **Separate reservations:** colony plazas and the actual footprint/exit union stay clear. Connected
   routes exclude water but allow gravel and moss, so Surface Modifiers can influence movement costs.
   Colony plazas do not grow with Lane Width. Both reservations include the complete reflected union
   because stock actor footprints are not themselves rotated. After materialization, every protected
   clear cell and every protected ground cell is checked against actual native terrain.
5. **Nested geometric districts:** the seed fixes district pitch (48 on 64/128 maps, otherwise
   88/96/104), roles and cut positions. Complexity recursively splits those same districts in alternating
   directions, with seed-selected 44/48/52/56-percent cuts. Rectangle, chamfer and diamond distance
   fields shape the resulting blocks. Water and surface quantities select coverage from these fields.
   Whole-group selections, contact cleanup and the existing native tile banks preserve exact symmetry.
6. **Finish and validate:** add mirrored passable doodads, write/reload the engine package, and check
   actual terrain and actors. Ownership applies afterward using the accepted slot/closest/random rules.

Changing only complexity, shape, width, water, modifiers, surface relations, biome or ownership keeps
start and colony positions/types fixed. Complexity refines the same district partition, while routes
bend locally around the fixed objectives. Density, species weights, overlap policy, players, size or
seed may change the colony plan. Configuration 1 colony placement is deliberately superseded.

Original Surface Relations controls contact rules outside the clear plazas. Moss still uses the stock
nested gravel envelope. Ground corridors are not guaranteed to be bare dirt or the fastest path.
The native fairness check includes their actual movement costs. Natural Landscape retains its separate
policy without a global ground-access requirement.

Routes, physical colony space and legal surface transitions can limit coverage, especially on small or
crowded maps. The report records actual coverage and percentage-point shortfalls. The lobby displays
**Routes/plazas/transitions limit coverage** when a target misses by more than two percentage points.
At 64 with four players there may be no room for neutral colonies; high density on larger maps can also
reach capacity. Large maps remain the useful setting for judging the full range of geometric detail.

## Validation

The native gate verifies exact reflected surfaces, typed colony/start symmetry, zero footprint overlap,
valid production exits and local colony terrain. It checks that every objective shares ground access
after neutral buildings and the union of possible starting footprints are applied. Weighted pathing
then compares each player's sorted travel costs to every colony species pool and other starting anchors.
This establishes equivalent static terrain opportunities, before asymmetric starting ownership choices;
it does not claim that every faction matchup or human strategy is equally strong.

`Verify-ArtificialBattlefield.py` now contains the rejected screenshot seed, all five complexity levels
for each shape, width comparisons and isolated water changes. In addition to repeatability and native
validation, it requires meaningful differences in actual materialized surfaces: at least 10 percent of
cells between Small and Ultra, 3 percent between the tested shape pairs, and 5 percent between Narrow
and Wide. It checks an interior objective group, a ten-percentage-point water response and that Extreme
modifiers remain substantial with Wide lanes. These are regression floors, not a substitute for visual
inspection. The same matrix replays accepted V16/V17 and historical V7/V8 packages byte for byte.

The opt-in runtime command `--validate-sa-rmg-runtime OUTPUT --wide --battlefield` exercises real widgets,
map saving, lobby/server setup, AI, ownership previews and repeated world startup across all four biomes.
It also includes the reported eight-player Diamonds/Wide/Ultra setup. Generated evidence stays under
`artifacts/rmg/artificial-battlefield/`, excluded from version control. No original artwork is committed.

Configuration 2 verification results:

- `artifacts/rmg/artificial-battlefield/revision-native-01/verification.json`: **65 accepted Battlefield
  maps**, each generated twice, package-linted and checked against native terrain; **11 exact older
  package replays**. The largest logical generation time in this local matrix was about 1.28 seconds.
- On the reported seed with Narrow lanes and Extreme water, Small-to-Ultra changes **25.8-33.5 percent**
  of native surface cells, depending on shape. Ultra Diamonds versus Cut Corners changes **10.0 percent**;
  versus Rectangles, **19.8 percent**. Narrow versus Wide changes **25.3 percent**. Low-to-Extreme water
  at Small complexity changes coverage from **15.2 to 30.4 percent**. These comparisons change only the
  named control. The five-level strip is `revision-native-01/complexity-comparison.jpg`.
- The reported Diamonds/Wide/Ultra case now has **29.5 percent water** and **17.3/15.8 percent gravel/moss
  of land**, compared with configuration 1's 28.1 percent water and 0.5/0.0 gravel/moss. The new spread
  places 48 of the requested 60 colonies, including an eight-colony interior group; the earlier plan
  placed 56 around the perimeter. The capacity warning reports this shortfall. The annotated native
  preview comparison is `revision-native-01/review-comparison.jpg`.
- `revision-runtime-01/verification.json`: **five passing runtime cases, each initialized twice**,
  including the reported eight-player map, 64/128/256/512 sizes, all biomes, AI, both ownership modes,
  actual widget integration, named saved copies and lobby/server startup. Screenshots accompany it.
- `artifacts/rmg/battlefield-revision-validation.log`: full repository build and runtime-data validation
  passed. A final style build (`battlefield-revision-style.log`) has the same 236 existing warnings as
  the preceding checkpoint, with no new warnings. The final Release build has zero warnings/errors,
  recorded in `battlefield-revision-release-final.log`. Focused settings/ownership regression also passed
  (`battlefield-revision-ownership.log`).

Automated fairness and startup checks complement, but do not replace, the user's gameplay review.
Crossroads, Ring, Divided Lands, Strongholds, Labyrinth and Chaos remain separate future work.
