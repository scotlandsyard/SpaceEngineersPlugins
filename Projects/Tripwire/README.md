# Tripwire

Find out who visited your grid and what happened to its blocks while you were away, and get a warning while you're nearby. Tripwire has two parts. They work together, and each works without the other:

- **The Tripwire script**, a programmable block script that runs on the server with nothing but the game's own scripting. It logs visitors and block damage while you're offline, into Custom Data and LCDs. Nothing needs to be installed for it to work.
- **The Tripwire plugin**, a client-side Pulsar plugin. It warns you in chat when your blocks are damaged, ground or lost while their grid is loaded on your PC, and shows the script's logs in a window.

Neither part ever tells you where an attacker is. The plugin names your own blocks and grids, never a position, distance or direction to anyone.

## The script

Paste the "Script" region of [Script/Program.cs](Script/Program.cs) into a programmable block, or use the workshop version once it's published. It runs every 100 ticks and spreads its work over several runs. By default it uses at most about 10,000 instructions per run (a fifth of the limit), so it stays light on the server.

**What it logs** (the format is in [Script/LOG FORMAT.md](Script/LOG%20FORMAT.md)):
- **Visitors** its sensors see, and the targets of its turrets, the moment they arrive and again when they leave. The game only tells scripts the names of owners, faction members and friends. Anyone else is logged as a numbered visitor, such as `V3 Human`.
- **Terminal blocks** of the construct (the PB's grid and the subgrids on rotors, pistons and hinges): damaged, being ground, lost, or given a new owner.
- **Armour and other non-terminal blocks**: the same. It has to find them by searching the grid's space cell by cell, so on a big grid this coverage builds up over the first minutes or hours.
- Each block event lists the visitors seen in the last 30 seconds. If more than 10 block events happen within 10 seconds, it writes one summary line per grid instead.

**Where the log goes:**
- The PB's Custom Data, below `--- Tripwire log ---`.
- The PB's own storage, saved with the world.
- The Custom Data of every block with `[Tripwire]` in its name, so grinding one copy doesn't erase it. Tagged LCDs also show it on screen, newest first.
- If the PB is ground and rebuilt, it picks the log up again from the newest surviving copy.

**Settings** are in the `[Tripwire]` section of the PB's Custom Data, written there on the first run:

| Setting | Default | Meaning |
|---|---|---|
| `Tag` | `[Tripwire]` | Blocks with this in their name get a copy of the log |
| `LogRelations` | `Enemies,Neutral` | Which visitors to log: any of Enemies, Neutral, Faction, Owner |
| `DetectGrids` | `true` | Log ships and stations, not just characters |
| `UseTurrets` | `true` | Also log what the construct's turrets are targeting |
| `WatchArmor` | `true` | Watch armour and other non-terminal blocks |
| `SensorRange` | empty | Empty leaves the sensors as they are; 1-50 sets every sensor's fields to that many metres |
| `IgnoreNames` | empty | Comma-separated names never to log (works for friends; the game doesn't name others) |
| `MinDamagePercent` | `2` | Smallest loss logged, as a percent of the block's full integrity (smaller losses add up) |
| `LogLength` | `200` | Lines kept |
| `WriteIntervalSeconds` | `10` | Copies are updated at most this often, and only when something changed. A new visitor is written straight away |
| `MaxCopies` | `4` | How many tagged blocks get a copy |
| `InstructionBudget` | `10000` | Instructions the script may use per run (the game's limit is 50,000) |
| `ScreenLines` | `30` | Lines shown on tagged LCDs |
| `ScreenUtcOffset` | `0` | Hours added to the UTC times on LCDs (the Custom Data log is always UTC) |
| `AlarmTimer` | empty | Name of a timer block to trigger when an enemy arrives |

**Sensors:**
- Turn on Detect Players, and Detect Enemy and/or Neutral (plus the ship types you want).
- The script checks its sensors every run, about every 1.7 seconds. For an instant record, add the PB's **Run** action with the argument `seen` to a sensor's "detected" action slot.
- Other arguments: `write` saves the copies now, `clear` empties the log.

## The plugin

Open the window with `/tripwire`, or with `/tim` and the plugin list at the top left of any of our plugin windows.

**Alerts, no script needed.** While a grid is loaded on your PC, the plugin watches:
- every block of grids you own (or your faction owns, if you tick that in Settings);
- the blocks you own on other grids.

**What gets reported:**
- A block that loses integrity: damaged, or ground when its build level drops.
- A block that disappears after losing integrity. A block that moves to another grid in one piece (a split or merge) isn't reported.
- A terminal block that stops being yours.
- Small losses add up until they reach 2% of the block, and the same block is reported at most once a minute.

**How you're told:** chat gets one line per grid at most every 30 seconds, with an optional sound. The window keeps every event.

**When it stays quiet:**
- For the ship you're piloting.
- For grinding near you while you hold a grinder, or near your ship while its grinders are on.
- `/tripwire mute 30` silences chat for 30 minutes.

**Script logs.** The plugin reads Tripwire logs from grids you could open in the vanilla terminal right now: you have access to the block, and you're on the grid, standing at it, or connected to it through antennas. Lines you haven't seen yet are yellow, and chat tells you when a grid with news comes within reach. Closing the window or **Mark All Read** marks them as seen.

**Copy to Datapad** writes the rows shown onto an empty datapad in your inventory, up to 10,000 characters. You can keep it or hand it to an admin. It never writes over a datapad that has text.

**Optional, off by default: names near a damaged block.** It adds the names of players within 50 m of the damaged block, but only players whose name the game's HUD is already drawing for you at that moment (relayed by your antennas), and only in worlds that show player names on the HUD. It never gives a position, distance or direction.

**Never used:** the game sends every client the identity of whoever grinds or shoots a block, but Tripwire doesn't read it. It only compares your blocks' integrity over time, the numbers the terminal shows.

## Fair play and what the server sees

| Part | What the server receives | Notes |
|---|---|---|
| Script, watching | Nothing beyond running the PB | Every 100 ticks, about 10,000 instructions per run by default; the PB's own text shows the average |
| Script, writing the log | Custom Data and LCD text changes, which the game sends to players nearby | Only when something changed, at most every 10 seconds; a quiet grid sends nothing |
| Script, `SensorRange` / `AlarmTimer` | Sensor field changes (only when they differ), a timer trigger | Vanilla block settings |
| Plugin, alerts and log reading | Nothing | It reads what the game has already sent to your PC |
| Plugin, Copy to Datapad | One datapad edit, the same request as editing a datapad by hand | Your own item |

Servers have their own rules about plugins. Ask the admins before using any plugin on a server.

## Chat personality

The Chat dropdown at the top right sets how much the Tripwire character comments, alongside the alerts: Off, Quiet, Normal or Chatty. `/tripwire chat <level>` does the same, and `/tripwire name <name>` renames it in chat. The alerts themselves are plain chat lines and appear at every level. Turn them off in Settings.

## Names used by this plugin and script

| What | Name |
|---|---|
| Assembly / namespace | `Tripwire` |
| Chat commands | `/tripwire`, `/tripwire mute [minutes]`, `/tripwire unmute`, `/tripwire chat <level>`, `/tripwire name <name>` |
| Settings file (local storage) | `Tripwire_Settings.txt` |
| Alerts (local storage) | `Tripwire_<world>_Alerts.txt` |
| Read marks (local storage) | `Tripwire_<world>_Visits.txt` |
| Script: Custom Data section, block tag | `[Tripwire]` |
| Script: log marker, line prefix | `--- Tripwire log ---`, `TW1\|` |
| Toolbar actions, GPS markers | none |

## Reporting bugs

Open an issue on [GitHub](https://github.com/scotlandsyard/SpaceEngineersPlugins/issues) with "Tripwire" and "script" or "plugin" in the title. Say what you did, what you expected and what happened instead. Include the PB's on-screen text for the script, and the game log (`%AppData%\SpaceEngineers\SpaceEngineers_<date>.log`) and Pulsar's log (`%AppData%\Pulsar\Legacy\info.log`) for the plugin.

## Building

Plugin:
```
cd "Projects/Tripwire/Source"
dotnet build -c Release
```
The DLL is `Source/bin/Release/net481/Tripwire.dll`. The project links `Shared/PluginSwitcher.cs`, `Shared/Personality.cs` and `Shared/PluginStorage.cs`, so a Pulsar hub release lists those next to `Projects/Tripwire/Source/`.

Script: `dotnet build -c Release` in `Projects/Tripwire/Script` compile-checks it against the game's assemblies (the game's own script compiler is the real test). `Script/Deploy-Script.ps1` copies the in-game part to `%AppData%\SpaceEngineers\IngameScripts\local\Tripwire\Script.cs`, where a programmable block's Browse Scripts finds it.
