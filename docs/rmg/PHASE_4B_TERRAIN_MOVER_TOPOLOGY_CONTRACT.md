# OpenSA RMG Phase 4B: Terrain, Mover, and Blocking-Topology Contract

Status: **FROZEN**

Target implementation: **Generator Version 2 / `normal-water-blocking-v2`**

Frozen against: Generator Version 1, the Phase 1 corpus audit, and the Phase 4A native-movement gate

## 1. Decision summary

The first blocking-topology slice will use homogeneous `Water` terrain on the existing 2x2 logical grid. `Water` is a real blocker for the standard ground `unit` locomotor. `Rock` and `Vegetation` are not blockers: they are traversable at higher pathing cost. No blocking actor, invisible wall, height cliff, or terrain-plus-actor combination is authorized in the first slice.

The first slice deliberately uses hard 2x2 macro-cell shores. It does **not** use mixed Clear/Water transition templates. The audited NORMAL tileset contains only all-Water templates and one-Clear-corner transition masks; it does not contain the complete neighbor-mask set needed to tile arbitrary shorelines without changing passability. The resulting visual seams are accepted for the technical Version 2 slice and must be identified as such in diagnostics. A later visual shoreline contract may add transitions, but it must preserve the logical/native passability proven here.

Ground topology is authoritative for ants, beetles, scorpions, spiders, ground creeps, and other actors using the `unit` locomotor. Wasps use their own custom movement layer and intentionally traverse Water and Air. Version 2 supports wasp playability and validates legal wasp exits/landing access, but it does not claim that ground chokepoints constrain wasp armies. Changing wasp movement to make the terrain contract simpler is prohibited.

Generator Version 1 remains frozen and Clear-only. Phase 4C must add Version 2 as a separate version/profile and must not change Version 1 same-seed output.

## 2. Evidence and authoritative sources

The contract is based on these current project facts:

- `mods/sa/rules/misc.yaml` defines `unit` with Clear speed 100, Rock 75, and Vegetation 50. Water and Air are absent and therefore impassable. The corresponding pathing costs observed by the native validator are 100, 133, and 200.
- The same file defines `wasp` with speed 100 on Clear, Rock, Vegetation, Water, and Air, and transition cost zero.
- `OpenRA.Mods.OpenSA/Traits/World/WaspLocomotor.cs` disables the normal domain-passability check, and `WaspActorLayer.cs` supplies the custom Air layer.
- `mods/sa/tilesets/normal.yaml` defines only 2x2 templates. The selected Clear and Water IDs below resolve all four frames to one terrain type.
- `mods/sa/rules/core.yaml` gives normal ground units the `unit` locomotor. No standard mobile footprint larger than one native cell was found in the Phase 4A rules audit.
- All five colony families use 6x6 building dimensions with different `x`/`=` footprints. Most ground production exits are at relative native cell `5,5`; the wasp colony exit is `6,5`. Native validation must use the resolved actor definitions, not a guessed 6x6 solid square.
- The Phase 1 reference corpus measured important route widths from 1 through 31 native cells, with quartiles 3, 5, and 11. Widths 1-3 were the deliberate-choke regime; 5-11 formed the normal middle band. Across 27 starts, the radius-12 eight-sector exit count ranged from 3 through 8 with median 8.
- The Phase 4A campaign accepted 3400/3400 logical cases and 34/34 save/reload/lint/native samples. Its minimum observed global widest-path width was 11 native cells. It also proved that the legacy colony proxy has both false-negative and false-positive cells, so native terrain and exact actor footprints are authoritative.
- Phase 4A strategic edges are abstract node-to-node reachability promises. Version 2 must turn each edge into an explicit reserved corridor before obstacle placement; global detours may not be counted as conformance to a named edge.

The measurements above calibrate this contract; they are not permission to copy map data or copyrighted assets. The repository records identifiers and behavior only. Required game assets continue to come from the external installation.

## 3. Coordinate and measurement conventions

These terms are normative:

