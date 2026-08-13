# Phase 4D manual gameplay-calibration seeds

The Phase 4D automated gate is complete. This shortlist is the first live gameplay pass for Generator Version 2. It emphasizes distinct route shapes, boundary colony counts, central chokepoints, dense four-player construction, and retry-heavy accepted maps.

Generated packages are installed from repository code plus the developer's external original-game assets. No copyrighted assets or generated packages belong in Git.

## Before testing

From the `OpenSA-Rebugged` repository root:

```powershell
.\build-pipeline.cmd validate
```

For each row below, run its generation command, then press F5 or Ctrl+F5 in VS Code. Open Skirmish and select the map named `OpenSA RMG <archetype> <seed>`.

## Primary shortlist

| Priority | Seed | Profile | Why it is included |
| ---: | ---: | --- | --- |
| 1 | 1000001 | 2P, 8 colonies, horizontal, open | Small baseline; broad normal/major routes |
| 2 | 1000076 | 2P, 8 colonies, vertical, central-contest | Small exact-chokepoint baseline |
| 3 | 1000551 | 2P, 20 colonies, rotational, open | Maximum 2P colony pressure |
| 4 | 1000601 | 4P, 12 colonies, horizontal, open | Minimum 4P colony count and four-start flow |
| 5 | 1001054 | 4P, 24 colonies, horizontal, open | Dense boundary profile; accepted after 3 retries |
| 6 | 1001106 | 4P, 24 colonies, vertical, open | Accepted example from the least-successful construction profile |
| 7 | 1001134 | 4P, 24 colonies, vertical, central-contest | Dense exact-choke case; accepted after 3 retries |
| 8 | 101368 | 4P, 24 colonies, rotational, central-contest | Dense rotational central-hub case |

The following commands install the exact accepted maps:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 1000001 -Players 2 -NeutralColonies 8  -Symmetry horizontal -Archetype open            -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 1000076 -Players 2 -NeutralColonies 8  -Symmetry vertical   -Archetype central-contest -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 1000551 -Players 2 -NeutralColonies 20 -Symmetry rotational -Archetype open            -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 1000601 -Players 4 -NeutralColonies 12 -Symmetry horizontal -Archetype open            -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 1001054 -Players 4 -NeutralColonies 24 -Symmetry horizontal -Archetype open            -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 1001106 -Players 4 -NeutralColonies 24 -Symmetry vertical   -Archetype open            -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 1001134 -Players 4 -NeutralColonies 24 -Symmetry vertical   -Archetype central-contest -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 101368  -Players 4 -NeutralColonies 24 -Symmetry rotational -Archetype central-contest -Topology mixed -MovementValidation both -InstallForPlay -Overwrite
```

## Per-map checklist

Record pass/fail plus a short observation for each item:

1. The map appears in the map browser and enters a live match without an exception.
2. Every player slot receives exactly one starting colony at the intended spawn.
3. The requested number of neutral colonies appears; no colony is in Water or visibly clipped into a blocker.
4. Ground units can leave every starting colony in at least two useful directions.
5. Ground units can reach every neutral colony without becoming trapped beside its footprint.
6. Each declared route is practically usable by a group, not only by a single unit.
7. On `central-contest`, the exact chokepoint is traversable and strategically recognizable.
8. On `open`, the major route feels materially broader than a central-contest choke.
9. Unit groups do not exhibit persistent oscillation, single-file deadlock, or asymmetric path selection at mirrored/rotated geometry.
10. Water blockers and route openings are visually understandable using the current homogeneous seam presentation.
11. The symmetry feels fair in travel time and access even when the terrain is not aesthetically varied.

## Useful stress actions

- Order the starting force across the center, then back through the alternate route.
- Send multiple groups through the same constriction in opposite directions.
- Rally newly produced units across the central route for several minutes.
- Capture or contest the closest, central, and farthest neutral colonies.
- In four-player maps, issue simultaneous cross-map orders from all start areas.

## Reporting a blocker

Keep the exact seed and settings. Include whether the problem is a load failure, unreachable start/colony, route-width violation, unit deadlock, symmetry/fairness concern, or subjective tuning concern. A screenshot and the generated JSON report are useful; generated packages and original-game assets should remain untracked.

Technical contract violations block progression. Subjective density, route feel, and swarm throughput are calibration findings unless they expose an actual reachability, width, footprint, determinism, or runtime-load failure.
