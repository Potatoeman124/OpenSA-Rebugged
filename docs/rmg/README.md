# Random Map Generator

This directory contains the durable technical documentation for OpenSA's random map generator (RMG). Phase reports, generated catalogues, and other point-in-time evidence belong under `artifacts/rmg/`, which is intentionally excluded from version control.

## Project boundary

The repository contains only original source code, build tooling, and project documentation. Copyrighted game assets are not committed. Building, auditing, or running OpenSA obtains the required assets from an external installation supplied by the developer or player.

The RMG must preserve that boundary: it may refer to terrain and actor identifiers supported by the engine, but it must not copy original maps, art, audio, or other legally ambiguous asset material into the repository.

## Current map contract

A generated skirmish map must be loadable by the existing OpenSA map pipeline and provide:

- valid `map.yaml` and `map.bin` data;
- the `Lobby` and `Conquest` rule sets used by current skirmish maps;
- `Neutral`, `Creeps`, and sequential `MultiN` player definitions;
- exactly one `mpspawn` actor for every supported player slot;
- terrain and actor placement that satisfy native-cell bounds and passability checks; and
- the standard starting-unit, production, and victory behavior supplied by the existing rules.

Starting colonies are not serialized into reference maps. At runtime, `SpawnStartingUnits` creates the configured base actor at each `mpspawn`, so the generator's responsibility is to produce valid and fair spawn positions.

## Terrain model

The audited map corpus strongly favors a logical 2x2 macro-cell structure. That is a useful generation convention, not an engine guarantee: authored maps can and do contain partial or mixed native-cell regions. Generation should therefore be topology-first, materialize broad uniform regions on the 2x2 logical grid where practical, and perform final validation on native map cells.

Mixed terrain boundaries require transition-aware tiling rather than independent random tile selection. Validation must account for the existing ground movement costs: Clear is the baseline, Rock is passable at reduced speed, Vegetation is slower, and Water and Air are impassable to ground movement. Wasp movement traverses terrain independently, so movement-class validation must not assume ground connectivity proves connectivity for every unit type.

The frozen Generator Version 1 contract is intentionally Clear-only. Because Rock and Vegetation are traversable and Water/shoreline transitions are deferred, obstacle and vegetation densities resolve to zero for the first vertical slice.

Generator Version 2 is governed by the [Phase 4B terrain, mover, and blocking-topology contract](PHASE_4B_TERRAIN_MOVER_TOPOLOGY_CONTRACT.md), its [Phase 4C implementation record](PHASE_4C_BLOCKING_TOPOLOGY_IMPLEMENTATION.md), the historical [Phase 4D automated verification decision](PHASE_4D_BLOCKING_TOPOLOGY_VERIFICATION.md), and the current [combat-space safety contract](COMBAT_SPACE_SAFETY.md). Configuration version 3 selects homogeneous Water templates as the first real ground blocker, implements macro-aligned route/chokepoint geometry and mover-specific validation, and derives bidirectional colony turret and player production-path clearances from the active ruleset. The [Phase 5A NORMAL terrain-materialization contract](PHASE_5A_NORMAL_TERRAIN_MATERIALIZATION_CONTRACT.md) audits the deferred transition layer and freezes the requirements for an opt-in shoreline implementation. [Phase 5B](PHASE_5B_NORMAL_SHORELINE_MATERIALIZATION.md) implements that transition catalogue and native-intent-aware materializer as the separate opt-in Generator Version 3 path, including authored-corpus-calibrated sparse shoreline and open-Water detail. Generator Version 1 remains the default and is identity-stable.

## Movement validation architecture

OpenSA has two movement classes relevant to generated maps:

- `unit` is the gameplay-critical ground locomotor. Clear, Rock, and Vegetation have pathing costs 100, 133, and 200 respectively; omitted terrain types are impassable. Ground mobiles occupy one native cell and do not use a 3x3 movement footprint.
- `wasp` is a separate custom locomotor that permits Clear, Rock, Vegetation, Water, and Air and disables the normal domain passability check. It does not prove ground-map connectivity and is not the blocking-topology gate.

