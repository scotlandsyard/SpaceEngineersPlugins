using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using VRage;
using VRage.Utils;

namespace Tally;

/// <summary>A bill as plain text: for the datapad pages, the clipboard and chat.</summary>
internal static class Text
{
	/// <summary>Whole numbers, rounded up so the list never comes up short.</summary>
	public static string Amount(double amount)
	{
		return Math.Ceiling(amount - 1e-6).ToString("N0", CultureInfo.InvariantCulture);
	}

	public static string Have(double amount)
	{
		return Math.Floor(amount + 1e-6).ToString("N0", CultureInfo.InvariantCulture);
	}

	public static string Percent(double ratio)
	{
		return (ratio * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
	}

	public static string Multiplier(float value)
	{
		return "x" + value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	public static string What(Bill bill)
	{
		switch (bill.SourceKind)
		{
		case Source.Projector:
			return "projection";
		case Source.Grid:
			return "grid";
		default:
			return "blueprint";
		}
	}

	/// <summary>What was counted, in one line.</summary>
	public static string Summary(Bill bill)
	{
		string line = $"{bill.Mode} of {What(bill)} \"{bill.Name}\": {bill.Blocks:N0} blocks";
		if (bill.Mode == "Still needed")
		{
			line += $", {bill.Done:N0} of them finished";
		}
		if (bill.Unknown > 0)
		{
			line += $", {bill.Unknown:N0} left out (not in this world's mods)";
		}
		return line + ".";
	}

	public static string Settings(Bill bill)
	{
		return $"Assembler efficiency {Multiplier(bill.Assembler)}. Ore at {Percent(bill.Yield)} refinery yield" + (bill.YieldTyped ? "." : " (this world's best refinery, no yield modules).");
	}

	public static List<string> Lines(Bill bill)
	{
		List<string> lines = new List<string>
		{
			"TALLY: " + bill.Name,
			Summary(bill),
			Settings(bill),
			"Made " + bill.Made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " with Tally."
		};
		if (bill.Subtracted)
		{
			lines.Add($"\"A (of B)\": A still to get of the B needed, after what's in {bill.HaveFrom}. Items you have enough of are left out.");
		}
		Section(lines, "COMPONENTS", bill.Components, bill.Subtracted);
		Section(lines, "INGOTS (kg)", bill.Ingots, bill.Subtracted);
		Section(lines, "ORE (kg)", bill.Ores, bill.Subtracted);
		if (bill.NoRecipe.Count > 0)
		{
			lines.Add("");
			lines.Add("NO RECIPE IN THIS WORLD (not broken down)");
			lines.AddRange(bill.NoRecipe.Select(l => $"{l.Name}: {Amount(l.Needed)}"));
		}
		return lines;
	}

	private static void Section(List<string> lines, string title, List<Line> items, bool subtracted)
	{
		lines.Add("");
		lines.Add(title);
		int listed = 0;
		foreach (Line line in items)
		{
			if (!subtracted)
			{
				lines.Add($"{line.Name}: {Amount(line.Needed)}");
				listed++;
			}
			else if (Math.Ceiling(line.Short - 1e-6) > 0)
			{
				lines.Add($"{line.Name}: {Amount(line.Short)} (of {Amount(line.Needed)})");
				listed++;
			}
		}
		if (listed == 0)
		{
			lines.Add(items.Count == 0 ? "(none)" : "(you have all of it)");
		}
	}

	/// <summary>Puts text on the clipboard the way the game does it: on a short-lived STA thread.</summary>
	public static bool Copy(string text)
	{
		bool done = false;
		Thread thread = new Thread(() =>
		{
			try
			{
				MyVRage.Platform.System.Clipboard = text;
				done = true;
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Tally] Clipboard: {ex.Message}");
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join(2000);
		return done;
	}
}