| Term | Definition |
| --- | --- |
| Native cell | One OpenRA `CPos` in the 128x128 playable bounds. |
| Logical cell | One generator cell materialized as a 2x2 native-cell template. The Version 2 logical grid remains 64x64. |
| Ground mover footprint | One native cell for the `unit` locomotor. Colony/building footprints are separate static obstructions. |
| Cell-set distance | Minimum Chebyshev distance between any native cell in each set. A distance of 1 means adjacency and zero intervening cells. |
| Nominal aperture | The literal passable cross-section produced by macro-cell materialization. It is even for the homogeneous-only first slice. |
| Effective native width | The Phase 4A clearance-diameter metric `W = 2d - 1`, where `d` is the largest Chebyshev-clearance threshold along a connecting path. A nominal 4-, 6-, or 10-cell straight aperture resolves to effective width 3, 5, or 9 respectively. |
| Symmetry orbit | A cell, region, route, choke, actor, or repair together with every image required by the selected horizontal, vertical, or rotational symmetry. |

All route thresholds are hard-validated using **effective native width**. Nominal apertures are also recorded so a future validator cannot silently reinterpret an even macro-aligned opening as its odd clearance diameter.

## 4. Blocking-options audit

### 4.1 Homogeneous Water terrain

- **Source:** `mods/sa/tilesets/normal.yaml`; `Locomotor@Unit` and `WaspLocomotor` in `mods/sa/rules/misc.yaml`.
- **Mechanism/pathfinder behavior:** terrain-based. Water is absent from `unit.TerrainSpeeds`, so every Water native cell is ground-impassable. Wasps can cross it at speed 100.
- **Visual/editor behavior:** rendered as water. The custom editor brush paints selected template frames; it does not synthesize a complete shoreline. The terrain `MapCreated` hook randomizes `PickAny` templates for editor-created maps, but the RMG constructs and saves its map directly and must select every template deterministically itself.
- **Colonies/footprints:** colony coverage, production exits, start clearances, and neutral-colony clearances must remain on Open terrain. Water needs no extra actor occupancy and cannot overlap any reserved footprint/apron.
- **Procedural suitability:** high on the logical grid. Four native cells share the same meaning, native passability exactly matches the semantic cell, serialization is compact, and runtime actor count does not grow.
- **Transition requirement:** arbitrary polished shores are not currently representable with the audited mixed masks. Hard macro shores are therefore the only authorized first-slice boundary.
- **Determinism:** deterministic when the generator chooses an allow-listed template from a named RNG stream and writes all four frames explicitly.
- **Verdict:** **selected** for Version 2 ground blocking.

### 4.2 Rock and Vegetation terrain

- **Source:** the same terrain and locomotor files.
- **Mechanism/pathfinder behavior:** terrain-based movement restriction, not blocking. Ground pathing costs are 133 for Rock and 200 for Vegetation; both remain connected ground.
- **Visual/editor behavior:** appropriate rough-ground visuals exist, but their Clear/Rock and Rock/Vegetation transitions also change native movement cost at frame granularity.
- **Colonies/footprints:** legal under colonies from a binary-passability perspective, but weighted access and production egress would need separate fairness rules.
- **Procedural suitability:** useful for a later rough-terrain layer, not for logical `BLOCKED`.
- **Transition requirement:** a deterministic weighted-terrain boundary tiler and weighted-route validation.
- **Determinism:** feasible, but not yet contracted.
- **Verdict:** **forbidden as `BLOCKED`; deferred as a future `SLOW` category**.

### 4.3 Existing occupied actors and decorations

- **Source:** `Building`, `IOccupySpaceInfo`, and resolved actor rules. `^CoreDecoration` defaults to a 1x1 `Building` footprint `x`; some decoration actors override it with `_` and do not occupy the cell.
- **Mechanism/pathfinder behavior:** actor-based. Resolved `x`/`X` building cells and other occupied cells block the ground layer; `=` stays pathable and `+` is transit-only. The Phase 4A validator already distinguishes these symbols.
- **Visual/editor behavior:** visible and editor-placeable, but a small plant can block a whole cell and communicate wall strength poorly. Random-sprite decorations add another visual-selection path. Ground actor occupancy is not a reliable way to constrain the wasp custom Air layer.
- **Colonies/footprints:** actors need overlap checks, ownership, map actor definitions, and exact spacing from colonies and production exits. They also participate in targeting/interaction rules according to their actor type.
- **Procedural suitability:** acceptable for sparse decoration after topology, poor as the primary wall system because wall area becomes actor count.
- **Transition requirement:** no terrain shoreline, but coherent visual clustering and edge art would still be required.
- **Determinism:** placement and actor order must be canonical; runtime sprite variation must not define gameplay.
- **Verdict:** **not authorized as a Version 2 blocker**.

