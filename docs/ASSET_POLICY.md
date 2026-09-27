# Asset and provenance policy

By default, OpenSA source and release artifacts require documented redistribution rights. A maintainer-authorized, version-scoped exception is recorded below for inherited content in OSArB 1.1. This does not resolve or relicense that content. The fact that a game is old, unavailable for sale, or commonly described as abandonware does not grant redistribution rights.

## Original-game assets

The original Swarm Assault files are an external runtime dependency. A user supplies their own installed copy and the offline importer copies the eight required files into the user's OpenRA support directory:

`%APPDATA%\OpenRA\Content\sa`

The importer verifies every file against [the known-good manifest](../assets/original-game-manifest.json). It never copies those files into the repository, build tree, portable package, or release archive.

The game may also use the engine's existing registry-based local installer source. There is deliberately no quick-download or mirror fallback.

## Repository content

[The provenance policy](../assets/provenance-policy.json) is default-deny for tracked media and other content-bearing binary formats. Every such file must be either:

- approved with its author, source, license, and evidence recorded; or
- unresolved and therefore a release blocker.

The initial inventory intentionally classifies the existing media, original campaign/map data, installer UI, and packaging artwork as unresolved. This is not a conclusion that every listed file infringes copyright. It means the repository does not yet contain enough evidence to redistribute it safely.

Run an informational inventory with:

```powershell
.\build-pipeline.cmd audit-assets
```

Run the mandatory release gate with:

```powershell
.\build-pipeline.cmd verify-assets
```

The release gate must remain failing until all unresolved items have either been removed, independently recreated, or supported by adequate redistribution evidence and moved to the approved list.

## Contributions

New media or other content-bearing files require provenance before merge. Record the author, original source, applicable license or permission, and a durable evidence location. Do not add extracted or converted original-game assets.

## Evidence-backed approvals

Approvals name one exact repository path and record `author`, `source`, `license`, a local `evidence` document, and the file's `sha256`. The release gate rejects incomplete approvals, wildcard approvals, missing evidence, and changed file bytes. Source-marker findings and forbidden original bundles remain independent blockers.

See [the September 2026 audit](ASSET_PROVENANCE_AUDIT.md) for the remaining inventory and [the verified sound evidence](../assets/provenance/2026-09-18-sounds.md) for the first three approvals. Attribution notices for those sounds live inside the packaged mod at `mods/sa/ASSET_ATTRIBUTIONS.md`.

## Authorized OSArB 1.1 exception

The maintainer chose upstream-equivalent risk acceptance: preserve the audit and attribution, acknowledge unresolved rights, and permit the existing inherited content for release 1.1. See [the release notice](../mods/sa/RELEASE_EXCEPTION.md) and [exact-file snapshot](../assets/provenance/release-1.1-exception.json). This supersedes the strict blocking decision for those files and that release only.

Pass `-ReleaseVersion 1.1` to `Check-AssetPolicy.ps1` to evaluate this exception. Without it, the strict audit still fails. Reports retain every finding and separately count accepted and blocking findings. Changed/new files, invalid approvals, original bundles, and remote-download routes are not exempt. The portable packager passes its explicit version and scans the stage before making an archive.
