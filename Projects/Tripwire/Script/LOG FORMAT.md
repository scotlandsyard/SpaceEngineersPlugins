# Tripwire log format (version 1)

The Tripwire PB script writes its log as plain text, one event per line, so people can read it and other programs (the Tripwire plugin, your own scripts) can parse it. This file is the reference for both the script and the plugin.

## Where the log is

- In the programmable block's `Storage` (saved with the world, never sent to players).
- In the PB's Custom Data, below a line that reads exactly `--- Tripwire log ---`. The `[Tripwire]` settings section stays above that line.
- In the Custom Data of every block on the same construct with the tag (default `[Tripwire]`) in its name, up to `MaxCopies` blocks. Anything above the marker line in those blocks is kept as it is.
- Tagged LCD panels also show a readable version, newest first. Only the Custom Data copy follows this format.

After the marker line, every line that starts with `TW1|` is a log line. Ignore any other line.

If the PB is rebuilt with an empty `Storage`, the script restores the log from the copy whose last line is newest.

## Line layout

```
TW1|<time>|<event>|<field>|<field>|...
```

- Fields are separated by `|`. Names never contain `|` (the script replaces it with `/`). Line breaks become spaces, and `;` becomes `,`.
- `<time>` is the server's clock in UTC: `yyyy-MM-ddTHH:mm:ssZ`, for example `2026-10-15T21:04:33Z`.
- Lines are oldest first. The log keeps the last `LogLength` lines and stays under 60,000 characters.
- A reader should skip lines with fewer fields than listed below, and should keep or skip unknown event kinds without failing. New kinds or extra trailing fields may be added in version 1. A change that breaks this layout gets a new prefix (`TW2|`).

## Events

### Visitors

| Event | Fields after the event | Meaning |
|---|---|---|
| `SEEN` | visitor, relation, type, name, source | A visitor came into range of a sensor or a turret's target. Written straight away, before anything else happens. |
| `LEFT` | visitor, relation, type, name, minutes | The visitor hasn't been detected for 30 seconds. `minutes` is how long they were there. |

- **visitor**: `V` and a number (`V3`), counting up for the life of the log. The same number in later lines means the same visitor.
- **relation**: `Enemy`, `Neutral`, `Faction` (faction members and friends) or `Owner`, as seen from the PB's owner.
- **type**: `Human` (a player), `NPC`, `LargeGrid` or `SmallGrid`.
- **name**: whatever the game tells a script. It only gives the names of owners, faction members and friends. For anyone else it's the generic `Human`, `Large Grid` or `Small Grid` text in the server's language.
- **source**: the name of the sensor or turret on your own grid that saw them. The log never contains a position, distance or direction.

### Blocks

| Event | Fields after the event | Meaning |
|---|---|---|
| `DMG` | grid, block, position, percent, present | Damaged: integrity fell to `percent` of full. |
| `GRIND` | grid, block, position, percent, present | Being ground: the build level fell to `percent`. |
| `LOST` | grid, block, position, (empty), present | The block is gone (destroyed, ground away, or cut off in a split). |
| `OWNER` | grid, block, position, (empty), present | A terminal block got a new owner. The new owner isn't logged. |
| `MANY` | grid, summary, (empty), lowest percent, present | Over 10 block events in 10 seconds: one line per grid instead of a flood, e.g. `12 damaged, 3 ground, 1 lost`. |

- **grid**: the grid's name. **block**: the terminal block's name, or the block type for armour and other non-terminal blocks (`LargeHeavyBlockArmorBlock`).
- **position**: the block's cell on its own grid, `x,y,z`.
- **present**: the visitors seen in the last 30 seconds, as `V3:Human;V7:Large Grid`, or empty. "Present" doesn't prove anyone did it.
- A loss smaller than `MinDamagePercent` of the block's full integrity isn't logged until losses add up past it. The same block is logged at most once a minute.

### Script

| Event | Fields after the event | Meaning |
|---|---|---|
| `START` | reason | The script started (`loaded 1.0.0`: world load, recompile or PB rebuilt) or the log was cleared (`cleared`). Anything that happened while the script wasn't running isn't in the log. |

## Example

```
--- Tripwire log ---
TW1|2026-10-15T20:58:02Z|START|loaded 1.0.0
TW1|2026-10-15T21:04:33Z|SEEN|V3|Enemy|Human|Human|Sensor North
TW1|2026-10-15T21:05:10Z|GRIND|Base Alpha|Refinery 2|3,0,-5|62|V3:Human
TW1|2026-10-15T21:05:10Z|DMG|Base Alpha|LargeHeavyBlockArmorBlock|7,1,2|40|V3:Human
TW1|2026-10-15T21:06:02Z|LOST|Base Alpha|Cargo Large|1,0,4||V3:Human
TW1|2026-10-15T21:09:40Z|LEFT|V3|Enemy|Human|Human|5
```
