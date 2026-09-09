# Random Map Generator

This directory contains the durable technical documentation for OpenSA's random map generator (RMG). Phase reports, generated catalogues, and other point-in-time evidence belong under `artifacts/rmg/`, which is intentionally excluded from version control.

## Project boundary

The repository contains only original source code, build tooling, and project documentation. Copyrighted game assets are not committed. Building, auditing, or running OpenSA obtains the required assets from an external installation supplied by the developer or player.

The RMG must preserve that boundary: it may refer to terrain and actor identifiers supported by the engine, but it must not copy original maps, art, audio, or other legally ambiguous asset material into the repository.

## Current development

[Artificial Battlefield V18](ARTIFICIAL_BATTLEFIELD_V18.md) is implemented on
`codex/rmg-artificial-battlefield`, pending user in-game review. It replaces the lobby's old Battlefield
with distributed colony plazas, connected ground routes and geometric terrain. Configuration 2 revises
the rejected four-reservoir plan: complexity subdivides seeded districts, Block Shape also changes route
corners, and Lane Width changes the protected ground corridor. All current biomes, quantities and
ownership features remain supported with 2/4/8 players.

Its base is [Natural Landscape PVP](NATURAL_LANDSCAPE_PVP.md), implementation `15f4882`, accepted by the
user on 2026-09-09. It has not yet been promoted to main. Ordinary Natural Landscape remains the default.
PVP uses V17/configuration 1/schema 11; the new Battlefield uses V18/configuration 2/schema 12. Older
schemas preserve historical generators. Structured Competitive remains retired from the lobby selector.
The linked documents record the agreed one-layout-at-a-time roadmap and validation evidence.

## Current integration checkpoint

The accepted checkpoint on `main` is **Regions V16 with all four biomes, 512 x 512 maps,
and named map saving**, configuration 1 / player-settings schema 10. The user accepted this
checkpoint and authorized merge/push on 2026-09-08. Implementation commits `55341a1`
and `71f2b51` were developed on `codex/rmg-512-map-size` from accepted main `5742b43`.

The [512 x 512 extension](NATURAL_REGIONS_512.md) adds an optional 512 size for 1-8 players
in all four Regions biomes, retaining the default 256 size and accepted smaller-map behavior.
[Named map saving](RMG_SAVED_MAPS.md) adds **Save Map...** beside Return to Skirmish,
prompting for a title and creating a persistent copy in Custom Maps. Native and live-world
validation passed for both additions before acceptance.

The preceding [Regions biome checkpoint](REGIONS_BIOME_EXTENSION.md) was accepted and
promoted on 2026-09-08. Implementation commit `bb699f3` was developed on
`codex/rmg-desert-swamp-candy` from main `bede09e`; checkpoint notes were recorded at `5742b43`.
The Terrain selector supports **Normal, Desert, Swamp and Candy** for Natural Landscape. All accepted
RMG options remain available; changing only the theme preserves terrain and actor geometry. Native
map validation and live-world checks passed for all four themes. Normal remains the default and
replays the accepted packages exactly.

The preceding V16 checkpoint, including Random ownership, was accepted and promoted on 2026-09-08.
Its implementation ends at `4bfcdad`, developed on `codex/rmg-eight-players-colony-weights`
from main `1143fad394ef845fd21ac986b93c3b322f14b25e`; checkpoint notes were recorded at `bede09e`.

[Regions V16: small maps and starting ownership](NATURAL_REGIONS_V16_STARTING_OWNERSHIP.md)
provides **64 x 64** maps, per-player starting colony shares, **0-100** ranges for both colony slider
groups, colored ownership previews, and **Ownership Choice: Closest to Spawn / Random**. Closest is
the default; Random uses the RMG seed and the same quotas. Both modes match the live preview. The
skirmish-start fix prevents repeated selection of a generated map from invalidating the host.
[Regions V15](NATURAL_REGIONS_V15_PLAYERS_AND_WEIGHTS.md), commit `7035e00`, supplies the 1-8 player
and species-weight foundation. 64 x 64 is limited to 1-4 players; 128/256 support 1-8.
Existing Closest V16 map contents and default settings remain exact.

The accepted three-level **Regions V12** checkpoint was merged and pushed to `main` at
`c34387a6f12a232418e80df6ba89d71fc8e13480` after user in-game acceptance on 2026-09-07.

