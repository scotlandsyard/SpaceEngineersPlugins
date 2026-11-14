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

To clear markers out of your GPS list:

- **Delete all**: deletes every marker of one kind, after asking with the count.
- **Keep newest**: keeps only the newest N of a kind (hidden or not) and deletes the oldest whenever there are more. 0 turns it off, and it is off by default.

**Deleting can't be undone.** Both use the G menu's own Delete, five markers a second, so 100 markers take about 20 seconds. The game keeps no time on a marker, so Quiet Skies notes the order markers arrive in; for markers that were already there, it goes by the cargo drop's number or an encounter's time left.

Quiet Skies never touches any other marker: not your own GPS, not Body Location, not contracts or mod markers. Show and Nearby only only turn a marker back on if Quiet Skies hid it; a marker you hid yourself in the G menu stays hidden.

## Controls

- **Window**: `/skies` in chat, the *Quiet Skies: signal filter* toolbar action, or `/tim` / the plugin switcher. One row per kind with its mode, the Nearby only distance, Keep newest, how many markers there are and how many are shown, and a Delete all button.
- **Toolbar actions** on cockpits and remote controls: open the window, one action per kind that steps through Show, Hide and Nearby only, and one per kind that deletes them all (it asks first).
- **Chat**:

| Command | Does |
|---|---|
| `/skies` | Opens the window |
| `/skies status` | Lists each kind's mode and markers |
| `/skies <unidentified\|unknown\|strong\|all> <show\|hide\|nearby> [km]` | Sets a mode, e.g. `/skies strong nearby 20`, `/skies all hide` |
| `/skies keep <unidentified\|unknown\|strong\|all> <number>` | Keep newest, e.g. `/skies keep unknown 5`; 0 turns it off |
| `/skies delete <unidentified\|unknown\|strong>` | Deletes every marker of that kind, after asking with the count |
| `/skies chat off\|quiet\|normal\|chatty` | How much it comments in chat |
| `/skies name <name>` | The name its chat lines show under; on its own puts it back |

## What the server sees

| Feature | Sent to the server |
|---|---|
| Hide | One *Show on HUD* request per marker: the same request as the G menu checkbox. The server stores it in your GPS list. |
| Show (after Hide) | Two requests per marker Quiet Skies hid: *Show on HUD*, and *Always visible* (the game turns that off when a marker is hidden). |
| Delete all, Keep newest | One *Delete* request per marker: the same request as the G menu's Delete button. The server removes the marker from your GPS list for good. |
| Nearby only | Nothing. The marker is only taken off this PC's HUD. |
| Window, chat command | Nothing. |
| Toolbar actions | Nothing when used. If you put one in a cockpit's toolbar, the slot is saved with the ship like any other. |

Requests are sent at most five a second. Hidden markers stay hidden after a reconnect, even without the plugin, because the server keeps the switch.

## Notes

- The game still plays its arrival sound for a new marker; Quiet Skies applies the setting right after.
- Cargo drop markers are told apart by their name (in English or your game language) or, failing that, by the colour the drop system gives each kind.

## Files

`QuietSkies_Settings.txt` (chat setting) and `QuietSkies_<world>_Settings.txt` (modes, distances, Keep newest, the markers Quiet Skies hid, and the order markers arrived in) in `%AppData%\SpaceEngineers\Storage`.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Quiet Skies"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
