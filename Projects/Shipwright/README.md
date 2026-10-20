# Shipwright

A programmable block script that turns a station into a ship factory. It welds the projector's blueprint with a few moving welders, releases the finished ship, resets, and builds the next one, as many times as you ask. It runs on the server with nothing but the game's own scripting, so it keeps working while you're offline, and it picks up where it left off after a disconnect, a server restart, a recompile or the grid unloading.

Paste the "Script" region of [Script/Program.cs](Script/Program.cs) into a programmable block, or use the workshop version once it's published.

## How it builds: slice by slice

The ship grows outward from the factory's merge block. The welders sit on a **slice axis** (pistons tagged `[SW Z]`) that carries them away from the merge block one slice at a time. **The welders face back toward the merge block**, so they weld the slice just in front of them, and everything already built is always behind them. They never have to drive through the finished part of the ship.

On each slice the welders visit a number of stops, wait while something within reach is still being welded, and move on when nothing changes. A slice is finished when a whole pass over it makes no more progress. Then the head moves to the next slice. It never goes back.

## Layouts

Set with `Layout` in Custom Data.

| Layout | Slice axis | On each slice | Good for |
|---|---|---|---|
| **Gantry** | Pistons `[SW Z]` | Pistons `[SW X]` and optionally `[SW Y]` carry a small welder head over the slice in a zig-zag | Any size; fewest welders |
| **Arm** | Pistons `[SW Z]` | A rotor `[SW Rotor]` swings an arm and pistons `[SW Radius]` extend it, covering the slice in arcs | Round or compact bays |
| **Wall** | Pistons `[SW Z]` | Nothing moves: a wall of welders covers the whole cross-section | Fast builds, many welders |
| **Pull** | Pistons `[SW Z]` move the **projector's carriage** | The welders stand still in a frame around the ship; the carriage pulls the growing ship back through them | Long ships, fixed welders |

In **Pull**, the projector and the merge block sit on the piston-driven carriage, and the welders are fixed. Gantry, Wall and Pull may also use `[SW X]`/`[SW Y]` pistons if they're there.

An axis can be several pistons stacked end to end with the same tag; the move is split evenly between them. Rotor ranges are in degrees, piston ranges in metres of total extension.

## Blocks and name tags

Put the tag in the block's name. `SW` is the default; change it with `Tag`.

