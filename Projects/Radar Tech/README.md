# Radar Tech

A range finder for Space Engineers, in two forms:

- **The plugin** (`Source`): a client-side Pulsar plugin. Point your crosshair at an asteroid, a planet's surface, a ship, a station or a character, and it shows the distance just under the crosshair. Works on foot and in a cockpit.
- **The script** (`Script`): a programmable block script that does the same with camera blocks and shows it on screens. Nothing to install; it runs on any server that allows scripts.

Both measure only the first thing in the line and only what's already loaded. They never see through rock or a ship.

## The plugin

The readout looks like this:

`Radar: Asteroid 4.21 km  |  closing 85 m/s  |  0:49 to contact`

- **Readout**: measures six times a second while it's on. *Closing* is how fast the gap is shrinking; *opening* means it's growing. The line turns amber when you'd reach the target in under 10 seconds at your current speed. It hides with the rest of the HUD (Tab).
- **Ping**: one reading, shown under the crosshair for five seconds and said in chat.
- **Distance** is from you (your character, or the cockpit you sit in), not from the camera, so third person reads the same as first person.
- **Skipped**: your own ship, anything docked to it, and your character.
- **Range**: 1 to 50 km, 20 by default.

### Controls

- **Hotkey**: Ctrl+Alt+R turns the readout on and off, on foot too. Change it in the window.
- **Window**: `/radar`, the *Radar Tech: window* toolbar action, or `/tim` / the plugin switcher. Readout on/off, range, closing speed on/off, hotkey, the last reading, a Ping button and a Help page.
- **Toolbar actions** on cockpits and remote controls: *Radar Tech: readout on/off*, *Radar Tech: ping*, *Radar Tech: window*.
- **Chat**:

| Command | Does |
|---|---|
| `/radar` | Opens the window |
| `/radar on`, `/radar off` | Turns the readout on or off |
| `/radar ping` | One reading |
| `/radar range <km>` | Sets the range, 1 to 50 km |
| `/radar status` | Shows the settings and the last reading |
| `/radar chat off\|quiet\|normal\|chatty` | How much Radar comments in chat |
| `/radar name <name>` | The name Radar's chat lines show under; on its own puts it back |

### How it measures

The same way a camera block's raycast does: the game's physics ray for ships, stations, characters and nearby rock, then the voxel shapes of asteroids and planets further out. Only the nearest hit counts. Planets are traced by stepping down towards the ground and then finding the exact surface, so long rays over a planet stay quick. The voxel part runs on a background thread.

### What the server sees

Nothing. Radar Tech only reads what's already on your PC, and sends no requests. A toolbar action saved in a cockpit's toolbar is stored with the ship, like any other toolbar slot.

### Files

`RadarTech_Settings.txt` in `%AppData%\SpaceEngineers\Storage`: readout on/off, range, closing speed, hotkey and the chat setting.

## The script

1. Put `[Radar]` in the name of one or more camera blocks. Point them where you want to measure (usually straight ahead).
2. Put `[Radar]` in the name of the screens to show it on: text panels, or cockpits and other blocks with screens (the first screen by default). The programmable block's own screen always shows it.
3. Load the script into a programmable block and run it with an argument:

| Argument | Does |
|---|---|
| `ping` | One reading |
| `on`, `off`, `toggle` | Keep measuring, as often as the cameras' charge allows |
| `range <km>` | Sets how far each reading reaches (10 km by default) |

The screens show what was hit, the distance, closing speed and time to contact, a GPS line for the point hit (copy it from the screen's text into your GPS list), how long ago the reading was, and the cameras' charge.

Camera blocks need charge to measure: about 2 km of range per second each, so a 10 km reading needs 5 seconds of charge. With several tagged cameras the script takes turns, so readings come faster. Settings are in the `[Radar]` section of the programmable block's Custom Data: `Tag`, `RangeKm`, and `CockpitScreen` (which screen of a block with several, 0 is the first).

`Deploy-Script.ps1` copies the script into the game's local script folder, so it shows under *Browse Scripts* in a programmable block. `Script.csproj` is only a compile check against the game's assemblies.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Radar Tech"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
