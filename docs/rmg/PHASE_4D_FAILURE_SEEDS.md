# Phase 4D blocking-topology failure and regression seeds

This register records technically relevant Generator Version 2 seeds discovered during Phase 4D. Generated maps, JSON reports, screenshots, and campaign corpora remain under `artifacts/rmg/phase-4d-blocking-topology-verification/` and are not tracked.

All commands assume the repository root and a completed `build-pipeline.cmd validate` run.

## Safe expected rejections in the final campaign

The final campaign found nine cases where the logical candidate reached packaging but authoritative native validation found a normal-route width below contract. Each is classified as the typed expected rejection `NATIVE_CONFORMANCE` with the underlying diagnostic `NATIVE_ROUTE_WIDTH`.

| Seed | Profile | Native result |
| ---: | --- | --- |
| 1001053 | 4P, 24 colonies, horizontal, open | 2 named routes below width |
| 1001059 | 4P, 24 colonies, horizontal, open | 2 named routes below width |
| 100476 | 4P, 24 colonies, vertical, open | 2 named routes below width |
| 101460 | 4P, 24 colonies, vertical, open | 2 named routes below width |
| 101544 | 4P, 24 colonies, vertical, open | 2 named routes below width |
| 101568 | 4P, 24 colonies, vertical, open | 4 named routes below width |
| 102792 | 4P, 24 colonies, vertical, open | 2 named routes below width |
| 104256 | 4P, 24 colonies, vertical, open | 2 named routes below width |
| 104400 | 4P, 24 colonies, vertical, open | 2 named routes below width |

These are not published-map failures. The adapter validates in a temporary package, throws a bounded generation rejection, and deletes the package before it can be installed or reported as accepted. The exact-profile accounting regression is retained at `artifacts/rmg/phase-4d-blocking-topology-verification/tooling-smoke/native-rejection-accounting-exact-fixed.json`.

Reproduce the first horizontal case with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 1001053 -Players 4 -NeutralColonies 24 -Symmetry horizontal `
    -Archetype open -Topology mixed -MovementValidation both -Overwrite
```

Reproduce a vertical case by substituting its seed and `-Symmetry vertical`. The expected result is a clean rejection with a detailed native bottleneck diagnostic and no installed map.

## Representative bounded logical rejections

These seeds remain useful for proving that ordinary construction failure is not confused with a technical failure:

| Seed | Profile | Expected code |
| ---: | --- | --- |
| 4 | 2P, 10 colonies, horizontal, central-contest | `CHOKEPOINT_PLACEMENT` |
| 11 | 4P, 16 colonies, horizontal, open | `TOPOLOGY_ATTEMPTS_EXHAUSTED` |
| 13 | 4P, 16 colonies, rotational, central-contest | `COLONY_PLACEMENT` |

These are deterministic bounded outcomes. They must increment expected-rejection accounting, must not increment technical-failure accounting, and must not leave a playable package behind.

## Resolved generator defect classes

### Choke-shoulder exhaustion

Seeds 1000046, 100012, and 100024 exposed supported central-contest layouts with no legal choke shoulder after route overlap and strategic-region exclusions.

- 1000046: 4P, 24 colonies, vertical, central-contest.
- 100012 and 100024: 4P, 24 colonies, rotational, central-contest.
- Correction: adjust the affected authored route joins and provide a dedicated outer rotational shoulder; do not weaken choke distance, width, symmetry, strategic-region, or route-overlap rules.
- Regression: all three are retained in `Invoke-BlockingTopologyReferenceMatrix.ps1` and pass logical, package, native movement, and five-identity determinism checks.

Example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 1000046 -Players 4 -NeutralColonies 24 -Symmetry vertical `
    -Archetype central-contest -Topology mixed -MovementValidation both -Overwrite
```

### Exact actor-footprint route narrowing

Seeds 53, 1000877, 1001176, 1001178, 103990, 104240, 104740, 1700145, and 800065 exposed routes that satisfied the logical model but narrowed after exact starting-colony or neutral-colony footprint overlay in the native model.

- Correction: reserve and widen routes around exact actor footprints; expand symmetry-paired junctions deterministically; protect the central hub and declared choke masks.
- Regression: the focused seeds either produce a native-conformant accepted map or a deterministic bounded logical rejection. None can be published with an invalid native route.

### Dense open-profile fallback

Seeds 100488 and 104088 exposed a late-attempt open profile whose obstacle construction could not satisfy the required major-route envelope after exact widening.

- Correction: retain a deterministic compact-region fallback while preserving target density and the major-route contract.
- Current result: these configurations may end in `TOPOLOGY_ATTEMPTS_EXHAUSTED`; this is a safe rejection, not permission to narrow the route.

## Rejected diagnostic experiments

The following experiments were evaluated and deliberately not retained:

- forbidding all colony overlap with route reservations reduced placement success and did not address the exact-footprint defect;
- shrinking obstacle-region targets on later attempts reduced campaign acceptance without proving native width;
- weakening choke distance, symmetry, strategic-region, route-overlap, or native width checks would conceal contract violations; and
- treating package-native failures as accepted campaign results produced misleading accounting and was replaced by the three-outcome model.

## Durable regression coverage

`scripts/rmg/Invoke-BlockingTopologyReferenceMatrix.ps1` generates 15 supported profiles twice in independent `OpenRA.Utility` processes and compares:

1. logical SHA-256;
2. actor SHA-256;
3. graph SHA-256;
4. canonical `map.yaml` plus `map.bin` SHA-256; and
5. OpenRA UID SHA-1.

`scripts/rmg/Invoke-RmgV1Regression.ps1` separately freezes all five Version 1 identities for seed 424242. `scripts/rmg/Invoke-Phase4dVerification.ps1` enforces expected-rejection/technical-failure separation and prevents native-conformance rejections from being counted as accepted maps.

## Remaining risk, not blocker

There is no unresolved automated validity blocker. There is an efficiency and calibration risk: 4P/24/open maps, especially vertical symmetry, reject frequently, and the nine native-conformance seeds show that the logical generator can spend time materializing a candidate later rejected by exact native validation. A future improvement may move more exact-footprint reasoning into logical construction, but it must preserve the authoritative native gate.

Subjective route flow, swarm throughput, chokepoint feel, and visual density remain manual gameplay-calibration questions. They must not be reclassified as generator validity defects without a reproducible contract violation.
