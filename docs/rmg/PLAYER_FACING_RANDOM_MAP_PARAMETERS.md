# Player-facing random map parameters

## Status

Phase 8C provides the merged in-game Random Map Generator panel and player-settings schema version 3. Structured Competitive Version 8 and frozen Artificial Battlefield Version 7 are functional. Natural Landscape Version 10 is the user-approved integration checkpoint, with the experimental UI label retained; Version 9 remains available as a historical generator baseline. Balanced, Open Conflict, and Tactical Crossroads presets; 2/4 players; Automatic or explicit symmetry; Open Fields or Contested Center battlefield plan; constrained neutral-colony density; Water Amount; Tactical Terrain; and seed are implemented. The [corrected Phase 8C contract](PHASE_8C_STRUCTURED_COMPETITIVE_COHERENT_WATER.md) remains authoritative for the frozen V7/V8 schema-version-3 baseline; schema versions 1 and 2 remain compatibility contracts.

Natural Landscape now has a size-specific 256 x 256 implementation for 2/4 players using schema 4; see [the large-map contract and verification record](NATURAL_LANDSCAPE_256.md). Existing 128 x 128 requests keep schema 3. Hostile controls and game-option presets are independent lobby settings, not RMG parameters. Chokepoint intensity, cosmetic-detail intensity, 64 x 64, and other tilesets remain proposals or disabled placeholders. They must not be presented as functional until their generator contracts and validation gates exist. V10 Natural Landscape remains explicitly experimental after this checkpoint; broader setting coverage and live gameplay validation are still ongoing.

The objective is to let a player choose recognizable gameplay outcomes without exposing coupled generator internals. Connectivity, combat-space safety, production exits, legal asset boundaries, and bounded validation remain mandatory. Symmetry fairness and weighted movement parity belong to Structured Competitive and Artificial Battlefield; Natural Landscape is intentionally asymmetric and is not bounded by competitive fairness.

## Recommended interface model

Use two layers:

1. A compact default panel containing a preset, player count, map layout, terrain impact, colony density, and seed.
2. An optional Advanced panel for symmetry and a small number of independently meaningful overrides.

Most players should be able to choose a preset, press Generate, inspect a preview, and start the match. Advanced controls must remain constrained to valid combinations.

## Default parameters

| Parameter | Recommended control | Initial choices | Player-facing meaning | Readiness |
| --- | --- | --- | --- | --- |
| Generation preset | Dropdown | Balanced, Open Conflict, Tactical Crossroads, Narrow Passages | Sets a coherent combination of layout, Water, slow terrain, colonies, and chokepoints | First three implemented; Narrow Passages deferred |
| Map size | Dropdown | 128 x 128; 256 x 256 for Natural Landscape | Changes playable area, not native unit or building scale | 128 retained; 256 implementation and validation described in the large-map contract |
| Players | Segmented buttons | 2, 4 | Number of supported starting positions | Implemented in schema v1 |
| Layout family | Dropdown | Natural Landscape, Structured Competitive, Artificial Battlefield | Selects the organic terrain-first family, V8 coherent-Water competitive layout, or frozen V7 engineered tactical-pool layout | Natural V10 approved checkpoint, still experimental; Structured and Artificial implemented in schema v3 |
| Battlefield plan | Dropdown | Open Fields, Contested Center, Mixed Fronts, Narrow Passages | Controls battlefield regions, likely travel lanes, flank structure, and contest areas inside the selected family | Open Fields and Contested Center implemented; others deferred |
| Tactical terrain impact | Three-state selector | Low, Standard, High | Controls how often Rock and Vegetation influence likely movement and combat areas | Implemented in schema v2 / Generator V7 |
| Water amount | Three-state selector | Low, Standard, High | Controls blocking-region frequency and area, not transition appearance | Implemented in schema v2 / Generator V7 |
| Neutral colony density | Three-state selector | Sparse, Standard, Dense | Controls the number of capturable neutral colonies within legal values for the selected player count | Implemented in schema v1 |
| Chokepoints | Three-state selector | Few, Standard, Many | Controls the number and strength of intentionally constrained crossings | Partial support exists; depends on layout |
| Cosmetic detail | Three-state selector | Sparse, Standard, Lush | Controls Clear-native stones and other non-gameplay decoration independently of movement terrain | Clear details exist; distribution remains WIP |
| Seed | Text box plus Randomize button | Unsigned numeric seed; new random values use at most 18 digits | Reproduces the same map when all other settings and generator version match; older full-range unsigned 64-bit seeds remain accepted for replay | Implemented in schema v1; display length tightened in V9 checkpoint |

`Tactical terrain impact` must not mean only a raw Rock/Vegetation percentage. It should control a combination of coverage, number of tactical fields, intersection with likely travel areas, crossing depth, and availability of Clear alternatives. Even the Low setting must forbid a border-only result unless a future explicitly named cosmetic preset requests it.

With **Original surface relations** enabled in experimental V10 Natural Landscape, player starts and neutral colonies must fit entirely on existing dirt. Incompatible footprints are relocated, not cleared. Colony counts may decrease safely when available dirt cannot support the target. See [the surface-authority contract](NATURAL_V10_SURFACE_AUTHORITY.md).

## Advanced parameters

