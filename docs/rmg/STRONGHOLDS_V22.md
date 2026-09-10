# Strongholds V22

## Status

Developed on `codex/rmg-strongholds` from accepted Divided Lands revision `91f1594`
(2026-09-10). Strongholds uses generator **22**, configuration **1**, and player-settings
schema **16**. Accepted in game at `af971d7` on 2026-09-10.
The subsequent [starting-area options](STARTING_AREA_OPTIONS.md) use schema 17 when enabled.

Strongholds starts the asymmetric part of the layout roadmap. It does not mirror terrain,
starts, colonies, species or doodads, and does not enforce equal travel costs or equal colony
pools. The previously accepted layouts keep their existing contracts. Ordinary Natural
Landscape V16 remains the default layout.

## Gameplay design

Each player starts inside a distinct fortified area. Seeded positions, orientations and
irregular chamfered outlines create separate strongholds. Water preferentially forms moats
around their boundaries; the land entrances connect to a common interior meeting area.
Gravel and vegetation favor the fort edges and exterior approaches. Short interior circulation
paths and starting footprints stay clear. The entrance network stays passable, but its exposed
sections may be slow terrain: this is part of the defensive design.

There is no new building or terrain type. Castles are smaller terrain fortifications containing
colonies drawn from the existing neutral pool. They are not an additional actor or free player
base. Starting colony factions continue to be chosen by the existing lobby/runtime rules.

The placement roles were checked against the repository's actual rules:

| Species | Colony turret range, native cells | Relevant production characteristic | Preferred location |
|---|---:|---|---|
| Ants | 12 | Unit build durations 125 / 200 / 350 ticks | Front production line near the entrance |
| Beetles | 12 | Basic units take 200 ticks; later units take 450 / 700 | Front production line near the entrance |
| Scorpions | 18 | Ranged units reach 15 and 12 cells; build durations 250 / 500 / 750 | Protected firing positions behind the front line and on its flanks |
| Spiders | 13 | Advanced units reach 14 and 18 cells; build durations 200 / 450 / 550 | Protected firing positions behind the front line and on its flanks |
| Wasps | 12 | Units use the flying locomotor; build durations 250 / 350 / 550 | Rear areas, from which flying units can cross the moat |

These are placement preferences, not a new balance system. In particular, turret damage and
reload behavior do not support a universal ranking of colony value. Supporting gun-line targets sit behind the forward production line by their actual loaded
turret range plus a small buffer, rather than a fixed percentage of fort size. This respects
neutral combat clearance before ownership is assigned and keeps the
intended support distance relevant on 512 maps as well as smaller ones.

Source rules: `mods/sa/weapons/*-buildings.yaml`, `mods/sa/weapons/*-units.yaml`,
`mods/sa/rules/*-units.yaml`, and the rule-derived `RmgColonyCombatRules` profiles.
Ground Clear/Rock/Vegetation speeds remain 100/75/50 percent, while Water blocks ground units.
Generated grass and mushroom doodads remain passable.

## Controls and ownership

Supported sizes are **64, 128, 256 and 512**, with **1-8 players**, capped at four on 64.
All four biomes are available. Terrain and objective geometry are identical across biome
translations for otherwise identical settings.

| Control | Strongholds behavior |
|---|---|
| Generate Castles | New checkbox, enabled by default. Adds smaller fortifications in available space between player strongholds. The placement target is one on 64/128, four on 256, seven on 512; space can reduce this. Turning it off retains the player starts. |
| Terrain Complexity | Changes the moat edge detail, bastions and surface pattern frequency around the fixed fort locations. All five levels are available. |
| Water Amount | Expands water outward from the moat priorities while preserving the fixed fort interiors and entrance network. |
| Surface Modifiers | Controls the normal gravel/moss targets, preferentially along defensive belts and approaches. |
| Neutral Colony Density | Fills available fortified sites. It never clears moats or narrows protected entrances to meet a target. |
| Neutral Colony Types | Honors all species weights, including excluded types and the all-zero setting. Draws are individual, not mirrored groups. |
| Prevent Colony Overlapping | On enforces the existing turret separation. Off fills shortfalls with the smallest available turret-range overlap, while preserving physical sites and shore passage. Starting combat safety is controlled separately by Respect Starting Safe Area. |
| Original Surface Relations | On keeps original surface transitions and colony sites on dirt. Off allows colonies on slowing terrain and makes all terrain independent of density/types/ownership. |
| Respect Starting Safe Area | On by default. Off allows extra colonies within the starting colony combat buffer while keeping footprints and exits clear. |
| Own Starting Stronghold | Off by default. On grants every colony in the occupied player fort to its actual spawning player; castles and unoccupied forts remain neutral. |
| Starting Ownership | Existing percentage/weight allocation and Closest to Spawn / Random choice apply to the whole generated colony pool, including castles, when Own Starting Stronghold is Off. |

