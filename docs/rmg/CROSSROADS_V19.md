# Crossroads V19

## Current status

Crossroads has been revised on `codex/rmg-crossroads` after the user's review of `536088b`;
the revision is ready for their next in-game review.
The revised generator uses **V19 / configuration 2 / player-settings schema 13**. The first version
is not accepted: its complexity subdivisions opened unintended crossings, and colony reservations
could overrule the intended geometry. Its former qualification that None only disabled *planned*
connections did not satisfy the user's intended setting.

Natural Landscape V16, Natural Landscape PVP V17 and accepted Artificial Battlefield V18 retain their
output. No merge or push has been requested. Crossroads remains available through Layout Family;
ordinary Natural Landscape remains the default.

## Route and placement contract

Crossroads has a central junction, symmetric player approaches and continuous terrain dividers between
neighboring approaches. **Side Connections means exactly zero, one or two tiers of ground crossings**
through those dividers. With None, ground travel between player sectors must pass through the central
junction. Complexity changes the outlines and widths of the dividers without splitting them apart.

All four biomes and 64/128/256/512 sizes remain supported. Player counts are 2/4/8, capped at four for
64x64. Two players use opposite approaches; four use corner approaches; eight use an octagonal starting
arrangement. Exact native terrain, start and typed-colony symmetry remains mandatory.

| Control | Revised behavior |
|---|---|
| Approach Width | Narrow / Standard / Wide clear routes, normally 6 / 10 / 16 native cells |
| Side Connections | None / One tier / Two tiers, validated against actual materialized terrain |
| Terrain Complexity | Small through Ultra increase continuous shoreline variation; no additional crossings |
| Water Amount | Existing five target percentages, constrained by continuous dividers and protected routes |
| Surface Modifiers | Existing five targets, respecting water grammar, approaches and occupied footprints |
| Neutral Colony Density | Fills fixed colony areas more heavily; cannot carve water or add connections |

Widths scale to 4/6/8 cells at 64x64 and 6/9/12 at 128x128 with eight players. The latter also spaces its
bypass tiers slightly farther from the center to prevent the first tier merging into wide approaches.
Defaults remain Standard width and One tier. A tier is a complete set of crossings, not a single bridge.

The usual targets cannot all be guaranteed on cramped maps. If the dividers require more water than a
low target, or protected routes limit high terrain targets, the lobby reports this. Colony shortfalls
are reported as placed/target counts. Disabling overlap prevention relaxes colony combat spacing within
the designated areas; physical footprints, exits, player protections and route topology remain enforced.
Colony species, ownership allocation and mode, colored previews, biome hostiles and named saving apply.

## Construction

1. Fix the mirrored starting positions and main approaches. Define continuous radial dividers to the
   playable boundary and the central junction. Reserve the selected square tiers through the dividers.
2. Define fixed colony strips beside the approaches, keeping native footprints away from the dividers.
   These water exclusions do not depend on density, colony species, ownership or overlap policy.
   Larger maps retain an unobstructed central strip through each approach for actors to move through.
3. Rank mirrored colony groups by central contest/expansion proximity and approach shoulder distance.
   Validate actual actor footprints, production exits and combat spacing in the designated strips.
   Apply the existing minimum-overlap fallback only inside those strips. When Original Surface Relations is enabled, reserve only actual occupied
   cells and production exits as clear ground; do not create long colony spurs or extra water exclusions.
   With the option disabled, neutral colonies may occupy any valid non-water surface, and density does
   not alter terrain at all. Starting plazas retain their clear-ground protection.
4. Build continuous divider priorities. Complexity adds seeded variation in width with positive minimum
   width, rather than splitting the dividers into ponds. Require a connected water core outside the
   intended crossings. Reuse the native shoreline and land-transition materializer.
5. Validate protected surfaces, the count of openings on every divider and absence of extra routes.
   Closing only the intended junction and crossing gates must leave every starting sector disconnected
   from the others, even with permissive diagonal movement. This also detects paths around the map edge.
6. Add mirrored passable decorations and existing ownership rules. Reload the saved native map and check
   symmetry, physical footprints, production exits, connected objectives and equal terrain travel costs.

Changing complexity, water, modifiers, width, side connections, biome or ownership preserves the colony
plan for a given seed/size/player count. Changing density or colony policy may change colony placement
and local clear footprints, but **must leave every native water cell unchanged** for otherwise equal
terrain settings. Divider cores remain continuous except at the designated junction and tiers.