| Parameter | Recommended control | Choices | Notes |
| --- | --- | --- | --- |
| Symmetry | Dropdown | Automatic, Horizontal, Vertical, Rotational | Implemented in schema v1; Automatic deterministically chooses a legal mode. |
| Exact neutral colonies | Constrained numeric selector | Legal values derived from player count | At 128 x 128, targets are even values 8-20 for two players and multiples of four 12-24 for four players. At 256 x 256 Natural Landscape, corresponding target ranges are 24-60 and 36-72. Actual colony counts may be reduced safely. |
| Starting distance | Three-state selector | Close, Standard, Far | Add only after the layout generator can preserve combat safety and useful intermediate objectives at every distance. |
| Slow-terrain character | Dropdown | Mixed, Rock-heavy, Vegetation-heavy | Add only after independent coverage ranges and transition capacity are validated. Vegetation must retain a Rock envelope. |
| Map name | Optional text field | Generated default or user value | The seed and preset should remain visible in metadata even when a custom title is used. |

Advanced choices should override the selected preset visibly. The UI should offer `Reset to preset` and clearly mark customized values.

## Parameters to defer

### Map size

The 128 x 128 baseline remains unchanged. The new 256 x 256 Natural Landscape profile is covered by its [separate contract](NATURAL_LANDSCAPE_256.md); it does not extend frozen V7/V8 sizes. 64 x 64 and larger sizes remain deferred. Do not infer support for additional player counts from the larger area.

### Tileset or biome

The current audited transition catalogue is NORMAL-specific. Candy, Desert, and Swamp should not be selectable until each tileset has its own terrain-semantic audit, transition catalogue, movement-cost contract, materializer, and validation corpus. A tileset choice is a later feature, not merely an art swap.

### Teams and faction

Team and faction assignment belong to the normal lobby. They should not be baked into terrain generation unless a later team-layout preset explicitly creates paired allied starts. The generated map should expose player slots and let the lobby own assignments.

## Internal parameters that must not be exposed

Do not present these as player controls:

- generator or configuration version;
- internal topology name;
- proxy/native validation mode;
- template IDs or transition variants;
- raw safety, turret-range, production-exit, or shoreline radii;
- route-width and repair budgets;
- retry limits or random-stream names;
- raw requested/effective capacity counters;
- an option to disable fairness, symmetry validation, combat safety, connectivity, or weighted-cost parity; or
- an option to permit copyrighted assets inside the repository or generated configuration.

These are implementation and validation contracts. Diagnostic builds may report them, but normal players should choose gameplay outcomes rather than engine mechanics.

## Suggested presets

### Balanced

The default. Moderate Water and slow terrain, several meaningful battle routes, safe starts, neutral colonies distributed between local and contested areas, and exact fairness. No large central region should be visually or tactically empty.

### Open Conflict

Broad maneuver space and few hard chokepoints, but still contains multiple slow-terrain fields intersecting central or flank travel. `Open` must not mean `all Clear` or `slow terrain only at the border`.

### Tactical Crossroads

Emphasizes contested hubs, route intersections, neutral-colony approaches, and bounded Rock/Vegetation crossings. Clear bypasses should exist, but using them should involve a meaningful distance or positional tradeoff.

### Narrow Passages

Uses Water and terrain regions to form deliberately constrained lanes and flank routes. It requires a separate audited layout contract based partly on authored references such as Narrow Passage; it should not be simulated by simply increasing obstacle density.

## Dependency and validation behavior

The interface must prevent or normalize invalid combinations:

- player count constrains legal neutral-colony counts and valid start orbits;
- layout constrains applicable chokepoint settings;
- Water and slow-terrain targets are clamped when start, colony, route, or transition safety leaves insufficient capacity;
- the preview and saved metadata must show achieved values when they differ materially from requested settings;
- identical seed, generator version, and normalized settings must reproduce the same canonical map;
- generation failure should produce a clear retry or New Seed action, never silently fall back to a different seed; and
- the host should not be allowed to launch a generated map that failed the normal package, native movement, combat-space, or weighted-fairness gates.

## Proposed in-game workflow

The existing map chooser's `Random Map` button currently chooses a random map from the visible list; it does not generate a map. Keeping that label while adding generation would be ambiguous.

Recommended workflow:

1. Rename the existing action to `Pick Random`.
2. Add a separate `Generate Map...` action.
3. Open a settings dialog using the default and Advanced parameters above.
4. Generate and validate into the normal user-map directory.
5. Show the resulting preview, seed, preset, achieved terrain summary, and any capacity warning.
6. Offer `Use Map`, `New Seed`, `Edit Settings`, and `Cancel`.
7. On `Use Map`, refresh the user-map cache and select the new package automatically.

The first integrated version should remain host-local and use the engine's existing user-map lifecycle. Multiplayer transfer, cancellation, progress reporting, map-cache refresh behavior, and cleanup of generated packages require an integration audit before implementation.

## Recommended delivery order

1. Freeze a layout-aware gameplay contract and metrics.
2. Implement and manually accept layout-aware Water, Rock, Vegetation, colonies, and travel regions.
3. Introduce a serializable player-facing settings model that maps presets to validated generator settings.
4. Add the in-game generation dialog and map-cache workflow.
5. Expand to additional sizes and tilesets only through separate audits.

This order avoids freezing a UI around the current test-layout limitations.
