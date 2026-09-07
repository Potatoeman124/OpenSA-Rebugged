# Regions V15: player counts and colony weights

## Scope and version

Development on `codex/rmg-eight-players-colony-weights` starts from frozen V14 main commit
`1143fad394ef845fd21ac986b93c3b322f14b25e`. Player-settings **schema 9** selects **V15 configuration 1**.
The 128 and 256 profiles retain V14 terrain quantities and accepted V13 complexity calibration,
including Ultra broad 2.70 / fine 2.60. Schemas 1-8 retain their historical behavior.

The update is implemented for NORMAL / Natural Landscape (Regions). Other terrain types remain future
work. Structured Competitive and Artificial Battlefield retain their two/four-player contracts.

## Player counts and solo behavior

The lobby slider and JSON contract support every integer **1 through 8** at both 128 x 128 and 256 x 256.
Each slot has one locally valid `mpspawn` and a sequential `MultiN` player definition. The existing native
adapter reloads the package and checks both counts. Start selection keeps the same geographic-reference
method and protections for every possible starting faction, footprint, turret envelope and production exit.

Skirmish servers enable single-player games (`Server.Type != Dedicated`), and the lobby Start button
accepts one occupied slot. The game uses `ColonyConquestVictoryConditions`: neutral colonies still need
capturing in a solo game. Disabling both neutral colonies and enemies can make existing victory
conditions complete immediately. No victory, hostile, or faction behavior is changed here.

Terrain is completed before actor placement. A map may reject a requested player count when it cannot
find enough protected starts. This is especially relevant to eight players on small, heavily obstructed
maps. No terrain is carved and the generator does not silently reduce player count. The existing preview
remains stale when generation fails.

Density targets interpolate/extrapolate the accepted two- and four-player values. For player count `p`,
128 x 128 targets are:

| Density | Target | 1 player | 4 players | 8 players |
|---|---:|---:|---:|---:|
| Sparse | 4 + 2p | 6 | 12 | 20 |
| Standard | 4 + 3p | 7 | 16 | 28 |
| Dense | 16 + 2p | 18 | 24 | 32 |
| Extreme | 24 + 4p | 28 | 40 | 56 |
| Ultra | 40 + 6p | 46 | 64 | 88 |

256 x 256 multiplies these targets by three. Targets do not impose equal allocations per player or
cross-map accessibility. Preset resolution retains its previous two/four-player targets; the lobby presets
still select their existing explicit density values.

## Neutral Colony Types dialog

The RMG panel exposes **Neutral Colony Types...**, opening five rows for ants, beetles, scorpions,
spiders and wasps. Each row has a slider, integer entry, default button and live percentage/fraction.
The dialog has Apply, Cancel and Reset all weights. Apply marks an existing preview stale when the
weights change; Cancel discards the draft. Configuration follows the existing host/ready-state guards.

Weights follow [Hostile Settings](../LOBBY_HOSTILE_CONTROLS.md):

- Independent integer values from **0 through 1000**; no required sum.
- Equal defaults of **100** for each species (20% each).
- Zero excludes a species from both strict placement and relaxed fallback.
- All zero disables neutral colonies; no hidden fallback species and no capacity warning.
- Weights are selection probabilities, not exact final quotas. A finite map can differ from the displayed
  shares, and terrain/spacing capacity can reduce the number placed for a selected species.
- Only neutral colonies are weighted; player starting colonies retain the normal faction setup.

An independent deterministic composition stream draws one type for each requested neutral colony.
Each selected type searches existing terrain for a strict site. Selected types that could not be placed
are carried to the optional relaxed pass without substitution. For that type, the fallback chooses the
smallest available maximum turret overlap, then total squared overlap depth, then pair count and stable
candidate rank. Footprints, exits and all player-start protections remain mandatory.

Changing weights preserves terrain and player starts for the same seed and other settings. Relaxed
spacing preserves the strict placements. Doodads can move around the changed colony reservations.
The new composition policy changes neutral actors compared with V14; replay schema 8 for the frozen
V14 result. Relative weights need not yield identical maps when all raw values are multiplied, although
their selection probabilities remain the same.

