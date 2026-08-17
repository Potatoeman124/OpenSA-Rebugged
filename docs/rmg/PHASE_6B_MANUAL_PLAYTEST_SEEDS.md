# Phase 6B manual Clear-detail playtest corpus

## Status

The eight-map Generator Version 4 corpus completed manual visual and match-play review on 2026-08-17. Automated and manual functional gates pass, and Phase 6C may proceed. Visual distribution remains WIP: conservative protected-zone exclusions can concentrate stone details near map margins, especially on route-dense map 3100026. This should be judged again together with Phase 6C land cover before the cosmetic distribution contract is finalized.

These are the same seeds and settings accepted for Phase 5B. Version 4 inherits their exact Version 3 topology, actors, routes, Water templates, and shoreline. This makes the corpus a direct visual comparison in which the only permitted difference is sparse Clear-native detail template 61 or 62.

| Seed | Players | Neutral colonies | Symmetry | Archetype | Primary coverage |
| ---: | ---: | ---: | --- | --- | --- |
| 3100000 | 2 | 8 | horizontal | open | Low colony pressure and large open Clear areas |
| 3100009 | 2 | 10 | vertical | central-contest | Vertical balance and route constrictions |
| 3100016 | 2 | 14 | rotational | open | Prior shoreline-density reference and rotational balance |
| 3100023 | 2 | 20 | rotational | central-contest | Maximum two-player colony pressure |
| 3100024 | 4 | 12 | horizontal | open | Four-player minimum colony count |
| 3100025 | 4 | 12 | horizontal | central-contest | Horizontal chokepoint orbit |
| 3100026 | 4 | 12 | vertical | open | Vertical four-player distribution |
| 3100047 | 4 | 24 | rotational | central-contest | Maximum supported colony pressure |

The development user-map folder should contain only these eight Version 4 RMG packages during review.

## Launch

From the repository root, press F5 or Ctrl+F5 in VS Code. In the skirmish map chooser, select maps whose titles contain land-details and one of the seeds above.

## Manual acceptance checklist

For every map:

- inspect several large Clear regions and confirm small stone details are sparse, irregular, and visually subordinate to the terrain;
- confirm details do not form repeated rows, rings, checkerboards, shoreline bands, or visibly mirrored carpets;
- compare both symmetry sides and confirm their overall density is balanced even though individual detail locations intentionally differ;
- inspect every starting colony, production exit, neutral colony clearance, strategic route, junction, and chokepoint; these protected areas should remain visually clean;
- order ground units directly across multiple template 61 and 62 details and confirm there is no collision, rerouting, hesitation, or speed reduction;
- confirm no Rock or Vegetation movement surface has been introduced;
- confirm shoreline and open-Water visuals match the accepted Version 3 quality and have not reverted to dense or systematic decoration;
- train units from each available starting colony and confirm their exits remain clear and no match-start neutral-colony fire occurs;
- move units along all major routes and through both central-contest chokepoints;
- confirm neutral colonies remain reachable and normal match play is unchanged; and
- confirm wasp behavior remains intentionally independent of ground terrain.

The highest-priority visual maps are 3100000 and 3100024, because their open archetypes expose large Clear surfaces, and 3100016, because it is the established visual-density comparison. The highest-priority protected-zone maps are 3100023, 3100025, and 3100047.

Record any failure with seed, player slot/faction, approximate location, elapsed match time, unit type, and a screenshot. Generated packages, reports, and screenshots remain local evidence and must not be committed.
