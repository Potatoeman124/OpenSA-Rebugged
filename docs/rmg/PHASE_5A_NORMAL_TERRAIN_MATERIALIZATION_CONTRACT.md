# Phase 5A NORMAL terrain-materialization audit and contract

## Status and decision

The Phase 5A audit is complete. No blocker prevents a transition-aware shoreline implementation, but the existing engine does not provide an automatic shoreline selector that the RMG can reuse.

Configuration `normal-water-blocking-v2` version 3 is the manually accepted combat-safe baseline and must remain identity-stable. Shoreline materialization must be introduced behind a new opt-in generator/profile identity until it passes automated and manual gates. It must not silently rewrite version 3 output.

The implementation phase may proceed under the constraints in this document.

## Legal and evidence boundary

The audit used three sources:

1. tracked OpenSA and pinned OpenRA source code;
2. tracked shipped map metadata and `map.bin` packages; and
3. the user's externally installed original assets, mounted by OpenSA from the support directory.

The repository and playable installation contain byte-identical `NORMAL` tileset definitions with SHA-256 `a46cf6c43c310b4ceb4eb78fc71d24d7274a17b2c9e011e1758bae35143139d5`. Representative `skirmish`, `Narrow_Passage`, and `Two_Armies` `map.bin` files are also byte-identical between the repository and `E:\Gejms\OpenSA`.

External DDF sprites and `OpenSA.PAL` remain user-supplied content. A locally rendered Water-template atlas is generated only under `artifacts/rmg/phase-5a-terrain-audit/`, which is ignored. The atlas, extracted sprites, original map packages, screenshots, and other asset-derived evidence must never be staged or distributed from this repository. Durable documentation may retain template identifiers, structural classifications, counts, and behavioral conclusions.

## Reproducible audit evidence

The pinned engine audited 125 shipped maps, of which 36 use `NORMAL`. The focused transition probe found:

| Evidence | Result |
| --- | ---: |
| `NORMAL` Water templates | 32, IDs `0` through `31` |
| `NORMAL` Clear interior templates | 5, IDs `32` through `36` |
| Water `PickAny` templates | `9`, `12`, `26`, `28`, `29` |
| Fixed Water templates | 27 |
| Mixed Clear/Water templates | `0`, `2`, `3`, `5`, `16`, `18`, `19`, `21` |
| Homogeneous-Water fixed templates | 19 |
| Water templates used by authored maps | 32 of 32 |
| Fixed Water templates found as canonical 2x2 stamps | 27 of 27 |
| Canonical fixed-template stamps | 9,876 |
| Horizontal canonical adjacencies | 5,257 observations, 137 distinct ordered pairs |
| Vertical canonical adjacencies | 4,806 observations, 140 distinct ordered pairs |
| Noncanonical fixed-template native cells | 159 |

The 159 noncanonical cells occur only in four hand-authored maps: `Custom_Mission_Skull` (11), `Custom_Mission_Usual_Brawl` (3), `shellmap` (114), and `skirmish` (31). They are useful historical exceptions, not a generator target. Generated maps must use complete canonical 2x2 stamps.

Authored adjacency frequency is calibration evidence, not a complete compatibility whitelist. An unobserved pair is not automatically invalid, and a frequently observed pair is not automatically correct for every local shoreline shape.

## Engine and format findings

### Binary representation

`map.bin` stores each native terrain cell as a `ushort` template type plus a `byte` frame index. Tile records are written in X-major then Y order after the binary header. For a canonical 2x2 stamp, indexes are:

```text
0 1
2 3
```

The legacy OpenSA importer reads one source terrain value per 2x2 macro-cell and expands it into these four native records. For `NORMAL`, the imported source value is converted directly to template ID after the legacy offset is removed.

### Active terrain implementation

OpenSA declares `TerrainFormat: CustomTerrain`. Its custom terrain loader, renderer, tile cache, and editor brush are authoritative; the generic OpenRA terrain implementation is not the complete runtime model.

The custom editor brush stamps the template selected by the author. It does not inspect neighboring cells and does not select or repair transitions automatically. Therefore the RMG needs its own transition catalogue and materializer.

### `PickAny` behavior

At map creation, `CustomTerrain.MapCreated` visits 2x2 macro-cells and replaces a `PickAny` stamp with a randomly selected `PickAny` template having the same terrain type. The RNG is seeded from `Environment.TickCount`.

Consequences:

- serialized map/package identity remains reproducible;
- Clear and deep-Water gameplay semantics remain unchanged;
- interior texture choice may differ between loads; and
- cosmetic interior variation is not guaranteed to mirror the map's strategic symmetry.

Phase 5B must not expand into an engine-wide change to this legacy cosmetic behavior. Fixed shoreline templates are not `PickAny`, so shoreline geometry and orientation can remain deterministic. Interior `PickAny` variation may remain load-random as a documented cosmetic exception. A deterministic cosmetic seed, if later desired for screenshot reproducibility, is a separate engine task.

### Template semantics are insufficient for visual selection

