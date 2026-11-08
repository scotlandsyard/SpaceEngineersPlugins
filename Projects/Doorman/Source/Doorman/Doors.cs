using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IngameDoor = Sandbox.ModAPI.Ingame.IMyDoor;
using IngameDoorStatus = Sandbox.ModAPI.Ingame.DoorStatus;

namespace Doorman;

/// <summary>
/// Finds the doors on the grids loaded on this PC that you own (or your faction owns, if ticked) and closes them once
/// they've been fully open for the delay.
///
/// A close is the terminal's own Open switch: <see cref="MyDoorBase.SetOpenRequest"/> with the block's owner, one
/// reliable request that the server checks for access, power and the block working, exactly as when you click it.
/// It never uses the ModAPI CloseDoor: on airtight doors that only changes the door on this PC.
/// The game tells a client nothing when a door opens, so the Open flag is read a few times a second; that's a bool
/// per door, with the door lists rebuilt only when a grid's block count changes.
/// </summary>
internal static class Doors
{
	private const double RescanInterval = 3.0;

	private const int CheckEveryFrames = 10;

	/// <summary>A door that opens again this soon after Doorman closed it counts as reopened.</summary>
	private const double ReopenWindow = 10.0;

	/// <summary>Reopened this fast, it's a timer or script, not a person: left alone at once.</summary>
	private const double FastReopen = 1.5;

	/// <summary>Reopened this many times in a row: left alone.</summary>
	private const int ReopensToBackOff = 2;

	/// <summary>Someone this close to the door when it reopens opened it by hand: that never counts as something fighting Doorman.</summary>
	private const double PersonRange = 5.0;

	/// <summary>A door left alone gets closed again once it has stayed shut this long.</summary>
	private const double BackOffQuiet = 60.0;

	/// <summary>A door left alone while it stays open is tried again after this, doubling up to <see cref="MaxRetryWait"/>.</summary>
	private const double RetryWait = 60.0;

	private const double MaxRetryWait = 600.0;

	/// <summary>No new request for the same door before this.</summary>
	private const double ResendAfter = 3.0;

	private const int MaxSends = 3;

	private const int MaxClosesPerSecond = 5;

	/// <summary>How far past the door block's cells someone still counts as standing in the doorway.</summary>
	private const double DoorwayMargin = 0.0;

	internal class Door
	{
		public MyDoorBase Block;

		public GridDoors Grid;

		public string Type;

		public bool Hangar;

		public bool Airlock;

		public double OpenSince = -1;

		public double ClosedSince = -1;

		public double SentAt = -1000;

		public int Sends;

		public bool Pending;

		public bool ClosedByUs;

		public int Reopens;

		public bool LeftAlone;

		public double LeftAloneSince;

		/// <summary>Times Doorman tried again on a door it left alone and it was reopened again; each doubles the wait.</summary>
		public int Retries;

		public string State = "";

		public string Name => Block.CustomName?.ToString() ?? Type;
	}

	internal class GridDoors
	{
		public MyCubeGrid Grid;

		public int BlockCount = -1;

		public bool Seen;

		public readonly List<Door> Doors = new List<Door>();

		public string Name => Grid.DisplayName ?? "";
	}

	private static readonly Dictionary<long, GridDoors> s_grids = new Dictionary<long, GridDoors>();

	private static readonly HashSet<IMyEntity> s_entities = new HashSet<IMyEntity>();

	private static readonly HashSet<long> s_airlockGroup = new HashSet<long>();

	private static readonly List<Sandbox.ModAPI.IMyTerminalBlock> s_groupBlocks = new List<Sandbox.ModAPI.IMyTerminalBlock>();

	private static readonly List<IMyEntity> s_inBox = new List<IMyEntity>();

	private static double s_nextRescan;

	private static int s_frame;

	private static double s_secondStart;

	private static int s_sentThisSecond;

	private static long s_me;

