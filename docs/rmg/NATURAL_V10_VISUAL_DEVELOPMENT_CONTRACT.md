# Natural Landscape V10 visual development contract

Status: the user approved the V10 checkpoint for integration on 2026-09-05 after the terrain and surface-authoritative placement iterations. Natural Landscape retains its experimental UI label. See [the placement contract and verification record](NATURAL_V10_SURFACE_AUTHORITY.md) for the approved state and remaining limits.

Baseline: `main` merge `fdfc546` freezes Generator Versions 7 and 8 and checkpoints the reliable but visually rejected Natural Landscape V9 implementation. V10 must not change the deterministic output or contracts of those frozen versions.

## Purpose

V10 replaces the V9 composition pipeline rather than tuning it. A successful V10 map must satisfy both independent gates:

1. mechanical acceptance: generation, package reload, lint, proxy movement validation, and native movement validation;
2. visual acceptance: direct inspection of the actual packaged `map.png` plus topology diagnostics.

Mechanical acceptance never implies visual acceptance.

## Required architecture

- Generate geography before choosing starts, colonies, and routes.
- Preserve continuous terrain priorities as the semantic source of truth through projection and materialization instead of reducing an external prototype with a majority mask before composition.
- Use several morphology families with materially different macro composition, including inland basins, connected lakes, coast or peninsula, river or floodplain, and fragmented wetlands.
- Treat Battlefield Plan as a gameplay-placement preference, not a terrain-shape template.
- When Original surface relations is enabled, create nested surface regions by construction:
  - Water belongs to Dirt and cannot contact Gravel or Moss;
  - Moss is an interior subset of a sufficiently large Gravel region;
  - Gravel is not painted as a one-cell repair halo around an independently generated Moss mask;
  - candidates without space for a credible transition are removed or regenerated.
- Apply route and colony repairs only after terrain synthesis and report their visual footprint.

## Offline visual feedback loop

Generate the default 15-map corpus without opening OpenSA:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgVisualReviewLoop.ps1 `
    -Count 15 -OutputRoot artifacts\rmg\visual-review\natural-v10-current
```

The command retains, for every seed:

- UI-equivalent player settings;
- the generated `.oramap`;
- the complete validation report and generation log;
- the actual packaged `map.png`;
- a route, clearance, repair, and blocker debug preview.

It also creates actual-map and diagnostic contact sheets plus `visual-review.csv`. Every successful generation begins with `visual_status=UNREVIEWED`.

After inspecting every row and filling the worksheet, enforce the visual gate with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Test-RmgVisualReviewGate.ps1 `
    -OutputRoot artifacts\rmg\visual-review\natural-v10-current
```

The gate exits non-zero when any row is incomplete, inconsistent, or visually rejected. A rejected map returns to implementation; it is not silently replaced with another favorable seed.

## Visual axes

Each mechanically successful map is reviewed for:

- `natural_shapes`: organic macro geography rather than rectangles, corridors, or obvious construction;
- `boundary_quality`: no dominant staircase, long orthogonal boundary, square basin, or coarse-grid signature;
- `surface_relations`: transitions look like composed regions rather than mandatory thin halos;
- `layout_distinctiveness`: the map is materially different from other seeds in the same corpus.

An ACCEPT requires every axis to be accepted. A REJECT requires a concrete note identifying the feedback for the next implementation iteration.

## Pre-user-review rule

Before preparing maps for user review:

1. draw 15 fresh random seeds using the player-facing 18-digit maximum;
2. generate every map through the normal map-package path with both movement validators;
3. stop and analyze the first mechanical failure;
4. visually inspect every mechanically successful map;
5. stop and iterate on any visual rejection;
6. restart the complete 15-map test from run 1 after any functional or visual correction;
7. prepare a user-facing review corpus only after one uninterrupted batch receives 15 mechanical passes and 15 visual accepts.

The ignored V9 negative-control corpus at `artifacts/rmg/visual-review/v10-harness-smoke/` verifies the gate: all three maps generated successfully, all three were visually rejected, and the gate blocked delivery.

## Development sequence

1. Offline generation, contact-sheet, worksheet, and enforced visual gate.
2. V10 geography and nested-surface prototype, kept outside the player-facing UI.
3. Actual map-package materialization through the same visual loop.
4. Terrain-aware start, colony, and route embedding with bounded repair telemetry.
5. Fresh 15-map mechanical and visual acceptance campaign.
6. Only after acceptance, promote V10 to the Natural Landscape UI selection.

## Current first-iteration limitation

The NORMAL materializer still selects 2x2 native transition templates: a 64x64 logical semantic lattice for 128x128 maps, or a 128x128 lattice for the [256x256 extension](NATURAL_LANDSCAPE_256.md). V10 now generates continuous-valued fields directly on that lattice and retains their priorities through terrain-aware gameplay embedding, but it does not yet assign 128x128 native terrain semantics independently. Finer-than-template morphology would require a separate transition-catalogue and materializer expansion rather than another field-weight adjustment.

## V10.1 candidate and boundary revision

The previous V10 batch received 10/15 user accepts. Its earlier assistant 15/15
visual gate was too permissive and is superseded by that review. The labels guide
visual criteria, but the user's validation contract explicitly calls for new maps;
replaying that particular batch is not a required acceptance step.

V10.1 selects a family once from the displayed seed. All twelve terrain candidates
retain it. A cheap projection ranks candidates by boundary shape and disconnected
dry regions, then up to three mechanically valid results are compared after full
terrain materialization. The final score includes water/gravel/moss boundary risk
and the symmetric difference between projected and repaired water. Every metric is
reported under `validation.metrics.natural_*`; low risk is not visual acceptance.

Coasts use a rotated curved field with varying curvature instead of one of four
cardinal planes. Interior presence is encouraged by a smooth radial field rather
than exchanging cells across a rectangular window. Geological attractors can
continue outside the map and have their own domain warp. The V10 materializer
constructs the gravel envelope before fitting a moss core with a varying inset.
The native transition catalogue and all frozen V1-V9 paths remain intact.

Descriptors exclude the outer map frame and normalize lengths to a 128-cell map.
They include longest straight boundary, share in runs of at least 12 cells,
repeated coarse run lengths, largest-body bounding-box occupancy, and small-body
share. These are diagnostics and ranking terms, not hard universal naturalness laws.

### Fresh validation

Run a fresh family sample with three randomly drawn seeds per family:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-RmgVisualReviewLoop.ps1 `
    -Count 15 -StratifyFamilies -OutputRoot artifacts\rmg\visual-review\v10.1-family-current
```

Family stratification only filters seeds by their pre-generation family hash.
It never selects by generation success or appearance. The wrapper checks the
resulting family against that same fixed hash after every generation.

Then run a separate, unrestricted batch of fifteen new random seeds through the
default preview loop. Inspect every resulting packaged preview and record concrete
notes on all four axes before enforcing the visual gate. A correction requires a
new complete batch. The existing focused suite also checks all five families across
twelve retry indices, boundary metric controls, and same-seed generation determinism.
