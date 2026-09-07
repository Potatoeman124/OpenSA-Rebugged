# Natural Landscape: Regions V11

Status (2026-09-07): implemented and built on `codex/rmg-reassessment-comparison` for the user's in-game test. The supported repository validation, focused contracts, paired probes, and fresh 15-map mechanical/terrain visual review passed. Live lobby interaction and actual unit production/movement remain unverified because the Windows computer-use runtime fails during initialization. This is not a claim of completed live gameplay acceptance.

## Player interface

Open `launch-game.cmd`, enter Skirmish, and open Random Map Generator. The panel now starts with:

| Setting | Default |
| --- | --- |
| Layout Family | Natural Landscape (Regions) |
| Terrain | Normal |
| Size | 256 x 256 |
| Players | 4 |
| Preset | Balanced |
| Terrain Complexity | Standard |
| Water Amount | Standard |
| Gravel and Moss Amount | Standard |
| Neutral Colony Density | Standard |
| Original surface relations | On |
| Seed | Fresh unsigned seed on panel initialization |

Generate Preview creates, validates, installs and selects a playable map. Changing controls or the seed makes the previous generated preview stale until regeneration. The normal lobby player/spawn controls remain the engine controls.

Terrain Complexity is an independent setting: Low uses a characteristic terrain scale of 112 native cells, Standard 64, High 36. These are construction scales, not fixed feature diameters. High produces more distributed, smaller features at comparable surface quantities.

Water Amount targets 16 / 20 / 24 percent of the map. Gravel and Moss Amount targets 10/5, 14/8, or 18/11 percent of non-water land. Native fixed-tile transition resolution can reduce achieved quantities; reports and lobby status show achieved surfaces. The previous Tactical Terrain setting controlled these slow surfaces, not doodad density.

The three UI presets now configure Regions explicitly. Balanced uses Standard complexity and amounts. Open Conflict uses Low complexity, water, gravel/moss and Sparse colonies. Tactical Crossroads uses High complexity and gravel/moss, Standard water, and Dense colonies. Presets preserve the selected supported size and player count and enable Original surface relations.

Battlefield Plan is hidden for Regions, since it has no compulsory strategic route graph. Unimplemented themes, sizes and plans are absent from menus. The player slider selects only 2 or 4. Structured Competitive and Artificial Battlefield remain available at 128 x 128 and retain their old generation paths; switching to them selects the supported size.

