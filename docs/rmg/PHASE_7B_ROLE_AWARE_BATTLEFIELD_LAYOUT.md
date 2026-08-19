# Phase 7B role-aware battlefield layout

## Status

Generator Version 6 is implemented and has passed the automated Phase 7B gate. It is ready for manual visual and gameplay review. It is opt-in through the `battlefield-layout` topology; Versions 1 through 5 retain their established behavior.

Phase 7B addresses two separate findings from the Phase 7A audit:

- movement-affecting Rock and Vegetation must be placed according to battlefield purpose rather than used mainly as a border treatment; and
- terrain-specific cosmetic detail must cover the playable interior broadly enough to avoid large visually empty fields.

These are deliberately separate systems. Cosmetic coverage is spatially broad, while slow terrain remains tactically guided. Phase 7B does not uniformly scatter Rock or Vegetation.

## Calibration scope

The Phase 7A evidence behind this implementation included the complete shipped corpus: all 100 campaign maps, 13 custom/challenge scenarios, 11 skirmish references, one unclassified shipped map, and the eight accepted Phase 6 generated comparisons. Campaign and scripted scenario maps are valid spatial and visual references, but their runtime traffic cannot be inferred completely from static map data.

The generator does not copy authored geometry or assets. It uses original generator logic and identifiers already available through the externally supplied game data, preserving the repository's asset boundary.

## Battlefield roles

Version 6 classifies every logical cell before land-cover materialization:

- `blocked`: Water and other ground-blocking topology;
- `protected-clear`: starts, colony footprints, combat-space envelopes, and other mandatory safety reservations;
- `contest`: central-contest and declared chokepoint influence;
- `primary-route`: declared strategic route space outside protected Clear;
- `flank`: passable cells near tactical sources that are not primary routes or contest space; and
- `quiet`: the remaining passable land.

Roles are closed over the configured horizontal, vertical, or rotational symmetry orbit. If members of an orbit initially receive different meanings, the safety-first priority is `blocked`, `protected-clear`, `contest`, `primary-route`, `flank`, then `quiet`. This makes semantic symmetry explicit rather than relying on coincidentally symmetric output.

Protected Clear remains authoritative. Version 6 rejects a map if any protected cell becomes Rock or Vegetation.

## Movement-affecting terrain

Version 6 inherits the Version 5 capacity-aware 14-percent Rock and 8-percent Vegetation targets and the nested Clear-to-Rock-to-Vegetation materialization contract. It changes where eligible terrain is preferred:

1. contest space;
2. primary routes;
3. flank space;
4. quiet space.

Within those roles, central positions receive additional priority. This prevents the inherited edge-depth scoring from turning slow terrain into little more than a map border.

The generator also selects up to three symmetry-aware tactical slow-terrain anchor orbits, subject to map capacity. At least one must be materialized. Dense layouts may legitimately support fewer than three: for example, the 24-colony rotational comparison seed `3100047` supports two without violating protected Clear or symmetry.

Validation requires:

- positive slow-terrain coverage in tactical roles;
- positive slow-terrain coverage in the central half of the map;
- between one and three materialized tactical anchor orbits;
- no slow terrain in protected Clear; and
- exact semantic symmetry for roles and terrain.

Weighted movement validation remains authoritative. Rock and Vegetation continue to use the active ruleset's reduced ground movement speeds, while native and logical path results must agree.

Template `93` is excluded from Version 6. Although its four native frames all report Vegetation semantics, its artwork has a square Rock-colored border that creates disconnected cuts inside moss fields. Version 6 uses seamless Vegetation interior template `78` for the same sparse detail selection; the frozen Version 5 output remains unchanged.

## Terrain-specific cosmetic coverage

Version 6 adds a separate decoration pass after terrain materialization. Its visual mapping is:

- Clear soil: yellow flowers and broad-leaf high grass;
- Rock gravel: brown mushrooms; and
- Vegetation moss: red toad-stool mushrooms.

The stock grass and mushroom actors are genuinely blocking. An attempted implementation with their native footprints reduced required route width from nine cells to seven on a manual-corpus seed, and seed `3100026` places every moss candidate inside tactical route space. Weakening the route gate or omitting the requested terrain type would both violate the accepted contract.