### 4.4 Purpose-built invisible blocker actors

- **Source:** no safe existing Version 1 actor; this option would require a new actor contract.
- **Mechanism/pathfinder behavior:** actor occupancy can block ground cells, subject to movement layer behavior.
- **Visual/editor behavior:** invisible walls are misleading in play and difficult to diagnose in the editor unless paired with an overlay or visible asset.
- **Colonies/footprints:** exact overlap and exit exclusions are mandatory. Ownership, serialization, map lint, targeting, crushing, fog behavior, and deletion behavior would all need specification.
- **Procedural suitability:** predictable geometrically but expensive in actor definitions/count and poor for player comprehension.
- **Transition requirement:** would require a separate visual layer that exactly matches occupancy.
- **Determinism:** canonical actor IDs/order and a hard actor-count budget would be required.
- **Verdict:** **rejected for the first slice**.

### 4.5 Terrain plus actor blockers

- **Mechanism/pathfinder behavior:** could pair visual Rock or decoration with invisible occupancy, but creates two sources of truth that can disagree. Pairing Water with a blocker is redundant for ground movement and still does not create a shared wasp barrier.
- **Visual/editor behavior:** potentially more polished, but editor deletion or a serialization mismatch can separate graphics from collision.
- **Colonies/footprints:** must satisfy both terrain and actor exclusions.
- **Procedural suitability:** higher complexity and runtime cost than homogeneous Water without a first-slice gameplay benefit.
- **Transition/determinism:** requires atomic paired placement, paired repair, and paired validation.
- **Verdict:** **rejected for the first slice**.

### 4.6 Height discontinuities

- **Mechanism/pathfinder behavior:** the current native validator treats neighbor height differences greater than one as non-traversable, matching the pinned movement constraint.
- **Visual/editor behavior:** the selected homogeneous templates are height zero. No audited cliff/ramp materialization and visual transition set exists for generated NORMAL maps.
- **Colonies/footprints:** building placement and exit validity on height boundaries would require additional rules.
- **Procedural suitability/determinism:** not safe until height, ramps, rendering, and pathing are contracted together.
- **Verdict:** **forbidden in Version 2; every generated native height remains zero**.

## 5. Semantic terrain and topology model

Graphics do not define generator intent. Each logical cell has exactly one base semantic value and zero or more metadata flags.

### 5.1 Base semantics

| Semantic | Passability meaning | Materialized map data | Diagnostic lifetime |
| --- | --- | --- | --- |
| `OPEN` | Traversable by `unit`; normally traversable by `wasp`. | One allow-listed homogeneous Clear template. | Retained in logical hashes and layer dumps. |
| `BLOCKED` | Impassable to `unit`; bypassable by `wasp` in Version 2. | One allow-listed homogeneous Water template. | Retained with obstacle-region ID and materialization evidence. |

`OPEN` and `BLOCKED` are mutually exclusive and exhaustive inside playable bounds. They are gameplay meanings, not aliases for template IDs.

### 5.2 Reservation and role flags

| Flag | Meaning | May overlap | Materialization effect |
| --- | --- | --- | --- |
| `RESERVED_ROUTE` | Native corridor promised by a named strategic graph edge. | `OPEN`, another route only in an endpoint region, and `CHOKE_SEGMENT`. | Forces `OPEN`; carries route ID and required width class. |
| `START_CLEARANCE` | Terrain apron around an `mpspawn` and every possible runtime starting colony footprint. | `OPEN`, routes, strategic regions, colony clearance. | Forces `OPEN`. |
| `COLONY_CLEARANCE` | Terrain apron around a known neutral colony footprint and exit cells. | `OPEN`, routes, strategic regions, start clearance. | Forces `OPEN`. |
| `STRATEGIC_REGION` | Open node/room where routes may meet or a contest objective is placed. | `OPEN`, routes, clearances. | Forces `OPEN`; carries region/node ID. |
| `CHOKE_SEGMENT` | A measured low-width segment belonging to one named route and choke role. | `OPEN` and exactly one reserved route. | Narrows the route reservation; it never makes its own passable cells Blocked. |

Every `BLOCKED` logical cell carries an `ObstacleRegionId`. Every route and choke cell carries a stable ID. Flags, IDs, intended widths, repair provenance, and pre/post-materialization hashes survive into JSON diagnostics even though reservation flags do not need to be serialized into `map.bin`.

