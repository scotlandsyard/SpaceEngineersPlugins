# Sacrificial Stockpile Manager

A client-side Pulsar plugin for inventory management and auto-restocking. It takes ideas from inventory scripts such as Isy's Inventory Manager, Mamba Inventory Manager, GOAT Sorter and iBex, but everything is set up in an in-game menu instead of block names or Custom Data:

- Shows the inventory of every ship and station you own, including ones that aren't loaded right now (as they were when last in range).
- Sorts items into the storage blocks you choose (by category or by single item).
- Keeps any block between a minimum and a maximum of an item: lockers, reactors, generators, turrets, refineries.
- Keeps grid-wide quotas by queuing the shortfall in assemblers (autocraft).
- Shows status pages on LCD screens.
- Works alongside [SHEPHERD](https://steamcommunity.com/sharedfiles/filedetails/?id=3824156985), an inventory script for a programmable block: where SHEPHERD runs, Stockpile Manager leaves its jobs to it and shows its state (see [Working with SHEPHERD](#working-with-shepherd)).

## Opening the menu

- Type `/ssm` in chat, or
- Drag any block with an inventory or a screen onto a toolbar and pick **Stockpile Manager**. The menu opens on that block.

The same blocks also get the toolbar actions **Stockpile Manager: Sort now** and **Stockpile Manager: Unload docked ships**, and the chat has `/ssm sort` and `/ssm unload` (they act on the grid you're on or nearest to).

| Control | What it does |
|---|---|
| Grid | Your ships and stations: loaded ones first, then the rest with when they were last seen. Ships docked by connector are listed under the grid they're docked to; pick one to see only that ship |
| View | All grids, Overview, Items & quotas, Blocks, Block settings, Block type limits, Production, Production details, Refinery priority, Displays (LCD), Log, Help |

The window closes with the X in its corner or with Esc. Tables update every half second. Click a column header to sort.

## Views

| View | What it shows | Buttons |
|---|---|---|
| All grids | Every grid: last sync (Live when loaded), distance, storage used, storage size, fill, kinds of item; totals under the table | Open grid, GPS marker, Remove from list, Help |
| Overview | Grid settings (double-click to switch), status, storage used / size / free, item totals, warnings, recent actions | Change setting, Sort now, Unload docked ships, GPS marker |
| Items & quotas | Every item on the grid with its quota, maximum, queued amount and autocraft / disassembly state | Set quota, Set maximum, Clear quota, Autocraft on/off; Find box |
| Blocks | Every block with an inventory: role, fill %, used / size in litres, settings | Edit block, Turn on / off, Rescan, Help |
| Block settings | One block: role, fill priority (0-9), Accepts checkboxes, and per item what's there and its min/max (including limits from its type) | Set minimum, Set maximum, Clear limits, Back; Accept item |
| Block type limits | Minimums and maximums for every block of a type on the grid: reactors, O2/H2 generators, turrets and guns, cockpits and seats, ship tools, gas tanks, refineries, assemblers. Shows how many blocks it covers and how many are below or above | Set minimum, Set maximum, Clear limits, Help |
| Production | Assemblers, refineries, reactors, generators: assembler mode, state, and queue or contents | Details, Edit block, Turn on / off, Assembler mode |
| Production details | One assembler: its mode (Main / Co-op / Manual list at the top), its queue (with progress) and every material the queue needs, how much the grid has and how much is missing, and whether it blocks the current item. One refinery: input in refining order, and output | Remove from queue, Turn on / off, Edit block, Back |
| Refinery priority | Every refinable ore: its priority, amount in storage and in refineries, why it has its place (a short ingot quota or your list) and which refineries are working on it | Raise priority, Lower priority, No priority, Help |
| Displays (LCD) | Every screen on the grid and the page it shows | Show page, Stop showing, Rescan, Help |
| Log | What the plugin did on this grid, newest first | Clear log, Rescan, Help |

**Storage** in All grids, the Overview and the LCD Overview page means blocks acting as Storage or Stock (cargo containers unless you change their role), not counting docked ships. Machines, cockpits and tools are left out so they don't inflate the totals.

**Remove from list** is for grids that were deleted or destroyed. It removes the grid and its settings (block settings, quotas, LCD assignments; screens elsewhere that showed it go back to their own grid). A loaded grid can't be removed. A removed grid that still exists comes back, with default settings, the next time it's in range.

In Items & quotas and Block settings, pick an item by selecting its row or from the list under the table, type an amount (`500`, `2,500`, `2.5k`, `1.2M`) and press the button. A blank amount clears a minimum or maximum.

## Grid settings

Set in the Overview. All are off for a new grid except Clean production blocks, and nothing moves until Automation is on.

| Setting | What it does |
|---|---|
| Automation | Apply minimums, maximums, sorting and intake draining. While off, nothing is moved and the menu only shows what would need doing. |
| Autocraft | Queue the shortfall of quotas in the grid's assemblers |
| Clean production blocks | **On by default.** Move what refineries and assemblers made into storage, and take anything out of an assembler's input that its queue doesn't need (for example components a modded blueprint pulled in, which can fill the input until nothing assembles). While an assembler disassembles, the parts are moved out and the items waiting to be taken apart are left alone. Cooperating assemblers keep what any queue on the grid needs |
| Survival kits autocraft | Let autocraft use survival kits too |
| Disassemble surplus | Disassemble what's above an item's maximum (Items & quotas) |
| Keep bottles filled | Take bottles that aren't full to a gas tank to refill, and put them back when full (needs Automation) |
| Blocks shared with me | Off: only blocks you own are managed. On: blocks shared with your faction or everyone, and unowned blocks, are managed too |
| Chat name (all grids) | The name Stockpile Manager's chat lines show under. Change it with `/ssm name <name>` in chat (blank resets it) or from Wilson's window |

**Personality.** Stockpile Manager has a voice: a solemn high priest of logistics who treats containers like sacred reliquaries. Its name shows in salmon. It comments in chat (on your screen only, nothing is sent to anyone) when the menu opens, after a Sort now or a big sorting pass, when it fills a block to its minimums, when it queues quota work, when a quota is met, when it disassembles surplus, when a grid's storage fills up, and when a grid goes out of range. It doesn't greet on load; with Wilson loaded, it answers Wilson's roll call instead. Quiet keeps only the full-storage warning. Lines come from `Source/Personality.txt` through the shared `Shared/Personality.cs`, which paces the whole plugin family: one turn per cycle (a cycle is 2 minutes on Chatty, 5 on Normal, 15 on Quiet), important lines after 30 seconds, and the same event at most every 10 minutes. The **Chat: Normal** list at the top right of the window (Off, Quiet, Normal, Chatty) sets how often it talks, in every window of our plugins. **Chat name (all grids)** in the Overview shows the name its lines appear under; change it with `/ssm name <name>` (at most 24 characters; `/ssm name` alone goes back to Stockpile Manager) or from Wilson's window. Both settings are stored in `SacrificialStockpileManager_Preferences.xml` in local storage, the same for every world.

**Whose blocks.** The game lets a player use blocks shared with their faction, blocks shared with everyone, and blocks nobody owns. By default the plugin only manages blocks the player owns. It never takes from, puts into or queues on anyone else's block, and never writes to their screens. Switching a block on or off, setting up a screen on it, giving it the Manual role and changing an assembler's mode (Custom Data) are refused for blocks that aren't yours, the same way. The server checks every move and queue request against the player's access either way, so the plugin can't do more than the player could by hand.

**Reach.** The server only accepts item moves, queue changes and screen text from a player who is within 15 m of the grid (or any grid docked to it by connector, rotor or hinge), or linked to it by antenna. For anything farther away it refuses each request and logs a failed validation, which shows up in the admin's list. So when you are out of reach of a ship or station, the plugin pauses everything that sends requests for it (moves, queuing, Sort now, screen text), keeps showing what it last read, and the Overview says "out of reach: automation paused". It resumes by itself when you come back. Not applied in single player or on the host of a game. The log line "Moved ..." is written when the server has confirmed the move.

**Sort now** runs everything Automation would do, once, until a pass has nothing to move (at most 2 minutes), even while Automation is off. **Unload docked ships** moves the cargo of the player's ships docked to the grid (cargo containers, connectors, collectors; not Stock or Manual blocks, and above any minimum) into the grid's storage. A grid's main grid is a station if it has one, so unloading always goes ship to station. A ship keeps its own quotas on board: only what it holds above a quota is unloaded.

**Priority** (0-9, Block settings): among storage blocks that accept an item equally well, the higher number fills first.

**Assembler modes.** Shared with our other plugins through `Shared/AssemblerModes.cs` and stored in the assembler's Custom Data as `[TIM] Assembler=Main|Coop|Manual`. Every assembler starts out **Manual** (no mode stored), which means no plugin queues on it or changes it, so autocraft only uses assemblers you opt in. Main takes the orders. Co-op stays in the game's cooperative mode and helps a Main assembler, because in the game a cooperative assembler only takes work from a connected assembler that isn't cooperative. Each quota shortfall is queued as one order on a Main assembler (the shortest queue if there are several). What's already queued is counted on every assembler. If none of the Main or Co-op assemblers is Main, one is made Main the first time something is queued. Set a mode with the Mode list in Production details, or the Production view's Assembler mode button (Manual > Main > Co-op). Setting an assembler's role to Manual in Block settings also sets its shared mode to Manual. The mode only decides who gets orders; the assembler's inventory is handled by its role, so Clean production blocks still empties a Manual assembler's output.

**BaR Maid goes first.** When the BaR Maid plugin is loaded too (checked by its assembly name, `BaRMaid`), its repair orders take priority over quotas. Autocraft then only queues while every Main and Co-op assembler of the ship or station is idle, and queues about a minute of work at a time (worked out from the blueprint's time, the world's assembler speed and each assembler's speed and modules). Whatever BaR Maid queues waits at most for that batch, and no more quota work is added until BaR Maid's queue is done. Without BaR Maid, the whole shortfall is queued at once as before.

**Disassembly.** The plugin picks one idle Main or Co-op assembler (on, powered, nothing queued, not cooperating; normally the Main one), switches it to disassembly with the same request as the terminal's mode switch, and queues the surplus. Once that queue is done, it switches the assembler back to assembly. The game keeps the assembly queue aside meanwhile. It only switches back assemblers it switched itself, and it remembers them across restarts. The assembler pulls the items itself through conveyors, from any connected inventory. If the surplus disappears first, the plugin takes those entries out of the queue.

**Refinery priority.** Refineries refine their input top to bottom. With Automation on, the plugin moves the highest-priority ore a refinery holds to the front, using the same request as dragging a stack inside an inventory. If the refinery holds none of a better ore that's in storage, it brings some in, sending back its lowest-priority stack first if it's full.

**Refine by ingot need.** With Autocraft on, a short quota for an item only refineries make (ingots, gravel) steers the refineries instead of the assemblers: the ore that makes it goes to the top of the refinery priority until the quota is met, then drops back to where you put it. How it combines: short items come first, the most short (as a share of its quota) at the top; each pulls up every ore that makes it, the one giving the most per unit of ore first; an ore that makes several short items is listed once, at the place of the most short; then your own list follows, unchanged and not saved over. It uses the same moves as your own priorities (needs Automation), only for the station's own quotas and refineries, not a docked ship's. The Refinery priority view shows the order in use with a Why column, and the Items view says what is being done, for example "Short 5,000: Platinum Ore moved to the top of refinery priority", or why nothing can be (no such ore here, Automation off).

**Bottles.** Bottles below 100% in storage or intakes go to a tank of the same gas with Auto-Refill on, power and gas in it. The tank fills them as they arrive, and full bottles go back to storage. Gas generators aren't used, because they refill bottles only on the server and a client can't see when a bottle in one is full. Stock blocks with bottle minimums get the fullest bottles first.

## Working with SHEPHERD

[SHEPHERD](https://steamcommunity.com/sharedfiles/filedetails/?id=3824156985) ([source](https://github.com/chrumck/SHEPHERD)) is an inventory script for a programmable block, built by chrumck. Stockpile Manager drew inspiration from a number of inventory manager scripts, one of them an unfinished early version of SHEPHERD. It runs on the server, so it keeps a grid sorted and stocked whenever the server has the grid loaded, even while you're away. Stockpile Manager is the window you work from while you play. The two are meant to be used together.

When a grid has a working SHEPHERD block (owned by you, or shared with your faction with `factionShare` on), Stockpile Manager steps back from the jobs SHEPHERD does, so the two never move the same items back and forth:

| Job | Left to SHEPHERD means |
|---|---|
| Sorting | SHEPHERD empties every cargo container, cockpit and connector into its tagged containers; Stockpile Manager stops sorting and draining intakes there |
| Docked ships | SHEPHERD unloads them; Stockpile Manager stops restocking them for their quotas (Unload docked ships still works when you press it) |
| Reactor and generator fuel | SHEPHERD fills them to `reactorFillPct` / `gasGenFillPct`; their minimums and maximums here are left alone |
| Refineries | SHEPHERD feeds them (`refineryFillPct`) and steers them for ingot quotas; refinery priority and refine by ingot need are left alone |
| Production output | SHEPHERD empties refineries and assemblers |
| Crafting its A quotas | Items with an A quota in SHEPHERD's Custom Data are left to it (Items view: "Left to SHEPHERD: 500MA in [Reserve]"); other quotas are still crafted here on the Main and Co-op assemblers |
| Bottles | SHEPHERD refills them every `bottleSweepMinutes` |

A job SHEPHERD's settings switch off (a fill % or bottle sweep of 0) stays Stockpile Manager's. The Overview shows the SHEPHERD block, what its `[SHEPHERD]` status screen says, and a row per job: double-click one to keep it for Stockpile Manager, or to leave it to SHEPHERD again. If the block is off, damaged or unpowered, or its status screen says HALTED (it stopped after repeated crashes), or that screen stops updating (no change for 90 seconds, or 3 seconds per SHEPHERD screen on a grid with many, and not in the first 3 minutes after it's first seen), Stockpile Manager does every job again until it's back. While it's learning, its jobs stay with it. Without a [SHEPHERD] status screen, Stockpile Manager can't tell when it stops, so it assumes it runs while its block is on; the Overview suggests adding one. The LCD Overview page shows SHEPHERD's state too.

Whatever you choose, while SHEPHERD runs: containers tagged `[SHEPHERD:...]` or `[SHEPHERD-RESERVE...]` are SHEPHERD's (Stockpile Manager never moves items into or out of them), and assemblers tagged `[SHEPHERD-Main]` or `[SHEPHERD-Coop]` only get SHEPHERD's orders. Blocks tagged `[SHEPHERD-Off]` are left to Stockpile Manager: give that tag to lockers you keep stocked here (Stock role or minimums), or SHEPHERD empties them. The Overview warns about each one, and about quotas both keep for the same item.

**Export setup to SHEPHERD** (double-click its row in the Overview) hands the grid's setup over in one go, after showing you what it will change:

- Storage blocks that accept categories get `[SHEPHERD:Ores,Ingots,...]`, with their priority as `:#N`. An accepted single item gets a category of its own in `[Categories]`, and accepted bottles a `Bottles` category. General storage containers get `[SHEPHERD:*]`.
- Stock blocks become reserve containers, `[SHEPHERD-RESERVE:name]`, with their limits as quota lines: a minimum only becomes an `M` line, a maximum only an `L` line, both the maximum.
- Manual blocks get `[SHEPHERD-Off]`.
- With Autocraft on, the grid's quotas go into `[Reserve:Quotas]` as A lines: `MA` (made up to the quota, never disassembled) for a quota; with Disassemble surplus on, `LA` (disassembled above, never made) for a maximum alone and `A` (both) for a quota equal to its maximum. SHEPHERD keeps one amount per line, so a quota with a higher maximum is only made up to the quota; the confirm box says so, and the Main and Co-op assemblers get `[SHEPHERD-Main]` and `[SHEPHERD-Coop]`.

Then it runs the block with `configReload` and leaves every job to SHEPHERD. Only the grid itself and blocks you own are changed, and blocks that already have a SHEPHERD tag are skipped. Refinery priority, block type limits and docked ships aren't exported.

**Shepherd speaks.** With the Chat setting on, Shepherd has a voice of its own while it minds a grid: a weathered hill shepherd, in slate gray. It speaks when it's found running on a grid, when its status screen shows a new problem, when its crafting meets a quota, and after an export. Stockpile Manager says a word when SHEPHERD stops and the jobs come back to it.

## Block roles

| Role | Auto for | Behaviour |
|---|---|---|
| Storage | Cargo containers | Stores items; a source for minimums and a destination for sorting |
| Intake | Connectors, collectors | Emptied into storage |
| Stock | (never Auto) | Holds only the items it has limits for, between min and max; everything else is moved out |
| Machine | Everything else | Left alone except for its own limits (and output draining) |
| Manual | (never Auto) | Never touched |

**Accepts** (Block settings): a storage block can accept categories (ore, ingots, components, ammo, tools, bottles, food, other) and single items. Once any storage block accepts something, items move to the best match: exact item, then category, then general storage (storage that accepts nothing in particular). Items a block doesn't accept are moved out of it.

**Limits** work on any block. A minimum brings the item in from intakes, production output, storage and other blocks' surplus. A maximum sends the excess to storage. On a machine, limits count its input inventory.

**Docked ships.** In Space Engineers a ship docked by connector shares the station's terminal, so it shows up as part of the station while docked, and the station's settings apply. Its Auto blocks count as machines, so its cargo isn't sorted into the station. To restock a docked ship, give its lockers the Stock role with minimums.

Quotas stay with the grid they were set on. Each mechanical group in a terminal system (the station itself, each docked ship) is handled on its own: its settings are looked up only among its own grids, and its quotas count only its own stock and queue only on its own assemblers. A docked ship's quotas keep working while it's docked, but they never drive the station's assemblers, and the station's quotas never use the ship's. A docked ship short of one of its quotas is first restocked from the station's storage (while the station's Automation is on, or during Sort now): the shortfall is moved into the ship's cargo containers, but never below the station's own quota for the item or a storage block's own minimum, and autocraft only queues what the station can't cover. Unloading leaves the ship's quota amount on board, so the two don't undo each other.

A docked ship has its own entry in the Grid list, under the grid it's docked to. Picking it shows only the ship's blocks and items, and its own settings, quotas and notes. The entry uses the ship's main grid as its key, so it's the same entry the ship has once it undocks, and settings made there carry over. Its type limits apply while it's docked too, and fill from anything connected, the station included. The other grid settings set there apply once it undocks.

## LCD pages

Overview, Ores, Ingots, Components, Ammo, Tools & bottles, All items, Containers (each storage block's fill, the screen's own ship or station only), Containers + docked (ships docked to it too, marked `>`), Stock limits, Quotas, Warnings, Log. A screen shows the ship or station its block is on (a screen on a docked ship shows the ship, not the station), or any other known grid. Pages are fitted to the screen's width and height in its font and size: the Containers page switches to one line per container when two don't fit, and a page longer than the screen shows a screenful at a time, turning every 6 seconds (`1/3` after the page name). When a page is first assigned, the screen is switched to text mode with a monospace font. After that, font and size are left as you set them.

## Where settings are stored

Everything is stored on your computer in the plugin's local storage, one pair of files per world (`SacrificialStockpileManager_<world>_Settings.xml` and `..._Grids.xml`, keyed by the world's name). None of your settings are written to Custom Data or block names, so other players don't see them. The one exception is the shared assembler mode (see Assembler modes), which is written to the Custom Data of assemblers you own, and which BaR Maid reads and writes as well. The other is Export setup to SHEPHERD, which only writes block names and a SHEPHERD block's Custom Data when you press it. LCD text is visible to everyone.

## Multiplayer

Everything runs on your client. Nothing is needed on the server.

- **Moving items** sends the same request as dragging an item in the terminal's inventory screen (`MyInventory.TransferByUser`). The server checks your access to both inventories. The server doesn't check conveyors for this request, so the plugin checks them itself, the same way a programmable block does, and only moves items between conveyor-connected inventories.
- **Queuing** sends the same request as clicking a blueprint in the production screen.
- **Pacing:** each grid gets at most 10 moves every 3 seconds, and the work is spread over frames, one grid at a time.
- **Server delay:** the server takes a moment to confirm a move. Moves still on their way are counted for up to 6 seconds, so they aren't sent twice. An autocraft item isn't queued again for 15 seconds.
- **Only while you're around.** Unlike a programmable block, the plugin only works while you're online and the grid is loaded near you. Grids out of range show their last known contents.
- **Other players with the plugin.** If two players manage the same grid, both move items.
- **SHEPHERD.** Finding a SHEPHERD block sends nothing: block names, Custom Data and screen text are already in your game. Export setup to SHEPHERD sends, once, the same requests as renaming blocks and editing Custom Data in the terminal, plus one Run of the programmable block (`configReload`); the server checks your access for each, and anyone who can open those blocks sees the new names and Custom Data.

## Switching between our plugins

If any of our other plugins are loaded too (such as BaR Maid, Fat Albert, OreScout or Script to Plugin), the top-left corner of the window has a dropdown with this plugin's name. Pick another plugin there to close this window and open that plugin's. Type `/tim` in chat to open whichever of these windows you used last. See [Shared/README.md](../../Shared/README.md).

## Reporting bugs

Open an issue on [GitHub](https://github.com/scotlandsyard/SpaceEngineersPlugins/issues) with the plugin's name in the title. Say what you did, what you expected and what happened instead, and attach the game log (`%AppData%\SpaceEngineers\SpaceEngineers_<date>.log`) and Pulsar's log (`%AppData%\Pulsar\Legacy\info.log`) if you can.

## Building

The project also builds `Shared/PluginSwitcher.cs` from the repo's `Shared` folder, so build from a full clone of the repo. Set `Bin64` in `Source/SacrificialStockpileManager.csproj` if Space Engineers isn't in the default Steam location. Then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/SacrificialStockpileManager.dll`.

The plugin uses its own assembly name (`SacrificialStockpileManager`), action ID (`SacrificialStockpileManager_OpenMenu`), chat command (`/ssm`), storage files (`SacrificialStockpileManager_*`) and GPS key line (`SSM:grid:`), so it can't clash with other plugins or mods.
