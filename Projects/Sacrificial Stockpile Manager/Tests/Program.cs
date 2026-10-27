using System;
using System.IO;
using System.Linq;

namespace SacrificialStockpileManager;

/// <summary>
/// Stockpile Manager's side of the contract test with SHEPHERD: its parsers against SHEPHERD's sample files (status
/// panels for each health word, the default [Settings]), plus the reading rules in SHEPHERD's INTEGRATION.md, Part A.
/// </summary>
internal static class Program
{
	private static int s_failed;

	private static int s_passed;

	private static int Main()
	{
		string fixtures = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures");
		Panels(fixtures);
		Settings(fixtures);
		ReadingRules();
		Quotas();
		Console.WriteLine();
		Console.WriteLine($"{s_passed} passed, {s_failed} failed");
		return s_failed == 0 ? 0 : 1;
	}

	private static void Check(bool ok, string what)
	{
		if (ok)
		{
			s_passed++;
			Console.WriteLine("ok    " + what);
		}
		else
		{
			s_failed++;
			Console.WriteLine("FAIL  " + what);
		}
	}

	// A3: line 1 "SHEPHERD <version> · <health>", line 2 the block's name, line 3 a clock; \r\n and \n alike.
	private static void Panels(string fixtures)
	{
		(string file, string health, bool running, string name)[] samples =
		{
			("status-working.txt", "working", true, "Base PB"),
			("status-idle.txt", "idle", true, "Base PB"),
			("status-learning.txt", "learning", true, "Base PB"),
			("status-halted.txt", "HALTED", false, "Base PB"),
			("status-standing-by.txt", "standing by", false, "Shuttle PB"),
		};
		foreach ((string file, string health, bool running, string name) in samples)
		{
			string text = File.ReadAllText(Path.Combine(fixtures, file));
			foreach (string variant in new[] { text.Replace("\r\n", "\n"), text.Replace("\r\n", "\n").Replace("\n", "\r\n") })
			{
				string ending = variant.Contains("\r\n") ? "CRLF" : "LF";
				ShepherdPanel panel = ShepherdPanel.Parse(variant);
				Check(panel != null, $"{file} ({ending}): line 1 is a status panel");
				if (panel == null)
				{
					continue;
				}
				Check(panel.Health == health, $"{file} ({ending}): health '{panel.Health}' is '{health}'");
				Check(panel.Running == running, $"{file} ({ending}): counts as doing its jobs: {running}");
				Check(panel.Name == name, $"{file} ({ending}): line 2 is the block's name '{name}'");
				Check(System.Text.RegularExpressions.Regex.IsMatch(panel.Clock, @"^\d\d:\d\d:\d\d$"), $"{file} ({ending}): line 3 is a clock");
				Check(System.Text.RegularExpressions.Regex.IsMatch(panel.Version, @"^\d+\.\d+\.\d+$"), $"{file} ({ending}): version '{panel.Version}' is MAJOR.MINOR.PATCH");
			}
		}
		Check(ShepherdPanel.Parse("SHEPHERD v1.0.21 running\nBase PB") == null, "the Echo's first line is not a panel");
		Check(ShepherdPanel.Parse("Some LCD text") == null, "other text is not a panel");
		Check(ShepherdPanel.AtLeast("1.0.21", 1, 0, 21) && !ShepherdPanel.AtLeast("1.0.5", 1, 0, 21) && ShepherdPanel.AtLeast("1.1.0", 1, 0, 21) && !ShepherdPanel.AtLeast(null, 1, 0, 21), "version comparison");
	}

	// A1 and A2: the default Custom Data marks SHEPHERD and gives the defaults.
	private static void Settings(string fixtures)
	{
		string text = File.ReadAllText(Path.Combine(fixtures, "settings-defaults.ini"));
		Check(ShepherdConfig.Looks(text), "settings-defaults.ini: detected as a SHEPHERD block's Custom Data");
		ShepherdConfig config = ShepherdConfig.Parse(text);
		Check(config.ReactorFillPct == 15.0 && config.GasGenFillPct == 50.0 && config.RefineryFillPct == 30.0, "settings-defaults.ini: fill % 15 / 50 / 30");
		Check(config.BottleSweepMinutes == 3.0, "settings-defaults.ini: bottleSweepMinutes 3");
		Check(!config.DockedFuel && !config.DockedProduction, "settings-defaults.ini: dockedFuel and dockedProduction false");
		Check(!ShepherdConfig.Looks("[Settings]\nreactorFillPct=15"), "no refineryFillPct: not SHEPHERD");
		Check(!ShepherdConfig.Looks("[Other]\nrefineryFillPct=30"), "refineryFillPct outside [Settings]: not SHEPHERD");
	}

