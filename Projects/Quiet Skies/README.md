# Quiet Skies

A client-side Pulsar plugin for Space Engineers that filters the signal markers the game puts on your HUD:

| Marker | What it is |
|---|---|
| **Unidentified Signal** | Global encounters |
| **Unknown Signal** | Personal cargo drops |
| **Strong Unknown Signal** | Cargo drops anyone can claim |

For each kind you pick one mode:

- **Show**: the game's normal behaviour.
- **Hide**: off the HUD, using the game's own *Show on HUD* switch (the same as unticking it in the G menu). The markers stay in your G list.
- **Nearby only**: on the HUD only within a distance you set (1-500 km, default 10). This only changes your own screen.

New markers follow the setting as they arrive. Settings are kept per world.

Quiet Skies never touches any other marker: not your own GPS, not Body Location, not contracts or mod markers. Show and Nearby only only turn a marker back on if Quiet Skies hid it; a marker you hid yourself in the G menu stays hidden.

## Controls

- **Window**: `/skies` in chat, the *Quiet Skies: signal filter* toolbar action, or `/tim` / the plugin switcher. One row per kind with its mode, the Nearby only distance, and how many markers there are and how many are shown.
- **Toolbar actions** on cockpits and remote controls: open the window, and one action per kind that steps through Show, Hide and Nearby only.
- **Chat**:

| Command | Does |
|---|---|
| `/skies` | Opens the window |
| `/skies status` | Lists each kind's mode and markers |
| `/skies <unidentified\|unknown\|strong\|all> <show\|hide\|nearby> [km]` | Sets a mode, e.g. `/skies strong nearby 20`, `/skies all hide` |
| `/skies chat off\|quiet\|normal\|chatty` | How much it comments in chat |
| `/skies name <name>` | The name its chat lines show under; on its own puts it back |

## What the server sees

| Feature | Sent to the server |
|---|---|
| Hide | One *Show on HUD* request per marker: the same request as the G menu checkbox. The server stores it in your GPS list. |
| Show (after Hide) | Two requests per marker Quiet Skies hid: *Show on HUD*, and *Always visible* (the game turns that off when a marker is hidden). |
| Nearby only | Nothing. The marker is only taken off this PC's HUD. |
| Window, chat command | Nothing. |
| Toolbar actions | Nothing when used. If you put one in a cockpit's toolbar, the slot is saved with the ship like any other. |

Requests are sent at most five a second. Hidden markers stay hidden after a reconnect, even without the plugin, because the server keeps the switch.

## Notes

- The game still plays its arrival sound for a new marker; Quiet Skies applies the setting right after.
- Cargo drop markers are told apart by their name (in English or your game language) or, failing that, by the colour the drop system gives each kind.

## Files

`QuietSkies_Settings.txt` (chat setting) and `QuietSkies_<world>_Settings.txt` (modes, distances, and the markers Quiet Skies hid) in `%AppData%\SpaceEngineers\Storage`.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Quiet Skies"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
