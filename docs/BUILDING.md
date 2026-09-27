# Building OpenSA on Windows

The supported restoration target is a self-contained 64-bit Windows layout compatible with the known playable installation at `E:\Gejms\OpenSA`. The code build and portable package never require or consume original Swarm Assault assets.

## Pinned baseline

- OpenRA engine commit: `386f691c2e1f469596ef6f58e258a10a176bdc3d`
- .NET SDK: `6.0.428`
- runtime target: `win-x64`, self-contained for portable packages
- mod id: `sa`
- launcher: `OpenSA.exe`

The SDK is downloaded to the ignored `.tools` directory and verified with the SHA-512 checksum published in Microsoft's release metadata. The engine is fetched by exact Git commit into the ignored `engine` directory.

Requirements are Windows PowerShell 5.1 or later, Git, and network access for first-time dependency acquisition. Later builds reuse the local pinned dependencies.

## Commands

From the repository root:

```powershell
.\build-pipeline.cmd bootstrap
.\build-pipeline.cmd build
.\build-pipeline.cmd validate
```

`build` compiles Release engine and mod assemblies. `validate` also performs a Debug/static build, explicit-interface checks, MiniYAML and map checks, bundled Lua 5.1 syntax validation, and an informational asset-provenance inventory.

Generated dependency and package directories are ignored:

- `.tools\` - pinned SDK, NuGet cache, and temporary dependency checkout
- `engine\` - pinned OpenRA source and engine build output
- `artifacts\` - reports, staging directories, archives, and hashes

## Original-game asset import

The original installation root must contain:

```text
Swarm Assault\
  Game\
    Game.ANI
    Game.DDF
    Game.MIN
    Game.SDF
  Start\
    Start.ANI
    Start.DDF
    Start.MIN
    Start.SDF
```

Import it offline:

```powershell
.\build-pipeline.cmd import-assets -OriginalGamePath "C:\Games\Swarm Assault"
```

The default destination is `%APPDATA%\OpenRA\Content\sa`. Use `-SupportDir` only for an isolated test or a custom OpenRA support location. Every source file is checked for exact size and SHA-256 before copying and checked again afterward.

The importer supports the known original Windows release represented by [the manifest](../assets/original-game-manifest.json). A hash mismatch is a hard stop, not permission to weaken verification; another legitimate edition should be researched and added explicitly.

## Portable package

```powershell
.\build-pipeline.cmd portable -Version 20260811
```

This command builds and validates the project, enforces the release asset gate, stages the self-contained `OpenSA.exe` layout, scans the stage for original-game files, and creates a ZIP plus SHA-256 file in `artifacts\packages`.

At the initial restoration checkpoint this command is expected to stop at the release gate. The inherited repository contains media and authored map content whose redistribution provenance is not adequately documented. See [ASSET_POLICY.md](ASSET_POLICY.md). Portable code exists now so it can become usable without redesign once that inventory is resolved.

## CI and release safety

The Windows CI job uses `build-pipeline.cmd validate`. Existing tag packaging jobs are dependent on the release provenance gate, so they cannot publish while unresolved material remains.

No installer should be considered releasable until the same gate passes. The portable directory is the first delivery target because it directly proves the playable executable and layout with fewer unrelated packaging dependencies.

## OSArB 1.1 release packaging

The maintainer authorized a version-scoped exception for existing inherited content. See ASSET_POLICY.md and `mods/sa/RELEASE_EXCEPTION.md`; rights remain unresolved. `build-pipeline.cmd portable -Version 1.1 -Architecture x64` (or x86) applies this exception and preserves the audit. Without an explicit matching version, the strict gate remains in effect.

For local Windows installers, download the official [NSIS 3.11 ZIP](https://sourceforge.net/projects/nsis/files/NSIS%203/3.11/nsis-3.11.zip/download), verify SHA-256 `c7d27f780ddb6cffb4730138cd1591e841f4b7edb155856901cdf5f214394fa1`, and extract `.tools/nsis-3.11`. Then run `scripts/build/New-WindowsInstaller.ps1 -Version 1.1 -Architecture x64` and repeat for x86 after building its portable stage. Installers use architecture-specific reBugged directories and registry keys, include the complete portable payload, importer, audit and notices, and preserve user Support data on uninstall.

The artifact-only `release-artifacts.yml` workflow builds Linux AppImage and macOS DMG on native hosted runners. It uploads downloadable Actions artifacts without creating a tag or publishing a GitHub release. It runs when its workflow file changes on main, or by manual dispatch when available on the default branch. The macOS release uses bundled .NET 6 for Intel and Apple Silicon, requires macOS 10.15 or later (Apple Silicon requires 11 or later), and disables the inherited legacy Mono build. Without an Apple signing identity the DMG is unsigned and unnotarized. Windows installers are also unsigned.