### 5.3 Precedence and overlap

1. Start and colony footprint legality is established first.
2. `START_CLEARANCE`, `COLONY_CLEARANCE`, `STRATEGIC_REGION`, and the final-width `RESERVED_ROUTE` masks force the base semantic to `OPEN`.
3. Obstacle regions may occupy only remaining eligible cells.
4. A choke changes the width of its route mask before obstacle placement; it does not permit a Blocked cell to overlap a reserved route.
5. Any remaining `OPEN`/`BLOCKED` conflict is a generator defect and causes rejection, not precedence-based silent repair.

## 6. Supported mover contract

### 6.1 Standard ground class: `unit`

- **Representatives:** all standard ant, beetle, scorpion, and spider units plus ground actors inheriting `^CoreUnit` or explicitly selecting `Locomotor: unit`.
- **Footprint:** one native cell. Static actor footprints are overlaid separately from mover size.
- **Passability:** Clear, Rock, and Vegetation are traversable; Water and Air are not. Version 2 materializes only Clear and Water at height zero.
- **Turning:** no orientation-dependent footprint exists. Eight-neighbor native connectivity and the Phase 4A clearance field are sufficient for static turning clearance.
- **Swarm capacity:** binary one-cell connectivity is necessary but insufficient. Named normal and major routes must meet the effective-width thresholds in section 8.
- **Colony egress:** every resolved `ExitCell` for a potential starting colony, and the actual `ExitCell` for each neutral colony actor, must connect through the colony apron to a normal-width route.

This is the authoritative topology class and the most restrictive terrain class in Version 2.

### 6.2 Wasp class: `wasp`

- **Representatives:** wasp units and actors explicitly selecting `Locomotor: wasp`.
- **Footprint/layer:** one native cell with access to the custom Air movement layer.
- **Passability:** all current NORMAL terrain categories are speed 100; transition cost is zero; the configured empty transition-terrain set permits transition on any terrain.
- **Topology promise:** starting and neutral colony exits must be in bounds and must have legal access/landing cells. No Water layout may make a required actor invalid. Ground route width, ground route cuts, and ground chokepoint roles are reported as intentionally bypassable by wasps and are not misreported as wasp constraints.
- **Drift guard:** Phase 4C must reject Version 2 generation if the resolved wasp locomotor ceases to permit Water/Air or if its transition contract changes without a contract revision.

Version 2 therefore supports both gameplay movement classes, validates the restrictive `unit` class for terrain topology, and separately validates wasp map legality. It does not promise cross-faction equality of route defensibility; flight bypass is existing game balance, not an RMG defect to hide.

## 7. First materialization contract

### 7.1 Profile and granularity

- Target profile ID: `normal-water-blocking-v2`.
- Generator/configuration version: 2.
- Tileset: `NORMAL` only.
- Playable/storage geometry: unchanged from Version 1 (128x128 native playable, 64x64 logical, cordon width 2).
- Placement unit: one complete 2x2 logical macro-cell.
- Height: zero for every frame.
- No terrain template may straddle logical-cell boundaries.

### 7.2 Allow-list

| Semantic | Allowed NORMAL template IDs | Required native frames |
| --- | --- | --- |
| `OPEN` | `32, 33, 34, 35, 36` | Frames 0-3 must all resolve to `Clear`. |
| `BLOCKED` | `9, 12, 26, 28, 29` | Frames 0-3 must all resolve to `Water`. |

These are the homogeneous `PickAny: true` subsets. The generator, not the editor, chooses among them with named deterministic streams such as `terrain-open-v2` and `terrain-blocked-v2`. Transform partners receive the same semantic and template variant. Version 1 Clear IDs 61 and 62 remain legal only in Version 1; their Rock editor category makes them needlessly ambiguous for a new Clear/Water visual contract even though their frames resolve to Clear.

No actor type is allow-listed as a blocking materialization.

### 7.3 Shoreline and transition rule

The known mixed Clear/Water IDs `0, 2, 3, 5, 16, 18, 19, 21` are **forbidden** in the first Version 2 slice. They encode only one-Clear/three-Water corner masks, with two visual variants per corner. They do not provide the full set of straight, concave, convex, and diagonal masks required for arbitrary region boundaries.

Consequently:

