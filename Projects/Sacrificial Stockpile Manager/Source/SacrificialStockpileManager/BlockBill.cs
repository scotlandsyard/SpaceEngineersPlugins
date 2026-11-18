using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities.Cube;
using TimShared;
using VRage;
using VRage.Game;

namespace SacrificialStockpileManager;

/// <summary>
/// Add block materials: a block's components, times a count, summed over several block types, added to a container's
/// minimums (see <see cref="BillLine"/>). The preview says per item what the grid holds, what's short, and what can't
/// be made here (the quota picker's rule: no admin-only or loot-only items get crafted). Bills from other plugins
/// come in through <see cref="BillApi"/> and land in the same preview.
/// </summary>
internal static class BlockBill
{
	/// <summary>One item of a preview.</summary>
	public class Line
	{
		public string Item;

		public double Needed;

		public double InBlock;

		/// <summary>On the ship or station, the block included.</summary>
		public double OnGrid;

		public double Short => Math.Max(0.0, Needed - OnGrid);

		public bool Buildable;

		/// <summary>No blueprint at all (Prototech parts): only found as loot.</summary>
		public bool LootOnly;

		/// <summary>What Apply adds: everything for an item that can be made, otherwise only what exists here.</summary>
		public double Fill;

		public string Note
		{
			get
			{
				string what = Buildable ? "" : LootOnly ? "loot only" : "not buildable here";
				if (!Buildable)
				{
					return Fill <= 0.0 ? $"{what}; none here, skipped" : Fill < Needed ? $"{what}; only {Items.Amount(Fill)} here" : what;
				}
				return Short > 0.0 ? "short" : "";
			}
		}
	}

	private static List<MyCubeBlockDefinition> s_blocks;

	/// <summary>Drops the block list when the world unloads: the next world can have other mods.</summary>
	public static void ResetCaches()
	{
		s_blocks = null;
	}

	/// <summary>Every public block with components (mod blocks included), by name.</summary>
	public static List<MyCubeBlockDefinition> AllBlocks
	{
		get
		{
			s_blocks ??= MyDefinitionManager.Static.GetAllDefinitions().OfType<MyCubeBlockDefinition>()
				.Where(d => d.Public && d.Enabled && d.Components != null && d.Components.Length > 0)
				.OrderBy(d => d.DisplayNameText, StringComparer.OrdinalIgnoreCase)
				.ThenBy(d => d.CubeSize)
				.ToList();
			return s_blocks;
		}
	}

