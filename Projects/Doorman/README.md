# Doorman

A client-side Pulsar plugin for Space Engineers that closes doors left open. Once a door is fully open, Doorman waits the delay (3 seconds by default) and closes it, the same as switching it to closed in the terminal.

- **Which doors**: doors on grids you own (or your faction owns, if you tick *Faction grids too*), and only doors you have access to. Doors, sliding doors, advanced doors and modded doors are in; hangar doors are off unless you switch them on, for all grids or per grid.
- **Delays**: a default, a grid's own, or a door's own (1-600 s).
- **Per door**: *Default* (follows its grid), *Never*, or *Always* (closed even if it's a hangar or airlock door).
- **Airlocks**: doors with "airlock" in their name or in a group's name are left alone (can be switched off). A door that something reopens right after Doorman closed it while nobody is within 5 m (a timer, a script) is left alone until it has stayed shut for a minute; if it stays open, Doorman tries again after 1, 2, 4, 8 and then every 10 minutes. Players walking in and out never count.
- **Doorways**: if anyone is standing in the doorway (inside the door block's own space), Doorman waits.
- Doors that are off, unpowered, not working or part of a projection are left alone.

**Only while you're here**: Doorman runs on your PC, so it only closes doors on grids loaded in your game. While you're offline or out of range, open doors stay open. A programmable block script keeps working while you're away; Doorman needs no programmable block and costs the server no script time.

## Controls

- **Window**: `/doorman` in chat, the *Doorman: door settings* toolbar action, or `/tim` / the plugin switcher. The on/off switch and options at the top, a grid picker with the delay and hangar setting (*All grids* sets the defaults), the list of doors with each one's delay and what it's doing now, the selected door's own setting, and a Find box.
- **Toolbar actions** on cockpits and remote controls: open the window, and switch auto-close on or off.
- **Chat**:

| Command | Does |
|---|---|
| `/doorman` | Opens the window |
| `/doorman on` / `off` | Switches auto-close on or off for this world |
| `/doorman status` | What it's watching and how many doors it closed |
| `/doorman delay <seconds>` | Sets the default delay |
| `/doorman chat off\|quiet\|normal\|chatty` | How much it comments in chat |
| `/doorman name <name>` | The name its chat lines show under; on its own puts it back |

## What the server sees

| Feature | Sent to the server |
|---|---|
| Closing a door | One request: the terminal's own *Open* switch set to closed. The server checks access and power as for a click. At most five a second; the same door gets at most three tries. |
| Watching doors | Nothing. Doorman reads the door state your game already has. |
| Settings, window, chat commands | Nothing. Settings stay on your PC; nothing is written to your grids (no Custom Data, no names). |
| Toolbar actions | Nothing when used. If you put one in a cockpit's toolbar, the slot is saved with the ship like any other. |

Two players on the same grid both running Doorman: the second request finds the door already closed and does nothing.

## Files

`Doorman_Settings.txt` (chat setting) and `Doorman_<world>_Settings.txt` (on/off, delays, per-grid and per-door settings) in `%AppData%\SpaceEngineers\Storage`.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Doorman"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
