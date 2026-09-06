# RMG generation-speed pass

Based on main `5b43f63`; implementation branch `codex/rmg-generation-speed`.

## Contract

This pass changes execution cost, not terrain design. Generator/configuration versions,
seed normalization, random streams, candidate ordering, visual scoring, colony targets,
surface authority and logical/native safety checks remain unchanged. Existing visual
problems are deliberately reproducible for the next terrain-fix task.

Normal lobby and default CLI generation now build the logical map once. The explicit
`--verify-repeatability` CLI flag (PowerShell `-VerifyRepeatability`) runs the second
logical generation and compares logical, actor and graph hashes. This is an audit
execution option, not a player setting and not part of map identity. Existing fuzz
package audits still generate independent package pairs and compare canonical hashes
and engine UIDs.

## Measured changes

The full retained 15-map 256x256 corpus was replayed sequentially with no competing
generation tests. All five content hashes and the packaged PNG match each baseline.

| Pipeline time | Previous baseline | Optimized normal generation |
| --- | ---: | ---: |
| Minimum | 45.2 s | 4.49 s |
| Median | 76.0 s | 6.68 s |
| Maximum | 96.7 s | 8.61 s |
| Mean | 71.1 s | 6.62 s |

Ratio of mean times: 10.74x. Previous baseline includes the former mandatory duplicate
generation; optimized normal timings do not. Both include package and native validation.
Process startup and PowerShell wrapper overhead are not included.

A fresh before-change probe confirmed the representative baseline at 72.56 s.
Incremental checkpoints for exactly the same map and all five hashes:

1. Single-generation mode plus instrumentation: 38.19 s.
2. Exact decoration selection acceleration: 19.07 s.
3. Exact water-priority bound: 6.86 s.

Evidence: `artifacts/rmg/speed-pass/before.json`, `single.json`, `decoration.json`,
`bounded.json`, and `comparison-256/performance-comparison.json`.

## Why these changes preserve decisions

### Decoration selection

The old selector repeatedly scanned all previously selected points and sorted eligible
candidate orbits for every actor. Per-surface exclusion masks now encode the exact
Chebyshev spacing predicate, including its original strict distance boundary.

The candidate list retains its seeded shuffle. A stable maximum scan returns the same
first candidate at the highest uncovered-sector score. It stops only when the exact
upper bound is reached, so no later candidate can improve the answer. Blocking-actor
route eligibility is unchanged. Masks are separate by surface, as the old spacing sets
were. A 100-scenario multi-round differential self-test compares this selector against
the old brute-force predicate and stable ordering.

### Water growth

Added cells already carry seed-derived water priorities. The generator now computes an
extension's priority and coordinate tie-break before its expensive whole-region shape
check. An extension that cannot beat the best already valid extension is skipped.

This is exact pruning, not a heuristic substitute for validation. Every possible new
winner still passes the existing shoreline predicate, and the chosen extension still
passes the connectivity check. Pure feasibility checks are the only skipped work.

### Heuristics and reduced analysis

The user's suggestion was evaluated. Most of the first probe's apparent
decoration/validation cost was decoration selection (6.46 s); logical validation itself
was about 0.04 s for that candidate. Dropping that validation would have saved little
while weakening safety.

Exact score bounds deliver the useful part of the suggestion without output drift.
Reducing the twelve terrain candidates, returning the first valid candidate, approximate
shoreline/connectivity tests, or replacing layout analysis with a different seeded
construction would change generated maps. These remain separate design decisions.
No fast/low-quality mode or broad concurrency rewrite is introduced.

## Instrumentation and reusable checks

Reports retain `logical_generation_and_repeat_ms` as the combined total and additionally
report `logical_generation_ms`, `repeatability_ms`, and `repeatability_checked`.
V10 reports preparation time and each attempted full candidate's time, including rejected
attempts; chosen large-candidate stages distinguish decoration, validation and final
visual metrics/hashes. Candidate timings refer to the first generation; audit-repeat
time is separate.

```powershell
.\scripts\rmg\Test-RmgGenerationPerformance.ps1 `
    -BaselineRoot artifacts/rmg/size-256/review-02 `
    -OutputRoot artifacts/rmg/speed-pass/comparison-256

# Use a new directory and enable the extra logical-repeat audit.
.\scripts\rmg\Test-RmgGenerationPerformance.ps1 `
    -BaselineRoot artifacts/rmg/size-256/delivery-128 `
    -OutputRoot artifacts/rmg/speed-pass/audit-128 -VerifyRepeatability

.\scripts\rmg\Invoke-RmgVisualReviewLoop.ps1 -MapSize 256 -Count 15 `
    -AlternatePlayers -PauseForVisualReview `
    -OutputRoot artifacts/rmg/speed-pass/fresh-15
```

The comparison script stops on generation, validation, hash or preview mismatch and
retains completed records plus the failure. It never overwrites existing evidence.

With `-PauseForVisualReview`, every successful map writes `visual-review-request.json`
in its run folder, then waits for `visual-decision.json` containing `visual_status`
(ACCEPT or REJECT) and concrete `visual_notes`. ACCEPT means all four existing review
axes were inspected and accepted. REJECT or a ten-minute unanswered review stops the
batch before another map is generated. Normal generation timing excludes the review
wait. Final worksheets still pass through `Test-RmgVisualReviewGate.ps1`.

## Verification status

- Release build: zero warnings/errors.
- V1-V10, large-map contract, inherited baselines, native validator and new differential
  decoration-selection tests: pass.
- Fifteen existing large maps: all five content hashes and packaged previews match.
- Explicit repeatability audits: both preserved 128x128 seeds and the 256x256 Low/on
  and High/off settings probes match all five baseline hashes and packaged previews.
- Frozen V7/V8: 12/12 cases match the original fixture across two independent processes.
  The first invocation exposed obsolete test expectations, not a map mismatch:
  it omitted the approved Original surface relations metadata and still required
  Natural Landscape to be disabled. The test now checks that exact added default and
  requires validated V10 isolation; no frozen identity or terrain rule was changed.
- Fresh visual validation: 15/15 unrestricted new seeds pass mechanical, actual package
  geometry, surface-authority and individual visual review gates. The loop paused after
  every success for inspection before generating the next map; no seeds were replaced.
  Coverage: six river valleys, three coastal shelves, two inland seas, two lake districts
  and two wetlands, alternating four/two players. Evidence: `fresh-15/` under the speed-pass
  artifact directory. These are offline lobby-equivalent checks, not a live UI-click or
  full-match performance claim.
- Low-level CLI audit mode also passes (in addition to the player-settings audit path).
- Minor existing angular river stretches are recorded in the review notes. This pass
  does not claim to fix the user's newly reported terrain issues.
