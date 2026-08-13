# Phase 4D blocking-topology verification

## Decision

Generator Version 2 passes the Phase 4D automated blocking-topology gate for the supported matrix below. The gate found no accepted invalid map, unhandled generation exception, determinism mismatch, package/hash mismatch, lint failure, or unresolved automated blocker.

This decision establishes technical readiness for manual gameplay calibration. It does not claim that route spacing, blocker density, chokepoint feel, or swarm throughput are balanced. In particular, some dense four-player profiles have high bounded-rejection rates and should receive focused gameplay and later acceptance-rate calibration.

**CLI BLOCKING TOPOLOGY VERIFIED — READY FOR GAMEPLAY CALIBRATION**

## Supported matrix

The verified contract is deliberately explicit:

| Dimension | Supported values |
| --- | --- |
| Generator | Version 2, configuration `normal-water-blocking-v2` version 2 |
| Topology | `mixed` |
| Map | 128x128 native cells, 64x64 logical macro-cells, `NORMAL` tileset |
| Players | 2 or 4 |
| Symmetry | `horizontal`, `vertical`, `rotational` |
| Archetype | `open`, `central-contest` |
| Neutral colonies, 2P | 8, 10, 14, 20 |
| Neutral colonies, 4P | 12, 16, 20, 24 |
| Ground blocker | Homogeneous Water templates |
| Movement validation | `unit` ground topology is authoritative; `wasp` bypass is checked separately |

Other map sizes, tilesets, player counts, colony counts, terrain transitions, topology presets, and archetypes are outside this gate.

## Evidence and method

The point-in-time evidence is intentionally untracked under:

`artifacts/rmg/phase-4d-blocking-topology-verification/final-exhaustive/`

The authoritative campaign report is `campaign/phase-4d-full.json`; the original pre-classification output is retained beside it as `campaign/phase-4d-full.raw-pre-classification.json`. The campaign executed for 7,312,840 ms, approximately 2 hours 2 minutes.

During review, nine candidates were found whose temporary packages correctly failed authoritative native route-width validation and were deleted, but whose results had been classified as accepted failures by the first campaign harness. The harness now models three outcomes: accepted, expected rejection, and technical failure. The authoritative report was normalized mechanically from the retained raw report by moving those nine cases from accepted/failure accounting into typed `NATIVE_CONFORMANCE` rejections. No candidate was regenerated, omitted, or changed. A focused exact-profile run independently confirmed the corrected accounting in `tooling-smoke/native-rejection-accounting-exact-fixed.json`.

The automated package layer covers materialization, save/reload, rules and sequence initialization, map lint, player/spawn definitions, exact static actor footprints, native movement semantics, canonical package hashing, and OpenRA UID calculation. The pinned engine does not expose a constructible live `World` to the utility assembly, so interactive world initialization and live swarm behavior remain manual F5/Ctrl+F5 checks.

## Gate results

| Gate | Requirement | Result |
| --- | --- | --- |
| Gate A | At least 100 accepted maps per default-count player/symmetry/archetype profile | PASS: 12/12 profiles; minimum 100 accepted; maximum 174 attempts |
| Gate A consecutive window | 100 initial consecutive candidates per profile | PASS: all candidates classified; expected rejections did not hide technical failures |
| Boundary matrix | All 48 legal player/count/symmetry/archetype profiles, 25 candidates each | PASS: 1,200 candidates; all 48 profiles produced accepted maps |
| Mixed campaign | At least 5,000 mixed supported candidates | PASS: 5,000 candidates; 4,111 accepted and 889 expected rejections |
| Full package/native validation | Authoritative validation of every logically accepted candidate | PASS: 6,375 package attempts; 6,366 accepted and 9 typed native-conformance rejections |
| Hard validity | No accepted invalid map | PASS: 0 logical hard-invalid, 0 accepted hard-invalid, 0 blocking failures |
| Exceptions | No unexpected generation exception | PASS: 0 |
| Determinism | Five identities equal across separate processes | PASS: 15/15 reference profiles |
| Version 1 regression | Frozen Version 1 identities unchanged | PASS: 5/5 identities for seed 424242 |

Across the complete 7,518-candidate campaign:

- 6,366 candidates were accepted (84.676776%);
- 1,152 were expected bounded rejections (15.323224%);
- 0 were technical failures;
- 0 accepted maps violated hard validity;
- 0 same-seed identity mismatches occurred; and
- all 48 forced boundary profiles completed authoritative package/native validation.

## Rejection and retry accounting

Expected rejection reasons are kept separate from technical failures:

| Expected rejection | Count | Meaning |
| --- | ---: | --- |
| `CHOKEPOINT_PLACEMENT` | 559 | No legal exact choke shoulder survived the bounded placement envelope |
| `TOPOLOGY_ATTEMPTS_EXHAUSTED` | 509 | All four deterministic topology attempts were rejected |
| `COLONY_PLACEMENT` | 75 | No legal symmetric colony placement survived the frozen clearances |
| `NATIVE_CONFORMANCE` | 9 | The temporary package failed authoritative native route-width validation and was not published |

