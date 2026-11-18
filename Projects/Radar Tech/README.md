# Radar Tech

A range finder for Space Engineers, in two forms:

- **The plugin** (`Source`): a client-side Pulsar plugin. It measures through a camera block on your ship (or the camera you're looking through) and shows the distance to the asteroid, planet surface, ship, station or character ahead, just under the crosshair.
- **The script** (`Script`): a programmable block script that does the same with camera blocks and shows it on screens. Nothing to install; it runs on any server that allows scripts.

Both use a camera block's own raycast, so the game's camera rules (charge, range, view cone) apply to both. They measure only the first thing in the line and never see through rock or a ship.

## The plugin

The readout looks like this:

`Radar: Asteroid 4.21 km  |  closing 85 m/s  |  0:49 to contact`

- **Readout**: measures whenever the camera has charge for the full range (about 2 km per second, so a 15 km reading about every 7 seconds) while it's on. *Closing* is how fast the gap is shrinking; *opening* means it's growing. At your current speed, the line turns yellow with *slow down* when you'd reach the target within 60 seconds, and red with *COLLISION IMMINENT* within 10. Both times can be changed (0 turns one off). It hides with the rest of the HUD (Tab).
- **Ping**: one reading with whatever charge the camera has, shown under the crosshair for five seconds and said in chat.
- **Which camera**: in a cockpit, a camera on your ship (rotor and piston parts included) whose view cone covers the ship's aim, the best-aligned one; it measures along that aim (the reticle the game draws in front of the ship), even in third person or free look, and the distance is from the cockpit. Looking through a camera, that camera, and the distance is from it. On foot with no camera, no reading: the line says *Needs a camera*.
- **The camera** must be built, powered and on, yours or shared with you, and within reach: the grid you're on, within 15 m, or linked to you by antenna (the server's rule for using a block). Radar Tech switches the camera's raycast on while it uses it, so the camera draws its charging power, as it would for a script.
- **Own ship in the way**: if the first thing in front of the camera is your own ship, the line says so; place the camera where it can see.
- **Range**: 1 to 50 km, 20 by default, but never past the world's **view distance** (15 km unless the world or server changes it). That's how far the game draws: what you can't see, Radar can't measure. A higher setting is held to the view distance, and `/radar status` says so.

### Controls

- **Hotkey**: Ctrl+Alt+R turns the readout on and off, on foot too. Change it in the window.
- **Window**: `/radar`, the *Radar Tech: window* toolbar action, or `/tim` / the plugin switcher. Readout on/off, range, closing speed on/off, warning times, hotkey, the last reading, a Ping button and a Help page.
- **Toolbox**: the terminal (K or I) has a Toolbox tab listing our loaded plugins, each with an Open button.
- **Toolbar actions** on cockpits and remote controls: *Radar Tech: readout on/off*, *Radar Tech: ping*, *Radar Tech: window*.
- **Chat**:

| Command | Does |
|---|---|
| `/radar` | Opens the window |
| `/radar on`, `/radar off` | Turns the readout on or off |
| `/radar ping` | One reading |
| `/radar range <km>` | Sets the range, 1 to 50 km (held to the view distance) |
| `/radar warn <s> <s>` | Warning times in seconds to contact: yellow (slow down), then red (collision imminent). 0 turns one off. Default 60 and 10 |
| `/radar status` | Shows the settings and the last reading |
| `/radar chat off\|quiet\|normal\|chatty` | How much Radar comments in chat |
| `/radar name <name>` | The name Radar's chat lines show under; on its own puts it back |

### How it measures

With the camera's own raycast (`IMyCameraBlock.Raycast`), the call a programmable block script uses, so its range, charge and view-cone limits apply. Radar Tech uses only what the raycast reports about the hit: its kind, position and speed, never its name, owner or faction. Asteroids and rock far off may not register until you're close enough for the game to give them physics, the same as for any camera.

### Fair play

Radar Tech gives you what a camera and a short programmable block script would: the same camera, the same charge, the same reach. The differences are convenience: no programmable block or screens to build, and a line under the crosshair instead of an LCD. Where a world has in-game scripts switched off it doesn't measure at all, because vanilla lets only scripts use a camera's raycast. It never shows names, owners or factions, and never sees through anything.

### What the server sees

Nothing. The raycast runs on your PC, as it would for a script, and sends no request. A toolbar action saved in a cockpit's toolbar is stored with the ship, like any other toolbar slot.

### Files

`RadarTech_Settings.txt` in `%AppData%\SpaceEngineers\Storage\RadarTech`: readout on/off, range, closing speed, warning times, hotkey and the chat setting.

## The script

1. Put `[Radar]` in the name of one or more camera blocks. Point them where you want to measure (usually straight ahead).
2. Put `[Radar]` in the name of the screens to show it on: text panels, or cockpits and other blocks with screens (the first screen by default). The programmable block's own screen always shows it.
3. Load the script into a programmable block and run it with an argument:

| Argument | Does |
|---|---|
| `ping` | One reading |
| `on`, `off`, `toggle` | Keep measuring, as often as the cameras' charge allows |
| `range <km>` | Sets how far each reading reaches (10 km by default) |
| `warn <s> <s>` | Warning times in seconds to contact: yellow *SLOW DOWN*, then red *COLLISION IMMINENT* (60 and 10 by default, 0 turns one off) |

The screens show what was hit, the distance, closing or opening speed and time to contact, a GPS line for the point hit (copy it from the screen's text into your GPS list), how long ago the reading was, and the cameras' charge.

Like the plugin in a cockpit, it measures along the cameras' forward. Between readings the time to contact counts down from the last one, and the screens turn yellow (*SLOW DOWN*) or red (*COLLISION IMMINENT*) when you'd reach the target within the warning times; a reading over 30 seconds old warns of nothing. The text colour goes back to each screen's own when the warning ends.

What a programmable block can't do, compared with the plugin: it can't read the world's view distance, so the range is whatever you set (the game's camera rules still apply); it can't tell a station from a ship (it says *Large grid* or *Small grid*); and closing speed needs a cockpit or remote control on the ship to know your own speed. Both take readings as often as the cameras' charge allows.

It's built to be light for servers that limit scripts: it runs six times a second (every 10 game ticks), finds its blocks once every 10 seconds, and only rewrites the screens when the text changes (about once a second).

Camera blocks need charge to measure: about 2 km of range per second each, so a 10 km reading needs 5 seconds of charge. With several tagged cameras the script takes turns, so readings come faster. Settings are in the `[Radar]` section of the programmable block's Custom Data: `Tag`, `RangeKm`, `CockpitScreen` (which screen of a block with several, 0 is the first), `SlowDownSeconds` and `ImminentSeconds`.

`Deploy-Script.ps1` copies the script into the game's local script folder, so it shows under *Browse Scripts* in a programmable block. `Script.csproj` is only a compile check against the game's assemblies.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Radar Tech"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
