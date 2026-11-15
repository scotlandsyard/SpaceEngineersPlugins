using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;

namespace SacrificialStockpileManager;

/// <summary>
/// Every on-screen (HUD) notification the plugin shows. A notification is one line in the middle of the screen, so
/// each is kept to <see cref="Max"/> characters: grid names are cut to fit, and the detail goes to the grid's Log and
/// the window's status line instead. Tested in Proving Ground through <see cref="Samples"/> and <see cref="Recent"/>.
/// </summary>
internal static class Notices
{
	/// <summary>Longest notification, "Stockpile Manager: " included.</summary>
	public const int Max = 64;

	private const string Prefix = "Stockpile Manager: ";

	/// <summary>The last notifications shown, newest last (for the tests).</summary>
	public static readonly List<string> Recent = new List<string>();

	public static string NotYourGrid() => Prefix + "this grid isn't one of yours";

	public static string NoShipDocked() => Prefix + "no ship of yours is docked here";

	public static string Unloading(string grid) => Named("unloading docked ships into ", grid);

	public static string Sorting(string grid) => Named("sorting ", grid);

	/// <summary>SHEPHERD ran unloadShip on its leading block.</summary>
	public static string ShepherdUnloading() => Prefix + "SHEPHERD is unloading the docked ships";

	/// <summary>SHEPHERD ran on every block, as no [SHEPHERD] panel says which one leads.</summary>
	public static string ShepherdRanAll(int blocks) => Prefix + $"ran unloadShip on {blocks} SHEPHERD blocks";

	/// <summary>The leading SHEPHERD block couldn't be found; the reason is in the log.</summary>
	public static string ShepherdNotRun() => Prefix + "not unloaded, see the Log view";

	/// <summary>A notification as the window's status line: without "Stockpile Manager: ", capitalised, pointing to the Log view.</summary>
	public static string ForWindow(string notice)
	{
		string text = notice.StartsWith(Prefix) ? notice.Substring(Prefix.Length) : notice;
		text = text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
		return text.Contains("Log view") ? text + "." : text + ". Details in the Log view.";
	}

	/// <summary>Every notification with the longest grid name and block count, for the length test.</summary>
	public static List<string> Samples()
	{
		string longName = new string('W', 80);
		return new List<string> { NotYourGrid(), NoShipDocked(), Unloading(longName), Sorting(longName), ShepherdUnloading(), ShepherdRanAll(9999), ShepherdNotRun() };
	}

	/// <summary>Shows a notification. Anything over the limit is cut (and logged whole), though the builders above never make one.</summary>
	public static void Show(string text, int milliseconds, string font = MyFontEnum.White)
	{
		if (text.Length > Max)
		{
			VRage.Utils.MyLog.Default.WriteLineAndConsole($"[SSM] Notification over {Max} characters: {text}");
			text = text.Substring(0, Max - 3) + "...";
		}
		Recent.Add(text);
		if (Recent.Count > 20)
		{
			Recent.RemoveAt(0);
		}
		MyAPIGateway.Utilities.ShowNotification(text, milliseconds, font);
	}

	/// <summary>The prefix, the text and a grid name cut so the whole line fits.</summary>
	private static string Named(string text, string grid)
	{
		string name = (grid ?? "").Trim();
		int room = Max - Prefix.Length - text.Length;
		if (name.Length > room)
		{
			name = name.Substring(0, room - 3).TrimEnd() + "...";
		}
		return Prefix + text + name;
	}
}
