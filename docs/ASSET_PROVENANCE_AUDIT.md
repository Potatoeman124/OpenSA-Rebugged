# OSArB 1.1 asset provenance audit

Audit date: 2026-09-18. Source snapshot: `d358e0a09eff558391e489fa276d77673c8be02a`.

The release remains blocked. The user explicitly retained the provenance gate. No exception has been granted, and no release package has been created.

## Findings and evidence

The original gate reported **459 findings across 457 paths**: 346 binary assets and 113 source-marker hits. Two attribution documents each matched two markers. Findings are not a count of independently infringing works.

Three sound files now have exact-file approvals supported by their upstream attribution, unchanged upstream release blobs, verified primary-source license statements, packaged attribution notices, and SHA-256 hashes. See [sound evidence](../assets/provenance/2026-09-18-sounds.md). The expected remaining gate count is **456**: 343 binary assets and the unchanged 113 marker hits.

The complete [inventory](../assets/provenance/2026-09-18-inventory.json) records all 457 baseline paths, file hashes, upstream-release matches, known attribution, review category and next action. Its categories describe evidence and outstanding work, not legal conclusions:

| Category | Paths | Meaning |
| --- | ---: | --- |
| Original-game maps | 327 | 109 Gate 5 credited maps, each with metadata, map data and preview |
| Community maps | 32 | Data and preview for 16 maps; author credit found, rights and preview source still need confirmation |
| Documented original derivatives | 29 | Attribution describes original game material or altered existing sprites |
| Claimed contributor originals | 11 | Self-made/recorded attribution found; media license scope still needs confirmation |
| Other third-party sources | 5 | Source identified; complete licensing evidence still outstanding |
| UI and packaging artwork | 27 | Original artwork and derivative chain need review |
| Videos with unverified origin | 10 | Source and permission not yet established |
| Other undocumented media | 11 | Creator/source/permission incomplete |
| Mixed attribution documents | 2 | Preserve credits while underlying assets are resolved |
| Verified third-party sounds | 3 | Approved with packaged attribution |

The inventory is limited to this repository gate's baseline. It is not a complete audit of engine/dependency payloads or every text/script asset. Passing this inventory eventually does not eliminate the need to check final staged package contents and dependency notices.

## Why the upstream contribution statement does not clear media

The supplied upstream statement requires contributions to be the contributor's own code or acknowledged open-source code. That establishes a code contribution expectation. It does not identify or license the original game's artwork, sounds, videos or authored level data. The upstream GPL file and availability of an upstream downloadable release are not treated as blanket rights-holder permission for third-party game content.

The three sound approvals use explicit source licensing. No such permission was found for the original-game derivatives during this audit. This is an evidence gap, not a finding that permission cannot exist.

## Work needed before packaging

1. Obtain written permission or an applicable licensing statement from the original-content rights holder, with enough scope to cover the listed original maps and media. Record the actual grant and its source; author credits alone do not suffice.
2. Confirm media licensing with contributors for independently authored art and community maps. Review map previews separately from their layouts.
3. Resolve the five remaining third-party entries: `flag.png`, `ship.png`, `warning.wav`, `gas_explosion.wav`, and `electricity.wav`. The sound attribution links did not yield verifiable license statements in this audit. Daniel Cook's Hard Vacuum page permits use in general terms; a precise distribution grant and the derivative chain still need recording. Iron Plague requires its own source evidence.
4. Review unattributed media, UI artwork, videos, and the importer tutorial. The new reBugged logo incorporates inherited logo art; adding a new wordmark does not clear those underlying elements.
5. If permission cannot be obtained, plan replacements or local reconstruction/import from the player's own original installation. This changes release scope or the content pipeline and must be designed explicitly; removing campaign content silently is not an acceptable fix.
6. After evidence or replacements are complete, rerun the release gate, inspect staged contents and notices, and build the platform artifacts. The eight original runtime bundles remain excluded.

## Gate validation

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build/Test-AssetPolicy.ps1` exercises valid approval, changed hash, missing evidence, missing license, wildcard rejection, unapproved assets, informational inventory, source markers and forbidden staged original bundles. The real release gate must continue to exit 1 until unresolved findings are cleared.
