# Natural Landscape comparison results

Date: 2026-09-07
Branch: codex/rmg-reassessment-comparison
Baseline: 3d40e2dc28879fb29b96405f9af84af893762972
Status: comparison reviewed; Regions selected by the user on 2026-09-07. Playable integration remains to be completed.

## Selected approach

Both constructions can vary meaningful terrain at fixed surface quantities. Regions has the clearest Low / Standard / High progression in this pilot, including on 128x128 maps. Fields offers less uniformly rounded shapes, but its initial Low and Standard bands leave much broader homogeneous areas.

The user selected Regions, judging its design superior in almost every comparison. Regions is now the basis for the next Natural generator. The initial review identified repeated oval/pool outlines as a remaining consideration. This is a design judgment about these implementations, not proof that region construction is inherently superior. Fields at High approaches Regions at Standard in local variation. Identical numerical scale inputs do not calibrate the two methods to identical observed complexity.

The constructor decision is complete; numeric Complexity bands and any shape refinements remain part of integration. The choice is based on the reviewed map character, not solely on minimizing empty ground. Broad plains remain legitimate, particularly at Low. Rivers, long channels, unusual clustering and the wider distribution of outcomes are not established by two fresh seeds.

## Review material

The full local gallery includes every case, actual-texture overviews, native-surface sheets, fixed-position crops, the largest all-dirt area, and raw reports:

- [Full gallery](../../artifacts/rmg/reassessment-comparison/review/review.md)
- [Measurements](../../artifacts/rmg/reassessment-comparison/review/measurements.csv)
- [Paired summary](../../artifacts/rmg/reassessment-comparison/review/summary.json)
- [Fresh seed 1 at 256](../../artifacts/rmg/reassessment-comparison/review/pilot-256-15920227472914540108-textures.png)
- [Fresh seed 2 at 256](../../artifacts/rmg/reassessment-comparison/review/pilot-256-15559935339691374119-textures.png)
- [Fresh seed 1 at 128](../../artifacts/rmg/reassessment-comparison/review/pilot-128-15920227472914540108-textures.png)
- [Fresh seed 2 at 128](../../artifacts/rmg/reassessment-comparison/review/pilot-128-15559935339691374119-textures.png)
- [Stored V10 / Fields / Regions, reported seed 1](../../artifacts/rmg/reassessment-comparison/review/reference-265249412814339965-comparison.png)
- [Stored V10 / Fields / Regions, reported seed 2](../../artifacts/rmg/reassessment-comparison/review/reference-464831658165234256-comparison.png)

The images are offline renders of the actual NORMAL tile frames and palette from reloaded packages. They do not include actor sprites or claim to be runtime screenshots. All 28 whole-map cases and their four-crop sheets were inspected. The fixed native crops use 32x32 cells at 24 texture pixels per native cell; sheet thumbnails are reduced, with original crops retained in each case directory.

## Experimental contract and scope

Two cryptographic seed draws were written to a manifest before either method's first output:

- 15920227472914540108
- 15559935339691374119

The experiment retained 12 cases at 256x256, then the same 12 combinations at 128x128, then four Standard-complexity cases for the two reported seeds. Each case used one proposal, with no candidate ranking, replacement seeds or search for an attractive map. All 28 materializations and package checks passed on their first comparison attempt. Determinism replays are separate from this design sample.

All cases request 20% water of total native map area, 14% gravel and 8% moss of non-water land, with Original surface relations enabled. Initial characteristic scales are 112 / 64 / 36 native cells. These are calibration values, not final settings.

Terrain packages are hidden from the lobby and contain no playable starts, colonies or doodads. They are explicitly TERRAIN_COMPARISON_ONLY. Player placement, combat separation, settings extremes, doodad placement, runtime unit production and the final uninterrupted 15-map player-path acceptance batch remain later work.

The new constructors do not call old topology generation, provisional start/route reservation, connectivity repair, water-body hierarchy gates, interior-water quotas or V10 candidate scoring. The old V10 profile supplies only existing tile banks and the shoreline emitter. Experimental identity is natural-reassessment-comparison-v1, not V10 replay identity.

