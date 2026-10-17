using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Tripwire;

/// <summary>
/// Finds the Tripwire script's logs (the "--- Tripwire log ---" part of a block's Custom Data) on grids you could open
/// in the vanilla terminal right now, and keeps what it read for the rest of the session.
///
/// "Could open in the terminal" means the same as the game: you have access to the block, and its grid is the one
/// you're in, within reach of where you stand, or connected to you through antennas the way the terminal's remote
/// grid list is. It only reads Custom Data the game has already sent to this client; nothing is requested.
/// </summary>
internal static class ScriptLogs
{
	private const double ScanInterval = 10.0;

	/// <summary>How close you must be to a grid to count as standing at it (the terminal needs you at one of its blocks).</summary>
	private const double StandingReach = 10.0;

	private class Construct
	{
		public long Key;

		public string Name = "";

		public readonly Dictionary<string, LogEntry> Lines = new Dictionary<string, LogEntry>();

		public bool InReach;
	}

	private static readonly Dictionary<long, Construct> s_constructs = new Dictionary<long, Construct>();

	/// <summary>The newest event time you've seen for each construct (by its key), saved per world.</summary>
	private static readonly Dictionary<long, DateTime> s_lastVisit = new Dictionary<long, DateTime>();

	private static readonly HashSet<IMyEntity> s_entities = new HashSet<IMyEntity>();

	private static readonly HashSet<long> s_reachable = new HashSet<long>();

	private static readonly List<IMyCubeGrid> s_group = new List<IMyCubeGrid>();

	private static double s_nextScan;

	public static int ConstructCount => s_constructs.Count;

	public static IEnumerable<LogEntry> Entries => s_constructs.Values.SelectMany(c => c.Lines.Values);

