# RMG combat-space safety contract

## Status

This contract is implemented by Generator Version 2 configuration `normal-water-blocking-v2` version 3. It was added after live testing of configuration version 2 found match-start colony fire and newly produced units dying at their starting colony. On 2026-08-15, all five version 3 regression maps passed manual live validation with no match-start colony fire. Configuration version 3 is therefore the accepted combat-safe baseline for further RMG development; the earlier Phase 4D automated topology verdict remains historical evidence for configuration version 2 only.

## Explicit V14 and V15 neutral-spacing option

Regions V14 adds, and V15 retains, the user-requested **Prevent Colony Overlapping**, default **On**. With it enabled,
all invariants below remain mandatory. With it disabled, only neutral-to-neutral turret separation
(invariant 1) becomes a placement preference: the original strict pass runs first, then a deterministic
fallback adds the least-overlapping legal neutral sites until the target or physical capacity is reached.
Captured neutral colonies may consequently fire on neighboring colonies. This is an intentional opt-in
exception to the previous no-immediate-neutral-fire requirement.

Player-start center and production-path protection remains mandatory for all possible starting factions.
Building footprints and exit cells must never overlap, and terrain is never cleared to create capacity.
Thus invariant 5 and a nonnegative minimum-combat-margin requirement still apply to all protected pairs;
a negative neutral-only margin is allowed solely in the explicit V14 relaxed mode. The report separately
records `neutral_overlapping_pairs` and `maximum_neutral_overlap_native` in that version.
Historical V1-V13 settings cannot activate this exception.

V15 draws neutral types using the configured relative weights before testing sites. Its strict pass and
fallback preserve those type selections; the fallback minimizes overlap among sites for the selected
type. A species with no legal site produces a reported capacity shortfall rather than substitution.

## Runtime-derived inputs

The generator loads colony data from the active OpenSA ruleset. It does not duplicate weapon ranges or starting-faction lists in the RMG profile.

| Colony actor | Maximum turret range in native cells |
| --- | ---: |
| `ants_colony` | 12 |
| `beetles_colony` | 12 |
| `scorpions_colony` | 18 |
| `spiders_colony` | 13 |
| `wasps_colony` | 12 |

The possible player starting colonies are derived from `StartingUnitsInfo`. Production safety uses each colony's runtime `BuildingInfo` center, `ExitInfo.SpawnOffset`, and `ExitInfo.ExitCell`. A one-native-cell safety buffer is added to the loaded turret range.

## Hard invariants

All distance tests use OpenRA world coordinates and Euclidean distance.

1. For every pair of neutral colonies, each colony center must be outside the other colony's maximum turret range plus the safety buffer.
2. For every neutral colony and every player start, the two colony centers must be outside both turret envelopes for every possible starting faction.
3. A neutral turret must also be unable to cover any point on a possible player's initial production path from `SpawnOffset` to `ExitCell`.
4. Every pair of player starts must satisfy the same two-way center and production-path check for every possible pair of starting factions.
5. A violation is a hard `COLONY_COMBAT_SPACE` or `START_COMBAT_SPACE` rejection. An unsafe package must not be published or installed.

Production-path protection is intentionally start-specific. Neutral colonies do not produce at match start, so reserving every hypothetical neutral production path would consume map space for a state that does not exist and would make legal dense layouts impractical. Their colony centers remain protected in both directions, exactly matching the no-immediate-colony-fire requirement.

## Construction behavior

Configuration version 3 uses bounded deterministic backtracking for colony placement. Candidate scoring prefers dispersed layouts while retaining symmetry and role preferences. Only `near-start` colonies are hard-partitioned by their closest player start. The `central-contest` archetype is defined by routes, chokepoints, and terrain; it no longer forces neutral colonies into a central box that conflicts with chokepoint and combat clearances.

The validator reports:

- `maximum_colony_attack_range_native`;
- `colony_combat_safety_buffer_native`;
- `maximum_required_colony_separation_logical`, a conservative reference envelope; and
- `minimum_colony_combat_margin_native`, which must be nonnegative for an accepted map.

## Manual regression identities

The configuration version 2 live failures were seeds `1000001`, `1001134`, `101368`, `1001054`, and `1001106`. Configuration version 3 retains these seeds in the manual corpus with settings that exercise low density, standard density, both terrain archetypes, every symmetry, and one maximum-density open map.

Manual testing of all five regenerated version 3 maps confirmed that no player or neutral colony fired at another colony at spawn. This closes the critical combat-space gate that blocked later terrain and layout work.

The old 4-player, 24-neutral central-contest identities are allowed to reject when no layout satisfies all hard constraints. Safe bounded rejection is preferable to publishing a spawn-kill map.

Visual shoreline transitions, terrain texturing, decoration, and broader layout aesthetics remain outside this corrective contract.
