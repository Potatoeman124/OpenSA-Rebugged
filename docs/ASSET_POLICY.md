# Asset and provenance policy

OpenSA source and release artifacts must contain only material that the project has a documented right to redistribute. The fact that a game is old, unavailable for sale, or commonly described as abandonware does not grant redistribution rights.

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
