# Natural Landscape reassessment and comparison plan

Date: 2026-09-07
Status: approved for staged implementation. The user selected Regions after comparison review on 2026-09-07.
Inspected baseline: main at 3d40e2dc28879fb29b96405f9af84af893762972.

This document records the user's clarified direction separately from implementation proposals. It scopes the comparison that should precede a new Natural Landscape implementation. Existing V1-V10 contracts continue to describe historical behavior; they are not automatically requirements for the next Natural generator.

## 1. Direction established by the user

- Terrain appearance and gameplay are strongly coupled in this game. Judge terrain composition as gameplay design expressed visually. A convincing map is strong evidence of a promising playing experience; exact engine semantics and local actor validity still need verification.
- Terrain Complexity is a first-class control over meaningful spatial variation, including the frequency of surface changes at gameplay scale. Its effect must be recognizable when terrain quantities are held fixed.
- Water Amount controls quantity. Natural maps may contain several small lakes with no dominant body, concentrated features, dispersed features, broad plains, or combinations. No universal feature hierarchy, dominant lake, competitive symmetry, or fairness requirement applies.
- Cross-map accessibility is not a requirement for this iteration. Disconnected land, starts on separate land components, and colonies without a ground connection to a player are permitted. Do not require flying-unit availability as a substitute. Future analysis may relate land paths to access to flying units; that work is deferred.
- Original surface relations remains optional. When enabled, preserve the Water-Dirt-Gravel-Moss adjacency chain and fit entire start/colony footprints on existing dirt. Final actor placement must not repaint terrain.
- Neutral-colony quantity can decrease when valid placement capacity is insufficient.
- Keep the current passable RMG grass/mushroom variants. Their blockers are excluded from the comparison. This does not change stock actor definitions or reinterpret stock blocking actors as passable.
- Reproducibility and generation cost matter. Additional candidate search is not an assumed entitlement.
- The user selected Regions after reviewing the 28-case comparison on 2026-09-07. Regions is the basis for the next Natural generator; Fields remains comparison evidence.
- This version must show actual neutral-colony placements on the lobby map preview alongside player starts. Markers must follow the selected saved map and adaptive placed count. Neutral markers are grey squares, matching the neutral-colony visual convention; gold remains available for player colours.
- A future option will allow players to own multiple colonies at game start. Record this as future work: colony previews will need to distinguish neutral colonies from player-owned colonies using ownership information. It is not part of the current version.

The user confirmed retaining passable RMG doodads after their stock/RMG distinction was explained. No doodad-behavior decision remains pending.

## 2. Tactical Terrain: verified behavior and naming proposal

Current Natural V10 uses Tactical Terrain to set surface-area targets relative to non-Water native land:

| Current level | Gravel / Rock | Moss / Vegetation |
| --- | ---: | ---: |
| Low | 10% | 5% |
| Standard | 14% | 8% |
| High | 18% | 11% |

Sources: RmgModels.RockLandPercentFor / VegetationLandPercentFor, RmgLandCoverMaterializer.Materialize, and both normal-natural-landscape-v10 profiles.

V7/V8 also use this setting for tactical slow-terrain anchors. That stage is gated by UsesBattlefieldLayout and does not run for V10. The five Natural morphology families are not changed by this setting.

Doodad actor count uses the independent fixed profile value LandDecorationPerThousand = 3. Its distribution among dirt/gravel/moss follows the generated surface areas, so Tactical Terrain can indirectly change the mix and locations of doodads, but it is not a doodad-density control. Fixed terrain-detail percentages are separate again.

Stock grass and mushroom actors inherit a blocking one-cell footprint. The rmg_plant_* aliases override it to passable. The Phase 7B record explains that these aliases were introduced because blocking variants narrowed a required route from nine cells to seven. That reason belongs to the historical route-width contract.

Proposed Natural UI label: **Gravel and Moss Amount**. A shorter **Slow Terrain Amount** is an alternative. Preserve the old tactical_terrain field's meaning for historical schemas. Do not rename it Doodad Density or reuse that field to store a different concept.

A future Doodad Density control would need its own field and semantics. It is not required for this terrain comparison and is deferred.

## 3. What must change in the comparison contract