	/// <summary>The blocks of one grid size whose name contains the search text.</summary>
	public static List<MyCubeBlockDefinition> Choices(string search, bool large)
	{
		string text = (search ?? "").Trim();
		return AllBlocks.Where(d => (d.CubeSize == MyCubeSize.Large) == large
			&& (text.Length == 0 || d.DisplayNameText.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || d.Id.SubtypeName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
	}

	/// <summary>
	/// The components to build these blocks: <paramref name="blockIds"/> as MyDefinitionId.ToString() gives them,
	/// each times its count. Item keys as SSM uses them ("Component/SteelPlate").
	/// </summary>
	public static Dictionary<string, double> Sum(List<string> blockIds, List<int> counts)
	{
		Dictionary<string, double> items = new Dictionary<string, double>();
		for (int i = 0; i < blockIds.Count && i < counts.Count; i++)
		{
			if (counts[i] <= 0 || !MyDefinitionId.TryParse(blockIds[i], out MyDefinitionId id) || !MyDefinitionManager.Static.TryGetCubeBlockDefinition(id, out MyCubeBlockDefinition def) || def.Components == null)
			{
				continue;
			}
			foreach (MyCubeBlockDefinition.Component component in def.Components)
			{
				if (component?.Definition == null)
				{
					continue;
				}
				string key = Items.Key(component.Definition.Id);
				items.TryGetValue(key, out double amount);
				items[key] = amount + (double)component.Count * counts[i];
			}
		}
		return items;
	}

	/// <summary>
	/// The preview for a block on a grid: per item, what's needed, in the block, on the grid, and whether it can be
	/// made here (<paramref name="assemblers"/> null for a grid that isn't loaded: any public assembler type).
	/// </summary>
	public static List<Line> Preview(Dictionary<string, double> items, GridSnapshot grid, long blockId, IList<MyProductionBlock> assemblers)
	{
		Dictionary<string, double> totals = grid?.Totals ?? new Dictionary<string, double>();
		BlockSnapshot block = grid?.Block(blockId);
		List<Line> lines = new List<Line>();
		foreach (KeyValuePair<string, double> item in items.Where(i => i.Value > 0.0))
		{
			totals.TryGetValue(item.Key, out double onGrid);
			double inBlock = 0.0;
			block?.ItemAmounts.TryGetValue(item.Key, out inBlock);
			bool buildable = Items.RefineryOnly(item.Key) || (assemblers != null ? Items.BuildableBlueprint(item.Key, assemblers) : Items.OfferedBlueprint(item.Key)) != null;
			Line line = new Line
			{
				Item = item.Key,
				Needed = item.Value,
				InBlock = inBlock,
				OnGrid = onGrid,
				Buildable = buildable,
				LootOnly = !buildable && Items.Blueprint(item.Key) == null,
			};
			line.Fill = buildable ? line.Needed : Math.Min(line.Needed, line.OnGrid);
			if (Items.IsIntegral(item.Key))
			{
				line.Fill = Math.Floor(line.Fill + 1e-6);
			}
			lines.Add(line);
		}
		return lines.OrderBy(l => (int)Items.Category(l.Item)).ThenBy(l => Items.Name(l.Item), StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>Adds the preview's minimums to the block, on top of what's set (never lowering one). Returns how many items were added.</summary>
	public static int Apply(long blockId, List<Line> lines, bool keep, string source)
	{
		BlockRules rules = Store.EditBlockRules(blockId);
		int added = 0;
		foreach (Line line in lines.Where(l => l.Fill > 0.0))
		{
			BillLine same = rules.Bill.FirstOrDefault(b => b.Item == line.Item && b.Keep == keep && !b.Done && b.Source == source);
			if (same != null)
			{
				same.Amount += line.Fill;
			}
			else
			{
				rules.Bill.Add(new BillLine { Item = line.Item, Amount = line.Fill, Keep = keep, Source = source });
			}
			added++;
		}
		Store.SaveSettings();
		return added;
	}

	/// <summary>Removes the block's block-materials minimums: one set (by source and mode), or all of them when source is null.</summary>
	public static int Remove(long blockId, string source, bool? keep)
	{
		BlockRules rules = Store.BlockRules(blockId);
		if (rules?.Bill == null)
		{
			return 0;
		}
		int removed = rules.Bill.RemoveAll(b => source == null || (b.Source == source && (!keep.HasValue || b.Keep == keep.Value)));
		if (removed > 0)
		{
			Store.SaveSettings();
		}
		return removed;
	}

	/// <summary>Called from Construct.Refresh: the block's bill lines become part of its minimums (see <see cref="BillLine"/>).</summary>
	public static void Merge(LiveBlock live)
	{
		live.BillItems = null;
		live.NoRefill = null;
		List<BillLine> bill = live.Rules?.Bill;
		if (bill == null || bill.Count == 0)
		{
			return;
		}
		live.BillItems = new HashSet<string>();
		foreach (IGrouping<string, BillLine> item in bill.Where(b => b.Amount > 0.0).GroupBy(b => b.Item))
		{
			double total = item.Sum(b => b.Amount);
			double done = item.Where(b => !b.Keep && b.Done).Sum(b => b.Amount);
			int index = live.Limits.FindIndex(l => l.Item == item.Key);
			ItemLimit own = index >= 0 ? live.Limits[index] : null;
			ItemLimit merged = new ItemLimit
			{
				Item = item.Key,
				Min = (own != null && own.HasMin ? own.Min : 0.0) + total,
				Max = own?.Max ?? -1.0
			};
			if (merged.HasMax && merged.Max < merged.Min)
			{
				// A maximum below the materials would send them straight out again.
				merged.Max = merged.Min;
			}
			if (index >= 0)
			{
				live.Limits[index] = merged;
			}
			else
			{
				live.Limits.Add(merged);
			}
			live.BillItems.Add(item.Key);
			if (done > 0.0)
			{
				live.NoRefill ??= new Dictionary<string, double>();
				live.NoRefill[item.Key] = done;
			}
		}
	}

	/// <summary>
	/// After an engine pass: Fill-once lines the block now holds are marked Done (no longer refilled), and Done lines
	/// whose items were taken out are dropped. A block whose Fill-once lines are all done is "packed": a log line and a
	/// chat line.
	/// </summary>
	public static void Update(Construct construct)
	{
		bool changed = false;
		foreach (LiveBlock live in construct.Blocks)
		{
			List<BillLine> bill = live.Rules?.Bill;
			if (bill == null || bill.Count == 0 || live.NotYours || live.Input == null || live.Block.Closed)
			{
				continue;
			}
			bool waiting = bill.Any(b => !b.Keep && !b.Done);
			foreach (IGrouping<string, BillLine> item in bill.GroupBy(b => b.Item).ToList())
			{
				if (!Items.TryParse(item.Key, out MyDefinitionId id))
				{
					continue;
				}
				double have = (double)live.Input.GetItemAmount(id);
				ItemLimit own = live.Rules.Limit(item.Key);
				double ownMin = own != null && own.HasMin ? own.Min : 0.0;
				double full = ownMin + item.Sum(b => b.Amount);
				if (have >= full - 1e-6)
				{
					foreach (BillLine line in item.Where(b => !b.Keep && !b.Done))
					{
						line.Done = true;
						changed = true;
					}
				}
				else if (have <= ownMin + item.Where(b => b.Keep).Sum(b => b.Amount) + 1e-6 && item.Any(b => b.Done))
				{
					// The packed items were taken out: nothing left for these lines to protect.
					bill.RemoveAll(b => b.Item == item.Key && b.Done);
					changed = true;
				}
			}
			if (waiting && !bill.Any(b => !b.Keep && !b.Done) && bill.Any(b => !b.Keep))
			{
				string source = bill.Where(b => !b.Keep).Select(b => b.Source).FirstOrDefault(s => !string.IsNullOrEmpty(s));
				construct.AddLog($"{live.Name} is packed{(source != null ? $" ({source})" : "")}: its Fill-once materials stop refilling");
				Personality.Say("crate_packed", "block", live.Name);
			}
		}
		if (changed)
		{
			Store.SaveSettings();
		}
	}
}