Neutral colonies appear as small grey squares (#A0A0A0) with black borders on the selected package's lobby preview. They are read from actual saved actors, including reduced-count maps. Player spawn markers and their interaction remain in the base MapPreview widget. Gold is not assigned to neutral colonies.

## Generation and applicable validation

Regions is the construction selected by the user from the retained 28-case Fields/Regions comparison. It generates water and geological regions before actor placement, then resolves them through the audited NORMAL fixed-tile catalogues. No terrain is cleared or repainted to fit a start or colony. There is one terrain attempt, a finite candidate list for actor placement, and no terrain ranking or hidden fallback to V10.

Starts use native runtime footprints for all five starting-base variants, including faction offsets. Starts are spread geographically among valid sites; this preference does not shape terrain or impose symmetry. Neutral colonies use their exact footprints, existing rule-derived combat separation, and a capacity-aware target. Failure to fit every neutral does not reject an otherwise valid map. Exact player count is mandatory.

Original surface relations On enforces Water-Dirt-Gravel-Moss adjacency and dirt-only structure coverage. Off relaxes the water/geology buffer and allows structures on passable gravel or moss; the available tile catalogue still governs materialization. Both modes forbid water-blocked footprints and preserve completed terrain.

The existing passable grass/mushroom variants remain. Doodads are added after placement at the fixed target of three per thousand native cells, spaced apart and kept away from reserved footprints/exits. Their target can fall short. There is no new doodad-density option, and doodads are not incorporated into global blocker analysis.

The independent saved-map validator checks exact native terrain semantics and heights, saved actors and owners, local footprints/overlaps, all faction production exits, the ground-speed contract (dirt 100%, gravel 75%, moss 50%), and applicable surface relations. Existing colony combat-space validation is retained.

Global ground accessibility, compulsory strategic routes, symmetry/fairness parity, and flying-unit availability are **NOT_REQUIRED** for Regions. Disconnected land and unreachable neutral objectives can remain. No component cap, dominant-lake rule, interior-water quota or global route repair is applied. Static validation does not establish live production, movement, faction accessibility or winnability.

## Settings identity and compatibility

Regions uses GeneratorVersion 11, `natural-regions`, and the `normal-natural-regions-v11[-256]` profile identities. Player-settings schema 5 uses `terrain_complexity` and `gravel_moss_amount`; the old `tactical_terrain` spelling is rejected in schema 5. Explicit Battlefield Plan or symmetry choices are rejected for Regions instead of silently pretending to implement them.

Schemas 1-4 retain their previous meanings, including Natural Landscape V10 for schemas 3/4. Legacy defaults on the low-level settings object remain unchanged; the lobby explicitly requests the new schema. Existing Fields/Regions comparison identities remain unchanged. The shared profile data class retains legacy fields, but Regions dispatches before all legacy generation and native connectivity validation.

A reproducible CLI equivalent is:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed '16029658737383046505'
```

This defaults to 256, four players and Standard controls. Optional parameters include `-MapSize`, `-Players`, `-TerrainComplexity`, `-WaterAmount`, `-GravelMossAmount`, `-NeutralColonyDensity`, `-OriginalSurfaceRelations` and `-VerifyRepeatability`. Existing player-settings wrappers remain available for historical replay.

## Verification and evidence

Evidence is retained under `artifacts/rmg/reassessment-comparison/`:

- `regions-final-validation.log`: supported `build-pipeline.cmd validate` passed, including Release/Debug compilation, static checks, YAML/maps, Lua and assets.
- `regions-self-tests.log`: V1-V10 tests plus Regions schema round trips, invalid-setting rejection, disconnected local sites, neutral capacity shortfall, unchanged terrain after placement, and negative native-map corruption/missing-actor tests passed.
- `regions-probes/`: 20 paired cases at both sizes. Each passed package and native checks plus deterministic repeat generation. Water controls responded; gravel/moss changes retained identical water; complexity changed spatial scale; On/Off and 2/4-player requests remained operational.
- `regions-final-01/`: the settings schedule and 15 fresh seeds were saved before generation. All 15 generated first attempt, without replacements. Actual package terrain matched retained construction surfaces, intent/template/priority exports were retained, and actual neutral/spawn counts matched preview extraction.
- `regions-final-01/review/`: all 15 whole maps and 60 fixed native texture windows were inspected. The contact sheets show actual terrain and package positions; they are not live lobby screenshots. Region scale, nesting, native transitions, and geography were accepted for the requested in-game evaluation. Broad open ground at Low complexity and edge-near starts remain visible characteristics; no competitive fairness claim is made.
- `regions-legacy-preservation/`: all 12 committed V7/V8 fixtures matched all five identities across two independent processes.
- `regions-v10-preservation/`: both stored V10 handoff maps reproduced all five identifiers exactly.
- `regions-benchmark/replay-*.log`: all 28 retained terrain comparisons reproduced exactly.

The older standalone V1 fixture script failed its old expected hashes. An isolated build of untouched HEAD `3d40e2dc28879fb29b96405f9af84af893762972` reproduced the same five V1 identifiers as this build, proving the mismatch predates Regions. Evidence: `head-baseline/v1-head-comparison.json`. The stale fixture was not silently rewritten.

Normal pipeline time in the fresh 15-map batch was 264-727 ms, with medians of 319 ms at 128 and 629 ms at 256. This excludes process startup, audit-only replay and texture export. Four sequential matched settings on this machine measured:

| Size / players | V10 pipeline | Regions pipeline |
| --- | ---: | ---: |
| 128 / 4 | 2.781 s | 0.330 s |
| 128 / 2 | 1.411 s | 0.264 s |
| 256 / 4 | 12.197 s | 0.657 s |
| 256 / 2 | 13.328 s | 0.454 s |

These are four measurements, not a universal timing guarantee. V10 and Regions enforce different approved gameplay contracts. Per-stage and process times are retained in `regions-benchmark/results.json`.

## Live test still required

The Windows computer-use kernel failed both initialization and the documented reset/retry with:
`windows sandbox failed: helper_unknown_error: apply deny-read ACLs`.
It never connected to the game. No Windows security settings were changed, and no automation failure was represented as a passed UI or gameplay test.

The user's in-game test should therefore exercise Generate Preview, a change of size/complexity and seed, grey neutral markers after map changes, player spawn selection, and local production/movement from chosen starting factions. Static rules and package checks passed; live UI rendering, spawn-marker interaction, unit emergence and actual movement need that confirmation.

Suggested reproducible Standard map: seed `16029658737383046505`, 256 x 256, four players, Standard controls, Original surface relations On. The acceptance corpus also contains both sizes, all complexity levels, both player counts and two Off cases.

No commit, merge or push has been performed as part of this delivery.
