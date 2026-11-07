using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace Doorman;

internal enum DoorMode
{
	/// <summary>Follows the grid: closed after the grid's delay, unless it's a hangar door the grid leaves open or an airlock door.</summary>
	Default,

	/// <summary>Never closed by Doorman.</summary>
	Never,

	/// <summary>Closed even if it's a hangar door the grid leaves open or an airlock door.</summary>
	Always
}

/// <summary>
/// The chat setting (one file for all worlds) and the door settings (one file per world). Everything stays on this
/// PC: nothing is written to the grids, their Custom Data or block names.
/// </summary>
internal static class Settings
{
	private const string FileName = "Doorman_Settings.txt";

	public const double DefaultDelaySeconds = 3.0;

	public const double MinDelay = 1.0;

	public const double MaxDelay = 600.0;

	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static string DisplayName = "";

	// ---------------------------------------------------------------- per world

	public static bool Enabled = true;

	public static double Delay = DefaultDelaySeconds;

	public static bool Hangars;

	public static bool FactionGrids;

	public static bool SkipAirlocks = true;

	public static readonly Dictionary<long, double> GridDelay = new Dictionary<long, double>();

	public static readonly Dictionary<long, bool> GridHangars = new Dictionary<long, bool>();

	public static readonly Dictionary<long, DoorMode> DoorModes = new Dictionary<long, DoorMode>();

	public static readonly Dictionary<long, double> DoorDelay = new Dictionary<long, double>();

	private static string s_world = "";

	public static double ClampDelay(double seconds) => Math.Max(MinDelay, Math.Min(MaxDelay, seconds));

	public static DoorMode Mode(long door) => DoorModes.TryGetValue(door, out DoorMode mode) ? mode : DoorMode.Default;

	public static double DelayForGrid(long grid) => GridDelay.TryGetValue(grid, out double d) ? d : Delay;

	public static double DelayFor(long grid, long door) => DoorDelay.TryGetValue(door, out double d) ? d : DelayForGrid(grid);

	public static bool HangarsFor(long grid) => GridHangars.TryGetValue(grid, out bool on) ? on : Hangars;

	private static string WorldFileName() => $"Doorman_{s_world}_Settings.txt";

	public static void Load()
	{
		Enabled = true;
		Delay = DefaultDelaySeconds;
		Hangars = false;
		FactionGrids = false;
		SkipAirlocks = true;
		GridDelay.Clear();
		GridHangars.Clear();
		DoorModes.Clear();
		DoorDelay.Clear();
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		s_world = new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

		Dictionary<string, string> values = ReadFile(FileName);
		if (values.TryGetValue("Personality", out string level) && Enum.TryParse(level, true, out Personality.Chattiness parsed) && Enum.IsDefined(typeof(Personality.Chattiness), parsed))
		{
			Chattiness = parsed;
		}
		if (values.TryGetValue("DisplayName", out string displayName))
		{
			DisplayName = displayName;
		}

		values = ReadFile(WorldFileName());
		foreach (KeyValuePair<string, string> pair in values)
		{
			string[] parts = pair.Key.Split('.');
			if (parts.Length == 1)
			{
				switch (pair.Key)
				{
				case "Enabled":
					Enabled = pair.Value != "False";
					break;
				case "Delay":
					if (TryDelay(pair.Value, out double delay))
					{
						Delay = delay;
					}
					break;
				case "Hangars":
					Hangars = pair.Value == "True";
					break;
				case "FactionGrids":
					FactionGrids = pair.Value == "True";
					break;
				case "SkipAirlocks":
					SkipAirlocks = pair.Value != "False";
					break;
				}
				continue;
			}
			if (parts.Length != 3 || !long.TryParse(parts[1], out long id) || id == 0)
			{
				continue;
			}
			if (parts[0] == "Grid" && parts[2] == "Delay" && TryDelay(pair.Value, out double gridDelay))
			{
				GridDelay[id] = gridDelay;
			}
			else if (parts[0] == "Grid" && parts[2] == "Hangars")
			{
				GridHangars[id] = pair.Value == "True";
			}
			else if (parts[0] == "Door" && parts[2] == "Delay" && TryDelay(pair.Value, out double doorDelay))
			{
				DoorDelay[id] = doorDelay;
			}
			else if (parts[0] == "Door" && parts[2] == "Mode" && Enum.TryParse(pair.Value, true, out DoorMode mode) && Enum.IsDefined(typeof(DoorMode), mode) && mode != DoorMode.Default)
			{
				DoorModes[id] = mode;
			}
		}
	}

	public static bool TryDelay(string text, out double seconds)
	{
		seconds = 0;
		if (!double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
		{
			return false;
		}
		seconds = ClampDelay(value);
		return true;
	}

	public static string FormatDelay(double seconds)
	{
		return seconds.ToString("0.#", CultureInfo.InvariantCulture);
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
			using (TextReader reader = PluginStorage.Read(file, typeof(Settings)))
			{
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
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Doorman] Could not read {file}: {ex.Message}");
		}
		return values;
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = PluginStorage.Write(FileName, typeof(Settings)))
			{
				writer.WriteLine("Personality=" + Chattiness);
				writer.WriteLine("DisplayName=" + DisplayName);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Doorman] Could not save settings: {ex.Message}");
		}
	}

	public static void SaveWorld()
	{
		if (s_world.Length == 0)
		{
			return;
		}
		try
		{
			using (TextWriter writer = PluginStorage.Write(WorldFileName(), typeof(Settings)))
			{
				writer.WriteLine("Enabled=" + Enabled);
				writer.WriteLine("Delay=" + Delay.ToString(CultureInfo.InvariantCulture));
				writer.WriteLine("Hangars=" + Hangars);
				writer.WriteLine("FactionGrids=" + FactionGrids);
				writer.WriteLine("SkipAirlocks=" + SkipAirlocks);
				foreach (KeyValuePair<long, double> pair in GridDelay)
				{
					writer.WriteLine($"Grid.{pair.Key}.Delay={pair.Value.ToString(CultureInfo.InvariantCulture)}");
				}
				foreach (KeyValuePair<long, bool> pair in GridHangars)
				{
					writer.WriteLine($"Grid.{pair.Key}.Hangars={pair.Value}");
				}
				foreach (KeyValuePair<long, DoorMode> pair in DoorModes)
				{
					writer.WriteLine($"Door.{pair.Key}.Mode={pair.Value}");
				}
				foreach (KeyValuePair<long, double> pair in DoorDelay)
				{
					writer.WriteLine($"Door.{pair.Key}.Delay={pair.Value.ToString(CultureInfo.InvariantCulture)}");
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Doorman] Could not save this world's settings: {ex.Message}");
		}
	}
}
