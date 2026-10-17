using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Tripwire;

/// <summary>
/// Watches your own blocks on the grids loaded on this client and reports when they lose integrity or disappear.
///
/// It only compares each block's integrity and build level with what it was a moment ago, a few thousand blocks a
/// frame: the same numbers the terminal shows. It deliberately never looks at who caused a change. The game does send
/// the grinder's or shooter's id to clients with every change, but this plugin doesn't register damage handlers or
/// read those messages, so it couldn't tell anyone where an attacker is even by mistake.
/// Grinding lowers a block's build level; damage only lowers its integrity. That's how the two are told apart.
/// </summary>
internal static class Watcher
{
	private const double RescanInterval = 5.0;

	private const double QuietInterval = 1.0;

	private const int ChecksPerFrame = 3000;

	/// <summary>A loss smaller than this share of the block's full integrity isn't reported (yet: small losses add up).</summary>
	private const float MinLoss = 0.02f;

	/// <summary>The same block isn't reported again within this many seconds; losses in between add up.</summary>
	private const double BlockCooldown = 60.0;

	/// <summary>A block removed within this many seconds of losing integrity counts as lost, not moved.</summary>
	private const double RecentDrop = 60.0;

	private const double GrindQuietRange = 20.0;

	private class Watched
	{
		public MySlimBlock Block;

		public float Build;

		public float Integ;

		public long Owner;

		public double LastDrop = -1000.0;

		public double LastEvent = -1000.0;

		public bool Removed;
	}

	private class GridWatch
	{
		public MyCubeGrid Grid;

		/// <summary>True: every block (a grid you or your faction own). False: only the blocks you own on someone else's grid.</summary>
		public bool WholeGrid;

		public readonly List<Watched> Blocks = new List<Watched>();

		public readonly Dictionary<MySlimBlock, Watched> Map = new Dictionary<MySlimBlock, Watched>();

		public int Cursor;

		public bool Seen;
	}

	private static readonly Dictionary<long, GridWatch> s_grids = new Dictionary<long, GridWatch>();

	private static readonly List<GridWatch> s_order = new List<GridWatch>();

	private static readonly HashSet<IMyEntity> s_entities = new HashSet<IMyEntity>();

	private static readonly HashSet<long> s_controlledGroup = new HashSet<long>();

	private static readonly List<IMyCubeGrid> s_groupGrids = new List<IMyCubeGrid>();

	private static int s_orderCursor;

	private static double s_now;

	private static double s_nextRescan;

	private static double s_nextQuiet;

	private static long s_me;

	private static bool s_holdingGrinder;

	private static Vector3D s_playerPosition;

	private static bool s_shipGrinding;

	private static BoundingSphereD s_shipSphere;

	public static int GridCount => s_grids.Count;

	public static void Update(double now)
	{
		s_now = now;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (player == null)
		{
			return;
		}
		s_me = player.IdentityId;
		if (now >= s_nextQuiet)
		{
			s_nextQuiet = now + QuietInterval;
			UpdateQuiet(player);
		}
		if (now >= s_nextRescan)
		{
			s_nextRescan = now + RescanInterval;
			Rescan();
		}
		CheckSome();
	}

	// ---------------------------------------------------------------- which grids

	private static void Rescan()
	{
		foreach (GridWatch gw in s_order)
		{
			gw.Seen = false;
		}
		s_entities.Clear();
		MyAPIGateway.Entities.GetEntities(s_entities, e => e is MyCubeGrid);
		foreach (IMyEntity entity in s_entities)
		{
			MyCubeGrid grid = (MyCubeGrid)entity;
			if (grid.Closed || grid.MarkedForClose || grid.IsPreview || grid.Physics == null)
			{
				continue;
			}
			bool whole = OwnsGrid(grid);
			bool partial = !whole && grid.SmallOwners.Contains(s_me);
			if (!whole && !partial)
			{
				continue;
			}
			if (s_grids.TryGetValue(grid.EntityId, out GridWatch existing))
			{
				if (existing.WholeGrid == whole && existing.Grid == grid)
				{
					existing.Seen = true;
					continue;
				}
				Remove(existing);
			}
			Add(grid, whole).Seen = true;
		}
		s_entities.Clear();
		for (int i = s_order.Count - 1; i >= 0; i--)
		{
			if (!s_order[i].Seen)
			{
				Remove(s_order[i]);
			}
		}
	}