The previous frozen main checkpoint was **Regions V14**, configuration 1 / player schema 8. The user confirmed
in-game acceptance and authorized its freeze and promotion to `main` on 2026-09-07. The implementation
is `8bbee8d36dc0472a49586055ceb0759e848f812a`, developed on `codex/rmg-extended-options`.
It preserves the accepted
V13 configuration-4 Terrain Complexity calibration (Ultra 2.70 broad / 2.60 fine), extends Water Amount,
**Surface Modifiers** (formerly Gravel and Moss Amount), and Neutral Colony Density with **Extreme / Ultra**,
and adds **Prevent Colony Overlapping**, enabled by default.

See [Regions V14 quantities and colony spacing](NATURAL_REGIONS_V14_QUANTITIES_AND_SPACING.md) for values,
placement behavior and verification. The panel retains NORMAL, 256 x 256, four players, Balanced, Medium
complexity, Standard quantities, Original Surface Relations On and grey neutral markers. The first
generated-map selection initializes Explored Map On and Fog of War Off; later regenerations preserve
user changes. With overlap prevention off, the generator fills neutral-colony shortfalls using the smallest
available turret-spacing overlaps while retaining physical clearance and all player-start protections.

Schema 12 selects V18 for explicit Artificial Battlefield.
Schema 11 and newer select V17 for Natural Landscape PVP and preserve V16 for ordinary Natural Landscape.
Schema 10 selects V16; schema 9 preserves V15; schema 8 preserves V14; schema 7 preserves [accepted V13 complexity](NATURAL_REGIONS_V13_EXTENDED_OPTIONS.md);
schema 6 replays [Regions V12](NATURAL_REGIONS_V12_CONTINUITY.md); schema 5 preserves
[Regions V11](NATURAL_REGIONS_V11.md). Schemas 1-4 retain their historical generator selections, including V10.
Earlier phase reports below are chronological records; their global connectivity and competitive fairness
rules do not govern Regions.

## Current map contract

A generated skirmish map must be loadable by the existing OpenSA map pipeline and provide:

- valid `map.yaml` and `map.bin` data;
- the `Lobby` and `Conquest` rule sets used by current skirmish maps;
- `Neutral`, `Creeps`, and sequential `MultiN` player definitions;
- exactly one `mpspawn` actor for every supported player slot;
- terrain and actor placement that satisfy native-cell bounds and passability checks; and
- the standard starting-unit, production, and victory behavior supplied by the existing rules.

Starting colonies are not serialized into reference maps. At runtime, `SpawnStartingUnits` creates the configured base actor at each `mpspawn`, so the generator must provide valid local spawn positions. Natural Regions does not impose cross-map connectivity or competitive fairness parity. Artificial
Battlefield V18 adds its own connected-access and terrain-cost parity checks.

## Terrain model

The audited map corpus strongly favors a logical 2x2 macro-cell structure. That is a useful generation convention, not an engine guarantee: authored maps can and do contain partial or mixed native-cell regions. Legacy generators construct strategic topology first. Regions constructs the terrain first, then places actors on completed surfaces. Both use native-cell validation after fixed-tile materialization.

Mixed terrain boundaries require transition-aware tiling rather than independent random tile selection. Validation must account for the existing ground movement costs: Clear is the baseline, Rock is passable at reduced speed, Vegetation is slower, and Water and Air are impassable to ground movement. Wasp movement traverses terrain independently, so movement-class validation must not assume ground connectivity proves connectivity for every unit type.

The frozen Generator Version 1 contract is intentionally Clear-only. Because Rock and Vegetation are traversable and Water/shoreline transitions are deferred, obstacle and vegetation densities resolve to zero for the first vertical slice.

Generator Version 2 is governed by the [Phase 4B terrain, mover, and blocking-topology contract](PHASE_4B_TERRAIN_MOVER_TOPOLOGY_CONTRACT.md), its [Phase 4C implementation record](PHASE_4C_BLOCKING_TOPOLOGY_IMPLEMENTATION.md), the historical [Phase 4D automated verification decision](PHASE_4D_BLOCKING_TOPOLOGY_VERIFICATION.md), and the current [combat-space safety contract](COMBAT_SPACE_SAFETY.md). Configuration version 3 selects homogeneous Water templates as the first real ground blocker, implements macro-aligned route/chokepoint geometry and mover-specific validation, and derives bidirectional colony turret and player production-path clearances from the active ruleset. The [Phase 5A NORMAL terrain-materialization contract](PHASE_5A_NORMAL_TERRAIN_MATERIALIZATION_CONTRACT.md) audits the deferred transition layer and freezes the requirements for an opt-in shoreline implementation. [Phase 5B](PHASE_5B_NORMAL_SHORELINE_MATERIALIZATION.md) implements that transition catalogue and native-intent-aware materializer as the separate opt-in Generator Version 3 path, including authored-corpus-calibrated sparse shoreline and open-Water detail. Generator Version 1 remains the default and is identity-stable.

