# Phase 8B Water Morphology and Layout-Algorithm Audit

Status: audit complete; implementation direction proposed
Date: 2026-08-29
Generator behavior changed by this phase: no

> **Post-implementation naming correction (2026-08-30):** Phase 8C visual review showed that the coherent-Water V8 implementation still uses a route-first, exact-symmetry competitive scaffold. It is therefore **Structured Competitive**, not Natural Landscape. Frozen V7 is **Artificial Battlefield**. Natural Landscape remains planned and unimplemented. The original taxonomy discussion below is retained as audit history, but its proposed version-to-family assignment is superseded.

## Purpose

Phase 8A made Water Amount functional and corrected the earlier edge bias. Live review then exposed a different problem: High Water reaches an appropriate total percentage, but usually presents as many tactically distributed puddles rather than a few lakes, rivers, coasts, or dominant basins.

This audit answers four questions:

1. How are Water bodies shaped and distributed in shipped campaign, scenario, and skirmish maps?
2. How does Generator Version 7 compare with those authored references?
3. Which established procedural-generation approaches fit OpenSA's deterministic RTS constraints?
4. Which map-layout families must become first-class generator concepts?

The three supplied external examples are treated as visual intent only. They illustrate correlated, irregular land/water masses and are not copied or used as source assets.

## Scope

The read-only corpus contains every shipped System map that the Phase 7A inventory could load, plus a generated comparison set:

| Category | Maps audited | Water-bearing maps |
| --- | ---: | ---: |
| Campaign | 100 | 93 |
| Custom/challenge scenario | 13 | 12 |
| Official skirmish reference | 11 | 11 |
| Other shipped map | 1 | 1 |
| Generator Version 7 comparison | 14 | 14 |
| **Total** | **139** | **131** |

The generated set contains the Phase 8A manual profiles and the reported High-Water seeds `16542743543062672902`, `1997304676944854472`, `2420437712743368630`, and `2903549930824399365`. Campaign and custom scenarios are included because their static terrain remains valuable morphology evidence even when scripted mission traffic cannot be inferred from map data alone.

## Method

Water is read from each packaged map's native terrain semantics. Bodies are four-neighbor connected components. The audit records:

- total Water as a percentage of the playable map;
- body count and count above a meaningful-size threshold;
- component area and share of total Water;
- perimeter and compactness, `4 * pi * area / perimeter^2`;
- bounding-box width and height;
- edge-band and central-half occupancy;
- the largest-body and top-three-body shares; and
- the share of Water contained in very small bodies.

A meaningful body contains at least four native cells and at least 0.5 percent of the playable map. This is a descriptive threshold, not a universal definition of a lake. Compactness is likewise descriptive: it separates compact blobs from long rivers or coastlines but does not score either form as inherently better.

The comparison deliberately avoids a single synthetic "naturalness" score. Natural landscapes can contain one coast, several lakes, a river network, or many wetlands; artificial maps can intentionally use repeated pools. Component statistics are evidence for classification and regression, not a substitute for visual and gameplay review.

## Results

### Category medians

Medians below are calculated over Water-bearing maps only.

| Category | Bodies | Meaningful bodies | Largest body share of Water | Largest body share of map | Largest compactness | Small-body share of Water |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Campaign | 5 | 4 | 56.14% | 12.45% | 0.299 | 0.55% |
| Custom/challenge | 7.5 | 5 | 43.72% | 10.12% | 0.173 | 0.43% |
| Official skirmish | 4 | 3 | 63.31% | 7.92% | 0.315 | 1.42% |
| Generator Version 7 | 24 | 15 | 7.01% | 1.07% | 0.501 | 20.54% |

The difference is structural, not subjective. Compared with the official-skirmish median, Version 7 has six times as many Water bodies, its dominant body contains roughly one ninth as much of the map's Water, and about fifteen times as much Water is consumed by very small bodies. It therefore can satisfy High Water numerically while still reading as a field of puddles.

Quartiles reinforce the result:

| Category | Body-count p25 / p50 / p75 | Largest-body share p25 / p50 / p75 | Small-body share p25 / p50 / p75 |
| --- | --- | --- | --- |
| Campaign | 3 / 5 / 8 | 39.6 / 56.1 / 80.5% | 0.0 / 0.5 / 2.2% |
| Custom/challenge | 4.25 / 7.5 / 15.5 | 31.6 / 43.7 / 77.8% | 0.0 / 0.4 / 1.9% |
| Official skirmish | 2.5 / 4 / 7 | 37.6 / 63.3 / 72.3% | 0.0 / 1.4 / 11.5% |
| Generator Version 7 | 20 / 24 / 29.5 | 5.9 / 7.0 / 10.0% | 14.5 / 20.5 / 42.1% |

### Reported High-Water seeds

| Seed | Total Water | Bodies | Meaningful | Largest share of Water | Largest share of map | Top-three share | Small-body share |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `16542743543062672902` | 17.09% | 28 | 16 | 6.64% | 1.14% | 19.89% | 19.07% |
| `1997304676944854472` | 17.19% | 30 | 18 | 6.25% | 1.07% | 17.72% | 13.92% |
| `2420437712743368630` | 18.41% | 30 | 20 | 5.77% | 1.06% | 17.21% | 17.18% |
| `2903549930824399365` | 18.79% | 28 | 22 | 6.04% | 1.14% | 17.64% | 8.84% |

These seeds confirm the user's observation. Water Amount is functioning as an area budget, but the current topology is not functioning as a lake-size or morphology control.

### Authored examples

The shipped maps also demonstrate that no single component profile should be imposed on every layout:

- `Suprise` uses two bodies, with one holding 99.7 percent of its Water.
- `Team Brawl` uses one body containing all Water.
- `Swamp Battleground` uses eight bodies, with the largest holding 63.3 percent.
- `Narrow Passage` uses five long bodies and 38.8 percent total Water.
- campaign maps such as `Coastal Battle`, `River Cruise`, `Island Paradise`, and `Vertical Islands` use one dominant connected body.
- `Memories of Blue` is a legitimate multi-basin exception: ten bodies and a 12.1-percent largest-body share.
- artificial custom layouts such as `Maze`, `Puck-Man`, and `Candy Siege` intentionally use many repeated or geometric Water features.

The correct conclusion is therefore not "all maps need one lake." The generator needs morphology selected by layout intent.

## Why Version 7 produces puddles

Version 7 is a direct, constraint-first constructive generator. Its current rules actively favor fragmentation:

1. High Water requests six independent symmetry orbits before general filling.
2. Interior seed selection prioritizes previously uncovered cells in a 4 x 4 sector grid.
3. General fill visits those sectors round-robin, spreading additions across the playfield.
4. Initial High-Water bodies are normally capped at 32 logical cells for `central-contest` or 48 for `open`; subsequent fill bodies are normally capped at 16 cells.
5. Every final body must remain within the configured 8-64 logical-cell range.
6. Distinct bodies must preserve two complete open cells between them, so nearby bodies cannot coalesce.
7. Starts, colonies, strategic regions, routes, chokepoints, connectivity, and combat clearances have priority over Water.

Those rules were sensible for correcting edge-only Water while maintaining strong safety. Together, however, they encode "many separated tactical pools." Increasing the percentage adds more pools because the algorithm has no representation of a dominant lake, coast, river, or basin hierarchy.

This also explains why the current appearance is not merely a consequence of the existing `open` and `central-contest` battlefield plans. Their reservations constrain candidate space, but the fragmentation is produced directly by Water seeding, size caps, sector coverage, and mandatory inter-body separation.

## Comparison with established procedural approaches