Both methods use the same local transition normalization and fixed NORMAL catalogue. Unsupported local patterns are reduced monotonically; no water or geological regions are grown to replace losses. This is bounded by the finite set of removable cells/vertices. No connectivity or component-count target controls these adjustments.

The intent image combines each stage's pre-tile surface choices: raw water and gravel/moss vertex choices. Geology eligibility already accounts for the materialized shoreline and moss eligibility already accounts for its gravel envelope. It is not an entirely unconstrained initial picture of all surfaces. Raw priorities, final templates, native surfaces and changed-cell images are retained, and each normalization's removals are reported.

## Measured response

The following are arithmetic means across the two fresh seeds, not population estimates. A 32x32 window is counted at every in-bounds native origin with a one-cell stride. Smaller empty-window shares are descriptive, not automatic acceptance criteria.

| Size | Method | Complexity | Largest all-dirt square, side | All-dirt 32x32 windows | Changes along fixed transects |
| --- | --- | --- | ---: | ---: | ---: |
| 256 | Fields | Low | 134.5 | 43.72% | 3.50 |
| 256 | Fields | Standard | 102.0 | 30.11% | 4.31 |
| 256 | Fields | High | 65.5 | 13.59% | 8.38 |
| 256 | Regions | Low | 91.0 | 28.56% | 4.88 |
| 256 | Regions | Standard | 73.0 | 14.67% | 7.63 |
| 256 | Regions | High | 50.0 | 6.16% | 14.88 |
| 128 | Fields | Low | 59.5 | 25.62% | 2.31 |
| 128 | Fields | Standard | 66.5 | 28.10% | 2.75 |
| 128 | Fields | High | 53.5 | 16.79% | 3.63 |
| 128 | Regions | Low | 64.0 | 29.84% | 1.88 |
| 128 | Regions | Standard | 45.5 | 11.87% | 3.44 |
| 128 | Regions | High | 39.5 | 2.54% | 6.50 |

Transects are horizontal and vertical lines at 1/8, 3/8, 5/8 and 7/8 of map width. They describe geometry, not reachable unit journeys. Distance-to-boundary distributions exclude the outer map frame. Component areas and counts are retained as descriptive observations.

Across all 28 cases:

- Actual water: 19.1406–19.8639% of map area.
- Actual gravel: 13.9604–14.0675% of non-water land.
- Actual moss: 7.9564–8.0264% of non-water land.
- Composite intent to final native changes: 0.1373–0.9277% of map cells.
- Forbidden native surface contacts, including diagonals: zero in every case.
- Terrain construction plus materialization: median 138 ms, maximum 159 ms on this machine in this run.

Timing excludes process startup, visual rendering and any future player-placement work. These are prototype timings, not a full-path speed claim or a matched V10 benchmark. Each report records packaging and rendering separately.

## Visual observations

Fields produces irregular coast-like structures, some narrow protrusions and occasional tiny remnants, with broad open interiors. Its current scale is particularly coarse relative to the smaller map. Low-to-Standard does not consistently reduce large plains there. High is more active, but fixed center and north-west crops can still contain almost entirely dirt.

Regions spreads water and geological changes more consistently as Complexity rises. It can merge regions and create disconnected dry areas without being rejected. The elliptical basis remains visible: several lakes look like variations of rounded pools. Raising density does not by itself resolve that shape repetition.

Both have natural-looking local transitions at the inspected crop scale. The known square moss-detail border is absent in the new methods because both use the regular transition/interior catalogue rather than V10's fixed-detail template 93. Repetition in the underlying gravel and moss artwork remains visible; no new artwork was introduced.

The unmodified V10 references retain their prior geography and detail tiles. Re-reading their packages reproduces the previous largest-dirt-square measurements of 86 and 112 native cells. Their images are included to keep the reported regressions visible, not as a revised-contract baseline that the new methods must imitate.

## Neutral-colony lobby preview

Neutral-colony markers are included in this version's scope and implemented in ColonyMapPreviewWidget, with the OpenSA lobby preview template using that subclass.