	private static bool OwnsGrid(MyCubeGrid grid)
	{
		List<long> owners = grid.BigOwners;
		if (owners.Contains(s_me))
		{
			return true;
		}
		if (!Settings.WatchFaction || owners.Count == 0)
		{
			return false;
		}
		IMyFaction mine = MyAPIGateway.Session.Factions.TryGetPlayerFaction(s_me);
		return mine != null && MyAPIGateway.Session.Factions.TryGetPlayerFaction(owners[0])?.FactionId == mine.FactionId;
	}

	private static GridWatch Add(MyCubeGrid grid, bool whole)
	{
		GridWatch gw = new GridWatch { Grid = grid, WholeGrid = whole };
		foreach (MySlimBlock block in grid.GetBlocks())
		{
			Track(gw, block);
		}
		s_grids[grid.EntityId] = gw;
		s_order.Add(gw);
		grid.OnBlockAdded += OnBlockAdded;
		grid.OnBlockRemoved += OnBlockRemoved;
		return gw;
	}

	private static void Remove(GridWatch gw)
	{
		gw.Grid.OnBlockAdded -= OnBlockAdded;
		gw.Grid.OnBlockRemoved -= OnBlockRemoved;
		s_grids.Remove(gw.Grid.EntityId);
		s_order.Remove(gw);
	}

	private static void Track(GridWatch gw, MySlimBlock block)
	{
		if (gw.Map.ContainsKey(block))
		{
			return;
		}
		long owner = block.FatBlock?.OwnerId ?? 0;
		if (!gw.WholeGrid && owner != s_me)
		{
			return;
		}
		Watched w = new Watched { Block = block, Build = block.BuildIntegrity, Integ = block.Integrity, Owner = owner };
		gw.Map[block] = w;
		gw.Blocks.Add(w);
	}

