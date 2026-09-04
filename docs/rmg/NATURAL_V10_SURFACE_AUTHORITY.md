# Natural V10: authoritative surfaces and dirt-only placement

Scope: experimental Natural Landscape V10, with **Original surface relations enabled**.
Integration approval: the user approved this checkpoint for merge and push on 2026-09-05.
Report identity: `natural-v10.1-authoritative-surface-placement-v4`.

## Placement contract

A generated surface is a constraint, not a building-site preparation request.

- Materialize Water, Dirt, Gravel and Moss first. Final starts and neutral colonies must fit this existing surface.
- Check the entire building coverage, not just its anchor. Starting bases include every supported faction and its runtime `BaseActorOffset`; neutral footprints come from runtime `BuildingInfo.Tiles`.
- A non-dirt footprint is rejected. Search another location; never clear moss/gravel or rebuild its transition border around a colony.
- Prefer starts near their provisional anchors, but do not confine them to obsolete provisional territories.
- Keep native Water clearance, combat separation, production exits and connectivity mandatory.
- Refit graph endpoints and routes to the selected starts. Final routes use existing land (including passable gravel/moss); they cannot clear Water.
- Fit neutral colonies in balanced rounds after the final routes. Keep the existing adaptive lower-count behavior if the requested count cannot fit.
- If no safe minimum placement fits any of the bounded terrain candidates, reject generation explicitly rather than carve terrain or silently disable safety.

The older footprint-reservation prototype is withdrawn. Its square clearings were caused by making the land materializer honor colony sites that had been selected before the land surface existed.

The fix retains V10.1's existing provisional water/gameplay planning and terrain-candidate ranking.
It does **not** claim that the entire upstream water construction is independent of provisional starts or routes.
The new authority boundary is the completed shoreline/land-cover materialization; final placement cannot change it.
Cosmetic Clear-only detail templates and decoration actors are applied afterward without changing surface semantics.

## Enforcement

`RmgDirtPlacementRules` exposes read-only footprint predicates, with no surface-writing/reservation operation.
`FitNaturalV10PlacementsToSurfaces` snapshots all native surface intents, terrain template IDs and logical Water cells.
The equality guard rejects any change to any of these arrays during final placement/routing.

Reports expose:
- `natural_surface_authority_enforced = 1`;
- `placement_changed_native_surface_cells = 0`;
- `placement_changed_terrain_templates = 0`;
- `placement_changed_water_cells = 0`;
- relocated-start and replaced-provisional-colony counts.

The independent native validator reads the exported map and runtime actor coverage again.
It requires zero non-dirt start/neutral footprint cells when this option is enabled.
The offline visual loop now requires both the equality proof and these native-footprint metrics.

Tests include individual forbidden cells across every supported footprint, alternative Clear sites,
out-of-bounds footprints, start Water clearance, mutation injection into each protected terrain
representation, and the start-territory regression below.

Unchecked Original surface relations keeps the previous V10.1 path. V7/V8/V9 are not changed by this placement pass.
The prior bounded warning/status-text UI fix is retained.

## Failure caught during verification

The first fresh family-stratified batch completed 15/15 mechanically. Additional screenshot regressions then
caught seed `975197840522651550`: all twelve River Valley candidates failed dirt-only start assignment.

The diagnostic last candidate had 365 reachable dirt sites, but per-provisional-territory candidate
counts were `0/192/5/35`. One start was forbidden from considering dirt outside its old territory.
The correction removes that artificial boundary while retaining nearest-anchor preference and all safety checks.

All four screenshot seeds subsequently passed, starting with the formerly failing case:
`975197840522651550`, `600565923169188248`, `45587577125439995`, `847751883559543649`.
Evidence: `artifacts/rmg/visual-review/v10.1-surface-authority-repro-03/`.

The first fresh batch is superseded, not counted as final validation.
Failure and diagnostic evidence remains under `v10.1-surface-authority-repro-02/` and
`v10.1-surface-authority-diagnostic-01/`; no failed seed was concealed or deleted.

Unchecked control `656060532586988781` has identical canonical map, logical, actor and graph SHA256 hashes
to `v10.1-placement-off-before`. Current control: `v10.1-surface-authority-off-control-01/`.

## Final verification

Final fresh corpus: `artifacts/rmg/visual-review/v10.1-surface-authority-fresh-02/`.
Final result: **15/15 mechanical and 15/15 assistant visual acceptance**, with three new random seeds per family.
No seed failed or was replaced within this final batch. All generated reports prove zero placement changes to
native surfaces, terrain templates and Water, and zero non-dirt coverage beneath starts/neutral colonies.

The final safe adaptive count was **4-8 neutral colonies versus 16 requested**. This is a real trade-off:
the bounded search must use available dirt and preserve combat/route clearance, rather than punching building sites into geology.
It does not prove that a globally optimal packing could not place more colonies.

Additional verification:
- All four screenshot regressions pass mechanical and assistant visual review in `v10.1-surface-authority-repro-03`.
- Unchecked-option canonical map/logical/actor/graph hashes are unchanged.
- Final engine/OpenSA build passes with zero warnings and errors.
- V1-V10 self-tests, inherited baselines, status-text layout, native validation, footprint checks,
  injected-mutation checks and the formerly failing seed regression all pass.
- `git diff --check` reports no whitespace errors (an existing line-ending notice remains).

See the reviewed worksheets and `visual-review-gate.json` files for per-map observations.
The user approved integration after reviewing the fix. This approval does not turn the offline
checks into live-match evidence; broader playtesting remains a separate activity.

This is offline validation through the same player-settings resolver, generator and package path as the RMG UI,
with both logical/proxy and engine-grounded static native checks. It is not live mouse-click or live-match validation.
The 2x2 native stamp catalogue still creates some local stepping; this fix does not replace the terrain morphology.
