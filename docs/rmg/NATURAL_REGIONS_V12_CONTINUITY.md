# Regions V12: continuity across complexity levels

Status: frozen after user in-game acceptance on 2026-09-07. The user confirmed that continuity works as intended and authorized merging this checkpoint to main and pushing to origin before starting extended options on a new branch. Delivery build, automated stability, compatibility and native visual checks passed.

## Player behavior

The existing Natural Landscape (Regions) menu now generates V12 maps using player-settings schema 6. The available Complexity levels remain **Low / Standard / High**. Their names, presets, and Standard default remain unchanged. Extreme/Ultra and the proposed recalibration of the middle level are not implemented in this step.

For fixed seed, size and other settings, all three levels share their broad regional positions, orientations and scale. Complexity changes the contribution of smaller terrain features: inlets, local divisions, nearby pools and irregular slow-ground boundaries. Actual water, gravel and moss amounts remain separate controls. The completed native terrain can change connectivity; there is still no compulsory land-accessibility or competitive fairness rule.

Starting positions are selected relative to the same Low-complexity reference map. A start stays at its preferred location when valid, otherwise the generator selects the closest locally valid candidate that satisfies combat separation. It does not repaint terrain. Neutral colonies still use valid terrain and combat separation; their individual coordinates are not promised to remain fixed.

The previously requested visibility defaults are also applied: after the server selects the first generated map in a lobby, RMG initializes **Explored Map = true** and **Fog of War = false**. The controls remain editable and later regenerations preserve the user's choices. This is an RMG initialization, not a change to the shared authored-map rules.

The other defaults remain NORMAL, 256 x 256, four players, Balanced, Standard terrain amounts and colony density, Original Surface Relations On, and a fresh seed. Grey neutral-colony preview markers and the existing passable doodads remain.

## Construction and compatibility

`Reassessment/ContinuousRegions.cs` supplies the V12 priority construction. Region centers, radii, rotations, coarse distortion, and noise frequencies are independent of Complexity. The characteristic scale is 64 native cells for all three levels. The secondary detail uses fixed 24/12-native-cell frequencies and strengths 0/.65/1.15, with a reduced contribution near regional centers. Geological detail is damped to preserve interiors needed by the nested moss tile bank. These are implementation constants, not new player settings or feature-diameter guarantees.

Moss uses fixed broad geological and moisture priorities. At higher complexity it prefers the original reference moss sites wherever the completed geological envelope still permits them, and uses nearby legal sites for replacement coverage. This does not preserve moss on cells that become water or lose the surrounding gravel needed for a valid transition. Native tile normalization can also remove unsupported small arrangements. Consequently, individual moss patches can change substantially even when the broad water and combined gravel/moss geography stays recognizable.

Water priorities do not depend on gravel/moss quantity. The Low reference is reused for moss preference and start placement; higher levels do not regenerate or rank multiple candidate maps. There is no seed substitution, terrain repair for actors, forced route, or fallback to a historical generator.

V12 uses `normal-natural-regions-v12[-256]` profiles and schema 6. Schema 5 retains V11, including its original scale-changing behavior, and schemas 1-4 retain their previous meanings. The original Fields/Regions comparison path is unchanged. Continuity is a contract within V12; the same seed number does not imply an identical V11 and V12 map.

## Verification

Delivery evidence is under `artifacts/rmg/regions-continuity-v12/`. Earlier prototype and acceptance directories are retained as development evidence; **acceptance-final** is the delivery matrix.

- `delivery-validation.log`: supported `build-pipeline.cmd validate`, including engine/mod builds, static checks, MiniYAML/maps, Lua and asset inventory.
- `delivery-self-tests.log`: historical RMG self-tests plus V11/V12 schema separation, round trips, deterministic terrain, meaningful native changes, water-quantity independence, broad slow-terrain continuity and retention of still-valid reference moss sites.
- `acceptance-final/`: a predeclared 42-map matrix: 14 settings groups, each evaluated at Low/Standard/High. It covers 128/256, 2/4 players, seven seeds including zero and maximum UInt64, both surface-relation modes, and sparse/standard/dense colonies with lower and upper terrain amounts. Each map is generated with deterministic repeat checking and independently exported from its saved package.
- `acceptance-final/verification.json`: the durable verifier checks saved package hashes, native semantic hashes, player/neutral positions, package/native acceptance and repeat-generation evidence. Native overlap and coarse spatial measurements accompany the visual review; they do not reject maps during gameplay generation.
- `replay-final/summary.json`: ten stored V11 and two V10 maps reproduce their logical, actor, graph, canonical map and engine identities. The V11 maps include both sizes and both surface-relation modes.

All 42 final maps passed native/package validation and deterministic repeats, with every requested start and neutral colony placed. All 14 three-level native comparisons were visually inspected. Selected actual texture windows were inspected during the terrain review; these are saved-map exports, not live game screenshots. The public wrapper reproduced the accepted final High map on all five identities.

For the user's seed `397716241463670640` at 256, Water Standard and Gravel/Moss High, Low-to-High retains 69.2% of water cells (V11: 30.2%), 77.4% of combined gravel/moss cells, and 78.8% of moss cells. Player A-D displacement is approximately 7.2 / 10 / 0 / 2 native cells.

Across the 14 settings groups, Low-to-High water retention is 59.8-80.0% (median 71.7%). Combined gravel/moss retention is 63.0-82.3%. Thirty-nine of 52 starting positions are unchanged; the largest observed displacement is 25.1 native cells. These observations are evidence from this corpus, not guarantees for every seed.

Native transition-edge counts are not strictly monotonic for every seed: joins can offset newly introduced boundaries. One seed has almost unchanged total edge count despite visible terrain redistribution. The full five-level intensity calibration is deliberately deferred. Moss can change more than the combined geological region when water or a narrowed envelope removes its former valid interior; the regression test checks that still-valid reference sites are retained rather than enforcing an arbitrary universal moss-overlap threshold.

See `delivery-summary.json` for all surface metrics, measured pipeline timings and the compiled assembly hash. The pre-final runs and failed experimental checks are retained in their original directories and logs. Their results are not the delivery evidence.

## Reproduce and play

Restart the game through `launch-game.cmd`, open Skirmish -> Random Map Generator, and use Natural Landscape (Regions). For comparison with the user's reported case, enter seed `397716241463670640`, size 256 x 256, four players, Water Standard, Gravel/Moss High, Neutral Density Standard and Original Surface Relations On. Generate Low, Standard and High with the same seed. Saved maps identify their author as OpenSA RMG v12.

CLI equivalent:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 397716241463670640 -TerrainComplexity high -GravelMossAmount high
```

The wrapper defaults to 256/four players and now requests schema 6. Use the general player-settings wrapper with a retained schema-5 JSON file for exact V11 replay.

A repeatable verification matrix is available without additional Python packages:

```powershell
python .\scripts\rmg\Verify-RegionsContinuity.py .\artifacts\rmg\my-continuity-check
```

Use `--verify-only` with an existing matrix to validate its retained packages and measurements. The script does not overwrite an existing generation run.

The user subsequently tested this version in game and confirmed that it works as intended. This is user-provided gameplay acceptance; the agent did not automate live game control. V12 is the accepted three-level continuity freeze. Additional levels belong to the next branch and must preserve schema-6/V12 replay.
