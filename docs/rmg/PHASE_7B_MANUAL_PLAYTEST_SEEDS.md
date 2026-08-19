# Phase 7B manual playtest corpus

## Purpose

This corpus is the manual gate for Generator Version 6 `battlefield-layout`. It keeps the eight Phase 6 comparison seeds so visual and gameplay changes can be judged directly rather than against unrelated random layouts.

The automated gate is complete. Manual review remains responsible for judging tactical quality, visual rhythm, and actual in-match movement behavior.

## Installed comparison maps

| Seed | Players | Colonies | Symmetry | Archetype | Rock % | Vegetation % | Tactical slow cells | Central-half slow cells | Tactical anchors | Passable decorations | Covered 4x4 sectors | Native validation |
| ---: | ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 3100000 | 2 | 8 | horizontal | open | 13.969 | 8.030 | 3200 | 1962 | 3 | 48 | 16 | pass |
| 3100009 | 2 | 10 | vertical | central-contest | 13.981 | 8.022 | 2976 | 1926 | 3 | 48 | 16 | pass |
| 3100016 | 2 | 14 | rotational | open | 14.026 | 8.003 | 3204 | 1632 | 3 | 48 | 16 | pass |
| 3100023 | 2 | 20 | rotational | central-contest | 13.992 | 8.016 | 2828 | 1424 | 3 | 48 | 16 | pass |
| 3100024 | 4 | 12 | horizontal | open | 13.977 | 8.029 | 3256 | 1832 | 3 | 48 | 16 | pass |
| 3100025 | 4 | 12 | horizontal | central-contest | 13.998 | 8.020 | 3012 | 1340 | 3 | 48 | 16 | pass |
| 3100026 | 4 | 12 | vertical | open | 13.987 | 8.012 | 3196 | 1996 | 3 | 48 | 16 | pass |
| 3100047 | 4 | 24 | rotational | central-contest | 14.028 | 5.701 | 2000 | 904 | 2 | 48 | 16 | pass |

Seed `3100047` is intentionally capacity-stressed. Its lower Vegetation coverage and two tactical anchors are valid capacity-aware outcomes after protected Clear, dense colony placement, and symmetry take precedence.

## Installation

From the repository root, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Prepare-Phase7bManualCorpus.ps1 `
    -InstallForPlay -ReplaceInstalledRmgCorpus -Overwrite
```

This removes only old `OpenSA-RMG-*.oramap` development packages and installs the eight Version 6 maps. Launch OpenSA with F5 or Ctrl+F5, open the skirmish map browser, and select titles containing `battlefield-layout` and one of the seeds above.

## Visual review

Check the following on every map:

- passable flowers and Clear-native details appear across the playable interior rather than only around the map brim;
- no large interior region is visually empty without a clear compositional reason;
- cosmetic coverage reads as irregular natural detail, not a visible grid or repeated line;
- symmetry preserves competitive equivalence without making individual decoration pairs distractingly mechanical;
- Rock and Vegetation form coherent fields and transitions rather than isolated random noise; and
- Water, shoreline, and open-Water details remain visually correct.

Seed `3100026` is the primary regression for the previously empty central battlefield. Inspect the center at both map-preview scale and normal game-camera scale.

## Tactical and movement review

Check that:

- Rock and Vegetation occur on plausible travel, contest, and flank space instead of functioning mainly as a border;
- central routes contain meaningful slow-terrain decisions on every map;
- slow fields create route choices without sealing corridors or forcing a single dominant path;
- the 50-percent Vegetation and 75-percent Rock ground-speed behavior is noticeable where combat and reinforcement movement are likely;
- horizontal, vertical, or rotational counterparts feel competitively equivalent; and
- the dense `3100047` layout remains playable despite its reduced land-cover capacity.

Do not interpret broadly distributed flowers as a request for uniformly random Rock or Vegetation. The former are passable visual detail; the latter change movement and must remain role-aware.

## Functional regression review

For at least one two-player and one four-player map, verify:

- every player receives exactly one starting colony;
- starting production exits remain clear;
- no player or neutral colony fires at another colony at match start;
- newly produced units are not exposed to immediate neutral-colony fire;
- all neutral colonies can be reached and captured by ground units;
- intended routes remain connected for ground units;
- Water remains ground-blocking while wasps retain their expected traversal; and
- a conquest match can progress normally through production, capture, and victory conditions.

## Reporting a failure

Record the seed, player count, symmetry, archetype, camera location, and whether the issue is visual, weighted movement, blocking topology, combat-space safety, or actor placement. Screenshots at both minimap and game-camera scale are especially useful for layout failures.

Machine-readable per-map reports are written to the ignored `artifacts/rmg/phase-7b-battlefield-layout/reports/` directory and can be paired with the seed when reproducing a failure.