The Phase 4A native validator is implemented in `OpenRA.Mods.OpenSA/Rmg/NativeMovementValidator.cs`. It builds its ground graph from the reloaded map using `LocomotorInfo`, `Map.GetTerrainInfo`, native height transitions, and exact static `IOccupySpaceInfo`/`BuildingInfo` footprints. Building `x` and `X` cells block; `=` cells remain pathable; `+` cells are transit-only and remain traversable. It overlays the union of all five possible runtime starting-colony footprints at every `mpspawn`, then validates a shared start component, neutral-colony access, strategic graph reachability, and configured route width.

The previous 3x3 obstruction proxy remains part of the frozen Version 1 core and is reported beside the native result. It is a regression/debug layer, not the authoritative description of unit size. Both false-negative and false-positive cell classifications are counted, with representative native cells recorded in per-map reports.

This is the strongest safe utility-context validation supported by the pinned engine. A live `World` cannot be constructed from the mod utility assembly without changing the engine API: its constructor is internal and requires lobby, order-manager, renderer, player, and global game state. Bounded samples therefore exercise save/reload, rules and sequences initialization, the engine map-lint passes, player/spawn definitions, exact start footprints, UID/content hashes, and static movement semantics. Final interactive world initialization remains covered by launching the generated map with F5 or Ctrl+F5.

Generator Version 1 strategic edges are abstract node-to-node reachability promises. Route reservations do not yet constrain terrain, so usable route width is the maximum-bottleneck native path between the configured node regions. The blocking-topology contract must explicitly define corridor conformance before route reservations can become terrain constraints.

## Frozen MVP contract

Generator Version 1 is the deterministic 128x128 `NORMAL`-tileset vertical slice for two or four players. It supports horizontal reflection, vertical reflection, and 180-degree rotation, plus the `open` and `central-contest` archetypes. Its only terrain class is Clear; blocking terrain, rough-terrain variation, and chokepoint generation require a later contract revision.

The point-in-time Phase 2 specification and gate evidence are retained locally under `artifacts/rmg/phase-2-mvp-contract/` and intentionally ignored. Durable constraints and user-facing behavior are kept here and updated with the implementation.

## Reference maps and calibration roles

Use these maps as measurement references, not as source material to copy:

- `Two_Armies` for a compact two-player baseline;
- `Team_Battle` for a four-player/team baseline;
- `Suprise` for dense colony-placement pressure;
- `Narrow_Passage` for a future blocking-terrain/chokepoint contract, not Generator Version 1; and
- `skirmish` as a native-cell terrain-transition stress case and negative placement reference.

## Reproducible corpus audit

The read-only audit utility is implemented in `OpenRA.Mods.OpenSA/UtilityCommands/AuditRmgMapCorpusCommand.cs`. After the normal build pipeline has produced `OpenRA.Utility.exe`, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapCorpusAudit.ps1
```

By default, generated JSON is written to `artifacts/rmg/phase-1-existing-map-audit/map_audit/`. An alternative output path can be supplied with `-OutputDirectory`. Outputs are diagnostic evidence and must remain untracked.

## Documentation policy

Keep stable architecture, contracts, and decisions in this document and update them as the RMG evolves. Store temporary investigation notes, phase-gate reports, generated corpora, logs, and test captures under an appropriately named `artifacts/rmg/<phase-or-investigation>/` directory. Promote a finding into this document only when it becomes an ongoing project constraint or design decision.

## Generator Version 1 usage

Build and validate the project, then generate a map directly into the development user-map folder:

```powershell
.\build-pipeline.cmd validate
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 12345 -Players 2 -Symmetry horizontal -Archetype open -InstallForPlay -Overwrite
```

Start OpenSA with F5 or Ctrl+F5 and select `OpenSA RMG open 12345` from the skirmish map list. The wrapper writes the map to `%APPDATA%\OpenRA\maps\sa\{DEV_VERSION}\` and its validation report to the ignored Phase 3 artifact directory.

Supported switches are:

- `-Players 2` or `-Players 4`;
- `-Symmetry horizontal`, `vertical`, or `rotational`;
- `-Archetype open` or `central-contest`;
- an even `-NeutralColonies` value from 8 through 20 for two players, or a value from 12 through 24 divisible by four for four players;
- `-MovementValidation proxy`, `native`, or `both` (default); and
- any unsigned 64-bit `-Seed`.

Omit `-InstallForPlay` to generate into the ignored example directory instead. The utility refuses to overwrite an existing map unless `-Overwrite` is supplied.

Run the full deterministic test matrix with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGeneratorFuzz.ps1 -Overwrite
```

