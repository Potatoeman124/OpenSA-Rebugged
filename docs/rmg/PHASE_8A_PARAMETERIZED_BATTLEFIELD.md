# Phase 8A parameterized battlefield

## Status

Phase 8A implements the first two deferred player-facing battlefield controls: `Water Amount` and `Tactical Terrain`. Both are active in the separate Random Map Generator lobby panel and normalize through player-settings schema version 2 to the opt-in Generator Version 7 `parameterized-battlefield` profile.

Generator Versions 1 through 6 remain frozen. Schema version 1 remains accepted and continues to normalize to Version 6, so existing settings documents and regression identities do not silently change.

## Scope

The implemented controls are:

- Water Amount: `low`, `standard`, or `high`;
- Tactical Terrain: `low`, `standard`, or `high`;
- coherent defaults for the three established presets;
- deterministic generation, preview, map-cache refresh, and return-to-skirmish behavior; and
- requested-versus-achieved diagnostics and non-fatal capacity warnings.

Chokepoints and Amount of Hostiles remain visible placeholders. Terrain/tileset, map size, and additional layout families remain constrained to supported values.

## Player-settings schema version 2

```json
{
  "schema_version": 2,
  "preset": "balanced",
  "seed": "8100001",
  "players": 2,
  "symmetry": "automatic",
  "layout": "preset",
  "neutral_colony_density": "preset",
  "water_amount": "preset",
  "tactical_terrain": "preset"
}
```

The new fields accept `preset`, `low`, `standard`, or `high`; omission means `preset`. Unknown fields remain errors. A schema-version-1 document is checked against the historical field allow-list and cannot opt into Version 7 accidentally.

Schema version 2 normalizes to Generator Version 7, topology `parameterized-battlefield`, tileset `NORMAL`, native size `128,128`, and explicit Low/Standard/High values for both new controls.

## Preset normalization

| Preset | Layout | Water | Tactical terrain | 2P colonies | 4P colonies |
| --- | --- | --- | --- | ---: | ---: |
| Balanced | Contested Center | Standard | Standard | 10 | 16 |
| Open Conflict | Open Fields | Low | Low | 8 | 12 |
| Tactical Crossroads | Contested Center | Standard | High | 14 | 20 |

Explicit values override the corresponding preset dimension. Presets are normalization conveniences, not separate generator algorithms.

## Water Amount

Water Amount changes the obstacle-density quality target used by the deterministic Water topology search. It never weakens route, connectivity, movement, symmetry, shoreline, combat-space, or production-exit gates.

| Layout | Low target/range | Standard target/range | High target/range |
| --- | --- | --- | --- |
| Open Fields | 8% / 6-10% | 12% / 10-14% | 16% / 14-18% |
| Contested Center | 12% / 10-14% | 16% / 14-18% | 20% / 18-22% |

Version 7 treats the selected range as an acceptance requirement. Water is placed in two deterministic stages: distributed interior anchors reserve meaningful battlefield features before optional neutral colonies consume capacity, then the normal topology pass completes the selected whole-map density around the combat-safe colony layout. Open Fields with High Water uses fewer, larger interior lakes; Contested Center uses smaller distributed features to preserve its central combat structure.

Whole-map density alone is insufficient because a border-heavy result can satisfy the percentage while leaving the playfield dry. Version 7 therefore measures the central 48 x 48 logical-cell battlefield as a 4 x 4 sector grid. A sector participates when it contains at least four Water cells.

| Water level | Minimum interior density | Minimum participating sectors |
| --- | ---: | ---: |
| Low | 3% | 4/16 |
| Standard | 6% | 8/16 |
| High | 10% | 12/16 |

Both the whole-map range and the interior-participation threshold must pass without weakening route, connectivity, movement, symmetry, shoreline, combat-space, or production-exit gates.

## Tactical Terrain

Tactical Terrain controls requested Rock/Vegetation coverage and role-aware tactical anchor orbits. Placement continues to prioritize contest, route, flank, and quiet-space roles from Version 6; it is not uniform random noise and never reduces protected Clear space.

