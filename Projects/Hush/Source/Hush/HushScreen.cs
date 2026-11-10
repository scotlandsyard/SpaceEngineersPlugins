using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Hush;

/// <summary>
/// The Hush window. Two views:
/// - Blocks: pick one of your grids, see its blocks that make sound, mute them one by one or by type.
/// - Types everywhere: mute a block type in every world and on every grid, other players' included.
/// Only grids you own a block on (or that share a block with you) are listed, the same blocks the K menu would show;
/// other grids are never listed, only covered by the type mutes.
/// </summary>
public class HushScreen : MyGuiScreenBase
{
	private const int ColumnMuted = 0;

	private const int ColumnType = 1;

	private const int ColumnName = 2;

	private const int ColumnWhy = 3;

	private const int ColumnInfo = 4;

	private const int ViewBlocks = 0;

	private const int ViewTypes = 1;

	private MyGuiControlTable _table;

	private MyGuiControlCombobox _viewBox;

	private MyGuiControlCombobox _gridBox;

	private MyGuiControlLabel _gridLabel;

	private MyGuiControlLabel _status;

	private string _statusText = "";

	private int _view = ViewBlocks;

	private List<MyCubeGrid> _grids = new List<MyCubeGrid>();

	private Dictionary<MyCubeGrid, List<MyCubeBlock>> _blocksByGrid = new Dictionary<MyCubeGrid, List<MyCubeBlock>>();

	private MyCubeGrid _grid;

	private bool _sortedOnce;

	private readonly List<MyGuiControlBase> _blockButtons = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _mainControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _helpControls = new List<MyGuiControlBase>();

	private MyGuiControlMultilineText _helpText;

	/// <summary>Text in the Find box, kept while the window is open. It filters whichever list is showing.</summary>
	private string _find = "";

	public HushScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.86f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "HushScreen";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Hush");
		PluginSwitcher.AddSwitcher(this, caption);
		// The chat setting sits in the top-right corner, level with the caption; the Grid list below ends at its right edge.
		Personality.AddChatSetting(this, caption);

		_mainControls.Clear();
		_helpControls.Clear();
		_blockButtons.Clear();