The [Phase 6A NORMAL land-surface audit and contract](PHASE_6A_NORMAL_LAND_SURFACE_AUDIT.md) classifies Clear, Rock, and Vegetation template banks, measures authored-map density and clearance, and freezes the split between Clear-native cosmetic details and gameplay-affecting slow terrain. NORMAL land cover is a nested Clear-to-Rock-to-Vegetation stack; Rock and Vegetation impose 75-percent and 50-percent ground-speed movement, so their future implementation requires weighted-route validation.

[Phase 6B](PHASE_6B_CLEAR_LAND_DETAILS.md) implements the first opt-in land-surface slice as Generator Version 4. It inherits Version 3 topology, actors, routes, Water, and shoreline identity, then replaces an exact rounded four percent of eligible non-protected Clear stamps with runtime-verified Clear-native templates 61 and 62. The [manual Phase 6B corpus](PHASE_6B_MANUAL_PLAYTEST_SEEDS.md) reuses the accepted Phase 5B seeds for direct visual comparison.

[Phase 6C](PHASE_6C_WEIGHTED_LAND_COVER.md) implements opt-in Generator Version 5 with nested Clear-to-Rock-to-Vegetation fields, capacity-aware coverage targets, exact semantic symmetry, and native weighted-path parity. The full protected-Clear contract takes precedence over the nominal 14-percent Rock and 8-percent Vegetation targets. The [manual Phase 6C corpus](PHASE_6C_MANUAL_PLAYTEST_SEEDS.md) reuses the same eight comparison seeds and records the achieved coverage on each map.

The [player-facing random map parameters](PLAYER_FACING_RANDOM_MAP_PARAMETERS.md) separate meaningful gameplay choices from mandatory safety and fairness invariants. Phase 8A implements Water Amount and Tactical Terrain through the separate in-game RMG panel; deferred controls remain visibly disabled until their own generator contracts exist.

The [Phase 7A battlefield-layout audit](PHASE_7A_BATTLEFIELD_LAYOUT_AUDIT.md) covers all 100 campaign maps, 13 custom/challenge scenarios, 11 skirmish references, one unclassified shipped map, and the eight Phase 6 generated comparisons. It confirms that the prototype's Rock and Vegetation are almost entirely edge-biased while cosmetic detail is too sparse, and freezes the separation between role-aware movement terrain, passable cosmetic detail, and blocking decorations for Phase 7B.

[Phase 7B](PHASE_7B_ROLE_AWARE_BATTLEFIELD_LAYOUT.md) implements opt-in Generator Version 6. It classifies passable land as protected Clear, contest, primary-route, flank, or quiet space before materialization; prioritizes Rock and Vegetation according to those roles; and independently spreads passable cosmetic decoration across a 4x4 coverage grid. The [manual Phase 7B corpus](PHASE_7B_MANUAL_PLAYTEST_SEEDS.md) preserves the eight Phase 6 comparison seeds, including the empty-center regression seed `3100026` and the dense capacity case `3100047`.

[Phase 8A](PHASE_8A_PARAMETERIZED_BATTLEFIELD.md) adds opt-in Generator Version 7 and player-settings schema version 2. Water Amount selects Low/Standard/High deterministic obstacle-density targets; Tactical Terrain selects Low/Standard/High Rock, Vegetation, and tactical-anchor targets. Both controls preserve hard safety gates, report requested versus achieved outcomes, and remain compatible with schema-version-1 documents normalized to frozen Version 6.

The [Phase 8B Water-morphology and layout-algorithm audit](PHASE_8B_WATER_MORPHOLOGY_AND_LAYOUT_ALGORITHM_AUDIT.md) compares all 125 shipped maps with 14 Version 7 examples. It confirms that Version 7 reaches its Water-area target but fragments that budget into many small bodies. The durable taxonomy is Natural Landscape, Structured Competitive, and Artificial Battlefield, with theme independent. Later visual review corrected the implementation mapping: Version 7 is Artificial Battlefield, Version 8 is Structured Competitive, and Natural Landscape remains planned.

[Phase 8C](PHASE_8C_STRUCTURED_COMPETITIVE_COHERENT_WATER.md) implements Generator Version 8 and player-settings schema version 3 with corrected terminology. Structured Competitive uses coherent Water fields while retaining the route-first exact-symmetry scaffold; Artificial Battlefield is frozen Version 7; Natural Landscape is a non-generating planned option. The [paired manual corpus](PHASE_8C_LAYOUT_FAMILY_MANUAL_PLAYTEST_SEEDS.md) compares both implemented families under identical settings.

