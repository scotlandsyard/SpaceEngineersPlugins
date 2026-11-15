using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>
/// The Block materials view: pick block types and counts (3 x Prototech Drill, 1 x Small Reactor), see the summed
/// components with what's here and what's short, and add them to the block's minimums (see <see cref="BlockBill"/>).
/// A bill from another plugin (<see cref="BillApi"/>) opens here too, as an extra list next to the block lines.
/// </summary>
public partial class SsmScreen
{
	private static long s_billBlockId;

	/// <summary>Block definition id (MyDefinitionId.ToString()) and count, in the order added.</summary>
	private static readonly List<KeyValuePair<string, int>> s_billLines = new List<KeyValuePair<string, int>>();

	/// <summary>Items sent by another plugin, and its label; empty when the bill is only block lines.</summary>
	private static Dictionary<string, double> s_billExtra = new Dictionary<string, double>();

	private static string s_billExtraSource = "";

	private static bool s_billKeep;

	private static string s_billSearch = "";

	private static bool s_billLarge = true;

	private static string s_billPick;

	private static string s_billCount = "1";

	private MyGuiControlCombobox _billBlockCombo;

	private readonly List<string> _billBlockKeys = new List<string>();

	/// <summary>Called by SsmSession.OpenBill before the window opens: the block, and a bill from another plugin.</summary>
	internal static void PrepareBill(long gridKey, long blockId, Dictionary<string, double> items, bool keep, string source)
	{
		s_gridKey = gridKey;
		s_blockId = blockId;
		s_billBlockId = blockId;
		s_billLines.Clear();
		s_billExtra = new Dictionary<string, double>(items);
		s_billExtraSource = source;
		s_billKeep = keep;
		s_view = View.Bill;
	}

	private void OpenBillFor(long blockId)
	{
		if (blockId == 0 || Grid?.Block(blockId) == null)
		{
			ShowMessage("Pick a block with an inventory first.", WarningColor);
			return;
		}
		if (s_billBlockId != blockId)
		{
			s_billBlockId = blockId;
			s_billLines.Clear();
			s_billExtra = new Dictionary<string, double>();
			s_billExtraSource = "";
		}
		SwitchView(View.Bill);
	}

