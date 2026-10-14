using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using VRage.Game;

namespace SacrificialStockpileManager;

public enum ItemCategory
{
	Ore,
	Ingot,
	Component,
	Ammo,
	Tool,
	Bottle,
	Consumable,
	Other
}

/// <summary>
/// Item keys, names and categories. An item key is the definition id without the "MyObjectBuilder_" prefix,
/// e.g. "Ingot/Iron" or "Component/SteelPlate", the same form the reference scripts use.
/// </summary>
internal static class Items
{
	public const string TypePrefix = "MyObjectBuilder_";

	public static readonly ItemCategory[] Categories = (ItemCategory[])Enum.GetValues(typeof(ItemCategory));

	/// <summary>Plural names for lists and LCD headings, in the order of <see cref="ItemCategory"/>.</summary>
	public static readonly string[] CategoryNames = { "Ores", "Ingots", "Components", "Ammo", "Tools", "Bottles", "Consumables", "Other" };

	/// <summary>Short labels for the Accepts checkboxes, in the order of <see cref="ItemCategory"/>.</summary>
	public static readonly string[] CategoryShort = { "Ore", "Ingot", "Comp", "Ammo", "Tool", "Bottle", "Food", "Other" };

	private static readonly Dictionary<MyDefinitionId, string> s_keys = new Dictionary<MyDefinitionId, string>();

	private static readonly Dictionary<string, string> s_names = new Dictionary<string, string>();

	private static readonly Dictionary<string, MyBlueprintDefinitionBase> s_blueprints = new Dictionary<string, MyBlueprintDefinitionBase>();

	private static List<string> s_catalog;

	public static string Key(MyDefinitionId id)
	{
		if (!s_keys.TryGetValue(id, out string key))
		{
			string type = id.TypeId.ToString();
			if (type.StartsWith(TypePrefix, StringComparison.Ordinal))
			{
				type = type.Substring(TypePrefix.Length);
			}
			key = type + "/" + id.SubtypeName;
			s_keys[id] = key;
		}
		return key;
	}

	public static bool TryParse(string key, out MyDefinitionId id)
	{
		return MyDefinitionId.TryParse(TypePrefix + key, out id);
	}

	public static ItemCategory Category(string key)
	{
		int slash = key.IndexOf('/');
		string type = slash < 0 ? key : key.Substring(0, slash);
		switch (type)
		{
		case "Ore":
			return ItemCategory.Ore;
		case "Ingot":
			return ItemCategory.Ingot;
		case "Component":
			return ItemCategory.Component;
		case "AmmoMagazine":
			return ItemCategory.Ammo;
		case "PhysicalGunObject":
			return ItemCategory.Tool;
		case "OxygenContainerObject":
		case "GasContainerObject":
			return ItemCategory.Bottle;
		case "ConsumableItem":
			return ItemCategory.Consumable;
		default:
			return ItemCategory.Other;
		}
	}

	public static string Name(string key)
	{
		if (s_names.TryGetValue(key, out string name))
		{
			return name;
		}
		name = BaseName(key) ?? key.Substring(key.IndexOf('/') + 1);
		// Ore and ingot of the same element can share a display name (mods especially); keep them apart.
		ItemCategory category = Category(key);
		if (category == ItemCategory.Ore || category == ItemCategory.Ingot)
		{
			string subtype = key.Substring(key.IndexOf('/') + 1);
			string counterpart = (category == ItemCategory.Ore ? "Ingot/" : "Ore/") + subtype;
			if (string.Equals(BaseName(counterpart), name, StringComparison.OrdinalIgnoreCase))
			{
				name += category == ItemCategory.Ore ? " Ore" : " Ingot";
			}
		}
		s_names[key] = name;
		return name;
	}

	/// <summary>The item's display name, or null when the game has no such item.</summary>
	private static string BaseName(string key)
	{
		string name = Construct.OneLine(Definition(key)?.DisplayNameText);
		return name.Length == 0 ? null : name;
	}

