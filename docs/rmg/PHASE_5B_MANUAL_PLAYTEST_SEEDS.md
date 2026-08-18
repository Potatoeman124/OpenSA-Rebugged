# Phase 5B Manual Shoreline Playtest Corpus

Status: accepted on 2026-08-16; the post-review visual-refinement corpus passed manual validation.

The reviewer inspected all eight maps and confirmed that shoreline and open-Water materialization look correct. No visual-density, movement, production-exit, spawn-fire, or match-play regression was reported. Generator Version 3 is the accepted baseline for subsequent phases.

The development user-map folder contains only the following Generator Version 3 shoreline maps. All previous generated and roundtrip-test packages were removed before this corpus was regenerated and installed.

| Seed | Players | Neutral colonies | Symmetry | Archetype | Primary coverage |
| ---: | ---: | ---: | --- | --- | --- |
| 3100000 | 2 | 8 | horizontal | open | Low colony count, horizontal edge transforms |
| 3100009 | 2 | 10 | vertical | central-contest | Vertical transforms and route constrictions |
| 3100016 | 2 | 14 | rotational | open | Rotated edge/corner transforms at medium-high colony pressure |
| 3100023 | 2 | 20 | rotational | central-contest | Two-player maximum colony pressure and chokepoints |
| 3100024 | 4 | 12 | horizontal | open | Four-player minimum colony count and open routes |
| 3100025 | 4 | 12 | horizontal | central-contest | Four-player horizontal chokepoint orbit |
| 3100026 | 4 | 12 | vertical | open | Four-player vertical transform coverage |
| 3100047 | 4 | 24 | rotational | central-contest | Maximum supported colony pressure, rotation, and chokepoints |

All eight packages passed deterministic repeat generation, save/reload, active NORMAL template verification, MiniYAML lint, native terrain-semantic comparison, starting-colony production-exit checks, actor reachability, and strategic-route validation before installation.

Across the installed corpus, decorated shoreline stamps range from 30 to 52 per map and open-Water details range from 11 to 21 per map. Every report records zero terrain-semantic mismatches and zero production-exit failures.

## Launch

From the repository root, press F5 or Ctrl+F5 in VS Code. In the skirmish map chooser, select maps whose titles contain `shoreline` and one of the seeds above.

## Manual acceptance checklist

For every map:

- inspect every visible Water body at normal gameplay zoom and confirm there are no square hard seams where Water meets land;
- inspect north, east, south, and west edges plus convex and concave corners for wrong orientation, gaps, land wedges, or Water wedges;
- confirm reed and dead-tree shoreline stamps are sparse and irregular rather than forming repeated rows or appearing on roughly every second shoreline cell;
- confirm small islands, scattered rocks, and dead trees sometimes texture open Water, but do not form dense carpets;
- compare symmetry partners and confirm their shoreline roles and orientations are mirrored or rotated correctly; cosmetic detail locations may intentionally differ;
- order ground units along several shorelines and corners; units must remain on Clear terrain and must not cross Water through a visually blocked frame;
- order units through every strategic route and, on `central-contest`, through both members of the chokepoint symmetry orbit;
- train units from each available player colony and verify they leave the production area without obstruction or immediate neutral-colony fire;
- confirm neutral colonies remain reachable by ground units and do not begin firing at player colonies or their production exits at match start;
- verify there are no isolated passable pockets, unreachable starts, trapped produced units, or one-cell Water ribbons; and
- play long enough to confirm the match behaves normally beyond initial placement and that wasp movement remains intentionally independent of ground Water blocking.

The highest-priority visual checks are `3100016`, because it exposed the original density problem, and `3100009`, `3100025`, and `3100047`, because central-contest walls concentrate long shoreline runs. The highest-pressure gameplay checks are `3100023` and `3100047` because they use the maximum supported neutral-colony counts.

Record any failure with seed, player slot/faction, approximate map location, elapsed match time, unit type, and a screenshot. Generated packages and JSON reports remain under the ignored Phase 5B artifact directory and must not be committed.