	public static int ClosesSent { get; private set; }

	public static IEnumerable<GridDoors> Grids => s_grids.Values;

	public static int DoorCount
	{
		get
		{
			int count = 0;
			foreach (GridDoors grid in s_grids.Values)
			{
				count += grid.Doors.Count;
			}
			return count;
		}
	}

	public static void Reset()
	{
		s_grids.Clear();
		s_nextRescan = 0;
		ClosesSent = 0;
		// Play time starts again in another world: a start time left from the last one would hold every close back.
		s_secondStart = 0;
		s_sentThisSecond = 0;
		s_frame = 0;
	}

	public static void RescanSoon()
	{
		s_nextRescan = 0;
	}

	public static void Update(double now)
	{
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (player == null)
		{
			return;
		}
		s_me = player.IdentityId;
		if (now >= s_nextRescan)
		{
			s_nextRescan = now + RescanInterval;
			Rescan();
		}
		if (++s_frame % CheckEveryFrames != 0)
		{
			return;
		}
		if (now - s_secondStart >= 1.0 || now < s_secondStart)
		{
			s_secondStart = now;
			s_sentThisSecond = 0;
		}
		foreach (GridDoors grid in s_grids.Values)
		{
			foreach (Door door in grid.Doors)
			{
				try
				{
					Check(door, now);
				}
				catch (Exception ex)
				{
					door.State = "Error";
					MyLog.Default.WriteLineAndConsole($"[Doorman] Check: {ex.Message}");
				}
			}
		}
	}

	// ---------------------------------------------------------------- which doors

