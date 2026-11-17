using System.Collections.Generic;
using System.Linq;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Utils;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>The Find box above the Blocks table: shortens the list to the blocks whose name, type, role or settings match.</summary>
public partial class SsmScreen
{
	/// <summary>Text in the Blocks Find box, kept while the game runs. Not the item lists' Find text (s_search).</summary>
	private static string s_blocksFind = "";

	/// <summary>How many blocks the Find box left in the table at the last refresh.</summary>
	private int _blocksFindShown;

	private void CreateBlocksControls()
	{
		AddLabel(Left, Row2Y, ListSearch.Label);
		MyGuiControlTextbox find = new MyGuiControlTextbox(new Vector2(-0.375f, Row2Y + BoxNudge), s_blocksFind, ListSearch.MaxLength)
		{
			Size = new Vector2(0.795f, BoxHeight),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		find.SetToolTip(ListSearch.ToolTip + " Matches the name, type, role and settings columns.");
		find.TextChanged += box =>
		{
			s_blocksFind = box.Text ?? "";
			_rowsSignature = null;
			RefreshRows();
		};
		Controls.Add(find);
	}

	/// <summary>The Blocks rows the Find box leaves, or a single "no match" row.</summary>
	private List<RowData> FilterBlockRows(List<RowData> rows)
	{
		if (!ListSearch.IsActive(s_blocksFind) || rows.Count == 0)
		{
			return rows;
		}
		List<RowData> shown = rows.Where(r => r.Key.StartsWith("b:", System.StringComparison.Ordinal) && ListSearch.Matches(s_blocksFind, r.Texts)).ToList();
		_blocksFindShown = shown.Count;
		if (shown.Count == 0)
		{
			shown.Add(Row("none", MutedColor, "No block matches Find."));
		}
		return shown;
	}
}