	private void CreateBillControls()
	{
		if (s_billBlockId == 0)
		{
			s_billBlockId = s_blockId;
		}
		AddLabel(Left, Row2Y, "Find");
		MyGuiControlTextbox search = new MyGuiControlTextbox(new Vector2(-0.375f, Row2Y), s_billSearch, 30)
		{
			Size = new Vector2(0.12f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		search.TextChanged += box =>
		{
			s_billSearch = box.Text ?? "";
			FillBillBlocks();
		};
		Controls.Add(search);

		MyGuiControlCombobox size = AddCombo(-0.245f, Row2Y, 0.1f, 2);
		size.AddItem(1, "Large", 0, null, sort: false);
		size.AddItem(0, "Small", 1, null, sort: false);
		size.SelectItemByKey(s_billLarge ? 1 : 0, sendEvent: false);
		size.ItemSelected += () =>
		{
			s_billLarge = size.GetSelectedKey() == 1;
			FillBillBlocks();
		};

		_billBlockCombo = AddCombo(-0.135f, Row2Y, 0.555f, 14);
		_billBlockCombo.ItemSelected += () =>
		{
			int index = (int)_billBlockCombo.GetSelectedKey();
			if (index >= 0 && index < _billBlockKeys.Count)
			{
				s_billPick = _billBlockKeys[index];
			}
		};
		FillBillBlocks();

		AddLabel(Left, Row3Y, "Count");
		MyGuiControlTextbox count = new MyGuiControlTextbox(new Vector2(-0.355f, Row3Y), s_billCount, 5)
		{
			Size = new Vector2(0.07f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		count.TextChanged += box => s_billCount = box.Text;
		Controls.Add(count);
		AddButton(-0.18f, "Add line", AddBillLine, Row3Y);

		AddLabel(-0.06f, Row3Y, "Mode");
		MyGuiControlCombobox mode = AddCombo(0.0f, Row3Y, 0.18f, 2, "Fill once: stop refilling once the block holds it all; the minimums go when the items are taken out.\nKeep stocked: keep refilling until you remove them.");
		mode.AddItem(0, "Fill once", 0, null, sort: false);
		mode.AddItem(1, "Keep stocked", 1, null, sort: false);
		mode.SelectItemByKey(s_billKeep ? 1 : 0, sendEvent: false);
		mode.ItemSelected += () => s_billKeep = mode.GetSelectedKey() == 1;

		string target = Grid?.Block(s_billBlockId)?.Name ?? "(no block)";
		AddLabel(0.2f, Row3Y, target.Length > 24 ? target.Substring(0, 23) + "~" : target);
	}

	/// <summary>Fills the block type list for the Find text and grid size, keeping the pick when it's still there.</summary>
	private void FillBillBlocks()
	{
		if (_billBlockCombo == null)
		{
			return;
		}
		_billBlockCombo.ClearItems();
		_billBlockKeys.Clear();
		foreach (MyCubeBlockDefinition def in BlockBill.Choices(s_billSearch, s_billLarge).Take(400))
		{
			string mod = def.Context != null && !def.Context.IsBaseGame ? $"  ({def.Context.ModName})" : "";
			_billBlockCombo.AddItem(_billBlockKeys.Count, def.DisplayNameText + mod, _billBlockKeys.Count, null, sort: false);
			_billBlockKeys.Add(def.Id.ToString());
		}
		int selected = s_billPick == null ? -1 : _billBlockKeys.IndexOf(s_billPick);
		if (selected < 0 && _billBlockKeys.Count > 0)
		{
			selected = 0;
			s_billPick = _billBlockKeys[0];
		}
		if (selected >= 0)
		{
			_billBlockCombo.SelectItemByKey(selected, sendEvent: false);
		}
	}

	private void AddBillLine()
	{
		if (s_billPick == null)
		{
			ShowMessage("Pick a block type first (type part of its name in Find).", WarningColor);
			return;
		}
		if (!int.TryParse((s_billCount ?? "").Trim(), out int count) || count <= 0 || count > 10000)
		{
			ShowMessage("Type how many, like 3.", WarningColor);
			return;
		}
		int index = s_billLines.FindIndex(l => l.Key == s_billPick);
		if (index >= 0)
		{
			s_billLines[index] = new KeyValuePair<string, int>(s_billPick, s_billLines[index].Value + count);
		}
		else
		{
			s_billLines.Add(new KeyValuePair<string, int>(s_billPick, count));
		}
		ShowMessage($"Added {count} x {BlockName(s_billPick)}.", GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private static string BlockName(string id)
	{
		return MyDefinitionId.TryParse(id, out MyDefinitionId defId) && MyDefinitionManager.Static.TryGetCubeBlockDefinition(defId, out MyCubeBlockDefinition def)
			? $"{def.DisplayNameText} ({(def.CubeSize == MyCubeSize.Large ? "large" : "small")})"
			: id;
	}

	/// <summary>The bill now: the block lines' components plus another plugin's items.</summary>
	private static Dictionary<string, double> BillItems()
	{
		Dictionary<string, double> items = BlockBill.Sum(s_billLines.Select(l => l.Key).ToList(), s_billLines.Select(l => l.Value).ToList());
		foreach (KeyValuePair<string, double> extra in s_billExtra)
		{
			items.TryGetValue(extra.Key, out double amount);
			items[extra.Key] = amount + extra.Value;
		}
		return items;
	}

	/// <summary>A label for what the bill is for, saved with its lines.</summary>
	private static string BillSource()
	{
		List<string> parts = s_billLines.Select(l => $"{l.Value} x {BlockName(l.Key)}").ToList();
		if (s_billExtra.Count > 0)
		{
			parts.Insert(0, s_billExtraSource);
		}
		string text = string.Join(", ", parts);
		return text.Length > 80 ? text.Substring(0, 77) + "..." : text;
	}

	private List<BlockBill.Line> BillPreview(GridSnapshot grid)
	{
		return BlockBill.Preview(BillItems(), grid, s_billBlockId, QuotaAssemblers(grid));
	}

	private List<RowData> BillRows(GridSnapshot grid)
	{
		List<RowData> rows = new List<RowData>();
		for (int i = 0; i < s_billLines.Count; i++)
		{
			rows.Add(Row("bl:" + i, GoodColor, $"{s_billLines[i].Value} x {BlockName(s_billLines[i].Key)}", "", "", "", "", "block line"));
		}
		if (s_billExtra.Count > 0)
		{
			rows.Add(Row("bx", GoodColor, $"From {s_billExtraSource}", "", "", "", "", $"{s_billExtra.Count} item(s)"));
		}
		foreach (BlockBill.Line line in BillPreview(grid))
		{
			Color? color = !line.Buildable ? WarningColor : line.Short > 0.0 ? (Color?)null : MutedColor;
			rows.Add(Row("bi:" + line.Item, color, "   " + Items.Name(line.Item), Items.Amount(line.Needed), Items.Amount(line.InBlock), Items.Amount(line.OnGrid), line.Short > 0.0 ? Items.Amount(line.Short) : "", line.Note));
		}
		// What's already on the block from earlier bills, by set.
		List<BillLine> existing = Store.BlockRules(s_billBlockId)?.Bill ?? new List<BillLine>();
		foreach (IGrouping<string, BillLine> set in existing.GroupBy(b => (b.Keep ? "k|" : "o|") + b.Source))
		{
			BillLine first = set.First();
			int done = set.Count(b => b.Done);
			string state = first.Keep ? "Keep stocked" : done == set.Count() ? "Fill once, packed" : $"Fill once, {done} of {set.Count()} items in";
			rows.Add(Row("bs:" + set.Key, MutedColor, $"On the block: {first.Source}", "", "", "", "", state));
		}
		return rows;
	}

	private string BillHint(GridSnapshot grid)
	{
		if (s_billLines.Count == 0 && s_billExtra.Count == 0)
		{
			return "Pick a block type and count, Add line, then Apply. Adds to the block's minimums; nothing is lowered.";
		}
		List<BlockBill.Line> preview = BillPreview(grid);
		int shortItems = preview.Count(l => l.Short > 0.0 && l.Buildable);
		int cant = preview.Count(l => !l.Buildable);
		return $"{preview.Count} items, {shortItems} short{(cant > 0 ? $", {cant} can't be made here (only what exists is added)" : "")}. Apply adds them to the minimums.";
	}

	private void RemoveBillRow()
	{
		string key = SelectedKey;
		if (key == null)
		{
			ShowMessage("Select a block line, the plugin's list, or a set already on the block.", WarningColor);
			return;
		}
		if (key.StartsWith("bl:", StringComparison.Ordinal) && int.TryParse(key.Substring(3), out int index) && index < s_billLines.Count)
		{
			ShowMessage($"Removed {BlockName(s_billLines[index].Key)}.", null);
			s_billLines.RemoveAt(index);
		}
		else if (key == "bx")
		{
			ShowMessage($"Removed the list from {s_billExtraSource}.", null);
			s_billExtra = new Dictionary<string, double>();
		}
		else if (key.StartsWith("bs:", StringComparison.Ordinal))
		{
			bool keep = key[3] == 'k';
			string source = key.Substring(5);
			int removed = BlockBill.Remove(s_billBlockId, source, keep);
			Session?.RunNow(Live);
			ShowMessage($"Removed {removed} block-materials minimum(s) ({source}) from the block.", null);
		}
		else
		{
			ShowMessage("Items come from the lines above: remove a line instead.", WarningColor);
			return;
		}
		_rowsSignature = null;
		RefreshAll();
	}

	private void ApplyBill()
	{
		GridSnapshot grid = Grid;
		BlockSnapshot block = grid?.Block(s_billBlockId);
		if (block == null)
		{
			ShowMessage("The block isn't on this grid any more.", WarningColor);
			return;
		}
		if (block.NotYours)
		{
			ShowMessage($"{block.Name} isn't yours, so it's left alone.", WarningColor);
			return;
		}
		List<BlockBill.Line> preview = BillPreview(grid);
		if (preview.Count == 0)
		{
			ShowMessage("Nothing to add: Add line first.", WarningColor);
			return;
		}
		string source = BillSource();
		int added = BlockBill.Apply(s_billBlockId, preview, s_billKeep, source);
		int skipped = preview.Count(l => l.Fill <= 0.0);
		Live?.AddLog($"Block materials for {block.Name} ({(s_billKeep ? "Keep stocked" : "Fill once")}): {added} item(s) added to its minimums, {source}");
		s_billLines.Clear();
		s_billExtra = new Dictionary<string, double>();
		Session?.RunNow(Live);
		ShowMessage($"Added {added} item(s) to {block.Name}'s minimums{(skipped > 0 ? $"; {skipped} skipped (none here, can't be made)" : "")}.", GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	/// <summary>Sets grid quotas for what's short and can be made here, so autocraft makes it (the existing quota path and its checks).</summary>
	private void BillQuotas()
	{
		GridSnapshot grid = Grid;
		if (grid == null)
		{
			return;
		}
		List<BlockBill.Line> shortItems = BillPreview(grid).Where(l => l.Buildable && l.Short > 0.0 && !Items.RefineryOnly(l.Item)).ToList();
		if (shortItems.Count == 0)
		{
			ShowMessage("Nothing short that the assemblers here can make.", null);
			return;
		}
		GridRules rules = Store.EditGridRules(grid);
		foreach (BlockBill.Line line in shortItems)
		{
			ItemLimit quota = rules.Quota(line.Item);
			if (quota == null)
			{
				quota = new ItemLimit { Item = line.Item };
				rules.Quotas.Add(quota);
			}
			// Enough on the grid for the whole bill; a higher quota stays.
			quota.Min = Math.Max(quota.Min, line.Needed);
			if (quota.HasMax && quota.Max < quota.Min)
			{
				quota.Max = quota.Min;
			}
		}
		Store.SaveSettings();
		Session?.RunNow(Live);
		ShowMessage($"Quotas set for {shortItems.Count} short item(s).{(rules.Autocraft ? "" : " Autocraft is off; switch it on in the Overview to queue them.")}", rules.Autocraft ? GoodColor : WarningColor);
		_rowsSignature = null;
		RefreshAll();
	}
}