		// The filter row sits under the caption, right of where the switcher's list drops down (it ends by x = -0.20).
		MyGuiControlLabel viewLabel = new MyGuiControlLabel(new Vector2(-0.16f, -0.34f), null, "Show:", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		AddMain(viewLabel);
		_viewBox = new MyGuiControlCombobox(new Vector2(-0.15f, -0.34f), new Vector2(0.2f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		_viewBox.AddItem(ViewBlocks, "Blocks on a grid", sort: false);
		_viewBox.AddItem(ViewTypes, "Types everywhere", sort: false);
		_viewBox.SelectItemByKey(_view, sendEvent: false);
		_viewBox.ItemSelected += OnViewSelected;
		_viewBox.SetToolTip("Blocks on a grid: mute single blocks on one of your grids. Types everywhere: mute a block type on every grid in every world.");
		AddMain(_viewBox);
		_gridLabel = new MyGuiControlLabel(new Vector2(0.1f, -0.34f), null, "Grid:", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		AddMain(_gridLabel);
		_gridBox = new MyGuiControlCombobox(new Vector2(0.11f, -0.34f), new Vector2(0.31f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, openAreaItemsCount: 12);
		_gridBox.SetToolTip("Your grids with blocks that make sound, nearest first, with how many such blocks each has. Only grids you own a block on, or that share one with you, are listed.");
		AddMain(_gridBox);

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.315f),
			Size = new Vector2(0.84f, 0.48f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 5,
			VisibleRowsCount = 12
		};
		_table.SetCustomColumnWidths(new float[] { 0.1f, 0.19f, 0.29f, 0.28f, 0.14f });
		_table.SetColumnAlign(ColumnInfo, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.ItemDoubleClicked += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) => ToggleSelected();
		AddMain(_table);

		_status = new MyGuiControlLabel(new Vector2(-0.42f, 0.2f), null, "", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		AddMain(_status);

		AddButton(-0.315f, 0.255f, "Mute / Unmute", ToggleSelected);
		MyGuiControlButton muteType = AddButton(-0.105f, 0.255f, "Mute Type on Grid", () => SetTypeOnGrid(true));
		muteType.SetToolTip("Mutes every listed block of the selected block's type on this grid. While Find is in use, only the ones it shows.");
		_blockButtons.Add(muteType);
		MyGuiControlButton unmuteType = AddButton(0.105f, 0.255f, "Unmute Type on Grid", () => SetTypeOnGrid(false));
		unmuteType.SetToolTip("Unmutes every listed block of the selected block's type on this grid. While Find is in use, only the ones it shows.");
		_blockButtons.Add(unmuteType);
		MyGuiControlButton unmuteGrid = AddButton(0.315f, 0.255f, "Unmute Grid", UnmuteGrid);
		unmuteGrid.SetToolTip("Unmutes every block on this grid. While Find is in use, only the blocks it shows.");
		_blockButtons.Add(unmuteGrid);
		AddButton(-0.315f, 0.315f, "Refresh", () => Refresh(true));
		// The rest of Refresh's row is free. Both views use it: Find filters whichever list is showing.
		AddMain(new MyGuiControlLabel(new Vector2(-0.21f, 0.315f), null, ListSearch.Label, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		MyGuiControlTextbox find = new MyGuiControlTextbox(new Vector2(-0.16f, 0.315f), _find, ListSearch.MaxLength)
		{
			Size = new Vector2(0.37f, 0.04f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		find.SetToolTip(ListSearch.ToolTip);
		find.TextChanged += box =>
		{
			_find = box.Text ?? "";
			Refresh(false);
		};
		AddMain(find);
		// Help / Back share the bottom-right spot, like the other plugins. Esc closes the window.
		AddButton(0.315f, 0.375f, "Help", () => SetHelp(true));

		_helpText = new MyGuiControlMultilineText(new Vector2(0f, -0.315f), new Vector2(0.84f, 0.55f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			Visible = false
		};
		_helpText.AppendText(HelpText);
		Controls.Add(_helpText);
		_helpControls.Add(_helpText);
		MyGuiControlButton back = AddButton(0.315f, 0.375f, "Back", () => SetHelp(false));
		back.Visible = false;
		_mainControls.Remove(back);
		_helpControls.Add(back);

		Refresh(true);
	}

	private void AddMain(MyGuiControlBase control)
	{
		Controls.Add(control);
		_mainControls.Add(control);
	}

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick());
		AddMain(button);
		return button;
	}

	/// <summary>Shows the Help page in place of the list, or goes back, by hiding controls rather than rebuilding the window.</summary>
	private void SetHelp(bool show)
	{
		foreach (MyGuiControlBase control in _mainControls)
		{
			control.Visible = !show;
		}
		foreach (MyGuiControlBase control in _helpControls)
		{
			control.Visible = show;
		}
		if (!show)
		{
			UpdateViewControls();
		}
	}

	private void UpdateViewControls()
	{
		bool blocks = _view == ViewBlocks;
		_gridLabel.Visible = blocks;
		_gridBox.Visible = blocks;
		foreach (MyGuiControlBase button in _blockButtons)
		{
			button.Visible = blocks;
		}
	}

	private void OnViewSelected()
	{
		_view = (int)_viewBox.GetSelectedKey();
		_sortedOnce = false;
		UpdateViewControls();
		Refresh(false);
	}

	private void OnGridSelected()
	{
		int index = (int)_gridBox.GetSelectedKey();
		if (index >= 0 && index < _grids.Count)
		{
			_grid = _grids[index];
			_sortedOnce = false;
			Refresh(false);
		}
	}

	/// <summary>Collects the sound blocks you have access to (when <paramref name="rescan"/>), then rebuilds the list.</summary>
	private void Refresh(bool rescan)
	{
		object selected = _table.SelectedRow?.UserData;
		if (rescan)
		{
			int pruned = Mutes.PruneMissingGrids();
			if (pruned > 0)
			{
				_statusText = $"Forgot {pruned} mute(s) on grids that no longer exist.";
			}
			Scan();
		}
		UpdateViewControls();
		_table.Clear();
		if (_view == ViewBlocks)
		{
			FillBlocks();
		}
		else
		{
			FillTypes();
		}
		if (_sortedOnce)
		{
			_table.Sort(switchSort: false);
		}
		else
		{
			_table.SortByColumn(ColumnType);
			_sortedOnce = true;
		}
		if (selected != null)
		{
			int index = _table.FindIndex(r => Equals(r.UserData, selected));
			if (index >= 0)
			{
				_table.SetSelectedRow(index);
				_table.ScrollToSelection();
			}
		}
		if (_statusText.Length > 0)
		{
			_status.Text = _statusText;
			_statusText = "";
		}
		else
		{
			string shown = ListSearch.IsActive(_find) ? $"Find shows {_table.RowsCount}. " : "";
			_status.Text = _view == ViewBlocks
				? (_grid == null ? "None of your grids nearby has blocks that make sound." : ListSearch.IsActive(_find) ? $"{shown}Double-click a row to mute or unmute it." : $"{_table.RowsCount} block(s) that make sound. Double-click a row to mute or unmute it.")
				: $"{shown}Types muted here are muted on every grid, in every world. Double-click a row to mute or unmute it.";
		}
	}

	/// <summary>A block's row against the Find box: what the row shows, plus the block's type name when it has been renamed.</summary>
	private bool BlockMatchesFind(MyCubeBlock block)
	{
		if (!ListSearch.IsActive(_find))
		{
			return true;
		}
		return !block.Closed && ListSearch.Matches(_find, MutedText(block), Mutes.TypeName(block.BlockDefinition.Id.TypeId), BlockName(block), Mutes.Why(block) ?? "", DistanceText(block), block.BlockDefinition.DisplayNameText);
	}

	private static string DistanceText(MyCubeBlock block)
	{
		return $"{Distance(block):N0} m";
	}

	/// <summary>
	/// Groups the sound blocks by grid, keeping only blocks you have terminal access to on grids where you own a block or
	/// one is shared with you. Grids are listed nearest first.
	/// </summary>
	private void Scan()
	{
		_blocksByGrid = new Dictionary<MyCubeGrid, List<MyCubeBlock>>();
		Dictionary<MyCubeGrid, bool> gridAllowed = new Dictionary<MyCubeGrid, bool>();
		foreach (MyCubeBlock block in Mutes.SoundBlocks())
		{
			MyCubeGrid grid = block.CubeGrid;
			if (!gridAllowed.TryGetValue(grid, out bool allowed))
			{
				allowed = HasAccessToGrid(grid);
				gridAllowed[grid] = allowed;
			}
			if (!allowed || !(block is IMyTerminalBlock terminal) || !terminal.HasLocalPlayerAccess())
			{
				continue;
			}
			if (!_blocksByGrid.TryGetValue(grid, out List<MyCubeBlock> list))
			{
				list = new List<MyCubeBlock>();
				_blocksByGrid[grid] = list;
			}
			list.Add(block);
		}

		Vector3D me = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		_grids = _blocksByGrid.Keys.OrderBy(g => Vector3D.DistanceSquared(g.PositionComp.GetPosition(), me)).ToList();
		if (_grid == null || !_grids.Contains(_grid))
		{
			_grid = ControlledGrid() is MyCubeGrid controlled && _grids.Contains(controlled) ? controlled : _grids.FirstOrDefault();
		}
		_gridBox.ItemSelected -= OnGridSelected;
		_gridBox.ClearItems();
		for (int i = 0; i < _grids.Count; i++)
		{
			_gridBox.AddItem(i, $"{_grids[i].DisplayName} ({_blocksByGrid[_grids[i]].Count})", sort: false);
		}
		if (_grid != null)
		{
			_gridBox.SelectItemByKey(_grids.IndexOf(_grid), sendEvent: false);
		}
		_gridBox.ItemSelected += OnGridSelected;
	}

	/// <summary>True if the local player owns a block on the grid, or a block on it is shared with them (faction or all).</summary>
	private static bool HasAccessToGrid(MyCubeGrid grid)
	{
		foreach (MyCubeBlock block in grid.GetFatBlocks())
		{
			if (block.OwnerId != 0 && block is IMyTerminalBlock terminal && terminal.HasLocalPlayerAccess())
			{
				return true;
			}
		}
		return false;
	}

	private static MyCubeGrid ControlledGrid()
	{
		return (MyAPIGateway.Session?.ControlledObject as IMyCubeBlock)?.CubeGrid as MyCubeGrid;
	}

	private void FillBlocks()
	{
		_table.SetColumnName(ColumnMuted, new StringBuilder("Muted"));
		_table.SetColumnName(ColumnType, new StringBuilder("Type"));
		_table.SetColumnName(ColumnName, new StringBuilder("Name"));
		_table.SetColumnName(ColumnWhy, new StringBuilder("Why listed"));
		_table.SetColumnName(ColumnInfo, new StringBuilder("Distance"));
		_table.SetColumnComparison(ColumnMuted, (a, b) => MutedText(b.UserData).CompareTo(MutedText(a.UserData)));
		_table.SetColumnComparison(ColumnType, (a, b) => CompareText(a, b, ColumnType));
		_table.SetColumnComparison(ColumnName, (a, b) => CompareText(a, b, ColumnName));
		_table.SetColumnComparison(ColumnWhy, (a, b) => CompareText(a, b, ColumnWhy));
		_table.SetColumnComparison(ColumnInfo, (a, b) => Distance(a.UserData as MyCubeBlock).CompareTo(Distance(b.UserData as MyCubeBlock)));
		if (_grid == null || !_blocksByGrid.TryGetValue(_grid, out List<MyCubeBlock> blocks))
		{
			return;
		}
		foreach (MyCubeBlock block in blocks.Where(b => !b.Closed && BlockMatchesFind(b)))
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(block);
			row.AddCell(new MyGuiControlTable.Cell(MutedText(block), block));
			row.AddCell(new MyGuiControlTable.Cell(Mutes.TypeName(block.BlockDefinition.Id.TypeId), block));
			row.AddCell(new MyGuiControlTable.Cell(BlockName(block), block));
			row.AddCell(new MyGuiControlTable.Cell(Mutes.Why(block) ?? "", block));
			row.AddCell(new MyGuiControlTable.Cell(DistanceText(block), block));
			_table.Add(row);
		}
	}

	/// <summary>Every block type with a sound in the loaded definitions (mods included), plus any type already muted.</summary>
	private void FillTypes()
	{
		_table.SetColumnName(ColumnMuted, new StringBuilder("Muted"));
		_table.SetColumnName(ColumnType, new StringBuilder("Type"));
		_table.SetColumnName(ColumnName, new StringBuilder("For example"));
		_table.SetColumnName(ColumnWhy, new StringBuilder("Sound"));
		_table.SetColumnName(ColumnInfo, new StringBuilder("On your grids"));
		_table.SetColumnComparison(ColumnMuted, (a, b) => MutedText(b.UserData).CompareTo(MutedText(a.UserData)));
		_table.SetColumnComparison(ColumnType, (a, b) => CompareText(a, b, ColumnType));
		_table.SetColumnComparison(ColumnName, (a, b) => CompareText(a, b, ColumnName));
		_table.SetColumnComparison(ColumnWhy, (a, b) => CompareText(a, b, ColumnWhy));
		_table.SetColumnComparison(ColumnInfo, (a, b) => YourCount(a.UserData).CompareTo(YourCount(b.UserData)));

		Dictionary<MyObjectBuilderType, MyCubeBlockDefinition> examples = new Dictionary<MyObjectBuilderType, MyCubeBlockDefinition>();
		foreach (MyCubeBlockDefinition definition in MyDefinitionManager.Static.GetAllDefinitions().OfType<MyCubeBlockDefinition>())
		{
			if (!examples.ContainsKey(definition.Id.TypeId) && definition.Public && Mutes.DefinitionMakesSound(definition))
			{
				examples[definition.Id.TypeId] = definition;
			}
		}
		foreach (MyObjectBuilderType type in Mutes.MutedTypes())
		{
			if (!examples.ContainsKey(type))
			{
				examples[type] = null;
			}
		}
		foreach (KeyValuePair<MyObjectBuilderType, MyCubeBlockDefinition> pair in examples)
		{
			MyObjectBuilderType type = pair.Key;
			string example = pair.Value?.DisplayNameText ?? "";
			string sound = pair.Value == null ? "" : Mutes.WorkingSoundName(pair.Value);
			if (!ListSearch.Matches(_find, MutedText(type), Mutes.TypeName(type), example, sound, YourCount(type).ToString()))
			{
				continue;
			}
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(type);
			row.AddCell(new MyGuiControlTable.Cell(MutedText(type), type));
			row.AddCell(new MyGuiControlTable.Cell(Mutes.TypeName(type), type));
			row.AddCell(new MyGuiControlTable.Cell(example, type));
			row.AddCell(new MyGuiControlTable.Cell(sound, type));
			row.AddCell(new MyGuiControlTable.Cell(YourCount(type).ToString(), type));
			_table.Add(row);
		}
	}

	/// <summary>How many sound blocks of this type are on the grids listed in the Blocks view.</summary>
	private int YourCount(object userData)
	{
		return userData is MyObjectBuilderType type ? _blocksByGrid.Values.Sum(list => list.Count(b => b.BlockDefinition.Id.TypeId == type)) : 0;
	}

	private static string MutedText(object userData)
	{
		switch (userData)
		{
			case MyCubeBlock block:
				if (Mutes.IsBlockMuted(block))
				{
					return "Muted";
				}
				return Mutes.IsTypeMuted(block.BlockDefinition.Id.TypeId) ? "By type" : "";
			case MyObjectBuilderType type:
				return Mutes.IsTypeMuted(type) ? "Muted" : "";
			default:
				return "";
		}
	}

	private static string BlockName(MyCubeBlock block)
	{
		string name = (block as IMyTerminalBlock)?.CustomName;
		return string.IsNullOrWhiteSpace(name) ? block.BlockDefinition.DisplayNameText ?? block.BlockDefinition.Id.SubtypeName : name;
	}

	private static double Distance(MyCubeBlock block)
	{
		Vector3D me = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		return block == null ? 0.0 : Vector3D.Distance(block.PositionComp.GetPosition(), me);
	}

	private static int CompareText(MyGuiControlTable.Cell a, MyGuiControlTable.Cell b, int column)
	{
		return string.Compare(a.Row?.GetCell(column)?.Text?.ToString(), b.Row?.GetCell(column)?.Text?.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	private void ToggleSelected()
	{
		object selected = _table.SelectedRow?.UserData;
		switch (selected)
		{
			case MyCubeBlock block:
			{
				bool mute = !Mutes.IsBlockMuted(block);
				Mutes.SetBlocks(new[] { block }, mute);
				if (mute)
				{
					Personality.Say("block_muted", "block", BlockName(block));
				}
				_statusText = mute ? $"Muted {BlockName(block)}." : Mutes.IsTypeMuted(block.BlockDefinition.Id.TypeId) ? $"Unmuted {BlockName(block)}, but its whole type is still muted (Types everywhere)." : $"Unmuted {BlockName(block)}.";
				break;
			}
			case MyObjectBuilderType type:
			{
				bool mute = !Mutes.IsTypeMuted(type);
				Mutes.SetType(type, mute);
				if (mute)
				{
					Personality.Say("type_muted", "type", Mutes.TypeName(type));
				}
				_statusText = mute ? $"{Mutes.TypeName(type)}: muted everywhere." : $"{Mutes.TypeName(type)}: unmuted. Blocks you muted one by one stay muted.";
				break;
			}
			default:
				_statusText = "Select a row first.";
				break;
		}
		Refresh(false);
	}

	/// <summary>Mutes or unmutes every listed block of the selected row's type on this grid.</summary>
	private void SetTypeOnGrid(bool mute)
	{
		if (!(_table.SelectedRow?.UserData is MyCubeBlock selected) || _grid == null || !_blocksByGrid.TryGetValue(_grid, out List<MyCubeBlock> blocks))
		{
			_statusText = "Select a block of the type first.";
			Refresh(false);
			return;
		}
		MyObjectBuilderType type = selected.BlockDefinition.Id.TypeId;
		// With Find in use only the blocks it shows change; the rest of the type stays as it was.
		List<MyCubeBlock> ofType = blocks.Where(b => b.BlockDefinition.Id.TypeId == type).ToList();
		List<MyCubeBlock> shown = ofType.Where(BlockMatchesFind).ToList();
		Mutes.SetBlocks(shown, mute);
		int hidden = ofType.Count - shown.Count;
		_statusText = $"{(mute ? "Muted" : "Unmuted")} {shown.Count} {Mutes.TypeName(type)} block(s) on {_grid.DisplayName}." + (hidden > 0 ? $" {hidden} hidden by Find left as they were." : "");
		Refresh(false);
	}

	private void UnmuteGrid()
	{
		if (_grid == null || !_blocksByGrid.TryGetValue(_grid, out List<MyCubeBlock> blocks))
		{
			_statusText = "No grid picked.";
			Refresh(false);
			return;
		}
		if (ListSearch.IsActive(_find))
		{
			// Only what Find shows; a muted block it hides stays muted.
			List<MyCubeBlock> shown = blocks.Where(BlockMatchesFind).ToList();
			Mutes.SetBlocks(shown, false);
			Personality.Say("grid_unmuted", "grid", _grid.DisplayName);
			_statusText = $"Unmuted the {shown.Count} block(s) Find shows on {_grid.DisplayName}; {blocks.Count - shown.Count} it hides left as they were.";
			Refresh(false);
			return;
		}
		Mutes.SetBlocks(blocks, false);
		Personality.Say("grid_unmuted", "grid", _grid.DisplayName);
		_statusText = $"Unmuted every block on {_grid.DisplayName}. Types muted everywhere stay muted.";
		Refresh(false);
	}

	private const string HelpText =
		"WHAT IT DOES\n" +
		"Hush lists the blocks that make sound and lets you mute them: one block, every block of a type on one grid, or a whole block type everywhere. It works for modded blocks too. Muting only changes what your own PC plays: nothing is sent to the server, nothing is stored on the grid, and other players still hear everything.\n\n" +
		"BLOCKS ON A GRID\n" +
		"Pick one of your grids in the Grid list (nearest first, with how many sound blocks it has). Only grids you own a block on, or that have a block shared with you, are listed, and only the blocks you could open in the terminal. A block shows up if its type has a working sound (thrusters, drills, refineries, doors, sound blocks and so on) or if it has played a working sound since the world loaded (damage and destruction sounds don't count: every block has those). The Why listed column says which: Sound and the sound's name for a type with a working sound, Played and the sound's name for a block heard since the world loaded, or Muted for a muted block that would not be listed otherwise. Subgrids on rotors, hinges and pistons are listed as their own grids.\n" +
		"Select a row and press Mute / Unmute, or double-click it. The Muted column says Muted for a block you muted, and By type when its whole type is muted everywhere. Mute Type on Grid mutes every listed block of the selected block's type on this grid; Unmute Type on Grid undoes that. Unmute Grid unmutes every block on this grid. Blocks added later aren't muted by these buttons: mute them too, or use Types everywhere. Click a column header to sort. Refresh reads the grids again. Find shortens either list as you type; while it's in use, the Type and Grid buttons only change the blocks it shows.\n\n" +
		"TYPES EVERYWHERE\n" +
		"Every block type with a sound in the game and your mods. Muting a type silences it on every grid, other players' included, in every world. Sound is the sound that type plays. On your grids is how many of that type are on the grids the Blocks view lists.\n\n" +
		"HOW IT'S SAVED\n" +
		"Block mutes are kept for each world in Hush_Mutes_<world>.txt, and type mutes for every world in Hush_Settings.txt, both in %AppData%\\SpaceEngineers\\Storage\\Hush. A block is remembered by its grid and its place on the grid, so a mute survives a reload but not the block being rebuilt or the grid being pasted again. In single player, mutes on grids that no longer exist are forgotten when you open this window.\n\n" +
		"COMMANDS\n" +
		"/hush opens this window. /hush chat off, quiet, normal or chatty sets how much Hush talks, like the Chat dropdown. /hush name and a name changes what Hush's chat lines show under; /hush name on its own puts Hush back. /tim opens whichever of our plugin windows you used last, and the list at the top left of this window switches to another of our plugins. Esc closes the window.\n\n" +
		"CHAT\n" +
		"The Chat dropdown at the top right sets how much Hush talks: Off, Quiet, Normal or Chatty. Hush is a soft-spoken librarian, the quietest voice among our plugins. It whispers when you open this window, mute a block, mute a type everywhere, or unmute a grid (Mute Type on Grid says nothing). It has no urgent lines, so on Quiet it only answers Wilson's roll call when Wilson is loaded. Our plugins take turns: after any of them speaks, the next ordinary line waits a while, and the same comment isn't repeated soon. Only you see the lines. The chat level and name are saved in Hush_Settings.txt and apply in every world.";
}
