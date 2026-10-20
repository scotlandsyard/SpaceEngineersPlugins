using System;
using System.Collections.Generic;
using System.IO;
using TimShared;
using VRage.Utils;

namespace DrillSergeant;

/// <summary>The plugin's own settings, kept in its local storage file. Rigs are per world, in RigStore.</summary>
internal static class Settings
{
	private const string FileName = "DrillSergeant_Settings.txt";

	/// <summary>Offer Start/Pause and Stop as toolbar actions on rig rotors. Off by default: their ids are saved in the grid's toolbars.</summary>
	public static bool ToolbarActions;

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
			ToolbarActions = values.TryGetValue("ToolbarActions", out string toolbar) ? toolbar == "1" : ToolbarActions;
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
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] Could not read settings: {ex.Message}");
		}
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = PluginStorage.Write(FileName, typeof(Settings)))
			{
				writer.WriteLine("ToolbarActions=" + (ToolbarActions ? "1" : "0"));
				writer.WriteLine("Personality=" + Chattiness);
				writer.WriteLine("DisplayName=" + DisplayName);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] Could not save settings: {ex.Message}");
		}
	}
}