| Level | Rock request | Vegetation request | Tactical anchor orbits |
| --- | ---: | ---: | ---: |
| Low | 10% of non-Water native land | 5% | 2 |
| Standard | 14% | 8% | 3 |
| High | 18% | 11% | 5 |

Protected starts, colonies, production exits, routes, Water separation, transition legality, and morphology capacity remain authoritative. When capacity reduces an effective target, generation may succeed with `TACTICAL_TERRAIN_TARGET_REDUCED`; requested, effective, achieved, capacity, and shortfall values remain visible in the report.
## Determinism and compatibility

The canonical Version 7 identity includes both normalized parameter values. Repeating the same schema, seed, normalized settings, profile, and assets must reproduce the same logical, actor, graph, and package identities. Changing either parameter intentionally selects a different deterministic result.

Version 6 and earlier profiles reject non-Standard parameters. Phase 8A is additive through a new profile rather than a reinterpretation of accepted Version 6 maps.

## Adaptive quality behavior

Version 7 preserves every hard safety envelope while avoiding unnecessary player-visible seed failures:

- infeasible neutral-colony targets reduce in complete symmetric groups, never below the supported sparse count;
- High Water reserves distributed battlefield anchors before optional colonies, and may reduce neutral colonies in complete symmetric groups to a floor of two per player when Water and colony capacity conflict;
- an infeasible central-contest chokepoint orbit is omitted rather than moved inside its frozen start, colony, reservation, or route-overlap clearance, producing `CHOKEPOINT_TARGET_REDUCED`; and
- an unfillable Rock-envelope diagonal first uses legal corner fill, then localized symmetry-paired Vegetation pruning, and only then bounded alternate layouts or effective-target reduction.

These fallbacks never relax connectivity, movement, combat-space, production-exit, protected-Clear, transition, symmetry, package, or lint validation. Requested and achieved values remain distinct.
## Diagnostics

Version 7 map reports use schema version 9 and record:

- requested and normalized player settings;
- Water level, target, acceptable range, achieved whole-map density, interior density/share, covered interior sectors, and their required minima;
- Tactical level, requested/effective Rock and Vegetation targets, achieved coverage, capacities, and shortfalls;
- requested and achieved tactical anchor orbits;
- requested and achieved chokepoint segments;
- requested and achieved neutral-colony counts; and
- every warning and hard failure.

The lobby success status summarizes achieved Water, Rock, Vegetation, and colonies. Adaptive quality changes are described as safely adjusted instead of as hard failures.

## Development usage

Generate through schema version 2:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-PlayerMapGenerator.ps1 `
    -Seed 8100001 -Preset balanced -Players 2 `
    -WaterAmount high -TacticalTerrain low -InstallForPlay -Overwrite
```

Or select Version 7 directly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rmg\Invoke-MapGenerator.ps1 `
    -Seed 8100001 -Players 2 -NeutralColonies 10 -Symmetry rotational `
    -Archetype central-contest -Topology parameterized-battlefield `
    -WaterAmount high -TacticalTerrain low -MovementValidation both `
    -InstallForPlay -Overwrite
```

Low-level Water/Tactical switches cannot be combined with `-PlayerSettingsPath`; the document is the complete source of normalized choices.

## Automated gate

The focused self-tests check all nine Low/Standard/High Water and Tactical combinations for hard acceptance and same-settings determinism, in addition to frozen V1-V6 and inherited-baseline tests. They also freeze both player-reported High-Water seeds as interior-density regressions. The bounded Version 7 campaign covers 90 player/symmetry/layout/colony-count/parameter cases; the accepted reference is 90/90 with zero bounded rejections and zero blocking failures. Failure records include exact reproducer switches.

Manual validation must compare each control independently and representative combined settings. Expected results are a visible progression without spawn fire, isolated starts, broken routes, illegal transitions, or tactically empty slow-terrain placement.