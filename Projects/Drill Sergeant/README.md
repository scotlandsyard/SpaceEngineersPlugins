# Drill Sergeant

A client-side Pulsar plugin that runs rotor-and-piston mining rigs. A rotor turns an arm of horizontal pistons, vertical pistons lower the drills, and the rig mines out a ring-shaped pit layer by layer. Each rig runs in one of two modes:

- **Plugin mode** (the default): Drill Sergeant ("Sarge") drives the rotor, pistons and drills itself. No programmable block or script needed.
- **Script mode**: the [Automatic Mining Platform](https://steamcommunity.com/sharedfiles/filedetails/?id=1695500366) script by Kezeslabas drives the rig from a programmable block, untouched. Sarge sets it up and sends its commands for you.

## Credit

The mining pattern, its settings and its safety ideas come from the **Automatic Mining Platform** script by **Kezeslabas** (Steam Workshop [1695500366](https://steamcommunity.com/sharedfiles/filedetails/?id=1695500366)). Plugin mode is new code that does the same job; none of the script's code is in this plugin. Script mode runs Kezeslabas' own script in a programmable block. Thanks, Kezeslabas.

## Opening the window

Type `/sarge` in chat. The window lists every rig in the world:

| Column | Meaning |
|---|---|
| Rig | The name you gave it |
| Mode | Plugin or Script |
| State | Ready, Lining up, Mining, Moving arm, Retracting, Paused, Out of reach, Cargo full, Finished, Warning, Conflict (plugin mode); Mining, Paused, Cargo full, Finished, Warning, Not set up, Script off (script mode) |
| Progress | Steps done out of the plan |
| Depth | How far down the vertical pistons are, out of how far they can go |
| Cargo | Fill of the cargo the rig watches |
| Time left | An estimate from the rotor and piston speeds; cargo pauses come on top |

Below the table, the selected rig's details: the step it's on, or what's wrong. For a script rig, the script's status and the PB's detailed info.

**Start** starts or continues a rig. **Pause** stops the rotor, pistons and drills where they are. **Stop** (plugin mode) ends the job: drills and rotor off, pistons back to the start, progress back to step 0; it asks first. In script mode that button is **Set** instead. **Remove** forgets a rig without touching its blocks.

## Setting up a rig

Press **New Rig** near the rig and pick its rotor (rotors with a head within 150 m of you, or on the ship you control). Sarge finds the pistons and drills on the rotor's head side and measures how far out the drills cut.

- **Pistons:** Sarge guesses each piston's job from the way it points: horizontal (pushes the drills outwards), vertical (pushes them down) or vertical inverted (mounted the other way round, retracts to go down). Check them and change any with **Job**.
- **Cargo:** what the cargo pause watches: the drills' own inventories, one container, or all containers on the rig.
- **LCD** (optional): one of your LCDs shows the rig's state.
- **When done / When full** (optional): timer blocks started when the rig finishes, or when the cargo pause sets in.

Settings (the script's defaults):

| Setting | Default | Meaning |
|---|---|---|
| Rotor angle from / to | 0 / 358 | Each sweep goes from one end to the other. 10 to 360 degrees apart, each between -358 and 358 |
| Rotor rpm at 10 m | 0.5 | Rotor speed for drills 10 m from the axis. The rotor turns slower when the drills are farther out, so the tips always cut at the same speed |
| Horizontal / vertical speed | 0.3 m/s | All pistons of a group together |
| Horizontal step | 0 (auto) | How far the arm moves out between sweeps. Auto: the width the drills cut, less 10% |
| Vertical step | 0 (auto) | How far the drills go down between layers. Auto: from the drill's cutting radius (2.4 m for large drills) |
| Pause at / carry on at cargo % | 90 / 50 | The cargo pause |
| Alerts | Chat | Chat line, sound, both or off, when a rig finishes, pauses for cargo or needs you |
| Pause when the cargo is full | On | |
| Pull the arm in before going down | Off | Off: the arm works its way back in on the next layer, which saves the empty trip |
| Share inertia tensor on start | On | Ticks that box on the rotor and pistons when the rig starts. Keeps big rigs steadier. Only where the world allows it and the game has Experimental mode on, like the terminal checkbox |
| Rotor tensor refresh | Off | The script's "dynamic rotor tensor": while sweeping, the rotor's sharing is off and comes on for 2 s once a minute. Costs 2 requests a minute |

## The pattern

Step 0 lines up: the rotor turns to the nearer end of its range and the pistons go to the start. Then the rotor sweeps once, the horizontal pistons move the drills one step out, the rotor sweeps back, and so on until the arm is fully out. The vertical pistons then go one step down and the arm works its way back in. When everything in reach is mined, the arm pulls back to the start and the drills go off. Steps are numbered like the script's (even steps move pistons, odd steps sweep), so a rig can change modes and carry on at the same step.

## Plugin mode

Sarge sends each step once, as the same settings a player types into the terminal: a sweep sets the rotor's velocity towards its other limit; a move sets each piston's limits and velocity. The blocks then stop by themselves at those limits. Between steps Sarge only reads angles, positions and inventories on your PC, which sends nothing to the server.

It only acts while you're online and the server would accept your changes: your character within 15 m of the rig (or anything joined to it), or linked to it by antenna. That's the server's own rule for block changes. Out of reach, the step in progress finishes by itself and the rig stands still. It carries on when you're back. Progress is saved per world and rig, so the rig also carries on after a reconnect or a game restart. On servers that unload or despawn grids far from players, this loses nothing: the rig only ever runs while you're there.

Sarge pauses the rig and tells you when:
- a block is gone, damaged, unpowered or not yours to use;
- a setting it sent reads different a few seconds later: another player, a timer or a script changed it, or the server refused it;
- a mining script is still running the same rig (**Conflict**).

It also tells you if the rig hasn't moved for 90 s.

## Script mode

For worlds where grids stay loaded without you (single player, or servers that keep them running), the script keeps mining while you're away.

1. Subscribe to the [script](https://steamcommunity.com/sharedfiles/filedetails/?id=1695500366), build a programmable block on the rig, load the script into it (Edit > Browse Scripts) and press OK. The script writes its settings block into the PB's Custom Data.
2. In Setup: Mode **Script drives the rig**, pick the PB, **Save**, then **Apply to script**. Sarge adds the script's tags (`/Mine 01/`, `/Hor/`, `/Ver/`, `/Inv/`, `/Adv/`) to the block names, takes the tag off blocks that aren't part of the rig, and writes your settings into the script's settings. If another mining PB on the same grids already uses `/Mine 01/`, it picks the next free tag.
3. Press **Set** on the main page: the script reads its blocks and settings and lines up. Then **Start**.

Start, Pause and Set are each one normal "Run with argument", as if you pressed Run in the terminal. The status comes from what the script shows on the PB's screen; the PB's detailed info is fetched while the rig is selected in the window, the way the terminal does it. **Continue at N** (in Setup) sends `set;N` and `start`, so the script carries on from where Sarge got to.

The script needs an advanced rotor. Its last update says Space Engineers 1.200.025. It compiles against the current game, but check it in game before relying on it.

### Switching modes

The plugin and the script never drive a rig at the same time:
- **To script mode:** Sarge pauses the rig first, and switches the PB back on if Sarge had switched it off.
- **To plugin mode:** Sarge sends `pause` to the script and waits until it has stopped. Then it switches the PB off (a PB that's off can't run) and takes over at the step the script had reached.

## What the server sees

Only requests a player can make by hand, each sent once when it's needed and never every tick:

| Feature | Requests |
|---|---|
| A sweep | Rotor velocity, and turning the rotor on if it's off (limits only when they differ) |
| A piston move | Each piston's limits and velocity, and turning it on if it's off |
| Pause / Stop / cargo pause | Rotor, pistons and drills on or off |
| LCD | One text update at most every 10 s, only when the text changed; only on your own LCDs |
| Timers | The terminal's Start, once per event |
| Share inertia tensor | The checkbox, once at start; with the refresh option, 2 per minute while sweeping |
| Script mode setup | Block renames and one Custom Data edit |
| Script mode commands | One Run per button press |
| Toolbar actions (optional) | Their ids are saved in the grid's toolbar, like any action. Other players see an empty slot |

Nothing is stored on the server. The rigs and their progress live in files on your PC.

## Toolbar actions

Off by default; turn them on in **Settings**. Rig rotors then offer **Drill Sergeant Start/Pause** and **Drill Sergeant Pause** for your cockpit or character toolbar. They only work from your own toolbar: button panels, timers and event controllers run actions on the server, where the plugin isn't.

## Chat

Sarge comments now and then in chat, in olive drab. `/sarge chat off|quiet|normal|chatty` sets how much (or the Chat dropdown at the top right of the window); `/sarge name <name>` changes the name the lines show under. Rig alerts are set per rig in Setup.

## Switching between our plugins

If any of our other plugins are loaded too, the top-left corner of the window has a dropdown with this plugin's name. Pick another plugin there to close this window and open that plugin's. Type `/tim` in chat to open whichever of these windows you used last. See [Shared/README.md](../../Shared/README.md).

## Files

Settings are in `DrillSergeant_Settings.txt`, each world's rigs in `DrillSergeant_<world>_Rigs.xml`, both in `%AppData%\SpaceEngineers\Storage`.

## Reporting bugs

Open an issue on [GitHub](https://github.com/scotlandsyard/SpaceEngineersPlugins/issues) with the plugin's name in the title. Say what you did, what you expected and what happened instead, and attach the game log (`%AppData%\SpaceEngineers\SpaceEngineers_<date>.log`) and Pulsar's log (`%AppData%\Pulsar\Legacy\info.log`) if you can.

## Building

The project also builds `Shared/PluginSwitcher.cs`, `Shared/Personality.cs` and `Shared/PluginStorage.cs` from the repo's `Shared` folder, so build from a full clone of the repo. Set `Bin64` in `Source/DrillSergeant.csproj` if Space Engineers isn't in the default Steam location. Then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/DrillSergeant.dll`.

The plugin uses its own assembly name (`DrillSergeant`), action IDs (`DrillSergeant_*`), chat command (`/sarge`) and storage files (`DrillSergeant_*`), so it can't clash with the other plugins in this repo.
