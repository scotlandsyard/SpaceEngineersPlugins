using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using VRage.Game;

namespace FatAlbert;

/// <summary>
/// The Cargo fill view: what the ship weighs with its cargo space 0, 25, 50, 75 and 100% full of one kind of
/// item, and the thrust it needs to leave the planet at each weight. Fill is by volume (the game's cargo is limited
/// by volume), so a mix is just a density. The ship without its cargo, then plus each fill, is run through the same
/// climb as the Lift-off check.
/// </summary>
internal static class Cargo
{
	public class Mix
	{
		public string Name;

		/// <summary>Kilograms per litre; 0 for "what's in the cargo now" when the cargo is empty.</summary>
		public double KgPerLitre;

		public bool IsCurrent;
	}

	/// <summary>One row of the table.</summary>
	public class Row
	{
		/// <summary>0..1 of the cargo volume; the Now row uses what's in there.</summary>
		public double Fill;

		public string Label;

		/// <summary>What the cargo adds to the ship's weight, in kg.</summary>
		public double Weight;

		public double ShipMass;

		public Ascent.ThrustNeed Need;
	}

	/// <summary>Everything the table needs, worked out on a background thread.</summary>
	public class Result
	{
		public string Key;

		public Mix Mix;

		public double KgPerLitre;

		/// <summary>The ship without its cargo, in kg.</summary>
		public double BaseMass;

		public readonly List<Row> Rows = new List<Row>();
	}

	// Ore, one common ingot, one common component and the densest ingot: low, middle and high ends of what ships carry.
	private static readonly (string Name, string Id, double Fallback)[] Known =
	{
		("Ore (any: all ores and ice)", "MyObjectBuilder_Ore/Iron", 2.7),
		("Iron ingots", "MyObjectBuilder_Ingot/Iron", 7.87),
		("Steel plates", "MyObjectBuilder_Component/SteelPlate", 6.67),
		("Platinum ingots (densest)", "MyObjectBuilder_Ingot/Platinum", 21.4)
	};

	private static List<Mix> s_mixes;

	public static void Clear()
	{
		s_mixes = null;
	}

	/// <summary>The kinds of cargo to pick from; the last is whatever is in the cargo now. Reads definitions, so on the game thread.</summary>
	public static List<Mix> Mixes()
	{
		if (s_mixes != null)
		{
			return s_mixes;
		}
		List<Mix> mixes = new List<Mix>();
		foreach ((string name, string id, double fallback) in Known)
		{
			mixes.Add(new Mix { Name = name, KgPerLitre = Density(id, fallback) });
		}
		mixes.Add(new Mix { Name = "What's in the cargo now", IsCurrent = true });
		s_mixes = mixes;
		return mixes;
	}

	/// <summary>Item mass over item volume, in kg per litre, from the item's own definition (so modded worlds are right).</summary>
	private static double Density(string id, double fallback)
	{
		try
		{
			if (MyDefinitionId.TryParse(id, out MyDefinitionId definitionId) && MyDefinitionManager.Static.TryGetPhysicalItemDefinition(definitionId, out MyPhysicalItemDefinition item) && item.Mass > 0f && item.Volume > 0f)
			{
				// Volume is in cubic metres, 1000 litres each.
				return item.Mass / (item.Volume * 1000.0);
			}
		}
		catch (Exception)
		{
		}
		return fallback;
	}

	/// <summary>The kg per litre a mix means for this ship; the "now" mix is the ship's own cargo, or ore density when it's empty.</summary>
	public static double Density(Mix mix, ShipSnapshot ship, out bool fellBack)
	{
		fellBack = false;
		if (!mix.IsCurrent)
		{
			return mix.KgPerLitre;
		}
		if (ship.CargoUsedVolume > 1e-6 && ship.CargoItemMass > 0.0)
		{
			return ship.CargoItemMass / (ship.CargoUsedVolume * 1000.0);
		}
		fellBack = true;
		return Mixes()[0].KgPerLitre;
	}

	/// <summary>The fills to show: what's in there now, then 0 to 100%, then the one typed in (if it is a percentage).</summary>
	public static List<(string Label, double Fill)> Fills(ShipSnapshot ship, double? customPercent)
	{
		List<(string, double)> fills = new List<(string, double)>();
		double now = ship.CargoVolume > 0.0 ? Math.Min(1.0, ship.CargoUsedVolume / ship.CargoVolume) : 0.0;
		fills.Add(($"Now ({now * 100.0:0}%)", now));
		foreach (int percent in new[] { 0, 25, 50, 75, 100 })
		{
			fills.Add(($"{percent}%", percent / 100.0));
		}
		if (customPercent.HasValue && customPercent.Value >= 0.0 && customPercent.Value <= 100.0)
		{
			fills.Add(($"{customPercent.Value:0.#}% (yours)", customPercent.Value / 100.0));
		}
		return fills;
	}

	/// <summary>Runs every row. Plain maths on the snapshot, so it can run on a background thread.</summary>
	public static Result Compute(ShipSnapshot ship, AscentPlan plan, Mix mix, double kgPerLitre, double? customPercent, string key)
	{
		Result result = new Result { Key = key, Mix = mix, KgPerLitre = kgPerLitre };
		result.BaseMass = Math.Max(0.0, ship.Mass - ship.CargoWeight);
		double multiplier = Math.Max(1.0, ship.InventoryMultiplier);
		bool first = true;
		foreach ((string label, double fill) in Fills(ship, customPercent))
		{
			Row row = new Row { Fill = fill, Label = label };
			if (first)
			{
				// The ship as it is: its real mass, whatever is in the cargo.
				row.Weight = ship.CargoWeight;
				row.ShipMass = ship.Mass;
			}
			else
			{
				// Litres of cargo × kg per litre is real item mass; the game adds item mass ÷ the inventory multiplier to the ship.
				row.Weight = fill * ship.CargoVolume * 1000.0 * kgPerLitre / multiplier;
				row.ShipMass = result.BaseMass + row.Weight;
			}
			row.Need = Ascent.Need(ship, plan, row.ShipMass);
			first = false;
			result.Rows.Add(row);
		}
		return result;
	}
}
