# Phase 4C Blocking Topology Implementation

## Status

Generator Version 2 implements deterministic blocking topology for the frozen `NORMAL`-tileset contract. The `mixed` topology preset materializes logical `BLOCKED` cells as homogeneous Water macro-cells and keeps required ground routes, starts, colony exits, and strategic regions open. Generator Version 1 remains the default and its generated map identity is unchanged.

`BLOCKING TOPOLOGY IMPLEMENTED — READY FOR CAMPAIGN VALIDATION`

This status means the implementation, focused negative tests, deterministic reference matrix, save/reload, map lint, and engine-grounded static movement gate pass. Phase 4D still owns broad seed fuzzing and gameplay calibration. A human should launch representative Version 2 maps before campaign promotion because the utility assembly cannot construct a live `World` without engine API changes.

## Architecture

The implementation remains topology-first:

1. Create symmetric starts and the strategic graph.
2. Reserve per-edge route masks and endpoint strategic regions.
3. For `central-contest`, place one symmetry orbit of declared `ROUTE_CONSTRICTION` chokepoints.
4. Place colonies and reserve their exact logical clearance regions.
5. Generate symmetric Water obstacle regions only in eligible cells.
6. Apply bounded subtractive repairs (`BLOCKED -> OPEN`).
7. Assign regions and select symmetric terrain-template variants.
8. Materialize the 64x64 logical grid into uniform 2x2 native macro-cells.
9. Save, reload, lint, and validate exact native movement and actor footprints.

The Version 2 path is dispatched before the frozen Version 1 implementation. New semantic arrays retain the authoritative route mask, obstacle-region ID, chokepoint ID, strategic-region membership, and repair-change state for every logical cell. These fields are included in Version 2 identity hashes and diagnostic output; they do not alter Version 1 canonical settings or hashes.

### Source paths

- `OpenRA.Mods.OpenSA/Rmg/RmgGenerator.cs`: version dispatch, shared deterministic core, focused tests, and V1 behavior.
- `OpenRA.Mods.OpenSA/Rmg/RmgBlockingTopologyGenerator.cs`: V2 routes, blockers, chokepoints, repair, validation, hashes, metrics, and debug layers.
- `OpenRA.Mods.OpenSA/Rmg/RmgModels.cs`: topology preset, Version 2 profile fields, semantic layers, and records.
- `OpenRA.Mods.OpenSA/Rmg/OpenRaRmgMapAdapter.cs`: terrain-template checks, 2x2 materialization, save/reload agreement, and JSON report.
- `OpenRA.Mods.OpenSA/Rmg/NativeMovementValidator.cs`: engine-grounded native movement, exact actor footprints, route/choke widths, colony exits, and mover drift checks.
- `OpenRA.Mods.OpenSA/UtilityCommands/GenerateRmgMapCommand.cs`: `--topology off|mixed` generation interface.
- `OpenRA.Mods.OpenSA/UtilityCommands/FuzzRmgMapGeneratorCommand.cs`: topology-aware reproduction metadata for Phase 4D.
- `OpenRA.Mods.OpenSA/UtilityCommands/ValidateRmgGeneratorCommand.cs`: focused V1, V2, and native-validator self-test entry point.
- `mods/sa/rmg/normal-water-blocking-v2.yaml`: frozen Version 2 configuration.
- `scripts/rmg/Invoke-MapGenerator.ps1`: developer generation wrapper.
- `scripts/rmg/Invoke-RmgSelfTests.ps1`: focused test wrapper.
- `scripts/rmg/Invoke-BlockingTopologyReferenceMatrix.ps1`: bounded deterministic Phase 4C reference gate.

## Version and configuration contract

The public topology presets are:

- `off`: Generator Version 1 and profile `normal-clear-v1`; this remains the default.
- `mixed`: Generator Version 2 and profile `normal-water-blocking-v2`.

The command rejects a topology/version mismatch. Version 2 freezes the following relevant values:

- logical/native size: 64x64 / 128x128;
- normal route: effective native width at least 5;
- open-archetype major route: effective native width at least 9;
- declared choke: nominal 4 native cells and effective width exactly 3;
- obstacle density: open target 12%, accepted 10-14%; central target 16%, accepted 14-18%;
- obstacle-region size: 8-64 logical cells;
- topology attempts: at most 4;
- repairs: at most 8 operations and 64 changed logical cells.

No independent obstacle-density or chokepoint-frequency tuning switches were added. `mixed + open` resolves to low blockers and no declared choke. `mixed + central-contest` resolves to moderate blockers and exactly one symmetry orbit (two materialized segments) of route constrictions.

## Materialization

The first blocking material is Water from the existing externally supplied `NORMAL` tileset. Version 2 accepts only allow-listed templates that the engine resolves as complete homogeneous 2x2 macro-cells:

- Clear template IDs: 32, 33, 34, 35, 36;
- Water template IDs: 9, 12, 26, 28, 29.

Each logical cell materializes to four native cells with height zero. Reload validation requires every `OPEN` macro-cell to resolve as Clear and every `BLOCKED` macro-cell to resolve as Water. This deliberately creates a hard visual seam; transition-aware shoreline tiling remains out of scope and is reported as incomplete rather than disguised as finished artwork.

Ground units using the `unit` locomotor treat Water as impassable. The custom `wasp` locomotor intentionally traverses Water and Air. Its rule data is validated separately so the implementation does not misreport ground chokepoints as universal mover barriers.

## Routes and chokepoints