- every logical OPEN cell is four native Clear cells;
- every logical BLOCKED cell is four native Water cells;
- an OPEN/BLOCKED boundary is a hard 2x2-aligned visual seam;
- the report records `shoreline_mode: homogeneous-hard-seam-v1` and `visual_shoreline_complete: false`;
- editor `PickAny` or `MapCreated` behavior must not be invoked as a post-process;
- Phase 4C acceptance is based on gameplay topology and explicit acknowledgment of the seam, not a claim of final shoreline polish.

Adding mixed transitions later requires a separate adjacency table, native passability proof for every mask/orientation, golden visual samples, and proof that the same semantic seed retains its strategic graph and required capacities.

### 7.4 Passability equivalence

Before and after save/reload:

- each logical OPEN cell must materialize as four `unit`-passable native cells;
- each logical BLOCKED cell must materialize as four `unit`-impassable native cells;
- each logical BLOCKED cell must remain wasp-traversable under the current rules, as an expected bypass rather than an error;
- no template fallback or missing frame may silently resolve to Clear;
- static actor obstruction is validated as a separate overlay and may not be folded into the terrain semantic hash.

## 8. Route and clearance contract

### 8.1 Frozen numeric thresholds

| Requirement | Nominal geometry | Hard effective-width/distance rule |
| --- | --- | --- |
| Normal strategic route | 3 logical cells / 6 native cells | Effective native width at least **5**. |
| Major route | 5 logical cells / 10 native cells | Effective native width at least **9**. |
| First-slice chokepoint | 2 logical cells / 4 native cells | Effective native width exactly **3**. Minimum and maximum are both 3 for Version 2. |
| Start-region clear radius | Native-cell square centered at `mpspawn` | Chebyshev radius **12** is OPEN. |
| Starting-colony blocker buffer | Union of exact resolved coverage for all five possible starting colonies | Blocked-to-coverage distance at least **7**, leaving at least 6 intervening native cells. |
| Neutral-colony clear radius | Native-cell square centered at actor anchor | Chebyshev radius **8** is OPEN. |
| Neutral-colony blocker buffer | Exact coverage of the selected colony actor | Blocked-to-coverage distance at least **5**, leaving at least 4 intervening native cells. |
| Start exits | Strategic and local | At least **2** route exits and at least **6 of 8** radius-12 sectors. |

The actual reserved clear mask is the union of the anchor-radius square, the exact footprint buffer, all resolved production `ExitCell` positions, and the route connection. The stronger condition wins where masks overlap.

### 8.2 Corridor conformance

Each strategic graph edge owns a native corridor mask derived before obstacle generation. Validation of that edge is restricted to:

1. its own corridor mask;
2. the source and target strategic-region masks; and
3. another route's mask only inside those endpoint regions.

A global path outside this set does not prove that the named edge was materialized. Except within a shared start clearance or strategic endpoint region, two exit routes from the same start may not share native cells. Removing either route outside the shared endpoint masks must leave the other route from the start to the common strategic network.

All configured colony `ExitCell` values must be native-passable after exact actor occupancy is applied and must reach the common ground component through a path whose bottleneck is at least normal effective width 5 once it leaves the colony apron.

## 9. Chokepoint contract

A chokepoint is an intentional, identified restriction on a named strategic route. Two obstacle blobs that happen to approach one another are not a chokepoint unless all rules below pass.

### 9.1 Roles

- `ROUTE_CONSTRICTION`: narrows one route while a strategically meaningful alternate route remains. This is the only role authorized in Phase 4C.
- `REGION_GATE`: a cut whose removal can isolate a strategic region. The role is defined for diagnostics but **not authorized** in the first implementation slice because it needs stronger articulation and faction-balance rules.

### 9.2 Measurable first-slice rules

Every `ROUTE_CONSTRICTION` must:

- belong to exactly one `RouteId` and one `ChokeId`;
- have effective width exactly 3, produced by a nominal 4-native-cell macro-aligned aperture;
- sustain that width for at least **4** and at most **12** native cells along the route centerline;
- have a normal-width shoulder (effective width at least 5) for at least **6** native centerline cells at both ends;
- lie between the edge's source and target endpoint regions and form a cut within that edge's restricted corridor mask;
- be at Chebyshev distance at least **24** native cells from every `mpspawn` anchor;
- be at cell-set distance at least **10** native cells from every starting or neutral colony coverage set;
- be at cell-set distance at least **16** native cells from any other chokepoint segment;
- retain an alternate start-to-strategic-network route outside the constricted route;
- have no overlap with start clearance, colony clearance, or strategic-region interiors; and
- have a symmetry-equivalent choke with the same role, width, length, route importance, and distance class.

