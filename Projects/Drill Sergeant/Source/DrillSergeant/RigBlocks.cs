using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace DrillSergeant;

/// <summary>Finds a rig's blocks, works out which side of the rotor they're on, and measures where the drills reach.</summary>
internal static class RigBlocks
{
	/// <summary>Rotors this far from you (or on the ship you control) show up in Setup.</summary>
	public const double SearchDistance = 150;

	public static T Get<T>(long id) where T : class
	{
		if (id == 0)
		{
			return null;
		}
		IMyEntity entity = MyAPIGateway.Entities.GetEntityById(id);
		return entity == null || entity.Closed ? null : entity as T;
	}

	public static bool IsHinge(IMyTerminalBlock block)
	{
		return block.BlockDefinition.SubtypeName.IndexOf("Hinge", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public static long MyIdentity => MyAPIGateway.Session?.Player?.IdentityId ?? 0;

	/// <summary>Rotors (not hinges) with a head, on grids near you, that you may use.</summary>
	public static List<IMyMotorStator> NearbyRotors()
	{
		List<IMyMotorStator> rotors = new List<IMyMotorStator>();
		Vector3D me = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		HashSet<IMyEntity> grids = new HashSet<IMyEntity>();
		MyAPIGateway.Entities.GetEntities(grids, e => e is IMyCubeGrid grid && grid.WorldAABB.DistanceSquared(me) <= SearchDistance * SearchDistance);
		IMyEntity controlled = MyAPIGateway.Session?.ControlledObject?.Entity?.GetTopMostParent();
		if (controlled is IMyCubeGrid controlledGrid)
		{
			grids.Add(controlledGrid);
		}
		foreach (IMyEntity entity in grids)
		{
			if (!(entity is MyCubeGrid grid))
			{
				continue;
			}
			foreach (MyCubeBlock block in grid.GetFatBlocks())
			{
				if (block is IMyMotorStator rotor && !IsHinge(rotor) && rotor.TopGrid != null && rotor.HasLocalPlayerAccess())
				{
					rotors.Add(rotor);
				}
			}
		}
		return rotors.Distinct().OrderBy(r => r.CustomName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>Every grid joined to the rotor by rotors, hinges and pistons.</summary>
	public static List<IMyCubeGrid> Construct(IMyMotorStator rotor)
	{
		List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(rotor.CubeGrid, GridLinkTypeEnum.Mechanical, grids);
		if (!grids.Contains(rotor.CubeGrid))
		{
			grids.Add(rotor.CubeGrid);
		}
		return grids;
	}

	/// <summary>The grids on the rotor's head side: everything that turns with it.</summary>
	public static HashSet<IMyCubeGrid> ArmGrids(IMyMotorStator rotor)
	{
		HashSet<IMyCubeGrid> arm = new HashSet<IMyCubeGrid>();
		if (rotor.TopGrid == null)
		{
			return arm;
		}
		List<IMyCubeGrid> construct = Construct(rotor);
		// Every mechanical joint but our rotor links two grids; walk them from the rotor's head.
		Dictionary<IMyCubeGrid, List<IMyCubeGrid>> links = new Dictionary<IMyCubeGrid, List<IMyCubeGrid>>();
		foreach (IMyCubeGrid grid in construct)
		{
			if (!(grid is MyCubeGrid cubeGrid))
			{
				continue;
			}
			foreach (MyCubeBlock block in cubeGrid.GetFatBlocks())
			{
				if (block is IMyMechanicalConnectionBlock joint && joint.EntityId != rotor.EntityId && joint.TopGrid != null)
				{
					Link(links, joint.CubeGrid, joint.TopGrid);
					Link(links, joint.TopGrid, joint.CubeGrid);
				}
			}
		}
		Queue<IMyCubeGrid> queue = new Queue<IMyCubeGrid>();
		queue.Enqueue(rotor.TopGrid);
		arm.Add(rotor.TopGrid);
		while (queue.Count > 0)
		{
			IMyCubeGrid grid = queue.Dequeue();
			if (!links.TryGetValue(grid, out List<IMyCubeGrid> next))
			{
				continue;
			}
			foreach (IMyCubeGrid other in next)
			{
				// The rotor's own base can be reached again through another joint (a rig on a closed loop); don't take it.
				if (other != rotor.CubeGrid && arm.Add(other))
				{
					queue.Enqueue(other);
				}
			}
		}
		return arm;
	}

	private static void Link(Dictionary<IMyCubeGrid, List<IMyCubeGrid>> links, IMyCubeGrid from, IMyCubeGrid to)
	{
		if (!links.TryGetValue(from, out List<IMyCubeGrid> list))
		{
			list = new List<IMyCubeGrid>();
			links[from] = list;
		}
		list.Add(to);
	}

	public static List<T> BlocksOn<T>(IEnumerable<IMyCubeGrid> grids) where T : class, IMyTerminalBlock
	{
		List<T> blocks = new List<T>();
		foreach (IMyCubeGrid grid in grids)
		{
			if (grid is MyCubeGrid cubeGrid)
			{
				foreach (MyCubeBlock block in cubeGrid.GetFatBlocks())
				{
					if (block is T typed)
					{
						blocks.Add(typed);
					}
				}
			}
		}
		return blocks.OrderBy(b => b.CustomName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	// ---------------------------------------------------------------- geometry

	/// <summary>Where a drill cuts: the centre and radius of its cutting sphere.</summary>
	public static void CutSphere(IMyShipDrill drill, out Vector3D centre, out double radius)
	{
		MyShipDrillDefinition definition = (drill as MyCubeBlock)?.BlockDefinition as MyShipDrillDefinition;
		radius = definition?.CutOutRadius ?? 1.9f;
		centre = drill.WorldMatrix.Translation + drill.WorldMatrix.Forward * (definition?.CutOutOffset ?? 2.8f);
	}

	private static double RadialDistance(IMyMotorStator rotor, Vector3D point)
	{
		Vector3D axis = rotor.WorldMatrix.Up;
		Vector3D offset = point - rotor.WorldMatrix.Translation;
		return (offset - axis * Vector3D.Dot(offset, axis)).Length();
	}

	public class Reachout
	{
		/// <summary>Outer edge of the cut, metres from the rotor axis.</summary>
		public double Outer;

		/// <summary>Inner edge of the cut.</summary>
		public double Inner;

		/// <summary>The largest drill's cutting radius.</summary>
		public double CutRadius;

		/// <summary>Direction the drills hang from the rotor along its axis ("down" for the vertical pistons).</summary>
		public Vector3D Down;
	}

	/// <summary>Measures the ring the drills cut right now. Null without drills.</summary>
	public static Reachout Measure(IMyMotorStator rotor, List<IMyShipDrill> drills)
	{
		if (drills.Count == 0)
		{
			return null;
		}
		Reachout result = new Reachout { Inner = double.MaxValue };
		Vector3D sum = Vector3D.Zero;
		foreach (IMyShipDrill drill in drills)
		{
			CutSphere(drill, out Vector3D centre, out double radius);
			double distance = RadialDistance(rotor, centre);
			result.Outer = Math.Max(result.Outer, distance + radius);
			result.Inner = Math.Min(result.Inner, Math.Max(0, distance - radius));
			result.CutRadius = Math.Max(result.CutRadius, radius);
			sum += centre;
		}
		Vector3D axis = rotor.WorldMatrix.Up;
		double along = Vector3D.Dot(sum / drills.Count - rotor.WorldMatrix.Translation, axis);
		result.Down = along >= 0 ? axis : -axis;
		return result;
	}

	/// <summary>Horizontal step from the width of the ring the drills cut, with a 10% overlap; at least 0.5 m.</summary>
	public static float AutoHorizontalStep(Reachout reach)
	{
		return reach == null ? 2.5f : Round(Math.Max(0.5, (reach.Outer - reach.Inner) * 0.9));
	}

	/// <summary>Vertical step from the drill's cutting radius (2.4 m for the large drill, near the script's 2.5 m).</summary>
	public static float AutoVerticalStep(Reachout reach)
	{
		return reach == null ? 2.5f : Round(Math.Max(0.5, reach.CutRadius * 1.3));
	}

	/// <summary>Down to 0.1 m: the script reads step lengths with more decimals through a rounding that nudges them up, so tenths keep both counting the same steps.</summary>
	private static float Round(double metres)
	{
		return (float)(Math.Floor(metres * 10 + 1e-6) / 10);
	}

	/// <summary>A first guess at a piston's job from which way it points.</summary>
	public static PistonRole GuessRole(IMyPistonBase piston, IMyMotorStator rotor, Vector3D down)
	{
		Vector3D direction = piston.WorldMatrix.Up;
		double along = Vector3D.Dot(direction, rotor.WorldMatrix.Up);
		if (Math.Abs(along) > 0.7)
		{
			return Vector3D.Dot(direction, down) >= 0 ? PistonRole.Vertical : PistonRole.VerticalInverted;
		}
		return Math.Abs(along) < 0.3 ? PistonRole.Horizontal : PistonRole.Unused;
	}

	/// <summary>Total horizontal extension right now, metres.</summary>
	public static double HorizontalExtension(IEnumerable<IMyPistonBase> horizontal)
	{
		return horizontal.Sum(p => p.CurrentPosition - p.LowestPosition);
	}

	public static bool IsMine(IMyTerminalBlock block)
	{
		return block.OwnerId == MyIdentity;
	}

	/// <summary>The block's tensor-sharing setting, or null when this world or this PC doesn't offer it (the game only shows it with Experimental mode on and the world allowing it).</summary>
	public static bool? GetTensor(IMyTerminalBlock block)
	{
		if (MyAPIGateway.Session?.SessionSettings?.EnableShareInertiaTensor != true)
		{
			return null;
		}
		ITerminalProperty property = block.GetProperty("ShareInertiaTensor");
		return property == null ? null : (bool?)block.GetValueBool("ShareInertiaTensor");
	}

	/// <summary>Sets tensor sharing the way the terminal checkbox does. False when it isn't offered here.</summary>
	public static bool SetTensor(IMyTerminalBlock block, bool on)
	{
		bool? current = GetTensor(block);
		if (current == null)
		{
			return false;
		}
		if (current.Value != on)
		{
			block.SetValueBool("ShareInertiaTensor", on);
		}
		return true;
	}
}
