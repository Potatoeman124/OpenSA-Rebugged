# Artificial Battlefield V18

## Scope and status (2026-09-09)

Branch: `codex/rmg-artificial-battlefield`, based on `15f4882`, the Natural Landscape PVP
implementation accepted by the user on 2026-09-09. No merge or push was requested for this step.
This replacement for Artificial Battlefield is **generator 18 / configuration 1 / player schema 12**,
pending user in-game review. Historical Artificial Battlefield V7 remains available through its older
settings schemas. Ordinary Natural Landscape V16 and Natural Landscape PVP V17 remain unchanged.

The user requested visibly artificial, geometric terrain, pre-planned colony placement and a field
built for fair PvP, delegating parameter design. The central design decision is to reserve player
plazas, colony sites and connected clear lanes before constructing water and slow terrain. This is
separate from the terrain-first Natural families. Their absence of a global ground-access requirement
still applies to them; Battlefield has an explicit ground-access and terrain-cost parity requirement.

## Player controls

Select **Layout Family -> Artificial Battlefield**. The two layout-specific controls are:

| Control | Choices | Effect |
|---|---|---|
| Block Shape | Rectangles, Cut Corners, Diamonds | The geometric distance shape used for water and slow-terrain blocks and their subdivisions |
| Lane Width | Narrow, Standard, Wide | Nominal clear street widths of 6, 10 or 16 native cells, with larger plazas around actors |

Default Block Shape is **Cut Corners** and Lane Width is **Standard**. The global RMG default stays
ordinary Natural Landscape. Quantity presets retain the selected Battlefield family.

Terrain Complexity controls subdivision inside fixed districts, rather than relocating starts or
colony sites. Low uses the broad blocks; Medium through Ultra add progressively stronger geometric
substructure. The detail strengths are 0, 0.35, 0.70, 1.20 and 2.00. These are specific to Battlefield;
the accepted Natural Landscape 2.70/2.60 Ultra calibration is untouched.

Normal, Desert, Swamp and Candy support 64, 128, 256 and 512 square maps. Battlefield supports **2, 4
or 8 players**, limited to 2 or 4 at 64. The slider selects those discrete counts. Eight players use
horizontal, vertical and diagonal symmetry; four use horizontal and vertical symmetry. Two have
opposite starts and one reflection axis, vertical for even seeds and horizontal for odd seeds. Every
start belongs to one complete symmetry group. Natural families retain their own player ranges.

All five levels of Water Amount, Surface Modifiers and Neutral Colony Density remain available,
along with colony species weights, overlap prevention, original surface relations, both starting
ownership modes/shares, colored colony previews, biome-specific hostiles and named map saving.
The retired Battlefield Plan control is replaced by the new controls in this family.

## Construction

1. **Player plazas:** reserve opposite ends for two players, corners for four, or an eight-position
   approximately regular octagon for eight. Starting positions use fixed native coordinates and are
   checked against all possible starting species' footprints, production exits and combat envelopes.
2. **Colony plan:** choose sites from a regular eight-native-cell lattice in complete player-sized
   groups. Prioritize expansion opportunities about 36 native cells from starts, then proximity to the
   planned street grid. The seed resolves the remaining order. Draw a species once per group according
   to the existing weights. Zero-weight exclusions and all-zero disabling still apply.
3. **Spacing:** use the accepted strict combat-spacing rule first. If overlap prevention is disabled,
   use the existing minimum-overlap ordering within remaining planned sites. Footprints, exits and
   starting protections stay mandatory. Targets round down to complete groups and may hit capacity.
4. **Streets and plazas:** construct an orthogonal street network and outer supply routes, connect
   every planned objective to it, and reserve clear ground around all actor footprints and exits.
   Reflect the complete reservation, including asymmetric stock footprint offsets. District spacing
   is 48 native cells on small maps and seed-selected 80/88/96 on larger maps. Lane Width changes these
   reservations without relocating the objectives.
5. **Terrain blocks:** construct deterministic rectangle, chamfered-square or diamond distance fields
   per district. The seed fixes district roles and shape variation. Complexity adds geometric detail;
   quantities select coverage from the same fields. All selections and transition cleanup operate on
   complete reflection groups, using the existing audited native tile materializer and biome mapping.
6. **Finish:** validate the reserved clear space, add mirrored passable doodads, emit the map, reload
   it through OpenRA, and validate native gameplay surfaces and actual actors.

Changing only **Block Shape, Lane Width, Terrain Complexity, Water Amount, Surface Modifiers,
Original Surface Relations, biome or ownership** preserves planned start and colony positions/types.
Changing density, species weights, overlap policy, players, size or seed can change the colony plan.
This is deliberate continuity: terrain controls shape the field around an established objective plan.

All plazas remain clear even with Original Surface Relations off. That toggle still controls the
surface-contact restriction outside the protected plan. Moss remains within the gravel envelope,
using the accepted tileset grammar. Wider streets and larger colony populations can consume terrain
capacity. The generator reports actual coverage and displays **Lanes/plazas limit terrain coverage**
when a requested water/rock/moss target is missed by more than two percentage points. It never moves
objectives or weakens clearance simply to fill a terrain percentage.