The generator therefore uses RMG-only aliases that inherit the exact stock artwork but override the footprint to passable. The original actors and their official, campaign, and authored custom-map behavior are unchanged. Native package validation reloads these alias definitions and confirms they add no static blocker cells.

For the current 128x128 map contract, the pass places 48 actors, equivalent to three decorations per thousand native cells after symmetry-compatible rounding. Surface quotas are proportional to the materialized Clear, Rock, and Vegetation cell counts. Placement uses exact native-cell symmetry orbits, four-cell native spacing within each terrain family, and greedy 4x4 sector coverage before filling the remaining budget.

The hard gate requires at least 12 of the 16 sectors to contain a decoration, validates the terrain-to-actor mapping and exact native symmetry, and requires every configured variant to be used. All eight manual comparison maps cover all 16 sectors and use all four variants. Decorations may not overlap Water, blocked cells, protected Clear, or an occupied native actor cell.

Clear-native template details from Version 4 are also less conservatively excluded in Version 6: only protected Clear remains forbidden. This permits audited passable stone details on route, contest, flank, and quiet land without changing their movement semantics.

## Configuration

The opt-in profile is `mods/sa/rmg/normal-battlefield-layout-v6.yaml`. Its new contract values are:

- flank influence radius: 3 logical cells;
- tactical slow-terrain anchor target: 3 symmetry orbits, capacity-aware with a hard minimum of 1;
- soil decoration types: `plant_flower` and passable RMG high-grass alias;
- gravel decoration type: passable RMG brown-mushroom alias;
- moss decoration type: passable RMG red-mushroom alias;
- land-decoration density: 3 per thousand native cells; and
- minimum occupied decoration sectors: 12 of a 4x4 grid.

Changing these values is a contract change and must be accompanied by deterministic, native-movement, coverage, and manual visual validation.

## Automated evidence

The final Phase 7B validation produced:

- complete Release and Debug compilation with zero compilation errors;
- MiniYAML, all 125 shipped-map, and 113 Lua-script validation passes;
- focused self-test passes for Generator Versions 1 through 6;
- inherited-baseline passes for Versions 3→4, 4→5, and 5→6;
- a native movement validator pass; and
- a Version 6 smoke campaign with 84 accepted cases from 106 attempts, 22 expected bounded rejections, zero blocking failures, zero accepted hard-invalid maps, zero same-seed hash mismatches, and 48/48 package/native samples accepted.

The eight-map manual package corpus also passed static and native validation with 48 passable decorations, all four visual variants, and all 16 sectors covered per map. Seed `3100016` contains six Vegetation detail selections, uses seamless template `78`, and contains no template `93`.

The bounded campaign rejections remain expected capacity or topology rejections rather than accepted invalid maps. No decoration, seam, movement, or package failure remained in the final campaign.

Generated packages and machine-readable reports remain under ignored `artifacts/rmg/phase-7b-battlefield-layout/` directories.

## Compatibility boundary

Version 6 is additive and opt-in. The V5→V6 inherited-baseline test verifies that the Version 5 graph, blocking topology, reservations, shoreline, Water templates, and pre-existing actor placements remain stable. RMG-only passable aliases do not change the stock decoration actors used by shipped maps.

Phase 7B is not the final battlefield-layout design. Manual review must still judge whether the amount, shapes, tactical positioning, and terrain-specific decoration rhythm are visually and tactically successful. New archetypes and the player-facing in-game generator interface remain later work.

## Usage

Generate and install one Version 6 map:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 3100026 -Players 4 -NeutralColonies 12 -Symmetry vertical `
    -Archetype open -Topology battlefield-layout -MovementValidation both `
    -InstallForPlay -Overwrite
```

Replace only previously generated RMG development maps and install the eight-map manual comparison corpus:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Prepare-Phase7bManualCorpus.ps1 `
    -InstallForPlay -ReplaceInstalledRmgCorpus -Overwrite
```

The replacement option deletes only non-recursively matched `OpenSA-RMG-*.oramap` files from `%APPDATA%\OpenRA\maps\sa\{DEV_VERSION}\`. It does not delete authored or differently named custom maps.
