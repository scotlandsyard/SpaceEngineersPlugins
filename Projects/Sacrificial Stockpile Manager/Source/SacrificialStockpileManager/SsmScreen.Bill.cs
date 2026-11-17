using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Graphics.GUI;
using TimShared;
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
		MyGuiControlTextbox search = new MyGuiControlTextbox(new Vector2(BillBoxX, Row2Y + BoxNudge), s_billSearch, 30)
		{
			Size = new Vector2(0.12f, BoxHeight),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		search.SetToolTip("Type part of a block's name to shorten the block type list.");
		search.TextChanged += box =>
		{
			s_billSearch = box.Text ?? "";
			FillBillBlocks();
			RefreshBillRows();
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
			RefreshBillRows();
		};

		_billBlockCombo = AddCombo(-0.135f, Row2Y, 0.555f, 14);
		_billBlockCombo.ItemSelected += () =>
		{
			int index = (int)_billBlockCombo.GetSelectedKey();
			if (index >= 0 && index < _billBlockKeys.Count)
			{
				s_billPick = _billBlockKeys[index];
				RefreshBillRows();
			}
		};
		FillBillBlocks();

		// Buttons are taller than combo boxes, so the rows with a button sit further apart than Row2Y and Row3Y.
		AddLabel(Left, BillRow3Y, "Count");
		MyGuiControlTextbox count = new MyGuiControlTextbox(new Vector2(BillBoxX, BillRow3Y + BoxNudge), s_billCount, 5)
		{
			Size = new Vector2(0.07f, BoxHeight),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		count.TextChanged += box => s_billCount = box.Text;
		Controls.Add(count);
		AddButton(-0.205f, "Add line", AddBillLine, BillRow3Y);

		AddLabel(-0.09f, BillRow3Y, "Mode");
		MyGuiControlCombobox mode = AddCombo(-0.03f, BillRow3Y, 0.2f, 2, "Fill once: stop refilling once the block holds it all; the minimums go when the items are taken out.\nKeep stocked: keep refilling until you remove them.");
		mode.AddItem(0, "Fill once", 0, null, sort: false);
		mode.AddItem(1, "Keep stocked", 1, null, sort: false);
		mode.SelectItemByKey(s_billKeep ? 1 : 0, sendEvent: false);
		mode.ItemSelected += () => s_billKeep = mode.GetSelectedKey() == 1;

		AddLabel(Left, BillRow4Y, "Crate");
		MyGuiControlTextbox crateFind = new MyGuiControlTextbox(new Vector2(BillBoxX, BillRow4Y + BoxNudge), s_billCrateFind, ListSearch.MaxLength)
		{
			Size = new Vector2(0.12f, BoxHeight),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		crateFind.SetToolTip("Type part of a container's name to find the crate to fill.");
		crateFind.TextChanged += box =>
		{
			s_billCrateFind = box.Text ?? "";
			FillBillCrates(pickFirst: ListSearch.IsActive(s_billCrateFind));
		};
		Controls.Add(crateFind);
		_billCrateCombo = AddCombo(-0.245f, BillRow4Y, 0.45f, 14, "The crate (or any block with an inventory) the materials go into.");
		_billCrateCombo.ItemSelected += () =>
		{
			int index = (int)_billCrateCombo.GetSelectedKey();
			if (index >= 0 && index < _billCrateKeys.Count)
			{
				PickBillCrate(_billCrateKeys[index]);
			}
		};
		_billStockButton = AddButton(0.315f, "Make it Stock", ToggleBillStock, BillRow4Y);
		FillBillCrates(pickFirst: false);
	}

	/// <summary>Rows of the Block materials controls: Row2Y (block type), then the Count and Crate rows, spaced for a button.</summary>
	private const float BillRow3Y = -0.27f;

	private const float BillRow4Y = -0.21f;

	private const float BillTableTop = -0.175f;

	/// <summary>Where the text boxes start in every Block materials row, so they line up under each other.</summary>
	private const float BillBoxX = -0.375f;

	private static string s_billCrateFind = "";

	/// <summary>Block ids whose parts are listed under their line (a new line opens; double-click toggles).</summary>
	private static readonly HashSet<string> s_billOpen = new HashSet<string>();

	private MyGuiControlCombobox _billCrateCombo;

	private readonly List<long> _billCrateKeys = new List<long>();

	private MyGuiControlButton _billStockButton;

	private void RefreshBillRows()
	{
		_rowsSignature = null;
		RefreshRows();
	}

	/// <summary>The grid's blocks with an inventory that match the crate Find text: containers first, then by name.</summary>
	private void FillBillCrates(bool pickFirst)
	{
		if (_billCrateCombo == null)
		{
			return;
		}
		_billCrateCombo.ClearItems();
		_billCrateKeys.Clear();
		GridSnapshot grid = Grid;
		IEnumerable<BlockSnapshot> blocks = grid == null ? Enumerable.Empty<BlockSnapshot>() : grid.Blocks
			.Where(b => b.HasInventory && ListSearch.Matches(s_billCrateFind, b.Name, b.Type))
			.OrderBy(b => b.Kind == BlockKind.Cargo ? 0 : 1)
			.ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase);
		foreach (BlockSnapshot block in blocks)
		{
			string role = block.NotYours ? "not yours" : RoleText(Store.BlockRules(block.Id), Construct.Resolve(block));
			_billCrateCombo.AddItem(_billCrateKeys.Count, $"{block.Name}  ({block.Type}, {role})", _billCrateKeys.Count, null, sort: false);
			_billCrateKeys.Add(block.Id);
		}
		int selected = _billCrateKeys.IndexOf(s_billBlockId);
		if (selected < 0 && pickFirst && _billCrateKeys.Count > 0)
		{
			PickBillCrate(_billCrateKeys[0]);
			selected = 0;
		}
		if (selected >= 0)
		{
			_billCrateCombo.SelectItemByKey(selected, sendEvent: false);
		}
		UpdateBillStockButton();
	}

	/// <summary>Makes this block the one the bill fills. The block lines stay; only the target changes.</summary>
	private void PickBillCrate(long blockId)
	{
		s_billBlockId = blockId;
		s_blockId = blockId;
		UpdateBillStockButton();
		RefreshBillRows();
	}

	private void UpdateBillStockButton()
	{
		if (_billStockButton == null)
		{
			return;
		}
		BlockSnapshot block = Grid?.Block(s_billBlockId);
		bool stock = block != null && Store.BlockRules(block.Id)?.Role == BlockRole.Stock;
		_billStockButton.Text = stock ? "Back to Auto" : "Make it Stock";
		_billStockButton.Enabled = block != null && !block.NotYours;
		_billStockButton.SetToolTip(stock
			? "It's a Stock crate: it keeps only the items it has minimums or maximums for. Back to Auto lets the plugin use it as it would by default."
			: "Stock crate: keeps only the items it has minimums or maximums for (these block materials included), and nothing else is sorted into it.");
	}

	/// <summary>Switches the crate between the Stock role and Auto, like the Role box in Block settings.</summary>
	private void ToggleBillStock()
	{
		BlockSnapshot block = Grid?.Block(s_billBlockId);
		if (block == null)
		{
			ShowMessage("Pick the crate first, under Crate.", WarningColor);
			return;
		}
		if (block.NotYours)
		{
			ShowMessage($"{block.Name} isn't yours, so it's left alone.", WarningColor);
			return;
		}
		BlockRules rules = Store.EditBlockRules(block.Id);
		rules.Role = rules.Role == BlockRole.Stock ? BlockRole.Auto : BlockRole.Stock;
		Store.SaveSettings();
		Session?.RunNow(Live);
		block = Grid?.Block(s_billBlockId) ?? block;
		Effective role = Construct.Resolve(block);
		ShowMessage($"{block.Name} is now {role.ToString().ToLowerInvariant()}. {RoleHelp(role)}", GoodColor);
		FillBillCrates(pickFirst: false);
		RefreshBillRows();
	}

	/// <summary>Opens or closes the parts list under the selected block line.</summary>
	private void ToggleBillParts()
	{
		string key = SelectedKey;
		if (key == null || !key.StartsWith("bl:", StringComparison.Ordinal) || !int.TryParse(key.Substring(3), out int index) || index >= s_billLines.Count)
		{
			return;
		}
		string id = s_billLines[index].Key;
		if (!s_billOpen.Remove(id))
		{
			s_billOpen.Add(id);
		}
		RefreshBillRows();
	}

	/// <summary>One block type's parts, times the count, as rows under its line.</summary>
	private static IEnumerable<RowData> PartRows(string keyPrefix, string blockId, int count)
	{
		Dictionary<string, double> parts = BlockBill.Sum(new List<string> { blockId }, new List<int> { count });
		foreach (KeyValuePair<string, double> part in parts.OrderBy(p => (int)Items.Category(p.Key)).ThenBy(p => Items.Name(p.Key), StringComparer.OrdinalIgnoreCase))
		{
			yield return Row(keyPrefix + part.Key, MutedColor, "        " + Items.Name(part.Key), Items.Amount(part.Value), "", "", "", "");
		}
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
		// The new line shows its parts; the others fold up so the totals stay in view.
		s_billOpen.Clear();
		s_billOpen.Add(s_billPick);
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
			string id = s_billLines[i].Key;
			bool open = s_billOpen.Contains(id);
			rows.Add(Row("bl:" + i, GoodColor, $"{(open ? "-" : "+")} {s_billLines[i].Value} x {BlockName(id)}", "", "", "", "", open ? "its parts below (double-click hides)" : "double-click for its parts"));
			if (open)
			{
				rows.AddRange(PartRows($"bp:{i}:", id, s_billLines[i].Value));
			}
		}
		if (s_billExtra.Count > 0)
		{
			rows.Add(Row("bx", GoodColor, $"From {s_billExtraSource}", "", "", "", "", $"{s_billExtra.Count} item(s)"));
		}
		if (s_billLines.Count == 0 && s_billExtra.Count == 0 && s_billPick != null)
		{
			// Nothing added yet: the parts of the block type picked above, so they show before Add line.
			rows.Add(Row("bpick", MutedColor, $"Parts for 1 x {BlockName(s_billPick)} (not added yet)"));
			rows.AddRange(PartRows("bp:pick:", s_billPick, 1));
		}
		else if (s_billLines.Count + (s_billExtra.Count > 0 ? 1 : 0) > 1 || s_billOpen.Overlaps(s_billLines.Select(l => l.Key)))
		{
			rows.Add(Row("bt", null, "Total for the bill"));
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
		if (grid?.Block(s_billBlockId) == null)
		{
			return "Pick the crate to fill under Crate (Find shortens the list), and the block types to build above.";
		}
		if (s_billLines.Count == 0 && s_billExtra.Count == 0)
		{
			return "Pick a block type and count, Add line, then Apply. Adds to the crate's minimums; nothing is lowered.";
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
			ShowMessage("Pick the crate to fill first, under Crate.", WarningColor);
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