| Existing assumption | Treatment in new Natural experiments |
| --- | --- |
| All dry land must form one component | Remove as a requirement; report components descriptively |
| Starts must share a ground component | Remove; choose valid sites across all eligible components |
| Every colony must be ground-reachable from common starts | Remove; retain local footprint and exit validity |
| Mandatory strategic graph routes and route widths | Remove as placement prerequisites and terrain constraints |
| Provisional start/route masks influence water construction | Remove from terrain construction |
| ReconnectOpenLand cuts through water | Do not run in new terrain generation |
| Largest water body >=20%, small-body share <=15%, 12/24-body cap | Do not use as universal Natural acceptance rules |
| Forced interior-water share/sector quotas | Descriptive only; no universal composition requirement |
| Candidate score penalizes small components for all maps | Do not use to choose comparison winners |
| Exact target percentages/counts or decoration sector coverage | Report achieved values and shortfalls; do not reject solely for these targets |
| Original surface adjacency, when enabled | Retain, including corner contacts |
| Valid native tiles, geometry, actor definitions, non-overlapping footprints | Retain |
| Local production exit cells and no immediate colony-to-colony fire | Retain; do not imply cross-map reachability |
| Old V1-V10 behavior and replay identities | Preserve in their existing generation paths |

The current seven-cell start water margin, five-cell neutral water margin, radius-12 exit-sector quotas, and wide route-center requirements are stronger design rules than a valid footprint and local exit. Audit their callers rather than automatically carrying them into the new Natural path. The provisional new contract uses exact runtime footprint/exit validity and existing rule-derived combat separation. Check actual local production in the package integration stage before finalizing any additional local clearance. Do not hide a global route requirement inside a local-usability test.

Ground connection diagnostics must explicitly say NOT_REQUIRED for this version. A disconnected result must not be reported as a full-accessibility pass. Native validation remains enabled for terrain/actor semantics and applicable local checks.

## 4. Terrain Complexity proposal

Use Low / Standard / High for the initial experiment. These labels express variation in native gameplay space:

- Low: longer stretches of the same surface and broader spacing between changes.
- Standard: intermediate frequency of meaningful surface changes, with composition varying by seed.
- High: more frequent changes in traversed or viewed terrain, through feature scale and distribution.

Complexity must directly drive the terrain constructor. Merely adding boundary noise, changing doodad count, or raising water/moss percentages does not satisfy this contract. Component count alone is not its definition: a connected irregular water system and several separate pools can both produce frequent local changes.

Map size adds space; units, colony footprints, and the reference native-cell observation windows keep their scale. Derive feature count or field wavelength from that scale rather than scaling every feature in proportion to map width.

Quantity settings and Complexity have separate inputs and random streams. Hold composition draws as stable as practical when comparing complexity levels; avoid making a control change reshuffle the whole random sequence unnecessarily. Exact pixel nesting or per-seed monotonic component counts are not required.

Do not freeze numerical wavelengths, region sizes, or coverage thresholds before the first comparison. Calibrate against stock colony scale and actual gameplay views. Retain broad plains as legitimate outcomes, while High should produce recognizably more frequent meaningful changes across the paired sample.

## 5. Comparison alternatives

Keep untouched V10 as the reference, with its restrictions labeled. It is not an implementation of the revised requirements.

| Alternative | Construction | Main question | Failure modes to inspect |
| --- | --- | --- | --- |
| A: seeded fields | Generate water and geological priorities from seeded spatial fields; Complexity directly sets correlation lengths and distribution scales; quantity selects coverage | Can fields express the desired range without relying on a few broad attractors or restrictive candidate scoring? | Large homogeneous stretches, same-looking blobs, fine boundary noise without interior variety |
| B: seeded regions | Allocate intended coverage to seeded irregular regions; Complexity directly controls their characteristic extent and spacing; allow clustering, overlap, and varied aspect ratios | Does explicit region construction give clearer control over local variation while remaining visually convincing and inexpensive? | Repeated isolated shapes, regular spacing, visible growth artifacts, expensive filling to exact targets |

Both may express lakes, coasts, rivers, and mixed arrangements. The old five-family taxonomy is reference coverage, not a compulsory architecture. Neither method may impose a dominant-body rule, obligatory large/small hierarchy, or evenly spaced features on every seed.

