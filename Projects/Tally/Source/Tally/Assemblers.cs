using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using TimShared;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Tally;

/// <summary>An assembler or a group of them to queue a bill in.</summary>
internal class Target
{
	public string Label;

	/// <summary>Stays the same while the grid and group do, so the window can pick it again.</summary>
	public string Key;

	public long GridId;

	public List<long> AssemblerIds = new List<long>();
}

/// <summary>One order: so many runs of a blueprint on one assembler.</summary>
internal class Order
{
	public MyAssembler Assembler;

	public MyBlueprintDefinitionBase Blueprint;

	public MyDefinitionId Component;

	public string Name;

	public int Runs;

	/// <summary>Components the runs make.</summary>
	public double Makes;
}

/// <summary>What queuing a bill would do, worked out before anything is sent.</summary>
internal class QueuePlan
{
	public Target Target;

	public List<MyAssembler> Assemblers = new List<MyAssembler>();

	public List<Order> Orders = new List<Order>();

	/// <summary>Components left out, with why.</summary>
	public List<string> Skipped = new List<string>();

	/// <summary>Components already covered by what's queued or in the cargo.</summary>
	public List<string> Covered = new List<string>();

	/// <summary>Assemblers left out (disassembling, broken) and other things worth a warning.</summary>
	public List<string> Warnings = new List<string>();

	/// <summary>Set when the server would refuse requests for these assemblers (see Reach): why, starting "out of reach".</summary>
	public string OutOfReach;

	public int Runs => Orders.Sum(o => o.Runs);
}

/// <summary>
/// Queues a bill's components in your assemblers, the same request the terminal's production screen sends when you
/// add something to a queue (MyProductionBlock.AddQueueItemRequest). The server checks the request: you need access
/// to the assembler and to be near its grid or connected to it by antenna. Nothing is ever put into an inventory.
/// Each component's order goes whole to the assembler with the shortest queue, as BaR Maid does; co-op assemblers
/// share it out from there. Tally never changes an assembler's mode, cooperative switch or Custom Data.
/// </summary>
internal static class Assemblers
{
	/// <summary>The last queue sent this session, for Undo.</summary>
	internal static List<Order> LastQueue;

	private static Vector3D Here => MySession.Static?.LocalCharacter?.PositionComp.GetPosition() ?? MyAPIGateway.Session?.Camera?.Position ?? Vector3D.Zero;

