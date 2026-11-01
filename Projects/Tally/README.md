# Tally

A client-side [Pulsar](https://github.com/StarCpt/Pulsar) plugin for Space Engineers that adds up what something takes to build: the components, the ingots the assembler needs for them and the ore the refinery needs for those ingots. It works from a projection, a blueprint in your F10 library or one of your own grids, uses the definitions the game has loaded (mods included), and writes the list onto datapads you can hand round.

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
- **Ore**: the refinery recipe for each ingot, at the best refinery's base yield in the world, times 1.0905 per yield module (pick 0 to 4). Speed modules and refinery speed don't change amounts. Ore is always the ore of the same name (no stone or scrap recipes).
- Amounts are rounded up. Items without a recipe in the world are listed as not broken down.

The window shows Components, Ingots and Ore as a table you can sort by any column; ingots and ore are in kg.

## Datapad

**Write datapad** tallies again and writes the list onto a datapad in your own inventory: what was tallied and how, then the components, ingots and ore. It's named `Tally: <name>` (`#2` if you already carry one by that name). With something taken off, it lists only what's still to get, as `A (of B)`.

- Tally can't make datapads; carry one. *Auto* uses an empty datapad or the ones already holding this list; pick one in the list to use that one (it asks before overwriting other text).
- A datapad holds 1000 characters, so a long list uses several, named `(1/2)`, `(2/2)`. If you don't carry enough, Tally asks before writing a shortened list.
- A few seconds later Tally checks the datapads show the new name.

**Copy** puts the same text on the clipboard.

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

Only what you already have is read: projectors you have access to, grids you own, your own blueprint folder.

## Files

In `%AppData%\SpaceEngineers\Storage\Tally`: `Tally_Settings.txt` (chat setting and the window's choices). Blueprint files are only read, never changed.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Tally"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
