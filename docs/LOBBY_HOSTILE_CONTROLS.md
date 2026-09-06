# Lobby hostile controls

## Scope and entry point

Open **Skirmish or Multiplayer -> Options -> Hostile Settings...**.
The feature applies to authored lobby maps and generated maps alike. It is not
part of RMG configuration and does not change a generated map package, terrain,
or preview when adjusted. Initial units are placed at match startup.

Campaign/mission-selector maps and shellmaps retain authored spawning logic.
Existing Pirates, Plants and Flyers checkboxes continue to enable/disable
ongoing spawning. They are also available inside the corresponding sections.
The initial pirate population is independent of the Pirates spawning checkbox.

The window has four tabs, a scrollable body, editable numeric fields, sliders,
per-field default buttons, section reset, Apply and Cancel. Changes are drafts
until Apply. Apply uses the standard host-only synchronized lobby option
commands and server-side allowed-value validation. Non-hosts can inspect the
settings but cannot apply them. A map change closes an open draft window.

## Controls

| Section | Controls |
| --- | --- |
| Initial pirates | Amount; scattered/groups/mixed distribution; Bull/Grenadier/Bazooka weights |
| Pirate spawning | Event interval; first-event delay; minimum/maximum pirates per anthole; maximum living spawned pirates; independent unit weights |
| Plant spawning | Event interval; first-event delay; regular-spawning population cap; weights for all 11 plant actor variants |
| Flier spawning | Event interval; first-event delay; weights for Dragonfly, Desert Fly, Spawn Moth and Flying Machine |

Timing inputs are seconds at Normal game speed (25 simulation ticks per second).
Faster/slower game speeds change their wall-clock durations. Map-default timing
retains the authored fixed or randomized tick interval. Custom inputs specify
a fixed interval/delay. Initial-delay zero means the first available startup tick.

Supported input bounds:
- Initial pirates: 0-500.
- Event intervals: 1-3600 seconds; initial delays: 0-3600 seconds.
- Anthole group limits: 1-50; reversed limits are sorted automatically.
- Pirate/plant population caps: 0-1000.
- Each species weight: 0-1000.

## Defaults and weights

Plant terrain defaults enable exactly one species:
Normal -> Popcorn, Desert -> Thorn Bush, Swamp -> Seed Ball, Candy -> Choc Freckle.
Flier defaults similarly select Dragonfly, Desert Fly, Spawn Moth or Flying Machine.
Pirate composition defaults remain 45/45/10 relative shares.

Every weight is independently editable, including cross-theme choices.
A field still on Terrain default follows the selected map's tileset.
Explicit values remain explicit when the map changes; resetting a field restores
its theme-following behavior.

Weights are non-negative integer tickets, not required percentages.
For 75, 50, 40, 10 the total is 175, and the first entry has probability
75/175 = 42.857...%. The UI shows both the calculated percentage and fraction.
Zero excludes a species; an all-zero pool disables that population's creation.
There are no implicit fallback species after a pool is set to zero.

## Placement and population semantics

Map default for initial amount preserves authored pirates, so distribution and
initial composition only apply when a numeric replacement amount is selected.
A numeric amount suppresses authored Creeps-owned pirate ants and requests a new
population. Zero removes those authored pirate ants. Other units, colonies,
decorations, and pre-placed antholes are not removed.

Placement uses existing playable, passable ground and rejects occupied cells.
It never repaints or clears surfaces, forces symmetry, or adds fairness zones.
If there is insufficient usable ground, it stops at the amount it can safely place.

The pirate cap excludes initial pirates and includes both live generated pirates
and reservations for ants waiting to emerge from holes. A partial group uses
remaining capacity; a full cap blocks further additions, and deaths/removal free
capacity exactly once. Zero disables new generated pirates. It does not delete
initial or already-living units.

The regular plant-spawner cap counts existing plants and seeds, including authored
ones, but does not delete excess plants or override reproduction behavior.
Moths can introduce seeds/Seed Balls even with ordinary plant spawning disabled
or a different regular plant composition. Seed Balls can reproduce. These paths
can exceed the regular-spawner cap. Flying Machines retain their pilot-on-death
behavior. The UI explicitly describes these interactions.

