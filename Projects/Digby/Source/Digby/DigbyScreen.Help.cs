namespace Digby;

public partial class DigbyScreen
{
	private const string HelpText =
		"WHAT IT DOES\n" +
		"Digby plans a tunnel from where you are (or any GPS) to a deposit a given depth under the surface somewhere else on the same planet. It reads the planet and the real terrain height from the game, gives the angles and the route, puts markers on the start, the turns and the end, and can write the whole plan onto a datapad to hand round.\n\n" +
		"THE PLAN PAGE\n" +
		"From: where the tunnel starts: Here, a GPS from your list, a library point, or a GPS pasted in the box under it. Target: roughly the spot on the surface above the deposit. Depth: how far under the surface at the target the deposit is. Calculate works it out:\n" +
		"- Straight tunnel: its length and its angle below the horizon. Aim at the Dig target marker.\n" +
		"- Down then across: dig straight down to the target's altitude (as a cockpit shows it, sea level), then across. The Dig turn marker is where you turn.\n" +
		"- Notes when the straight line sags deeper in the middle (planets are round), goes below the lowest terrain, or is very long.\n" +
		"The line at the top says which planet you're on (or that you're in space) and how deep you are now.\n\n" +
		"IN SPACE\n" +
		"With both points in space (an asteroid), the tunnel goes straight from From to the target, so put the target GPS on the deposit; the depth is ignored and there's no horizon or down-then-across. Mixing a planet point with a space point isn't allowed.\n\n" +
		"THE RIG LINE\n" +
		"For extending a grid (a drill rig) toward the deposit without hinges or rotors. P1 and P2 are two GPS on your grid along its forward axis; the Up point is a GPS straight above P1 on the grid (leave it on (none) for a level grid on a planet; it's needed in space). Tick Small grid for 0.5 m blocks. The plan then adds: the turn and pitch against the true horizon, the same against the grid itself, the tunnel in whole blocks forward, sideways and down with the step pattern per 10 blocks, and the route as straight legs along the grid with a Dig rig turn marker where each leg ends. The rig line stays with the plan until you change it, since it belongs to the grid.\n\n" +
		"MARKERS\n" +
		"Dig start, Dig target and the turn markers are local by default: they show on your HUD and in your GPS list but never leave your PC, and they're gone when you leave the world. Tick As server GPS to make them normal GPS entries instead. A new plan replaces the old markers. Clear markers removes them.\n\n" +
		"DATAPAD\n" +
		"Write datapad calculates the plan and writes it onto a datapad in your own inventory (not a container): what the plan is, which planet it's on or that it's in space, the start of the dig, a GPS line for every marker, then the whole plan. The datapad is named \"Dig: <plan>\" (with #2 if you already carry one by that name). Anyone who opens it can press the game's Create GPS button, which adds the start of the dig as a GPS; the other markers are plain GPS lines they can copy.\n" +
		"Digby can't make datapads: craft one, or pick one up, and carry it. Auto (the Datapad list) uses an empty datapad, or the ones already holding this plan; pick a datapad in the list to use that one (Digby asks before overwriting one with other text). A datapad holds 1000 characters, so a long plan uses more than one, named (1/2), (2/2); if you don't carry enough, Digby asks before writing a shortened plan.\n" +
		"A few seconds after writing, Digby checks the datapads show the new name and says so.\n" +
		"You can rename and edit datapads yourself too: open your inventory and right-click a datapad.\n\n" +
		"LIBRARY\n" +
		"Save to library moves the plan's markers into Digby's library for this world (off your GPS list), so they survive leaving the world. The Library page lists them: Use as From or Use as Target puts a point in the plan, Show on HUD adds it as a local marker, Hide markers takes those off again, Remove deletes a point. Library points also appear in the From, Target and rig lists.\n" +
		"If another plugin's marker library has points for this world (OreScout's, for example), pick it in the Library list to see them, read only, and Copy to Digby or Copy all to bring them over.\n\n" +
		"PLANET NAMES\n" +
		"The game only knows a planet's type (EarthLike, Mars...). Servers often call their planets something else: stand on the planet and type /digby planet name <name> to use that name in this world. If you use Fat Albert and named the planet there, Digby uses that name too.\n\n" +
		"COMMANDS\n" +
		"/digby opens this window. /digby dig [from] <target> [depth] plans from chat: points are a GPS, \"a name\" from your GPS list or the library, or here; add server, nomarker or nostart to change the markers. /digby dig on its own plans your last plan again. /digby rig <P1> <P2> [up] [small|large] sets the rig line; /digby rig off removes it. /digby pad writes the last plan to a datapad. /digby clear removes the markers. /digby planet says where you are; /digby planet name <name> names the planet. /digby library and /digby help open those pages. /digby chat off, quiet, normal or chatty sets how much Digby comments, and /digby name changes the name its chat lines show under. /tim opens whichever of our plugin windows you used last, and the list at the top left switches to another of our plugins.\n\n" +
		"WHAT THE SERVER SEES\n" +
		"Planning, the planet line, the rig line, the library and local markers send nothing: they use what your game already knows. As server GPS sends the normal GPS add, change and remove requests, the same as making GPS by hand. Write datapad sends one datapad edit request per datapad, the same request the game's own datapad screen sends when you press OK, and only for datapads in your own inventory; the server then saves the new name and text with your inventory.\n\n" +
		"FILES\n" +
		"In %AppData%\\SpaceEngineers\\Storage\\Digby: Digby_Settings.txt (chat setting), and per world Digby_<world>_plan.txt (the last plan, with named points saved as GPS so it still works if the GPS is renamed), Digby_<world>_library.xml and Digby_<world>_planets.txt. Other plugins' files are only ever read.";
}
