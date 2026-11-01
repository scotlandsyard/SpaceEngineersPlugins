using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.World;
using VRage.Game;

namespace Tally;

/// <summary>One item of the bill: how much is needed, how much you have (when that's taken off) and what's left to get.</summary>
internal class Line
{
	public MyDefinitionId Id;

	public string Name;

	public double Needed;

	public double Have;

	public double Short => Math.Max(0.0, Needed - Have);
}

/// <summary>A worked-out bill of materials, ready for the window, the datapad and the clipboard.</summary>
internal class Bill
{
	public string Name;

	/// <summary>"Full cost" or "Still needed".</summary>
	public string Mode;

	public string SourceKind;

	/// <summary>Which source it came from (Source.Key), so the window can pick it again.</summary>
	public string SourceKey;

	public int Blocks;

	/// <summary>Blocks needing nothing more (only counted for Still needed).</summary>
	public int Done;

	/// <summary>Blueprint blocks this world doesn't have (a mod that isn't loaded).</summary>
	public int Unknown;

	/// <summary>Empty when nothing was taken off, else who it was taken from ("you", "you and the grid's cargo").</summary>
	public string HaveFrom = "";

	public bool Subtracted => HaveFrom.Length > 0;

	public float Assembler;

	public float Yield;

	public int Modules;

	public List<Line> Components = new List<Line>();

	public List<Line> Ingots = new List<Line>();

	public List<Line> Ores = new List<Line>();

	/// <summary>Items the game has no assembler or refinery recipe for, with the amount that couldn't be broken down.</summary>
	public List<Line> NoRecipe = new List<Line>();

	public DateTime Made = DateTime.Now;
}

/// <summary>
/// The maths, the same as Drydock's Materials tab: block components from the block definitions; components to
/// ingots with the assembler recipe, divided by the world's assembler efficiency unless the recipe ignores it
/// (MyAssembler.GetEfficiencyMultiplierForBlueprint); ingots to ore with the refinery recipe, times the refinery's
/// MaterialEfficiency and its yield modules (MyRefinery). Everything comes from the definitions the game has loaded,
/// mods included.
/// </summary>
internal static class Materials
{
	/// <summary>What one vanilla yield module multiplies a refinery's Effectiveness by.</summary>
	public const double YieldModule = 1.0905077;

	public const int MaxModules = 4;

	private static Dictionary<string, MyBlueprintDefinitionBase> s_component;

	private static Dictionary<string, MyBlueprintDefinitionBase> s_ingot;

	private static float s_refinery = 1f;

	public static void Unload()
	{
		s_component = null;
		s_ingot = null;
	}

	/// <summary>The best refinery's base yield in this world (MaterialEfficiency), 1 = 100%.</summary>
	public static float RefineryYield
	{
		get
		{
			Load();
			return s_refinery;
		}
	}

	private static bool Is(MyDefinitionId id, string type)
	{
		return id.TypeId.ToString().EndsWith("_" + type, StringComparison.Ordinal);
	}

	public static bool IsComponent(MyDefinitionId id) => Is(id, "Component");

	public static bool IsIngot(MyDefinitionId id) => Is(id, "Ingot");

	public static bool IsOre(MyDefinitionId id) => Is(id, "Ore");

	/// <summary>Picks one recipe per component and per ingot, once a session (definitions don't change in a session).</summary>
	private static void Load()
	{
		if (s_component != null)
		{
			return;
		}
		List<MyBlueprintDefinitionBase> all = new List<MyBlueprintDefinitionBase>();
		HashSet<MyDefinitionId> seen = new HashSet<MyDefinitionId>();
		foreach (MyBlueprintDefinitionBase blueprint in MyDefinitionManager.Static.GetBlueprintDefinitions())
		{
			if (blueprint?.Results != null && blueprint.Prerequisites != null && seen.Add(blueprint.Id))
			{
				all.Add(blueprint);
			}
		}

		// A component's assembler recipe: one result, preferring the blueprint named after it, then a public one.
		Dictionary<string, MyBlueprintDefinitionBase> component = new Dictionary<string, MyBlueprintDefinitionBase>(StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, MyBlueprintDefinitionBase> group in all.Where(b => b.Results.Length == 1 && IsComponent(b.Results[0].Id) && (double)b.Results[0].Amount > 0).GroupBy(b => b.Results[0].Id.SubtypeName, StringComparer.OrdinalIgnoreCase))
		{
			component[group.Key] = group.OrderByDescending(b => (b.Id.SubtypeName.Equals(group.Key, StringComparison.OrdinalIgnoreCase) ? 2 : 0) + (b.Public ? 1 : 0)).First();
		}

		// An ingot's refinery recipe: one ore in, preferring the ore of the same name, a single result and the full
		// refinery recipe over the survival kit's "Basic" one.
		Dictionary<string, MyBlueprintDefinitionBase> ingot = new Dictionary<string, MyBlueprintDefinitionBase>(StringComparer.OrdinalIgnoreCase);
		foreach (MyBlueprintDefinitionBase blueprint in all.Where(b => b.Prerequisites.Length == 1 && IsOre(b.Prerequisites[0].Id)))
		{
			foreach (MyBlueprintDefinitionBase.Item result in blueprint.Results.Where(r => IsIngot(r.Id) && (double)r.Amount > 0))
			{
				string sub = result.Id.SubtypeName;
				if (!ingot.TryGetValue(sub, out MyBlueprintDefinitionBase best) || Score(blueprint, sub) > Score(best, sub))
				{
					ingot[sub] = blueprint;
				}
			}
		}

		float refinery = 0f;
		foreach (MyRefineryDefinition definition in MyDefinitionManager.Static.GetDefinitionsOfType<MyRefineryDefinition>())
		{
			if (definition.Public && definition.MaterialEfficiency > refinery)
			{
				refinery = definition.MaterialEfficiency;
			}
		}
		s_refinery = refinery > 0f ? refinery : 1f;
		s_ingot = ingot;
		s_component = component;
	}

