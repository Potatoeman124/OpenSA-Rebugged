# Natural Landscape PVP

## Scope and status (2026-09-08)

Implemented on `codex/rmg-natural-landscape-pvp`, from accepted main `e100d92`.
This is **Regions V17, configuration 1 / player-settings schema 11**, accepted by the user on
2026-09-09 at implementation `15f4882`. It has not yet been merged or pushed.
The accepted default remains ordinary Natural Landscape, V16/schema 10. No merge or push is part
of this implementation step.

The user selected mirroring of terrain, player starts, and neutral-colony positions/types, followed
by the existing starting-ownership allocation. This adds symmetry to the accepted terrain-first
Regions construction. It does not reinstate the historical route-first generator or require ground
connections across the map.

## Layout roadmap

The user selected the following order of work, with a separate review of each layout:

1. Natural Landscape PVP: this implementation.
2. [Artificial Battlefield V18](ARTIFICIAL_BATTLEFIELD_V18.md): implemented next, pending user review.
3. Crossroads.
4. Ring.
5. Divided Lands.
6. Strongholds.
7. Labyrinth.
8. Chaos: explore extreme, unconventional but playable maps beyond the current all-Ultra setup.

Ordinary Natural Landscape is the finished baseline. Open Battlefield needs no separate family:
its character can be obtained through Natural Landscape parameters. Structured Competitive is
removed from the lobby family selector; its historical schema/CLI implementation remains readable
for exact reproduction of old maps. The later new families remain future work.

## Controls

Select **Layout Family -> Natural Landscape PVP**. A **Mirroring Axes** dropdown appears.

| Axes | Reflection | Players at 128/256/512 | Players at 64 |
|---|---|---|---|
| 1 | Two halves | 2, 4, 6, 8 | 2, 4 |
| 2 | Four quarters | 4, 8 | 4 |
| 4 | Eight sectors, including diagonals | 8 | Unavailable |

One-axis orientation is vertical for even seeds and horizontal for odd seeds. Two axes use the
horizontal and vertical center lines; four add both diagonals. Orientation is stable when other
settings change. One axis allows six players because three complete mirrored pairs fit the contract.
Mirroring guarantees counterparts within each group, not equivalence between separate groups.

The player slider snaps to valid group sizes. When only one count is legal, it displays **Fixed by
symmetry** and the count. Selecting 64 while four axes are active changes to two axes/four players.
The four-axis choice is unavailable at 64. Solo and odd player counts remain available in ordinary
Natural Landscape.

All four themes, all four sizes, all five complexity/quantity levels, Surface Modifiers, Original
Surface Relations, colony species weights, overlap prevention, starting-ownership modes and shares,
ownership preview colors, and named map saving remain operational. Choosing a quantity preset keeps
PVP selected. Existing default settings are unchanged.

## Generation contract

- Build the accepted continuous Regions terrain fields for the seed. Fold the fields into the chosen
  reflection domain before coverage selection. Select whole reflection groups, and normalize invalid
  shoreline/land transitions across whole groups. The resulting native movement surfaces are exact
  reflections, including diagonal seams. Existing tile artwork and transition banks are reused.
- Keep the accepted complexity calibration, including Ultra broad/fine strengths **2.70/2.60**. The
  reference terrain, region fields and stream identities stay tied to the seed. Complexity changes
  detail intensity; Ultra can substantially fragment large Low-complexity features. There is no new
  universal minimum percentage of shared water cells.
- Place starts in complete mirrored groups. Prefer sites from the same seed/axes low-complexity
  reference, relocating the whole group when necessary on the finished terrain. Validate the union
  of all five possible starting species' footprints, exits and combat clearances.
- Place neutral colonies in complete groups of 2, 4 or 8. Draw one weighted species per group, then
  mirror its positions and type. Zero-weight species remain excluded. The effective target is rounded
  down to a complete group; capacity may reduce it further. Reports retain requested and placed counts.
- With Prevent Colony Overlapping off, fill missing groups using the existing ordering of smallest
  maximum turret overlap, smallest squared overlap, and fewest overlapping pairs. Physical footprints,
  exits and player-start protections remain mandatory. Never insert an incomplete group to fill a quota.
- Reflect passable decorative actors too. Keep the accepted decision that these doodads do not become
  movement blockers in generated maps. Runtime hostiles retain the existing biome/lobby controls.
- Do not repaint terrain for placement, substitute a different seed, or force a cross-map land route.
  If safe mirrored starts cannot fit, reject the candidate with a useful message.
