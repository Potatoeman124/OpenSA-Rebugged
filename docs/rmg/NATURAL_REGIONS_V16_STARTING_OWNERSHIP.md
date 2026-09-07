# Regions V16: small maps and starting colony ownership

## Scope and compatibility

Player-settings schema **10** selects **Regions V16, configuration 1**, developed on
`codex/rmg-eight-players-colony-weights` after the accepted V15 implementation `7035e00`.
The lobby and Regions wrapper select V16. Schemas 1-9 keep their historical results, including
V15's 0-1000 species weights. NORMAL / Natural Landscape is the supported terrain/layout.

V16 adds 64 x 64 and Starting Colony Ownership. It lowers both current colony slider groups
to integer values **0-100**. Terrain calibration, colony placement and native clearances are
unchanged at 128 and 256. Changing ownership shares changes the map's setup rules/identity,
but does not move terrain, player starts, neutral colonies or doodads.

## Ownership contract

The **Starting Ownership...** button opens **Starting Colony Ownership** with one row per
configured player (1-4 at 64 x 64; 1-8 at larger sizes). Each row has the same slider, integer entry and default button as
Neutral Colony Types. All ownership defaults are **0**. Apply commits the draft and marks the
preview stale; Cancel discards it; Reset all shares sets every row to zero. Host/ready-state
guards apply. Reducing the player count excludes hidden rows from generation; increasing it
again restores their previous values from the current RMG session. Applying an RMG preset clears all shares.

The pool contains only **successfully placed non-starting colonies**, regardless of species.
A density target that could not be completely placed does not inflate the ownership pool.
Original faction starting colonies are never part of this allocation.

For pool size N and sum S of participating players' shares:

1. If S is zero, assign no extra colonies.
2. If S is below 100, the total allocation budget is `ceil(N * S / 100)`.
3. If S is 100 or above, the budget is N.
4. Split this fixed integer budget proportionally to shares: take each floor, then award remaining
   colonies to the largest fractional remainders. Equal remainders use ascending player-slot order.
   Zero shares always receive zero. Small positive shares may receive zero when the pool is tiny.

The user explicitly accepted rounding the combined budget before apportionment, avoiding the
oversubscription caused by independently rounding every player upward.

| Pool | Shares | Assigned by player | Remains neutral |
|---:|---|---|---:|
| 10 | 0, 10, 50 | 0, 1, 5 | 4 |
| 10 | 40, 40, 80 | 3, 2, 5 | 0 |
| 2 | 33, 33, 33 | 1, 1, 0 | 0 |
| 3 | 0, 10, 50 | 0, 0, 2 | 1 |

Player 1 means the first lobby player slot (`Multi0`), not map marker A. Allocation runs during
match setup after actual starting colonies exist, so explicit, swapped and random spawn choices
are honored. Unoccupied slots receive nothing and their shares are omitted from the sum and
allocation. Thus closing a slot can change the total assigned when the remaining shares sum below 100.

After quotas are fixed, globally sort eligible player/colony pairs by squared straight-line native-cell
distance from the actual starting colony. Assign each still-unclaimed colony to that player if its
quota is not yet filled. Distance ties use slot order, then saved colony order. This is a deterministic
nearest-pair preference, not a promise of globally minimum total travel distance. It does not impose
land connectivity or evaluate flying-unit availability.

Saved colony actors retain the existing `Creeps` owner. A map-local `RmgStartingColonyOwnership`
World trait stores the shares and exact eligible actor IDs. It applies ownership once at the end of
the first setup tick, using normal engine owner-change notifications so production queues, faction
conditions and render state refresh. Later captures are not overridden. No synchronized RNG is consumed.
Lobby preview squares remain grey: assignment depends on the eventual occupied slots and actual spawns.

## Sizes and defaults

The RMG offers **64 x 64 (1-4 players), 128 x 128 and 256 x 256 (1-8 players)**.
Selecting 64 x 64 reduces an existing higher player setting to four, updates the slider range, and
marks the preview stale. JSON, direct generation and the wrapper reject more than four at 64 x 64. The 64 profile uses 32 x 32 logical stamps,
a two-cell border, and the same native colony/start/production clearances. Its neutral density target
is `ceil(existing 128 target / 4)`; actual capacity can reduce placement. Generation never silently reduces the requested player count. Insufficient protected start space rejects generation and keeps the previous
preview stale. In the two sampled Medium seeds, one through five starts fit; six through eight reject.
The user therefore requested a four-player cap for 64 x 64. This is a product limit, not an engine player-count ceiling.