At 64 with four players, the protected plazas and streets can consume the entire map, with no space
for neutral colonies or modifiers. This is a supported but capacity-limited close-quarters configuration;
256 is the useful starting size for assessing the full layout. At high colony densities, large maps
can also reach the planned-site limit or leave very little room for water/modifiers.

## Fairness and access validation

The native gate checks exact reflected terrain and typed colony/start positions, zero physical
footprint overlaps, valid production exits, local terrain requirements, and the stock movement costs.
It adds two Battlefield checks:

- All player plazas and neutral-colony objectives must share a ground-access component after actual
  neutral footprints and the union of all possible starting footprints are applied.
- Run weighted native terrain pathing once from each starting anchor. Compare the complete sorted
  distance distribution to each colony species and to the other starting anchors. Every player must
  have the same reachable cost profile. Costs use the established ground validator's Clear/Rock/Moss
  and diagonal-step rules. This compares terrain opportunities before static actor obstruction and
  ownership; occupied access is checked separately above.

Starting ownership remains a later operation based on lobby slots, selected starts and configured
Closest to Spawn / Random mode. Unequal shares, factions, teams and runtime hostiles are intentional
lobby choices, outside the equal-terrain-opportunity guarantee. Native validation does not claim
balance for every faction matchup or predict dynamic battle behavior.

## Reproduction

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Battlefield -Seed 1 -MapSize 256 -Players 2 `
    -BlockShape diamonds -LaneWidth narrow -Tileset DESERT -StartingColonyShares 40,80 `
    -StartingColonyMode random -VerifyRepeatability

python scripts/rmg/Verify-ArtificialBattlefield.py artifacts/rmg/artificial-battlefield/native-new
```

The public wrapper retains its name and all existing arguments; `-Battlefield` selects schema 12.
It cannot be combined with `-Pvp` or `-MirroringAxes`. The JSON fields are `block_shape`
(`rectangles`, `cut-corners`, `diamonds`) and `lane_width` (`narrow`, `standard`, `wide`). They require
schema 12 and explicit `layout_family: artificial-battlefield`. Other families and older schemas reject
them. Symmetry is derived from the player count; no manual axis field is accepted for Battlefield.

The matrix uses local accepted artifact corpora for preservation cases. `--verify-only` checks an
existing matrix without generation. The runtime command is
`OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --battlefield`, with the normal local
engine/runtime environment. It uses isolated worlds/map-save locations and does not save user settings.

## Evidence

- **47 accepted Battlefield maps** and **11 exact older-package replays**, recorded in
  `artifacts/rmg/artificial-battlefield/native-01/verification.json`. Coverage includes all three
  shapes, all five complexity and quantity levels, every map size, 2/4/8 players, multiple seeds,
  all four biomes, disabled/excluded colony species, both ownership modes, and strict/relaxed all-Ultra
  512 placement. Every package is generated repeatedly, linted and reloaded. Exported native semantics
  and actor orbits are independently checked. No capacity rejection occurred in this matrix.
- Tests verify fixed objectives under terrain controls, distinct outputs for all shapes/complexities
  and lane widths, nondecreasing achieved water/total-modifier/colony coverage as corresponding targets
  increase, and exact theme-only geometry preservation. Individual gravel coverage can fall as moss
  occupies more of its envelope; combined modifier coverage is the appropriate quantity comparison.
- Eleven preservation cases cover seven V16 packages, two V17 packages, historical V7 Artificial
  Battlefield and V8 Structured Competitive. Map package member contents compare exactly.
- Live tests cover four biomes, all four sizes, 2/4/8 players, ownership allocation/preview agreement,
  recoloring and changed spawn choices, saved map copies, hostiles, bots, repeated world initialization,
  and an actual loopback server start. Real widgets exercise **288 Battlefield settings combinations
  (99 valid)** plus the existing 96 PVP combinations. Logs and screenshots are under
  `artifacts/rmg/artificial-battlefield/runtime-final/`.
- The public PowerShell wrapper and focused existing settings/ownership tests pass. Full repository
  validation passes with 236 existing Debug style warnings (one fewer than the preceding checkpoint)
  and no errors. The final Release build has zero warnings/errors. Logs:
  `battlefield-validation-final.log`, `battlefield-release-final.log`, `battlefield-regression-final.log`
  and `battlefield-wrapper-final.log` under `artifacts/rmg/`.
- Typical 256 generation in the matrix is about 0.13-0.16 seconds. The eight-player 512 all-Ultra case
  takes about 0.45 seconds with strict spacing (592/792 colonies), or 1.04 seconds with relaxed spacing
  (792/792). Their water coverage is respectively 8.5% and 2.5%, illustrating the terrain-space tradeoff.
  These are local logical-generation times, excluding package/native validation and long-session play.

Generated maps, images, native exports and reports remain ignored artifacts. No original map or
artwork is added to version control. The next step is the user's in-game review of this implementation;
Crossroads and the later layout families remain separate future work.
