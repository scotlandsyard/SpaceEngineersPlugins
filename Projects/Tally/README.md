# Tally

A client-side [Pulsar](https://github.com/StarCpt/Pulsar) plugin for Space Engineers that adds up what something takes to build: the components, the ingots the assembler needs for them and the ore the refinery needs for those ingots. It works from a projection, a blueprint in your F10 library or one of your own grids, uses the definitions the game has loaded (mods included), and writes the list onto datapads you can hand round. It can also queue the components in your assemblers.

Open it with `/tally`, or from the plugin list at the top left of any of our plugin windows.

## What you can tally

| Source | What it counts |
|---|---|
| **Projector** | Any projector with a projection loaded that you have access to, nearest first. Projected sub grids count too |
| **Blueprint (F10)** | A blueprint from your local blueprint folder (the F10 screen's Local list). Cloud and workshop blueprints aren't read; save one locally first. Blocks from mods the world doesn't have are left out and counted |
| **My grid** | A grid you own, with what's joined to it by rotors, pistons and hinges (if you own that too, or nobody does): what the ship cost |

**Still needed** (projector or grid): a projection counts only the blocks not built yet, plus what built but unfinished blocks still lack, matched the way the projector does it (same place, same block). A grid counts what unfinished or damaged blocks lack. Unticked, or for a blueprint, you get the full cost.

**Minus what I carry** and **Minus the grid's cargo** take off what you have, down the chain: components you have aren't made, so their ingots aren't needed; ingots you have aren't refined, so their ore isn't needed. The server keeps your own inventory up to date on your PC, but a grid's inventories only while you have one open in the terminal, so open the grid's inventory first for current cargo numbers.

## The maths

- **Components**: each block's components from its definition.
- **Ingots**: the assembler recipe for each component, divided by the world's assembler efficiency unless the recipe ignores it (as `MyAssembler` does).
- **Ore**: the refinery recipe for each ingot, at the refinery yield you type in percent, as the refinery's terminal shows it (modded refineries differ; vanilla is 100%, about 141% with four yield modules). Left empty, it uses the best refinery in the world without modules. Speed modules and refinery speed don't change amounts. Ore is always the ore of the same name (no stone or scrap recipes).
- Amounts are rounded up. Items without a recipe in the world are listed as not broken down.

The window shows Components, Ingots and Ore as a table you can sort by any column; ingots and ore are in kg.

## Datapad

**Write datapad** tallies again and writes the list onto a datapad in your own inventory: what was tallied and how, then the components, ingots and ore. It's named `Tally: <name>` (`#2` if you already carry one by that name). With something taken off, it lists only what's still to get, as `A (of B)`.

- Tally can't make datapads; carry one. *Auto* uses an empty datapad or the ones already holding this list; pick one in the list to use that one (it asks before overwriting other text).
- A datapad holds 1000 characters, so a long list uses several, named `(1/2)`, `(2/2)`. If you don't carry enough, Tally asks before writing a shortened list.
- A few seconds later Tally checks the datapads show the new name.

**Copy** puts the same text on the clipboard.

## Queue in assemblers

**Queue in assemblers** tallies again and opens the Queue page. Pick an assembler, or a terminal group of them, that you have access to. The page shows what would be queued before anything is sent.

- Each component goes whole to one assembler, the one with the shortest queue, so a group shares the bill out; co-op assemblers then take their share from there.
- Only components are queued, never ingots or ore, and only ones these assemblers have a blueprint for. The rest is listed as not queued. Damaged assemblers and ones set to disassemble are left out.
- It queues what's still to get when the tally took something off, else the whole bill.
- *Count what's already queued there* (on by default): what the assemblers already have queued, or finished in their output, counts as on its way, so pressing Queue twice doesn't double it.
- *Minus the assemblers' grid*: also takes off what's in that grid's inventories and what's docked to it.
- Queue asks first, then checks a few seconds later that the assemblers show the orders. **Undo last queue** takes Tally's last queue back out, except runs already started or passed on to co-op assemblers. It only takes off what Tally added, never other orders in the same queue.
- **Reach:** in multiplayer the server only takes queue changes from a player within 15 m of the assemblers' grid (or what's docked to it) or linked to it by antenna. Tally checks the same rule first: assemblers out of reach are marked "out of reach" in the list, and Queue and Undo send nothing to them and say so, so the server never logs refused requests.
- Stockpile Manager and BaR Maid queue on assemblers set to Main or Co-op, and inventory scripts may queue too. The page warns when the target has a Main or Co-op assembler. Tally never changes an assembler's mode, cooperative switch or Custom Data. For a big bill, a group nothing else uses is best.

## Send to an SSM crate

With [Sacrificial Stockpile Manager](../Sacrificial%20Stockpile%20Manager) loaded, the Queue page can also send the components to one of your own cargo containers (not shared ones). Pick it under *SSM crate* and press **Send to SSM crate**. It sends what's still to get when the tally took something off, else the whole bill. Stockpile Manager opens on that container with a preview of the minimums it would add (as *Fill once*, which you can change there). Nothing changes until you press Apply there. Without Stockpile Manager the button is greyed out. The hand-over uses SSM's [bill API](../Sacrificial%20Stockpile%20Manager/BILL-API.md).

## Commands

| Command | What it does |
|---|---|
| `/tally` | Opens the window |
| `/tally pad` | Writes the last list to datapads |
| `/tally help` | Opens the Help page |
| `/tally chat off\|quiet\|normal\|chatty` | How much Tally comments |
| `/tally name <name>` | The name Tally's chat lines show under; on its own it puts it back |

## What the server sees

| Feature | Sent to the server |
|---|---|
| Tallying, the table, Copy | Nothing: projections, grids, inventories and definitions are already on your PC, and blueprints are your own files |
| Write datapad | One datapad edit request per datapad, the same one the game's datapad screen sends when you press OK, only for datapads in your own inventory. The server saves the name and text with your inventory |
| Queue, Undo last queue | One add-to-queue request per order and one remove request per queue entry, the same ones the terminal's production screen sends. The server checks access to the assembler and that you're within 15 m of its grid or connected to it by antenna; Tally checks that first and sends nothing out of reach, so the server never sees a refused request. Only queues change: nothing is ever put into an inventory |
| Send to SSM crate | Nothing: the message to Stockpile Manager stays inside your own game. What it does after you press Apply is its own |

Only what you already have is read: projectors you have access to, grids you own, your own blueprint folder.

## Files

In `%AppData%\SpaceEngineers\Storage\Tally`: `Tally_Settings.txt` (chat setting and the window's choices). Blueprint files are only read, never changed.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Tally"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
