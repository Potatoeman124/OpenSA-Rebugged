# Phase 5B: NORMAL Shoreline Materialization

Status: accepted and merged as the opt-in Generator Version 3 path; automated and manual gates pass.

## Purpose and compatibility boundary

Generator Version 3 replaces Version 2's homogeneous Water hard seams with audited NORMAL Water-to-Clear transition templates. It is selected explicitly with `-Topology shoreline` and profile `normal-water-shoreline-v3`.

The accepted Version 2 path remains selected by `-Topology mixed`. Version 3 uses a new profile identity, deterministic random streams, logical hash payload, report schema, and map title. It does not change Version 2 topology, terrain selection, actors, graph, serialized package, or identity hashes.

The repository stores only template identifiers and semantic metadata. It does not contain copied terrain artwork or other original-game assets. Active template definitions are read from the externally supplied NORMAL tileset at runtime and verified before generation.

## Audited transition catalogue

`NormalWaterTransitionCatalogue` is the sole allow-list for fixed NORMAL shoreline templates. Every entry declares:

- its fixed template ID and semantic shoreline role;
- deterministic variant index;
- north/east/south/west visual edge signatures;
- horizontal, vertical, and 180-degree transform targets; and
- the Clear/Water terrain intent of its four native frames in northwest, northeast, southwest, southeast order.

Permitted fixed templates are:

| Role | Variant 0 | Variant 1 |
| --- | ---: | ---: |
| Outer corner northwest | 0 | 3 |
| Outer corner northeast | 2 | 5 |
| Outer corner southeast | 18 | 21 |
| Outer corner southwest | 16 | 19 |
| North edge | 1 | 4 |
| East edge | 10 | 13 |
| South edge | 17 | 20 |
| West edge | 8 | 11 |
| Inner corner northwest | 15 | 31 |
| Inner corner northeast | 14 | 30 |
| Inner corner southeast | 6 | 22 |
| Inner corner southwest | 7 | 23 |

Water interior base variation uses the profile's homogeneous `PickAny` templates `9`, `12`, `26`, `28`, and `29`.

Fixed detail templates `24`, `25`, and `27` depict a small island, scattered rocks, and a dead tree. They remain all-Water in native terrain semantics and are selected only for eight percent of interior Water cells, so they add authored-style open-Water texture without changing ground movement.

At startup, the adapter verifies that every catalogue entry exists in the active NORMAL tileset, is a complete 2x2 template, and has the audited per-frame terrain semantics. A mismatch is a hard failure.

## Topology preparation and materialization

Version 3 retains the Version 2 strategic graph, route reservations, chokepoint dimensions, combat-space rules, density ranges, region-size limits, separation rings, and bounded-repair limits.

Random Water regions use a Version 3-only grower. A region is built as a connected union of overlapping 2x2 logical blocks, which allows it to follow available topology space without creating one-cell Water ribbons. Every intermediate candidate must satisfy the exact supported shoreline neighborhood grammar before it can be placed. A region remains constrained to the frozen 8-64 logical-cell range and must retain its symmetry separation and a single connected OPEN component.

After topology and bounded repair, `RmgShorelineMaterializer` classifies every Water logical cell from its 3x3 neighborhood as one of:

- interior;
- cardinal edge;
- outer corner; or
- inner corner.

Opposite-edge, three-sided, four-sided, and multiple-inner-corner neighborhoods are unsupported. Their presence rejects generation with `TERRAIN_MATERIALIZATION_UNSUPPORTED_NEIGHBORHOOD`; no fallback hard seam is emitted.

Clear, Water-interior, and shoreline variants use separate deterministic streams. The five vegetation-bearing shoreline templates are explicitly tagged and selected at a 16-percent target; other shoreline variants remain plain. The target comes from 614 decorated versus 3,241 plain straight-shore stamps in the authored NORMAL corpus.

Symmetry partners retain transformed shoreline roles, native terrain masks, and compatible edge signatures, but sample cosmetic variants independently. This preserves topology and passability symmetry while avoiding repeated decoration bands. Adjacent logical cells must still expose matching visual edge signatures after assignment.

## Native movement authority

Each logical cell records four `RmgNativeTerrainIntent` values. These values, not the coarse logical obstacle flag, are authoritative after materialization. This matters for outer-corner stamps, whose land-facing native corner is Clear while their other frames are Water.

The following gates consume native intent:

- saved-package reload terrain comparison;
- logical connectivity proxy for Version 3;
- native movement validator semantic comparison;
- proxy/native comparison; and
- Version 3 logical hashing.

Reload validation additionally uses the engine's active terrain data, and the native movement validator remains authoritative for ground reachability, route width, starting-colony exits, and static actor footprints.

Reports use schema version 4 and include role counts, Water template usage, configured visual-density targets, actual shoreline-decoration and open-Water-detail counts, Clear/Water native-cell counts, unsupported-neighborhood count, template ID grids, shoreline role grids, and four-frame native-intent grids.

## Automated evidence

The focused self-test command covers catalogue completeness, transform involution, per-frame transform semantics, visual-edge transforms, every supported neighborhood role, negative unsupported neighborhoods, two-logical-cell thickness, same-seed determinism, bounded repair, and combat-space rules.

The bounded Phase 5B cross-product campaign passed with:

- 74 generated candidates, 49 accepted and 25 typed bounded rejections;
- zero blocking failures, invalid accepted maps, generation exceptions, or same-seed hash mismatches;
- 34/34 package, reload, lint, and native-movement samples accepted;
- zero package-hash mismatches, YAML lint failures, topology-result disagreements, or overall proxy/native result disagreements; and
- minimum native route width 3, matching the intentional central chokepoint contract.

After manual review identified excessive systematic shoreline vegetation and empty Water interiors, the final visual-refinement campaign passed with:

- 69 generated candidates, 52 accepted and 17 typed bounded rejections;
- zero blocking case failures; and
- 36/36 package, reload, lint, and native-movement samples accepted.

Passing campaign reports are generated under `artifacts/rmg/phase-5b-shoreline-materialization/` and remain untracked.

Five accepted Version 2 combat-regression identities were regenerated after the Version 3 implementation. Seeds `1000001`, `1001134`, `101368`, `1001054`, and `1001106` matched their pre-Phase-5B logical, actor, graph, canonical-package, and OpenRA UID hashes exactly.

After the visual-density refinement, seed `1000001` was regenerated again and retained all five frozen Version 2 identities exactly. The Version 3 configuration identity remained unchanged so the cosmetic refinement did not reseed topology, colony placement, or combat-space streams.

## Usage

Build and run focused validation:

```powershell
.\build-pipeline.cmd validate
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgSelfTests.ps1
```

Generate and install a shoreline map:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 51001 -Players 2 -NeutralColonies 10 `
    -Symmetry horizontal -Archetype open -Topology shoreline `
    -MovementValidation both -InstallForPlay -Overwrite
```

Run the full fuzz harness on Version 3:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGeneratorFuzz.ps1 `
    -Topology shoreline -Overwrite
```

Omitting `-Topology shoreline` continues to select the frozen Clear-only Version 1 default. `-Topology mixed` continues to select the frozen blocking-topology Version 2 baseline.

## Manual acceptance

On 2026-08-16, the reviewer accepted all eight post-review maps. Shoreline orientation remained coherent, shoreline vegetation appeared sparse and irregular, open-Water details remained visible without becoming dense, and no blocking, production-exit, spawn-fire, or match-play regression was reported.

Land decoration, land-cover fields, Rock/Vegetation terrain transitions, more irregular authored-style coastlines, new topology archetypes, and broader layout redesign remain outside Phase 5B.