At most one chokepoint may lie on a strategic route. Counts are expressed as symmetry orbits and as materialized segments; both values are reported. Phase 4C's `central-contest + mixed` combination requires exactly one chokepoint orbit. `open + mixed` requires zero intentional chokepoints and rejects accidental effective-width-3 segments on required routes.

## 10. Obstacle-region contract

### 10.1 Region identity and geometry

- An obstacle region is a four-neighbor-connected set of logical BLOCKED cells with one stable `ObstacleRegionId`.
- Region size is at least **8** and at most **64** logical cells (32-256 native Water cells).
- Two distinct regions must have cell-set Chebyshev distance at least **3 logical cells**, leaving two complete logical cells between them. If this is not true, they merge before IDs and size validation.
- A region and every symmetry image are generated as one orbit. If images touch or overlap at a symmetry axis, they merge into one self-symmetric region and must still respect the maximum size.
- The outermost **1 logical-cell** ring of playable bounds is always OPEN; Water regions may not touch the map edge or cordon.
- Regions may not overlap any route, start-clearance, colony-clearance, strategic-region, actor-footprint, or actor-exit reservation.
- Region IDs and cells are canonicalized by topmost/leftmost logical coordinate before hashing and serialization diagnostics.

### 10.2 Density and archetype behavior

Density is `BLOCKED logical cells / 4096 playable logical cells`, measured after repair.

| Archetype with `mixed` topology | Target | Accepted range | Chokepoint behavior |
| --- | --- | --- | --- |
| `open` | 12% | **10-14%** | No intentional or accidental required-route chokepoint. Major routes are preferred between starts and the shared network. |
| `central-contest` | 16% | **14-18%** | Exactly one symmetry orbit of `ROUTE_CONSTRICTION`; normal routes elsewhere. |

For both archetypes, all hub strategic regions are OPEN through Chebyshev radius **6 logical cells**. For `central-contest`, every central-contest colony's full clearance mask is added to this open region before obstacles. Density is redistributed among remaining eligible cells; reservations are never shrunk to meet density.

The terrain-only OPEN native mask must be one connected ground component. Obstacle generation may not create decorative Clear islands or inaccessible pockets. Static colony footprints are then overlaid and exact start/colony access is validated again.

## 11. Bounded repair and retry policy

Generation is topology-first: graph, endpoint regions, corridor masks, clearances, choke intent, obstacle regions, native materialization, validation, then bounded repair. Noise or cellular smoothing may shape only cells that remain eligible; neither may alter a reservation or define strategic connectivity.

### 11.1 Allowed repairs, in order

1. Clear BLOCKED logical cells from an intended route until its required width is restored.
2. Clear BLOCKED logical cells from a start or starting-colony buffer.
3. Clear BLOCKED logical cells from a neutral-colony buffer or configured production-exit path.
4. Reconnect a failed named strategic edge with a deterministic constrained corridor inside its declared routing envelope.
5. Trim the edge of an obstacle region to restore region separation, density, or an authorized choke shoulder.

Repairs are subtractive only: `BLOCKED -> OPEN`. A repair may not add Water, move a start/colony, move a graph node, change a choke role, or invent a new graph edge. Each repair is applied to the full symmetry orbit.

### 11.2 Budgets

- Maximum repair operations: **8 per map**.
- Maximum changed cells: **64 logical cells total**, counting every symmetry image.
- Maximum deterministic topology attempts after an unrepairable rejection: **4**.
- Attempt streams are derived from the canonical settings and stable labels `topology-attempt-0` through `topology-attempt-3`; a retry never consumes an implicit global RNG sequence.

### 11.3 Regenerate instead of repair

The current attempt is rejected without repair when:

- a selected template/frame does not resolve to the contracted terrain;
- a logical/materialized passability mismatch occurs;
- symmetry, actor legality, or save/reload fails;
- a start, actor footprint, or production exit is out of bounds;
- the strategic graph itself lacks two required exits;
- a region exceeds the maximum size by more than the entire remaining cell budget;
- a required `ROUTE_CONSTRICTION` has no alternate strategic route;
- repair would remove the requested choke, shrink a strategic region, or change the requested archetype;
- the repair operation or changed-cell budget would be exceeded; or
- post-repair density remains outside the archetype range.