	private static int Score(MyBlueprintDefinitionBase blueprint, string ingot)
	{
		return (blueprint.Prerequisites[0].Id.SubtypeName.Equals(ingot, StringComparison.OrdinalIgnoreCase) ? 4 : 0)
			+ (blueprint.Results.Length == 1 ? 2 : 0)
			+ (blueprint.Id.SubtypeName.IndexOf("basic", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1);
	}

	public static string NameOf(MyDefinitionId id)
	{
		MyPhysicalItemDefinition definition = MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id);
		string name = definition?.DisplayNameText;
		return string.IsNullOrWhiteSpace(name) ? id.SubtypeName : name;
	}

	/// <summary>
	/// Works out the bill from a component count. With <paramref name="have"/>, what you have is taken off at each
	/// step: components you have aren't made, so their ingots aren't needed, and so on down to the ore.
	/// </summary>
	public static Bill Compute(Count count, Dictionary<MyDefinitionId, double> have, string haveFrom, int modules)
	{
		Load();
		Bill bill = new Bill
		{
			Name = count.Name,
			Mode = count.StillNeeded ? "Still needed" : "Full cost",
			SourceKind = count.Kind,
			Blocks = count.Blocks,
			Done = count.Done,
			Unknown = count.Unknown,
			HaveFrom = have == null ? "" : haveFrom,
			Assembler = MySession.Static?.AssemblerEfficiencyMultiplier ?? 1f,
			Yield = s_refinery,
			Modules = Math.Max(0, Math.Min(MaxModules, modules))
		};
		double Has(MyDefinitionId id) => have != null && have.TryGetValue(id, out double amount) ? amount : 0.0;

		bill.Components = count.Components.Where(c => c.Value > 0).Select(c => new Line { Id = c.Key, Name = NameOf(c.Key), Needed = c.Value, Have = Has(c.Key) }).ToList();

		Dictionary<MyDefinitionId, double> raw = new Dictionary<MyDefinitionId, double>();
		Dictionary<MyDefinitionId, double> ores = new Dictionary<MyDefinitionId, double>();
		Dictionary<MyDefinitionId, double> missing = new Dictionary<MyDefinitionId, double>();
		double assembler = Math.Max(bill.Assembler, 1e-6);

		// Components made from other components are expanded too (depth-limited against recipe loops).
		void Assemble(MyDefinitionId id, double amount, int depth)
		{
			if (amount <= 0)
			{
				return;
			}
			if (depth > 8 || !s_component.TryGetValue(id.SubtypeName, out MyBlueprintDefinitionBase recipe))
			{
				Add(missing, id, amount);
				return;
			}
			double made = (double)recipe.Results[0].Amount;
			double multiplier = recipe.IgnoreEfficiencyMultiplier ? 1.0 : 1.0 / assembler;
			foreach (MyBlueprintDefinitionBase.Item item in recipe.Prerequisites)
			{
				double need = amount * (double)item.Amount * multiplier / made;
				if (IsComponent(item.Id))
				{
					Assemble(item.Id, need, depth + 1);
				}
				else if (IsOre(item.Id))
				{
					Add(ores, item.Id, need);
				}
				else
				{
					Add(raw, item.Id, need);
				}
			}
		}

		foreach (Line line in bill.Components)
		{
			Assemble(line.Id, bill.Subtracted ? line.Short : line.Needed, 0);
		}
		bill.Ingots = raw.Select(r => new Line { Id = r.Key, Name = NameOf(r.Key), Needed = r.Value, Have = Has(r.Key) }).ToList();

		double yield = Math.Max(bill.Yield, 1e-6) * Math.Pow(YieldModule, bill.Modules);
		foreach (Line line in bill.Ingots)
		{
			if (!IsIngot(line.Id))
			{
				continue;
			}
			double amount = bill.Subtracted ? line.Short : line.Needed;
			if (!s_ingot.TryGetValue(line.Id.SubtypeName, out MyBlueprintDefinitionBase recipe))
			{
				Add(missing, line.Id, amount);
				continue;
			}
			double made = recipe.Results.Where(r => IsIngot(r.Id) && r.Id.SubtypeName.Equals(line.Id.SubtypeName, StringComparison.OrdinalIgnoreCase)).Sum(r => (double)r.Amount);
			MyBlueprintDefinitionBase.Item ore = recipe.Prerequisites[0];
			Add(ores, ore.Id, amount * (double)ore.Amount / (made * yield));
		}
		bill.Ores = ores.Select(o => new Line { Id = o.Key, Name = NameOf(o.Key), Needed = o.Value, Have = Has(o.Key) }).ToList();
		bill.NoRecipe = missing.Select(m => new Line { Id = m.Key, Name = NameOf(m.Key), Needed = m.Value }).ToList();

		Comparison<Line> byAmount = (a, b) => b.Needed != a.Needed ? b.Needed.CompareTo(a.Needed) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
		bill.Components.Sort(byAmount);
		bill.Ingots.Sort(byAmount);
		bill.Ores.Sort(byAmount);
		bill.NoRecipe.Sort(byAmount);
		return bill;
	}

	private static void Add(Dictionary<MyDefinitionId, double> map, MyDefinitionId id, double amount)
	{
		map.TryGetValue(id, out double old);
		map[id] = old + amount;
	}
}