- Apply ownership quotas and Closest to Spawn / Random after generating the colony pool, using actual
  native starting positions. Deliberately unequal player shares can produce unequal ownership despite
  symmetric terrain and opportunities. Ownership settings do not move or change generated colonies.

Exact reflection sometimes places actors on odd native coordinates. `RmgActorPlan.NativeFrame`
records those offsets; placement, combat-space checks, saved actors, production-exit validation and
ownership use the actual native anchors rather than rounding them to logical 2x2 cells.

## Small-map capacity

The 64 x 64 maximum remains four players. It is a supported configuration, not a promise that every
seed can fit four safe mirrored starts. In the fixed Medium/Standard sample, all four tested two-player
seeds worked. Four-player seeds 0 and 1 worked but had no room for a complete neutral group; seeds 2
and 397716241463670640 were deterministically rejected for insufficient start space. The rejection
preserves the seed and safety requirements. Use a larger map when testing richer colony distributions.

## Reproduction

From the repository root:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 1 -Pvp -MirroringAxes 1 `
    -MapSize 128 -Players 6 -Tileset DESERT -StartingColonyMode random `
    -StartingColonyShares 0,10,20,30,40,50 -VerifyRepeatability

python scripts/rmg/Verify-RegionsPvp.py artifacts/rmg/natural-pvp/native-new
```

The wrapper uses schema 11 and `layout_family: natural-landscape-pvp` only with `-Pvp`.
`mirroring_axes` is 1, 2 or 4; JSON omission defaults to 1 for that family. The field is rejected for
other families and older schemas. Without `-Pvp`, the wrapper retains its accepted V16 output.
The matrix's seven preservation cases require the earlier local biome/512 artifact corpora.
Use `--verify-only` to verify an existing matrix without regenerating it.

The runtime command is `OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --pvp`, using
the normal local engine/runtime environment. It creates isolated worlds, temporarily redirects map
saving into its artifact folder, restores that location, and does not save personal settings.

## Validation evidence

- `artifacts/rmg/natural-pvp/native-01/verification.json`: **31 accepted PVP maps**, **two repeated safe
  64-cell capacity rejections**, and **seven exact V16 package replays**. Coverage includes 1/2/4 axes,
  all five complexity levels, all biomes, all sizes, horizontal/vertical mirroring, species exclusions,
  an empty colony pool, both ownership modes, and strict/relaxed all-Ultra placement. Independent
  checks compare every exported native terrain cell and each actor type's positions to its reflections.
  Accepted packages pass lint, reload, deterministic repeat generation, bounds, footprint and exit checks.
- The runtime harness also compares water/geology priority fields with ordinary Natural Landscape
  for all five complexity levels under all three axis settings. These are exact reflected matches.
  Low-to-Ultra water retention in the matrix is 53.8%, 40.5% and 33.3% for 1/2/4 axes; the same seed's
  unmirrored V16 reference is 50.7%. These are descriptive metrics, not arbitrary acceptance thresholds.
  Visual inspection confirms that the large regions gain the accepted intense Ultra fragmentation.
- `artifacts/rmg/natural-pvp/runtime-final/verification.json` and `runtime-compact/verification.json`:
  four cases each, two live worlds per case, covering all biomes/sizes and 4/6/8 players. Saved copies,
  ownership quotas, recolored/changed-spawn previews, bots, hostiles and actual loopback server startup
  pass. The widgets check 96 size/player/axis combinations (24 valid), preset retention, modern controls,
  ownership row counts, size limits, invalid schema/family combinations and returning to ordinary Natural Landscape.
- UI screenshots at 1280 x 800 show the full panel and new row. At 1024 x 600, the controls and worlds
  run, but the pre-existing fixed 1182-pixel RMG width still crops the right preview; this is not a new
  responsive-layout implementation.
- Focused existing settings/ownership regression and the public PowerShell wrapper pass. Full repository
  validation passes (`artifacts/rmg/rmg-pvp-validation-final.log`), with the existing 237 Debug style
  warnings and no new warnings. The final Release build passes with zero warnings/errors
  (`artifacts/rmg/rmg-pvp-release-final.log`), followed by a successful final runtime check.
- The sampled 512 all-Ultra eight-player cases took approximately 0.50 seconds in strict mode and
  5.30 seconds with relaxed overlap. Strict mode placed 8/792 colonies; relaxed placed 792/792. These
  are local logical-generation measurements, not a long-session performance or balance claim.

Generated maps, screenshots, native exports and logs remain ignored artifacts. No game artwork or
map package is added to version control. The user accepted the in-game result on 2026-09-09; subsequent work is the separate Battlefield rework.
