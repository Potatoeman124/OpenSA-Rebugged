# Natural Landscape V9 — Step 2 Visual Review Result

## Decision

The blind Step 2 terrain-only corpus passed user visual review on 2026-09-03, with one shared, versioned condition. The result authorizes progression to gameplay-embedding design; it does not authorize production Natural generation, playable map packaging, starts, colonies, route repair, materialization, or decoration.

## Calibration

- All 8 primary authored references received ACCEPT.
- All 8 negative controls received REJECT.
- The V7 and V8 controls remained rejected.
- Calibration therefore exceeds the acceptance contract's 6/8 primary and 6/8 control thresholds.

## Generated candidates

- All 27 reviewed generated candidates received ACCEPT*.
- The reviewer confirmed that the omitted generated morphology_fit entries mean MATCH.
- The asterisk records one shared condition rather than an individual candidate defect.

## Shared condition: optional original surface relations

Natural V9 gains a player-facing checkbox labeled exactly "Original surface relations". It is checked by default and applies only to Natural Landscape.

When checked, all edge and corner contacts use the original NORMAL surface transition chain:

Water -> Dirt -> Gravel -> Moss

The semantic prototype mapping is:

- Dirt = Clear;
- Gravel = Rock;
- Moss = Vegetation.

Consequently:

- Water may touch only Water or Dirt;
- Gravel may touch Dirt, Gravel, or Moss, but never Water;
- Moss may touch Gravel or Moss, but never Dirt or Water.

When unchecked, the accepted Step 2 unrestricted classification remains available. The underlying accepted correlated fields and random streams must remain identical between the two modes; only semantic surface classification may change.

## Preservation boundary

This option must not change the frozen V7 Artificial Battlefield or V8 Structured Competitive settings, identities, generated maps, or validation fixtures. Natural Landscape remains disabled in the production lobby until a later, separately reviewed gameplay-embedding milestone is implemented.
