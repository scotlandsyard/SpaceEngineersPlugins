using System;
using System.Collections.Generic;
using System.IO;
using TimShared;
using VRage.Utils;

namespace Tripwire;

/// <summary>The plugin's settings, kept in its own local storage file (never in Custom Data).</summary>
internal static class Settings
{
	private const string FileName = "Tripwire_Settings.txt";

	/// <summary>Write a chat line when your blocks are damaged, ground or lost.</summary>
	public static bool ChatAlerts = true;

	/// <summary>Play a HUD sound with each chat alert.</summary>
	public static bool Sound = true;

	/// <summary>Also watch grids owned by your faction.</summary>
	public static bool WatchFaction;

	/// <summary>No alerts for the ship you're piloting (you already know it's being hit).</summary>
	public static bool QuietWhenPiloting = true;

	/// <summary>No grinding alerts near you while you hold a grinder, or near the ship you pilot while its grinders are on.</summary>
	public static bool QuietWhenGrinding = true;

	/// <summary>Optional, off by default: name players near a damaged block, only when the game's HUD is showing their name (NearbyNames.cs).</summary>
	public static bool NameNearby;

	/// <summary>Tell you in chat when a grid's script log has new events since your last visit.</summary>
	public static bool VisitNews = true;

	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static string DisplayName = "";

	public static void Load()
	{
		try
		{
			if (!PluginStorage.FileExists(FileName, typeof(Settings)))
			{
				return;
			}
			Dictionary<string, string> values = new Dictionary<string, string>();
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
			ChatAlerts = Flag(values, "ChatAlerts", ChatAlerts);
			Sound = Flag(values, "Sound", Sound);
			WatchFaction = Flag(values, "WatchFaction", WatchFaction);
			QuietWhenPiloting = Flag(values, "QuietWhenPiloting", QuietWhenPiloting);
			QuietWhenGrinding = Flag(values, "QuietWhenGrinding", QuietWhenGrinding);
			NameNearby = Flag(values, "NameNearby", NameNearby);
			VisitNews = Flag(values, "VisitNews", VisitNews);
			if (values.TryGetValue("Personality", out string level) && Enum.TryParse(level, true, out Personality.Chattiness parsed) && Enum.IsDefined(typeof(Personality.Chattiness), parsed))
			{
				Chattiness = parsed;
			}
			if (values.TryGetValue("DisplayName", out string displayName))
			{
				DisplayName = displayName;
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Could not read settings: {ex.Message}");
		}
	}

	private static bool Flag(Dictionary<string, string> values, string key, bool fallback)
	{
		return values.TryGetValue(key, out string value) ? value == "1" : fallback;
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = PluginStorage.Write(FileName, typeof(Settings)))
			{
				writer.WriteLine("ChatAlerts=" + (ChatAlerts ? "1" : "0"));
				writer.WriteLine("Sound=" + (Sound ? "1" : "0"));
				writer.WriteLine("WatchFaction=" + (WatchFaction ? "1" : "0"));
				writer.WriteLine("QuietWhenPiloting=" + (QuietWhenPiloting ? "1" : "0"));
				writer.WriteLine("QuietWhenGrinding=" + (QuietWhenGrinding ? "1" : "0"));
				writer.WriteLine("NameNearby=" + (NameNearby ? "1" : "0"));
				writer.WriteLine("VisitNews=" + (VisitNews ? "1" : "0"));
				writer.WriteLine("Personality=" + Chattiness);
				writer.WriteLine("DisplayName=" + DisplayName);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Could not save settings: {ex.Message}");
		}
	}
}
