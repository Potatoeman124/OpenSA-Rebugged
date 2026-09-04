# Natural Landscape V10.1 - implementation and fresh validation

Follow-up: [authoritative surfaces and dirt-only placement](NATURAL_V10_SURFACE_AUTHORITY.md) documents the later placement fix and its separate verification. The corpus and terrain-v2 identity below describe the preceding terrain-shape iteration.

Status: assistant mechanical/visual pre-review complete; **human visual sign-off pending**.
Branch: `codex/rmg-natural-v10`. No merge or push was performed in this iteration.
Report identity: `natural-v10.1-ranked-curved-terrain-v2` (experimental Generator 10).

## Changes

- Select the morphology family once from the displayed seed; all twelve candidates retain it.
- Rank twelve cheap terrain projections, then compare up to three mechanically valid fully
  materialized candidates. Scores penalize straight/repeated boundaries, rectangularity,
  small fragments, disconnected dry regions, and water altered during gameplay embedding.
- Use rotated curved coastal fields and smooth interior-water encouragement. Remove the
  rectangular interior-cell exchange that could imprint an artificial square boundary.
- Compose independently warped geological regions before fitting moss cores inside them,
  with spatially varying insets rather than making the gravel region from the moss outline.
- Record native water/gravel/moss boundary diagnostics and actual projected-to-final water
  changes. These descriptors help selection; they do not automatically certify naturalness.
- Add fresh family-stratified sampling to the offline preview loop, verify the family hash
  against each generated result, and synchronize reviewed worksheets with their manifests.

The selection score is currently common across families, not five independently calibrated
naturalness classifiers. Morphology-specific construction and stable-family sampling preserve
family coverage while the visual review remains authoritative.

## Failure found and corrected

The first unrestricted trial stopped immediately on seed `482208208218735515`:
`LAND_COVER_TARGET_ACCOUNTING`.

The new gravel-first path incorrectly promoted the actual envelope area into an effective
target. Template rounding or a smaller moss core could therefore make the reported gravel
target exceed the request. The correction preserves requested/capacity-bounded effective
targets and refits the geological fringe around the accepted core if the retained envelope
would exceed the materializer tolerance.

The exact saved configuration was rerun successfully. Its report has logical, proxy, native,
package reload, rules/sequences initialization and YAML lint acceptance. Its effective
gravel target is 1840 native cells (request 1840); moss target 1051 (request 1051), with
actual counts 1840/1052, within the existing tolerance.

Evidence:
- Failed trial: `artifacts/rmg/visual-review/v10.1-fresh-validation-01/failure.json`.
- Reproduction after correction: `artifacts/rmg/visual-review/v10.1-accounting-regression-01/`.
- The earlier family trial was superseded. **Both final batches below use new seeds after
  the correction**; the previously user-reviewed fifteen maps were not the acceptance set.

## Final fresh batches

Shared settings: Balanced, 4 players, automatic symmetry, Contested Center, Natural Landscape,
Standard water, Standard tactical terrain, Standard neutral colonies, Original surface
relations enabled. Seeds are fresh random integers below 10^18.

| Batch | Mechanical | Assistant visual | Family coverage | End-to-end generation time |
| --- | --- | --- | --- | --- |
| `v10.1-family-validation-02` | 15/15 | 15/15 | 3 each: coast, inland sea, lakes, river, wetlands | mean 4.71 s; range 3.17-7.10 s |
| `v10.1-fresh-validation-02` | 15/15 | 15/15 | unrestricted: 5 coast, 4 inland sea, 3 lakes, 3 wetlands | mean 4.30 s; range 3.25-7.40 s |

The unrestricted draw happened not to contain River Valley. It was not resampled to conceal
that gap; the independent family-stratified sample supplies three river cases.

All thirty map packages were generated through the player-settings resolver and normal
map-package path with `-MovementValidation both`. Packaged `map.png` files, not substitute
terrain-only previews, were inspected on the full-size contact sheets. Topology diagnostics
were also inspected. Concrete per-map observations are saved in each `visual-review.csv`;
both `visual-review-gate.json` files report 15 accepted, zero rejected, zero problems.
No failed or visually rejected seed was replaced within either final batch.

Longest native water-boundary run (outer map frame excluded):
- Family sample: mean 11.33, maximum 16 cells.
- Unrestricted sample: mean 10.93, maximum 14 cells.

Those are diagnostics, not acceptance thresholds or proof that every shoreline is natural.
Generation timings include process startup, repeatability checking, materialization and
package/native validation, but exclude the subsequent debug-preview/contact-sheet rendering.

## Other checks

- `build-pipeline.cmd build`: engine and OpenSA build pass; zero warnings/errors.
- `scripts/rmg/Invoke-RmgSelfTests.ps1`: V1-V10 pass; inherited-baseline checks pass;
  parameter matrix, terrain catalogue/materializer and native-validator checks pass.
- Added controls cover fixed family across twelve retry indices for all five families,
  rectangle-versus-curved boundary ranking, and exclusion of the outer map frame.
- Existing repeated-generation checks cover deterministic output.
- `git diff --check`: no whitespace errors (only existing line-ending normalization notices).

## Scope and remaining limitations

This is offline validation of the same settings/package generation path, **not a sequence
of native UI clicks or a live match**. Native movement validation uses OpenRA terrain and
static actor semantics; it does not instantiate a live World or validate dynamic combat.

The fixed NORMAL 2x2 transition catalogue still operates on a 64x64 logical lattice for a
128x128 native map. Local stepping and occasional angular notches remain. Some rounded
peninsulas still reveal the start-clearance discs. These are recorded explicitly in the
visual worksheet rather than hidden by a blanket claim of finished natural terrain.

Geological insets vary, but the materializer still guarantees legal surface transitions.
Some wetland/river candidates require more reshaping during route embedding: the maximum
projected-to-final symmetric difference was 23.57% of projected water in the family sample
and 16.12% in the unrestricted sample. It is penalized, not prohibited; no claim of zero
terrain damage is made.

Neutral colony counts remain adaptive (4-12 in these samples versus 16 requested). Native
route/start safety passed; this is not proof of competitive fairness across asymmetric
landscapes. Water/tactical settings and the player count were fixed in these visual batches;
the separate regression suite is broader, but does not replace a future visual matrix.

Assistant acceptance means ready for the user's next review, not user approval. Review
the unrestricted sheet as the primary new fifteen-map set, and identify maps by batch
and number because each sheet numbers independently from 1 to 15.

## Re-run with new maps

```powershell
& .\scripts\rmg\Invoke-RmgVisualReviewLoop.ps1 -Count 15 -StratifyFamilies -OutputRoot artifacts/rmg/visual-review/v10.1-family-next
& .\scripts\rmg\Invoke-RmgVisualReviewLoop.ps1 -Count 15 -OutputRoot artifacts/rmg/visual-review/v10.1-fresh-next
```

Inspect the packaged previews and diagnostics, fill each worksheet, then run
`Test-RmgVisualReviewGate.ps1 -OutputRoot <batch-directory>`.
Stop at any generation error or visual rejection; analyze, correct if within scope,
and restart a complete fresh batch after a correction.
