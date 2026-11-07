using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Doorman;

/// <summary>
/// The window: the on/off switch and defaults, a grid picker whose delay and hangar setting override the defaults for
/// that grid, the list of doors with what each is doing, the selected door's own setting, and a Help page.
/// </summary>
public partial class DoormanScreen : MyGuiScreenBase
{
	private const int ColumnGrid = 0;

	private const int ColumnDoor = 1;

	private const int ColumnType = 2;

	private const int ColumnSetting = 3;

	private const int ColumnState = 4;

	private const long AllGrids = 0;

	private static long s_grid = AllGrids;

	private static string s_find = "";

	private readonly List<MyGuiControlBase> _mainControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _helpControls = new List<MyGuiControlBase>();

	private readonly List<long> _gridIds = new List<long>();

	private MyGuiControlCombobox _gridBox;

	private MyGuiControlTextbox _gridDelay;

	private MyGuiControlCheckbox _gridHangars;

	private MyGuiControlButton _useDefaults;

	private MyGuiControlTable _table;

	private MyGuiControlCombobox _doorMode;

	private MyGuiControlTextbox _doorDelay;

	private MyGuiControlLabel _doorLabel;

	private MyGuiControlLabel _status;

	private string _rowsSignature = "";

	private string _gridsSignature = "";

	private bool _filling;

	private int _frames;

	public DoormanScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.82f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "DoormanScreen";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Doorman");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);
		_mainControls.Clear();
		_helpControls.Clear();
		_rowsSignature = "";
		_gridsSignature = "";

		AddCheckbox(-0.42f, -0.32f, "Close doors automatically", Settings.Enabled, on => DoormanSession.SetEnabled(on), "Off: Doorman watches but closes nothing.");
		AddCheckbox(-0.1f, -0.32f, "Faction grids too", Settings.FactionGrids, on =>
		{
			Settings.FactionGrids = on;
			Settings.SaveWorld();
			Doors.RescanSoon();
		}, "Also close doors on grids your faction owns, not only grids you own. Only doors you have access to either way.");
		AddCheckbox(0.15f, -0.32f, "Leave airlocks alone", Settings.SkipAirlocks, on =>
		{
			Settings.SkipAirlocks = on;
			Settings.SaveWorld();
		}, "Skip doors with \"airlock\" in their name or in the name of a group they're in. A door set to Always is closed anyway.");

		AddLabel(-0.42f, -0.265f, "Grid:", _mainControls);
		_gridBox = new MyGuiControlCombobox(new Vector2(-0.37f, -0.265f), new Vector2(0.25f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, openAreaItemsCount: 10);
		_gridBox.SetToolTip("All grids: the delay and hangar setting to the right are the defaults. A grid: they're that grid's own, and the list shows only its doors.");
		Add(_gridBox, _mainControls);
		AddLabel(-0.1f, -0.265f, "Delay (s):", _mainControls);
		_gridDelay = AddTextbox(0.0f, -0.265f, 0.07f, "", text =>
		{
			if (s_grid == AllGrids)
			{
				if (Settings.TryDelay(text, out double delay))
				{
					Settings.Delay = delay;
				}
			}
			else if (Settings.TryDelay(text, out double delay))
			{
				Settings.GridDelay[s_grid] = delay;
			}
			else if (string.IsNullOrWhiteSpace(text))
			{
				Settings.GridDelay.Remove(s_grid);
			}
			Settings.SaveWorld();
		});
		_gridDelay.SetToolTip($"Seconds a door stays fully open before Doorman closes it, {Settings.MinDelay:0} to {Settings.MaxDelay:0}. On a grid, blank uses the default.");
		_gridHangars = AddCheckbox(0.09f, -0.265f, "Hangar doors", Settings.Hangars, on =>
		{
			if (s_grid == AllGrids)
			{
				Settings.Hangars = on;
			}
			else
			{
				Settings.GridHangars[s_grid] = on;
			}
			Settings.SaveWorld();
		}, "Close hangar doors too. Off by default: they're slow and often left open on purpose.");
		_useDefaults = AddButton(0.33f, -0.265f, "Use Defaults", () =>
		{
			Settings.GridDelay.Remove(s_grid);
			Settings.GridHangars.Remove(s_grid);
			Settings.SaveWorld();
			ShowGridSettings();
		}, _mainControls);
		_useDefaults.SetToolTip("This grid goes back to the default delay and hangar setting.");

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.235f),
			Size = new Vector2(0.84f, 0.4f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 5,
			VisibleRowsCount = 10
		};
		_table.SetCustomColumnWidths(new float[] { 0.2f, 0.24f, 0.14f, 0.12f, 0.3f });
		_table.SetColumnName(ColumnGrid, new StringBuilder("Grid"));
		_table.SetColumnName(ColumnDoor, new StringBuilder("Door"));
		_table.SetColumnName(ColumnType, new StringBuilder("Type"));
		_table.SetColumnName(ColumnSetting, new StringBuilder("Setting"));
		_table.SetColumnName(ColumnState, new StringBuilder("Now"));
		for (int i = 0; i < 5; i++)
		{
			int column = i;
			_table.SetColumnComparison(column, (a, b) => string.Compare(a.Text.ToString(), b.Text.ToString(), StringComparison.OrdinalIgnoreCase));
		}
		_table.ItemSelected += (_, __) => ShowDoorSettings();
		Add(_table, _mainControls);

