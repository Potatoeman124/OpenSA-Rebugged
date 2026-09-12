# About

This repository contains a recreated Swarm Assault game in OpenRA engine. The goal of this project is to bring modern controls and quality life improvements to Swarm Assault game released in 1999 while still being faithful as much as possible to the original source material. Although the project does not hesitate to add new content which enriches the overall gameplay.

# Current status

At the moment the project is highly complete, all required features are there and what's left is purely cosmetical or very minor. Moreover all 100 missions have been recreated and the project adds several more additional levels to play.

# Swarm Assault vs OpenSA

Since OpenSA project is based on the OpenRA Mod SDK, it carries on a lot of quality life improvements. Here are the key features:

- open source,
- multiplayer,
- Windows, macOS and Linux support,
- support of HD resolutions,
- seamless zooming in and out,
- rally points,
- units controlled by player are less likely to wander than units controlled by AI,
- advanced map editor with the ability of creating own missions with objectives,
- ability of adding own units to the game,
- there's no random map generator in OpenSA, however the project is able to convert legacy maps to ORA format, so nothing is lost.

# How to compile/play

On Windows, use the pinned build entry point:

```powershell
.\build-pipeline.cmd bootstrap
.\build-pipeline.cmd build
.\launch-game.cmd
```

Run `.\build-pipeline.cmd validate` for the complete check suite. See [docs/BUILDING.md](docs/BUILDING.md) for dependency, validation, portable-package, and external-asset import details.

See [Skirmish opponents and selected-unit ranges](docs/SKIRMISH_AND_RANGE_QOL.md) for the Fill Opponents dropdown and the Alt range-display toggle.

# Swarm Assault assets status

OpenSA requires a user-owned copy of the original Swarm Assault game. The project does not download or redistribute original-game assets: they are imported locally into the OpenRA support directory and remain outside this repository and its release packages.

See [docs/ASSET_POLICY.md](docs/ASSET_POLICY.md) for the repository policy and [docs/BUILDING.md](docs/BUILDING.md) for build and local-import instructions.

# Legal disclaimers

* This project is not affiliated with or endorsed by Gate 5 Software or Mountain King Studios in any way.
* This project is non-commercial. The source code is available for free and always will be.
* You are free contribute to this repository, however your contribution must be either your own original code, or open source code with a
  clear acknowledgement of its origin.

# Special thanks

I would love to thank:

* **Andre Mohren** ([IceReaper](https://github.com/IceReaper)) for his expertise in reverse engineering game asset formats. Without this knowledge, this project wouldn't be possible.

* **Matthias Mailänder** ([Mailaender](https://github.com/Mailaender)) for helping out with writing a lot of custom traits which pushed this project forward.

* **Zimmermann Gyula** ([GraionDilach](https://github.com/GraionDilach)) for using his traits written for his [Attacque Superior](https://github.com/AttacqueSuperior/Engine) project.

* **sayedmamdouh ([MikillRosen](https://github.com/MikillRosen))** for creating extra cursors and Ant Hole variants.

* **phredreeke** for upscaling logos which are used for mission preview images.

* **Hooet (교니체)** for creating new missions.

* OpenRA developers for creating this open source engine.

* The original team who created Swarm Assault game.

# Screenshots

![](https://imgur.com/Fiv7Kux.png)
![](https://imgur.com/3MVhFSn.png)
![](https://imgur.com/b8tsTs6.png)
![](https://imgur.com/C6QqtJS.png)
