# Phase 7A Battlefield Layout Audit

## Status and decision

Phase 7A is a read-only layout audit. It does not change Generator Version 5 output. The accepted Phase 6 result remains a playable working prototype, while this audit establishes the evidence and design boundary for the role-aware layout redesign in Phase 7B.

The audit confirms two separate weaknesses in the prototype:

- Rock and Vegetation are concentrated at the map edge instead of being assigned to tactically meaningful battlefield roles; and
- cosmetic detail is too sparse and uneven, leaving large visually empty areas.

These problems must not share one placement policy. Movement-cost terrain needs deliberate, archetype-aware placement. Cosmetic detail may use broad spatial sampling, but only after distinguishing genuinely passable decoration from actors that block a native cell.

## Corpus scope

The audit covers every shipped System map, not only skirmish maps:

| Classification | Maps | Traffic analysis |
| --- | ---: | --- |
| Campaign | 100 | Static spatial metrics only; scripted objectives and reinforcements are not inferred |
| Custom/challenge scenarios | 13 | Static spatial metrics only for scripted maps |
| Skirmish references | 11 | Static spatial metrics and inferred traffic bands |
| Other/unclassified | 1 | Static spatial metrics only |
| Phase 6 generated comparisons | 8 | Static spatial metrics and inferred traffic bands |

This resolves the analysis-scope gate: campaign and custom scenarios are first-class calibration sources. They are useful for terrain composition and visual-distribution evidence. They cannot be treated as equivalent competitive-layout references because mission scripts can create objectives, routes, encounters, and constraints that are not represented by spawn and colony coordinates alone.

The default generated comparison set is the eight ignored `manual-310*.oramap` packages from the Phase 6C corpus.

## Measurement model

The audit utility is implemented by `AuditRmgBattlefieldLayoutsCommand` and invoked through `scripts/rmg/Invoke-BattlefieldLayoutAudit.ps1`. It writes diagnostic JSON beneath `artifacts/rmg/phase-7a-battlefield-layout-audit/`, which remains untracked.

Each playable map is divided into a normalized 4x4 sector grid. The report measures:

- playable-cell fractions for Clear, Rock, Vegetation, Water, and combined movement-modifier terrain;
- the fraction of each surface in the outer 15-percent edge band;
- the fraction of each surface in the central half of the map;
- sector coverage, so a global density cannot conceal large empty regions;
- `plant_` decoration-actor density, sector coverage, edge/central distribution, and blocking/passable split;
- density and sector coverage of fixed embedded detail stamps using audited templates 61, 62, 93, and 94; and
- movement-terrain and decoration coverage inside inferred traffic bands for skirmish and generated maps.

Traffic bands connect player starts to other starts and strategic colonies. Their center paths are terrain-neutral: all passable steps use geometric cost only. This is intentional. A route derived from current movement costs would avoid slow terrain and make the measurement circular. The current band radius is eight native cells.

Traffic metrics are not emitted for campaign or scripted custom maps. Static terrain and decoration metrics still cover all of those maps.

## Decoration safety boundary

The actor name is not sufficient to decide that an object is cosmetic. Five audited `plant_` actors explicitly use a passable `_` footprint:

- `plant_flower`;
- `plant_desert_flower`;
- `plant_cherry_flower`;
- `plant_smarty`; and
- `plant_choc_chip`.

Other plant actors inherit the blocking `x` footprint from `^CoreDecoration`, including broad-leaf plants, desert grass, and mushrooms. Phase 7B must therefore keep three independent decoration categories:

1. passable fixed detail stamps embedded in terrain;
2. passable cosmetic actors; and
3. blocking decoration actors that require topology-aware placement and native movement validation.

## Aggregate findings

The following values are medians. Density is measured per 1,000 playable native cells; sector coverage is the fraction of the 16 normalized sectors containing the measured feature.

| Corpus | Movement cells | Movement at edge | Movement in central half | Movement sector coverage | Decoration actors / 1,000 | Actor sector coverage | Embedded details / 1,000 | Embedded sector coverage |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Campaign | 15.86% | 64.88% | 8.57% | 62.50% | 12.93 | 100.00% | 4.18 | 84.38% |
| Custom/challenge | 23.22% | 64.11% | 13.03% | 81.25% | 13.26 | 100.00% | 2.99 | 87.50% |
| Skirmish reference | 26.59% | 69.80% | 7.71% | 68.75% | 11.23 | 100.00% | 4.70 | 87.50% |
| Generated prototype | 15.98% | 97.38% | 0.00% | 75.00% | 0.00 | 0.00% | 1.22 | 59.38% |

