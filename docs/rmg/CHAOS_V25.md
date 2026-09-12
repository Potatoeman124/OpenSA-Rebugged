# Chaos V25

Status: accepted at `934ea2b` on 2026-09-11 as the final planned RMG layout.
The user approved the complete RMG feature chain for merge to main and push to origin.

Chaos is an experimental asymmetric layout built on accepted Archipelago `dc6dcfc`.
It deliberately has no competitive parity contract. Player-settings schema 20 selects
generator 25, configuration 1. Older schemas retain their existing generators.

## Terrain design

Independently positioned and rotated terrain fields overlap on the same map. The
family vocabulary includes island coasts, spiral rings, angular fortress moats,
labyrinth fragments, crossing routes, winding faults, artificial blocks, and natural
fields. A later collision can distort or interrupt an earlier one. These are terrain
construction motifs, not calls to complete older map generators; their symmetry and
fairness scaffolds do not apply to Chaos.

Terrain Complexity adds more collision layers and increases the internal detail of
rings, passages and blocks. Existing broad collision centers, rotations, and player
starts remain tied to the seed. Collision Scale changes feature radii: Small is 0.62x,
Standard 1x, Large 1.55x. Water Amount targets 18/33/46/58/70 percent before native shore
normalization and the minimum space needed for starts and flying access. Surface
Modifiers use the normal five-level profile plus 8 percentage points of Rock and 6
of Vegetation, capped at 43/34 percent of dry land. Native tile transitions and
Original Surface Relations constrain the achieved coverage.

The common controls remain available: 64/128/256/512 maps, 1-8 players (64 capped at4),
all five quantity/complexity levels, species weights, colony spacing, starting safe
areas and both ownership modes. Chaos does not expose controls from its source
layout families; Collision Scale and Biome Mixing are its two additional controls.

## Real mixed biomes

Biome Mixing offers Single, Patchwork (default), and Fractured. Single uses the
selected ordinary tileset. Mixed maps use a composite `CHAOS` tileset importing
existing NORMAL, DESERT, SWAMP and CANDY catalogues at offsets 0/256/512/768.
No original artwork is copied into the repository. Each translated template keeps
its source images, palette, frame order, native passability, height, and preview
colors. Every mixed map contains all four biomes. Biome boundaries follow a separate
seeded region plan aligned to complete 2x2 templates, preserving the four frames of
each shoreline or surface transition. Fractured uses more, smaller patches.

Terrain selection rotates the source biome assignment in mixed maps. Geography and
ownership are independent of the biome plan. Passable doodads follow their local
biome. Mixed-map rules permit hostile plants and fliers from all four themes while
preserving lobby weight overrides. These rules apply only to mixed maps; existing
world traits and older maps retain their previous synchronized random draws.

The mixed map is saved and reloaded as a normal map package and can be saved through
the existing Save Map button. It requires this updated mod's composite catalogue.

## Technical playability

Starts retain physical room for every possible starting colony, their production
exits, and the existing mandatory separation between player starts. Minimum local
land paths connect starts to seeded flying-access refuges. This does not impose
cross-map land connectivity, symmetry, equivalent economies, or equal travel costs.

Every actual dry connected component receives one mandatory neutral Wasps nest,
independent of starting factions. It counts toward the colony target, overrides a
zero Wasps weight or zero total species weights, and is excluded from the starting
ownership pool. It can be captured and produce units normally. Land fragments that
cannot hold such a usable nest are removed during terrain construction. The
mandatory nests' local surface reservations are made before optional colonies.

Optional colonies never reshape the completed terrain to meet a density target.
They retain at least two free native cells beside water, and physical walking gaps
between buildings even when turret-range overlap is allowed. The actual saved map
is validated with resolved actor footprints, all possible starting species, native
terrain and occupied ground connectivity. Every start and colony must have ground
access to its landmass's neutral Wasps nest. Colony count may fall below the requested
quantity where the terrain has insufficient capacity.

## Verification

Implementation validation is recorded under `artifacts/rmg/chaos/` (ignored).
The native matrix covers all sizes, player counts, biomes/mixing modes, collision
scales, complexity and quantity extremes, original surface relations, safe-area and
overlap toggles, zero/exclusive species weights, and starting ownership. It checks
repeatability, native save/reload/lint, mixed template bands, actual land components,
mandatory nests, physical access, parameter response and historical package identity.
Live-world tests separately exercise production, ownership, capture, bots, hostile
spawns, saved maps and actual lobby widgets. The current Release candidate passed
19 skirmish scenarios, each initialized twice, and 252 Chaos settings round trips.
Mixed maps were rendered in live worlds at each source biome and at five views of
512x512 maps. Full repository validation passed; style warnings remain at the
existing 236 with none added. The final native matrix passed all 306 Chaos cases and all 65 exact historical
package replays, including eight accepted Archipelago maps. The wrapper example
matched its matrix package exactly.

## Extreme-setting interactions

Original Surface Relations still requires colony footprints on Clear terrain.
Combining it with Ultra Surface Modifiers can sharply reduce optional colony
capacity; Chaos deliberately keeps the terrain. Turning the relation rule off
allows colonies on passable slowing surfaces and generally permits many more.
The smallest map devotes a substantial fraction of its area to the physical space
needed by four starts and the neutral Wasps nest. For the strongest Chaos experience, use 256x256 or 512x512: these can express much
more of the collision field. Dense/Ultra colony requests can obscure the minimap; the
terrain itself remains identical when only density changes.


Evidence: `matrix-01/verification.json`, `runtime-01/verification.json`,
`validation-01.log`, `style-final.log`, and `release-final.log` under the artifact
folder. Native previews were reviewed across complexity, collision scale, biome
mixing, water, density, sizes and additional seeds. Source-art exports and live
world views confirmed actual mixed textures. The most demanding 512x512 case took
about 7.4 seconds for logical generation during concurrent tests; this is an
observed stress-test time, not a controlled performance benchmark.


## Pirate-hole runtime correction (2026-09-12)

The original hostile runtime checks enabled plants and fliers but disabled pirate
spawning. A user subsequently hit `anthole_chaos` when a recurring pirate wave
created an ant hole. The body renderer had appended the composite tileset ID to
the sprite name, although ant-hole sequences exist only for the four source biomes.

Ant-hole rendering now selects the original biome variant from the source template
band under the actor. The same resolver supports actor previews, with Normal as the
location-free Chaos preview. Existing generated maps work with the updated mod;
terrain, hostile weights and synchronized random draws are unchanged.

The new `--chaos-hostiles` native runtime check reproduces the old crash and then
exercises Patchwork, Fractured and all four ordinary tilesets through repeated
opening, pirate spawning and closing cycles. It verifies all four local variants
on each mixed map and drains all tracked holes before declaring success. Evidence:
`artifacts/qol/followup/`.