	private static string FileName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		string safe = new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
		return $"Tripwire_{safe}_Visits.txt";
	}

	public static void Update(double now)
	{
		if (now < s_nextScan)
		{
			return;
		}
		s_nextScan = now + ScanInterval;
		Scan();
	}

	public static void Scan()
	{
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (player?.Character == null && !(MyAPIGateway.Session?.ControlledObject is IMyCubeBlock))
		{
			return;
		}
		FindReachable(player);
		foreach (Construct c in s_constructs.Values)
		{
			c.InReach = false;
		}
		Dictionary<long, Construct> fresh = new Dictionary<long, Construct>();
		s_entities.Clear();
		MyAPIGateway.Entities.GetEntities(s_entities, e => e is MyCubeGrid);
		foreach (IMyEntity entity in s_entities)
		{
			MyCubeGrid grid = (MyCubeGrid)entity;
			if (grid.Closed || grid.IsPreview || !s_reachable.Contains(Representative(grid)))
			{
				continue;
			}
			foreach (MyCubeBlock fat in grid.GetFatBlocks())
			{
				if (!(fat is MyTerminalBlock block) || !((IMyTerminalBlock)block).HasLocalPlayerAccess())
				{
					continue;
				}
				string data = block.CustomData;
				int at = data?.IndexOf(LogFormat.Marker, StringComparison.Ordinal) ?? -1;
				if (at < 0)
				{
					continue;
				}
				Construct construct = ConstructOf(grid);
				if (!construct.InReach)
				{
					construct.InReach = true;
					fresh[construct.Key] = construct;
				}
				foreach (string raw in data.Substring(at).Split('\n'))
				{
					string line = raw.Trim();
					if (line.Length == 0 || construct.Lines.ContainsKey(line))
					{
						continue;
					}
					LogEntry entry = LogFormat.Parse(line, construct.Name);
					if (entry != null)
					{
						entry.Construct = construct.Key;
						construct.Lines[line] = entry;
					}
				}
			}
		}
		s_entities.Clear();
		foreach (Construct construct in fresh.Values)
		{
			UpdateNew(construct);
		}
		Announce(fresh.Values);
	}

	/// <summary>Logical-group representatives of the grids you could open in the terminal now.</summary>
	private static void FindReachable(IMyPlayer player)
	{
		s_reachable.Clear();
		MyEntity receiver = MyAPIGateway.Session.ControlledObject is IMyCubeBlock controlled ? (MyEntity)controlled.CubeGrid : player.Character as MyEntity;
		if (receiver == null || MyAntennaSystem.Static == null)
		{
			return;
		}
		if (receiver is MyCubeGrid receiverGrid)
		{
			receiver = MyAntennaSystem.Static.GetLogicalGroupRepresentative(receiverGrid);
		}
		foreach (MyAntennaSystem.BroadcasterInfo info in MyAntennaSystem.Static.GetConnectedGridsInfo(receiver, null, mutual: true))
		{
			s_reachable.Add(info.EntityId);
		}
		// Standing at a grid: the terminal of any block you can reach opens the whole logical group.
		if (player.Character != null)
		{
			Vector3D position = player.Character.GetPosition();
			s_entities.Clear();
			MyAPIGateway.Entities.GetEntities(s_entities, e => e is MyCubeGrid);
			foreach (IMyEntity entity in s_entities)
			{
				BoundingBoxD box = entity.WorldAABB;
				box.Inflate(StandingReach);
				if (box.Contains(position) != ContainmentType.Disjoint)
				{
					s_reachable.Add(Representative((MyCubeGrid)entity));
				}
			}
			s_entities.Clear();
		}
	}

	private static long Representative(MyCubeGrid grid)
	{
		return MyAntennaSystem.Static?.GetLogicalGroupRepresentative(grid)?.EntityId ?? grid.EntityId;
	}

	/// <summary>The construct (mechanical group) a grid belongs to, keyed by its lowest grid id and named after its biggest grid.</summary>
	private static Construct ConstructOf(MyCubeGrid grid)
	{
		s_group.Clear();
		MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, s_group);
		long key = grid.EntityId;
		IMyCubeGrid biggest = grid;
		int most = -1;
		foreach (IMyCubeGrid member in s_group)
		{
			key = Math.Min(key, member.EntityId);
			int count = (member as MyCubeGrid)?.BlocksCount ?? 0;
			if (count > most)
			{
				most = count;
				biggest = member;
			}
		}
		s_group.Clear();
		if (!s_constructs.TryGetValue(key, out Construct construct))
		{
			construct = new Construct { Key = key };
			s_constructs[key] = construct;
		}
		construct.Name = biggest.DisplayName ?? "";
		return construct;
	}

	private static void UpdateNew(Construct construct)
	{
		DateTime seen = s_lastVisit.TryGetValue(construct.Key, out DateTime last) ? last : DateTime.MinValue;
		foreach (LogEntry entry in construct.Lines.Values)
		{
			entry.IsNew = entry.Time > seen;
		}
	}

	/// <summary>One chat line per construct that just came into reach with events you haven't seen.</summary>
	private static void Announce(IEnumerable<Construct> fresh)
	{
		foreach (Construct construct in fresh)
		{
			if (s_wasInReach.Contains(construct.Key))
			{
				continue;
			}
			int count = construct.Lines.Values.Count(e => e.IsNew && e.Kind != "START");
			if (count > 0 && Settings.VisitNews)
			{
				MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"{construct.Name}: {count} new event(s) in its Tripwire log since your last visit. /tripwire shows them.");
				Personality.Say("visit_news", "grid", construct.Name, "count", count.ToString());
			}
		}
		s_wasInReach.Clear();
		foreach (Construct construct in fresh)
		{
			s_wasInReach.Add(construct.Key);
		}
	}

	private static readonly HashSet<long> s_wasInReach = new HashSet<long>();

	/// <summary>Marks everything read so far as seen ("since your last visit" starts again from here).</summary>
	public static void MarkRead()
	{
		bool changed = false;
		foreach (Construct construct in s_constructs.Values)
		{
			if (construct.Lines.Count == 0)
			{
				continue;
			}
			DateTime newest = construct.Lines.Values.Max(e => e.Time);
			if (!s_lastVisit.TryGetValue(construct.Key, out DateTime last) || newest > last)
			{
				s_lastVisit[construct.Key] = newest;
				changed = true;
			}
			foreach (LogEntry entry in construct.Lines.Values)
			{
				entry.IsNew = false;
			}
		}
		if (changed)
		{
			SaveVisits();
		}
	}

	public static void Load()
	{
		s_constructs.Clear();
		s_lastVisit.Clear();
		s_wasInReach.Clear();
		s_nextScan = 0;
		try
		{
			string file = FileName();
			if (!PluginStorage.FileExists(file, typeof(ScriptLogs)))
			{
				return;
			}
			using (TextReader reader = PluginStorage.Read(file, typeof(ScriptLogs)))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					int equals = line.IndexOf('=');
					if (equals > 0 && long.TryParse(line.Substring(0, equals), out long key) && LogFormat.TryParseTime(line.Substring(equals + 1).Trim(), out DateTime time))
					{
						s_lastVisit[key] = time;
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Could not read visits: {ex.Message}");
		}
	}

	private static void SaveVisits()
	{
		try
		{
			using (TextWriter writer = PluginStorage.Write(FileName(), typeof(ScriptLogs)))
			{
				foreach (KeyValuePair<long, DateTime> kv in s_lastVisit)
				{
					writer.WriteLine($"{kv.Key}={kv.Value:yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'}");
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Could not save visits: {ex.Message}");
		}
	}

	public static void Unload()
	{
		s_constructs.Clear();
		s_wasInReach.Clear();
	}
}