		_doorLabel = AddLabel(-0.42f, 0.2f, "Selected door:", _mainControls);
		_doorMode = new MyGuiControlCombobox(new Vector2(-0.26f, 0.2f), new Vector2(0.15f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		_doorMode.AddItem((long)DoorMode.Default, "Default", sort: false);
		_doorMode.AddItem((long)DoorMode.Never, "Never", sort: false);
		_doorMode.AddItem((long)DoorMode.Always, "Always", sort: false);
		_doorMode.SetToolTip("Default: follows the grid. Never: Doorman leaves it alone. Always: closed even if it's a hangar door or an airlock door.");
		_doorMode.ItemSelected += () =>
		{
			long door = SelectedDoor();
			if (_filling || door == 0)
			{
				return;
			}
			DoorMode mode = (DoorMode)_doorMode.GetSelectedKey();
			if (mode == DoorMode.Default)
			{
				Settings.DoorModes.Remove(door);
			}
			else
			{
				Settings.DoorModes[door] = mode;
			}
			Doors.Forgive(door);
			Settings.SaveWorld();
			RefreshRows();
		};
		Add(_doorMode, _mainControls);
		AddLabel(-0.1f, 0.2f, "Delay (s):", _mainControls);
		_doorDelay = AddTextbox(0.0f, 0.2f, 0.07f, "", text =>
		{
			long door = SelectedDoor();
			if (door == 0)
			{
				return;
			}
			if (Settings.TryDelay(text, out double delay))
			{
				Settings.DoorDelay[door] = delay;
			}
			else if (string.IsNullOrWhiteSpace(text))
			{
				Settings.DoorDelay.Remove(door);
			}
			Settings.SaveWorld();
			RefreshRows();
		});
		_doorDelay.SetToolTip("This door's own delay. Blank uses its grid's.");
		AddLabel(0.09f, 0.2f, "Blank: the grid's delay.", _mainControls).ColorMask = new Vector4(0.7f, 0.7f, 0.7f, 1f);

		AddLabel(-0.42f, 0.26f, ListSearch.Label, _mainControls);
		MyGuiControlTextbox find = AddTextbox(-0.37f, 0.26f, 0.3f, s_find, text =>
		{
			s_find = text ?? "";
			RefreshRows();
		});
		find.SetToolTip(ListSearch.ToolTip);
		_status = AddLabel(-0.05f, 0.26f, "", _mainControls);
		AddButton(0.33f, 0.32f, "Help", () => ShowPage(_helpControls), _mainControls);

		CreateHelp();
		ShowPage(_mainControls);
		RefreshRows();
		ShowGridSettings();
		ShowDoorSettings();
	}

	public override bool Update(bool hasFocus)
	{
		if (++_frames % 30 == 0)
		{
			RefreshRows();
		}
		return base.Update(hasFocus);
	}

	// ---------------------------------------------------------------- rows

	private void RefreshRows()
	{
		try
		{
			RefreshGrids();
			List<Doors.Door> doors = Doors.Grids
				.Where(g => s_grid == AllGrids || g.Grid.EntityId == s_grid)
				.SelectMany(g => g.Doors)
				.Where(d => ListSearch.Matches(s_find, d.Grid.Name, d.Name, d.Type, SettingText(d), d.State))
				.ToList();
			string signature = string.Join(",", doors.Select(d => d.Block.EntityId));
			if (signature == _rowsSignature)
			{
				// Same doors: update the text in place so the selection and scroll stay put.
				foreach (MyGuiControlTable.Row row in Rows())
				{
					Doors.Door door = (Doors.Door)row.UserData;
					SetCell(row, ColumnDoor, door.Name);
					SetCell(row, ColumnSetting, SettingText(door));
					SetCell(row, ColumnState, door.State);
				}
			}
			else
			{
				_rowsSignature = signature;
				long selected = SelectedDoor();
				_table.Clear();
				foreach (Doors.Door door in doors.OrderBy(d => d.Grid.Name).ThenBy(d => d.Name))
				{
					MyGuiControlTable.Row row = new MyGuiControlTable.Row(door);
					row.AddCell(new MyGuiControlTable.Cell(door.Grid.Name, door, door.Grid.Name));
					row.AddCell(new MyGuiControlTable.Cell(door.Name, door, door.Name));
					row.AddCell(new MyGuiControlTable.Cell(door.Type, door));
					row.AddCell(new MyGuiControlTable.Cell(SettingText(door), door));
					row.AddCell(new MyGuiControlTable.Cell(door.State, door));
					_table.Add(row);
				}
				_table.Sort(switchSort: false);
				if (selected != 0)
				{
					int index = _table.FindIndex(r => ((Doors.Door)r.UserData).Block.EntityId == selected);
					if (index >= 0)
					{
						_table.SetSelectedRow(index);
						_table.ScrollToSelection();
					}
				}
				ShowDoorSettings();
			}
			string hidden = ListSearch.IsActive(s_find) ? $" ({doors.Count} shown)" : "";
			_status.Text = $"{Doors.DoorCount} door(s){hidden}. Closed this session: {Doors.ClosesSent}";
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Doorman] Window: {ex.Message}");
		}
	}

