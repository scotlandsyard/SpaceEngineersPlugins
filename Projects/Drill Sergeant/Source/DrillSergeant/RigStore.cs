using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace DrillSergeant;

public class RigFile
{
	public List<Rig> Rigs = new List<Rig>();
}

/// <summary>This world's rigs and their progress, in DrillSergeant_&lt;world&gt;_Rigs.xml in local storage. Nothing goes on the server.</summary>
internal static class RigStore
{
	public static List<Rig> Rigs = new List<Rig>();

	private static bool s_dirty;

	private static double s_nextSave;

	private static bool s_loadFailed;

	private static string FileName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		string safe = new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
		return $"DrillSergeant_{safe}_Rigs.xml";
	}

	public static void Load()
	{
		Rigs = new List<Rig>();
		s_loadFailed = false;
		try
		{
			string file = FileName();
			if (PluginStorage.FileExists(file, typeof(RigStore)))
			{
				Rigs = PluginStorage.ReadXml<RigFile>(file, typeof(RigStore))?.Rigs ?? new List<Rig>();
			}
		}
		catch (Exception ex)
		{
			// PluginStorage keeps the unreadable file aside before the next save.
			s_loadFailed = true;
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] Could not read the rigs: {ex.Message}");
		}
		foreach (Rig rig in Rigs)
		{
			rig.Live = new RigRuntime();
		}
	}

	public static bool LoadFailed => s_loadFailed;

	public static Rig Find(long rotorId) => Rigs.FirstOrDefault(r => r.RotorId == rotorId);

	public static void Changed()
	{
		s_dirty = true;
	}

	/// <summary>Saves at most every 10 seconds while progress changes.</summary>
	public static void Update(double now)
	{
		if (s_dirty && now >= s_nextSave)
		{
			Save();
			s_nextSave = now + 10;
		}
	}

	public static void Save()
	{
		s_dirty = false;
		try
		{
			using (TextWriter writer = PluginStorage.Write(FileName(), typeof(RigStore)))
			{
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(new RigFile { Rigs = Rigs }));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] Could not save the rigs: {ex.Message}");
		}
	}
}
