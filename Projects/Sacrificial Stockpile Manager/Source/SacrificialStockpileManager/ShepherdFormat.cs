using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SacrificialStockpileManager;

// What SSM reads from a SHEPHERD script, parsed the way SHEPHERD itself reads it (its INTEGRATION.md, Part A). This
// file uses no game types, so Tests/ can check it against SHEPHERD's sample files (SHEPHERD.Tests/Fixtures).

/// <summary>One line of a [Reserve] or [Reserve:Name] section: an item kept at an amount, maybe with automatic production.</summary>
internal sealed class ShepherdQuota
{
	public string Pool;

	public string Item;

	/// <summary>"", "M", "L" or "All".</summary>
	public string Mode;

	public double Target;

	/// <summary>The trailing A: the script produces toward the amount (crafts, or refines an ingot).</summary>
	public bool Auto;

	public string Text => Mode == "All" ? "All" : Target.ToString("0.##", CultureInfo.InvariantCulture) + Mode + (Auto ? "A" : "");

	/// <summary>
	/// Reads a quota value as the script does: a plain non-negative amount, then M or L, then A (letters in any case);
	/// or All. Anything else (AllA, a reordered or repeated letter, a negative, text) is ignored by the script: null.
	/// </summary>
	public static ShepherdQuota Parse(string pool, string item, string value)
	{
		string s = (value ?? "").Trim();
		if (s.Length == 0)
		{
			return null;
		}
		if (s.Equals("All", StringComparison.OrdinalIgnoreCase))
		{
			return new ShepherdQuota { Pool = pool, Item = item, Mode = "All" };
		}
		bool auto = char.ToUpperInvariant(s[s.Length - 1]) == 'A';
		if (auto)
		{
			s = s.Substring(0, s.Length - 1);
			if (s.Equals("All", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
		}
		string mode = "";
		char last = s.Length > 0 ? char.ToUpperInvariant(s[s.Length - 1]) : ' ';
		if (last == 'M' || last == 'L')
		{
			mode = last.ToString();
			s = s.Substring(0, s.Length - 1);
		}
		if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double target) || target < 0.0)
		{
			return null;
		}
		return new ShepherdQuota { Pool = pool, Item = item, Mode = mode, Target = target, Auto = auto };
	}
}

/// <summary>A programmable block's Custom Data read as INI, the way MyIni splits it: sections, then key=value lines in order.</summary>
internal static class ShepherdIni
{
	/// <summary>Sections in order, each with its key=value lines as written (key names keep their case; values are raw).</summary>
	public static List<KeyValuePair<string, List<KeyValuePair<string, string>>>> Read(string text)
	{
		List<KeyValuePair<string, List<KeyValuePair<string, string>>>> sections = new List<KeyValuePair<string, List<KeyValuePair<string, string>>>>();
		List<KeyValuePair<string, string>> current = null;
		foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
		{
			string line = raw.Trim();
			// MyIni stops at a line of three dashes: what follows is free text.
			if (line == "---")
			{
				break;
			}
			if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
			{
				continue;
			}
			if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
			{
				current = new List<KeyValuePair<string, string>>();
				sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>(line.Substring(1, line.Length - 2).Trim(), current));
				continue;
			}
			if (current == null)
			{
				continue;
			}
			// A line starting with | continues the last value on a new line.
			if (line.StartsWith("|", StringComparison.Ordinal) && current.Count > 0)
			{
				KeyValuePair<string, string> last = current[current.Count - 1];
				current[current.Count - 1] = new KeyValuePair<string, string>(last.Key, last.Value + "\n" + line.Substring(1));
				continue;
			}
			int equals = line.IndexOf('=');
			if (equals > 0)
			{
				current.Add(new KeyValuePair<string, string>(line.Substring(0, equals).Trim(), line.Substring(equals + 1).Trim()));
			}
		}
		return sections;
	}

	/// <summary>The value as SHEPHERD reads it: cut at the first ; and trimmed.</summary>
	public static string Value(string raw)
	{
		if (raw == null)
		{
			return null;
		}
		int semi = raw.IndexOf(';');
		return (semi >= 0 ? raw.Substring(0, semi) : raw).Trim();
	}
}

/// <summary>The parts of a SHEPHERD programmable block's Custom Data that decide which jobs it does.</summary>
internal sealed class ShepherdConfig
{
	public const double DefaultReactorFillPct = 15.0;

	public const double DefaultGasGenFillPct = 50.0;

	public const double DefaultRefineryFillPct = 30.0;

	public const double DefaultBottleSweepMinutes = 3.0;

	public double ReactorFillPct = DefaultReactorFillPct;

	public double GasGenFillPct = DefaultGasGenFillPct;

	public double RefineryFillPct = DefaultRefineryFillPct;

	public double BottleSweepMinutes = DefaultBottleSweepMinutes;

	public bool DockedProduction;

	public bool DockedFuel;

	/// <summary>[Categories]: item key to category, for items moved out of their built-in category.</summary>
	public readonly Dictionary<string, string> Categories = new Dictionary<string, string>();

	public readonly List<ShepherdQuota> Quotas = new List<ShepherdQuota>();

	/// <summary>A SHEPHERD block's Custom Data has a [Settings] section with the key refineryFillPct (it adds missing keys on its first save).</summary>
	public static bool Looks(string customData)
	{
		return !string.IsNullOrEmpty(customData) && customData.IndexOf("refineryFillPct", StringComparison.Ordinal) >= 0 && Settings(ShepherdIni.Read(customData)).Any(k => k.Key == "refineryFillPct");
	}

