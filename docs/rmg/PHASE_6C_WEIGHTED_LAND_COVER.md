# Phase 6C weighted NORMAL land cover

## Status

Generator Version 5 implementation, automated validation, and manual playtesting are complete. Manual review on 2026-08-18 approved it for merge as a working prototype and technical baseline. It is not yet a finished battlefield-layout baseline.

The review confirmed correct terrain semantics, movement penalties, shoreline inheritance, colony safety, and playable matches. Its major WIP limitation is spatial composition: Rock and Vegetation often remain near map borders while likely travel and combat regions stay predominantly Clear; seed 3100025 is the positive exception in the current corpus. Phase 7 must make these movement-affecting fields role-aware.

Cosmetic doodads are a separate concern. Grass, stones, mushrooms, weeds, and similar non-gameplay details should achieve broad sector coverage and avoid visually empty interiors, without using uniform noise to place movement-affecting terrain.

Phase 6C adds passable but movement-affecting Rock and Vegetation fields to the inherited Version 4 map. It preserves protected Clear space around starts, colonies, production exits, strategic routes, junctions, chokepoints, and repairs. It also adds native weighted shortest-path parity checks so symmetric players receive equal terrain traversal cost, not merely equal geometric reachability.

## Compatibility and identity

Version 5 is selected explicitly with `-Topology land-cover` and profile `normal-land-cover-v5`. Versions 1 through 4 remain available and identity-stable.

For matching gameplay settings and seed, Version 5 inherits Version 4's:

- starting positions, neutral colonies, actors, and strategic graph;
- Water topology, repairs, route reservations, chokepoints, shoreline, and open-Water detail;
- starting, structure, combat-space, and production-exit reservations; and
- Clear-detail selection before slow-terrain materialization.

Only the dedicated Version 5 land-cover streams select Rock/Vegetation morphology, transition variants, and slow-surface details. Version 5 logical and canonical identities include those selections. The focused regression test proves Version 4-to-Version 5 inherited-base equality independently of the expected land-template changes.

## Native semantic contract

The active NORMAL tileset is runtime-verified against the audited catalogue before generation. Catalogue entries cover templates 37 through 100 and declare each of their four native terrain frames as Clear, Rock, or Vegetation.

The materializer uses two nested banks:

1. Clear-to-Rock transition and Rock-interior templates; and
2. Rock-to-Vegetation transition and Vegetation-interior templates.

Vegetation must therefore remain inside a complete Rock envelope. Clear-to-Vegetation contact is forbidden, as is any Rock or Vegetation contact with Water. Unsupported diagonal-only masks are closed deterministically before a template is selected. Semantic symmetry is exact at native-cell level even when an equivalent visual variant is chosen for a partner stamp.

Templates 93 and 94 are permitted as sparse Vegetation- and Rock-native fixed details. Clear-native templates 61 and 62 remain governed by Phase 6B. No decoration actor is introduced.

## Morphology and capacity

The slow-terrain mask is generated on a 65-by-65 control lattice for the 64-by-64 logical map. Deterministic symmetric seeds grow into irregular connected fields; disconnected eligible regions may receive deterministic reseeds. The generator creates Vegetation cores first, dilates their required Rock envelope, and then grows additional Rock toward its target.

Nominal land-relative targets are:

- Rock: 14 percent;
- Vegetation: 8 percent;
- Rock-native detail: 2 percent of eligible Rock interiors; and
- Vegetation-native detail: 3 percent of eligible Vegetation interiors.

Targets are capacity-aware. Protected Clear cells and Water separation are removed before morphology. Vegetation is clamped to the capacity that can retain a complete Rock ring; Rock is then clamped to the remaining envelope capacity. Reports retain requested, effective, achieved, capacity, and shortfall values. A shortfall is acceptable only when it follows from audited capacity and all safety reservations remain intact.

This priority is intentional: gameplay-safe Clear space outranks visual-density targets. Four-player or colony-dense maps may therefore contain materially less slow terrain than open two-player maps.

## Weighted movement validation

The active ground locomotor contract is read from the loaded rules and must match:

| Terrain | Speed | Pathing cost |
| --- | ---: | ---: |
| Clear | 100% | 100 |
| Rock | 75% | 133 |
| Vegetation | 50% | 200 |
| Water | impassable | n/a |