	private IEnumerable<MyGuiControlTable.Row> Rows()
	{
		for (int i = 0; i < _table.RowsCount; i++)
		{
			yield return _table.GetRow(i);
		}
	}

	private static void SetCell(MyGuiControlTable.Row row, int column, string text)
	{
		StringBuilder cell = row.GetCell(column).Text;
		if (cell.ToString() != text)
		{
			cell.Clear().Append(text);
		}
	}

	private static string SettingText(Doors.Door door)
	{
		long id = door.Block.EntityId;
		DoorMode mode = Settings.Mode(id);
		if (mode == DoorMode.Never)
		{
			return "Never";
		}
		string delay = Settings.FormatDelay(Settings.DelayFor(door.Grid.Grid.EntityId, id)) + " s";
		return mode == DoorMode.Always ? "Always, " + delay : delay;
	}

	private void RefreshGrids()
	{
		List<Doors.GridDoors> grids = Doors.Grids.OrderBy(g => g.Name).ToList();
		string signature = string.Join(",", grids.Select(g => g.Grid.EntityId + ":" + g.Name)) + "|" + s_grid;
		if (signature == _gridsSignature)
		{
			return;
		}
		_gridsSignature = signature;
		_gridIds.Clear();
		_gridIds.Add(AllGrids);
		_gridIds.AddRange(grids.Select(g => g.Grid.EntityId));
		if (!_gridIds.Contains(s_grid))
		{
			s_grid = AllGrids;
		}
		_gridBox.ItemSelected -= OnGridSelected;
		_gridBox.ClearItems();
		_gridBox.AddItem(0, "All grids (defaults)", sort: false);
		for (int i = 0; i < grids.Count; i++)
		{
			_gridBox.AddItem(i + 1, grids[i].Name, sort: false);
		}
		_gridBox.SelectItemByKey(_gridIds.IndexOf(s_grid), sendEvent: false);
		_gridBox.ItemSelected += OnGridSelected;
	}

