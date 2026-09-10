# Crossroads V19

## Status and scope

Implemented on `codex/rmg-crossroads` from Artificial Battlefield commit `17881c4`, accepted by the user
on 2026-09-10. Crossroads is **generator 19 / configuration 1 / player-settings schema 13**. The user
requested the next layout in the agreed sequence; this implementation awaits their in-game review.
No merge or push was requested. Accepted Natural Landscape V16, Natural Landscape PVP V17 and
Artificial Battlefield V18 retain their output.

Crossroads concentrates conflict around a central junction. Every player has a clear approach to that
junction, with colonies near the central contest and along approach shoulders. Long terrain dividers
separate neighboring approaches. Optional connections cross those dividers to provide bypass routes.
The layout uses the same exact reflection and native fairness checks as Battlefield, but its main routes
and objective priorities are built around the junction rather than Battlefield's distributed network.

The older `tactical-crossroads` quantity preset remains a preset; it does not select this new layout.
Choose **Layout Family -> Crossroads** explicitly. Changing a quantity preset retains Crossroads.
Ordinary Natural Landscape remains the global default.

## Player controls

| Control | Choices and behavior |
|---|---|
| Approach Width | Narrow / Standard / Wide: nominal clear main-route widths of 6 / 10 / 16 native cells |
| Side Connections | None / One tier / Two tiers: 0 / 1 / 2 sets of links between neighboring approaches |
| Terrain Complexity | Small / Medium / High / Extreme / Ultra: 0 / 1 / 2 / 3 / 4 recursive basin subdivisions |
| Water Amount | The accepted five-level 16 / 20 / 24 / 32 / 44-percent water targets |
| Surface Modifiers | The accepted gravel/moss amounts, subject to colony, approach and transition space |
| Neutral Colony Density | The accepted five levels, with complete player-sized mirrored placement groups |

Defaults are Standard approach width and One tier of side connections. Side links are protected ground
and can contain modifiers. Main approaches, the central junction and colony footprints remain clear.
None means no *planned* side connections; passable terrain can still offer other ways around the center.
Wide approaches and high colony density can restrict terrain coverage. The lobby reports actual coverage
and capacity shortfalls rather than violating actor or route requirements.

All four biomes and 64/128/256/512 sizes are supported. Player counts are **2, 4 or 8**, limited to **2 or
4 at 64x64**. Two players have opposite approaches, four have corner approaches forming an X, and eight
have an approximately regular octagon of approaches. Two-player reflection is vertical for even seeds
and horizontal for odd seeds; four players use horizontal/vertical reflection, eight also use diagonals.
At 64 with four players, colony spacing can leave no room for neutral colonies.

Colony weights, overlap prevention, original surface relations, starting ownership percentages/weights,
closest/random ownership choice, colored previews, biome hostiles and named custom-map saving apply.
Species and footprint rules are shared with the accepted generators. Ownership is applied after layout
construction and may intentionally make the initial holdings asymmetric.

## Construction and continuity

1. Fix player plazas and validate the union of all possible starting-colony footprints, exits and combat
   spacing. Reserve those cells before considering neutral colonies.
2. Rank mirrored colony groups on a four-cell native lattice. Prefer a central contest band at about
   28 percent of the starting radius, then expansion positions around 76 percent, then remaining sites
   near approach shoulders. Seeded tie-breaking and weighted species draws are deterministic. Strict
   combat spacing applies first, with the existing minimum-overlap fallback when prevention is disabled.
3. Connect each start to the center. Draw the junction as a clear disk, with radius six percent of map
   size clamped to 6-30 native cells. Connect each colony to its nearest main approach with a short
   ground spur. Side tiers join neighboring approaches at 48 and 74 percent of the starting radius.
   Two-player side tiers add transverse junctions to form bypasses on both sides of the central route.
4. Keep separate clear-ground and no-water reservations. Include the actual footprint/exit union and
   reflect the complete masks, accounting for asymmetric stock actor footprints. Terrain must preserve
   every clear cell and every ground cell after native materialization.
5. Place elongated basin fields between approaches. Small uses the broad basins; subsequent levels
   recursively divide their longer dimension first. This avoids sub-tile detail in narrow eight-player
   sectors. Seed-fixed cuts are 46/48.5/51/53.5 percent; the partition decisions do not depend on the final
   chosen depth. Each child also receives a deterministic priority bias, varying which sections fill
   first and avoiding uniformly thin fragments. Water fills these fields, while related geology/moisture
   fields form land modifiers.
   The native tile grammar and Original Surface Relations still govern materialization.