Automatic whole-fort ownership is opt-in. With it Off, the existing Starting Ownership controls apply. Neutral preview icons remain grey and
assigned colonies use the actual lobby player colors. Save Map retains these rules.

At very small sizes or high player counts, starting combat clearances can leave little or no
space for additional colonies, especially with castles disabled. Disabling Respect Starting Safe Area
releases the combat buffer; physical capacity still limits placement. The status reports the
actual/target count instead of silently expanding the fort terrain. Castles can also be empty
when weights are zero or safe sites cannot be found. Their terrain still follows the checkbox.

## Generation and continuity

1. Seed-fixed starts and fort outlines are selected independently of density and terrain amounts.
2. Optional castles use a separate deterministic choice sequence. They cannot relocate starts.
3. Fort interiors, entrance passages and potential water cells define candidate sites. Physical
   colony footprints must leave at least **two native movement cells** before potential water.
4. The requested weighted species sequence is drawn once. Per-species candidate queues prefer
   its defensive role and spread placement among forts, without imposing symmetry or parity.
5. Strict placement runs before optional minimal-overlap fallback. Type-specific cursors avoid
   rescanning rejected candidates; fallback uses lazy priority queues.
6. Water and surface priorities are materialized through the existing native tile catalogue.
   Doodads and the common ownership/package completion run afterward.

Changing only complexity, water amount or surface amounts preserves starting and colony
positions/types. Increasing colony density never changes water. With Original Surface Relations
off it changes no terrain at all. With it on, additional colony sites can reserve dirt, as in
the accepted planned layouts. Castles alter local geography and the distribution of the neutral
pool, so toggling them is not a promise to preserve every colony location.

## Native validation and evidence

The native validator reloads each map, checks actual terrain semantics, all possible starting
footprints, colony footprints, production exits, overlap and ownership serialization. Strongholds
adds connected start/colony access after blocking actors, usable reserved entrance paths, and
minimum two-cell shore clearance. It intentionally does not run the mirrored fairness validator.

`scripts/rmg/Verify-Strongholds.py --wide` exercises size/player/castle combinations, all five
levels of the terrain controls, density/overlap interactions, odd player counts, multiple seeds,
biomes, individual species weights and ownership modes. It independently inspects exported
native terrain, non-symmetry, terrain-only connectivity, road centerlines, visible control
response, objective continuity, density independence and type placement order. It also regenerates
representative packages from all accepted layouts and requires exact member equality.

`--validate-sa-rmg-runtime OUTPUT --wide --strongholds` exercises actual lobby widgets,
serialized settings, ownership previews, named saves, repeated live-world startup, AI and
hostiles. Its settings checks include valid/invalid schemas, 1-8 player sliders, the 64 cap,
castle persistence through presets/family changes, and the unchanged Natural default.

The final 2026-09-10 campaign passed **239 Strongholds maps**, **39 exact historical replays**,
and **13 live scenarios**, each with identical results across two world initializations. The
size/player/castle settings contract checked 64 combinations (56 supported, eight rejected
by the four-player cap at size 64). The largest measured generation time in the parallel campaign
was 9.1 seconds, excluding the second repeatability generation and package checks.

Evidence: `artifacts/rmg/strongholds/matrix-final/verification.json`,
`artifacts/rmg/strongholds/runtime-final/verification.json`, native preview galleries in
`matrix-final/`, and the repository validation/build logs in `artifacts/rmg/strongholds/`.

## Reproduction

```powershell
powershell -ExecutionPolicy Bypass -File scripts/rmg/Invoke-RegionsMapGenerator.ps1 `
  -Strongholds -Seed 825300756769842102 -MapSize 256 -Players 4 `
  -TerrainComplexity medium -GenerateCastles $true -VerifyRepeatability `
  -OutputDirectory artifacts/rmg/strongholds/manual
```

The public wrapper accepts `-GenerateCastles $false` only with `-Strongholds`. The JSON setting
is `"generate_castles": true` under `"layout_family": "strongholds"`, schema 16.
