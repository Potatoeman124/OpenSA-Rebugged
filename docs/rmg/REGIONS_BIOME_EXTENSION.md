# Regions biome extension

## Scope and audit (2026-09-08)

Work branch: `codex/rmg-desert-swamp-candy`, from accepted main `bede09e`.
The user requested Desert, Swamp and Candy using the same Regions generation and existing hostile defaults.

The user accepted the completed extension and authorized merge to main and push to origin on
2026-09-08. Implementation commit: `bb699f3`. The automated evidence below records the accepted behavior.

The four source tilesets share template IDs 0-100, 2x2 dimensions, frame order 0-3, native terrain types
and heights. The categories are Clear / Water / Rock / Vegetation, with shared ground speeds 100 / blocked /
75 / 50 and wasp speeds 100 on all four. The NORMAL water/land transition roles can therefore be used as
an identity translation, provided the active target catalogue is checked against that contract.

Differences found in `mods/sa/tilesets/*.yaml`:

- Desert marks water template 26 fixed and 27 PickAny; NORMAL marks 26 PickAny and 27 fixed.
- Swamp/Candy mark 27 PickAny and additionally contain template 112. The RMG uses only IDs 0-100.
- Images and minimap colors differ. Generated maps explicitly write template IDs and all four frame
  indices; editor/blank-map PickAny selection is not the generator's materialization path.
- Decorative plants differ. RMG-specific passable variants are needed for blocking stock decorations,
  preserving the accepted decision to ignore doodad blocking during generation.
- Existing hostile defaults select popcorn/dragonfly, thorn/fly, puff/moth and freckle/flying_machine.
  Existing map-default spawn intervals and plant caps also vary by tileset. Custom lobby overrides
  remain explicit. Theme selection must use these existing rules, not overwrite hostile settings.
- Existing ambient music also follows the tileset (Frogs / Wind / Crickets / Dreamscape).

Source audit evidence: `artifacts/rmg/regions-biomes/tileset-source-audit.json`.

## Implementation plan

1. Add an optional schema-10 tileset choice for Regions. NORMAL remains the default and omits the new
   identity field so accepted maps replay exactly. Historical layouts/schemas stay NORMAL-only.
2. Derive each target profile from the accepted size-specific V16 profile, changing only its tileset,
   identity and corresponding decorative actors. Reuse the audited shared transition roles and validate
   target tile semantics against the loaded engine terrain before export.
3. Enable the four terrain entries in the lobby and expose the same choice in the Regions wrapper.
   Preserve same-seed terrain, colony/start geometry and all accepted options, including ownership modes.
4. Verify saved map reload/lint, ground costs, exits/footprints, explicit tile frames and repeatability
   across sizes, option extremes and all themes. Check exact NORMAL replay and biome-invariant geometry.
5. Render target textures and preview colors; start live worlds with ownership and biome-specific hostile
   defaults. Verify Apply/selection, map switching and explicit hostile overrides. Build playable Release.

The older NORMAL-only deferral is superseded for this audited V16 extension; the generator does not add
connectivity, fairness or terrain repair requirements.

## Implemented behavior

The lobby Terrain selector enables Normal, Desert, Swamp and Candy for Natural Landscape (Regions).
Normal remains the initial selection. All current presets, sizes, player counts, five-level terrain
and quantity controls, colony species weights, spacing and surface-relation toggles, and both ownership
modes operate in each biome. Historical layout families still select Normal and retain their old
limits. Switching to one resets the terrain selection to Normal.

The optional schema-10 `tileset` field accepts `NORMAL`, `DESERT`, `SWAMP` or `CANDY` (case-insensitive
input, canonical uppercase). It requires `layout_family: natural-landscape`. Omitted or explicit NORMAL
normalizes to the old settings representation. Non-Normal themes have distinct profile/map identities;
Normal serialization, map contents and identity remain unchanged. This is an additive V16 / configuration-1
extension, not a new terrain calibration. The Regions PowerShell wrapper exposes `-Tileset` with these
four values; legacy direct CLI generation remains Normal-only.

