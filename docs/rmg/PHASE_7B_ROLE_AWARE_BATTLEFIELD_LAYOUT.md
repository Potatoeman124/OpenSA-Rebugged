# Phase 7B role-aware battlefield layout

## Status

Generator Version 6 is implemented and has passed the automated Phase 7B gate. It is ready for manual visual and gameplay review. It is opt-in through the `battlefield-layout` topology; Versions 1 through 5 retain their established behavior.

Phase 7B addresses two separate findings from the Phase 7A audit:

- movement-affecting Rock and Vegetation must be placed according to battlefield purpose rather than used mainly as a border treatment; and
- passable cosmetic detail must cover the playable interior broadly enough to avoid large visually empty fields.

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

## Passable cosmetic coverage

Version 6 adds a separate passable-decoration pass after terrain materialization. The audited `plant_flower` actor is used because its active rules classify it as passable. Blocking vegetation and mushroom actors remain deferred until they can participate in topology, weighted routing, and placement validation rather than being treated as harmless decoration.

For the current 128x128 map contract, the pass places 48 actors, equivalent to three decorations per thousand native cells after symmetry-compatible rounding. Placement uses deterministic symmetry orbits, a minimum logical spacing of two cells, and greedy 4x4 sector coverage before filling the remaining budget.

The hard gate requires at least 12 of the 16 sectors to contain a decoration. All eight manual comparison maps currently cover all 16 sectors. Decorations may not overlap Water, blocked cells, protected Clear, or an occupied native actor cell.

Clear-native template details from Version 4 are also less conservatively excluded in Version 6: only protected Clear remains forbidden. This permits audited passable stone details on route, contest, flank, and quiet land without changing their movement semantics.

## Configuration

The opt-in profile is `mods/sa/rmg/normal-battlefield-layout-v6.yaml`. Its new contract values are:

- flank influence radius: 3 logical cells;
- tactical slow-terrain anchor target: 3 symmetry orbits, capacity-aware with a hard minimum of 1;
- passable decoration type: `plant_flower`;
- passable decoration density: 3 per thousand native cells; and
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

The final bounded rejection codes were inherited `CHOKEPOINT_PLACEMENT`, `TOPOLOGY_ATTEMPTS_EXHAUSTED`, and `COLONY_PLACEMENT`. No tactical-anchor rejection remained after the capacity-aware contract was applied.

Generated packages and machine-readable reports remain under ignored `artifacts/rmg/phase-7b-battlefield-layout/` directories.

## Compatibility boundary

Version 6 is additive and opt-in. The V5→V6 inherited-baseline test verifies that the Version 5 graph, blocking topology, reservations, shoreline, Water templates, and gameplay actor placements remain stable when Version 6 is not selected.

Phase 7B is not the final battlefield-layout design. Manual review must still judge whether the amount, shapes, and tactical positioning of slow terrain produce useful route choices rather than merely satisfying coverage metrics. New archetypes, blocking decorators, and the player-facing in-game generator interface remain later work.

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
