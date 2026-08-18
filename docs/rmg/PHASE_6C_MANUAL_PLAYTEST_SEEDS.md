# Phase 6C manual weighted-land-cover playtest corpus

## Status

Automated generation, package reload, semantic terrain, native movement, and weighted-cost parity gates pass. Manual visual and match-play review completed on 2026-08-18 and approved Version 5 for merge as a working prototype.

The gameplay layout is not final. Seed 3100026 demonstrates a visually sterile interior, and seven of the eight previews place most movement-affecting Rock and Vegetation outside likely travel and combat regions; seed 3100025 is the positive comparison. Phase 7 must treat those fields as tactical layout elements. Cosmetic doodads instead need broader, partly uniform spatial coverage so large Clear regions do not appear empty.

The eight Generator Version 5 maps below replace the prior generated corpus in the development user-map folder. They reuse the accepted Phase 5B/6B seeds and settings, so Version 5 can be compared directly against the inherited shoreline, actors, routes, colonies, and Clear-detail baseline.

| Seed | Players | Neutral colonies | Symmetry | Archetype | Rock | Vegetation | Primary coverage |
| ---: | ---: | ---: | --- | --- | ---: | ---: | --- |
| 3100000 | 2 | 8 | horizontal | open | 13.983% | 8.030% | Nominal cover targets in large open areas |
| 3100009 | 2 | 10 | vertical | central-contest | 14.024% | 7.993% | Vertical weighted-cost parity and chokepoints |
| 3100016 | 2 | 14 | rotational | open | 13.985% | 8.031% | Prior shoreline reference with full cover targets |
| 3100023 | 2 | 20 | rotational | central-contest | 13.992% | 4.655% | Vegetation capacity under maximum 2P colony pressure |
| 3100024 | 4 | 12 | horizontal | open | 11.544% | 3.623% | Four-player start and route protection |
| 3100025 | 4 | 12 | horizontal | central-contest | 14.027% | 4.944% | Chokepoint and slow-terrain separation |
| 3100026 | 4 | 12 | vertical | open | 12.913% | 2.258% | Route-dense visual-distribution reference |
| 3100047 | 4 | 24 | rotational | central-contest | 11.939% | 2.258% | Maximum supported colony pressure |

Lower percentages on protected, route-dense maps are intentional capacity limits. Starts, colony combat space, production exits, strategic routes, junctions, chokepoints, repairs, and Water separation take precedence over nominal 14-percent Rock and 8-percent Vegetation targets.

## Launch

From the repository root, press F5 or Ctrl+F5 in VS Code. In the skirmish map chooser, select maps whose titles contain `land-cover` and one of the seeds above.

## Manual acceptance checklist

For every map:

- confirm every player receives a starting colony, neutral colonies appear, and no colony opens fire at match start;
- train ground units from every available starting colony and confirm all production exits remain Clear and safe;
- move the same ground-unit type across Clear, Rock, and Vegetation and confirm the expected order: Clear fastest, Rock slower, Vegetation slowest;
- confirm units do not hesitate, reroute unexpectedly, or become trapped at a transition;
- traverse every major route and each central-contest chokepoint, then approach neutral colonies from several directions;
- confirm starting areas, colony clearances, production space, strategic routes, junctions, chokepoint apertures, and repair corridors remain visibly Clear;
- confirm Vegetation fields are nested inside a Rock envelope, with no direct Clear-to-Vegetation seam;
- confirm neither Rock nor Vegetation directly touches Water, and shoreline/open-Water visuals remain at the accepted Phase 5B quality;
- inspect corners, thin necks, and small islands for broken, diagonal-only, or semantically incorrect transition tiles;
- compare symmetry partners and confirm slow-terrain shape and movement cost are exactly balanced;
- confirm wasps remain able to cross all terrain independently of the ground-unit cost contract; and
- play long enough to capture colonies and fight through multiple terrain bands, watching for pathing or combat-space regressions.

## WIP visual review

Phase 6B's conservative Clear-detail exclusions can move templates 61 and 62 toward map margins, especially on seed 3100026. Do not judge those details in isolation: compare their combined distribution with the new Rock and Vegetation fields and with authored-map vegetation.

Please record whether the full surface now feels naturally guided or still visibly avoids colonies and central routes. Uneven cosmetic distribution alone remains WIP rather than a Phase 6C gameplay failure, but rows, rings, repeated edge bands, dense carpets, or large sterile interiors should be captured for the next tuning pass.

Record any failure with seed, player slot/faction, approximate location, elapsed match time, unit type, terrain crossed, and a screenshot. Generated packages, reports, and screenshots remain local evidence and must not be committed.