The report records raw weights, density target, effective requested count, disabled status, drawn counts
by type, placed counts by type, strict/fallback counts and overlap measurements. With all weights zero,
the density target stays in the placement seed identity but the effective requested count is zero. This
avoids moving player starts when neutral colonies are disabled.

## Defaults and interface

Defaults remain NORMAL, Regions, 256 x 256, four players, Balanced, Medium complexity, Standard
quantities, Original Surface Relations On, Prevent Colony Overlapping On; all five colony weights are
100. Neutral preview markers remain grey squares. The first generated-map selection initializes
Explored Map On and Fog of War Off; later regenerations preserve intentional user changes.

The region wrapper exposes `-Players 1..8`, `-AntsWeight`, `-BeetlesWeight`, `-ScorpionsWeight`,
`-SpidersWeight` and `-WaspsWeight`. For example:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Seed 397716241463670640 -Players 8 `
    -AntsWeight 45 -BeetlesWeight 45 -ScorpionsWeight 0 -SpidersWeight 10 -WaspsWeight 0 `
    -VerifyRepeatability
```

JSON uses a `neutral_colony_weights` object with exactly `ants`, `beetles`, `scorpions`, `spiders`, and
`wasps`. Omitting the entire object gives equal defaults; partial objects, unknown keys, strings, floats,
negative values and values above 1000 are rejected. Explicit weights require schema 9 and Natural
Landscape. Canonical settings and saved reports include weights in fixed species order.

## Verification

Focused C# tests (`--validate-sa-rmg --regions-players`) cover all player counts and densities at both
sizes, settings round trips, input rejection, exact ticket boundaries, two seeds across all counts/sizes,
zero/single-species/mixed pools, repeatability, terrain/start independence and strict-first fallback.

`Verify-RegionsPlayers.py` generates native packages, reloads and lints them, exports actual native
semantics, checks player/neutral preview markers, native footprints and production exits, weight
exclusions, per-type requested/placed counts, repeatability and unchanged strict placements. Its optional
V14 replay compares every frozen map hash and exported semantic bytes with the saved accepted matrix.

The all-Ultra 128 x 128 stress case at seed `397716241463670640`, eight players and Original Surface
Relations On, currently fits only five protected starts. Both spacing choices must reject twice without
writing a package. This is recorded separately from accepted native maps; neutral spacing cannot relax
player-start protection. The same settings are also tested with free surface placement and at 256 x 256.

Automated verification does not substitute for live gameplay or visual acceptance of the new dialog.
The accepted V13 calibration already has three known Small-to-Ultra coarse-water correlation failures
at seed 0 / 256 and maximum unsigned seed / 128 and 256. This update retains that calibration and
threshold rather than changing them as part of the new controls.

## Automated result (2026-09-07)

Evidence is under `artifacts/rmg/regions-v15/` and the adjacent `regions-v15-*.log` files:

- Focused V15 contract tests pass, including 80 size/player/density combinations and generation at
  two seeds across all eight player counts and both sizes.
- Native matrix: **56 accepted packages**, plus **2 expected capacity rejections**, each repeated.
  All packages pass native terrain, physical footprint, production-exit, marker-count and determinism checks.
  Twenty-two deliberately crowded/strict cases have reported neutral-colony shortfalls.
- **44/44 frozen V14 native replays** preserve all recorded hashes and native semantic bytes. The
  equivalent V15 four-player baselines also retain V14 terrain and start positions at both sizes.
- Regions PowerShell wrapper checks pass for a solo empty-colony map and an eight-player mixed pool
  (84/84 neutral colonies), both with native validation and repeatability.
- Full RMG regression: every group passes except the same three pre-existing V13 coarse-water
  correlation checks described above; the V15 group and frozen V14 options group both pass.
- Repository build/runtime-data validation passes. The final playable binaries use Release configuration.

The initial parallel build/export attempt encountered a shared-binary rebuild interruption. Validation
was rerun sequentially and interrupted exports resumed from their existing validated packages. This
was a test orchestration issue, not a changed generator result.
