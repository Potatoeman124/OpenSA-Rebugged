# Phase 6A NORMAL land-surface audit and contract

## Status and decision

The Phase 6A audit is complete. The active NORMAL tileset supports a deterministic land-materialization implementation, but Rock and Vegetation cannot be treated as cosmetic decoration. They are passable ground terrain with distinct movement penalties, and Vegetation is visually and semantically nested inside Rock rather than transitioning directly from Clear.

The accepted Generator Version 3 shoreline profile remains the frozen baseline. Any land-detail or slow-terrain implementation must use a new opt-in profile and must not change Versions 1 through 3 output identity.

The recommended implementation sequence is:

1. Phase 6B: Clear-native cosmetic land details only; and
2. Phase 6C: symmetric Rock and Vegetation fields with weighted-route validation.

No audit-data blocker prevents either phase. Phase 6C must add movement-cost validation before it can be accepted; connectivity and corridor width alone are insufficient for terrain that slows units.

## Legal and evidence boundary

The audit used tracked OpenSA and pinned OpenRA source, shipped System-class map metadata and `map.bin` packages, and the user's externally installed original sprites and palette. The audit command writes identifiers, native terrain semantics, actor coordinates, counts, and spatial metrics only.

The local Rock and Vegetation atlases are rendered beneath `artifacts/rmg/phase-6a-land-surface-audit/`, which is ignored. Atlas PNGs, extracted sprites, original map packages, screenshots, and other asset-derived evidence must never be staged or distributed. Durable documentation may retain template identifiers, structural classifications, measured frequencies, and behavioral conclusions.

## Movement authority

The pinned ruleset defines the following `unit` locomotor behavior:

| Terrain | Ground speed | Pathing cost | Ground passable |
| --- | ---: | ---: | --- |
| Clear | 100% | 100 | yes |
| Rock | 75% | 133 | yes |
| Vegetation | 50% | 200 | yes |

Water remains impassable to `unit`. The custom `wasp` locomotor remains independent and may traverse these terrain classes under its existing rules.

Consequences:

- Rock and Vegetation can preserve graph connectivity while still creating a large travel-time disadvantage.
- Exact semantic symmetry is required for gameplay-affecting terrain masks.
- Start-to-hub, start-to-colony, and strategic-route validation must measure weighted path cost in addition to reachability and bottleneck width.
- Cosmetic asymmetry is permitted only when the selected stamp preserves the same native terrain semantics.

## NORMAL land-template architecture

The audit covers 69 templates across 36 shipped NORMAL maps: 28 campaign missions, 4 custom/challenge scenarios, 3 skirmish references, and 1 other System-class map. It is not a skirmish-only audit.

The underlying Phase 1 corpus includes every available shipped System-class map: 100 campaign missions, 13 custom/challenge scenarios, 11 skirmish references, and 1 other map, for 125 total. Phase 7 layout analysis must retain that full scope even when individual terrain-semantic audits select a tileset subset.

### Clear interior bank

Templates `32` through `36` are 2x2 homogeneous Clear `PickAny` interiors. They remain the normal base land surface.

### Clear-to-Rock bank

The Rock category contains 32 templates, IDs `37` through `68`, but category membership does not imply that every native frame is Rock.

| Role | Template IDs | Native semantics |
| --- | --- | --- |
| Clear/Rock transitions | `37-45`, `47-48`, `50-60`, `67-68` | mixed Clear and Rock |
| Rock interiors | `46`, `49`, `63-66` | homogeneous Rock, `PickAny` |
| Clear-native fixed details | `61`, `62` | homogeneous Clear |

The 24 mixed templates cover every single-corner, straight-edge, and three-corner 2x2 Clear/Rock mask with two variants per mask. The local atlas confirms coherent rock-field edges and corners. Templates `61` and `62` depict small stone details on sand but remain Clear for movement; they are safe candidates for a cosmetic-only phase.

### Rock-to-Vegetation bank

The Vegetation category contains 32 templates, IDs `69` through `100`. It transitions between Rock and Vegetation, not between Clear and Vegetation.

| Role | Template IDs | Native semantics |
| --- | --- | --- |
| Rock/Vegetation transitions | `69-77`, `79-80`, `82-92`, `99-100` | mixed Rock and Vegetation |
| Vegetation interiors | `78`, `81`, `95-98` | homogeneous Vegetation, `PickAny` |
| Vegetation-native fixed detail | `93` | homogeneous Vegetation |
| Rock-native fixed detail | `94` | homogeneous Rock |

