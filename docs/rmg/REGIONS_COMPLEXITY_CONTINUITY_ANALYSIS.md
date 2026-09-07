# Regions V11: complexity continuity and range analysis

Date: 2026-09-07. Branch: `codex/rmg-reassessment-comparison`.

**Analysis only.** This records the user's latest in-game feedback, reproduces existing behavior, and proposes the next development scope. No generator, lobby, rules, or defaults were changed. The six diagnostic maps were written only under repository artifacts and were not installed into the user's game.

## Conclusion

Recognizable geography across complexity levels is attainable with a focused, moderately sized change to region construction. It does not require replacing the RMG, tile catalogues, terrain-first placement pipeline, package exporter, or native validator. It is more than adding two menu entries or changing a random seed: the current spatial model changes the whole distribution of terrain when its scale changes.

The recommended next development step is a small, versioned terrain prototype that establishes broad geography from the seed and then varies meaningful secondary features with Complexity. Compare its completed native terrain across all five levels before integrating it into the player path. Current High is the reference for the intensity of the new Medium, not a requirement to retain its numerical scale constant in the new model.

## User direction recorded for the next update

- Explored Map defaults to **true**; Fog of War defaults to **false**. These remain editable options.
- Expand Terrain Complexity to five levels: **Small / Medium / High / Extreme / Ultra**, using the wording in the latest request. Current labels are Low / Standard / High; exact display wording and serialized identifiers must be handled explicitly when integrating the new version.
- Current High is approximately the desired new Medium. Current Standard is too sparse for that role.
- At a fixed seed, size, surface quantities and other settings, changing Complexity should preserve recognizable broad geography while meaningfully intensifying terrain variation.
- Original Surface Relations Off still looks ordered; investigate rather than assume the checkbox provides unrestricted surface combinations.
- Preserve the approved gameplay scope: no mandatory global land accessibility, route graph, fairness symmetry, or flying-unit availability analysis. Keep local native footprints/exits and combat separation. Existing passable doodads and grey neutral-colony preview markers remain part of the accepted implementation.

The five-level proposal here concerns Terrain Complexity, which is the setting discussed in the feedback. It does not infer new water or gravel/moss percentages from the word Ultra.

## Evidence from the screenshot seed

Reproduced seed **397716241463670640**, NORMAL, 256 x 256, four players, Water Standard, Gravel/Moss High, Neutral Colony Density Standard. Generated all three current complexity levels with Original Surface Relations both On and Off through the existing production wrapper. Each generation requested repeatability checking. All six passed repeat generation, package reload/lint, native semantics, local start/colony footprint and exit checks; each produced four starts and 48 neutral colonies.

The On cases reproduce the broad layouts and displayed surface percentages in the screenshots. These are saved-map diagnostics, not a new live gameplay test.

| Current complexity | Construction scale, native cells | Proposed water ellipses | Actual connected water bodies | Actual water area | Surface changes per 100 native neighbor edges |
| --- | ---: | ---: | ---: | ---: | ---: |
| Low | 112 | 4 | 3 | 19.83% | 1.97 |
| Standard | 64 | 11 | 8 | 19.66% | 3.22 |
| High | 36 | 34 | 20 | 19.34% | 5.58 |

The ellipse count is the number of internal shape proposals, not a requested or guaranteed lake count. Overlap, global coverage selection and native transition resolution determine the final water bodies. This is one seed, not a universal component-count or performance claim.

Only **30.2% of Low's water cells remain water at High**. Across Low to High, 38,765 of 65,536 native cells change surface type (59.2%), and none of the four player start IDs retains its coordinates. Standard to High retains 36.8% of Standard's water cells. These are direct cell-overlap measurements, not a complete measure of perceived layout similarity; they substantiate the visual observation.

Evidence directory: [regions-complexity-analysis-20260907](../../artifacts/rmg/regions-complexity-analysis-20260907/). It includes exact input JSON, saved packages, generation logs, reports, native terrain arrays, texture exports, actor coordinates, [measurement script](../../artifacts/rmg/regions-complexity-analysis-20260907/analyze.py), [results JSON](../../artifacts/rmg/regions-complexity-analysis-20260907/analysis-results.json), and [six-map comparison](../../artifacts/rmg/regions-complexity-analysis-20260907/current-v11-comparison.png).

## Why the layout changes

`TerrainComparisonSettings.Scale` and `BuildPriorities` in `OpenRA.Mods.OpenSA/Rmg/Reassessment/TerrainComparison.cs` (lines 30 and 202) implement Complexity as a replacement of the characteristic spatial scale:

1. Every candidate region gets a different radius. Low to High changes the base scale from 112 to 36.
2. The number of candidate regions grows approximately as the inverse square of that scale. For this seed's settings, water proposals increase from 4 to 34, geology from 5 to 49, and moisture from 2 to 19.
3. Even the shared initial proposals move somewhat, because conversion of their random coordinates into map coordinates uses scale-dependent edge padding.
4. Broad shape distortion and fine noise also change their spatial frequencies with the same scale.
5. Coverage selection ranks cells over the whole map. New proposals compete with old proposals for the fixed water or geological area. There is no persistent allocation of surface area to broad regions across complexity settings.

