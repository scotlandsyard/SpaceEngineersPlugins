# Hush

A client-side Pulsar plugin for Space Engineers that lists the blocks that make sound and lets you mute them: one block at a time, every block of a type on one grid, or a whole block type everywhere. Modded blocks work too.

Muting only changes what your own PC plays. Nothing is sent to the server, nothing is stored on the grid, and other players still hear everything.

## Using it

Type `/hush` in chat (or pick Hush in the plugin list at the top left of another of our plugins' windows).

**Blocks on a grid**
- Pick one of your grids in the Grid list (nearest first). Only grids you own a block on, or that have a block shared with you, are listed, and only the blocks you could open in the terminal. You never see what's inside other players' grids.
- A block is listed if its type has a working sound (thrusters, drills, refineries, doors, sound blocks...) or if it has played a working sound since the world loaded (damage sounds don't count: every block has those). The **Why listed** column says which: **Sound** and the sound's name, **Played** and the sound's name, or **Muted**.
- Select a row and press **Mute / Unmute**, or double-click it. **Mute Type on Grid** / **Unmute Type on Grid** do every listed block of the selected block's type on that grid. **Unmute Grid** clears the grid.

**Find** shortens either list as you type. While it's in use, **Mute Type on Grid**, **Unmute Type on Grid** and **Unmute Grid** only change the blocks it shows.

**Types everywhere**
- Every block type with a sound in the game and your mods. A muted type is silent on every grid, other players' included, in every world. The **Sound** column names the sound that type plays.

**Chat**
- Hush has a voice in our plugins' shared chat: a soft-spoken librarian, the quietest of them. The **Chat** dropdown at the top right of the window (or `/hush chat off|quiet|normal|chatty`) sets how much it talks; `/hush name <name>` renames it in chat, `/hush name` on its own puts it back. Only you see the lines.

## How it's saved

| File (in `%AppData%\SpaceEngineers\Storage\Hush`) | What |
|---|---|
| `Hush_Mutes_<world>.txt` | Block mutes for that world: grid ID and the block's place on the grid |
| `Hush_Settings.txt` | Type mutes and chat settings, for every world |

A block mute survives a reload, but not the block being rebuilt or the grid being pasted again. In single player, mutes on grids that no longer exist are forgotten when you open the window.

## Server impact

None. Hush adds no toolbar actions, sends no requests, writes no Custom Data and stores nothing on grids. Muting only takes sound away from you, so it gives no advantage.

## How it works

Every block sound in Space Engineers is played through a 3D sound emitter. Hush patches the emitter (with Harmony, which Pulsar provides) so a sound from a muted block is skipped, including when the game restarts a looping sound. Unmuting lets the block's sound come back on its own.

## Building

`dotnet build -c Release` in `Source`. The game folder defaults to the usual Steam path; pass `-p:Bin64="<path to Bin64>"` if yours is elsewhere.
