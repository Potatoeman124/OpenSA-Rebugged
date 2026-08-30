# Phase 8A manual playtest corpus

## Purpose

This eleven-map corpus validates that Water Amount and Tactical Terrain produce visible, playable Low/Standard/High outcomes without weakening the accepted Version 7 safety contract. Same-seed triplets isolate each control. Two former bounded-rejection seeds verify the adaptive chokepoint and land-envelope paths, and two player-reported seeds verify that High Water occupies the actual playfield instead of accumulating at the outskirts.

## Installed maps

| Seed | Preset | Players | Layout | Water | Tactical | Purpose |
| ---: | --- | ---: | --- | --- | --- | --- |
| 8301001 | Balanced | 2 | Contested Center | Low | Standard | Water comparison |
| 8301001 | Balanced | 2 | Contested Center | Standard | Standard | Water comparison |
| 8301001 | Balanced | 2 | Contested Center | High | Standard | Water comparison |
| 8301002 | Balanced | 4 | Open Fields | Standard | Low | Tactical comparison |
| 8301002 | Balanced | 4 | Open Fields | Standard | Standard | Tactical comparison |
| 8301002 | Balanced | 4 | Open Fields | Standard | High | Tactical comparison |
| 8200002 | Balanced | 2 | Contested Center | Low | Standard | Safe chokepoint reduction regression |
| 9200041 | Tactical Crossroads | 4 | Open Fields | Standard | High | Localized land-envelope repair regression |
| 8301003 | Open Conflict | 2 | Open Fields | High | High | Combined high-intensity stress case |
| 5722426127237601134 | Balanced | 4 | Contested Center | High | High | Player-reported interior-Water regression |
| 16542743543062672902 | Balanced | 4 | Contested Center | High | High | Player-reported interior-Water regression |

Version 7 map titles include compact `W-<level>` and `T-<level>` labels so same-seed variants remain distinguishable.

## Installation

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Prepare-Phase8aManualCorpus.ps1 `
    -InstallForPlay -ReplaceInstalledRmgCorpus -Overwrite
```

The replacement option validates the exact OpenRA development map directory and removes only `OpenSA-RMG-*.oramap` packages before installing these eleven maps. Official and unrelated custom maps are untouched.

## Review

For the Water triplet, confirm that Low, Standard, and High produce an obvious increasing amount of blocking Water while starts, routes, colonies, shoreline transitions, and useful maneuver space remain intact.

For seeds 5722426127237601134 and 16542743543062672902, confirm that High Water forms multiple substantial features inside the traversed battlefield. The map may safely reduce optional neutral colonies, but it must not satisfy High Water mostly with an outer border or corner pockets.

For the Tactical triplet, confirm that Low, Standard, and High produce progressively more Rock and Vegetation in contest, route, flank, and quiet roles. High must remain structured gameplay terrain rather than uniform noise or border-only decoration. Terrain-specific doodads must still appear broadly and legally.

For all maps, confirm:

- no spawn fire or production-exit instant kills;
- all players receive a starting colony and can leave their start area;
- neutral colonies are reachable and do not attack a player at match start;
- ground units traverse Clear, Rock, and Vegetation with the expected speed changes;
- no isolated land pocket, illegal shoreline, square moss seam, or visible diagonal terrain defect;
- the preview matches the loaded map at strategic scale; and
- the match starts and remains playable.

Seed `8200002` should load with no chokepoint orbit and a safe-adjustment warning in its report. Seed `9200041` should retain approximately the requested High Vegetation coverage with only localized symmetric pruning. Machine-readable settings and reports remain ignored under `artifacts/rmg/phase-8a-parameterized-battlefield/`.