| Approach | What it contributes | Main limitation for OpenSA |
| --- | --- | --- |
| Correlated scalar fields, including gradient/fractal noise | Smooth multiscale variation; thresholding naturally creates irregular connected masses | Does not guarantee fair starts, usable routes, connectivity, or exact symmetry |
| Cellular automata | Cheap local smoothing and self-organization; joins noise into organic caves, islands, and basins | Topology is difficult to predict and can close essential RTS routes |
| Hydrology or erosion simulation | Causally plausible drainage, rivers, valleys, and lakes | More expensive and complex than needed for a flat 2D RTS; awkward under strict symmetry |
| Grammar or graph-driven construction | Encodes intentional routes, arenas, canals, rings, and encounter roles | Produces gameplay structure, not natural surface morphology by itself |
| Search-based or constraint-guided generation | Selects or repairs candidates against fairness and playability objectives | Needs a candidate representation and meaningful fitness metrics; bounded runtime is essential |
| Current Version 7 direct construction | Deterministic, inspectable, symmetric, and strong at reservations and hard safety | Its local bounded regions and distribution requirements expose the construction pattern |

Perlin's image-synthesis work formalized stochastic nonlinear functions for naturalistic texture and shape variation ([SIGGRAPH 1985](https://doi.org/10.1145/325334.325247)). Fournier, Fussell, and Carpenter demonstrated stochastic subdivision and fractional-Brownian terrain models with controllable irregularity ([CACM 1982](https://doi.org/10.1145/358523.358553)). These field-first methods are well suited to candidate morphology, not to enforcing an RTS contract alone.