Engine dimensions are not restricted to powers of two. `NewMapLogic` clamps the editor's playable
minimum to 2 x 2; `CPos` packs X and Y into signed 12-bit coordinates (-2048 through 2047).
For this rectangular RMG, the resulting coordinate ceiling is 2048 stored cells per axis, or
2044 playable cells with the existing four border cells. That is a representation ceiling, **not a
supported or performance-tested RMG maximum**. Smaller than 64 and larger than 256 remain outside
the exposed generation contract; larger-map performance work is deferred at the user's request.

Default configuration remains NORMAL, Regions, 256 x 256, four players, Balanced, Medium complexity,
Standard quantities, Original Surface Relations On and Prevent Colony Overlapping On. Species weights
are 100 each; ownership shares are zero. The first generated-map selection sets Explored Map On and
Fog of War Off; later regeneration preserves deliberate changes.

## JSON and command line

Schema 10 accepts `starting_colony_shares` as an array of exactly one integer 0-100 per player.
Omission means all zero. Null, wrong length, negative, >100, floating-point and string entries reject.
Explicit ownership shares require Natural Landscape and schema 10. Species weights retain the five
named keys and relative-probability behavior; schema 10 caps each at 100, while replay schema 9 keeps 1000.

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 397716241463670640 -MapSize 64 -Players 1 `
    -StartingColonyShares 100 -VerifyRepeatability

.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 397716241463670640 -Players 3 `
    -StartingColonyShares 0,10,50 -VerifyRepeatability
```

Reports include requested shares, allocated counts if every configured slot participates, the remaining
neutral count under that assumption, and the runtime-assignment policy. Those forecast counts can
change if slots are unoccupied when the match starts.

## Verification

- `--validate-sa-rmg --regions-ownership`: agreed examples, 6,321 conservation combinations,
  invalid input, 20 valid size/player settings round trips, 64 x 64 over-limit rejection at both settings and generator boundaries, frozen V15 weight range, 64 solo generation,
  nearest/swapped-start assignment and unchanged terrain/actors when shares change.
- `--validate-sa-rmg-runtime OUTPUT-DIRECTORY [--wide]`: actual 1024 x 600 slider widgets (1/3/8 rows,
  0-100 limits, Apply/Cancel/reset, non-host guard and scrolling); isolated live worlds for zero,
  percentage, weighted/swapped, unoccupied-slot, 64 solo, eight random starts and empty-pool cases.
  Each case initializes twice; checks assignment, quotas, production readiness/faction refresh,
  unchanged original starts/terrain and one-time setup behavior after a subsequent owner change.
- `Verify-RegionsOwnership.py OUTPUT_DIRECTORY --replay-v15`: actual native map export, YAML lint,
  native footprint/start/exit checks, repeatability, ownership metadata, unchanged map geometry across
  ownership settings, deterministic safe capacity rejection and exact saved V15 hash replay.

Automated checks support in-game testing; user visual/gameplay acceptance remains separate.

## Automated result (2026-09-07)

Evidence is under `artifacts/rmg/regions-v16/` and adjacent `regions-v16-*.log` files.

- **20 accepted native packages**, **10 deliberate 64 x 64 player-range rejections**, and **1 terrain
  capacity rejection**, each rejection repeated identically (`native-final/verification.json`). The
  capacity rejection is the four-player 64 x 64 all-Ultra, Original Surface Relations On case. All
  accepted packages pass native movement/footprint/exit checks, lint and repeatability. Ownership
  variations preserve terrain, starting positions and colony actors at all three sizes.
- **56/56 exact frozen V15 replays**, with every recorded SHA-256 and native semantic byte preserved
  (`native-matrix/frozen-v15/verification.json`, completed before the final 64-only cap).
- **7 live-world scenarios, each initialized twice**, pass ownership totals, actual-start distance
  assignment, production readiness, faction refresh, unchanged starting colonies/terrain and
  one-time behavior (`runtime-final/verification.json`).
- Actual RMG lobby widgets verify eight-to-four clamping on selecting 64, restoring the larger-map
  range, matching ownership row counts, both launchers, and retained Apply values. Dialogs were
  rendered and inspected at 1024 x 600 (`runtime-03`) and 1280 x 800 (`runtime-final`); the final wide
  run also captures the main RMG panel.
- PowerShell wrapper passes 64 solo with default zero ownership, 64 solo with 100 ownership, and
  three players with 0/10/50; rejects five players at 64 before writing a settings file.
- Full RMG regression passes every group except the **same three documented V13 Small-to-Ultra
  coarse-water correlation failures**: seed 0 / 256, maximum unsigned seed / 128 and / 256. V16's
  ownership/cap group, V15 player group and V14 options group all pass. Accepted V13 calibration
  and its test threshold were retained.
- Repository build/runtime-data validation passes. Final playable binaries were rebuilt in Release
  configuration with zero compiler warnings or errors (`regions-v16-final-build.log`).