	private void OnGridSelected()
	{
		int index = (int)_gridBox.GetSelectedKey();
		s_grid = index >= 0 && index < _gridIds.Count ? _gridIds[index] : AllGrids;
		ShowGridSettings();
		RefreshRows();
	}

	private void ShowGridSettings()
	{
		_filling = true;
		bool all = s_grid == AllGrids;
		SetText(_gridDelay, all ? Settings.FormatDelay(Settings.Delay) : Settings.GridDelay.TryGetValue(s_grid, out double d) ? Settings.FormatDelay(d) : "");
		_gridHangars.IsChecked = Settings.HangarsFor(s_grid);
		_useDefaults.Visible = !all && _table.Visible;
		_filling = false;
	}

	private long SelectedDoor()
	{
		return (_table?.SelectedRow?.UserData as Doors.Door)?.Block.EntityId ?? 0;
	}

	private void ShowDoorSettings()
	{
		_filling = true;
		long door = SelectedDoor();
		_doorMode.Enabled = door != 0;
		_doorDelay.Enabled = door != 0;
		_doorMode.SelectItemByKey((long)Settings.Mode(door), sendEvent: false);
		SetText(_doorDelay, door != 0 && Settings.DoorDelay.TryGetValue(door, out double d) ? Settings.FormatDelay(d) : "");
		_doorLabel.Text = door == 0 ? "Pick a door:" : "Selected door:";
		_filling = false;
	}

	// ---------------------------------------------------------------- controls

	/// <summary>Shows one page's controls and hides the others, without rebuilding the window from inside a click.</summary>
	private void ShowPage(List<MyGuiControlBase> page)
	{
		foreach (MyGuiControlBase control in _mainControls.Concat(_helpControls))
		{
			control.Visible = false;
		}
		foreach (MyGuiControlBase control in page)
		{
			control.Visible = true;
		}
		if (page == _mainControls && _useDefaults != null)
		{
			_useDefaults.Visible = s_grid != AllGrids;
		}
	}

	private void Add(MyGuiControlBase control, List<MyGuiControlBase> page)
	{
		Controls.Add(control);
		page.Add(control);
	}

	private MyGuiControlLabel AddLabel(float x, float y, string text, List<MyGuiControlBase> page)
	{
		MyGuiControlLabel label = new MyGuiControlLabel(new Vector2(x, y), null, text, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Add(label, page);
		return label;
	}

	private MyGuiControlCheckbox AddCheckbox(float x, float y, string text, bool value, Action<bool> set, string toolTip)
	{
		MyGuiControlCheckbox box = new MyGuiControlCheckbox(new Vector2(x + 0.012f, y), null, toolTip, value);
		box.IsCheckedChanged = b =>
		{
			if (!_filling)
			{
				set(b.IsChecked);
				RefreshRows();
			}
		};
		Add(box, _mainControls);
		AddLabel(x + 0.035f, y, text, _mainControls).SetToolTip(toolTip);
		return box;
	}

	private MyGuiControlTextbox AddTextbox(float x, float y, float width, string text, Action<string> changed)
	{
		MyGuiControlTextbox box = new MyGuiControlTextbox(new Vector2(x, y), text, 40)
		{
			Size = new Vector2(width, 0.04f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		box.TextChanged += b =>
		{
			if (!_filling)
			{
				changed(b.Text ?? "");
			}
		};
		Add(box, _mainControls);
		return box;
	}

	private static void SetText(MyGuiControlTextbox box, string text)
	{
		if (box.Text != text)
		{
			box.SetText(new StringBuilder(text));
		}
	}

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick, List<MyGuiControlBase> page)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick());
		Add(button, page);
		return button;
	}
}