The shared fairness report retains `battlefield_*` metric names for connected objectives and weighted
travel costs. Crossroads additionally reports `crossroads_plan.topology`, including actual crossing
counts, separated starting sectors and unintended bypass count. Static symmetry does not imply equal
faction abilities, teams or arbitrary ownership allocations.

## Verification

The expanded matrix is run with:

```powershell
python scripts/rmg/Verify-Crossroads.py artifacts/rmg/crossroads/revision/OUTPUT --wide --workers 4
```

It crosses the user's screenshot seed through all complexity, connection, density and overlap settings;
checks every water/modifier pair under colony pressure; and crosses all supported sizes/player counts
with all widths, complexities and connection counts at low water. Additional cases cover biomes,
ownership, species weights, high-water 512 extremes and exact replays of 19 accepted older packages.

Checks use saved native terrain and actors, not preview hashes alone. They include actual divider opening
counts, topology validation, native terrain identity across density changes, physical clearance, symmetry,
repeatability and native lint. The previous rejected Ultra preview is a negative control: the new opening
count criterion must reject it. Native previews are reviewed in comparison grids across controls and sizes.

The runtime harness includes the user's seed at all three crossing counts, dense relaxed placement,
compact-map wide approaches/two tiers, all biomes, ownership previews, saved copies, AI and server startup:

```powershell
# From engine/, with the normal Utility environment:
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --crossroads
```

The wrapper accepts `-Crossroads -ApproachWidth narrow -SideConnections many`. `-LaneWidth` remains an alias;
`-Pvp`, `-Battlefield` and `-Crossroads` are mutually exclusive.

Evidence and previews are retained under `artifacts/rmg/crossroads/revision/`, excluded from Git.
Verified on 2026-09-10:

- `matrix-final/verification.json`: **786 Crossroads maps accepted**, with native lint and repeatability,
  plus **19 exact accepted older-package replays**. The matrix includes all supported size/player/width/
  complexity/connection combinations, five seeds, every water/modifier pair and the user's screenshot
  scenarios. The prior Ultra preview fails the new opening-count negative control.
- **156 density/colony-configuration comparisons preserve every water cell**. With Original Surface
  Relations disabled, those comparisons additionally require the complete native terrain to be identical.
- Example native terrain response: Small-to-Ultra changes 25.29 percent of cells at four players and
  24.33 percent at eight; Narrow-to-Wide changes 5.02 percent; adding the first and second tiers changes
  9.82 and 11.99 percent respectively. These differences supplement the explicit topology tests.
- `runtime-final/verification.json`: **10 live configurations, each started twice**, all passed. This
  includes all four biomes, AI, both ownership modes, saved maps, lobby/server checks, the user's seed
  at every crossing count, compact wide/two-tier maps and a 512x512 all-Ultra map with **792 colonies**.
- `validation-final.log`: repository build/static checks, native MiniYAML/maps and Lua validation passed.
  The follow-up `style-final.log` returns to the existing **236 warnings / zero errors** baseline.
  The asset inventory retains its existing informational provenance findings.
- `release-final.log`: final Release build passed with zero warnings/errors. `ownership-final.log`: the
  focused Regions ownership regression passed. `wrapper-final/` repeats the fourth screenshot settings
  through the public generator wrapper, with native validation and deterministic repetition.

Visual review covered five-complexity/three-connection and five-density/three-connection grids, plus
64/four-player, 128/eight-player and 512/eight-player width/connection stress previews. The retained
sheets are `matrix-final/review-topology.jpg`, `review-density.jpg`, `review-sizes.jpg` and
`comparison-final.jpg`. Actual rendered world captures are retained with the runtime report.

Capacity remains a visible tradeoff: on the screenshot seed at 256/four players with relaxed spacing,
Sparse/Standard/Dense place 36/48/72 colonies; Extreme and Ultra both fill the available 112 sites
(targets 120/192). Their water geometry is identical. At 64/four players there may be no neutral sites;
compact eight-player maps also have lower capacity. High water can fill most of the available divider
envelope, reducing shoreline variation. These limits are reported and remain subject to gameplay review.
With surface relations disabled, the 512/792-colony stress map retains 36.04 percent gravel and 24.96
percent moss of land, rather than clearing those modifiers for the colonies.

Earlier `matrix-01` exposed 76 instances of an overextended colony-clearance border; it is retained as
failed diagnostic evidence. `matrix-02` passed 771 maps before the final free-surface correction and the
additional 15 density cases. `matrix-final` is the delivery evidence. No prior Crossroads configuration
was frozen; V19/configuration 2 intentionally regenerates it differently. Accepted V16/V17/V18 outputs
are preserved. The user's in-game acceptance is still pending.