[Phase 9.0](PHASE_9_0_V7_V8_PRESERVATION_BASELINE.md) freezes that corrected V7/V8 state before Natural Landscape V9 work. Its committed fixture records twelve representative five-hash identities, and the preservation wrapper regenerates every case in two independent processes while proving that Natural Landscape cannot fall back to V7 or V8.

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

The map title includes `shoreline`, and reports are written beneath the ignored `artifacts/rmg/phase-5b-shoreline-materialization/` directory. Version 3 uses a 16-percent target for audited shoreline vegetation variants and an 8-percent target for fixed all-Water detail templates. Use `-Topology mixed` for the identity-stable homogeneous-Water Version 2 baseline. See the [Phase 5B implementation record](PHASE_5B_NORMAL_SHORELINE_MATERIALIZATION.md) for the transition catalogue, native-intent contract, automated evidence, and completed manual gate.

## Generator Version 4 Clear-detail usage

Select Version 4 explicitly with `-Topology land-details`. It inherits the Version 3 map and adds only sparse Clear-native stone details outside every protected start, colony, route, strategic, chokepoint, and repair layer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 610001 -Players 2 -Symmetry rotational -Archetype open -Topology land-details -MovementValidation both -InstallForPlay -Overwrite
```

The title includes `land-details`, and reports are written beneath ignored `artifacts/rmg/phase-6b-clear-land-details/`. The target is the exact rounded four percent of eligible Clear stamps, and the two symmetry-side counts differ by at most one. See the [Phase 6B implementation record](PHASE_6B_CLEAR_LAND_DETAILS.md) and [manual corpus](PHASE_6B_MANUAL_PLAYTEST_SEEDS.md).

## Generator Version 5 weighted-land-cover usage

Select Version 5 explicitly with `-Topology land-cover`. It inherits Version 4 and adds capacity-aware Rock and Vegetation fields outside every protected Clear layer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 3100000 -Players 2 -NeutralColonies 8 -Symmetry horizontal -Archetype open -Topology land-cover -MovementValidation both -InstallForPlay -Overwrite
```

The title includes `land-cover`, and reports are written beneath ignored `artifacts/rmg/phase-6c-weighted-land-cover/`. See the [Phase 6C implementation record](PHASE_6C_WEIGHTED_LAND_COVER.md) and [manual corpus](PHASE_6C_MANUAL_PLAYTEST_SEEDS.md).

## Generator Version 6 battlefield-layout usage

Select Version 6 explicitly with `-Topology battlefield-layout`. It inherits Version 5 and adds semantic battlefield roles, role-prioritized slow terrain, capacity-aware tactical anchors, terrain-specific passable doodads, and a seamless moss-detail policy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 -Seed 3100026 -Players 4 -NeutralColonies 12 -Symmetry vertical -Archetype open -Topology battlefield-layout -MovementValidation both -InstallForPlay -Overwrite
```

Install the complete eight-map comparison corpus with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Prepare-Phase7bManualCorpus.ps1 -InstallForPlay -ReplaceInstalledRmgCorpus -Overwrite
```

The replacement option deletes only matching `OpenSA-RMG-*.oramap` files from the OpenRA development user-map directory. See the [Phase 7B implementation contract](PHASE_7B_ROLE_AWARE_BATTLEFIELD_LAYOUT.md) and [manual playtest corpus](PHASE_7B_MANUAL_PLAYTEST_SEEDS.md).

## Player-settings and Version 7 usage

The player-facing wrapper now creates schema-version-2 settings and selects Generator Version 7:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-PlayerMapGenerator.ps1 `
    -Seed 8100001 -Preset balanced -Players 2 -Symmetry automatic `
    -WaterAmount high -TacticalTerrain low -InstallForPlay -Overwrite
```

Presets are `balanced`, `open-conflict`, and `tactical-crossroads`. Optional overrides include `-Layout`, `-NeutralColonyDensity`, `-WaterAmount`, `-TacticalTerrain`, and explicit symmetry. Existing schema-version-1 documents remain accepted and normalize to frozen Version 6; schema version 2 normalizes to Version 7 `parameterized-battlefield`.

For low-level regression work, select Version 7 explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 8100001 -Players 2 -NeutralColonies 10 -Symmetry rotational `
    -Archetype central-contest -Topology parameterized-battlefield `
    -WaterAmount high -TacticalTerrain low -MovementValidation both `
    -InstallForPlay -Overwrite