The native validator now runs multi-source Dijkstra searches over reloaded engine terrain. Cardinal steps use the destination terrain cost; diagonal steps use the same cost multiplied by the existing 1.41 approximation. It records total weighted cost, step count, and Clear/Rock/Vegetation traversal counts for:

- each declared strategic endpoint pair;
- start-to-opponent paths; and
- start-to-neutral-colony paths.

Every path is compared with its exact symmetry partner. Any reachability disagreement or nonzero cost delta is a hard `LAND_COVER_WEIGHTED_PARITY` failure.

Weighted fairness uses the unconstrained engine-terrain graph between exact endpoint stamps. Units are not restricted to an internal route identifier during play. Named-route masks continue to gate route connectivity and usable width separately. Static colony footprints remain in the authoritative reachability/width graph; the weighted terrain-only graph removes those footprints so it measures terrain cost rather than faction-actor shape.

The wasp locomotor remains outside this ground-cost contract because it permits Clear, Rock, Vegetation, Water, and Air and disables normal domain passability checks.

## Validation gates

Generation or package validation rejects:

- a missing, noncanonical, or semantically incorrect audited template;
- Rock or Vegetation on any protected Clear cell;
- Rock/Vegetation contact with Water or direct Clear/Vegetation contact;
- an unsupported transition mask or incomplete Vegetation envelope;
- inconsistent requested/effective/capacity accounting;
- native semantic mismatch after save and reload;
- broken exact symmetry or weighted path-cost parity; or
- any existing topology, route-width, colony reachability, production-exit, combat-space, package, lint, or wasp-contract failure.

## Automated evidence

The focused suite passes Version 1 through Version 5 self-tests, Version 3-to-Version 4 and Version 4-to-Version 5 inherited-baseline checks, catalogue/materializer checks, and the native validator's synthetic weighted-path case.

The bounded Phase 6C campaign used seed start 630000, 20 Gate A seeds, 50 mixed legal-count seeds, five seeds per legal colony-count profile, all supported player counts, symmetries, and archetypes, and full runtime sampling at the configured boundary rate.

Results:

- 588 generated cases;
- 444 accepted cases;
- 144 typed bounded rejections;
- zero blocking failures and an empty failure-reason set;
- zero weighted-parity failures; and
- 82 of 85 package/native samples accepted.

The three runtime rejections are inherited `NATIVE_ROUTE_WIDTH` results for four-player, 20-colony, rotational/open topology. Reproducing seed 1630201 on the Version 4 baseline yields the same two routes at native width 7 against the required width 9. They are therefore retained as bounded topology rejections, not hidden or attributed to Version 5 land cover.

A post-optimization smoke campaign covered another 124 cases: 103 accepted, 21 typed bounded generation rejections, zero blocking failures, and 36 of 36 package/native samples accepted.

All eight prepared manual maps pass generation, save/reload, semantic terrain, connectivity, route-width, combat-space, production-exit, and weighted-cost parity gates. Their achieved Rock/Vegetation percentages are recorded in the manual corpus document. Raw packages and JSON evidence remain ignored under `artifacts/rmg/phase-6c-weighted-land-cover/`.

## WIP visual distribution

Manual Phase 6B review found that Clear-native stone details can be displaced toward map margins because conservative exclusions remove colonies, routes, and strategic interiors from their eligible set. Phase 6C intentionally does not relax those protections while introducing movement penalties.

The combined Version 5 surface must now be reviewed before changing the cosmetic-only rules. If large interiors still feel sterile or details form visibly guided edge bands, a later pass may relax selected exclusions for Clear-native cosmetic details only. Rock and Vegetation must continue to respect the full protected-Clear contract.

## Legal boundary

The repository contains only source, template identifiers, semantic rules, configuration, and durable documentation. Terrain artwork and other original assets continue to come from the user's external original-game installation. Generated maps, reports, screenshots, atlases, sprites, and other asset-derived evidence must not be staged.

## Usage

Build, generate, and install a Version 5 map:

```powershell
.\build-pipeline.cmd validate
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 3100000 -Players 2 -NeutralColonies 8 -Symmetry horizontal -Archetype open -Topology land-cover -MovementValidation both -InstallForPlay -Overwrite
```

Run focused tests and the default Phase 6C campaign:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgSelfTests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGeneratorFuzz.ps1 -Topology land-cover -MovementValidation both -Overwrite
```

See [Phase 6C manual playtest seeds](PHASE_6C_MANUAL_PLAYTEST_SEEDS.md) for the installed corpus and acceptance checklist.