The generated maps have a plausible global movement-terrain amount, but the spatial allocation is wrong. A 97.38-percent edge share and zero central-half share show that Rock and Vegetation are functioning almost entirely as borders. The inferred traffic band contains only 0.06 percent movement-modifier terrain, with a 0.003 median enrichment ratio relative to the whole map.

The cosmetic result is independently too sparse. The prototype currently emits no free-standing decoration actors and its fixed details occur at roughly one quarter of the authored-skirmish median density. A median of only 9.5 of 16 sectors containing embedded details explains the visibly empty interiors. This finding supports broader cosmetic sampling; it does not justify random scattering of movement-cost terrain.

## Authored battlefield references

No single authored map is a universal target. Together, these four references demonstrate different valid allocations of movement terrain while maintaining broad visual detail coverage:

| Map | Movement cells | Edge share | Central-half share | Movement sectors | Traffic-band movement | Embedded-detail sectors | Decoration-actor sectors |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Candyland Battleground | 31.13% | 79.98% | 7.61% | 15/16 | 12.30% | 16/16 | 16/16 |
| Desert Battleground | 32.52% | 69.80% | 5.56% | 15/16 | 33.75% | 16/16 | 16/16 |
| Narrow Passage | 17.08% | 59.61% | 31.38% | 9/16 | 18.61% | 16/16 | 16/16 |
| Swamp Battleground | 22.66% | 68.02% | 9.86% | 14/16 | 19.06% | 16/16 | 16/16 |

The evidence argues for layout archetypes and role fields, not one universal coverage threshold. Narrow Passage deliberately concentrates movement terrain through the center, while the Battleground maps distribute it more broadly. All four nevertheless place meaningful movement terrain away from a pure border-only role and provide cosmetic detail across every sector.

## Phase 7B contract direction

Phase 7B should introduce an explicit battlefield-role field before materializing terrain. At minimum it should identify:

- protected start, production, colony-combat, and mandatory-clearance zones;
- primary routes and contest zones;
- secondary or flank routes;
- quiet/open battlefield zones; and
- hard-blocker and shoreline reservations inherited from the accepted topology.

Movement-cost terrain must then be allocated by role and archetype:

- do not spend the movement-terrain budget primarily on border decoration;
- require non-zero movement terrain in declared gameplay-relevant roles for every accepted seed;
- preserve route reachability, width, combat-space clearance, and weighted-path symmetry;
- permit different central, route, flank, and sector profiles for `open` and `central-contest`; and
- validate spatial distribution in addition to total Rock and Vegetation coverage.

Cosmetic coverage should be a separate deterministic pass:

- sample eligible space broadly enough to prevent large empty sectors;
- use fixed terrain details and passable actors without changing movement or combat topology;
- treat blocking decorations as gameplay objects, with clearance rules and native validation;
- avoid rigid grids, systematic shoreline rows, and obvious symmetry repetition where semantic fairness does not require identical visuals; and
- report density and sector coverage separately from movement terrain.

Phase 7B acceptance should initially compare bounded seed distributions with authored ranges instead of freezing arbitrary constants from a single map. However, every accepted generated map must have non-zero movement-modifier presence in its declared tactical roles, and the present zero-central/near-zero-traffic condition is a hard failure. Cosmetic detail must demonstrate broad sector coverage without using blocking actors to manufacture visual density.

## Reproducing the audit

Build the project and run:

```powershell
.\build-pipeline.cmd build
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-BattlefieldLayoutAudit.ps1
```

The wrapper scans all shipped System maps. If the Phase 6C example directory exists, it also compares files matching `manual-310*.oramap`. Use `-GeneratedMapDirectory` to select another explicit generated-map directory, or `-OutputDirectory` to redirect ignored evidence.

The output is `battlefield_layout_audit.json`. It contains corpus aggregates, tileset aggregates, named reference summaries, and per-map measurements. It is evidence, not tracked project documentation.