`RmgBiome` validates IDs 0-100 in the loaded target tileset against the shared 2x2 frame, terrain,
height, palette and audited PickAny contract before export. The accepted Normal water and land role
catalogues are reused. Saved native cells retain exactly the same template IDs and frame indices across
themes. `RmgProfile.Load` derives a fresh theme profile from the matching size-specific Normal profile.
It changes only the tileset, profile identity and decorative actor names. Source tilesets are unchanged.

The decorative pools retain two soil choices, one rock choice and one vegetation choice. This preserves
the number of random draws and every decorative position. New RMG-specific passable variants cover
Desert Grass, Gumnut, Serata, Clover, Moon Mushroom, Moon Flower, Jelly Bean and Jaffa. Stock decorations
retain their existing footprints. This follows the accepted RMG rule that small doodads do not introduce
additional blocking during generation.

| Terrain | Default plant actor | Default flier actor |
|---|---|---|
| Normal | `popcorn` | `dragonfly` |
| Desert | `thorn` | `fly` |
| Swamp | `puff` | `moth` |
| Candy | `freckle` | `flying_machine` |

Hostile species, spawn intervals, plant caps and ambient music continue to resolve through the existing
rules for the selected map tileset. Explicit custom hostile settings retain their values across themes.
The extension does not replace those lobby choices. Map previews use each tileset's colors; owned colony
squares follow player colors and unowned squares remain grey. Non-Normal map titles include the theme.
The status line uses the generic term surface modifiers instead of gravel/moss for these themes.

## Verification (2026-09-08)

- Source audit: all 101 shared templates match semantic and height roles; the documented PickAny and
  extra-template differences are the only catalogue differences. Evidence:
  `artifacts/rmg/regions-biomes/tileset-source-audit.json`.
- Focused settings/ownership self-tests pass, including all biome parsing, invalid input rejection,
  live-catalogue checks, theme-specific decorations and identical seeded terrain/start/colony geometry.
  Log: `artifacts/rmg/regions-biomes-contract.log`.
- Native matrix: **20 accepted maps**, four themes times five option levels, spanning 64/128/256,
  4/8 players, both surface-relation and spacing policies, Random ownership and custom colony species.
  Each passed repeat generation, package lint, engine reload and native placement/movement validation.
  Native `map.bin`, semantic cells, starts, colonies and decorative positions match across themes.
  Each biome's demanding 64 x 64 all-Ultra case was safely rejected twice, as in Normal: **four repeated
  capacity rejections**, not generation regressions. Evidence:
  `artifacts/rmg/regions-biomes/native-01/verification.json`.
- Live runtime: **29 cases / 58 world initializations** pass. These cover all 16 accepted ownership
  cases plus biome-specific Closest, Random, solo, eight-player and real hostile-spawn cases. Repeated
  worlds agree. Live ownership matches colored previews; random/changed spawns and recoloring work.
  Repeated generated-map selection and actual loopback Server.StartGame with AI succeed in each new
  theme. The real RMG Terrain dropdown exposes all four choices. Evidence:
  `artifacts/rmg/regions-biomes/runtime-01/verification.json` and its screenshots.
- Exact compatibility: all members of **16 Normal map packages** match the accepted
  `regions-v16-random/runtime-01` packages byte-for-byte. Evidence:
  `artifacts/rmg/regions-biomes/normal-compatibility.json`.
- Existing hostile validation passes **10,219 assertions**, including theme defaults and custom
  cross-theme weights. Log: `artifacts/rmg/regions-biomes-hostiles.log`.
- Target texture exports and live UI/ownership previews were inspected for all three added biomes.
  Texture export now loads the source map's tileset instead of assuming Normal.
- Repository `build-pipeline.cmd validate` completed successfully. The final focused Debug compiler
  check has zero errors and 238 existing style warnings after cleaning the introduced warnings.
  Logs: `regions-biomes-final-validation.log` and `regions-biomes-final-style.log` under `artifacts/rmg/`.
  The final playable Release build has zero warnings/errors (`regions-biomes-final-release.log`).

The source matrix does not replace user in-game aesthetic/gameplay acceptance. The historic full RMG
suite's three documented V13 water-correlation failures are outside this biome change; terrain calibration
was not changed and that historical suite was not rerun. Generated asset-derived images and evidence stay
in ignored local artifacts; no original art is added to version control.
