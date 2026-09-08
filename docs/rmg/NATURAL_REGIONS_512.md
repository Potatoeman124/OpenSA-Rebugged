# Regions 512 x 512 extension

## Scope (2026-09-08)

Branch: `codex/rmg-512-map-size`, from accepted main `5742b43`.
The user requested a new branch to try 512 x 512 RMG maps. This adds the size to current Regions V16,
configuration 1 / player-settings schema 10, across Normal, Desert, Swamp and Candy. It does not alter
older map sizes or their defaults. In-game acceptance remains pending.

## Size and gameplay contract

- The Size dropdown and Regions wrapper accept **64, 128, 256 and 512** square playable maps.
  64 remains limited to 1-4 players; the other three sizes support 1-8. Default size remains 256.
- 512 requires schema 10 and Natural Landscape. Historical schemas, layouts and the legacy direct CLI
  retain their existing size restrictions. Example JSON: `"size": "512,512"`.
- The 512 profile uses 256 x 256 logical 2x2 stamps, a 512 x 512 playable area, and the existing two-cell
  border on each side. Saved dimensions are 516 x 516, with `Bounds: 2,2,512,512`.
- Terrain features keep the accepted scale in native cells. Native colony/start footprints, turret
  clearance, production exits, doodad spacing and movement costs retain their existing dimensions.
  The larger map therefore contains more terrain, rather than stretching individual features.
- Colony targets extend the existing threefold increase per doubling of side length: 512 targets are
  **3 times the 256 target**, or 9 times the 128 target. Targets still describe requested colonies;
  valid physical placement and the chosen overlap policy decide the achieved count. Species weights,
  zero-weight exclusion, ownership shares and both ownership choice modes retain their contracts.
- Same-seed continuity across complexity choices remains governed by the accepted Regions logic.
  Changing map size is not a promise to embed or enlarge the exact smaller map.
- All four biome mappings and colored/grey colony-preview behavior remain available.

| Neutral Colony Density | 512, four players | 512, eight players |
|---|---:|---:|
| Sparse | 108 | 180 |
| Standard | 144 | 252 |
| High / Dense | 216 | 288 |
| Extreme | 360 | 504 |
| Ultra | 576 | 792 |

## Engine and implementation audit

The engine does not impose a 256-cell map ceiling. `OpenRA.Game/CPos.cs` stores signed 12-bit X/Y
coordinates, allowing -2048 through 2047. `Map.Save` stores dimensions as unsigned 16-bit values.
516 stored cells fit both representations. `TerrainSpriteLayer` allocates geometry from actual map
width/height, and `CustomTerrainRenderer` updates every stored cell. Tile graphics are cached by tile
template, not as one map-sized texture. No engine or renderer modification is required.

The extension touches current settings parsing/count bounds, the Regions profile size contract, the
terrain-construction size guard, the lobby size selector and the Regions wrapper. The shared shoreline
emitter uses its accepted large-map template bank for 512 and loops over the actual logical map.
The new `normal-natural-regions-v16-512.yaml` supplies geometry; the existing biome adapter derives the
other three themes from it. Generator/version/schema identity for accepted smaller maps is unchanged.

## Validation plan

1. Verify settings round trips for 1-8 players at 512, invalid historical selections, count scaling and
   the existing small-map cap with `--validate-sa-rmg --regions-ownership`.
2. Generate/reload/lint repeated 512 packages across all four themes, all five complexity levels,
   quantity extremes, solo/eight players, species exclusions and both ownership/spacing policies.
   Compare theme geometry and replay representative accepted 64/128/256 packages exactly.
3. Exercise real size/ownership widgets, native world initialization twice per case, ownership-preview
   agreement, actual AI/server startup, and rendering at the center and all four corners.
4. Record generation timings, inspect textures/preview/screenshots, run repository validation and leave
   a Release build for user testing. These checks do not claim long-session performance or gameplay balance.

## Reproduction

From the repository root:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 397716241463670640 -MapSize 512 -Players 4 `
    -Tileset DESERT -StartingColonyShares 0,10,20,30 -VerifyRepeatability

python scripts/rmg/Verify-Regions512.py artifacts/rmg/regions-512/native-new
```

The replay matrix requires the accepted `artifacts/rmg/regions-biomes/native-01` package corpus.
`--verify-only` checks an existing matrix without regenerating it.
The runtime command is `OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT_DIRECTORY --wide --512`
using the normal local engine/runtime environment; it uses isolated test worlds and does not save user settings.

## Results

- Focused settings/ownership contract: PASS. This includes 28 valid size/player round trips across
  64/128/256/512, all 1-8 counts at 512, colony scaling, historical schema/layout/direct-profile rejection,
  and the existing ownership and biome checks. Log: `artifacts/rmg/regions-512-contract.log`.
- Native matrix: **13 accepted 512 maps** across four themes, all five complexity levels, three seeds,
  solo/eight players, custom wasp-only weights, both ownership modes and strict/relaxed placement.
  All pass native terrain/footprint/exit validation, package lint, reload and repeatability. Biome-only
  changes preserve exact native terrain and actor positions. **Six accepted smaller packages** (64,
  128 and 256 across all four themes) replay byte-for-byte, including their previews and metadata.
  Evidence: `artifacts/rmg/regions-512/native-01/verification.json`.
- Runtime: **four cases / eight live-world initializations** pass, including Normal with hostiles,
  a crowded Desert map with 510 colonies, Swamp solo and Candy with seven AI opponents. Ownership
  quotas and colored previews agree; repeated worlds agree. The crowded case passes actual loopback
  server startup and repeated generated-map selection. Each biome also rendered the center and all
  four corners (20 screenshots), exercising geometry beyond the earlier 256 limit.
  Evidence: `artifacts/rmg/regions-512/runtime-01/verification.json` and screenshots.
- The actual lobby size selector exposes 512 with eight players and an enabled Generate Preview
  button. Selecting 64 still clamps to four; returning to larger sizes restores the eight-player range.
  Screenshot: `runtime-01/rmg-512-eight-players.png`.
- Repository validation completed with zero build errors and the existing style warnings. The final
  playable Release build completed with zero warnings/errors. Logs:
  `artifacts/rmg/regions-512-final-validation.log` and `artifacts/rmg/regions-512-final-release.log`.

On this workstation, the standard four-player case's generation phase took about **1.5 seconds**;
8-player High about **3.7 seconds**, Extreme **5.5 seconds**, and all-Ultra with relaxed placement
**8.6 seconds**. Native report totals also include export/validation and a deliberate second generation
for repeatability; they must not be presented as ordinary preview generation times. Timings are a
sample of the tested seeds/settings, not a frame-rate or long-session benchmark.

The all-Ultra, eight-player case with Original Surface Relations and overlap prevention both enabled
placed **89/792** colonies on valid sites. With both disabled it reached **792/792**. The shortfall is
reported by the existing capacity warning; the larger size does not waive physical clearance or repaint
terrain. These paired cases change both policies and do not isolate the effect of either one alone.

All generated maps, logs and asset-derived screenshots remain under ignored `artifacts/rmg/regions-512*`
paths. The historic V13 water-correlation failures remain outside this additive size change; the full
historical RMG suite was not rerun. User in-game testing, including longer high-unit-count sessions,
remains the next acceptance step.
