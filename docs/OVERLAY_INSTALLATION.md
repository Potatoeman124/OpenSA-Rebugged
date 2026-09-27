# OSArB 1.1 overlay installation

The overlay ZIP updates a **working OpenSA 20230905 Windows x64 installation** by replacing files in place. It is not a standalone distribution. It was designed against the installed copy at `E:\Gejms\OpenSA`, whose engine revision matches reBugged's pinned engine.

## For players

1. Close OpenSA and back up its entire installation directory.
2. Move the overlay ZIP to that directory, next to `OpenSA.exe`, and extract directly there. Accept replacement of existing files. Do not create another enclosing directory or remove the old files first.
3. Run the same `OpenSA.exe` or existing shortcut. The menu should display **OSArB 1.1: Return of the RMG**.

The existing engine, runtime and Swarm Assault assets are reused. The archive contains no executable, installer, original-game bundles, settings or user maps. It does not change shortcuts, the registry or the installed-program name. Restore the backed-up directory to undo the update. Custom maps/replays from the old version remain in their old folders; reBugged uses the `1.1` folders.

## Building the ZIP

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build/New-Overlay.ps1 -BasePath "E:\Gejms\OpenSA"
```

The builder validates the baseline version, engine revision and x64 executables before building the Release mod assembly. It compares that assembly and current `mods/sa` content to the baseline and packages only changed files, plus installation instructions, the delta manifest, license, attribution and provenance evidence. No baseline files are written. Rebuilt engine/runtime files are never copied into the payload. Unreferenced old files can remain in place.

Output: `artifacts/1_1_Release/overlay/OpenSA-reBugged-1.1-overlay-20230905-x64.zip` and a SHA-256 sidecar. The sidecar is for verification; only the ZIP is needed to install. The package root directly contains `mods/sa` and `OpenRA.Mods.OpenSA.dll`.

The version-scoped inherited-content exception still applies; the audit is preserved and the eight original-game asset bundles remain excluded. An overlay changes delivery mechanics, not the recorded provenance status.

## Verification

Test by copying the original installation to an isolated artifacts directory, extracting the actual ZIP over that copy, and using **the copy's original** `OpenRA.Utility.exe` for `sa --check-yaml` and `sa --validate-sa-rmg`. Use a separate Support directory for native game checks and reference already installed assets there. Compare hashes of the original installation before and after testing. Keep validation output beside the staging artifacts.

This work stays on its feature branch until the maintainer explicitly authorizes a merge and push. Building does not publish, push or dispatch CI.

## Verified delivery (2026-09-27)

The 92-file ZIP is 874,434 bytes and adds 79 files while replacing 13. The installed engine, native libraries and bundled .NET 6.0.22 remain byte-identical. All 1,187 original installation files were checked before and after the work and remained unchanged.

Validation on the copy, after extracting the actual ZIP:

- Release build: zero warnings and errors. Asset gate passes under the existing 1.1 exception.
- Archive CRC, payload hashes and baseline replacement hashes match; no executable or original-game bundle is included. An incompatible baseline is rejected by the builder.
- Original `OpenRA.Utility.exe`: mod, sequences and maps pass `sa --check-yaml` with inherited warnings.
- Preset matrix: 420 settings round trips, 104 generated maps across sizes 64 through 512 and player counts 1 through 8. One 64x64 four-player Mirror Match case is safely rejected for insufficient starting space.
- Native runtime harness: all 15 presets generate maps and start live skirmish worlds successfully. RMG UI screenshots were visually inspected.
- The older RMG suite passes 22 of 23 groups. Its V13 Ultra continuity group reports three failures. The complete output is byte-identical when run against the earlier full package, so this is an existing failure rather than an overlay-specific regression; generation behavior was not changed in this packaging task.
- Native interactive control was unavailable because the helper could not initialize. Rendering and world startup were checked using the existing game test harness, not an interactive launcher session.

Detailed local evidence: `artifacts/overlay-1.1/verification.json`, `check-yaml.log`, `rmg-validation.log`, `full-package-rmg-validation.log`, `preset-runtime/`, and `preset-worlds/`.
