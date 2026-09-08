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
The lobby preview uses each current player's color for colonies assigned to that slot; unowned colonies
remain grey. It resolves random starting positions with the engine's server-player setup and an isolated
copy of the lobby random seed, then uses the same ownership allocator as runtime. Starting markers show
the corresponding player colors when ownership is enabled. Random factions remain hidden in tooltips.
The forecast refreshes for player colors, slots, factions, spawns, disabled starts and relevant lobby
options. It does not mutate the lobby's random choices or consume the game's random stream. Incomplete
lobby updates fall back to uncolored ownership until a consistent assignment can be computed.

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

## Preview and skirmish-start fixes (2026-09-08)

The user reported repeated crashes when starting a four-player skirmish with three Easy AI opponents,
random factions and starts, seed `748797295927410807`, 256 x 256, Medium complexity, Standard water and
surface modifiers, Ultra colony density, overlap prevention/Original Surface Relations enabled, and
ownership shares 0/10/20/30. This produces 126 of 192 requested colonies. The ownership budget is 76,
apportioned 0/13/25/38; 50 colonies remain neutral.

Windows .NET Runtime events show `InvalidOperationException: Sequence contains no elements` in
`OrderBuffer.Start`, called by `Server.StartGame`. The source is repeated selection of the same generated
map UID: common server map selection resets clients to Invalid, while common client map handling skips
acknowledgement when the UID has not changed. Starting then drops the invalid human host and passes an
empty connection list to the order buffer. The original live-world tests did not cover this server path.

The fix has three parts:

- The RMG does not send another map-selection command when the generated UID is already selected.
- A mod server trait, before common LobbyCommands, treats repeated selection of the current RMG UID as
  a no-op and rejects an invalid host's Start request while the map is being confirmed.
- The RMG lobby disables Start while the local client is invalid and acknowledges a locally available
  generated map once per pending invalid-state episode, allowing interrupted confirmations to recover.

The ownership preview is a live overlay, using existing generated-map rules; saved map identities and
terrain/colony allocation algorithms are unchanged. Maps generated before this fix can use the overlay.
The common engine checkout is unchanged.

The expanded `--validate-sa-rmg-runtime` command connects through an actual loopback server handshake,
adds three Easy AI opponents, reproduces the old invalidation through common LobbyCommands, verifies
that invalid-host Start is rejected without disconnecting, and starts successfully after repeated map
selection. It also checks the actual Start button and one-shot client acknowledgement.

Nine world scenarios now initialize twice, including the reported seed and another seed with AI/random
factions/starts. Preview ownership is compared with every colony's live owner. Both AI scenarios continue
for 600 additional simulation ticks per initialization. Real rendered widgets verify ownership colors,
color changes and spawn changes, while preserving the original lobby state. Evidence is in
`artifacts/rmg/regions-v16-fixes/`; crash event text is `artifacts/rmg/regions-v16-crash-evidence.txt`.

The final fix verification passes:

- `regions-v16-fixes/runtime-verified/verification.json`: all 9 scenarios pass both initializations
  (18 worlds); every colony's forecast matches its live owner. The adjacent
  `regions-v16-fixes-runtime-verified.log` records the successful real-server startup regression.
- `regions-v16-fixes-ownership.log`: the focused V16 ownership/settings contract passes.
- `regions-v16-fixes-validate.log`: repository build and runtime-data validation passes.
- `regions-v16-fixes-final-build.log`: final playable Release build, zero compiler warnings/errors.

This fix does not rerun the full historical generator matrix; its three pre-existing V13 correlation
failures documented above remain outside this change. The new runtime evidence covers the previously
missing server-start path; a complete interactive match still awaits user testing.