There were 572 accepted maps requiring a retry and 1,351 rejected attempts before acceptance. Retry count had mean 0.212, p95 3, p99 3, and maximum 3. No accepted map required subtractive repair: repair mean, p95, p99, maximum, and total operations were all zero.

High rejection is not a validity blocker, but it is calibration debt. The least accepting 25-seed boundary profiles were 4P/24/vertical/open at 16% and 4P/24/horizontal/open at 28%. Gate A's 2P/10/vertical/central-contest profile needed 174 candidates to reach 100 accepted maps. Future work should improve construction success without weakening route widths, clearances, symmetry, or native conformance.

## Native and package conformance

All 6,366 accepted maps passed:

- save/reload and metadata checks;
- rules and sequences initialization;
- map YAML lint;
- native start-component reachability;
- exact starting-colony and neutral-colony footprint clearance;
- strategic node and route reachability;
- configured normal, major, and exact chokepoint widths;
- actor, graph, logical, canonical package, and OpenRA UID identity calculation; and
- topology agreement between logical and authoritative native results.

The nine `NATIVE_CONFORMANCE` cases are retained in [the failure-seed register](PHASE_4D_FAILURE_SEEDS.md). They are safe rejections: the adapter throws before publication and deletes the temporary package. They indicate an efficiency gap between logical construction and final native validation, not a playable-map defect.

The older 3x3 proxy is intentionally not authoritative. Aggregate proxy false positives and false negatives therefore remain diagnostic disagreement counts, while the native ground model decides acceptance.

## Separate-process determinism and Version 1 regression

`final-exhaustive/independent-process-matrix-fixed/reference-matrix.json` records 15/15 profiles passing two independent utility-process generations. Each pair matched:

1. logical SHA-256;
2. actor SHA-256;
3. strategic graph SHA-256;
4. canonical `map.yaml` plus `map.bin` SHA-256; and
5. OpenRA UID SHA-1.

`final-exhaustive/v1-regression/seed-424242.json` independently confirms the five frozen Version 1 identities. Generator Version 2 changes therefore do not alter the default Clear-only generator.

## Visual audit

Ten accepted maps were rendered as semantic debug previews and inspected from the baseline, dense, boundary, retry-heavy, and central-contest profiles: seeds 1000001, 1000076, 1000551, 1000576, 1000601, 1000726, 1001054, 1001106, 1001134, and 101368.

The audit found:

- recognizable routes and strategically legible central hubs;
- coherent contiguous Water regions rather than noisy single-cell speckling;
- visible clearance around all starts and neutral colonies;
- understandable normal routes, major routes, and exact chokepoints;
- preserved reflection or rotation symmetry;
- no boxed start or colony; and
- no repair artifact, consistent with zero repair operations.

The previews are semantic overlays generated from project data. They do not copy original-game art or copyrighted map assets and remain in the ignored evidence directory.

## Performance observations

Performance is acceptable for an offline developer generator, but the dense rejection envelope is expensive:

| Phase | Mean | p95 | p99 | Maximum |
| --- | ---: | ---: | ---: | ---: |
| Logical generation | 147.667 ms | 1,094.720 ms | 1,149.697 ms | 1,383.314 ms |
| Complete candidate | 972.708 ms | 5,662.571 ms | 7,051.777 ms | 8,685.453 ms |
| Package total | 372.418 ms | 2,275.025 ms | 2,386.734 ms | 3,029.487 ms |
| Materialize and save | 30.439 ms | 41.929 ms | 56.200 ms | 208.402 ms |
| Reload and metadata | 19.035 ms | 28.665 ms | 43.234 ms | 84.053 ms |
| YAML lint | 2.971 ms | 7.736 ms | 15.180 ms | 33.355 ms |
| Native movement | 26.926 ms | 48.538 ms | 57.349 ms | 126.837 ms |

The package p95 is dominated by candidates that reach late topology attempts. This is a later optimization target, not an automated correctness blocker.

## Reproduction

After `build-pipeline.cmd validate`, the durable entry points are:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgSelfTests.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-BlockingTopologyReferenceMatrix.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgV1Regression.ps1
```

`Invoke-Phase4dVerification.ps1` runs the complete multi-hour campaign and all gates. Its outputs are evidence, not repository content. For gameplay testing, use [the manual playtest seed shortlist](PHASE_4D_MANUAL_PLAYTEST_SEEDS.md).

## Remaining boundary

No automated blocker remains for manual gameplay calibration. Manual testing must still establish live world load, unit throughput, path-choice quality, chokepoint feel, practical colony accessibility, and acceptable visual/gameplay density. Those observations may justify later tuning, but the Phase 4D gate forbids silently weakening technical validity rules to improve subjective acceptance.