```

The generated settings, reports, and examples remain ignored under `artifacts/rmg/phase-8a-parameterized-battlefield/`. Reports record requested, normalized, effective-target, and achieved values. See the [Phase 8A contract](PHASE_8A_PARAMETERIZED_BATTLEFIELD.md) and [nine-map manual corpus](PHASE_8A_MANUAL_PLAYTEST_SEEDS.md); [Phase 7C](PHASE_7C_PLAYER_SETTINGS_CONTRACT.md) remains the schema-version-1 compatibility contract.
## Current implementation boundary

Generator Version 1 implements deterministic settings, independent random streams, symmetry-aware starts, a strategic graph with two start-to-hub routes, widened route reservations, role-scored neutral colonies, symmetric Clear-template materialization, proxy and engine-grounded static movement validation, map lint, quality metrics, repeatability hashes, and repeated save/reload package validation. Its obstacle stage remains a validated zero-density no-op.

Generator Version 2 adds deterministic symmetric Water regions, named route masks, route-local chokepoints for `central-contest`, subtractive bounded repair, exact logical/native passability agreement, mover-specific validation, rule-derived combat-space validation, and durable debug layers. Homogeneous Water creates a known hard visual seam, and wasps intentionally bypass ground blockers. Configuration version 3 has passed manual spawn-safety validation and remains the frozen baseline. Phase 5A found no reusable engine autotiler, so opt-in Generator Version 3 selects fixed 2x2 shoreline stamps, validates per-frame native passability, and independently samples cosmetic symmetry partners. Its shoreline vegetation and open-Water detail densities are calibrated from authored NORMAL maps. Broader layout redesign, land decoration, Rock/Vegetation transitions, new archetypes, and UI work remain later phases.

Phase 6A found authored NORMAL land-relative medians of 13.8055 percent Rock and 8.3118 percent Vegetation, while Clear-native fixed details appear on 4.3060 percent of homogeneous Clear stamps. Generator Version 4 implements the safe cosmetic slice with templates `61` and `62`. Generator Version 5 implements symmetric nested Rock/Vegetation morphology, capacity-aware 14/8-percent targets, runtime-verified native semantics, and exact weighted-path parity. It remains the frozen pre-layout prototype.

Phase 7A quantifies the placement problem across the complete shipped corpus and the accepted generated comparison set. Generator Version 6 now creates symmetry-closed battlefield roles before terrain materialization, allocates Rock and Vegetation to declared tactical roles instead of borders, and broadens terrain-specific cosmetic coverage independently. Its automated and refreshed eight-map manual gates are accepted. Campaign and scripted scenario maps remain spatial and visual calibration sources; their runtime traffic cannot be inferred from static actors alone.

Phase 7B maps soil to flowers and high grass, gravel to brown mushrooms, and moss to red mushrooms. High grass and mushrooms use RMG-only passable aliases with the stock artwork, preserving official-map blocking behavior while preventing generated-route narrowing. Version 6 also rejects square-edged moss detail template `93` and uses seamless interior template `78`.

Phase 7C adds strict JSON schema version 1 and player-facing presets over Version 6. Unsupported fields remain rejected, and schema-version-1 normalization is preserved unchanged.

Phase 8A adds schema version 2 and opt-in Generator Version 7. The separate in-game RMG panel, modal progress state, preview, and map-cache refresh are integrated. Water Amount and Tactical Terrain are functional Low/Standard/High controls with requested-versus-achieved diagnostics; Chokepoints and Amount of Hostiles remain disabled placeholders.

Phase 8B adds Water-component morphology to the complete-corpus audit. Phase 8C adds a first-class Layout Family control and Version 8 coherent Water morphology. Visual review corrected the labels: Version 8 is Structured Competitive, Version 7 is Artificial Battlefield, and Natural Landscape remains a disabled placeholder until a terrain-first organic generator exists.

## Natural Landscape V9 checkpoint and V10 visual gate

Generator Version 9 makes Natural Landscape playable and reliable, including adaptive colony placement, water-preserving routes, Original surface relations, package validation, and native movement validation. Its output remains an experimental baseline: visual review rejected its coarse orthogonal boundaries, repair-like surface envelopes, and repeated composition.

V10 therefore begins with an offline feedback loop before player-facing promotion. Run `scripts/rmg/Invoke-RmgVisualReviewLoop.ps1` to generate actual map packages, extract their packaged previews, render topology diagnostics and contact sheets, and create an explicit per-map review worksheet. Run `scripts/rmg/Test-RmgVisualReviewGate.ps1` after inspection; it blocks incomplete and rejected batches. On the V10 feature branch, the Natural Landscape selection routes to the experimental V10 profile only after that loop achieved one uninterrupted 15-map mechanical and visual pass. See [Natural Landscape V10 visual development contract](NATURAL_V10_VISUAL_DEVELOPMENT_CONTRACT.md).
