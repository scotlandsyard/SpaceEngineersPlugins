using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TimShared;
using VRage.Utils;

namespace Tally;

/// <summary>The chat setting and the window's choices, one file for all worlds, in Storage\Tally through PluginStorage.</summary>
internal static class Settings
{
	private const string FileName = "Tally_Settings.txt";

	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static string DisplayName = "";

	/// <summary>Projector, Blueprint or Grid: the source list the window opens on.</summary>
	public static string Kind = Source.Projector;

	public static bool StillNeeded = true;

	public static bool Carried;

	public static bool Cargo;

	public static int Modules;

	public static void Load()
	{
		Dictionary<string, string> values = ReadFile(FileName);
		string Get(string key) => values.TryGetValue(key, out string value) ? value : null;
		if (Enum.TryParse(Get("Personality") ?? "", true, out Personality.Chattiness parsed) && Enum.IsDefined(typeof(Personality.Chattiness), parsed))
		{
			Chattiness = parsed;
		}
		DisplayName = Get("DisplayName") ?? "";
		string kind = Get("Kind");
		Kind = kind == Source.Library || kind == Source.Grid ? kind : Source.Projector;
		StillNeeded = Get("StillNeeded") != "no";
		Carried = Get("Carried") == "yes";
		Cargo = Get("Cargo") == "yes";
		int.TryParse(Get("Modules") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out Modules);
		Modules = Math.Max(0, Math.Min(Materials.MaxModules, Modules));
	}

	public static void Save()
	{
		Write(FileName, new Dictionary<string, string>
		{
			["Personality"] = Chattiness.ToString(),
			["DisplayName"] = DisplayName ?? "",
			["Kind"] = Kind,
			["StillNeeded"] = StillNeeded ? "yes" : "no",
			["Carried"] = Carried ? "yes" : "no",
			["Cargo"] = Cargo ? "yes" : "no",
			["Modules"] = Modules.ToString(CultureInfo.InvariantCulture)
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
			MyLog.Default.WriteLineAndConsole($"[Tally] Could not read {file}: {ex.Message}");
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
			MyLog.Default.WriteLineAndConsole($"[Tally] Could not save {file}: {ex.Message}");
		}
	}
}