All Water templates are categorized as Water, but only eight expose mixed Clear/Water native terrain metadata. The atlas shows that many homogeneous-Water templates still contain visible sand banks, corners, small islands, reeds, or shoreline detail. Template category and native terrain type therefore cannot identify visual role or edge compatibility by themselves.

Selecting a random template from the Water category is invalid. A dedicated visual-role and edge-compatibility catalogue is required.

## Frozen materialization contract

### Inputs and authority

1. The current symmetric logical obstacle mask remains the strategic planning input.
2. The materializer operates on canonical 2x2 macro-stamps.
3. A local neighborhood classifier may use at least the surrounding 3x3 macro-cell window; a four-neighbor terrain label alone is not assumed sufficient.
4. The selected fixed template and orientation define the final native terrain cells.
5. Reloaded `Map.GetTerrainInfo` results are the final passability authority.

Mixed Clear/Water templates may move the effective passability boundary by one native cell inside a 2x2 stamp. Logical obstacle flags must therefore not be treated as proof that all four materialized native cells are Water. Existing logical-versus-native checks must be revised to compare the intended shoreline contract with the actual materialized native mask, followed by the existing authoritative movement gate.

### Transition catalogue

Phase 5B must introduce an explicit project-owned catalogue containing, for every fixed Water template used by the materializer:

- numeric template ID;
- visual role, such as edge, outer corner, inner corner, isolated feature, or fixed detail;
- native terrain types for frames `0` through `3`, loaded and verified from the active tileset;
- north, east, south, and west visual edge signatures;
- horizontal-mirror, vertical-mirror, and 180-degree rotation mappings; and
- whether the template is permitted on a generated shoreline or retained only as authored-map evidence.

The table may contain identifiers and original analysis but no copied pixels. It must be checked against the active `NORMAL` tileset at startup or validation time so upstream metadata drift fails clearly.

### Selection and symmetry

1. Fixed shoreline selection must be deterministic for the generator seed and settings.
2. Neighboring fixed stamps must have compatible visual edge signatures.
3. Mirrored or rotated strategic geometry must use the corresponding transformed template ID, not blindly reuse the same oriented ID.
4. Fixed shoreline geometry must preserve the selected map symmetry.
5. Load-random `PickAny` interior texture variation is exempt from cosmetic pixel symmetry because it cannot affect passability or package identity.
6. Cosmetic selection must use a dedicated random stream and must never perturb starts, colonies, routes, chokepoints, repairs, or combat-space placement.

### Unsupported neighborhoods and repair

The materializer must never publish an arbitrary best-effort tile when no compatible template exists. It must either:

1. apply a bounded deterministic topology repair that preserves route, colony, density, and symmetry constraints; or
2. reject the candidate with a typed terrain-materialization failure.

Safe bounded rejection is preferable to a visible seam, passability mismatch, or asymmetric repair.

### Baseline preservation

The manually accepted configuration version 3 output remains frozen. Phase 5B must use a new opt-in profile or generator identity. Promotion may occur only after the new identity passes its full gate; the old baseline must remain reproducible during development and regression testing.

## Phase 5B implementation boundary

The smallest acceptable implementation slice consists of:

1. a `NORMAL` Water transition catalogue with transform and compatibility tables;
2. a deterministic shoreline materializer operating after logical topology construction and before final native validation;
3. a materialized native terrain-intent layer or equivalent validation model;
4. typed failures and metrics for unsupported neighborhoods, repairs, transition usage, and native semantic disagreement;
5. debug output showing logical Water, selected template IDs, fixed shoreline roles, and final native passability; and
6. an opt-in profile/CLI path that leaves the accepted version 3 identity unchanged.

Rock, Vegetation, free-standing decoration actors, new strategic archetypes, engine-wide `PickAny` seeding, and general map-layout redesign are outside this slice.

## Acceptance gate for Phase 5B

Automated acceptance requires:

- active-tileset verification for every catalogue entry and transform;
- focused fixtures covering every permitted fixed shoreline template and every symmetry transform;
- no incomplete 2x2 stamps in generated output;
- compatible visual edge signatures for every neighboring fixed stamp;
- deterministic package hashes and fixed shoreline IDs for repeated generation;
- reload validation against actual native terrain types;
- proxy and authoritative native movement acceptance;
- route-width, chokepoint, colony-access, and combat-space invariants unchanged;
- the five configuration version 3 combat-space regression identities retained as a comparison baseline;
- broad seed fuzzing for the new opt-in identity; and
- asset-policy verification proving that no external sprites, atlas images, or original map packages are staged.

Manual acceptance requires a newly installed visual corpus covering open and central-contest layouts, all supported symmetries, low and maximum colony density, shoreline corners, channels, and isolated Water features. The reviewer must confirm that shores are coherent, no raw hard seams remain, and gameplay behavior still matches the accepted baseline.

## Reproduction

All outputs are ignored evidence:

```powershell
build-pipeline.cmd build
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapCorpusAudit.ps1 -OutputDirectory artifacts\rmg\phase-5a-terrain-audit\corpus-baseline
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-TerrainTransitionAudit.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Export-NormalWaterTemplateAtlas.ps1
```

The atlas command requires the user's externally installed original assets. Its PNG output is local evidence only and must not enter Git.