	public static MyPhysicalItemDefinition Definition(string key)
	{
		return TryParse(key, out MyDefinitionId id) && MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out MyPhysicalItemDefinition definition) ? definition : null;
	}

	/// <summary>True for items that only come in whole numbers (components, ammo, tools).</summary>
	public static bool IsIntegral(string key)
	{
		return Definition(key)?.HasIntegralAmounts ?? true;
	}

	/// <summary>The blueprint an assembler uses to make the item, or null when there isn't one.</summary>
	public static MyBlueprintDefinitionBase Blueprint(string key)
	{
		if (s_blueprints.TryGetValue(key, out MyBlueprintDefinitionBase blueprint))
		{
			return blueprint;
		}
		blueprint = null;
		try
		{
			if (TryParse(key, out MyDefinitionId id))
			{
				blueprint = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(id);
			}
		}
		catch (Exception)
		{
		}
		s_blueprints[key] = blueprint;
		return blueprint;
	}

	/// <summary>How many of the item one run of the blueprint makes (1 for vanilla components).</summary>
	public static double BlueprintYield(MyBlueprintDefinitionBase blueprint, string key)
	{
		double total = 0.0;
		foreach (MyBlueprintDefinitionBase.Item result in blueprint.Results)
		{
			if (Key(result.Id) == key)
			{
				total += (double)result.Amount;
			}
		}
		return total > 0.0 ? total : 1.0;
	}

	private static Dictionary<string, List<KeyValuePair<string, double>>> s_refinedFrom;

	private static HashSet<string> s_assemblerMade;

	/// <summary>
	/// The ores a refinery turns into the item, each with how much of the item one unit of ore gives, best first.
	/// Empty for anything no refinery makes. Read from the refinery blocks' blueprint classes, so modded refineries
	/// and ores that give several ingots are covered.
	/// </summary>
	public static List<KeyValuePair<string, double>> RefinedFrom(string key)
	{
		if (s_refinedFrom == null)
		{
			s_refinedFrom = new Dictionary<string, List<KeyValuePair<string, double>>>();
			s_assemblerMade = new HashSet<string>();
			foreach (MyProductionBlockDefinition block in MyDefinitionManager.Static.GetAllDefinitions().OfType<MyProductionBlockDefinition>())
			{
				bool refinery = block is MyRefineryDefinition;
				bool assembler = block is MyAssemblerDefinition;
				if ((!refinery && !assembler) || block.BlueprintClasses == null)
				{
					continue;
				}
				foreach (MyBlueprintClassDefinition blueprintClass in block.BlueprintClasses)
				{
					foreach (MyBlueprintDefinitionBase blueprint in blueprintClass)
					{
						if (blueprint?.Results == null || blueprint.Prerequisites == null)
						{
							continue;
						}
						if (assembler)
						{
							foreach (MyBlueprintDefinitionBase.Item result in blueprint.Results)
							{
								s_assemblerMade.Add(Key(result.Id));
							}
							continue;
						}
						// A refinery blueprint: one ore in, one or more products out.
						MyBlueprintDefinitionBase.Item[] ores = blueprint.Prerequisites.Where(p => Category(Key(p.Id)) == ItemCategory.Ore && (double)p.Amount > 0.0).ToArray();
						if (ores.Length != 1)
						{
							continue;
						}
						string ore = Key(ores[0].Id);
						foreach (MyBlueprintDefinitionBase.Item result in blueprint.Results)
						{
							string product = Key(result.Id);
							double perOre = (double)result.Amount / (double)ores[0].Amount;
							if (!s_refinedFrom.TryGetValue(product, out List<KeyValuePair<string, double>> sources))
							{
								sources = new List<KeyValuePair<string, double>>();
								s_refinedFrom[product] = sources;
							}
							int existing = sources.FindIndex(s => s.Key == ore);
							if (existing < 0)
							{
								sources.Add(new KeyValuePair<string, double>(ore, perOre));
							}
							else if (perOre > sources[existing].Value)
							{
								sources[existing] = new KeyValuePair<string, double>(ore, perOre);
							}
						}
					}
				}
			}
			foreach (List<KeyValuePair<string, double>> sources in s_refinedFrom.Values)
			{
				sources.Sort((a, b) => b.Value.CompareTo(a.Value));
			}
		}
		return s_refinedFrom.TryGetValue(key, out List<KeyValuePair<string, double>> found) ? found : new List<KeyValuePair<string, double>>();
	}

	/// <summary>True for items only refineries make (ingots, gravel), so autocraft can't queue them in an assembler.</summary>
	public static bool RefineryOnly(string key)
	{
		return RefinedFrom(key).Count > 0 && !s_assemblerMade.Contains(key);
	}

	private static HashSet<string> s_refinable;

	/// <summary>True for ores some refinery blueprint turns into ingots (not ice, for example).</summary>
	public static bool IsRefinable(string key)
	{
		if (s_refinable == null)
		{
			s_refinable = new HashSet<string>();
			foreach (MyBlueprintDefinitionBase blueprint in MyDefinitionManager.Static.GetBlueprintDefinitions())
			{
				if (blueprint?.Prerequisites == null || blueprint.Results == null || !blueprint.Results.Any(r => Category(Key(r.Id)) == ItemCategory.Ingot))
				{
					continue;
				}
				foreach (MyBlueprintDefinitionBase.Item prerequisite in blueprint.Prerequisites)
				{
					string ore = Key(prerequisite.Id);
					if (Category(ore) == ItemCategory.Ore)
					{
						s_refinable.Add(ore);
					}
				}
			}
		}
		return s_refinable.Contains(key);
	}

	/// <summary>Every public item in the game, sorted by category and then name.</summary>
	public static List<string> Catalog
	{
		get
		{
			if (s_catalog == null)
			{
				s_catalog = MyDefinitionManager.Static.GetPhysicalItemDefinitions()
					.Where(d => d.Public)
					.Select(d => Key(d.Id))
					.Distinct()
					.OrderBy(k => (int)Category(k))
					.ThenBy(k => Name(k), StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
			return s_catalog;
		}
	}

	/// <summary>Whole numbers with separators; large ones shortened (12.5k, 3.40M).</summary>
	public static string Amount(double value)
	{
		double abs = Math.Abs(value);
		if (abs >= 10_000_000.0)
		{
			return (value / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M";
		}
		if (abs >= 1_000_000.0)
		{
			return (value / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + "M";
		}
		if (abs >= 100_000.0)
		{
			return (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
		}
		if (abs > 0.0 && abs < 1.0)
		{
			return value.ToString("0.00", CultureInfo.InvariantCulture);
		}
		return Math.Floor(value).ToString("N0", CultureInfo.InvariantCulture);
	}

	/// <summary>A volume in litres, short enough for a table column: 850 L, 3,400 L, 15.6k L, 1.25M L.</summary>
	public static string Litres(double litres)
	{
		if (litres >= 1_000_000.0)
		{
			return (litres / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + "M L";
		}
		if (litres >= 10_000.0)
		{
			return (litres / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k L";
		}
		return Math.Round(litres).ToString("N0", CultureInfo.InvariantCulture) + " L";
	}

	/// <summary>Reads an amount typed by the player: 500, 1,500, 2.5k, 1.2M.</summary>
	public static bool TryParseAmount(string text, out double value)
	{
		value = 0.0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string trimmed = text.Trim().Replace(",", "").Replace(" ", "");
		double multiplier = 1.0;
		char last = char.ToLowerInvariant(trimmed[trimmed.Length - 1]);
		if (last == 'k' || last == 'm')
		{
			multiplier = last == 'k' ? 1000.0 : 1_000_000.0;
			trimmed = trimmed.Substring(0, trimmed.Length - 1);
		}
		if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0.0 || double.IsNaN(value) || double.IsInfinity(value))
		{
			return false;
		}
		value *= multiplier;
		return true;
	}

	/// <summary>Encodes item amounts for storage: "Ingot/Iron=1200|Ore/Ice=35.5".</summary>
	public static string Encode(Dictionary<string, double> amounts)
	{
		StringBuilder text = new StringBuilder();
		foreach (KeyValuePair<string, double> entry in amounts)
		{
			if (entry.Value <= 0.0)
			{
				continue;
			}
			if (text.Length > 0)
			{
				text.Append('|');
			}
			text.Append(entry.Key).Append('=').Append(Math.Round(entry.Value, 3).ToString("R", CultureInfo.InvariantCulture));
		}
		return text.ToString();
	}

	public static Dictionary<string, double> Decode(string text)
	{
		Dictionary<string, double> amounts = new Dictionary<string, double>();
		if (string.IsNullOrEmpty(text))
		{
			return amounts;
		}
		foreach (string part in text.Split('|'))
		{
			int eq = part.LastIndexOf('=');
			if (eq > 0 && double.TryParse(part.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
			{
				string key = part.Substring(0, eq);
				amounts.TryGetValue(key, out double existing);
				amounts[key] = existing + amount;
			}
		}
		return amounts;
	}
}