6. Add mirrored passable doodads, write and reload the map, then validate actual native surfaces,
   footprints, production exits, ownership rules, symmetry and terrain travel costs.

Starts and colony positions/types stay fixed when only terrain complexity, approach width, side
connections, water, modifiers, surface relations, biome or ownership change. Complexity refines the
same basin arrangement. Density, species weights, overlap policy, seed, size or players can alter the
colony plan. The exact target count is not guaranteed when native spacing or terrain capacity is tight.

## Validation and inspection

`Verify-Crossroads.py` generates repeatable native maps across controls, player counts, map sizes,
biomes and ownership configurations. It checks actual saved terrain and actors, production exits,
physical overlap, the clear junction and every approach centerline, ground access after footprints,
and equal weighted travel costs to typed colony pools and opposing starts. It checks parameter response
on native surfaces and exact older-package replay, including the accepted Battlefield revision.

The shared fairness validator retains its existing `battlefield_*` report field names. In Crossroads
these describe the same connected-objective and weighted-cost checks. They establish symmetric static
opportunities, not universal balance between faction abilities, teams or arbitrary ownership shares.

The opt-in runtime harness supports:

```powershell
# From engine/, with the normal Utility environment configured:
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --crossroads
```

It checks actual lobby widgets, option serialization, player limits, ownership sliders, retained settings
when switching families, map saving, loopback server startup, AI and repeated worlds across all four
biomes. The public wrapper supports `-Crossroads -ApproachWidth narrow -SideConnections many`; the older
`-LaneWidth` spelling is also accepted. `-Pvp`, `-Battlefield` and `-Crossroads` are mutually exclusive.

Example:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Crossroads -Seed 397716241463670640 `
  -Players 4 -MapSize 256 -TerrainComplexity medium -ApproachWidth standard `
  -SideConnections standard -VerifyRepeatability
```

Verified on 2026-09-10:

- `native-02/verification.json`: **50/50 Crossroads maps accepted**, repeated generation and native lint
  passed, plus **19/19 exact older-package replays**. Cases cover all sizes, biomes and complexity levels,
  both two-player orientations, 4/8 players, quantity extremes, relaxed spacing, zero/single species
  weights, both ownership modes and 512 all-Ultra configurations.
- Native Small-to-Ultra changed **18.78 percent** of terrain cells for the four-player comparison and
  **12.40 percent** for eight players. Narrow-to-Wide changed **6.02 percent**; None-to-One-tier changed
  **8.25 percent**, One-to-Two-tiers **11.51 percent**. These are measured example responses, not universal
  guarantees for every size or capacity-limited configuration. Starts and colony positions/types stayed
  fixed in the continuity comparisons. Maximum logical generation time in this matrix was **2.46 s**.
- `runtime-final/verification.json`: **four live-world cases, each started twice**, including 64 Normal,
  128 Desert, 256 Swamp and 512 Candy, saved map copies, AI opponents, hostile settings, ownership previews
  and loopback lobby/server startup. Actual UI checks include 288 Crossroads size/player/width/connection
  combinations (99 valid), invalid fields, ownership row counts and switching back to accepted layouts.
- `wrapper-final/`: the public PowerShell wrapper generated a repeatable 256 Desert / eight-player /
  Ultra-complexity map with mixed-case `-ApproachWidth Narrow -SideConnections Many` and random ownership.
- `validation-final.log`: the full repository build, static checks, MiniYAML/map validation and Lua syntax
  checks passed. Asset inventory remains informational, with existing unresolved provenance records.
- `style-final.log`: the subsequent static check returned to the existing **236 warnings / zero errors**
  baseline. `release-final.log`: final Release build passed with **zero warnings / errors**.
  `ownership-final.log`: the focused Regions ownership regression passed on that Release build.

The comparison sheet is `native-02/crossroads-comparison.jpg`. Automated checks establish static layout
contracts and successful startup; the user's gameplay review is still pending.

Evidence, previews and performance reports live under `artifacts/rmg/crossroads/` and are ignored by Git.
Only original code, configuration and documentation are committed. Ring, Divided Lands, Strongholds,
Labyrinth and Chaos remain later, separate layout steps.
