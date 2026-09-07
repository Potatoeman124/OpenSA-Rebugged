# Regions V13: extended Terrain Complexity

Status: implemented and built on `codex/rmg-extended-options`; automated delivery and native visual checks passed. User gameplay
acceptance of V13 remains separate from the accepted V12 freeze.

## Accepted checkpoint and scope

The user confirmed V12 continuity in game and requested merging to main, pushing to origin, then starting
extended options. Main and origin/main were verified at `c34387a6f12a232418e80df6ba89d71fc8e13480` before this
branch was created. The merged main build/runtime-data validation passed. V12 is unchanged and replayable.

This iteration extends **Terrain Complexity** as specified in the approved continuity analysis. Water
Amount, Gravel and Moss Amount and Neutral Colony Density retain their existing ranges. It does not add
connectivity requirements, change native surface relations, or turn doodads into generation blockers.

## Player behavior and calibration

| Level | Calibration | Broad detail strength | Finer detail strength |
| --- | --- | ---: | ---: |
| Small | V12 Standard terrain; less empty than old Low | 0.65 | 0 |
| Medium | V12 High terrain; default | 1.15 | 0 |
| High | More inlets, divisions and secondary patches | 1.40 | 0.40 |
| Extreme | Stronger interacting local features | 1.65 | 0.85 |
| Ultra | Highest calibrated detail in this release | 1.90 | 1.30 |

These coefficients are implementation values, not feature diameters or additional user controls. Fixed
region centers, radii, rotations, characteristic scale (64 native cells), seed streams and detail frequencies
are independent of complexity. The existing secondary band uses 24/12-native-cell noise scales. Above
Medium a fixed 12/6-native-cell band also contributes. Geological detail receives 60% of the water detail
strength to keep usable interiors for the nested moss tile bank. Detail has less influence at region centers.

The first prototype increased only the existing band's strength up to 2.35. It preserved geography but
mostly enlarged deformations at the upper levels. The selected calibration adds the finer band to make
High/Extreme/Ultra visibly more distinct without changing the seed's regional arrangement.

All levels use the same V12 Low reference for moss preferences and preferred player starts. A structure
moves to a locally valid site when its reference position is invalid; terrain is never repainted for it.
Neutral coordinates may change. Moss can disappear from an old location when water or the changed gravel
envelope removes its legal interior. Water/land area targets remain separate from complexity, and materialization
may simplify unsupported fine shapes. More complex does not guarantee more connected components in every
seed: features can both join and split. Ultra is an intensity setting, not a global land-accessibility promise.

The live RMG dropdown, presets, default, status text, generated map title, saved player settings, normalized
report, canonical identity, profile selection and Regions PowerShell wrapper all use the five-level path.
Balanced selects Medium, Open Conflict selects Small and Tactical Crossroads selects High. Existing defaults
remain NORMAL, 256 x 256, four players, Standard quantities, Original Surface Relations On, grey neutral
preview squares, Explored Map On and Fog of War Off at the first generated-map selection.

## Versioning and reproduction

Player schema 7 selects Generator V13 and accepts `small`, `medium`, `high`, `extreme`, `ultra` for
`terrain_complexity`; an omitted complexity defaults to Medium. Schemas 5 and 6 retain only
`low`, `standard`, `high` and retain V11/V12 output. The shared internal enum retains its first three values
for historical compatibility; version-aware serialization supplies the new public names. Legacy direct
settings also reject Extreme/Ultra. Structured Competitive and Artificial Battlefield keep their old schemas
and controls even after switching away from Natural Landscape in the lobby.

Profiles are `normal-natural-regions-v13.yaml` and `normal-natural-regions-v13-256.yaml`. The standalone
Fields/Regions reassessment command remains a historical three-level experiment; use `--player-evidence`
with schema-7 settings or `--reference` on a saved V13 map for current evidence.

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 397716241463670640 `
    -MapSize 256 -Players 4 -TerrainComplexity ultra -GravelMossAmount high -VerifyRepeatability
```

`Invoke-RegionsMapGenerator.ps1` now defaults to V13 Medium. Historical replay uses the generic
`Invoke-MapGenerator.ps1 -PlayerSettingsPath ...` with the saved schema-5/schema-6 JSON, not renamed levels.