Johnson, Yannakakis, and Togelius show that cellular automata can self-organize noisy grids into organic cave structures in real time ([PCG Workshop 2010](https://pure.itu.dk/en/publications/cellular-automata-for-real-time-generation-of-infinite-cave-level/)). Kelley, Malin, and Nielson model terrain through stream erosion, demonstrating why drainage simulation produces different structure from independent blobs ([SIGGRAPH 1988](https://doi.org/10.1145/54852.378519)). Both approaches are relevant inspirations, but neither should replace OpenSA's route and combat validation.

Search-based PCG explicitly treats generation as optimization against quality criteria ([IEEE TCIAIG 2011](https://doi.org/10.1109/TCIAIG.2011.2148116)), while graph-grammar work demonstrates designer-constrained procedural level structure ([AIIDE 2013](https://doi.org/10.1609/aiide.v9i3.12592)). These support a hybrid architecture: generate morphology candidates, project hard gameplay constraints, then validate and select within a bounded deterministic search.

## Required layout taxonomy

`Terrain` or visual theme must not decide spatial topology. NORMAL, desert, swamp, candy, or any future tileset describes material and artwork. A first-class **Layout Family** must describe spatial design.

### 1. Natural Landscape

Intent: authored-looking coasts, lakes, rivers, wetlands, and irregular terrain fields.

- a small number of dominant, correlated Water masses;
- multiscale boundaries rather than repeated similarly sized blobs;
- optional secondary ponds and islands, controlled as a minority of the Water budget;
- gameplay reservations embedded into or carved from the candidate field;
- organic Rock and Vegetation regions using the same broad intent field, not independent noise at every cell.

Exact geometric symmetry can use organic shapes and still look substantially better than Version 7, but full-map mirroring may remain perceptible. The first implementation should retain exact symmetry. Relaxing it to semantic or statistical fairness requires stronger route-cost, territory-value, and resource-access parity metrics and should be a separate later contract.

### 2. Structured Competitive

Intent: the current generator's readable, symmetric tactical battlefield.

- distributed Water and terrain features;
- explicit open lanes, flanks, central contests, and protected starts;
- high transparency and exact symmetry;
- the existing Version 7 result preserved as a valid style, not treated as failed natural generation.

The current `open` and `central-contest` values are better understood as **Battlefield Plans** within a layout family, not as the complete layout-family taxonomy.

### 3. Artificial Battlefield

Intent: visibly constructed arenas and engineered defenses.

- canals, moats, rings, basins, walls, grids, corridors, and repeated geometric motifs;
- strongest visible symmetry and plan-driven chokepoints;
- topology produced by geometric primitives or a graph/shape grammar;
- custom-map precedents such as mazes and repeated pools accepted as intentional rather than judged against natural morphology.

## Recommended hybrid architecture

The next implementation should not replace Version 7 with raw noise. It should separate spatial intent, candidate morphology, and safety:

1. **Battlefield plan:** place starts, neutral-colony intent, contest zones, route graph, flank roles, and protected combat/production areas.
2. **Layout-family candidate:**
   - Natural uses a seeded multiscale scalar field, a small number of body attractors, and limited cellular smoothing/component selection.
   - Structured uses the current symmetric region-growth and sector-distribution method.
   - Artificial uses explicit geometric primitives or a constrained shape grammar.
3. **Constraint projection:** remove Water from protected cells, preserve required route widths, reconcile symmetry, and reconnect land where required.
4. **Component shaping:** grow or merge intended bodies, remove accidental micro-puddles, and allocate the remaining Water budget to secondary features according to the family.
5. **Materialization:** apply the accepted shoreline catalogue and open-Water details without changing semantic passability.
6. **Bounded validation/search:** run existing native movement, combat-space, colony, symmetry, and density gates plus the new morphology descriptors; retry deterministically with a strict attempt limit.

This keeps the proven OpenSA safety system as the final authority while allowing the candidate generator to produce forms that direct placement cannot express well.

## Proposed implementation gate

This audit originally proposed Natural Landscape Water for the next slice. Phase 8C implemented coherent Water but visual review rejected that classification. The delivered V8 path is Structured Competitive; V7 is Artificial Battlefield; Natural Landscape requires a separate terrain-first contract and remains planned.

For the 128 x 128 coherent-Water comparison corpus—now classified as Structured Competitive—the provisional batch-level targets were:

- High Water still meets the existing total and interior-area requirements;
- median body count no greater than eight;
- median largest-body share at least 40 percent of Water;
- median small-body share no greater than five percent;
- at least one meaningful body intersects the playable interior on every Standard/High map;
- all current native movement, combat-space, colony, shoreline, determinism, and packaging gates remain hard requirements; and
- visual review includes two-player and four-player maps under each supported exact symmetry.

These are calibration bands derived from the authored corpus, not hard per-map laws. Rivers, archipelagos, wetlands, and intentionally artificial plans need morphology-specific criteria rather than one global threshold.

Recommended settings model for the next schema revision:

- `LayoutFamily`: `natural`, `structured`, `artificial`;
- `BattlefieldPlan`: initially the existing `open` and `contested-center` values;
- `WaterAmount`: total semantic Water budget only;
- a non-player-facing morphology profile that controls dominant-body share, secondary-body budget, scale, smoothing, and allowed topology; and
- `Terrain`: visual/material theme, kept independent of layout.

## Reproduction

The audit is implemented by `OpenRA.Mods.OpenSA/UtilityCommands/AuditRmgBattlefieldLayoutsCommand.cs`. After building the utility, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-BattlefieldLayoutAudit.ps1 `
    -OutputDirectory artifacts\rmg\phase-8b-water-morphology-audit\corpus `
    -GeneratedMapDirectory artifacts\rmg\phase-8a-parameterized-battlefield\examples `
    -GeneratedMapPattern OpenSA-RMG-*.oramap
```

The ignored output is `artifacts/rmg/phase-8b-water-morphology-audit/corpus/battlefield_layout_audit.json`. Schema version 1.1 contains the complete per-map component measurements and the aggregate category summaries used above.

## Decision

The audit accepts Phase 8A's Water Amount implementation as a quantity control and rejects Version 7 as a Natural Landscape implementation. The original recommendation assigned V7 to Structured Competitive, but Phase 8C visual review superseded that assignment: V7 is **Artificial Battlefield**, V8 coherent Water is **Structured Competitive**, and **Natural Landscape remains unimplemented**. Future generation must still support all three spatially distinct families. Theme remains an independent axis.
