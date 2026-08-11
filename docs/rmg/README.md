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

## Reference maps and initial presets

The first implementation should use these maps as behavioral references, not as source material to copy:

- `Two_Armies` for a compact two-player baseline;
- `Team_Battle` for a four-player/team baseline;
- `Narrow_Passage` for choke-point topology;
- `Suprise` for dense or irregular terrain pressure; and
- the remaining skirmish-reference maps for terrain-transition and validation stress cases.

## Reproducible corpus audit

The read-only audit utility is implemented in `OpenRA.Mods.OpenSA/UtilityCommands/AuditRmgMapCorpusCommand.cs`. After the normal build pipeline has produced `OpenRA.Utility.exe`, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapCorpusAudit.ps1
```

By default, generated JSON is written to `artifacts/rmg/phase-1-existing-map-audit/map_audit/`. An alternative output path can be supplied with `-OutputDirectory`. Outputs are diagnostic evidence and must remain untracked.

## Documentation policy

Keep stable architecture, contracts, and decisions in this document and update them as the RMG evolves. Store temporary investigation notes, phase-gate reports, generated corpora, logs, and test captures under an appropriately named `artifacts/rmg/<phase-or-investigation>/` directory. Promote a finding into this document only when it becomes an ongoing project constraint or design decision.

The next implementation phase should turn this contract into a minimal generator architecture and deterministic validator before adding terrain variety or gameplay tuning.
