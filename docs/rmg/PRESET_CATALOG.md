# RMG preset proposals

The Preset menu now applies a complete map setup: layout family, biome, quantities, colony type weights, ownership, safety toggles and layout-specific choices. The seed and map size stay as selected. Player count stays where supported; mirrored and geometric families adapt it to their existing player limits. Balanced remains the initial setup.

Selecting a preset again restores its complete recipe. Editing its controls shows `Custom (name)`; restoring the recipe values removes that marker. Descriptions appear beside the controls and on the preset menu items. Presets do not change the separate lobby hostile or game-option settings.

## Proposed catalogue

| Preset | Layout / biome | Intended experience |
| --- | --- | --- |
| Balanced | Natural / Normal | Moderate general-purpose terrain and expansion. |
| Open Conflict | Natural / Normal | Open terrain, light slowing effects, scattered colonies. |
| Wild Frontier | Natural / Swamp | Dense terrain and heavy slowing surfaces; colonies can occupy modifiers. |
| Mirror Match | Natural PVP / Normal | Mirrored starts, terrain and colony opportunities across one axis. |
| Grand Arena | Artificial Battlefield / Desert | Diamond blocks, wide combat lanes and planned colony sites. |
| Tactical Crossroads | Crossroads / Normal | Narrow approaches, contested center, no side connections. |
| Long Way Round | Ring / Candy | Narrow square circuit, lake in the middle, fewer Wasps colonies. |
| Border Wars | Divided Lands / Normal | Defensible territories with one narrow crossing per border. |
| Fortress Realms | Strongholds / Desert | Own the starting stronghold, allow nearby colonies, contest neutral castles. |
| Castle Hunt | Strongholds / Swamp | Begin with one colony and expand into neutral fortified clusters. |
| Twisting Paths | Labyrinth / Swamp | Few colonies, narrow ground routes, heavy terrain and few Wasps colonies. |
| Great Continents | Archipelago / Normal | Few large landmasses with substantial inland room. |
| Island Hopping | Archipelago / Candy | Many irregular islands with neutral flying access on each. |
| Scrambled Empires | Chaos / Patchwork | Randomly distributed starting holdings across broad mixed-biome collisions. |
| Total Mayhem | Chaos / Fractured | Ultra complexity, surface modifiers and colony density; Standard water, small collision scale. |

Fortress Realms enables Own Starting Stronghold and disables Respect Starting Safe Area and Prevent Colony Overlapping. Smaller castles remain neutral. Scrambled Empires sets every player's share to 100 and uses Random ownership, distributing the eligible pool approximately equally; mandatory Wasps nests remain neutral. Total Mayhem sets every player's share to 2 and uses Closest to Spawn, allocating 2% of eligible colonies per player with the usual whole-colony rounding. All other presets reset ownership shares to zero.

The Starting Colony Ownership dialog includes a 0-100 value field and **Set for All** beside Ownership Choice. The button copies the value to every displayed player row, including rows below the scroll area. Changes remain a draft until Apply; Cancel discards them.

Long Way Round uses colony weights 100/100/100/100/20 for Ants/Beetles/Scorpions/Spiders/Wasps. Twisting Paths uses 100/80/50/50/10. All other presets reset weights to 100 for every species. Starting player factions remain lobby choices.

Use 256 x 256 initially to compare the designs. 64 x 64 retains the existing capacity limits: a preset does not force starts or colonies into invalid positions, and some seeds can be rejected. The generator may also place fewer neutral colonies than requested where terrain capacity is insufficient.

## Settings reference

| Preset | Complexity | Water | Surface modifiers | Colony density |
| --- | --- | --- | --- | --- |
| Balanced | Medium | Standard | Standard | Standard |
| Open Conflict | Small | Low | Low | Sparse |
| Wild Frontier | Extreme | High | Extreme | Dense |
| Mirror Match | High | Standard | High | Dense |
| Grand Arena | High | Standard | High | Dense |
| Tactical Crossroads | High | Standard | Standard | Dense |
| Long Way Round | High | High | High | Dense |
| Border Wars | High | High | Standard | Dense |
| Fortress Realms | High | High | Extreme | Dense |
| Castle Hunt | High | Standard | High | Dense |
| Twisting Paths | Extreme | High | Extreme | Sparse |
| Great Continents | High | Low | Standard | Dense |
| Island Hopping | Extreme | Standard | High | Standard |
| Scrambled Empires | High | High | High | Dense |
| Total Mayhem | Ultra | Standard | Ultra | Ultra |

## Implementation

Recipes are centralized in `OpenRA.Mods.OpenSA/Rmg/RmgPresetCatalog.cs`. They expand to ordinary existing generator settings, with no new generator version or change to historical serialized preset identifiers. Historical `tactical-crossroads` settings continue to resolve as before; the newly selected UI recipe explicitly requests the actual Crossroads family. Complete settings remain in generated map reports.

## Verification

The native utility accepts `--validate-sa-rmg-runtime OUTPUT --wide --presets` for the catalogue matrix, or `--preset-ui` for the smaller UI/sample pass. It checks every size/player settings round trip, live preset application in both selection orders, ownership/species dialogs, Custom/reset behavior, player adaptation, and actual saved maps with repeatability, lint and native movement validation. Small-map generation rejections are recorded separately from accepted maps.

The 2026-09-17 run passed 420 settings round trips and 104 of 105 generation cases across all four sizes, including one/eight-player requests and multiple seeds. Mirror Match at 64 x 64, four players, seed 1 was safely rejected for insufficient mirrored starting space. All 15 primary 256 x 256 samples passed generation and live skirmish startup with bots, including ownership, production and mandatory-neutral nest checks. UI captures cover 1280 x 800 and 1024 x 600. The existing layout widget regression and repository runtime-data validation passed. Evidence: `artifacts/rmg/presets/matrix-final/`, `compact/`, `existing-layout-ui/`, and `preset-overview.jpg`.

The 2026-09-18 ownership update passed native bulk-edit checks with 1/3/8 player rows, including input bounds, hidden rows, totals, individual overrides, host guards and Apply/Cancel isolation. All 15 preset samples and live skirmish startups passed again. Total Mayhem retained Standard water and applied shares of 2 to every player, with preview ownership matching runtime. Dialog captures were reviewed at 1024 x 600 and 1280 x 800. Evidence: `artifacts/rmg/ownership-qol/compact-final/` and `presets/`.