	private static IEnumerable<KeyValuePair<string, string>> Settings(List<KeyValuePair<string, List<KeyValuePair<string, string>>>> sections)
	{
		return sections.Where(s => s.Key.Equals("Settings", StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Value);
	}

	/// <summary>
	/// Reads the job switches exactly as SHEPHERD does: key names are case-sensitive, a value is cut at the first ;,
	/// a number counts only when it parses (. as the decimal mark) and is in range (otherwise the default applies),
	/// and a boolean is true/false in any case (anything else is false). Quota keys may carry MyObjectBuilder_.
	/// </summary>
	public static ShepherdConfig Parse(string customData)
	{
		ShepherdConfig config = new ShepherdConfig();
		List<KeyValuePair<string, List<KeyValuePair<string, string>>>> sections = ShepherdIni.Read(customData);
		foreach (KeyValuePair<string, string> setting in Settings(sections))
		{
			string value = ShepherdIni.Value(setting.Value);
			switch (setting.Key)
			{
			case "reactorFillPct":
				config.ReactorFillPct = Number(value, 0.0, 100.0, config.ReactorFillPct);
				break;
			case "gasGenFillPct":
				config.GasGenFillPct = Number(value, 0.0, 100.0, config.GasGenFillPct);
				break;
			case "refineryFillPct":
				config.RefineryFillPct = Number(value, 0.0, 100.0, config.RefineryFillPct);
				break;
			case "bottleSweepMinutes":
				config.BottleSweepMinutes = Number(value, 0.0, double.MaxValue, config.BottleSweepMinutes);
				break;
			case "dockedProduction":
				config.DockedProduction = bool.TryParse(value, out bool production) && production;
				break;
			case "dockedFuel":
				config.DockedFuel = bool.TryParse(value, out bool fuel) && fuel;
				break;
			}
		}
		foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> section in sections)
		{
			bool categories = section.Key == "Categories";
			string pool = null;
			if (section.Key.StartsWith("Reserve", StringComparison.OrdinalIgnoreCase))
			{
				string after = section.Key.Substring(7);
				pool = after.Length == 0 ? "" : after[0] == ':' ? after.Substring(1) : null;
			}
			if (!categories && pool == null)
			{
				continue;
			}
			foreach (KeyValuePair<string, string> line in section.Value)
			{
				string item = line.Key.StartsWith("MyObjectBuilder_", StringComparison.Ordinal) ? line.Key.Substring(16) : line.Key;
				string value = ShepherdIni.Value(line.Value);
				if (categories)
				{
					if (item.IndexOf('/') > 0 && !string.IsNullOrWhiteSpace(value))
					{
						config.Categories[item] = value;
					}
				}
				else if (line.Key != "docked")
				{
					ShepherdQuota quota = ShepherdQuota.Parse(pool, item, value);
					if (quota != null)
					{
						config.Quotas.Add(quota);
					}
				}
			}
		}
		return config;
	}

	private static double Number(string text, double low, double high, double fallback)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= low && value <= high ? value : fallback;
	}

	/// <summary>The quota lines that produce toward or take apart an item, from every pool.</summary>
	public IEnumerable<ShepherdQuota> AutoQuotas(string item)
	{
		return Quotas.Where(q => q.Auto && q.Item == item);
	}
}

/// <summary>
/// A [SHEPHERD] status panel: line 1 "SHEPHERD &lt;version&gt; · &lt;health&gt;", line 2 the drawing block's name, line 3 a
/// clock that changes on every redraw. The lines after the last rule are informative only (what it's doing and what
/// needs the player); SSM shows them but builds nothing on them.
/// </summary>
internal sealed class ShepherdPanel
{
	public const string Working = "working";

	public const string Idle = "idle";

	public const string Learning = "learning";

	public const string Halted = "HALTED";

	public const string StandingBy = "standing by";

	private static readonly Regex s_firstLine = new Regex(@"^SHEPHERD (\d+\.\d+\.\d+) · (.+)$", RegexOptions.CultureInvariant);

	public string Version;

	public string Health;

	public string Name;

	public string Clock;

	public List<string> Messages = new List<string>();

	/// <summary>SHEPHERD is doing its jobs (learning moves nothing on purpose, which is still its job).</summary>
	public bool Running => Health == Working || Health == Idle || Health == Learning;

	public static ShepherdPanel Parse(string text)
	{
		string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
		Match first = s_firstLine.Match(lines[0].TrimEnd());
		if (!first.Success)
		{
			return null;
		}
		ShepherdPanel panel = new ShepherdPanel
		{
			Version = first.Groups[1].Value,
			Health = first.Groups[2].Value.Trim(),
			Name = lines.Length > 1 ? lines[1].Trim() : "",
			Clock = lines.Length > 2 ? lines[2].Trim() : ""
		};
		int rule = Array.FindLastIndex(lines, l => l.Trim().Length >= 3 && l.Trim().Trim('─').Length == 0);
		if (rule >= 3)
		{
			panel.Messages = lines.Skip(rule + 1).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
		}
		return panel;
	}

	/// <summary>Whether a version is at least major.minor.patch (false when it's unknown).</summary>
	public static bool AtLeast(string version, int major, int minor, int patch)
	{
		string[] parts = (version ?? "").Split('.');
		if (parts.Length != 3 || !int.TryParse(parts[0], out int a) || !int.TryParse(parts[1], out int b) || !int.TryParse(parts[2], out int c))
		{
			return false;
		}
		return a != major ? a > major : b != minor ? b > minor : c >= patch;
	}
}