| Name tag | Block | Needed |
|---|---|---|
| `[SW]` | Projector with the ship's blueprint, on the factory grid | Yes |
| `[SW Release]` | Merge block the ship is built onto | Yes |
| `[SW Weld]` | Welders | Yes |
| `[SW Z]` | Slice-axis pistons | Yes |
| `[SW X]`, `[SW Y]` | Plane pistons (Gantry; optional for Wall and Pull) | Gantry |
| `[SW Rotor]`, `[SW Radius]` | Arm rotor and arm pistons | Arm |
| `[SW Dock]` | Connector the ship docks to | `Release=Connector` |
| `[SW Chat]` | Broadcast Controller for chat warnings | Recommended |
| `[SW LCD]` | Status screens (the PB's own screen always shows it) | No |
| `[SW Bay]` | Sensor that sees whether the bay is empty | No |
| `[SW Nanobot]` | Nanobot Build and Repair blocks, for places the welders can't reach | No |
| `[SW Timer Release]` | Timer, triggered just before release (doors, lights, push pistons) | No |
| `[SW Timer Done]` | Timer, triggered after each finished ship | No |

**Building the factory:**
- The blueprint has to include a merge block that lines up with `[SW Release]`. Line the projection up once by hand. The projector must be the same grid size as the ship, so a small-grid ship needs a small-grid factory grid (the gantry can be either size).
- Set the `Z` range so that at its first value, the welders reach the merge block's slice. At its last value they should be past the far end of the longest ship.
- The script turns on the projector's **Keep projection**, so the blueprint stays loaded for the next build.
- Welders draw components through the conveyor system. Keep the stock on the factory grid.

## Commands

Run the programmable block with one of these as the argument (from the terminal, a button or a timer).

| Command | What it does |
|---|---|
| `start` / `start N` | Builds one ship, or N ships one after another |
| `pause` / `resume` | Freezes everything in place (welders off), then carries on |
| `stop` | Stops the build and goes idle. A part-built ship stays on the merge block; `start` won't run until it's released or ground off |
| `skip` | Accepts the stuck blocks and moves on (or releases, if the stuck blocks are the last ones) |
| `retry` | Tries the current slice or step again |
| `release` | Releases whatever is on the merge block now |
| `park` | Moves the head to `Park` (while idle) |
| `test` | Runs the whole path with the welders off: checks reach and collisions before the first real build |
| `refresh` | Reads Custom Data again and finds the blocks again |
| `reset` | Forgets the saved state |

## Warnings in chat

A programmable block can't write to chat by itself. Tag a **Broadcast Controller** `[SW Chat]` and choose who receives it in its terminal (owner, faction or everyone). The script uses its first message slot, so don't keep a message of your own there.

When the factory has to wait, it warns once in chat, then again every `WarnEvery` seconds (300 by default; 0 = only once) while the problem is still there, and says so when it's solved. The LCD always shows the current reason. While its owner is offline, the game only delivers broadcast-controller messages within the world's offline broadcast distance.

What it waits for:
- **Components:** the stuck blocks' missing components are compared with the stock on the factory grid, e.g. `Waiting for components: SteelPlate 120, Motor 4`. It checks again every 30 seconds and carries on by itself.
- **Stuck blocks** (components are there, but they don't get finished): usually out of reach, or a block limit or PCU limit. It names up to five, with their block type and grid position. If there's a Nanobot, it gets switched on first. Welding them by hand works: the factory notices and carries on. `skip` moves on without them. With `StallAction=skip` it moves on by itself after warning.
- **Unplaced blocks:** after the last slice, projected blocks that were never placed (nothing they could attach to while the welders passed). Weld them by hand or use `skip`.
- **Power:** the welders are on but not working.
- **Blocked head:** a piston or rotor doesn't get to its target. A blocked stop inside a slice is skipped. A blocked slice move, or three blocked stops in a row, waits and retries every `RetryEvery` seconds.
- **Projection:** no blueprint loaded, or a part-built ship in the way.

## Releasing

Set `Release` in Custom Data.
- **Merge:** the head moves to `Park` if set, `[SW Timer Release]` fires, and the merge block turns off. The script waits `ClearDelay` seconds and until `[SW Bay]` sees nothing, then turns the merge block back on and starts the next ship. In gravity the ship falls when it's let go: give it landing gear, a floor or a push.
- **Connector:** the ship's connector locks to `[SW Dock]` first, then the merge block turns off. The ship waits on the connector until a pilot undocks it, and then the factory resets.

Pick a `Park` position that keeps the welder head out of the ship's exit path.

## Settings

In the `[Shipwright]` section of the PB's Custom Data, written there on the first run. Use `refresh` after changing them.

| Setting | Default | Meaning |
|---|---|---|
| `Tag` | `SW` | Name tag prefix |
| `Layout` | `Gantry` | Gantry, Arm, Wall or Pull |
| `Release` | `Merge` | Merge or Connector |
| `ClearDelay` | `15` | Seconds after release before the bay counts as clear |
| `SliceStep` | `2.5` | Metres between slices |
| `PlaneStep` | `2.5` | Metres between stops on a slice (along the arc for Arm) |
| `Z`, `X`, `Y`, `Radius` | empty | Range as `a..b` in metres. Empty = the pistons' full range. `10..0` runs the other way |
| `Rotor` | `-90..90` | Arm swing in degrees |
| `ArmLength` | `2.5` | Arm: distance from the rotor axis to the welders with the radius pistons retracted |
| `PistonSpeed` | `0.5` | m/s (0.05 to 5) |
| `RotorRPM` | `1` | Arm rotor speed |
| `MaxImpulse` | `0` | If above 0, sets the pistons' max impulse (N). A low value stops Klang if the head hits something |
| `MinDwell` | `2` | Seconds at each stop, at least |
| `IdleTimeout` | `6` | Seconds without progress before leaving a stop with unfinished blocks |
| `BlockedAfter` | `10` | Seconds without movement before an axis counts as blocked |
| `RetryEvery` | `60` | Seconds between retries when blocked |
| `WeldReach`, `WeldOffset` | `0` | Welder reach radius and its distance in front of the welder. 0 = the game's values (2.26 m large, 1.3 m small, at the welder's front face) |
| `StallAction` | `wait` | wait or skip, for stuck blocks |
| `WarnEvery` | `300` | Seconds between repeated chat warnings; 0 = warn once |
| `ChatDone` | `true` | Say in chat when each ship is finished |
| `Nanobot` | `auto` | auto (on only for stuck blocks), always (on while welding) or off |
| `NanobotWait` | `120` | Seconds the Nanobot gets without progress before the blocks count as stuck |
| `ScanBudget` | `20000` | Most instructions per run for the reach check (the game's limit is 50,000) |
| `Park` | empty | Where the head goes before release and after `stop`, e.g. `Z:10,X:0` |

## Server load

- Runs every 100 ticks, and every 10 ticks only while the welder head is moving. Never every tick.
- Pistons and rotors are given limits and stop themselves, so the script never has to be on time to stop them.
- The reach check reads only the grid cells within the welders' reach, and stops at `ScanBudget` instructions per run, then carries on next run. The block list is refreshed every 5 minutes, not every run.
- Welders are on only while welding. Nanobots (in `auto`) are on only when the welders are stuck.
- The LCD shows the script's average run time in milliseconds.

The real load of any welding factory is physics: moving piston subgrids and welders working. Keep piston speeds moderate, use as few moving subgrids as your layout allows, and set `MaxImpulse` so a collision doesn't turn into Klang.

## On servers with rules

- The printed ship counts against block limits and PCU like any ship. Welding stops when a limit is reached; the factory reports those blocks as stuck.
- **Ship Core Framework (and similar core mods):** add core blocks by hand after release. Leave them out of the blueprint.
- While it's being built, the ship is part of the factory's grid (that's how projector building works), so grid limits apply to factory and ship together until release.