The 24 mixed templates cover every required edge and corner mask, but variant counts are not uniform: the `Vegetation/Vegetation/Rock/Vegetation` frame pattern has three fixed candidates, while `Rock/Rock/Rock/Vegetation` has one. A future catalogue must therefore select from candidates by semantic mask and visual role; it must not assume two variants per role or derive IDs by a constant offset from the Water or Rock banks.

### Category and appearance are not movement authority

The fixed detail exceptions prove that category-based selection is unsafe:

- Rock-category templates `61` and `62` are entirely Clear.
- Vegetation-category template `94` is entirely Rock.
- Vegetation-category template `93` is entirely Vegetation.

The active template's four native terrain frames, followed by reloaded `Map.GetTerrainInfo`, are authoritative. Visual category, filename bank, and template ID range are classification aids only.

## Authored-map calibration evidence

### Land-relative terrain coverage

Coverage below is measured against non-Water land cells, not total map area.

| Terrain | Maps using terrain | Median | Mean | 90th percentile | Maximum |
| --- | ---: | ---: | ---: | ---: | ---: |
| Rock | 33 of 36 | 13.8055% | 13.8283% | 21.7841% | 30.0755% |
| Vegetation | 33 of 36 | 8.3118% | 8.4143% | 16.3963% | 25.7550% |
| Rock + Vegetation | 33 of 36 | 21.7784% | 22.2427% | 34.0432% | 55.8305% |

`024_Circuit`, `029_Ant_Crazy`, and `033_Conquest_Of_Paradise` contain neither Rock nor Vegetation. Slow terrain is therefore characteristic of NORMAL maps but is not a universal authored-map requirement.

The initial Phase 6C profile should target the corpus medians: approximately 14% final Rock and 8% final Vegetation among non-Water land cells. These are calibration targets, not permission to overwrite protected Clear zones. Placement constraints may lower achieved coverage, but any tolerance or bounded repair must be explicit and reported.

### Homogeneous fixed-detail frequency

| Native surface | Detail templates | Detail stamps | Interior stamps | Authored detail rate |
| --- | --- | ---: | ---: | ---: |
| Clear | `61`, `62` | 2,017 | 44,825 | 4.3060% |
| Rock | `94` | 143 | 6,111 | 2.2865% |
| Vegetation | `93` | 152 | 5,129 | 2.8782% |

Phase 6B should use a four-percent target for Clear-native details `61` and `62`. Phase 6C may use two-percent Rock-native detail and three-percent Vegetation-native detail targets. These rates apply only to homogeneous interior stamps and must use a dedicated cosmetic random stream.

### Connected field sizes

Four-neighbor native-cell components have the following corpus distribution:

| Terrain | Components | Median cells | 90th percentile | Maximum |
| --- | ---: | ---: | ---: | ---: |
| Rock | 171 | 84 | 658 | 3,702 |
| Vegetation | 260 | 4 | 298.3 | 2,711 |

The Vegetation median is strongly influenced by fixed four-cell details and small hand-authored fragments. These component statistics describe authored maps; they must not be copied directly into generator field-size settings. Generated field morphology must be defined on the logical grid and then validated at native resolution.

### Water separation

Only one of 43,893 authored Rock cells is eight-neighbor adjacent to Water, and no authored Vegetation cell is adjacent to Water. The single Rock contact is an isolated exception in `Custom_Mission_Boundary_Line`.

Generated Rock or Vegetation must not be cardinally or diagonally adjacent to Water after native materialization. At least one native Clear boundary must separate slow land from Water. Dead trees, stones, or plant-like imagery observed in open Water belong to Water-semantic detail templates and do not justify placing Rock or Vegetation terrain into Water.

### Spawn and colony clearance

Only four audited maps contain `mpspawn` actors, providing eight spawn anchors. Their minimum measured distance to Rock is seven native cells and to Vegetation is ten. This is useful supporting evidence but too small a sample to define generator safety.

Across 364 authored colony anchors, the median nearest distance is nine cells for Rock and eighteen for Vegetation. Five authored colonies overlap Rock at their anchor; Vegetation never overlaps a colony anchor and has a minimum distance of one. These hand-authored exceptions do not relax the generator contract.

The existing starting-colony footprints, production exits, combat-space envelopes, colony-access cells, and reserved strategic corridors remain authoritative protected Clear zones. Phase 6C must not place Rock or Vegetation inside them.

### Canonical stamps and authored exceptions

The audit found 67,683 canonical land stamps, including 11,618 fixed transition/detail stamps. It also found 364 noncanonical fixed cells in five hand-authored maps:

| Map | Noncanonical fixed cells |
| --- | ---: |
| `skirmish` | 183 |
| `shellmap` | 73 |
| `Custom_Mission_Skull` | 49 |
| `Custom_Mission_Usual_Brawl` | 44 |
| `Custom_Mission_Boundary_Line` | 15 |

