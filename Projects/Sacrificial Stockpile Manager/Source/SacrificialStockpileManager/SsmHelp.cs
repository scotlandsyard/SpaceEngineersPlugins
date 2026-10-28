using Sandbox.Graphics.GUI;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>The text of the Help view.</summary>
internal static class SsmHelp
{
	private static readonly string[] Lines =
	{
		"# What it does",
		"Sacrificial Stockpile Manager keeps track of the inventory on all your ships and stations and keeps them stocked. It sorts items into the containers you choose, keeps blocks between a minimum and maximum of each item, and queues items in your assemblers to keep a quota. Everything is set up in this window; your settings are stored on your computer, not in Custom Data. Only the assembler mode shared with our other plugins is written to the Custom Data of assemblers you own (and Export setup to SHEPHERD writes block names and a SHEPHERD block's Custom Data, when you press it).",
		"It runs in your own game. It works on a grid while that grid is loaded near you, and remembers what each grid held when it was last in range.",
		"",
		"# Opening this window",
		"- Type /ssm in chat, or",
		"- Put any block with an inventory or a screen on a toolbar and pick 'Stockpile Manager'. The window opens on that block.",
		"",
		"# Grids",
		"The Grid list shows every ship and station you own a part of: loaded ones first, then the ones you've seen before with how long ago. A grid that isn't loaded shows what it held then. You can still change its settings; they apply when it's back in range.",
		"Ships docked by connector are listed under the grid they're docked to, marked '> ... (docked)'. Pick one to see only that ship: its blocks, its items, and its own settings and quotas. While it's docked, items move by the settings of the grid it's docked to; its quotas and autocraft still run on its own assemblers, and its other settings apply once it undocks.",
		"- All grids (the first view) lists every grid at once: when it was last synced (Live if it's loaded now), distance, storage used, storage size, fill and how many kinds of item it holds. The line under the table adds them up. Double-click a grid, or select it and press Open grid, to go to its Overview.",
		"- Storage means blocks acting as Storage or Stock (cargo containers unless you change their role), not counting docked ships. Machines, cockpits and tools are left out so they don't inflate the totals. The Blocks view shows every block's used and total size.",
		"- GPS marker adds a marker where the grid was last seen (it moves the existing one if you press it again).",
		"- Remove from list is for grids that were deleted or destroyed. It removes the grid and its settings (block settings, quotas, displays). A grid that is loaded can't be removed. If a removed grid still exists, it comes back with default settings the next time it's in range.",
		"",
		"# Automation (Overview)",
		"Automation is off on every grid until you switch it on. While it's off, nothing is moved; the window only shows what is where and what would need doing. Double-click a setting in the Overview to switch it:",
		"- Automation: apply minimums, maximums, sorting and intake draining.",
		"- Autocraft: queue the shortfall of quotas in the assemblers.",
		"- Clean production blocks (on unless you switch it off; needs Automation): move what refineries and assemblers made into storage, and take anything out of an assembler's input that its queue doesn't need, like steel plates a modded blueprint pulled in, or leftovers from a cancelled item. That stops a full input from blocking all assembly. For an assembler that's disassembling, the parts that came out are moved (the game puts them in its input), and the items waiting to be taken apart are left alone. Cooperating assemblers keep whatever any queue on the grid needs, and an item you set a limit for on the assembler is left to that limit.",
		"- Survival kits autocraft: let autocraft use survival kits too.",
		"- Disassemble surplus: disassemble whatever is above an item's maximum (see Quotas below).",
		"- Keep bottles filled: take oxygen and hydrogen bottles that aren't full to a gas tank to refill, and put them back once full (needs Automation).",
		"- Blocks shared with me: off by default, so only blocks you own are touched (see 'Whose blocks' below).",
		"- Chat (the list at the top right of the window): how often Stockpile Manager comments in chat, in character: Off, Quiet (only when a grid's storage fills up), Normal (the default) or Chatty. It takes a turn in the chat of our plugins: at most one line per cycle (2 minutes on Chatty, 5 on Normal, 15 on Quiet), and the same kind of line at most every 10 minutes. Only you see the lines. It's the same in every world.",
		"- Chat name (all grids): the name its lines show under. Type /ssm name <new name> in chat to change it (at most 24 characters), or /ssm name to go back to Stockpile Manager.",
		"Sort now (Overview button, the 'Stockpile Manager: Sort now' toolbar action, or /ssm sort in chat) does everything Automation would do, once, until there's nothing left to move, even while Automation is off. Handy on a shared server where you'd rather not leave it running.",
		"Unload docked ships (button, toolbar action, or /ssm unload) moves the cargo of your ships docked to this grid into its storage: their cargo containers, connectors and collectors. Stock and Manual blocks keep their contents, a block keeps its minimums, and a ship keeps its own quota amounts: only what it holds above a quota is unloaded.",
		"",
		"# Whose blocks",
		"The game lets you use blocks shared with your faction, blocks shared with everyone, and blocks nobody owns. The plugin only manages blocks you own: it never takes from, puts into or queues on anyone else's block, and never writes to their screens or Custom Data, and never switches them on or off. They show as 'not yours'. Switch 'Blocks shared with me' on in a grid's Overview to manage those too, for example on a faction station everyone builds on. Either way the server checks every move against your access, so the plugin can never do more than you could by hand.",
		"",
		"# Reach",
		"The server only accepts item moves, queue changes and screen text from a player within 15 m of the grid (or any grid docked to it) or linked to it by antenna; for anything else it refuses every request and logs it as a failed validation for the admin. So when you're out of reach of a ship or station, the plugin pauses everything that sends requests for it, the Overview says 'out of reach: automation paused', and the Items view shows 'Paused: out of reach'. It resumes by itself when you're back in reach. It doesn't apply in single player or on the host of a game. The log shows 'Moved ...' once the server has confirmed the move.",
		"",
		"# Block roles",
		"Every block with an inventory has a role. Auto picks one from the block type.",
		"- Storage (cargo containers): items are stored here and taken from here.",
		"- Intake (connectors, collectors): emptied into storage.",
		"- Stock: a locker or loadout. It holds only the items it has limits for, kept between their minimum and maximum. Everything else is moved out.",
		"- Machine (everything else when Auto: reactors, generators, refineries, assemblers, cockpits, turrets, tools...): left alone, except for its own limits.",
		"- Manual: player only. The plugin never takes from it or puts into it.",
		"Docked ships: a ship docked by connector shows up as part of the station it's docked to, and the station's settings apply. Its Auto blocks count as machines, so its cargo isn't sorted into the station. To restock a docked ship, give its lockers the Stock role with minimums: they fill from the station's storage. Quotas stay with the grid they were set on. A docked ship short of a quota is restocked from the station's storage first (needs the station's Automation, or Sort now), never below the station's own quota or a block's minimum, and autocraft only makes what the station can't cover; unloading leaves a ship its quota amount. A docked ship's quotas count only the ship's own stock and use only its own assemblers, and the station's quotas never use the ship's.",
		"",
		"# Sorting (Accepts)",
		"In Block settings, tick the categories a storage block accepts: ore, ingots, components, ammo, tools, bottles, food or other. You can also accept single items: pick the item and press Accept item.",
		"Sorting starts once at least one storage block accepts something. Items then move into the block that accepts them best: an exact item beats its category, which beats general storage (a storage block that accepts nothing in particular). Items a block doesn't accept are moved out of it.",
		"Priority (Block settings, 0-9): among storage blocks that accept an item equally well, the one with the higher number fills first. It decides where new items go; it doesn't drag items already in a good place to another block.",
		"",
		"# Limits",
		"In Block settings, pick an item (select a row, or choose it in the list below the table), type an amount and press Set minimum or Set maximum. A blank amount clears it. Amounts can be written as 500, 2,500, 2.5k or 1.2M.",
		"Find: type part of a name (or a category, like 'ingot') in the Find box. The item list and the table only show matching items, and the first match is picked.",
		"- Minimum: the plugin brings the item in from storage, intakes, production output and other blocks' surplus, until the block holds that much.",
		"- Maximum: anything above it is moved to storage.",
		"This works on any block: keep uranium in reactors, ice in generators, ammo in turrets, or ore in a refinery. Limits on a machine count its input inventory.",
		"Block type limits set a minimum and maximum for every block of a type at once: all reactors, O2/H2 generators, turrets and guns, cockpits and seats, ship tools, gas tanks, refineries or assemblers on the grid. A docked ship has its own type limits (pick the ship in the Grid list); while it's docked they fill from the station too. A block's own limit for the same item wins. Blocks that can't hold the item, like a turret of another calibre, are skipped quietly.",
		"",
		"# Quotas and autocraft (Items & quotas)",
		"The Items view lists everything on the grid. Select an item, type an amount and press Set quota. With Autocraft on, the plugin counts what the grid holds plus what's already queued on any of its assemblers, and queues the difference as one order on a Main assembler. It only uses assemblers you've made Main or Co-op that are on, powered and in assembly mode.",
		"Assembler modes (shared with our other plugins, so you set them once for all of them): every assembler starts out Manual, which means no plugin queues on it or changes it. To let autocraft use an assembler, make it Main or Co-op: pick the mode in Production details, or select it in the Production view and press Assembler mode (it steps Manual > Main > Co-op). Main takes the orders. Co-op is kept in the game's cooperative mode and helps a Main assembler with its queue. If you only made assemblers Co-op, one of them is made Main the first time something is queued. Setting the Manual role in Block settings also sets the mode to Manual. The mode only decides who gets orders: Clean production blocks still empties a Manual assembler's output. The mode is stored in the assembler's Custom Data as [TIM] Assembler=Main, Coop or Manual.",
		"With BaR Maid (our repair plugin) loaded too, its orders go first. Quotas are then only queued while this ship's or station's Main and Co-op assemblers are all idle, and about a minute of work at a time. Whatever BaR Maid queues waits at most for that batch, and no more quota work is added until its queue is done. Big quotas still fill up, a batch after another.",
		"Set maximum sets the most you want of an item. With Disassemble surplus on, anything above it is disassembled: the plugin picks one idle Main or Co-op assembler (on, powered, nothing queued, not cooperating; normally the Main one), switches it to disassembly, queues the surplus, and switches it back to assembly once that queue is done. Its assembly queue is kept aside meanwhile, as the game always does. The assembler pulls the items itself through the conveyors, from any connected inventory. If the items are used up before they're disassembled, their entries are taken out of the queue.",
		"A maximum below the quota isn't allowed: setting one moves the other to match.",
		"",
		"# Production details",
		"Double-click an assembler or refinery in the Production view (or select it and press Details). For an assembler you see the queue (the first item shows how far along it is), then every material the whole queue needs: how much, how much the grid has, and how much is missing. 'Blocks the current item' means the assembler is stuck on it right now; 'Runs out partway' means the queue will stop later. Amounts allow for the world's assembler efficiency. Remove from queue takes the selected queue entry out.",
		"For a refinery you see its input in the order it refines it, and its output.",
		"",
		"# Refine by ingot need",
		"With Autocraft on, a quota for something only refineries make (ingots, gravel) works through the refineries: while it's short, the ore that makes it goes to the top of the refinery priority, and when the quota is met it drops back to where you put it. Your own list is never changed.",
		"- Several short items: the most short one (as a share of its quota) comes first.",
		"- An item several ores can make: all of them move up, the one that gives the most per unit of ore first.",
		"- An ore that makes several short items is listed once, at the place of the most short one.",
		"- Then your own list follows, in your order.",
		"The Refinery priority view shows the order in use, with a Why column (which short items, or 'Your list'). The Items view tells you what is being done, like 'Short 5,000: Platinum Ore moved to the top of refinery priority', or why it can't be (no such ore here, Automation off). It needs Automation, like your own priorities, and only steers the grid's own refineries for its own quotas, not a docked ship's.",
		"",
		"# Refinery priority",
		"The Refinery priority view lists every ore that can be refined. Select an ore and press Raise priority (or double-click it) to put it on the list; raise and lower to order it. With Automation on, each refinery works on the highest-priority ore it can get: the plugin moves that ore to the front of the refinery's input (refineries refine top to bottom), and if the refinery doesn't have any, brings some in from storage, sending back its lowest-priority ore if it's full. Ores without a priority are refined as they arrive.",
		"",
		"# Keeping bottles filled",
		"With Keep bottles filled and Automation on, oxygen and hydrogen bottles in storage that aren't full are taken to a tank of the same gas that has Auto-Refill switched on, power and gas in it. The tank fills them straight away, and full bottles go back to storage. Gas generators aren't used: they refill bottles on the server only, so your game can't see when a bottle in one is full. When a bottle can't be served, the Overview warns about it. Stock blocks with bottle minimums get the fullest bottles first.",
		"",
		"# Displays (LCD)",
		"Select a screen on the grid, pick a page and what it shows, then press Show page. Pages: Overview, Ores, Ingots, Components, Ammo, Tools & bottles, All items, Containers (each storage block's fill on the screen's own ship or station), Containers + docked (with the ships docked to it), Stock limits, Quotas, Warnings and Log. A screen on a docked ship shows that ship, not the station. A screen can show another grid's last known stock too. When a page is longer than the screen, it shows a screenful at a time and turns the page every 6 seconds (1/3 after the page name). The screen is switched to text mode with a monospace font once; after that you can change the font size as you like.",
		"",
		"# Working with SHEPHERD",
		"SHEPHERD is an inventory script for a programmable block, built by chrumck. Stockpile Manager drew inspiration from a number of inventory manager scripts, one of them an unfinished early version of SHEPHERD. It runs on the server, so it keeps a grid sorted and stocked whenever the server has the grid loaded, even while you're away. Stockpile Manager is the window you work from while you play. The two are meant to be used together. What each side relies on from the other is in INTEGRATION.md in the plugin's repository.",
		"Stockpile Manager finds SHEPHERD by your programmable blocks that carry its settings, and by your [SHEPHERD] status screens. While SHEPHERD is doing its jobs on a grid, Stockpile Manager steps back from them, so the two never move the same items back and forth:",
		"- Sorting: SHEPHERD empties every cargo container, cockpit and connector into its tagged containers; Stockpile Manager stops sorting and draining intakes there.",
		"- Docked ships: SHEPHERD unloads them; Stockpile Manager stops restocking them for their quotas (Unload docked ships still works when you press it).",
		"- Reactor and generator fuel: SHEPHERD fills them to reactorFillPct / gasGenFillPct; their minimums and maximums here are left alone.",
		"- Refineries: SHEPHERD feeds them (refineryFillPct) and refines for its ingot quotas; refinery priority and refine by ingot need are left alone.",
		"- Production output: SHEPHERD empties refineries and assemblers.",
		"- Crafting its A quotas: Items with an A quota in SHEPHERD's Custom Data are left to it (Items view: 'Left to SHEPHERD: 500MA in [Reserve]'); other quotas are still crafted here on the Main and Co-op assemblers. With refineryFillPct=0, ingot quotas stay with Stockpile Manager's refine by ingot need.",
		"- Bottles: SHEPHERD refills them every bottleSweepMinutes.",
		"A job SHEPHERD's settings switch off (a fill % or bottle sweep of 0) stays Stockpile Manager's. Settings are read as SHEPHERD reads them: key names are case-sensitive, a ; comment after a value is ignored, and a value that doesn't parse or is out of range counts as SHEPHERD's default. The Overview shows SHEPHERD's state and what its status screen says, with a row per job: double-click one to keep it for Stockpile Manager, or to leave it to SHEPHERD again. The LCD Overview page shows SHEPHERD's state too.",
		"When SHEPHERD counts as running. While any of your [SHEPHERD] status screens on the grid keeps changing (its clock moves on every redraw) and says working, idle or learning, or standing by (another SHEPHERD block does the work). Stockpile Manager takes every job back when the screens say HALTED (stopped after repeated crashes), when they stop changing (90 seconds, or 3 seconds per SHEPHERD screen on a grid with many; never in the first 3 minutes after a screen is first seen), or when your SHEPHERD blocks are all off, damaged or unpowered. It hands them back by itself as soon as a screen moves again with a running word. SHEPHERD only redraws the first [SHEPHERD] screen, so one moving screen is enough. Without any [SHEPHERD] screen, Stockpile Manager can't tell when it stops, so it assumes it runs while one of its blocks is on; the Overview suggests adding one. Another player's SHEPHERD only manages its owner's blocks, so it doesn't count.",
		"Whatever you choose, while SHEPHERD runs: containers tagged [SHEPHERD:...] or [SHEPHERD-RESERVE...] are SHEPHERD's (Stockpile Manager never moves items into or out of them), and assemblers tagged [SHEPHERD-Main] or [SHEPHERD-Coop] only get SHEPHERD's orders. Blocks tagged [SHEPHERD-Off] are left to Stockpile Manager: give that tag to lockers you keep stocked here (Stock role or minimums), or SHEPHERD empties them. The Overview warns about each one, and about quotas both keep for the same item. Limits you set on weapons and gas tanks stay Stockpile Manager's: SHEPHERD never manages weapons and uses tanks only to refill bottles.",
		"Export setup to SHEPHERD (double-click its row in the Overview) hands the grid's setup over in one go, after showing you what it will change:",
		"- Storage blocks that accept categories get [SHEPHERD:Ores,Ingots,...], with their priority as :#N (higher fills first in both). General storage containers get [SHEPHERD:*]. Accepted bottles get a Bottles category.",
		"- An accepted single item gets a category of its own in [Categories], named with its type (IronIngot). SHEPHERD then sends that item only to its own containers (then the catch-all), never to its usual category's containers; the confirm box says so.",
		"- Stock blocks become reserve containers, [SHEPHERD-RESERVE:name], with their limits as quota lines: a minimum only becomes an M line, a maximum only an L line, both the maximum. Stockpile Manager only caps an item that has just a maximum; SHEPHERD's L also pulls it in up to that amount. And from SHEPHERD 1.0.29, what an M block holds above its minimum can go to another reserve short of the same item, where Stockpile Manager would leave it. The confirm box says when either applies.",
		"- Manual blocks get [SHEPHERD-Off].",
		"- With Autocraft on, the grid's quotas go into [Reserve:Quotas] as A lines: MA (made up to the quota, never disassembled) for a quota; with Disassemble surplus on, LA (disassembled above, never made) for a maximum alone and A (both) for a quota equal to its maximum. Ingots never get LA (they're never disassembled). SHEPHERD keeps one amount per line, so a quota with a higher maximum is only made up to the quota. SHEPHERD adds up its pools, so a quota line is lowered by what the stock blocks and its other pools already keep of that item. With refineryFillPct=0, ingot quotas stay with Stockpile Manager. The confirm box lists each of these. The Main and Co-op assemblers get [SHEPHERD-Main] and [SHEPHERD-Coop].",
		"- Category and pool names are letters and digits only, since brackets, commas and a trailing :#N mean something in a tag.",
		"Then it runs the block with configReload and leaves every job to SHEPHERD. With SHEPHERD older than 1.0.21, it checks the [Categories] lines again a few seconds later and puts back any that a save of SHEPHERD's own dropped. Only the grid itself and blocks you own are changed, and blocks that already have a SHEPHERD tag are skipped. Refinery priority, block type limits and docked ships aren't exported.",
		"Shepherd (SSM) speaks. With the Chat setting on, Stockpile Manager gives SHEPHERD a voice of its own while it minds a grid: a weathered hill shepherd, in slate gray, under the name Shepherd (SSM) so it isn't taken for the script itself. It speaks when it's found running on a grid, when its status screen shows a new problem, when its crafting meets a quota, and after an export. It keeps quiet while SHEPHERD is learning (its screen then previews what it will save). Stockpile Manager says a word when SHEPHERD stops and the jobs come back to it.",
		"Finding SHEPHERD sends nothing to the server: Stockpile Manager only reads block names, the block's Custom Data and the status screens' text, which your game already has.",
		"",
		"# Good to know",
		"- Moving items uses the same request as dragging them in the terminal, and queuing uses the same request as clicking a blueprint, so the server checks your access. Items only move between inventories connected by conveyors, just like a programmable block.",
		"- At most 10 moves are sent every 3 seconds per grid, so large backlogs take a little while.",
		"- In multiplayer the server takes a moment to confirm each move. The plugin allows for moves still on their way, so it doesn't send them twice.",
		"- Only while you're around: unlike a programmable block, the plugin works only while you're online and the grid is loaded.",
		"- Settings are stored on your computer, per world. Other players don't see them. LCD text is visible to everyone.",
		"- If two players with the plugin manage the same grid, both will move items."
	};

	public static void Write(MyGuiControlMultilineText text)
	{
		Vector4 heading = Color.White.ToVector4();
		Vector4 body = new Color(200, 215, 230).ToVector4();
		foreach (string line in Lines)
		{
			if (line.StartsWith("# "))
			{
				text.AppendText(line.Substring(2), "White", 0.85f, heading);
			}
			else
			{
				text.AppendText(line, "Blue", 0.75f, body);
			}
			text.AppendLine();
		}
	}
}
