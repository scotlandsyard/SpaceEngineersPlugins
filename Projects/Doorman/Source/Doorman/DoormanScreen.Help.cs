using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace Doorman;

public partial class DoormanScreen
{
	private void CreateHelp()
	{
		MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, -0.3f), new Vector2(0.84f, 0.57f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		help.AppendText(HelpText);
		Add(help, _helpControls);
		AddButton(0.33f, 0.32f, "Back", () => ShowPage(_mainControls), _helpControls);
	}

	private const string HelpText =
		"WHAT IT DOES\n" +
		"Doorman closes doors that have been left open. Once a door is fully open, it waits the delay (3 seconds unless you change it) and then closes it, the same as switching it to closed in the terminal.\n\n" +
		"WHICH DOORS\n" +
		"Doors on grids you own, or your faction owns if you tick Faction grids too, and only doors you have access to. Doors, sliding doors, advanced doors and modded doors are in. Hangar doors are off unless you tick Hangar doors, for all grids or one grid. Doors that are off, unpowered, not working or part of a projection are left alone.\n\n" +
		"ONLY WHILE YOU'RE HERE\n" +
		"Doorman runs on your PC, so it only closes doors on grids loaded in your game. While you're offline or too far away for the grid to be loaded, open doors stay open. A programmable block script keeps working when you're away; Doorman needs no programmable block and costs the server no script time.\n\n" +
		"AIRLOCKS, TIMERS AND SCRIPTS\n" +
		"With Leave airlocks alone ticked, a door with \"airlock\" in its name, or in a group with \"airlock\" in its name, is never closed. If something opens a door again right after Doorman closed it (straight away, or twice within 10 seconds) while nobody is within 5 m of it, Doorman takes it for a timer or script and leaves that door alone and the list says so. Once the door has stayed shut for a minute, Doorman looks after it again. Players walking in and out never count: a door opened by someone standing near it is always closed as usual. Picking a setting for the door in the window also clears it.\n\n" +
		"NOT ON YOUR HEAD\n" +
		"If anyone is standing in the doorway, Doorman waits until they've gone.\n\n" +
		"THE WINDOW\n" +
		"Grid: All grids (defaults) sets the default delay and hangar setting. Pick a grid to give it its own delay (blank uses the default) and hangar setting, and to list only its doors. Use Defaults puts the grid back.\n" +
		"The list shows each door, its delay, and what it's doing now. Click a column header to sort, and use Find to shorten the list.\n" +
		"Selected door: Default follows its grid. Never: Doorman leaves it alone. Always: closed even if it's a hangar door or an airlock door. Delay gives that door its own delay; blank uses its grid's.\n\n" +
		"TWO PLAYERS\n" +
		"If two players on the same grid both run Doorman, whoever's PC sends first closes the door; the other request finds it already closed and does nothing.\n\n" +
		"TOOLBAR\n" +
		"In a cockpit or remote control, the toolbar has Doorman: door settings (opens this window) and Doorman: auto-close On/Off.\n\n" +
		"COMMANDS\n" +
		"/doorman opens this window. /doorman on and /doorman off switch it. /doorman status says what it's watching. /doorman delay 5 sets the default delay in seconds. /doorman chat off, quiet, normal or chatty sets how much it comments. /doorman name and a name changes what its chat lines show under; on its own it puts it back. /tim opens whichever of our plugin windows you used last, and the list at the top left switches to another of our plugins.\n\n" +
		"WHAT THE SERVER SEES\n" +
		"One request per door closed: the terminal's own Open switch set to closed, which the server checks for access and power as usual. At most five a second, and the same door gets at most three tries if the server doesn't close it. Doorman reads nothing from the server, and writes nothing to your grids: no Custom Data, no names. The window, the commands and the toolbar actions send nothing. A toolbar action saved in a cockpit's toolbar is stored with the ship, like any other toolbar slot.\n\n" +
		"FILES\n" +
		"The chat setting is in Doorman_Settings.txt and each world's settings in Doorman_<world>_Settings.txt, in %AppData%\\SpaceEngineers\\Storage.";
}
