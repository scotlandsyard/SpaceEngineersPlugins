using System;
using System.Collections.Generic;
using System.Linq;

namespace SacrificialStockpileManager;

/// <summary>
/// The refinery priority a grid actually uses: ores for short ingot quotas first, then the player's own list.
///
/// How it combines, by design:
/// - Only the grid's own quotas, only while its Autocraft is on (quotas are what autocraft keeps up). Moving the ore
///   still needs Automation, like the player's own priorities.
/// - A quota is short when the grid holds less than it (refinery outputs included). The most short one, as a share
///   of its quota, comes first; ties go by the player's list, then by name.
/// - Each short item pulls up every ore that makes it, the one giving the most per unit of ore first (ties by the
///   player's list). An ore that makes several short items comes once, at the place of the most short of them.
/// - The player's list follows, without the ores already pulled up. Nothing in it is changed or saved: when the
///   quota is met, the ore drops back to where the player put it (or off the list).
/// Worked out once per engine pass and kept on the construct, so the Refinery priority view and the Items notes
/// show the same order the engine used.
/// </summary>
internal class RefineByNeed
{
	/// <summary>The ore order refineries follow this pass.</summary>
	public List<string> Priority = new List<string>();

	/// <summary>For each ore pulled up by need: the short items it makes, most short first.</summary>
	public Dictionary<string, List<string>> Reasons = new Dictionary<string, List<string>>();

	/// <summary>For each short refinery-made quota item: how much is missing.</summary>
	public Dictionary<string, double> Short = new Dictionary<string, double>();

	public bool IsNeed(string ore) => Reasons.ContainsKey(ore);

	public static RefineByNeed Compute(GridSnapshot grid, GridRules rules)
	{
		RefineByNeed plan = new RefineByNeed();
		List<string> manual = rules?.OrePriority ?? new List<string>();
		if (rules != null && rules.Autocraft && grid != null)
		{
			Dictionary<string, double> totals = Construct.StationTotals(grid);
			int Manual(string ore)
			{
				int rank = manual.IndexOf(ore);
				return rank < 0 ? int.MaxValue : rank;
			}
			List<KeyValuePair<string, double>> shortItems = new List<KeyValuePair<string, double>>();
			foreach (ItemLimit quota in rules.Quotas.Where(q => q.HasMin && q.Min > 0.0 && Items.RefineryOnly(q.Item)))
			{
				totals.TryGetValue(quota.Item, out double have);
				if (have < quota.Min - 1e-6)
				{
					plan.Short[quota.Item] = quota.Min - have;
					shortItems.Add(new KeyValuePair<string, double>(quota.Item, (quota.Min - have) / quota.Min));
				}
			}
			IEnumerable<string> byNeed = shortItems
				.OrderByDescending(s => s.Value)
				.ThenBy(s => Items.RefinedFrom(s.Key).Select(o => Manual(o.Key)).DefaultIfEmpty(int.MaxValue).Min())
				.ThenBy(s => Items.Name(s.Key), StringComparer.OrdinalIgnoreCase)
				.Select(s => s.Key);
			foreach (string item in byNeed)
			{
				foreach (KeyValuePair<string, double> ore in Items.RefinedFrom(item).OrderByDescending(o => o.Value).ThenBy(o => Manual(o.Key)))
				{
					if (!plan.Reasons.TryGetValue(ore.Key, out List<string> reasons))
					{
						reasons = new List<string>();
						plan.Reasons[ore.Key] = reasons;
						plan.Priority.Add(ore.Key);
					}
					reasons.Add(item);
				}
			}
		}
		plan.Priority.AddRange(manual.Where(ore => !plan.Reasons.ContainsKey(ore)));
		return plan;
	}

	/// <summary>"Iron Ingot", or "Iron Ingot, Nickel Ingot" for an ore that makes several short items.</summary>
	public string ReasonText(string ore)
	{
		return Reasons.TryGetValue(ore, out List<string> items) ? string.Join(", ", items.Select(Items.Name)) : "";
	}
}
