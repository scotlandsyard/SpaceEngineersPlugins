using System.Collections.Generic;
using System.Linq;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Utils;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>
/// The Find box above the Blocks and Production tables: shortens the list to the blocks whose name, type, role,
/// mode, state or settings match.
/// </summary>
public partial class SsmScreen
{
	/// <summary>Text in the Blocks Find box, kept while the game runs. Not the item lists' Find text (s_search).</summary>
	private static string s_blocksFind = "";

	/// <summary>Text in the Production Find box, kept while the game runs.</summary>
	private static string s_productionFind = "";

	/// <summary>How many blocks the Find box left in the table at the last refresh.</summary>
	private int _blocksFindShown;

	/// <summary>True for the views with a block Find box above the table.</summary>
	private static bool HasBlockFind(View view) => view == View.Blocks || view == View.Production;

	/// <summary>The current view's block Find text.</summary>
	private static string BlockFindText => s_view == View.Production ? s_productionFind : s_blocksFind;

	private void CreateBlocksControls()
	{
		AddLabel(Left, Row2Y, ListSearch.Label);
		MyGuiControlTextbox find = new MyGuiControlTextbox(new Vector2(-0.375f, Row2Y + BoxNudge), BlockFindText, ListSearch.MaxLength)
		{
			Size = new Vector2(0.795f, BoxHeight),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		find.SetToolTip(ListSearch.ToolTip + (s_view == View.Production
			? " Matches the name, type, mode, state and detail columns."
			: " Matches the name, type, role and settings columns."));
		find.TextChanged += box =>
		{
			if (s_view == View.Production)
			{
				s_productionFind = box.Text ?? "";
			}
			else
			{
				s_blocksFind = box.Text ?? "";
			}
			_rowsSignature = null;
			RefreshRows();
		};
		Controls.Add(find);
	}

	/// <summary>The rows the Find box leaves, or a single "no match" row.</summary>
	private List<RowData> FilterBlockRows(List<RowData> rows)
	{
		string text = BlockFindText;
		if (!ListSearch.IsActive(text) || rows.Count == 0)
		{
			return rows;
		}
		List<RowData> shown = rows.Where(r => r.Key.StartsWith("b:", System.StringComparison.Ordinal) && ListSearch.Matches(text, r.Texts)).ToList();
		_blocksFindShown = shown.Count;
		if (shown.Count == 0)
		{
			shown.Add(Row("none", MutedColor, "No block matches Find."));
		}
		return shown;
	}

	/// <summary>"Find shows N. " while the current view's Find box is in use, else nothing.</summary>
	private string FindShownPrefix => ListSearch.IsActive(BlockFindText) ? $"Find shows {_blocksFindShown}. " : "";
}