	private static void OnBlockAdded(MySlimBlock block)
	{
		try
		{
			if (block?.CubeGrid != null && s_grids.TryGetValue(block.CubeGrid.EntityId, out GridWatch gw))
			{
				Track(gw, block);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Block added: {ex}");
		}
	}

	private static void OnBlockRemoved(MySlimBlock block)
	{
		try
		{
			if (block?.CubeGrid == null || !s_grids.TryGetValue(block.CubeGrid.EntityId, out GridWatch gw) || !gw.Map.TryGetValue(block, out Watched w))
			{
				return;
			}
			w.Removed = true;
			gw.Map.Remove(block);
			if (gw.Grid.Closed || gw.Grid.MarkedForClose)
			{
				return;
			}
			// A block that leaves intact was moved (a grid split or merge), not lost.
			float max = Math.Max(block.MaxIntegrity, 1f);
			if (block.Integrity / max >= 0.5f && s_now - w.LastDrop > RecentDrop)
			{
				return;
			}
			if (!IsQuiet(gw, block, grinding: true))
			{
				Report(gw, w, "LOST", "");
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Block removed: {ex}");
		}
	}

	// ---------------------------------------------------------------- checks

	private static void CheckSome()
	{
		int budget = ChecksPerFrame;
		int gridsTried = 0;
		while (budget > 0 && s_order.Count > 0 && gridsTried < s_order.Count)
		{
			if (s_orderCursor >= s_order.Count)
			{
				s_orderCursor = 0;
			}
			GridWatch gw = s_order[s_orderCursor];
			if (gw.Grid.Closed)
			{
				Remove(gw);
				continue;
			}
			while (budget > 0 && gw.Cursor < gw.Blocks.Count)
			{
				Watched w = gw.Blocks[gw.Cursor];
				budget--;
				if (w.Removed)
				{
					gw.Blocks[gw.Cursor] = gw.Blocks[gw.Blocks.Count - 1];
					gw.Blocks.RemoveAt(gw.Blocks.Count - 1);
					continue;
				}
				Check(gw, w);
				gw.Cursor++;
			}
			if (gw.Cursor >= gw.Blocks.Count)
			{
				gw.Cursor = 0;
				s_orderCursor++;
				gridsTried++;
			}
		}
	}

	private static void Check(GridWatch gw, Watched w)
	{
		MySlimBlock block = w.Block;
		float build = block.BuildIntegrity;
		float integ = block.Integrity;
		float max = Math.Max(block.MaxIntegrity, 1f);
		float threshold = MinLoss * max;
		if (build < w.Build || integ < w.Integ)
		{
			w.LastDrop = s_now;
		}
		long owner = block.FatBlock?.OwnerId ?? 0;
		if (w.Owner == s_me && owner != s_me && owner != 0)
		{
			if (!IsQuiet(gw, block, grinding: true))
			{
				Report(gw, w, "OWNER", "");
			}
		}
		w.Owner = owner;
		string kind = build < w.Build - threshold ? "GRIND" : integ < w.Integ - threshold ? "DMG" : null;
		if (kind == null)
		{
			// Welding and repairs raise the baseline; small losses add up until they pass the threshold.
			w.Build = Math.Max(w.Build, build);
			w.Integ = Math.Max(w.Integ, integ);
			return;
		}
		if (s_now - w.LastEvent < BlockCooldown)
		{
			return;
		}
		w.Build = build;
		w.Integ = integ;
		if (IsQuiet(gw, block, kind == "GRIND"))
		{
			return;
		}
		float ratio = kind == "GRIND" ? build / max : integ / max;
		Report(gw, w, kind, $"{Math.Round(ratio * 100):0}%");
	}

	private static void Report(GridWatch gw, Watched w, string kind, string detail)
	{
		w.LastEvent = s_now;
		MySlimBlock block = w.Block;
		string name = block.FatBlock is IMyTerminalBlock terminal ? terminal.CustomName : block.BlockDefinition.DisplayNameText;
		Alerts.Add(gw.Grid.EntityId, gw.Grid.DisplayName, kind, name, detail, gw.Grid.GridIntegerToWorld(block.Position));
	}

	// ---------------------------------------------------------------- when it's you

	private static void UpdateQuiet(IMyPlayer player)
	{
		s_controlledGroup.Clear();
		s_shipGrinding = false;
		s_playerPosition = player.GetPosition();
		s_holdingGrinder = player.Character?.EquippedTool is IMyAngleGrinder;
		if (!(MyAPIGateway.Session.ControlledObject is IMyCubeBlock controlled) || controlled.CubeGrid == null)
		{
			return;
		}
		s_groupGrids.Clear();
		MyAPIGateway.GridGroups.GetGroup(controlled.CubeGrid, GridLinkTypeEnum.Mechanical, s_groupGrids);
		foreach (IMyCubeGrid grid in s_groupGrids)
		{
			s_controlledGroup.Add(grid.EntityId);
			if (!s_shipGrinding && grid is MyCubeGrid cubeGrid)
			{
				foreach (MyCubeBlock fat in cubeGrid.GetFatBlocks())
				{
					if (fat is IMyShipGrinder grinder && grinder.Enabled && grinder.IsFunctional)
					{
						s_shipGrinding = true;
						s_shipSphere = controlled.CubeGrid.WorldVolume;
						break;
					}
				}
			}
		}
		s_groupGrids.Clear();
	}

	/// <summary>True when the change is most likely your own doing, so it isn't reported.</summary>
	private static bool IsQuiet(GridWatch gw, MySlimBlock block, bool grinding)
	{
		if (Settings.QuietWhenPiloting && s_controlledGroup.Contains(gw.Grid.EntityId))
		{
			return true;
		}
		if (!grinding || !Settings.QuietWhenGrinding)
		{
			return false;
		}
		Vector3D position = gw.Grid.GridIntegerToWorld(block.Position);
		if (s_holdingGrinder && Vector3D.DistanceSquared(position, s_playerPosition) < GrindQuietRange * GrindQuietRange)
		{
			return true;
		}
		return s_shipGrinding && Vector3D.Distance(position, s_shipSphere.Center) < s_shipSphere.Radius + GrindQuietRange;
	}

	public static void Unload()
	{
		foreach (GridWatch gw in s_order.ToArray())
		{
			Remove(gw);
		}
		s_grids.Clear();
		s_order.Clear();
		s_controlledGroup.Clear();
		s_orderCursor = 0;
		s_nextRescan = 0;
		s_nextQuiet = 0;
	}
}