Authored map spawners removed from the rules remain disabled with map-default
controls. Explicit pirate/plant caps or flier intervals can enable the new lobby
controller on those maps, subject to the category's lobby switch.
The new controller avoids the legacy plant counter's spawner-selection and
off-by-one problems. Legacy pirate weighted selection and fractional death
accounting have also been corrected.

## Implementation

- HostileOptions.cs: 33 hidden standard lobby option definitions, bounds,
  terrain defaults, relative-weight selection.
- LobbyHostiles.cs: shared runtime controller, initial placement and event scheduling.
- HostilePopulationBudget.cs: reservations and capacity.
- AntHole.cs / PirateAnt.cs: emergence reservations, live-unit release and legacy fixes.
- HostileOptionsLogic.cs: launcher, tabbed editor, host/read-only guards and drafts.
- mods/sa/chrome/lobby-options.yaml: preserves the normal lobby controls and adds
  the Hostile Settings entry point.
- mods/sa/chrome/hostile-options.yaml: real widget layout.
- RMG's obsolete Amount of Hostiles placeholder is replaced by a pointer to lobby Options.

## Game option presets

Open **Options -> Game Presets...** beside the Hostile Settings button.

1. Configure the normal game options and apply any changes in Hostile Settings.
2. Enter a preset name and click **Save to file**.
3. In a later lobby, choose that preset and click **Load preset**.
4. To update an existing preset, enter/select its name and explicitly tick
   **Replace existing file with this name** before saving.

Each preset is a readable, versioned JSON file in
`<OpenSA user-data folder>/Presets/sa/GameOptions/`. **Copy folder path** copies
the exact local directory. Files can be shared or copied into that directory;
**Refresh list** discovers them without restarting the game. Preset names use
1-80 letters, digits, spaces, hyphens or underscores (without the .json extension).

The file includes every synchronized lobby option, including hidden hostile
options and the original game-option switches/dropdowns. It does not save
player identities, slots, factions, map selection, RMG generation settings, or
server connection details. Save captures the current applied lobby state, not
unapplied edits from another dialog. Presets are loaded explicitly, not
automatically on lobby creation.

Map-default and terrain-default markers remain defaults; explicit values stay
explicit across terrain changes. Loading uses the same host-only option commands
as normal editing. Locked, unsupported and unavailable options are skipped and
listed; options absent from an older preset remain unchanged. All clients can
save a local snapshot, but only the host can apply a preset.

Malformed, oversized, other-mod and unsupported-version files produce a dialog
message without modifying the lobby. Saving uses a temporary file and
same-directory rename; replacement requires the checkbox, and failed saves do
not truncate existing files.

`Test-HostileSettings.ps1` also runs `--validate-sa-presets` for exact round trips,
four-terrain compatibility, weights/defaults, lock/value checks, overwrite
protection and malformed-file handling. The renderer test saves/reopens/loads
presets through actual widgets, checks non-host guards and errors, and captures
the preset dialog at both tested resolutions. Test files are isolated under
`artifacts/hostiles/`, never written to the user's real preset folder.

## Verification

Run:

```powershell
.\scripts\Test-HostileSettings.ps1
.\scripts\Test-HostileSettings.ps1 -Render
```

The basic command builds the mod, runs the focused option/weight/budget tests,
and checks mod/map YAML and sequences. The renderer option additionally constructs
the actual widgets in an isolated engine window, captures each tab and scrolling
state, exercises field editing/normalization/read-only and Apply behavior, and runs
15 real Worlds on the authored Two Armies map. The matrix uses fresh deterministic
runtime RNG seeds across scattered, grouped and mixed placement, normal spawning,
initial-only, spawn-only, zero weights and zero caps. It checks death/refill,
moths on Normal terrain, excluded dragonflies, plant limits and terrain preservation.

This is an engine-backed test harness, not a desktop click replay or a
two-computer multiplayer soak test. It creates no live network lobby and does
not save user settings. Reflection is confined to the opt-in test harness to
access the engine's internal World/WorldRenderer constructors and inspect
queued option commands. Production code uses normal engine APIs.