Use a shared terrain-intent boundary: preferred surface labels plus priorities needed to resolve supported transitions. Record intent before and after materialization. The existing land materializer also constructs regions, so it cannot be reused unchanged as a supposedly neutral renderer. Separate its catalogue/template selection from its existing GenerateMask composition policy, or provide a narrow experimental adapter that does so.

Use the existing NORMAL 2x2 template catalogue initially. Its representability limits are shared by both methods. Report necessary local transition adjustments by area and location. Neither adapter may reapply V10 water quotas, connectivity carving, or aesthetic ranking. Avoid silently replacing an intended layout during materialization.

Use seamless moss interiors for both new methods. Keep the untouched V10 reference, including the reported seam, available separately. This known artwork correction must not give one algorithm an unfair visual advantage.

The comparison has been reviewed and Regions selected. Continue the staged integration with Regions; the Fields implementation is retained as comparison evidence.

## 6. Staged work and decision points

### Stage 0: planning and baseline evidence (completed before prototype development)

Deliver this plan, the verified control mapping, the dependency/gate inventory, and native-scale observations from the two stored regressions. Review scope and the proposed naming before prototype development. No generator or UI changes.

### Stage 1: common experimental contract

Implement only after the plan has been reviewed:

- isolated experimental profiles and explicit applicability of validation rules;
- a shared terrain-intent/materialization interface;
- deterministic settings/seed manifests and per-stage timing;
- native semantic export plus whole-map and actual-texture review outputs;
- explicit removal of global-connectivity and morphology gates from new experiments.

Prove on a small constructed example that two separated land regions survive materialization unchanged, and that the new package policy permits disconnected actors with valid local placement. Also prove invalid footprints, blocked local exits, and wrong native semantics still fail. Old-version equivalents must keep their prior outcomes.

Do not silently amend the old acceptance CSV to make disconnected maps pass. Version the applicable checks and explicitly mark withdrawn constraints.

### Stage 2: paired terrain comparison

Start at 256x256, where the reported problem is strongest:

- draw and record two fresh seeds before viewing either method's output;
- for each seed generate Low / Standard / High Complexity using A and B;
- hold Water Amount and Gravel/Moss Amount at Standard, and Original surface relations on;
- this is 2 seeds x 3 levels x 2 methods = 12 materialized terrain cases.

Run one terrain proposal per case initially. Retain all failures and transformations. Investigate a materialization failure before continuing that method; do not search for a prettier replacement seed.

If both methods remain viable, repeat the same settings/seed pairing at 128x128: 12 further cases. Reuse the two reported 256 seeds as an additional four Standard-complexity diagnostic cases across A/B. Their new geometry is not expected to match V10.

These 24 comparison cases plus four diagnostic cases are a bounded design experiment, not a success-rate claim or a release acceptance batch. If the results are inconclusive, identify the uncertainty before expanding the matrix. No automatic large seed campaign.

Doodads do not influence terrain construction or blocker analysis. Review semantic terrain first and actual textures at fixed scale. A terrain-only output is not labeled a playable skirmish map.

### Stage 3: algorithm decision and player-placement integration

Summarize A/B without a universal weighted naturalness score. Consider:

- recognizable response to Complexity with quantities held fixed;
- range of valid arrangements and seed distinctiveness;
- gameplay-scale surface variation and movement readability;
- transition distortion and template artifacts;
- determinism, generation cost, and implementation complexity.

Select one method only after reviewing the side-by-side evidence. If neither succeeds, revise the underlying representation or control meaning rather than continuing to tune a bad score.

Fit players and colonies to completed surfaces across all eligible land components. Do not require a common component, guaranteed routes, compulsory central hubs, or balanced placement rounds inherited from competitive generation. Exact player count remains mandatory; neutral count is capacity-aware and must not fail solely because a complete symmetric round cannot fit.

Preserve locally valid starts and existing combat-separation behavior. Verify faction-specific base offsets and exit cells. Original surface relations on forbids placement repainting; on/off behavior must be reported and tested independently.

Restore the current passable doodad pass after placement using the existing actor variants. Keep its fixed density for the comparison; treat achieved quantity as a target and ensure the pass cannot alter terrain semantics or invalidate occupied footprints/local exits. This does not add a Doodad Density UI feature.

### Stage 4: settings probes, UI, compatibility, performance