Consequently, Low does not establish geography that High refines. Both levels construct their own distribution from the same underlying random numbers. This was the earlier implementation's interpretation of spatial complexity; the latest feedback adds the stronger and useful requirement of geographic continuity.

**This is not a Complexity-dependent random-stream reseeding bug.** Terrain priorities use `Mix(settings.Seed, stream)` for separate water/geology/moisture streams. The placement stream is also independent of Complexity in the current V11 path: `DeterministicRandom.ForStream` (line 27) selects `InheritedShorelineCanonical` for `regions-placement`, because V11 enables `UsesClearLandDetails`. That inherited canonical excludes Complexity. Merely separating random streams again would not resolve the spatial problem.

Player and neutral sites change because terrain eligibility changes. `RmgRegionsGenerator.cs` (lines 39-83) shuffles candidates once, chooses the first valid start, then spreads later starts relative to those already chosen. A different eligible first start can move all later starts. Neutral placement is greedy over valid candidates and current reservations. Terrain is not repainted for placement.

The current plan's requirement to keep composition draws stable was therefore only partly sufficient: draw stability is present, but recognizable geographic stability is not guaranteed. The next version needs to state and test both.

## Recommended construction change

Keep the Regions approach. Introduce a persistent broad geographical model generated from the seed and size, independent of Complexity. Major regional positions, their broad orientations and the coarse pattern of water and slow ground should remain identifiable across levels. Quantity controls continue to set surface area separately.

Complexity then controls the strength and activation of smaller features associated with that geography: substantial shoreline indentations, peninsulas, channels, local splits or satellite pools, and more intricate gravel/moss distributions. It must also vary terrain through open areas where appropriate; roughening the edges of otherwise empty maps would not satisfy the reported need. These features must survive native materialization and affect traversable space or movement costs.

Give broad regions and detail candidates persistent identities. Draw detail randomness per identity instead of allowing the number of earlier candidates to shift later candidates. Existing large features should not shrink indiscriminately every time a finer feature is enabled. Stabilize coarse surface allocation as well as feature coordinates; otherwise a single global coverage threshold can still move most terrain between regions. A prototype should compare soft regional coverage targets or a bounded contribution from detail, rather than assume adding extra noise is sufficient.

This is an internal construction technique, not a rule that every map needs a dominant lake or a particular large/small hierarchy. Seeds may still produce dispersed lakes, concentrated geology, broad plains or mixed arrangements.

The continuity contract should preserve broad locations and distributions, not every native cell. With the same water amount, adding water in one location generally requires removing it elsewhere. Strictly retaining every old water cell while adding more water cannot preserve the same coverage. Shorelines can move and water bodies can split while the overall geography remains recognizable.

Placement should retain preferred sites when they remain valid and, if necessary, seek a nearby valid replacement. The existing stable candidate order is useful, but the farthest-start selection can still amplify small local changes. Measure this in the prototype and add stable preferred start regions if needed. Do not promise fixed colony coordinates when water, footprint validity or combat separation makes them invalid, and do not carve terrain to preserve them.

## Five levels and upper-range stability

| Proposed level | Calibration intent |
| --- | --- |
| Small | Calmer variation with recognizable regional structure; reconsider today's very empty Low baseline. |
| Medium | Target the gameplay-scale richness the user currently sees at High. Recommended default. |
| High | Clearly stronger secondary variation, still preserving the same broad geography. |
| Extreme | Dense, interacting features with a visible increase over High. |
| Ultra | Highest useful intensity that the native tiles and valid local placement support; an explicit stress case. |

These are calibration targets, not tested settings or approved numerical thresholds. Do not produce the upper levels merely by extending the 112/64/36 sequence downward. That would both increase wholesale redistribution and multiply shape-evaluation work.

Important limits to measure:

- NORMAL terrain uses 2 x 2 native tile stamps and a restricted set of transition neighborhoods. Unsupported water shapes and diagonal land masks are simplified during materialization; features can disappear instead of becoming meaningfully more complex.
- Moss is placed within an eroded geological envelope. Too many narrow gravel regions can reduce available moss interiors, causing a moss shortfall even if the same amount was requested.
- More fragmentation can reduce locally valid start/colony footprints and production exits. Exact player count remains required; a neutral-colony shortfall is already an allowed, reported result. Global land connectivity remains outside scope.
- Current priority evaluation visits each sample and every candidate ellipse. More candidates can noticeably increase work, particularly at 256. Spatial indexing or local candidate evaluation may become useful if actual profiling justifies it.
- Avoid turning the upper levels into barely visible speckling. Assess completed native terrain at unit-movement scale, whole-map appearance, surface quantities, transitions and generation time together.