After four failed attempts the map generation command fails with all attempt summaries; it must not emit the least-bad map.

### 11.4 Required repair diagnostics

For every operation record: attempt number, repair index/type, triggering validation code, target route/region/actor ID, cells before symmetry, full orbit cells, before/after semantic values, cumulative changed-cell count, and post-repair validation result. Reports also record rejected-attempt seeds/stream labels without exposing mutable RNG state.

## 12. Phase 4C hard-validation contract

Phase 4C must reject a map on any failure below. The authoritative checks run on the reloaded package with resolved rules and actor definitions.

### 12.1 Profile, materialization, and package

- Profile ID, generator/configuration version, NORMAL tileset, dimensions, bounds, and cordon match Version 2.
- Every logical cell has exactly one base semantic and legal metadata overlaps.
- Only allow-listed templates occur; every frame resolves to the required terrain and height zero.
- Logical OPEN/BLOCKED passability is exactly equivalent to the reloaded native `unit` terrain mask.
- Every planned actor exists, has a legal owner/location, and has the expected exact resolved footprint.
- Player, spawn, and neutral-colony counts satisfy the existing skirmish contract.
- Engine map lint has zero errors; save/reload metadata, canonical content hash, and engine UID checks complete.
- Same canonical settings produce the same logical, graph, actor, repair, template, `map.yaml`/`map.bin`, and package-validation hashes on repeated runs.

### 12.2 Ground movement and topology

- All starts have access to one common `unit` component after the union of all possible starting-colony footprints is overlaid.
- Every neutral colony has at least one access cell in that component, and its actual production exit reaches it.
- Every possible starting colony production exit is legal and reaches its start apron.
- The terrain-only OPEN mask is one component; no inaccessible Clear island exists.
- Every named graph edge traverses inside its own corridor/endpoints and meets its width class. A global detour cannot satisfy the edge.
- Every start has at least two independent route exits and at least 6/8 radius-12 passable exit sectors.
- Normal, major, and choke widths meet section 8 under the effective-width metric; nominal apertures are also verified.
- Blocked cells do not overlap any prohibited reservation.
- Obstacle region size, separation, edge ring, density, merge, and central-region rules pass.
- Every declared choke passes role, width, length, shoulder, distance, separation, alternate-route, route-count, and symmetry checks.
- Any undeclared effective-width-3 segment on a required route is an accidental choke and causes rejection.
- Removing either of a start's two routes outside shared endpoint regions leaves the other route to the common strategic network.

### 12.3 Wasp legality and rule drift

- The resolved `wasp` locomotor still allows Clear, Rock, Vegetation, Water, and Air at the contracted speed and retains legal transition semantics.
- Wasp starting/neutral colony production exits are in bounds, not occupied illegally, and have at least one legal landing/takeoff access cell.
- Diagnostics explicitly report that all Water obstacle regions are bypassable by wasps; they may not label a ground choke as a universal mover choke.

### 12.4 Symmetry, repair, and diagnostics

- Base semantics, reservation masks, region/route/choke roles, template variants, actors, widths, and repairs are symmetry-equivalent.
- Repair operations and changed cells stay within both budgets and preserve the archetype.
- The report contains pre/post-repair metrics, nominal/effective widths, component and route identities, density, choke orbit/materialized counts, mover-specific results, shoreline status, and rejection details.
- The legacy proxy may remain as a comparison metric but cannot override a native failure.

Interactive F5/Ctrl+F5 play remains the final live-World smoke test because the pinned engine does not expose a constructible `World` to the utility assembly. It complements rather than replaces the hard static/package gate.

## 13. CLI and preset contract

The first slice exposes one semantic control in the PowerShell wrapper and corresponding utility command:

```text
TopologyPreset = off | mixed
```

The wrapper spelling should be `-Topology off|mixed`. The default is `off` until Version 2 clears its implementation gate.

