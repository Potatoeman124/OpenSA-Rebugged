# RMG interface organization

The RMG view groups map identity and layout choices on the left, terrain and colony quantities on the right, and an enlarged map preview in the adjacent panel.

- Left: Preset, Layout Family, Terrain, Size, Players; then Prevent Colony Overlapping, Original surface relations, and Respect Starting Safe Area.
- Layout-specific controls follow the left-hand toggles. Both Strongholds options also use the left alignment.
- Right: Terrain Complexity, Water Amount, Surface Modifiers, Neutral Colony Density, the paired Neutral Colony Types / Starting Ownership buttons, Seed / Random, and Generate Preview.

The window uses a consistent height across families and adapts its width to the UI resolution. The preview uses the largest square that fits beside the controls and map metadata; the map retains its aspect ratio. Its title and author truncation update when the panel changes size. Returning to skirmish restores the original lobby and preview geometry, including download and validation states.

## Verification

Native widget captures cover all ten layout families at 1024 x 600, 1280 x 800, and 1600 x 900. They check control bounds and overlaps, the five preview states, and restoration of the normal lobby. The existing QoL runtime harness also completed its real loopback lobby/startup checks.

Evidence is under `artifacts/rmg-ui/`: `compact-final/`, `wide-final/`, and `large-final/`. The capture instrumentation was temporary and is retained there for reproduction. The normal utility source was restored before delivery. Repository runtime-data validation passed; source-style checking adds no warnings over the existing baseline. A Release build is provided for in-game review.