	// A2 "How a value is read".
	private static void ReadingRules()
	{
		Check(ShepherdConfig.Parse("[Settings]\nrefineryFillPct=30\nrefineryFillPct2=0").RefineryFillPct == 30.0, "other keys don't count");
		Check(ShepherdConfig.Parse("[Settings]\nrefineryFillPct=0 ; SSM does it").RefineryFillPct == 0.0, "a ; comment is cut off");
		Check(ShepherdConfig.Parse("[Settings]\nRefineryFillPct=0").RefineryFillPct == 30.0, "keys are case-sensitive (wrong case: default)");
		Check(ShepherdConfig.Parse("[Settings]\nrefineryFillPct=150").RefineryFillPct == 30.0, "out of range: default");
		Check(ShepherdConfig.Parse("[Settings]\nreactorFillPct=-1").ReactorFillPct == 15.0, "negative fill %: default");
		Check(ShepherdConfig.Parse("[Settings]\ngasGenFillPct=12,5").GasGenFillPct == 50.0, "a comma decimal doesn't parse: default");
		Check(ShepherdConfig.Parse("[Settings]\ngasGenFillPct=12.5").GasGenFillPct == 12.5, "a . decimal parses");
		Check(ShepherdConfig.Parse("[Settings]\nbottleSweepMinutes=0").BottleSweepMinutes == 0.0, "bottleSweepMinutes 0 switches bottles off");
		Check(ShepherdConfig.Parse("[Settings]\nbottleSweepMinutes=-2").BottleSweepMinutes == 3.0, "bottleSweepMinutes below 0: default");
		Check(ShepherdConfig.Parse("[Settings]\ndockedFuel=TRUE").DockedFuel, "booleans in any case");
		Check(!ShepherdConfig.Parse("[Settings]\ndockedFuel=yes").DockedFuel, "a boolean that isn't true/false is false");
	}

	// A5.1: quota lines.
	private static void Quotas()
	{
		ShepherdConfig config = ShepherdConfig.Parse(string.Join("\n",
			"[Reserve]",
			"Component/SteelPlate=500ma",
			"MyObjectBuilder_Component/Motor=100A",
			"Ingot/Iron=All",
			"Component/Girder=AllA",
			"Component/Display=-5",
			"Component/Computer=lots",
			"[Reserve:Ship]",
			"docked=true",
			"Ammo/Missile=20L ; spare",
			"[Categories]",
			"MyObjectBuilder_Ingot/Uranium=Fuel"));
		ShepherdQuota plate = config.Quotas.FirstOrDefault(q => q.Item == "Component/SteelPlate");
		Check(plate != null && plate.Mode == "M" && plate.Auto && plate.Target == 500.0 && plate.Pool == "", "500ma: M with A, letters in any case, default pool");
		Check(config.Quotas.Any(q => q.Item == "Component/Motor" && q.Mode == "" && q.Auto), "MyObjectBuilder_ prefix stripped; plain amount with A");
		Check(config.Quotas.Any(q => q.Item == "Ingot/Iron" && q.Mode == "All"), "All");
		Check(!config.Quotas.Any(q => q.Item == "Component/Girder" || q.Item == "Component/Display" || q.Item == "Component/Computer"), "AllA, a negative and text are ignored");
		Check(!config.Quotas.Any(q => q.Item == "docked"), "docked=true is a pool setting, not an item");
		Check(config.Quotas.Any(q => q.Item == "Ammo/Missile" && q.Mode == "L" && !q.Auto && q.Pool == "Ship"), "a named pool, with a ; comment cut off");
		Check(config.Categories.TryGetValue("Ingot/Uranium", out string category) && category == "Fuel", "[Categories] line, prefix stripped");
		Check(config.AutoQuotas("Component/SteelPlate").Count() == 1 && !config.AutoQuotas("Ammo/Missile").Any(), "quota lines with A");
	}
}