Every strategic graph edge owns a named route mask. Native validation constrains each edge to its own corridor plus endpoint access regions; a global terrain detour cannot satisfy a named edge.

Open maps use major routes and reject widths below effective native width 9. Central-contest maps use normal routes and exactly one symmetry orbit of `ROUTE_CONSTRICTION` segments. Each declared choke records its route and orbit IDs, nominal length, and intended width. The native gate verifies:

- exact Water/Clear materialization;
- nominal aperture and effective aperture width 3;
- effective width 3 between the immediate route shoulders;
- removal of the aperture disconnects those shoulders;
- the full named edge remains traversable;
- non-choked routes remain at least width 5;
- start, colony, and inter-choke distances;
- one symmetry-equivalent partner; and
- a strategically meaningful alternate graph route remains.

Route junction expansion cannot add a choked route around its Water walls, and a post-expansion semantic cross-section preserves the declared aperture as that route's only crossing through the choke band. This affects only route intent; global ground terrain retains the alternate strategic route required by the contract.

## Repair behavior

Repairs are deterministic, symmetric, and subtractive only. The repair stage clears blockers that overlap, in order:

1. a reserved route;
2. a start buffer;
3. a colony exit/clearance;
4. a strategic region.

Every operation records its type, reason, target, and changed logical cells. The repair layer participates in Version 2 hashes and debug output. Exceeding either frozen budget rejects generation with an error; it cannot silently accept a more heavily repaired map. Focused tests prove one valid route repair is applied and recorded, and an intentional 65-cell repair request is rejected.

## Validation and diagnostics

The logical validator checks semantic/template equivalence, blocker symmetry, reservation exclusion, obstacle-region size/separation, one connected terrain-only open component, start and colony counts, route exits, width classes, choke count/geometry, density, and repair budgets.

After save/reload, the native validator uses the configured OpenRA `LocomotorInfo`, exact `IOccupySpaceInfo`/`BuildingInfo` footprints, resolved starting-colony alternatives, neutral colonies, and actual production `ExitCell` declarations. It reports:

- passable, static-blocked, transit-only, and starting-colony-blocked native cells;
- reachable starts and neutral colonies;
- named-route traversability and effective width;
- chokepoint aperture, shoulder, distance, separation, and route-local separator evidence;
- start escape sectors at radius 12;
- logical/materialized terrain disagreement cells;
- production-exit failures;
- wasp mover-contract status; and
- legacy proxy false-positive/false-negative cells.

JSON debug layers include topology, union route/strategic regions, every named route, start/colony clearances, chokepoints, and repaired cells. Native disagreement samples identify passability conflicts by cell and reason.

Run the focused positive and negative tests with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgSelfTests.ps1
```

They cover deterministic layouts and hashes, symmetry transforms, invalid terrain rejection, valid and over-budget repair behavior, deliberate disconnection, unreachable colony access, narrow-route rejection, and reproducible proxy/native disagreement reporting.

## CLI usage

Build, generate a Version 2 map into the ignored artifact directory, and validate both movement layers:

```powershell
.\build-pipeline.cmd validate
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 45004 -Players 2 -Symmetry horizontal `
    -Archetype central-contest -Topology mixed `
    -MovementValidation both -Overwrite
```

Install a generated map for interactive testing:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 45004 -Players 2 -Symmetry horizontal `
    -Archetype central-contest -Topology mixed `
    -MovementValidation both -InstallForPlay -Overwrite
```

Start OpenSA with F5 or Ctrl+F5 and select `OpenSA RMG central-contest 45004` from the skirmish map list. Omit `-Topology mixed` to use the unchanged Version 1 Clear-only generator.

Run the bounded Phase 4C reference gate with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-BlockingTopologyReferenceMatrix.ps1
```

Generated maps, reports, summaries, and debug layers are written under `artifacts/rmg/phase-4c-blocking-topology/` and remain untracked under the documentation policy.

## Determinism results

The Phase 4C reference matrix covers 2P and 4P, `open` and `central-contest`, and horizontal, vertical, and rotational symmetry. It uses 12 primary cases and 12 repeat generations. All 24 packages passed generation, save/reload, rules/sequences initialization, map lint, and native validation. All repeats matched:

- logical SHA-256;
- actor SHA-256;
- graph SHA-256;
- canonical `map.yaml` + `map.bin` SHA-256; and
- OpenRA UID SHA-1.

Observed obstacle densities were 11.5723-11.9629% for open maps and 15.7715-15.9668% for central-contest maps. Open maps contained no declared chokepoints and had minimum required-route width 13 in this set. Every central map contained exactly two symmetry-related choke segments and measured minimum required-route width 3. No logical/materialized passability disagreement or production-exit failure was accepted.

The frozen V1 regression case (seed 424242, 2P, horizontal, open) matches its pre-Phase-4C logical, actor, graph, canonical map-data, and engine UID identities exactly.

## Known limitations

- The first Water materialization has a macro-aligned hard seam. Visual shoreline transitions require a later, separately contracted layer.
- Wasps intentionally bypass Water; Phase 4C validates that behavior but does not change faction balance.
- Static validation does not model mobile congestion, temporary blockers, crushing, lane selection, or large-swarm throughput.
- A utility command cannot instantiate a live `World` because the pinned engine constructor is internal and requires live lobby, order manager, renderer, player, and global game state. Interactive launch and match play remain a human gate.
- The 12-case set is an implementation reference, not broad seed coverage. Do not treat it as Phase 4D campaign evidence.
- Density above 18%, region-gate/maze archetypes, mixed shoreline transitions, and UI work remain out of scope.
