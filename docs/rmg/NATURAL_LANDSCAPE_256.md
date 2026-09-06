# Natural Landscape: 256 x 256

Status: implemented from main `0f2180e` and merged as `5b43f63` after the user accepted all 15 presented maps. Full-match gameplay validation remains separate. The subsequent [generation-speed pass](GENERATION_SPEED_PASS.md) preserves those map identities and supersedes the generation timings below.

## Scope

- A true 256 x 256 playable NORMAL map, backed by a 128 x 128 logical lattice of existing 2 x 2 native templates.
- Two or four players, Natural Landscape V10 only. Artificial Battlefield V7 and Structured Competitive V8 remain at 128 x 128. 64 x 64 and other tilesets remain planned.
- The existing 128 x 128 Natural Landscape path and its default schema-3 seed mapping are preserved.
- Natural terrain remains asymmetric. This work adds no equal-travel-time or competitive fairness requirements.
- Existing native building footprints, production exits, combat buffer and passage widths do not scale with map size.
- With Original surface relations on, completed surfaces remain authoritative: starts and colonies fit existing dirt. Placement must not clear gravel, moss or water.

## Generation changes

The new profile is `mods/sa/rmg/normal-natural-landscape-v10-256.yaml`.
The playable area is four times larger; the stored map is 260 x 260 including the unchanged two-cell cordon.

Provisional anchors use the larger playfield, but remain preferences rather than enforced terrain geometry. Lake District and Wetlands use more basin sources. Geology has more region sources. Basin sizes, river wavelength/amplitude and coast curvature receive limited macro-scale changes; native detail wavelengths and templates are not doubled. This is not an upscaled 128 x 128 raster.

Colony targets are three times the equivalent 128 x 128 target. Requested targets remain distinct from actual feasible placement. Density settings explicitly selected in the UI resolve as follows:

| Players | Sparse | Standard | Dense |
| --- | ---: | ---: | ---: |
| 2 | 24 | 30 | 60 |
| 4 | 36 | 48 | 72 |

The preset-dependent target remains available when colony density is not explicitly overridden. Existing adaptive reduction still applies; no extra regeneration loop was added to chase colony counts.

The connected water-region capacity scales from 1024 to 4096 logical cells, preserving the same area fraction. The water-body count allowance rises from 12 to 24 alongside the doubled basin-source count. Minimum region size remains unchanged. The large-map terrain-first start search preflights eight dry native exit sectors at radius 12, leaving headroom for subsequent static footprints above the unchanged native six-sector gate. This chooses a different site instead of carving a start clearing.

## Settings and UI

Select Natural Landscape, then 256 x 256 in the existing RMG Size dropdown. Other unsupported size/family combinations are explained by the existing disabled-generation status.

Schema 4 adds `"size": "256,256"` or `"size": "128,128"`. An omitted size defaults to 128 x 128. Schema 1-3 JSON rejects an explicit size field; their old omitted-size behavior remains unchanged. The UI continues to emit schema 3 for 128 x 128.

CLI low-level settings also accept `--size 256,256`, restricted to `--topology natural-terrain-v10`. Large map titles include the size. Profile ID and non-default size participate in deterministic identity.

## Offline verification

```powershell
.\scripts\rmg\Invoke-RmgVisualReviewLoop.ps1 -MapSize 256 -Count 15 `
    -StratifyFamilies -AlternatePlayers `
    -OutputRoot artifacts/rmg/size-256/review-02
```

This uses the same player-settings resolver, generation/package path and both movement validators as the lobby. It is not a claim of clicking a live game UI or playing a match.

Each run retains its random seed, settings, map package, actual packaged preview, diagnostic preview, report and log. Five morphology families are covered with three fresh seeds each, alternating four/two players. Individual visual review is mandatory; a mechanical pass is not a visual accept.

The existing V1-V10 self-tests include schema-size rejection/round-trip checks and a focused large-profile geometry/clearance check. Two 128 x 128 seeds were captured before implementation for exact after-change comparisons:
`656060532586988781` and `975197840522651550`.

## Development failures and timing

The first large seed, `522547010532288443`, exposed an inherited 1024-cell lake capacity. After scaling the capacity, native validation exposed a five-sector start. The terrain-first site preflight corrected that case without weakening native validation or modifying terrain. Fresh-batch seed `720895329115855828` then exposed the old 12-water-body limit (19 bodies on the large map); that batch stopped at run 3 and was not accepted. The large-map body-count allowance was corrected before restarting validation.

Large-map timing is recorded separately from wrapper overhead. `performance.total_ms` includes a full deterministic repeat generation, packaging, reload/lint and native validation, as did the shared lobby path at that checkpoint. The subsequent speed pass moves the duplicate generation to an explicit audit mode. Candidate-stage measurements under `validation.metrics.large_candidate_*_ms` describe only the chosen candidate, not all attempts or the full repeat.

The redundant full-map water count inside each lake-growth candidate check is now computed once per unchanged operation. Broader performance work is outside this feature. Large maps remain substantially slower than 128 x 128. The uninterrupted standard-settings batch took 45.2-96.7 seconds per map (median 76.0 seconds), including the full repeat and validation; timings were collected without other generation tests running. This is a significant current limitation, not an optimized-release claim.

## Verification record (2026-09-06)

- Release build: zero warnings/errors. Final V1-V10/inherited-baseline/native-validator self-tests pass.
- Final 128 x 128 repeats match all five identity hashes (logical, actor, graph, canonical YAML/BIN, engine UID) for both preserved seeds; see `artifacts/rmg/size-256/delivery-128/size-validation.json`.
- Additional settings probes pass generation, native/proxy validation, actual package geometry and visual inspection: Low water/Low tactical/Sparse colonies/2 players/Original on (24 of 24 colonies); High water/High tactical/Dense colonies/4 players/Original off (68 of 72 colonies). These were concurrent regression probes, not timing benchmarks.
- Fresh batch: `artifacts/rmg/size-256/review-02/`; 15/15 mechanical passes and 15/15 individually reviewed visual accepts.
- Coverage: three fresh seeds per each of the five morphology families, eight four-player and seven two-player maps. Standard water, tactical terrain and colony density; Original surface relations enabled.
- Actual package bounds: all 15 verified as 256 x 256 playable, 260 x 260 stored. Both native and proxy gates pass.
- Final placement changed zero native surface, template or water cells in every reviewed map; native dirt-only start and colony footprint checks pass.
- The complete worksheet retains minor template-scale visual blemishes noted on maps 2, 8 and 15. Assistant acceptance does not replace user review; the user subsequently accepted all 15 presented maps.
- The earlier `review-01` failure and diagnostic seeds are retained outside the accepted review batch; no favorable replacement seeds were spliced into the successful 15.
- `scripts/rmg/Test-RmgMapSize.ps1` verifies actual packaged geometry and can compare all five baseline identity hashes.

## Remaining boundaries

- Existing 2 x 2 transition templates still impose native stair steps; this is not a finer-than-template renderer.
- Colony requests are targets, not a guarantee.
- Full-match AI and long-session runtime testing are separate from generation/native static movement validation.
- Chokepoint controls, extra player counts, other terrain themes and broader optimization are not included.
