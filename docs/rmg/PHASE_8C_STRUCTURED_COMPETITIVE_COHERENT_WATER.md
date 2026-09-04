# Phase 8C Structured Competitive coherent Water

## Status and naming correction

Implementation is complete for automated review. Human visual and gameplay sign-off remains pending. No merge or release claim is made by this document.

The first Phase 8C review used the name **Natural Landscape** for Generator Version 8. Visual review rejected that classification: Version 8 still uses the same exact-symmetry, route-first, colony-aware competitive scaffold as Version 7. Its principal difference is Water morphology: fewer, larger, coherent bodies. This document records the corrected taxonomy.

## Corrected layout-family mapping

- **Natural Landscape (planned)**: no implementation exists. Selection is visible but generation is rejected with a clear explanation.
- **Structured Competitive**: Generator Version 8. It adds coherent-field Water placement and larger bodies while retaining exact competitive symmetry and hard gameplay projections.
- **Artificial Battlefield**: frozen Generator Version 7. It uses distributed tactical pools and visibly systematic route/sector placement.

Terrain theme remains independent. NORMAL is supported; Desert, Swamp, and Candy remain UI placeholders. Open Fields and Contested Center are battlefield plans within either implemented family.

## Compatibility contract

Player-settings schema version 3 adds `layout_family`:

- `natural-landscape`: planned and rejected;
- `structured-competitive`: Generator Version 8 using internal topology `coherent-water`;
- `artificial-battlefield`: frozen Generator Version 7 using internal topology `parameterized-battlefield`;
- `preset`: Balanced resolves to Structured Competitive; Open Conflict and Tactical Crossroads resolve to Artificial Battlefield.

Schema versions 1 and 2 preserve their Version 6 and Version 7 generation contracts. Version 7 canonical identity does not acquire a layout-family field, so this naming correction does not change its generated topology.

## Version 8 generation pipeline

Structured Competitive Version 8 keeps gameplay structure authoritative:

1. Build exact player symmetry, starts, main/flank routes, contest regions, production exits, and protected strategic space.
2. Project capacity-safe neutral-colony anchors.
3. Build deterministic multi-octave coherent Water fields at logical scales 16, 8, and 4.
4. Attempt one dominant self-symmetric lake, then a bounded number of paired coherent basins.
5. Reject candidates that overlap protected route, start, colony, combat, production, symmetry, or shoreline constraints.
6. Materialize shoreline and open-Water templates and decorative details.
7. Run proxy and native movement, combat-space, production-exit, package, map-lint, and determinism gates.

The search is bounded to six coherent-field attempts and four Water-body orbits. Legacy deterministic stream identifiers are retained so the terminology correction does not alter seed output.

## Adaptive Water behavior

Water Amount is a requested quantity target, not permission to reject a safe map solely for missing an aesthetic percentage. Version 8 may accept a lower total only when at least 6 percent of logical cells remain Water and every safety invariant passes. It reports `WATER_DENSITY_TARGET_REDUCED`.

Interior Water has quality targets of 3, 6, and 10 percent for Low, Standard, and High, with hard floors of 2, 3, and 6 percent. A result between target and floor reports `WATER_INTERIOR_TARGET_REDUCED`. It is never accepted below the floor or without required interior-sector coverage.

## Morphology validation

Every Version 8 result reports body count, largest-body share, micro-body share, total/interior Water density, and interior sector coverage. The per-map envelope is intentionally broad: at most 12 bodies, at least 20 percent of Water in the largest body, and at most 15 percent in micro-bodies. The six-map deterministic comparison requires at least 35 percent median largest-body share.

The previous paired audit remains technically valid after relabeling because the algorithms did not change:

| Corrected family | Generator | Maps | Median Water | Median bodies | Largest-body share | Micro-body share |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Structured Competitive | V8 | 6 | 11.81% | 6 | 43.91% | 4.80% |
| Artificial Battlefield | V7 | 6 | 12.20% | 22 | 8.03% | 20.72% |

These metrics demonstrate larger coherent Water bodies. They do **not** demonstrate a natural landscape. Exact symmetry, predefined route bands, protected anchor masks, and shared land-cover materialization remain dominant visual constraints.

## Lobby behavior

The separate RMG panel exposes Layout Family in this order:

- Natural Landscape (planned);
- Structured Competitive;
- Artificial Battlefield.

Selecting Natural Landscape disables generation. Successful previews use the corrected family name in title, metadata, status, settings, and reports.

## Reproducible comparison

```powershell
./build-pipeline.cmd validate
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/rmg/Invoke-RmgSelfTests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/rmg/Prepare-Phase8cLayoutFamilyCorpus.ps1 -Overwrite
```

Corrected generated artifacts are written beneath `artifacts/rmg/phase-8c-structured-competitive/`.

## Explicit limitations and next-design boundary

No current algorithm produces organic landmass topology, semantic/asymmetric fairness, natural drainage, coastlines, river networks, watershed accumulation, biome succession, erosion, or terrain-first routing. Version 8 modifies Water candidate morphology inside a competitive scaffold; Version 7 is a more visibly engineered battlefield.

A genuine Natural Landscape implementation requires a new contract and likely a new generator stage. It must start from terrain formation and then fit fair gameplay into that terrain, rather than constructing gameplay geometry first and decorating the remaining cells. That future work must remain separate from this naming correction.