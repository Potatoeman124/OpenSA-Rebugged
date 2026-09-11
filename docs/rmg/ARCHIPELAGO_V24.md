# Archipelago V24

Status: implemented on `codex/rmg-archipelago`, based on accepted Labyrinth
checkpoint `20ccaa0`. Pending user in-game review. No merge or push is included.
Player-settings schema 19; generator version 24; configuration 1.

## Gameplay contract

Archipelago is asymmetric. Separate islands range from small objectives to large
landmasses supporting many colonies and potentially several players. Land travel
between islands is not required. Every actual dry connected component in the
finished native map must contain one mandatory **Wasps nest**. A player starting
colony never satisfies this requirement: its faction is resolved later by the game.

The user explicitly approved these details:

- The mandatory nest counts toward the normal colony target.
- One nest per island is a minimum even if the target is smaller or all colony
  weights are zero. A zero Wasps weight excludes only additional, optional Wasps nests.
- Mandatory nests always start neutral, even with 100% Starting Ownership.
  Percentages and weighted shares operate on the remaining eligible colony pool.
- The nests remain ordinary capturable colonies with working production; they are
  protected only from initial ownership assignment. Their preview markers stay gray.

Every start and placed colony must have land access to its island's nest after
actual starting and colony footprints are applied. Native production exits and a
minimum of two dry cells between blocked colony footprints and water are checked.
This establishes local access to flying-unit production, not a promise of equal
starting opportunities or of AI strategic competence across islands.

## Controls

**Island Amount** chooses the number of islands for the current map size:

| Map size | Few | Standard | Many | Extreme | Ultra |
| --- | ---: | ---: | ---: | ---: | ---: |
| 64 | 2 | 2 | 2 | 2 | 2 |
| 128 | 2 | 3 | 4 | 6 | 8 |
| 256 | 3 | 7 | 13 | 22 | 32 |
| 512 | 4 | 12 | 24 | 42 | 64 |

The 64 map is capped at two islands to retain room for up to four safe player
starts and independent Wasps nests. Its amount selector displays the limit and is
disabled; the chosen amount for larger maps is retained. Other sizes support 1-8
players. Starts can share a large island; island count is independent of player count.

**Island Size** offers Small, Standard (default), Large, Extreme and Ultra. It
controls how far each landmass extends into its surrounding sea region: base radial
fractions are 0.43, 0.62, 0.78, 0.89 and 0.97. These are construction fractions, not
promised percentages of map area. Few islands with Large/Extreme/Ultra size provide
broad territories with many colony sites. Small islands retain the minimum space
needed for mandatory nests and any starting colonies.

**Water Amount** adds 0.06 to the island radius fraction at Low, leaves it unchanged
at Standard, and subtracts 0.06, 0.13 and 0.21 at High, Extreme and Ultra. Higher
water therefore erodes the same islands and widens seas. Minimum starting/nest sites
and sea separation take priority over exact water coverage. The lobby reports actual
coverage rather than treating water as an absolute map-area quota.

**Terrain Complexity** adds increasingly deep coastal bays, peninsulas and fine
shore detail, and subdivides slowing surface patches inside islands. Broad island
anchors and starting locations remain fixed across complexity/size/water changes
for the same seed, map size, player count and island amount. Island size and water
settings can strongly change how much of each region is land. Complexity can change
local routes, internal lakes and colony capacity.

Surface Modifiers, Original Surface Relations, Neutral Colony Density, species
weights, both ownership modes, Prevent Colony Overlapping, Respect Starting Safe
Area, all four biomes and named map saving are integrated. Optional colonies use the
finished terrain; density and overlap requests never repaint islands. Crowded maps
can fall below the normal colony target. The mandatory nest floor takes priority
over a lower target, but does not bypass footprints, exits or starting safety.

## Construction

Seeded dispersed anchors partition the map into island regions. A bounded,
deterministic anchor retry handles arrangements that cannot fit safe starting
positions alongside mandatory nests. The retry depends on seed, map size, players
and island amount, never on density, species weights, ownership or complexity.

Island size, water and layered coastline variation select land inside these regions.
Minimum nest/start clearings and local paths to the island center are constructed
before colony placement. Native sea separator bands preserve distinct islands.
Detached logical land fragments are removed before shore materialization; any dry
slivers created by native transitions are removed during bounded terrain construction
before actors exist. Every finished native component must correspond to a planned
island and contain its mandatory nest. Failure rejects generation instead of silently
returning an island without Wasps.