	/// <summary>Assemblers you have access to, by grid: each group with an assembler first, then each one alone, nearest grid first.</summary>
	public static List<Target> Targets()
	{
		List<KeyValuePair<double, List<Target>>> found = new List<KeyValuePair<double, List<Target>>>();
		HashSet<IMyGridTerminalSystem> seen = new HashSet<IMyGridTerminalSystem>();
		foreach (MyCubeGrid grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(g => !g.Closed && !g.MarkedForClose && !g.IsPreview && g.Projector == null))
		{
			IMyGridTerminalSystem terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
			if (terminal == null || !seen.Add(terminal))
			{
				continue;
			}
			List<IMyAssembler> all = new List<IMyAssembler>();
			terminal.GetBlocksOfType(all, a => Usable(a as MyAssembler));
			if (all.Count == 0)
			{
				continue;
			}
			MyCubeGrid main = all.Select(a => (MyCubeGrid)a.CubeGrid).OrderByDescending(g => g.BlocksCount).First();
			Vector3D at = main.PositionComp.WorldAABB.Center;
			string where = $"on {main.DisplayName}, {Distance(at)}{(Reach.Check(main, out _) ? "" : ", out of reach")}";
			List<Target> targets = new List<Target>();
			List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
			terminal.GetBlockGroups(groups);
			foreach (IMyBlockGroup group in groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
			{
				List<IMyAssembler> members = new List<IMyAssembler>();
				group.GetBlocksOfType(members, a => Usable(a as MyAssembler));
				if (members.Count > 0)
				{
					targets.Add(new Target
					{
						Label = $"Group \"{group.Name}\" ({members.Count} assembler{(members.Count == 1 ? "" : "s")}) {where}",
						Key = "G:" + main.EntityId + ":" + group.Name,
						GridId = main.EntityId,
						AssemblerIds = members.Select(a => a.EntityId).ToList()
					});
				}
			}
			foreach (IMyAssembler assembler in all.OrderBy(a => a.CustomName, StringComparer.OrdinalIgnoreCase))
			{
				targets.Add(new Target
				{
					Label = $"{assembler.CustomName} {where}",
					Key = "A:" + assembler.EntityId,
					GridId = main.EntityId,
					AssemblerIds = new List<long> { assembler.EntityId }
				});
			}
			found.Add(new KeyValuePair<double, List<Target>>(Vector3D.DistanceSquared(Here, at), targets));
		}
		return found.OrderBy(f => f.Key).SelectMany(f => f.Value).ToList();
	}

	/// <summary>True when the two grids are docked or joined (one logical group), so their cargo is the same cargo.</summary>
	public static bool SameCargo(long gridA, long gridB)
	{
		if (gridA == 0 || gridB == 0 || !(MyEntities.GetEntityById(gridA) is MyCubeGrid a))
		{
			return false;
		}
		List<IMyCubeGrid> group = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(a, GridLinkTypeEnum.Logical, group);
		return gridA == gridB || group.Any(g => g.EntityId == gridB);
	}

	/// <summary>An assembler (survival kits too) you have access to.</summary>
	private static bool Usable(MyAssembler assembler)
	{
		return assembler != null && !assembler.Closed && assembler.HasLocalPlayerAccess();
	}

	private static string Distance(Vector3D at)
	{
		double metres = Vector3D.Distance(Here, at);
		return metres < 1000 ? $"{metres:0} m" : $"{metres / 1000:0.0} km";
	}

	/// <summary>
	/// Works out the orders for a bill's components (what's still to get, if something was taken off). With
	/// <paramref name="countQueued"/>, what the assemblers already have queued or finished in their output counts as on
	/// its way; with <paramref name="minusCargo"/>, what's in the inventories of the assemblers' grid (and what's docked
	/// to it) is taken off too.
	/// </summary>
	public static QueuePlan Plan(Bill bill, Target target, bool countQueued, bool minusCargo)
	{
		QueuePlan plan = new QueuePlan { Target = target };
		foreach (long id in target.AssemblerIds)
		{
			if (!(MyEntities.GetEntityById(id) is MyAssembler assembler) || !Usable(assembler))
			{
				continue;
			}
			string name = assembler.CustomName.ToString();
			if (!assembler.IsFunctional)
			{
				plan.Warnings.Add($"{name} is damaged, left out.");
			}
			else if (((IMyAssembler)assembler).Mode != Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly)
			{
				plan.Warnings.Add($"{name} is disassembling, left out (a queue there would take things apart).");
			}
			else
			{
				plan.Assemblers.Add(assembler);
				if (!assembler.Enabled)
				{
					plan.Warnings.Add($"{name} is off: its share waits until you turn it on.");
				}
			}
		}
		if (plan.Assemblers.Count == 0)
		{
			plan.Warnings.Add("No assembler there can take orders right now.");
			return plan;
		}
		foreach (MyCubeGrid assemblerGrid in plan.Assemblers.Select(a => a.CubeGrid).Distinct())
		{
			if (!Reach.Check(assemblerGrid, out string why))
			{
				plan.OutOfReach = char.ToUpperInvariant(why[0]) + why.Substring(1) + ". Move closer or link an antenna, then Refresh lists.";
				return plan;
			}
		}
		List<string> managed = plan.Assemblers.Where(a => !AssemblerModes.IsManual(a)).Select(a => $"{a.CustomName} ({AssemblerModes.Describe(AssemblerModes.Get(a))})").ToList();
		if (managed.Count > 0)
		{
			plan.Warnings.Add($"Set up for our plugins that queue on their own (Stockpile Manager, BaR Maid): {string.Join(", ", managed)}. They count Tally's order as already queued and may wait for it to finish before queuing their own. Tally won't change their modes.");
		}

		Dictionary<MyDefinitionId, double> cargo = new Dictionary<MyDefinitionId, double>();
		if (minusCargo && MyEntities.GetEntityById(target.GridId) is MyCubeGrid grid)
		{
			Sources.AddCargo(grid, cargo);
		}
		// Planned queue sizes, so each order goes to the assembler that's least busy after the ones before it.
		Dictionary<MyAssembler, double> load = plan.Assemblers.ToDictionary(a => a, a => a.Queue.Sum(q => (double)q.Amount));
		foreach (Line line in bill.Components)
		{
			double need = bill.Subtracted ? line.Short : line.Needed;
			if (need <= 1e-6)
			{
				continue;
			}
			cargo.TryGetValue(line.Id, out double inCargo);
			double onTheWay = countQueued ? Queued(plan.Assemblers, line.Id, !minusCargo) : 0.0;
			need -= inCargo + onTheWay;
			if (need <= 1e-6)
			{
				plan.Covered.Add(line.Name);
				continue;
			}
			MyAssembler best = null;
			MyBlueprintDefinitionBase bestBlueprint = null;
			foreach (MyAssembler assembler in plan.Assemblers)
			{
				MyBlueprintDefinitionBase blueprint = Recipes(line.Id).FirstOrDefault(b => assembler.CanUseBlueprint(b));
				if (blueprint != null && (best == null || load[assembler] < load[best]))
				{
					best = assembler;
					bestBlueprint = blueprint;
				}
			}
			if (best == null)
			{
				plan.Skipped.Add($"{line.Name} ({Text.Amount(need)}): {(Recipes(line.Id).Count == 0 ? "no assembler recipe in this world" : "these assemblers can't make it")}");
				continue;
			}
			double perRun = Makes(bestBlueprint, line.Id);
			int runs = (int)Math.Ceiling(need / perRun - 1e-6);
			load[best] += runs;
			plan.Orders.Add(new Order { Assembler = best, Blueprint = bestBlueprint, Component = line.Id, Name = line.Name, Runs = runs, Makes = runs * perRun });
		}
		return plan;
	}

	/// <summary>Blueprints that make just this component and that a production screen would offer: the usual one first.</summary>
	private static List<MyBlueprintDefinitionBase> Recipes(MyDefinitionId component)
	{
		MyBlueprintDefinitionBase usual = null;
		try
		{
			usual = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
		}
		catch (Exception)
		{
		}
		List<MyBlueprintDefinitionBase> all = MyDefinitionManager.Static.GetBlueprintDefinitions()
			.Where(b => b != null && b.Public && b.Enabled && b.Results != null && b.Results.Length == 1 && b.Results[0].Id == component && (double)b.Results[0].Amount > 0)
			.Distinct()
			.ToList();
		if (usual != null && all.Remove(usual))
		{
			all.Insert(0, usual);
		}
		return all;
	}

	private static double Makes(MyBlueprintDefinitionBase blueprint, MyDefinitionId component)
	{
		return blueprint.Results.Where(r => r.Id == component).Sum(r => (double)r.Amount);
	}

	/// <summary>How many of the component the assemblers have queued, and (with <paramref name="output"/>) finished in their output.</summary>
	private static double Queued(List<MyAssembler> assemblers, MyDefinitionId component, bool output)
	{
		double total = 0.0;
		foreach (MyAssembler assembler in assemblers)
		{
			foreach (MyProductionBlock.QueueItem item in assembler.Queue)
			{
				if (item.Blueprint?.Results != null && item.Blueprint.Results.Length == 1 && item.Blueprint.Results[0].Id == component)
				{
					total += (double)item.Amount * (double)item.Blueprint.Results[0].Amount;
				}
			}
			if (output && ((IMyAssembler)assembler).OutputInventory is IMyInventory inventory)
			{
				total += (double)inventory.GetItemAmount(component);
			}
		}
		return total;
	}

	/// <summary>
	/// What each assembler had queued just before Tally's last queue, by queue entry (ItemId): Undo only takes away
	/// what was added on top of that, so it never cuts into someone else's order.
	/// </summary>
	private static Dictionary<long, Dictionary<uint, double>> s_queuedBefore = new Dictionary<long, Dictionary<uint, double>>();

	/// <summary>
	/// Sends the orders, unless the server would refuse them (out of reach: then nothing is sent and
	/// <paramref name="why"/> says so). Returns the queue totals before, to check a few seconds later that they arrived.
	/// </summary>
	public static Dictionary<long, double> Send(QueuePlan plan, out string why)
	{
		why = null;
		foreach (MyCubeGrid grid in plan.Orders.Select(o => o.Assembler.CubeGrid).Distinct())
		{
			if (!Reach.Check(grid, out why))
			{
				return null;
			}
		}
		Dictionary<long, double> before = Watch(plan.Orders);
		s_queuedBefore = plan.Orders.Select(o => o.Assembler).Distinct().ToDictionary(a => a.EntityId, a => Snapshot(a));
		foreach (Order order in plan.Orders)
		{
			order.Assembler.AddQueueItemRequest(order.Blueprint, (MyFixedPoint)order.Runs);
		}
		LastQueue = plan.Orders.ToList();
		return before;
	}

	private static Dictionary<uint, double> Snapshot(MyAssembler assembler)
	{
		Dictionary<uint, double> amounts = new Dictionary<uint, double>();
		foreach (MyProductionBlock.QueueItem item in assembler.Queue)
		{
			amounts[item.ItemId] = (double)item.Amount;
		}
		return amounts;
	}

	/// <summary>Forgets the last queue (world unload).</summary>
	public static void Forget()
	{
		LastQueue = null;
		s_queuedBefore = new Dictionary<long, Dictionary<uint, double>>();
	}

	/// <summary>
	/// The total queued on the assemblers' whole grids (co-op assemblers take work out of the queue they were given),
	/// by grid. Compared after sending: a refused request adds nothing anywhere.
	/// </summary>
	public static Dictionary<long, double> Watch(List<Order> orders)
	{
		Dictionary<long, double> totals = new Dictionary<long, double>();
		foreach (MyCubeGrid grid in orders.Select(o => o.Assembler.CubeGrid).Distinct())
		{
			IMyGridTerminalSystem terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
			List<IMyAssembler> all = new List<IMyAssembler>();
			terminal?.GetBlocksOfType(all);
			totals[grid.EntityId] = all.OfType<MyAssembler>().Where(a => !a.Closed).Sum(a => a.Queue.Sum(q => (double)q.Amount));
		}
		return totals;
	}

	/// <summary>
	/// Takes the last queue back out: for each assembler, removes up to what Tally queued of each blueprint, from the end
	/// of its queue. What was already started or moved on to a co-op assembler can't be told apart from other work,
	/// so it stays. Only what was added on top of each queue entry since just before the queue counts (matched by the
	/// entry's ItemId in the queue as it is now), so other orders are never cut. Out of reach, nothing is sent and the
	/// queue is kept for another try: returns -1 and <paramref name="why"/>. Else returns the runs asked to be removed
	/// and the runs Tally had queued.
	/// </summary>
	public static int Undo(out int queued, out string why)
	{
		queued = LastQueue?.Sum(o => o.Runs) ?? 0;
		why = null;
		int removed = 0;
		if (LastQueue == null)
		{
			return 0;
		}
		foreach (MyCubeGrid grid in LastQueue.Where(o => !o.Assembler.Closed).Select(o => o.Assembler.CubeGrid).Distinct())
		{
			if (!Reach.Check(grid, out why))
			{
				return -1;
			}
		}
		foreach (IGrouping<MyAssembler, Order> perAssembler in LastQueue.Where(o => !o.Assembler.Closed).GroupBy(o => o.Assembler))
		{
			Dictionary<MyBlueprintDefinitionBase, int> left = perAssembler.GroupBy(o => o.Blueprint).ToDictionary(g => g.Key, g => g.Sum(o => o.Runs));
			s_queuedBefore.TryGetValue(perAssembler.Key.EntityId, out Dictionary<uint, double> before);
			List<MyProductionBlock.QueueItem> queue = perAssembler.Key.Queue.ToList();
			// From the end, so the indices still to come don't move.
			for (int i = queue.Count - 1; i >= 0; i--)
			{
				if (queue[i].Blueprint == null || !left.TryGetValue(queue[i].Blueprint, out int runs) || runs <= 0)
				{
					continue;
				}
				double had = before != null && before.TryGetValue(queue[i].ItemId, out double amount) ? amount : 0.0;
				int take = Math.Min(runs, (int)Math.Floor((double)queue[i].Amount - had + 1e-6));
				if (take <= 0)
				{
					continue;
				}
				perAssembler.Key.RemoveQueueItemRequest(i, (MyFixedPoint)take);
				left[queue[i].Blueprint] = runs - take;
				removed += take;
			}
		}
		LastQueue = null;
		return removed;
	}
}