| Preset | BlockingTopologyEnabled | ObstacleDensity | ChokepointFrequency | RouteOpenness | Version/profile |
| --- | --- | --- | --- | --- | --- |
| `off` | false | none (0%) | none | existing Clear-only behavior | Version 1 / `normal-clear-v1` |
| `mixed` + `open` archetype | true | moderate (target 12%, range 10-14%) | none | open (major start routes) | Version 2 / `normal-water-blocking-v2` |
| `mixed` + `central-contest` | true | moderate (target 16%, range 14-18%) | low (exactly one orbit) | balanced (normal routes plus one constriction orbit) | Version 2 / `normal-water-blocking-v2` |

`BlockingTopologyEnabled`, `ObstacleDensity`, `ChokepointFrequency`, and `RouteOpenness` are canonical resolved report fields, not four independent public tuning knobs in the first slice. Future `open` or `constrained` topology presets are reserved and must be rejected until separately contracted; they may not silently alias `mixed`.

The CLI must not expose noise frequency, blob count, smoothing passes, corridor-carving cost, repair budget, or attempt count. Existing seed, player, symmetry, archetype, colony-count, output, and movement-validation controls remain.

## 14. Versioning and configuration decision

No Version 2 YAML profile is added during Phase 4B. The current `RmgProfile` loader intentionally rejects anything except `normal-clear-v1`; committing an unread configuration would falsely imply runtime support and could drift before Phase 4C.

Phase 4C must add the Version 2 model fields, loader validation, `normal-water-blocking-v2` profile, generator behavior, and report schema atomically. The normative values in this document are the source for that implementation. Version 2's canonical setting string must include the topology preset and every resolved behavior that can change output.

## 15. Explicit non-goals

Version 2 does not include:

- polished or automatic shoreline transitions;
- Rock/Vegetation slow-terrain generation;
- blocker actors, invisible walls, or decorative collision walls;
- height, cliffs, ramps, bridges, islands, naval movement, or multiple tilesets;
- a `REGION_GATE`, fortress, maze, or high-obstacle preset;
- changes to unit, colony, locomotor, or wasp balance;
- lobby/editor UI;
- dynamic live-World pathfinding simulation in the utility;
- raw noise or repair tuning in the user CLI; or
- copying reference-map terrain or copyrighted assets into the repository.

## 16. Known risks and follow-up obligations

1. **Visual shoreline quality:** hard macro shores will look incomplete. This is accepted only for the technical first slice and must be visible in reports and playtest handoff.
2. **Wasp strategic bypass:** Water chokepoints are faction-asymmetric by design. Phase 4C must expose ground and wasp metrics separately; later gameplay review may decide whether an archetype is appropriate for competitive faction selection.
3. **Swarm throughput:** effective width 5 is a calibrated lower bound, not proof of ideal large-army flow. Manual tests must compare normal routes, the nominal-4/effective-3 choke, and colony egress under actual unit groups.
4. **Diagonal movement:** all hard widths use the same eight-neighbor/Chebyshev model as Phase 4A. If pinned engine corner-cut behavior is found to differ in live play, the validator must be corrected and this contract revised before release.
5. **Transition expansion:** the current one-Clear-corner template set is incomplete for arbitrary coastlines. It must not be expanded by guesswork or by treating visual category as passability.
6. **Reference calibration breadth:** one corpus supports open, moderate, and choke regimes but not a distinct maze family. Density above 18% and `REGION_GATE` remain unfrozen.
7. **Static validation boundary:** actor dynamics, temporary blockers, orders, and live traffic are outside the utility gate and require bounded interactive playtests.

## 17. Phase 4C entry criteria

- [x] Blocking semantics are defined independently from graphics.
- [x] Supported mover classes and the authoritative topology class are frozen.
- [x] A real blocking materialization strategy is selected.
- [x] The template allow-list is explicit; no blocking actor is allowed.
- [x] First-slice transition behavior and accepted visual seams are explicit.
- [x] Route, major-route, and chokepoint widths are numeric and use defined units.
- [x] Start and colony clearances and blocker distances are numeric.
- [x] Chokepoint roles, measurements, distances, alternates, and count limits are frozen.
- [x] Obstacle-region size, density, separation, symmetry, edge, merge, and archetype rules are frozen.
- [x] Repair operations, budgets, retry limits, and regeneration conditions are frozen.
- [x] Ground, wasp, materialization, package, determinism, and symmetry hard gates are frozen.
- [x] The first semantic CLI preset and resolved settings are defined.
- [x] No editor or lobby UI dependency exists.

**CONTRACT FROZEN — READY FOR BLOCKING IMPLEMENTATION**