- Five colony types are recognized when owned by Creeps or Neutral.
- Positions come from the selected package's actual map.yaml actors.
- The marker is a 5x5 grey square (#A0A0A0) with a one-pixel black border, smaller than the lettered player starts.
- Package/UID changes invalidate the cached position list.
- The widget inherits the engine's spawn drawing, coordinate conversion, hover and mouse handling.
- No map package or historical generator is rewritten for this presentation feature.

The user requested grey to match the neutral-colony visual convention and keep gold available for a common player colour. A future option will let players own multiple colonies at game start; that feature and ownership-aware colony preview colours are recorded as future work, outside this version.

The actual stored packages yielded 48 and 20 neutral markers respectively, each with four player starts. Focused tests cover all species, exclusion of player-owned colonies and mpspawn actors, reduced actual counts, empty input, and saved fixture positions.

[Marker layout illustration](../../artifacts/rmg/reassessment-comparison/review/neutral-preview-illustration.png) uses actual saved coordinates and the implemented grey-marker dimensions. Player symbols there are illustrative. Live lobby map switching, download/load transitions, hover and spawn-click behavior have not been exercised and remain integration checks. The illustration is not presented as a UI screenshot.

## Verification and remaining work

The supported build/data validation pipeline passed. The frozen V1–V10 tests, inherited baselines, size contract, decoration equivalence and existing native validator passed.

The experimental tests separately establish:

- Deterministic templates and semantics for both methods.
- Two native dry components survive shoreline materialization without connectivity carving.
- Every colony type has valid local sites on either land component.
- Corrupted footprint terrain, blocked production exits and incorrect native semantics are rejected.
- A saved and reloaded fixture preserves its disconnected colonies and preview coordinates.
- All five faction-specific starting bases, offsets and local exits fit on both components.

Fixture evidence: [policy](../../artifacts/rmg/reassessment-comparison/contract-fixture/fixture-policy.json), [test log](../../artifacts/rmg/reassessment-comparison/contract-tests.log). This is a local-site/semantics proof. It does not claim combat separation, actual production simulation, or long-term faction accessibility.

Final-code replay passed for all 28 retained cases: intent, semantic bytes and template IDs matched exactly, without rendering another sample. See artifacts/rmg/reassessment-comparison/retained-case-replay.log. The new reference renderer only reads stored V10 packages. Existing repository analyzer and asset-inventory findings remain outside this change; no release package was produced.

With Regions selected, the next work is to settle its shape behavior and Complexity calibration, then integrate exact local player/colony placement, restore current passable doodads, introduce the new settings path and Gravel and Moss Amount label, complete preview runtime checks, and execute the planned settings/performance/final-acceptance stages.

Regions has been selected. No merge or push has been performed.

## Reproduction

From the repository root, first build with build-pipeline.cmd build. Use a new output directory for every generation attempt.

~~~powershell
.\scripts\rmg\Invoke-NaturalTerrainComparison.ps1 -MapSize 256 -OutputDirectory <new-256-directory>
.\scripts\rmg\Invoke-NaturalTerrainComparison.ps1 -MapSize 128 -ManifestPath <256-directory>\manifest.json -OutputDirectory <new-128-directory>
.\scripts\rmg\Invoke-NaturalTerrainComparison.ps1 -DiagnosticsOnly -ManifestPath <256-directory>\manifest.json -OutputDirectory <new-diagnostic-directory>
~~~

The analysis script expects pilot-256, pilot-128 and diagnostics-256 directories under its supplied root. Pillow is required. Reference directories are optional.

The utility runs from engine with the same pinned DOTNET_ROOT, ENGINE_DIR and MOD_SEARCH_PATHS used by the PowerShell harness:

~~~text
OpenRA.Utility.exe sa --compare-natural-terrain OUTPUT_DIR SEED SIZE Fields|Regions Low|Standard|High
OpenRA.Utility.exe sa --compare-natural-terrain --self-test [FIXTURE_DIR]
OpenRA.Utility.exe sa --compare-natural-terrain --verify-manifest MANIFEST_PATH
OpenRA.Utility.exe sa --compare-natural-terrain --reference STORED_MAP_PATH OUTPUT_DIR
~~~

Binary exports are little-endian: row-major native semantic.u8 / intent.u8 (Clear=0, Water=1, Rock=2, Vegetation=3); logical row-major templates.u16; logical water priorities and (logical width+1) squared geology priorities as IEEE-754 f64.
