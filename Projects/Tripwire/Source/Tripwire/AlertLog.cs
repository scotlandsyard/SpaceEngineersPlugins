using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace Tripwire;

/// <summary>
/// This plugin's own alerts for the current world, newest last, kept in Tripwire_&lt;world&gt;_Alerts.txt so they survive a
/// reload. Each line is "time|kind|grid|block|detail|nearby".
/// </summary>
internal static class AlertLog
{
	public const int MaxEntries = 500;

	public static readonly List<LogEntry> Entries = new List<LogEntry>();

	private static bool s_dirty;

	private static double s_nextSave;

	private static string FileName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		string safe = new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
		return $"Tripwire_{safe}_Alerts.txt";
	}

	public static void Add(LogEntry entry)
	{
		entry.IsAlert = true;
		entry.Raw = string.Join("|", entry.Time.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'"), entry.Kind, Clean(entry.Grid), Clean(entry.Subject), Clean(entry.Detail), Clean(entry.Nearby));
		Entries.Add(entry);
		if (Entries.Count > MaxEntries)
		{
			Entries.RemoveRange(0, Entries.Count - MaxEntries);
		}
		s_dirty = true;
	}

	public static void Clear()
	{
		Entries.Clear();
		s_dirty = true;
		Save();
	}

	private static string Clean(string text)
	{
		return (text ?? "").Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
	}

	/// <summary>Saves at most every 30 seconds while alerts come in.</summary>
	public static void Update(double now)
	{
		if (s_dirty && now >= s_nextSave)
		{
			s_nextSave = now + 30.0;
			Save();
		}
	}

	public static void Load()
	{
		Entries.Clear();
		s_dirty = false;
		try
		{
			string file = FileName();
			if (!PluginStorage.FileExists(file, typeof(AlertLog)))
			{
				return;
			}
			using (TextReader reader = PluginStorage.Read(file, typeof(AlertLog)))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					string[] f = line.Split('|');
					if (f.Length < 6 || !LogFormat.TryParseTime(f[0], out DateTime time))
					{
						continue;
					}
					Entries.Add(new LogEntry { Time = time, Kind = f[1], Grid = f[2], Subject = f[3], Detail = f[4], Nearby = f[5], IsAlert = true, Raw = line });
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Could not read alerts: {ex.Message}");
		}
	}

	public static void Save()
	{
		if (!s_dirty || MyAPIGateway.Session == null)
		{
			return;
		}
		s_dirty = false;
		try
		{
			using (TextWriter writer = PluginStorage.Write(FileName(), typeof(AlertLog)))
			{
				foreach (LogEntry entry in Entries)
				{
					writer.WriteLine(entry.Raw);
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Could not save alerts: {ex.Message}");
		}
	}
}
