using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;

namespace DrillSergeant;

/// <summary>A rig's blocks as they are on this PC right now, looked up by entity id (blocks that stream out and back in are new objects with the same id).</summary>
internal sealed class RigParts
{
	public IMyMotorStator Rotor;

	public readonly List<IMyPistonBase> Horizontal = new List<IMyPistonBase>();

	public readonly List<IMyPistonBase> Vertical = new List<IMyPistonBase>();

	/// <summary>Vertical pistons that extend upwards: they start extended and retract to go down.</summary>
	public readonly HashSet<long> Inverted = new HashSet<long>();

	public readonly List<IMyShipDrill> Drills = new List<IMyShipDrill>();

	public readonly List<IMyTerminalBlock> Cargo = new List<IMyTerminalBlock>();

	/// <summary>Names of the blocks that are gone, broken or not yours to use.</summary>
	public readonly List<string> Problems = new List<string>();

	public int Missing;

	public static RigParts Resolve(Rig rig)
	{
		RigParts parts = new RigParts { Rotor = RigBlocks.Get<IMyMotorStator>(rig.RotorId) };
		if (parts.Rotor == null)
		{
			return parts;
		}
		Check(parts, parts.Rotor, true);
		foreach (RigPiston entry in rig.Pistons)
		{
			if (entry.Role == PistonRole.Unused)
			{
				continue;
			}
			IMyPistonBase piston = RigBlocks.Get<IMyPistonBase>(entry.Id);
			if (piston == null)
			{
				parts.Missing++;
				continue;
			}
			Check(parts, piston, true);
			if (entry.Role == PistonRole.Horizontal)
			{
				parts.Horizontal.Add(piston);
			}
			else
			{
				parts.Vertical.Add(piston);
				if (entry.Role == PistonRole.VerticalInverted)
				{
					parts.Inverted.Add(piston.EntityId);
				}
			}
		}
		foreach (long id in rig.DrillIds)
		{
			IMyShipDrill drill = RigBlocks.Get<IMyShipDrill>(id);
			if (drill == null)
			{
				parts.Missing++;
				continue;
			}
			Check(parts, drill, false);
			parts.Drills.Add(drill);
		}
		foreach (long id in rig.CargoIds)
		{
			IMyTerminalBlock cargo = RigBlocks.Get<IMyTerminalBlock>(id);
			if (cargo != null && cargo.HasInventory)
			{
				parts.Cargo.Add(cargo);
			}
		}
		return parts;
	}

	private static void Check(RigParts parts, IMyTerminalBlock block, bool needsWorking)
	{
		if (!block.HasLocalPlayerAccess())
		{
			parts.Problems.Add(block.CustomName + " (no access)");
		}
		else if (!block.IsFunctional)
		{
			parts.Problems.Add(block.CustomName + " (damaged)");
		}
		else if (needsWorking && block is IMyFunctionalBlock functional && functional.Enabled && !functional.IsWorking)
		{
			parts.Problems.Add(block.CustomName + " (no power)");
		}
	}

	public bool Loaded => Rotor != null;

	public IEnumerable<IMyTerminalBlock> Moving => new IMyTerminalBlock[] { Rotor }.Concat(Horizontal).Concat(Vertical);

	public double HorizontalRange => Horizontal.Sum(p => p.HighestPosition - p.LowestPosition);

	public double VerticalRange => Vertical.Sum(p => p.HighestPosition - p.LowestPosition);

	/// <summary>How far down the vertical pistons are right now, metres.</summary>
	public double Depth => Vertical.Sum(p => Inverted.Contains(p.EntityId) ? p.HighestPosition - p.CurrentPosition : p.CurrentPosition - p.LowestPosition);

	/// <summary>Fill of the cargo the rig watches (its drills when none is picked), 0..1; -1 when there is nothing to measure.</summary>
	public double CargoFill()
	{
		double current = 0;
		double max = 0;
		IEnumerable<IMyTerminalBlock> blocks = Cargo.Count > 0 ? Cargo : Drills.Cast<IMyTerminalBlock>();
		foreach (IMyTerminalBlock block in blocks)
		{
			IMyInventory inventory = block.GetInventory(0);
			if (inventory != null)
			{
				current += (double)inventory.CurrentVolume;
				max += (double)inventory.MaxVolume;
			}
		}
		return max > 0 ? current / max : -1;
	}
}
