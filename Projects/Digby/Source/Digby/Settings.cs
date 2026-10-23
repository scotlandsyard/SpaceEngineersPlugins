using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace Digby;

/// <summary>
/// The chat setting (one file for all worlds), and per world: the last plan and the planet names typed with
/// /digby planet name. All in Storage\Digby through PluginStorage.
/// </summary>
internal static class Settings
{
	private const string FileName = "Digby_Settings.txt";

	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static string DisplayName = "";

	/// <summary>Planet entity id -> the name typed for it in this world.</summary>
	public static readonly Dictionary<long, string> PlanetNames = new Dictionary<long, string>();

	/// <summary>This world's name, made safe for a file name.</summary>
	public static string World { get; private set; } = "Unknown";

	private static string PlanFile => $"Digby_{World}_plan.txt";

	private static string PlanetsFile => $"Digby_{World}_planets.txt";

	public static string SafeWorldName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		return new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
	}

	public static void Load()
	{
		World = SafeWorldName();
		Dictionary<string, string> values = ReadFile(FileName);
		if (values.TryGetValue("Personality", out string level) && Enum.TryParse(level, true, out Personality.Chattiness parsed) && Enum.IsDefined(typeof(Personality.Chattiness), parsed))
		{
			Chattiness = parsed;
		}
		if (values.TryGetValue("DisplayName", out string displayName))
		{
			DisplayName = displayName;
		}
		PlanetNames.Clear();
		foreach (KeyValuePair<string, string> pair in ReadFile(PlanetsFile))
		{
			if (long.TryParse(pair.Key, out long id) && pair.Value.Length > 0)
			{
				PlanetNames[id] = pair.Value;
			}
		}
	}

	public static void Save()
	{
		Write(FileName, new Dictionary<string, string> { ["Personality"] = Chattiness.ToString(), ["DisplayName"] = DisplayName ?? "" });
	}

	public static void SavePlanetNames()
	{
		Write(PlanetsFile, PlanetNames.ToDictionary(p => p.Key.ToString(CultureInfo.InvariantCulture), p => p.Value));
	}

	public static DigPlanner.Plan LoadPlan()
	{
		Dictionary<string, string> values = ReadFile(PlanFile);
		DigPlanner.Plan plan = new DigPlanner.Plan();
		string Get(string key) => values.TryGetValue(key, out string value) ? value : null;
		plan.From = Get("from") ?? plan.From;
		plan.Target = Get("target") ?? "";
		double.TryParse(Get("depth") ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out plan.Depth);
		string marker = Get("marker");
		plan.Marker = marker == "server" || marker == "none" ? marker : "local";
		plan.StartMarker = Get("start") != "no";
		plan.RigA = Get("riga") ?? "";
		plan.RigB = Get("rigb") ?? "";
		plan.RigUp = Get("rigup") ?? "";
		plan.SmallGrid = Get("small") == "yes";
		return plan;
	}

	public static void SavePlan(DigPlanner.Plan plan)
	{
		Write(PlanFile, new Dictionary<string, string>
		{
			["from"] = plan.From,
			["target"] = plan.Target,
			["depth"] = plan.Depth.ToString(CultureInfo.InvariantCulture),
			["marker"] = plan.Marker,
			["start"] = plan.StartMarker ? "yes" : "no",
			["riga"] = plan.RigA,
			["rigb"] = plan.RigB,
			["rigup"] = plan.RigUp,
			["small"] = plan.SmallGrid ? "yes" : "no"
		});
	}

	private static Dictionary<string, string> ReadFile(string file)
	{
		Dictionary<string, string> values = new Dictionary<string, string>();
		try
		{
			if (!PluginStorage.FileExists(file, typeof(Settings)))
			{
				return values;
			}
			using TextReader reader = PluginStorage.Read(file, typeof(Settings));
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				int equals = line.IndexOf('=');
				if (equals > 0)
				{
					values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Digby] Could not read {file}: {ex.Message}");
		}
		return values;
	}

	private static void Write(string file, Dictionary<string, string> values)
	{
		try
		{
			using TextWriter writer = PluginStorage.Write(file, typeof(Settings));
			foreach (KeyValuePair<string, string> pair in values)
			{
				writer.WriteLine(pair.Key + "=" + (pair.Value ?? "").Replace("\r", " ").Replace("\n", " "));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Digby] Could not save {file}: {ex.Message}");
		}
	}
}