## Original Surface Relations: what Off actually does

The current generator uses dirt/water shoreline banks, dirt/gravel land transitions and gravel/moss land transitions. `NormalLandTransitionCatalogue.cs` exposes only `ClearRock` and `RockVegetation` banks (line 18). `TerrainComparison.cs` always confines moss to a gravel envelope and always uses those banks (lines 107-155), independently of the checkbox.

Off removes the extra water-adjacency buffer used when selecting geology and allows start/colony footprints on passable gravel or moss. It does not construct free combinations such as moss directly adjoining dirt throughout the map. Geometry can change a little because the eligible geological area changes.

| Current complexity | Terrain cells changed by Off | Water mask | Neutral anchors on gravel / moss with Off |
| --- | ---: | --- | ---: |
| Low | 2,280 (3.48%) | Identical | 7 / 3 |
| Standard | 1,048 (1.60%) | Identical | 7 / 6 |
| High | 2,160 (3.30%) | Identical | 13 / 7 |

All On neutral anchors are on dirt. All six maps have zero forbidden native surface contacts under the current ordered-surface check. Anchor counts describe the anchor cell, not every cell of the building footprint; local footprints and exits were separately validated.

The user's observation is therefore correct in its main effect, with small terrain changes as well. The current label can suggest more freedom than the implementation provides. In the next plan, distinguish placement permission from terrain adjacency, or describe Off's limited scope explicitly. Implementing genuinely unrestricted surface relations would be a separate transition-catalogue/materialization task. This analysis does not establish that the stock assets lack every alternative transition; it establishes that the current audited generator does not support them. Removing validation alone would not implement them.

## Visibility defaults and integration scope

`mods/sa/rules/misc.yaml` (lines 35-40) currently sets `FogCheckboxEnabled: True` and `ExploredMapCheckboxEnabled: False`. `engine/OpenRA.Game/Traits/Player/Shroud.cs` (lines 61-66) exposes these as the actual `fog` and `explored` lobby defaults.

Changing those defaults is straightforward, but `LobbyCommands.LoadMapSettings` (line 1266) preserves existing preferred values when switching maps. Generated map packages currently inherit the mod defaults and the RMG panel selects them using a normal map-change command. Consequently, merely putting different defaults in the new generated package would not reliably initialize an existing lobby as requested.

For the next implementation, initialize the requested defaults at the appropriate lobby entry point and verify a fresh lobby plus a first generated-map selection. Preserve subsequent deliberate user choices when regenerating. A shared `misc.yaml` change affects ordinary skirmish maps too; decide that scope explicitly in implementation planning rather than accidentally imposing it through a shared rule. Do not lock the options to force the default.

Five-level integration spans the complexity model/parser, player settings, presets/defaults, lobby dropdown, CLI wrapper, map identity/title/reporting, documentation and validation. Current entry points include `RmgPlayerSettings.cs:392`, `RmgLobbyLogic.cs:310` and `Invoke-RegionsMapGenerator.ps1:6`.

Any output-changing constructor or recalibration needs a new generator/settings identity, with explicit V11 replay retained. A V12 path and a new player schema are reasonable choices; exact version plumbing is an implementation decision. Changing the meaning of an existing V11 serialized complexity value in place would undermine saved-seed reproducibility.

## Proposed development sequence after this analysis

1. Specify the continuity contract and prototype fixed broad geography plus increasing secondary features. Start with the user's seed and several preselected additional seeds, holding quantities fixed, at both supported sizes.
2. Compare all five levels side by side using native materialized terrain, actual textures and actor sites. Use current High as the Medium reference. Measure water overlap and coarse spatial coverage, change frequency at several native scales, open-ground window sizes, achieved quantities, transition loss and start displacement. Cell overlap alone must not become an arbitrary quality gate.
3. Calibrate the range from those outputs. Do not require component count to rise for every seed: feature joins and splits can both increase meaningful complexity. Verify that higher levels are not merely adding edge noise or silently losing requested moss/water.
4. Integrate the selected construction, five live controls, calibrated Medium default, requested visibility defaults, and clarified surface-relations behavior. Keep the grey neutral markers and accepted gameplay scope.
5. Run a predeclared multi-seed stress matrix at 128/256, 2/4 players, both relation modes, dense colonies and upper water/gravel settings, emphasizing Extreme/Ultra. Verify deterministic replay, native terrain and footprints/exits, capacity outcomes, bounded runtime, old-version replay, then perform live lobby and gameplay checks.

The source analysis establishes that this is a bounded redesign of region construction within the existing RMG. The proposed new behavior and its upper limits still require prototype evidence; the six reproductions validate the diagnosis, not the unimplemented design.

## Subsequent implementation scope

The user subsequently requested continuity across the existing three levels first, with additional levels only after stability confirmation. See [Regions V12 continuity](NATURAL_REGIONS_V12_CONTINUITY.md) for that implementation. The five-level proposal above remains a later phase.
