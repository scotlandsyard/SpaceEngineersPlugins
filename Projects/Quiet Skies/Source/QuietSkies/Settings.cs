using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace QuietSkies;

/// <summary>
/// The chat setting (one file for all worlds) and the filter (one file per world): the mode and distance for each
/// kind, and the markers this plugin turned off with the game's Show on HUD request, so Show only turns those back on.
/// </summary>
internal static class Settings
{
	private const string FileName = "QuietSkies_Settings.txt";

	public const double DefaultKm = 10.0;

	public const double MinKm = 1.0;

	public const double MaxKm = 500.0;

	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static string DisplayName = "";

	public static readonly Dictionary<SignalKind, SignalMode> Modes = new Dictionary<SignalKind, SignalMode>();

	public static readonly Dictionary<SignalKind, double> NearbyKm = new Dictionary<SignalKind, double>();

	/// <summary>Entity ids of markers this plugin hid through the server. Never filled with markers you hid yourself.</summary>
	public static readonly HashSet<long> HiddenByUs = new HashSet<long>();

	public const int MaxKeep = 999;

	/// <summary>Keep only this many of a kind, deleting the oldest; 0 (the default) is off.</summary>
	public static readonly Dictionary<SignalKind, int> KeepNewest = new Dictionary<SignalKind, int>();

	/// <summary>
	/// The order markers arrived in, by entity id: a higher number is newer. The game keeps no creation time on a GPS,
	/// so Keep newest uses this.
	/// </summary>
	public static readonly Dictionary<long, long> Seen = new Dictionary<long, long>();

	public static long NextSeen = 1;

	private static string s_world = "";

	public static int Keep(SignalKind kind) => KeepNewest.TryGetValue(kind, out int keep) ? keep : 0;

	public static int ClampKeep(int keep) => Math.Max(0, Math.Min(MaxKeep, keep));

	public static SignalMode Mode(SignalKind kind) => Modes.TryGetValue(kind, out SignalMode mode) ? mode : SignalMode.Show;

	public static double Km(SignalKind kind) => NearbyKm.TryGetValue(kind, out double km) ? km : DefaultKm;

	public static double ClampKm(double km) => Math.Max(MinKm, Math.Min(MaxKm, km));

	private static string WorldFileName() => $"QuietSkies_{s_world}_Settings.txt";

	public static void Load()
	{
		Modes.Clear();
		NearbyKm.Clear();
		HiddenByUs.Clear();
		KeepNewest.Clear();
		Seen.Clear();
		NextSeen = 1;
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
		foreach (SignalKind kind in Signals.Kinds)
		{
			if (values.TryGetValue("Mode." + kind, out string mode) && Enum.TryParse(mode, true, out SignalMode m) && Enum.IsDefined(typeof(SignalMode), m))
			{
				Modes[kind] = m;
			}
			if (values.TryGetValue("Km." + kind, out string km) && double.TryParse(km, NumberStyles.Float, CultureInfo.InvariantCulture, out double k))
			{
				NearbyKm[kind] = ClampKm(k);
			}
			if (values.TryGetValue("Keep." + kind, out string keep) && int.TryParse(keep, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
			{
				KeepNewest[kind] = ClampKeep(n);
			}
		}
		if (values.TryGetValue("Seen", out string seen))
		{
			foreach (string pair in seen.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] parts = pair.Split(':');
				if (parts.Length == 2 && long.TryParse(parts[0].Trim(), out long entityId) && entityId != 0 && long.TryParse(parts[1].Trim(), out long order))
				{
					Seen[entityId] = order;
					NextSeen = Math.Max(NextSeen, order + 1);
				}
			}
		}
		if (values.TryGetValue("HiddenByUs", out string hidden))
		{
			foreach (string id in hidden.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (long.TryParse(id.Trim(), out long entityId) && entityId != 0)
				{
					HiddenByUs.Add(entityId);
				}
			}
		}
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
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] Could not read {file}: {ex.Message}");
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
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] Could not save settings: {ex.Message}");
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
				foreach (SignalKind kind in Signals.Kinds)
				{
					writer.WriteLine("Mode." + kind + "=" + Mode(kind));
					writer.WriteLine("Km." + kind + "=" + Km(kind).ToString(CultureInfo.InvariantCulture));
					writer.WriteLine("Keep." + kind + "=" + Keep(kind).ToString(CultureInfo.InvariantCulture));
				}
				writer.WriteLine("HiddenByUs=" + string.Join(",", HiddenByUs));
				writer.WriteLine("Seen=" + string.Join(",", Seen.Select(p => p.Key + ":" + p.Value)));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] Could not save this world's settings: {ex.Message}");
		}
	}
}