Show neutral-colony markers on the lobby preview from the actual selected package. Use a visually distinct smaller symbol than the lettered player starts; verify reduced colony counts, map changes, both map sizes, and unchanged player-spawn interaction. This is preview presentation and must not alter terrain or historical map-package identity.

Before broad acceptance, test the winner with paired probes at both sizes:

- Low and High water while Complexity is fixed;
- Low and High gravel/moss amount while Complexity is fixed;
- Low/High Complexity at fixed quantities;
- Original surface relations on/off;
- 2/4 players and sparse/dense colony requests, including a capacity-shortfall case.

These probes should expose coupling and placement failures. Avoid a full Cartesian product unless a specific failure justifies it.

Introduce Terrain Complexity and the chosen surface-amount label in a new versioned settings/profile path. Keep old JSON tactical_terrain semantics and historical seed replay. Update presets explicitly. Battlefield Plan must not rebuild a compulsory strategic network; decide whether it remains a soft placement preference or is unavailable for the new Natural path. The first terrain comparison does not use it to shape geography.

Review RmgModels version/capability predicates: several natural features currently check only versions 9/10, while other features use broad >= comparisons. Adding a new integer version without updating these deliberately can accidentally inherit competitive assumptions.

Benchmark sequentially on the same machine against matched untouched V10 settings. Record terrain construction, transition resolution, placement, packaging/native validation, retries, and total pipeline time; separate process startup and visual rendering. The historical 256 median of 6.68 s is context, not a fresh benchmark or accepted budget. Aim to keep normal generation at least as responsive as current V10. Surface any regression before accepting a more expensive algorithm.

Keep production generation bounded. Record every attempt; do not inherit twelve candidates and three accepted full generations by default. Use exact repeatability audits separately from normal generation, preserving the existing speed-pass distinction.

### Stage 5: final acceptance and delivery

After functional corrections and the chosen design are settled, run an uninterrupted batch of 15 fresh random player-path generations. Predetermine a settings schedule covering both sizes, 2/4 players, and complexity levels. Use targeted probes for remaining extremes; do not filter seeds for the old five-family hash.

Review every actual package at whole-map and gameplay texture scales. Retain the handoff's stop/analyze rule and restart the complete fresh acceptance batch after a functional or visual correction. Old failures remain regressions; do not splice replacement seeds into a successful batch.

For the new version, mechanical acceptance means the revised applicable contract, not universal ground accessibility. Visual review explicitly covers gameplay-scale variation, control response, accurate movement appearance, surface relations, seed distinctiveness, and visible construction artifacts.

Run the supported build/validation entry point, focused relevant tests, old-version identity checks, package reload/lint, and independent native semantics/local-actor validation. Demonstrate local unit production and movement on selected packages; do not claim long-term faction accessibility or winnability has been validated.

Prototype contact sheets are labeled design evidence, not release-ready delivery. The existing strict 15-map rule is for final delivery; exposing imperfect cases is necessary to choose an algorithm. This distinction should be explicit in the revised review documentation.

Human review of the chosen result precedes integration. Commit/merge/push scope remains a separate instruction.

## 7. Evidence and measurement rules

Retain each case's seed, normalized settings, method/version, raw intent, materialized native surfaces/templates, package when applicable, changes introduced by each stage, timings, and review notes. Use ignored artifacts/rmg/reassessment-comparison/<batch>/ directories for generated maps, textures, and analysis.

Inspect fixed native windows, initially 16x16, 32x32, and 64x64, rather than only proportional map sectors. These are observation scales to calibrate, not acceptance thresholds or asserted viewport dimensions.

Report:

- achieved Water/Gravel/Moss quantities with clear denominators and shortfalls;
- distribution of distance to a surface boundary, excluding the outer map frame;
- homogeneous-window frequency and largest all-dirt square;
- surface-transition frequency along fixed geometry transects, with unreachable movement journeys reported separately;
- component sizes/counts descriptively, without target dominance or universal connectivity;
- intent-to-materialized and materialized-to-placed surface changes;
- actual blocked cells from configured actor definitions.

Keep representative windows predetermined by location and show worst homogeneous regions as well. Do not select only attractive screenshots. Visual inspection decides whether the observed variation is meaningful; a lower homogeneous-window percentage is not automatically better for every setting.

Stored regression evidence, measured on 2026-09-07 from report debug_layers.native_terrain_intent:

| Seed | Largest all-dirt square | All-dirt 16x16 windows | All-dirt 32x32 windows | All-dirt 64x64 windows |
| --- | ---: | ---: | ---: | ---: |
| 265249412814339965 | 86x86 native cells | 37.362% | 21.410% | 3.157% |
| 464831658165234256 | 112x112 native cells | 46.470% | 34.424% | 11.364% |

Every fully in-bounds overlapping window is counted with unit stride. These are terrain-only observations: colonies/doodads are not included in the surface labels. They measure stored reproductions, not newly generated maps. Both maps already achieve approximately 14% gravel and 8% moss of non-Water land, illustrating why global amounts alone miss local homogeneous stretches.

Machine-readable counts and source locations are recorded in artifacts/rmg/reassessment-plan-20260907/baseline-surface-observations.json.

## 8. Implementation dependency map

| Area | Relevant source | Planned responsibility |
| --- | --- | --- |
| Terrain construction | RmgNaturalLandscapeV10Generator.cs; RmgNaturalLandscapeGenerator.cs | Isolate old paths; introduce A/B without provisional reservations, connectivity carving, or inherited scoring |
| Materialization | RmgLandCoverMaterializer.cs; RmgShorelineMaterializer.cs; NormalLandTransitionCatalogue.cs | Separate region synthesis from template emission; preserve and measure intended semantics |
| Placement | RmgNaturalSurfacePlacement.cs; RmgDirtPlacementRules.cs; RmgCombatSpace.cs | Search all eligible components; use exact local constraints; no terrain editing |
| Validation | NativeMovementValidator.cs; RmgBlockingTopologyGenerator.cs; RmgGenerator.cs | Version policy applicability; retain real semantic/actor checks; remove global-accessibility and aesthetic-quantity failures from new path |
| Settings and identity | RmgModels.cs; RmgPlayerSettings.cs; OpenRaRmgMapAdapter.cs; mods/sa/rmg profiles | Explicit new capability/version/schema behavior and preserved legacy replay |
| UI | RmgLobbyLogic.cs; mods/sa/chrome/lobby.yaml | Complexity control, accurate surface-amount label, appropriate Battlefield Plan behavior |
| Review harness | scripts/rmg/Invoke-RmgVisualReviewLoop.ps1; Test-RmgVisualReviewGate.ps1 | Versioned checks, new review axes, texture-scale evidence, preserved seeds and failures |

## 9. Decisions tracked through review

- Accept or amend the proposed Gravel and Moss Amount label; Doodad Density is not a correct rename.
- Accept or amend the paired comparison scope and the shared materialization boundary.
- Review the proposed exact-local-validity policy in place of inherited water margins and exit-sector quotas.
- Choose numeric Complexity bands from the pilot evidence, not from the current generator's constants.
- Regions was selected by the user after comparison review. Resolve Battlefield Plan presentation and the new version identity during its integration.

Implementation proceeds on codex/rmg-reassessment-comparison with Regions selected. Grey neutral-colony preview markers are included in this version; final integration remains subject to the documented validation stages.


## 10. Comparison implementation checkpoint

Stages 1 and 2 are implemented and the user selected Regions after reviewing the 28-case comparison. See [comparison results](NATURAL_LANDSCAPE_COMPARISON_RESULTS.md). The neutral-preview widget uses grey markers and package-position checks pass; live lobby interaction remains in Stage 4. The next stage is Regions player/colony placement and settings integration. No playable Natural successor has been promoted.

## Playable integration checkpoint (2026-09-07)

The user authorized proceeding with playable integration and updated RMG defaults. Regions V11 and schema 5
are now implemented; local placement, capacity-aware neutrals, passable doodads, grey preview markers and all
applicable UI controls are connected. See [the V11 implementation record](NATURAL_REGIONS_V11.md).

The paired probes, supported repository validation, focused local contracts, historical replay checks and
uninterrupted 15-map mechanical/terrain visual review passed. The old standalone V1 fixture is stale against
both untouched HEAD and the updated build; the evidence is retained and its expected hashes were not changed.

Stage 4 live lobby interaction and Stage 5 live unit production/movement remain unverified: the required
Windows automation runtime failed before reaching the game, including after reset/retry. No live-test pass
is claimed. The compiled version is prepared for the in-game test the user requested next. No merge or push
has been performed.