## Verification record

Retained evidence is under `artifacts/rmg/regions-extended-v13/` (ignored by Git):

- `prototype-01/`: ten maps from the coarse-band-only calibration; not final delivery evidence.
- `prototype-02/`: ten maps from the selected two-band calibration, including native textures.
- `acceptance-final/`: a predeclared 90-map matrix, with settings, generation logs, repeatability checks,
  native validation, reloaded package exports, semantic hashes and measurements.
- `replay-final/`: all 42 accepted V12 maps plus ten V11 and two V10 saved references, compared using
  logical/actor/graph hashes, engine UID and canonical map hash, plus canonical settings for V11/V12.
- `delivery-validation.log` and `delivery-self-tests.log`: supported build/data validation and regression suite.

The matrix fixes its seeds and settings before generation. It covers five levels, both 128/256 sizes,
2/4 players, both surface-relation modes, and dense colonies with high water and gravel/moss, including the
user's original seed, zero and the maximum unsigned 64-bit seed. Native tests validate surface semantics,
footprints, production exits and combat separation; no global connection rule is introduced. The regression
suite also checks exact Medium-to-V12-High native terrain/template equality, schema isolation, unique level
identities, deterministic Ultra replay, distinct adjacent levels and fixed water geography across land quantities.

Delivery results:

- **90/90** maps passed deterministic repeats, package lint/reload, native terrain and actor validation.
  Every map achieved its full requested neutral-colony count.
- **54/54** historical replays matched: 42 V12, ten V11 and two V10 cases.
- **28/28** cross-version calibration comparisons matched native terrain plus logical/actor/graph hashes:
  new Small versus V12 Standard, and new Medium versus V12 High, across the 14 shared matrix groups.
- All 90 native semantic maps were visually reviewed; selected actual textures and 32-cell crops were checked.
- Median water-boundary edges at 128: **382 / 414 / 514 / 588 / 686** from Small through Ultra;
  at 256: **1408 / 1554 / 1758 / 2066 / 2288**. Boundaries alone are not a gameplay-quality score.
- Small-to-Ultra water retention ranged **44.5-75.4%**, median **64.6%**; coarse 32-cell water-distribution
  correlation ranged **0.635-0.956**, median **0.851**. Adjacent levels retained at least **79.3%** of water.
  The largest change was seed `8973695974549234062` at 128: a lower basin split and receded while nearby
  pools grew. The broad regional arrangement remains recognizable, but Ultra can substantially change
  individual bodies and connectivity. This is a measured limitation, not an exact-shoreline guarantee.
- Combined gravel/moss coarse correlation ranged **0.778-0.985**, median **0.903** from Small to Ultra.
  Maximum absolute coverage deviations from requested water/gravel/moss were **1.50 / 0.10 / 0.05**
  percentage points. Native simplification of fine water arrangements caused the largest shortfall.
- Maximum player-start displacement from the shared reference was **27.8 native cells** at Ultra;
  46 of the 64 Ultra starting positions needed no relocation. All final sites satisfied local checks.
- Matrix generation/validation time excluding the deliberate second generation was median **0.44 s**
  (max **0.50 s**) at 128 and median **1.10 s** (max **1.26 s**) at 256. Other validation jobs were running
  concurrently. The isolated final 256 wrapper checks took **0.75 s** for Medium and **0.73 s** for Ultra.
  These are utility pipeline timings excluding process startup, not measured lobby frame latency.
- Supported `build-pipeline.cmd validate` and the complete RMG regression suite passed. Final CLI help
  changes were rebuilt; both the default-Medium and explicit-Ultra PowerShell wrappers reproduced their
  corresponding delivery identities with repeatability enabled.

`delivery-summary.json`, `acceptance-final/verification.json` and `acceptance-final/measurements.json`
retain the evidence. `Render-RegionsExtended.py` renders the comparisons and grey-marker texture overlays.
A report-adapter issue in the replay helper was corrected to read V10's historical hash field names;
no generator change or identity mismatch was involved.

Live lobby/gameplay testing of V13 is still a user review step; automated package/native validation does
not stand in for playing the new upper levels. Main/origin retain the accepted V12 freeze.