For this layout, the shoreline emitter treats the outside of the map as ocean.
Older layouts retain their existing outside-clear interpretation, seed mapping and
package contents. No source art or generated maps are committed.

Optional colonies reuse existing strict placement and minimum-overlap fallback.
Mandatory nests participate in all footprint, exit and combat spacing checks. They
are tagged in the generation plan and omitted from `RmgStartingColonyOwnership`'s
actor list. Existing runtime assignment and live preview logic therefore leave them
neutral without introducing a new world ownership controller.

The `regions.archipelago_plan` report records requested/actual island count, native
island areas, anchors, mandatory nest locations, removed slivers and shore clearance.
Native validation independently floods finished terrain and checks each island,
nest ownership exclusion, actor access, footprints, exits and movement costs.

## Validation and reproduction

Use `scripts/rmg/Verify-Archipelago.py` for the generation matrix and independent
native raster checks. It crosses island amount/size/complexity, water and surface
levels, all sizes and player counts, multiple seeds, high colony pressure, ownership,
zero/exclusive species weights and biomes; accepted older packages are replayed.

Native runtime validation is available through:

```powershell
# In engine/, with the normal local DOTNET_ROOT / ENGINE_DIR / MOD_SEARCH_PATHS:
.\bin\OpenRA.Utility.exe sa --validate-sa-rmg-runtime OUTPUT --wide --archipelago
```

This exercises live lobby widgets and serialization, saved maps, repeated real world
startup, random spawn resolution, both ownership modes, gray mandatory markers,
neutral nest exclusion with all shares at 100, zero Wasps weights, capture and
production, bot simulation and biome hostiles.

Example generation from the repository root:

```powershell
.\scripts\rmg\Invoke-RegionsMapGenerator.ps1 -Archipelago -Seed 642188072337235576 `
    -MapSize 256 -Players 4 -IslandAmount few -IslandSize large `
    -TerrainComplexity ultra -VerifyRepeatability
```

Final evidence (2026-09-11):

- `artifacts/rmg/archipelago/matrix-02/verification.json`: 291 Archipelago native
  configurations, each repeated and YAML-linted; 57 accepted historical packages
  reproduced exactly. Independent raster floods verified island counts and nests;
  island size and water changes had measurable effects, complexity increased native
  shoreline detail, and terrain stayed identical under colony-pressure changes.
- `artifacts/rmg/archipelago/runtime-01/verification.json`: 15 actual skirmish
  scenarios, each initialized twice with identical results. Includes 64/128/256/512,
  all four biomes, absent slots, all ownership shares at 100, zero/exclusive weights,
  mandatory gray markers, capture and working production.
- `artifacts/rmg/archipelago/ui-final/verification.json`: live widgets, 700 island
  settings roundtrips, invalid fields, retained controls, the visible 64 map limit
  and the ownership exception text.
- `artifacts/rmg/archipelago/final-regression/verification.json`: 12 final-build
  native cases, including zero Wasps weight and zero total weights, reproduced the
  matrix packages exactly after tightening the native type-exclusion check.
- `validation-01.log`: repository build/runtime-data validation passed.
  `style-final.log`: the same 236 existing warnings, no new warnings.
  `release-final.log`: final Release build succeeded with zero warnings/errors.
- `wrapper-final/map.oramap`: the documented wrapper example reproduces the
  matrix's `review-ultra` package exactly.

The 256 review seed `642188072337235576`, Few islands / Large size / Standard water,
retains three islands and the same four starts across all five complexity levels.
The native shoreline length grows from 1,440 to 2,952 cell edges (Small to Ultra).
With Few islands / Ultra size, its largest landmass exceeds one quarter of the map.
The screenshots in `matrix-02/comparison-final.jpg`, `sizes-final.jpg` and
`water-final.jpg` use native generated map previews, not illustrative mockups.

Large-island and tiny-island extremes intentionally have very different colony
capacity. High density cannot expand islands to meet a quota. At 64, minimum sites
occupy much of the land, so the five size/water levels have less room to differ.
This remains a first in-game review version of the layout.
