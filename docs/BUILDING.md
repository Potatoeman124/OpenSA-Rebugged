# Building OpenSA on Windows

The supported release target is an overlay ZIP for a working OpenSA 20230905 x64 installation. Source builds use the same pinned OpenRA engine. Building code or the overlay never requires original Swarm Assault assets.

## Pinned baseline

- OpenRA engine commit: `386f691c2e1f469596ef6f58e258a10a176bdc3d`
- .NET SDK: `6.0.428`
- runtime target: `win-x64`; the overlay reuses the installed game runtime
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

## CI and release checks

The Windows CI job uses `build-pipeline.cmd validate`. Linux source-build checks remain in `ci.yml`. The retired Windows installer, standalone portable, Linux AppImage, and macOS DMG packaging scripts/workflows have been removed; the overlay is the current release path.

The maintainer authorized a version-scoped exception for inherited content in OSArB 1.1. See [ASSET_POLICY.md](ASSET_POLICY.md) and `mods/sa/RELEASE_EXCEPTION.md`; rights remain unresolved. The overlay builder applies the existing exception, preserves the audit, and scans its payload before creating the archive. The strict gate remains available through `verify-assets`.

## Overlay for an existing OpenSA installation

For the current delivery strategy, use [OVERLAY_INSTALLATION.md](OVERLAY_INSTALLATION.md). **Terminal > Run Task... > OpenSA: Build Overlay ZIP** in VS Code (or `build-pipeline.cmd overlay`) prepares dependencies and creates one ZIP directly in `artifacts` for a working OpenSA 20230905 x64 installation. It reuses the installed engine, runtime and assets, and packages only the mod delta plus notices and audit evidence. Building uses the checked-in baseline hash inventory, so no local OpenSA installation or original assets are needed. The task does not publish anything.