	private static void Rescan()
	{
		foreach (GridDoors grid in s_grids.Values)
		{
			grid.Seen = false;
		}
		s_entities.Clear();
		MyAPIGateway.Entities.GetEntities(s_entities, e => e is MyCubeGrid);
		foreach (IMyEntity entity in s_entities)
		{
			MyCubeGrid grid = (MyCubeGrid)entity;
			if (grid.Closed || grid.MarkedForClose || grid.IsPreview || grid.Physics == null || !OwnsGrid(grid))
			{
				continue;
			}
			if (!s_grids.TryGetValue(grid.EntityId, out GridDoors gd) || gd.Grid != grid)
			{
				gd = new GridDoors { Grid = grid };
				s_grids[grid.EntityId] = gd;
			}
			gd.Seen = true;
			if (gd.BlockCount != grid.BlocksCount)
			{
				gd.BlockCount = grid.BlocksCount;
				Rebuild(gd);
			}
			MarkAirlocks(gd);
		}
		s_entities.Clear();
		List<long> gone = null;
		foreach (KeyValuePair<long, GridDoors> pair in s_grids)
		{
			if (!pair.Value.Seen || pair.Value.Doors.Count == 0)
			{
				(gone = gone ?? new List<long>()).Add(pair.Key);
			}
		}
		if (gone != null)
		{
			foreach (long id in gone)
			{
				s_grids.Remove(id);
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
		if (!Settings.FactionGrids || owners.Count == 0)
		{
			return false;
		}
		IMyFaction mine = MyAPIGateway.Session.Factions.TryGetPlayerFaction(s_me);
		return mine != null && MyAPIGateway.Session.Factions.TryGetPlayerFaction(owners[0])?.FactionId == mine.FactionId;
	}

	/// <summary>Rebuilds a grid's door list, keeping the state of doors that are still there.</summary>
	private static void Rebuild(GridDoors gd)
	{
		Dictionary<long, Door> old = new Dictionary<long, Door>();
		foreach (Door door in gd.Doors)
		{
			old[door.Block.EntityId] = door;
		}
		gd.Doors.Clear();
		foreach (MyDoorBase block in gd.Grid.GetFatBlocks<MyDoorBase>())
		{
			if (block.Closed || block.MarkedForClose)
			{
				continue;
			}
			if (!old.TryGetValue(block.EntityId, out Door door) || door.Block != block)
			{
				door = new Door { Block = block, Type = TypeName(block), Hangar = block is MyAirtightHangarDoor };
			}
			door.Grid = gd;
			gd.Doors.Add(door);
		}
	}

	private static string TypeName(MyDoorBase block)
	{
		if (block is MyAirtightHangarDoor)
		{
			return "Hangar door";
		}
		if (block is MyAirtightSlideDoor)
		{
			return "Sliding door";
		}
		if (block is MyAdvancedDoor)
		{
			return "Advanced door";
		}
		return "Door";
	}

	/// <summary>A door whose name, or a group it's in, says "airlock" is run by something else (a timer, a script).</summary>
	private static void MarkAirlocks(GridDoors gd)
	{
		s_airlockGroup.Clear();
		try
		{
			var groups = gd.Grid.GridSystems?.TerminalSystem?.BlockGroups;
			if (groups != null)
			{
				foreach (var group in groups)
				{
					if (group.Name.ToString().IndexOf("airlock", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					s_groupBlocks.Clear();
					((Sandbox.ModAPI.IMyBlockGroup)group).GetBlocks(s_groupBlocks);
					foreach (Sandbox.ModAPI.IMyTerminalBlock block in s_groupBlocks)
					{
						s_airlockGroup.Add(block.EntityId);
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Doorman] Groups: {ex.Message}");
		}
		s_groupBlocks.Clear();
		foreach (Door door in gd.Doors)
		{
			door.Airlock = s_airlockGroup.Contains(door.Block.EntityId) || door.Name.IndexOf("airlock", StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}

	// ---------------------------------------------------------------- closing

	private static void Check(Door door, double now)
	{
		MyDoorBase block = door.Block;
		if (block.Closed || block.MarkedForClose)
		{
			door.State = "Gone";
			return;
		}
		if (!block.Open)
		{
			if (door.ClosedSince < 0)
			{
				door.ClosedSince = now;
				door.ClosedByUs = door.Pending;
			}
			door.Pending = false;
			door.Sends = 0;
			door.OpenSince = -1;
			if (now - door.ClosedSince >= BackOffQuiet)
			{
				door.LeftAlone = false;
				door.Reopens = 0;
				door.Retries = 0;
			}
			door.State = door.LeftAlone ? "Closed (left alone for now)" : "Closed";
			return;
		}

		if (door.ClosedSince >= 0)
		{
			// It just opened. Right after Doorman closed it, with nobody near the door, means a timer or script wants it open.
			double shutFor = now - door.ClosedSince;
			if (door.ClosedByUs && shutFor < ReopenWindow && !SomeoneNear(block, PersonRange))
			{
				door.Reopens++;
				if (!door.LeftAlone && (shutFor < FastReopen || door.Reopens >= ReopensToBackOff))
				{
					door.LeftAlone = true;
					door.LeftAloneSince = now;
					if (door.Retries == 0)
					{
						Personality.Say("door_left_alone", "door", door.Name);
					}
				}
			}
			else
			{
				// Opened by someone standing there, or long after: start counting again.
				door.Reopens = 0;
			}
			door.ClosedSince = -1;
			door.ClosedByUs = false;
		}

		if (door.LeftAlone && now - door.LeftAloneSince >= Math.Min(MaxRetryWait, RetryWait * Math.Pow(2, door.Retries)))
		{
			// Left open since: maybe the timer or script has stopped. Try again; one quick reopen and it's left alone again, for longer.
			door.LeftAlone = false;
			door.Reopens = ReopensToBackOff - 1;
			door.Retries++;
		}
		string skip = SkipReason(door);
		if (skip != null)
		{
			door.OpenSince = -1;
			door.State = skip;
			return;
		}
		if (((IngameDoor)block).Status != IngameDoorStatus.Open)
		{
			door.State = "Opening";
			return;
		}
		if (door.OpenSince < 0)
		{
			door.OpenSince = now;
		}
		double left = door.OpenSince + Settings.DelayFor(door.Grid.Grid.EntityId, block.EntityId) - now;
		if (left > 0)
		{
			door.State = $"Open, closes in {Math.Ceiling(left):0} s";
			return;
		}
		if (door.Pending && now - door.SentAt < ResendAfter)
		{
			door.State = "Closing";
			return;
		}
		if (door.Sends >= MaxSends)
		{
			door.State = "Server didn't close it";
			return;
		}
		if (SomeoneNear(block, DoorwayMargin))
		{
			door.State = "Waiting: someone in the doorway";
			return;
		}
		if (s_sentThisSecond >= MaxClosesPerSecond)
		{
			door.State = "Closing soon";
			return;
		}
		// The terminal's Open switch: SetOpenRequest(false, OwnerId).
		block.SetOpenRequest(false, block.OwnerId);
		s_sentThisSecond++;
		ClosesSent++;
		door.SentAt = now;
		door.Sends++;
		door.Pending = true;
		door.State = "Closing";
		Personality.Say("door_closed", "door", door.Name, "grid", door.Grid.Name);
	}

	/// <summary>Why an open door is left as it is, or null if it gets closed after its delay.</summary>
	internal static string SkipReason(Door door)
	{
		MyDoorBase block = door.Block;
		DoorMode mode = Settings.Mode(block.EntityId);
		if (!Settings.Enabled)
		{
			return "Open (Doorman is off)";
		}
		if (mode == DoorMode.Never)
		{
			return "Open (never closed)";
		}
		if (mode != DoorMode.Always && door.Hangar && !Settings.HangarsFor(door.Grid.Grid.EntityId))
		{
			return "Open (hangar doors off)";
		}
		if (mode != DoorMode.Always && door.Airlock && Settings.SkipAirlocks)
		{
			return "Open (airlock)";
		}
		if (door.LeftAlone)
		{
			return "Open (left alone: something reopens it)";
		}
		if (!((Sandbox.ModAPI.IMyTerminalBlock)block).HasLocalPlayerAccess())
		{
			return "Open (no access)";
		}
		if (!block.Enabled || !block.IsWorking)
		{
			return "Open (off or no power)";
		}
		return null;
	}

	/// <summary>
	/// A living character whose middle is within this many metres of the door block's own cells, measured along the
	/// door's axes (so a ship at an angle doesn't make the box bigger). 0 means inside the door's cells: the doorway.
	/// </summary>
	private static bool SomeoneNear(MyDoorBase block, double metres)
	{
		BoundingBoxD box = block.PositionComp.WorldAABB.Inflate(metres + 1.0);
		Vector3D half = new Vector3D(block.BlockDefinition.Size) * block.CubeGrid.GridSize * 0.5 + new Vector3D(metres);
		BoundingBoxD cells = new BoundingBoxD(-half, half);
		MatrixD toDoor = block.PositionComp.WorldMatrixNormalizedInv;
		s_inBox.Clear();
		s_inBox.AddRange(MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref box));
		bool found = false;
		foreach (IMyEntity entity in s_inBox)
		{
			if (entity is IMyCharacter character && !character.IsDead && cells.Contains(Vector3D.Transform(character.WorldAABB.Center, toDoor)) != ContainmentType.Disjoint)
			{
				found = true;
				break;
			}
		}
		s_inBox.Clear();
		return found;
	}

	/// <summary>Lets a door that was left alone be closed again straight away (the window's Reset button, a mode change).</summary>
	internal static void Forgive(long doorId)
	{
		foreach (GridDoors grid in s_grids.Values)
		{
			foreach (Door door in grid.Doors)
			{
				if (door.Block.EntityId == doorId)
				{
					door.LeftAlone = false;
					door.Reopens = 0;
					door.Retries = 0;
					door.Sends = 0;
				}
			}
		}
	}
}
