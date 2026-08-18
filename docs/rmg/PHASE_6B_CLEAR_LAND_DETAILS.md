# Phase 6B Clear-native land details

## Status

Automated implementation and validation are complete on the opt-in Generator Version 4 path. Manual review on 2026-08-17 found no functional blocker, so this is an accepted implementation baseline for Phase 6C, but its visual distribution remains WIP.

The review found that Clear-native stone details can appear strongly displaced toward map margins, most visibly on route-dense map 3100026. This follows directly from the conservative Phase 6B contract: starts, colonies, routes, junctions, chokepoints, and repairs all exclude cosmetic placement. The result is safe but more visibly guided than the broadly distributed vegetation on authored maps. Phase 6C may expose this less strongly once larger Rock and Vegetation fields are present; after combined visual review, a later cosmetic-only adjustment may relax selected exclusions without weakening the protected-Clear contract for movement-penalty terrain.

Phase 6B adds only sparse fixed details whose four native frames resolve to Clear terrain. It does not add Rock, Vegetation, decoration actors, movement penalties, blocking, or a new layout algorithm.

## Compatibility and identity

Version 4 is selected explicitly with -Topology land-details and profile normal-land-details-v4. Versions 1 through 3 remain available and identity-stable.

Version 4 inherits Version 3's deterministic streams for starts, actors, strategic graph, Water topology, repairs, Clear interiors, shoreline transitions, shoreline decoration, and open-Water details. For matching gameplay settings and seed:

- actor and graph hashes are identical;
- topology, reservations, routes, repairs, and shoreline roles are identical;
- Water and every non-land-detail template are identical; and
- only selected Clear interior templates may change to fixed detail template 61 or 62.

The new choices use the dedicated terrain-clear-land-details stream and are included in the Version 4 logical, canonical-package, engine-UID, and detail-selection identities.

## Placement contract

The active NORMAL tileset is runtime-verified before generation:

- templates 61 and 62 must exist;
- each must be a complete canonical 2x2 template; and
- all four frames must resolve to native Clear terrain.

A logical stamp is eligible only when it is non-Water Clear terrain and does not overlap any of these protected layers:

- starting-area reservations;
- starting or neutral colony clearances;
- named strategic routes;
- strategic junction regions;
- chokepoint apertures; or
- repaired cells.

The target is the nearest integer to four percent of eligible Clear stamps. Candidate stamps are divided into the two sides induced by the selected map symmetry, shuffled with the dedicated stream, and selected independently. Side counts may differ by at most one. Cosmetic partners may use different templates or only one side may receive a detail because native movement semantics remain exactly Clear.

The materializer records:

- eligible Clear stamps;
- protected Clear exclusions;
- target and selected detail stamps;
- achieved percentage;
- per-template usage;
- symmetry-side counts; and
- SHA-256 of ordered logical-index:template-id selections.

## Validation gates

Generation and save/reload validation reject:

- a configured detail template that is missing, noncanonical, or not homogeneous Clear;
- a detail placed on a protected stamp;
- a detail with any non-Clear native terrain intent;
- a selected count different from the exact rounded target;
- inconsistent eligible/protected accounting; or
- symmetry-side counts that differ by more than one.

Existing map lint, package reload, native terrain comparison, movement connectivity, route width, colony reachability, production-exit, combat-space, and wasp-contract gates remain mandatory.

The focused self-test additionally proves that Version 4 inherits the Version 3 base. It compares actors, graph, topology, routes, start and structure reservations, strategic regions, shoreline roles, and every non-detail template.

## Automated evidence

The bounded Phase 6B campaign used seed start 620000, 100 Gate A seeds, 500 mixed legal-count seeds, 25 seeds per legal colony-count profile, all supported player counts, symmetries, and archetypes, and one full runtime sample per 50 cases.

Results:

- 3,121 generated cases;
- 2,434 accepted cases;
- 687 expected typed bounded rejections;
- zero blocking failures;
- zero logical hard-invalid cases;
- focused self-tests passed; and
- 95 of 95 save/reload, package, lint, and native-movement samples passed.

Rejections used only the established CHOKEPOINT_PLACEMENT, TOPOLOGY_ATTEMPTS_EXHAUSTED, and COLONY_PLACEMENT classes. No Clear-detail rejection occurred.

A repeated representative map selected 66 of 1,648 eligible stamps, an achieved rate of 4.004854 percent, with 33 details on each symmetry side. Templates 61 and 62 were used 31 and 35 times respectively. Reloaded terrain reported zero semantic mismatches and zero production-exit failures.

The raw ZIP container may contain changing timestamps. Determinism is therefore gated by the canonical map.yaml/map.bin hash, engine UID, logical hash, actor hash, graph hash, and Clear-detail selection hash rather than the raw archive byte hash.

All generated packages, JSON reports, and runtime samples are under artifacts/rmg/phase-6b-clear-land-details/ and remain ignored.

## Legal boundary

The repository contains only source, template identifiers, semantic rules, configuration, and durable documentation. Template artwork continues to come from the user's external original-game installation. Generated maps, screenshots, atlases, original sprites, and other asset-derived evidence must not be staged.

## Usage

Build and generate an ignored example:

    .\build-pipeline.cmd validate
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 610001 -Players 2 -Symmetry rotational -Archetype open -Topology land-details -MovementValidation both -Overwrite

Install the map for play:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 610001 -Players 2 -Symmetry rotational -Archetype open -Topology land-details -MovementValidation both -InstallForPlay -Overwrite

Run focused and broad validation:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgSelfTests.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGeneratorFuzz.ps1 -Topology land-details -Overwrite

See [Phase 6B manual playtest seeds](PHASE_6B_MANUAL_PLAYTEST_SEEDS.md) for the prepared visual corpus and acceptance checklist.

## Deferred scope

Phase 6C remains responsible for Rock and Vegetation semantic masks, transition catalogues, field morphology, Water separation, protected Clear zones, weighted shortest-path validation, and terrain-cost fairness. Free-standing decoration actors still require a separate gameplay audit.

The final spatial distribution of Clear-native details also remains deferred pending combined Phase 6C review. In particular, Phase 6B's broad route and strategic-region exclusions should be reassessed for cosmetic details only; they remain mandatory for Rock and Vegetation.