The default Phase 4A gate checks:

- 100 consecutive seeds for every default-count player/symmetry/archetype profile;
- 25 seeds for each legal-count coverage profile: 8, 10, 14, and 20 colonies for 2P, and 12, 16, 20, and 24 for 4P, crossed with every symmetry and archetype;
- 1000 mixed legal-count cases; and
- one repeated save/reload/lint/native sample per 100 cases.

This is 3400 deterministic generator cases and 34 repeated package samples. Accepted sample maps are deleted; aggregate evidence remains under the ignored Phase 4A artifact directory. Use `-RuntimeSampleRate 0` to disable package samples for a quick core-only run, or `-PreserveFailures` to retain a reproducible package when a sampled case fails.

## Generator Version 2 blocking-topology usage

Select Version 2 explicitly with `-Topology mixed`. For example, install a central-contest map with Water blockers and one symmetry orbit of route constrictions:

```powershell
.\build-pipeline.cmd validate
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 45004 -Players 2 -Symmetry horizontal `
    -Archetype central-contest -Topology mixed `
    -MovementValidation both -InstallForPlay -Overwrite
```

Start OpenSA with F5 or Ctrl+F5 and select `OpenSA RMG central-contest 45004` from the skirmish map list. Use `-Archetype open` for the low-blocker, major-route profile with no declared chokepoint. Omitting `-Topology mixed` continues to use Version 1.

Run focused positive/negative tests and the bounded 12-case Version 2 reference gate with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgSelfTests.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-BlockingTopologyReferenceMatrix.ps1
```

Phase 4D verified configuration version 2's topology and native-movement matrix, but subsequent live play found an unmodeled combat-space blocker. Configuration version 3 adds the [combat-space safety contract](COMBAT_SPACE_SAFETY.md); its automated evidence and completed [manual playtest corpus](PHASE_4D_MANUAL_PLAYTEST_SEEDS.md) supersede the earlier gameplay-readiness claim. On 2026-08-15, all five version 3 regression maps passed live spawn-safety validation with no match-start colony fire. The [failure and regression seed register](PHASE_4D_FAILURE_SEEDS.md) preserves the version 2 failures as regression history. Generated maps, reports, previews, and campaign corpora remain untracked.

## Generator Version 3 shoreline usage

Select Version 3 explicitly with `-Topology shoreline`. It retains Version 2 gameplay contracts while materializing audited NORMAL Water edges and corners, sparse shoreline vegetation, and sparse open-Water details:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 51001 -Players 2 -Symmetry horizontal -Archetype open -Topology shoreline -MovementValidation both -InstallForPlay -Overwrite
```

The map title includes `shoreline`, and reports are written beneath the ignored `artifacts/rmg/phase-5b-shoreline-materialization/` directory. Version 3 uses a 16-percent target for audited shoreline vegetation variants and an 8-percent target for fixed all-Water detail templates. Use `-Topology mixed` for the identity-stable homogeneous-Water Version 2 baseline. See the [Phase 5B implementation record](PHASE_5B_NORMAL_SHORELINE_MATERIALIZATION.md) for the transition catalogue, native-intent contract, automated evidence, and remaining manual gate.

## Current implementation boundary

Generator Version 1 implements deterministic settings, independent random streams, symmetry-aware starts, a strategic graph with two start-to-hub routes, widened route reservations, role-scored neutral colonies, symmetric Clear-template materialization, proxy and engine-grounded static movement validation, map lint, quality metrics, repeatability hashes, and repeated save/reload package validation. Its obstacle stage remains a validated zero-density no-op.

Generator Version 2 adds deterministic symmetric Water regions, named route masks, route-local chokepoints for `central-contest`, subtractive bounded repair, exact logical/native passability agreement, mover-specific validation, rule-derived combat-space validation, and durable debug layers. Homogeneous Water creates a known hard visual seam, and wasps intentionally bypass ground blockers. Configuration version 3 has passed manual spawn-safety validation and remains the frozen baseline. Phase 5A found no reusable engine autotiler, so opt-in Generator Version 3 selects fixed 2x2 shoreline stamps, validates per-frame native passability, and independently samples cosmetic symmetry partners. Its shoreline vegetation and open-Water detail densities are calibrated from authored NORMAL maps. Broader layout redesign, land decoration, Rock/Vegetation transitions, new archetypes, and UI work remain later phases.
