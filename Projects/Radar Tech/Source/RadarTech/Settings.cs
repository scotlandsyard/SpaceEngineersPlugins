using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace RadarTech;

/// <summary>Radar Tech's settings, one file for all worlds: the readout, its range and hotkey, and the chat setting.</summary>
internal static class Settings
{
	private const string FileName = "RadarTech_Settings.txt";

	public const double DefaultKm = 20.0;

	public const double MinKm = 1.0;

	public const double MaxKm = 50.0;

	public const string DefaultKey = "Ctrl+Alt+R";

	/// <summary>The readout under the crosshair, measuring several times a second.</summary>
	public static bool Readout;

	public static double RangeKm = DefaultKm;

	/// <summary>Closing speed and time to contact after the distance.</summary>
	public static bool ShowClosing = true;

	public static string Hotkey = DefaultKey;

	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static string DisplayName = "";

	public static double ClampKm(double km) => Math.Max(MinKm, Math.Min(MaxKm, km));

	/// <summary>
	/// This world's view distance in km: how far the game draws, and the same limit a laser antenna's line of sight uses.
	/// Nothing past it can be seen, so nothing past it is measured. On a server it's the server's setting.
	/// </summary>
	public static double VisualKm
	{
		get
		{
			int? metres = MyAPIGateway.Session?.SessionSettings?.ViewDistance;
			return metres.HasValue && metres.Value > 0 ? metres.Value / 1000.0 : MaxKm;
		}
	}

	/// <summary>The range actually used: the setting, but never past the view distance.</summary>
	public static double EffectiveKm => Math.Min(RangeKm, VisualKm);

	public static void Load()
	{
		Dictionary<string, string> values = new Dictionary<string, string>();
		try
		{
			if (PluginStorage.FileExists(FileName, typeof(Settings)))
			{
				using (TextReader reader = PluginStorage.Read(FileName, typeof(Settings)))
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
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Radar Tech] Could not read {FileName}: {ex.Message}");
		}
		if (values.TryGetValue("Readout", out string readout) && bool.TryParse(readout, out bool r))
		{
			Readout = r;
		}
		if (values.TryGetValue("RangeKm", out string km) && double.TryParse(km, NumberStyles.Float, CultureInfo.InvariantCulture, out double k))
		{
			RangeKm = ClampKm(k);
		}
		if (values.TryGetValue("ShowClosing", out string closing) && bool.TryParse(closing, out bool c))
		{
			ShowClosing = c;
		}
		if (values.TryGetValue("Hotkey", out string key) && key.Length > 0)
		{
			Hotkey = key;
		}
		if (values.TryGetValue("Personality", out string level) && Enum.TryParse(level, true, out Personality.Chattiness parsed) && Enum.IsDefined(typeof(Personality.Chattiness), parsed))
		{
			Chattiness = parsed;
		}
		if (values.TryGetValue("DisplayName", out string displayName))
		{
			DisplayName = displayName;
		}
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = PluginStorage.Write(FileName, typeof(Settings)))
			{
				writer.WriteLine("Readout=" + Readout);
				writer.WriteLine("RangeKm=" + RangeKm.ToString(CultureInfo.InvariantCulture));
				writer.WriteLine("ShowClosing=" + ShowClosing);
				writer.WriteLine("Hotkey=" + Hotkey);
				writer.WriteLine("Personality=" + Chattiness);
				writer.WriteLine("DisplayName=" + DisplayName);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Radar Tech] Could not save settings: {ex.Message}");
		}
	}
}
