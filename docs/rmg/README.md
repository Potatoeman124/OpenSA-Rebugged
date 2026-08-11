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
- an even `-NeutralColonies` value from 8 through 20 for two players, or a value from 12 through 24 divisible by four for four players; and
- any unsigned 64-bit `-Seed`.

Omit `-InstallForPlay` to generate into the ignored example directory instead. The utility refuses to overwrite an existing map unless `-Overwrite` is supplied.

Run the full deterministic test matrix with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGeneratorFuzz.ps1 -Overwrite
```

This checks 100 consecutive seeds for every player-count, symmetry, and archetype profile, followed by 1000 mixed cases. Only failure details and aggregate metrics are retained; accepted fuzz maps are not saved.

## Current implementation boundary

Generator Version 1 now implements deterministic settings, independent random streams, symmetry-aware starts, a strategic graph with two start-to-hub routes, widened route reservations, role-scored neutral colonies, symmetric Clear-template materialization, hard validation, quality metrics, repeatability hashes, and save/reload package validation.

Its obstacle stage is deliberately a validated zero-density no-op. Terrain variety, blocking obstacles, transition tiles, and gameplay tuning follow only after the current package, editor, and skirmish gates pass.
