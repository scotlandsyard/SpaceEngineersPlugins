namespace Tally;

public partial class TallyScreen
{
	private const string HelpText =
		"WHAT IT DOES\n" +
		"Tally adds up what something takes to build: the components, the ingots the assembler needs for them and the ore the refinery needs for those ingots. It reads the block, assembler and refinery definitions the game has loaded for this world, mods included, and can write the list onto datapads to hand round.\n\n" +
		"WHAT YOU CAN TALLY\n" +
		"- Projector: any projector with a projection loaded that you have access to (yours, or shared with you), nearest first. Projected sub grids count too.\n" +
		"- Blueprint (F10): a blueprint from your own local blueprint folder, the same files the F10 screen shows under Local. Cloud and workshop blueprints aren't read; save one locally first. Blocks from mods this world doesn't have are left out and counted in the summary.\n" +
		"- My grid: a grid you own (with what's joined to it by rotors, pistons and hinges, if you own that too or nobody does). This is what the ship cost.\n" +
		"Find shortens the list to names with the text you type. Refresh list looks again.\n\n" +
		"STILL NEEDED\n" +
		"Ticked, a projection counts only the blocks not built yet, plus what built blocks that aren't finished still lack, the same way the projector matches them (same place, same block). For your own grid it counts what unfinished or damaged blocks lack. Unticked, you get the full cost. A blueprint is always the full cost.\n\n" +
		"MINUS WHAT YOU HAVE\n" +
		"Minus what I carry takes off what's in your own inventory. Minus the grid's cargo also takes off what's in the projector's grid (or your grid) and anything docked to it, in blocks you have access to. It works down the chain: components you have aren't made, so their ingots aren't needed; ingots you have aren't refined, so their ore isn't needed. Have shows what was found and Still to get what's left.\n" +
		"The server keeps your own inventory up to date on your PC all the time, but a grid's inventories only while you have one of them open in the terminal. So for current cargo numbers, open the grid's inventory (K, Inventory) before you tally.\n\n" +
		"THE MATHS\n" +
		"Components: each block's components from its definition. Ingots: the assembler recipe for each component, divided by this world's assembler efficiency (the summary shows it, e.g. x3 or x6) unless the recipe ignores it. Ore: the refinery recipe for each ingot, at the refinery yield you type (in percent, as the refinery's terminal shows Effectiveness). Modded refineries differ, so type yours; a vanilla refinery is 100%, and each vanilla yield module adds about 9% (four modules: about 141%). Left empty, it uses the best refinery in this world without modules. Speed modules and the world's refinery speed don't change the amounts. Stone and scrap recipes are skipped: ore is always the ore of the same name. Amounts are rounded up. Anything without a recipe in this world is listed as not broken down.\n\n" +
		"THE TABLE\n" +
		"Components, Ingots and Ore switch the table. Click a column heading to sort by it. Ingots and ore are in kg.\n\n" +
		"DATAPAD\n" +
		"Write datapad tallies again (so it matches what's built and carried now) and writes the list onto a datapad in your own inventory (not a container): what was tallied and how, then the components, ingots and ore. It's named \"Tally: <name>\" (with #2 if you already carry one by that name). With something taken off, it lists only what's still to get, as \"A (of B)\".\n" +
		"Tally can't make datapads: craft one, or pick one up, and carry it. Auto (the Datapad list) uses an empty datapad, or the ones already holding this list; pick a datapad in the list to use that one (Tally asks before overwriting one with other text). A datapad holds 1000 characters, so a long list uses more than one, named (1/2), (2/2); if you don't carry enough, Tally asks before writing a shortened list. A few seconds after writing, it checks the datapads show the new name and says so.\n" +
		"Copy puts the same text on the clipboard.\n\n" +
		"COMMANDS\n" +
		"/tally opens this window. /tally pad writes the last list to datapads. /tally help opens this page. /tally chat off, quiet, normal or chatty sets how much Tally comments, and /tally name changes the name its chat lines show under. /tim opens whichever of our plugin windows you used last, and the list at the top left switches to another of our plugins.\n\n" +
		"WHAT THE SERVER SEES\n" +
		"Tallying sends nothing: projections, grids, inventories and definitions are already on your PC, and blueprints are your own files. Write datapad sends one datapad edit request per datapad, the same request the game's own datapad screen sends when you press OK, and only for datapads in your own inventory; the server then saves the new name and text with your inventory.\n\n" +
		"FILES\n" +
		"In %AppData%\\SpaceEngineers\\Storage\\Tally: Tally_Settings.txt (chat setting and the window's choices). Blueprint files are only ever read.";
}