Generated maps must continue to use complete canonical 2x2 stamps. The partial-cell exceptions are historical editing evidence, not a generator target.

## Frozen implementation contract

### Baseline preservation

1. Generator Versions 1 through 3 remain identity-stable.
2. Land changes require a new opt-in profile and generator identity.
3. New random decisions use dedicated streams and must not perturb starts, colonies, Water topology, routes, chokepoints, repairs, or combat-space placement in earlier profiles.

### Phase 6B: Clear-native cosmetic slice

The smallest safe next slice is fixed Clear detail placement:

1. Begin with the accepted Version 3 logical and materialized terrain.
2. Consider only homogeneous Clear 2x2 interior stamps outside protected zones.
3. Replace approximately four percent with fixed template `61` or `62` using a dedicated cosmetic stream.
4. Preserve all four native frames as Clear and verify them against the active NORMAL tileset at startup or validation time.
5. Cosmetic detail positions may differ between symmetry partners because movement semantics are unchanged, but distribution should remain visually balanced.
6. Report eligible cells, selected details, achieved rate, template counts, and excluded protected cells.

Free-standing decoration actors are not part of this slice. They require a separate audit of footprints, targeting, visibility, ownership, and gameplay interaction.

### Phase 6C: weighted land-cover slice

The slow-terrain implementation must apply the material stack in this order:

1. freeze starts, colonies, Water, route reservations, chokepoints, combat-space clearances, and protected production areas;
2. create a symmetric Rock semantic mask only on remaining Clear land;
3. create a symmetric Vegetation mask strictly inside the Rock land-cover envelope;
4. materialize the Clear/Rock boundary from an explicit catalogue;
5. materialize the Rock/Vegetation boundary from a separate explicit catalogue;
6. apply homogeneous PickAny interiors and fixed same-semantic details; and
7. reload the map and validate actual native terrain and weighted movement.

The semantic Rock and Vegetation masks must transform exactly under the selected map symmetry. Fixed cosmetic variants may be selected independently only when their transformed four-frame native semantics remain exact.

### Transition catalogues

Each future land catalogue must record:

- template ID and image-bank local index;
- four native terrain frames;
- semantic 2x2 mask and visual role;
- permitted boundary and interior uses;
- horizontal-mirror, vertical-mirror, and 180-degree candidate sets;
- fixed-detail versus `PickAny` behavior; and
- whether variant selection is gameplay-semantic or cosmetic.

Every entry must be verified against the active NORMAL tileset. Unsupported local neighborhoods must cause bounded symmetric repair or a typed generation rejection; arbitrary best-effort tiles are forbidden.

### Weighted movement and fairness

Phase 6C must extend validation beyond passability:

- compute weighted shortest-path cost using the active `unit` locomotor costs;
- compare symmetry-equivalent start-to-hub, start-to-opponent, start-to-neutral-colony, and declared strategic-route costs;
- preserve the existing native bottleneck-width and reachability gates;
- reject slow-terrain islands that create a materially cheaper route for one player than its symmetry partner;
- verify every starting-colony production exit begins on Clear and connects to the shared Clear strategic network; and
- report Clear, Rock, and Vegetation traversal counts and total cost for representative accepted routes.

Exact semantic symmetry should normally produce exact paired costs. Any future tolerance for asymmetric archetypes requires a separate contract revision and manual acceptance.

## Acceptance gates

Phase 6B automated acceptance requires:

- active-tileset verification for templates `61` and `62`;
- all selected frames resolving to Clear after save/reload;
- deterministic package and detail-template hashes for repeated generation;
- Version 1 through 3 regression identities unchanged;
- achieved detail rate reported against eligible Clear stamps;
- existing native movement, route, colony, production-exit, and combat-space gates passing; and
- broad seed fuzzing plus a manually reviewed visual corpus.

Phase 6C additionally requires:

- complete Clear/Rock and Rock/Vegetation catalogue/transform fixtures;
- no Rock or Vegetation adjacent to Water;
- exact symmetry of native slow-terrain masks;
- weighted-route parity for every paired strategic route;
- protected Clear-zone verification;
- achieved land-relative coverage and component metrics;
- typed rejection for unsupported transition neighborhoods;
- save/reload comparison against engine-resolved terrain costs; and
- manual validation of visual fields, unit-speed behavior, route fairness, and normal match play.

## Reproduction

All generated evidence remains ignored:

```powershell
build-pipeline.cmd build
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-NormalLandSurfaceAudit.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Export-NormalLandTemplateAtlas.ps1
```

The atlas command requires the user's externally installed original assets. Its PNG outputs are local evidence only and must not enter Git.
