using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace DrillSergeant;

/// <summary>
/// The window: every rig in a table with Start, Pause and Stop; Setup for picking a rig's blocks and settings;
/// Settings and Help. Pages are shown and hidden, never rebuilt from inside a click.
/// </summary>
public partial class SargeScreen : MyGuiScreenBase
{
	private const int ColumnName = 0, ColumnMode = 1, ColumnState = 2, ColumnProgress = 3, ColumnDepth = 4, ColumnCargo = 5, ColumnTime = 6;

	private const float ButtonRow1 = 0.29f;

	private const float ButtonRow2 = 0.375f;

	private static readonly Color WarningColor = new Color(255, 190, 90);

	private static readonly Color DoneColor = new Color(140, 230, 140);

	private static long s_selectedRotor;

	private MyGuiControlTable _table;

	private MyGuiControlMultilineText _detail;

	private MyGuiControlLabel _status;

	private MyGuiControlButton _stopButton;

	private MyGuiControlTextbox _chatNameBox;

	private int _frames;

	private readonly List<MyGuiControlBase> _mainControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _settingsControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _helpControls = new List<MyGuiControlBase>();

	public SargeScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.86f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "DrillSergeantScreen";
	}

	protected override void OnClosed()
	{
		ScriptLink.Subscribe(null);
		RigStore.Save();
		base.OnClosed();
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Drill Sergeant");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);
		_mainControls.Clear();
		_settingsControls.Clear();
		_helpControls.Clear();
		_setupControls.Clear();

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.34f),
			Size = new Vector2(0.84f, 0.36f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 7,
			VisibleRowsCount = 8
		};
		_table.SetCustomColumnWidths(new float[] { 0.21f, 0.08f, 0.14f, 0.1f, 0.13f, 0.09f, 0.09f });
		_table.SetColumnName(ColumnName, new StringBuilder("Rig"));
		_table.SetColumnName(ColumnMode, new StringBuilder("Mode"));
		_table.SetColumnName(ColumnState, new StringBuilder("State"));
		_table.SetColumnName(ColumnProgress, new StringBuilder("Progress"));
		_table.SetColumnName(ColumnDepth, new StringBuilder("Depth"));
		_table.SetColumnName(ColumnCargo, new StringBuilder("Cargo"));
		_table.SetColumnName(ColumnTime, new StringBuilder("Time left"));
		_table.ItemSelected += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) =>
		{
			s_selectedRotor = Selected()?.RotorId ?? 0;
			ScriptLink.Subscribe(Selected());
			ShowDetail();
		};
		_table.ItemDoubleClicked += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) => OpenSetup(Selected());
		Add(_table, _mainControls);

		_detail = new MyGuiControlMultilineText(new Vector2(0f, 0.05f), new Vector2(0.84f, 0.17f), null, "Blue", 0.75f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		Add(_detail, _mainControls);
		_status = AddLabel(-0.42f, 0.245f, "", null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.315f, ButtonRow1, "Start", () => OnRig(Commands.Start), _mainControls, "Starts or continues the selected rig. Plugin mode: from where it stopped. Script mode: sends \"start\" to the script.");
		AddButton(-0.105f, ButtonRow1, "Pause", () => OnRig(Commands.Pause), _mainControls, "Stops the rotor, pistons and drills where they are; Start carries on from there. Script mode: sends \"pause\".");
		_stopButton = AddButton(0.105f, ButtonRow1, "Stop", StopOrSet, _mainControls, "");
		AddButton(0.315f, ButtonRow1, "Setup", () => OpenSetup(Selected()), _mainControls, "Blocks and settings of the selected rig (double-click a row does the same).");
		AddButton(-0.315f, ButtonRow2, "New Rig", () => OpenSetup(null), _mainControls, "Set up a rig: pick its rotor and Sarge finds the pistons and drills on it.");
		AddButton(-0.105f, ButtonRow2, "Remove", RemoveRig, _mainControls, "Forgets the selected rig. Its blocks are left exactly as they are.");
		AddButton(0.105f, ButtonRow2, "Settings", () => ShowPage(_settingsControls), _mainControls, null);
		AddButton(0.315f, ButtonRow2, "Help", () => ShowPage(_helpControls), _mainControls, null);

		CreateSettings();
		CreateHelp();
		CreateSetup();
		ShowPage(_mainControls);
		RefreshRows();
		if (RigStore.LoadFailed)
		{
			SetStatus("This world's rig file couldn't be read; it's kept aside as .unreadable. See the game log.");
		}
	}

	// ---------------------------------------------------------------- pages

	private void CreateSettings()
	{
		AddLabel(-0.42f, -0.34f, "TOOLBAR", _settingsControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		MyGuiControlCheckbox toolbar = new MyGuiControlCheckbox(new Vector2(-0.40f, -0.28f), null, null, Settings.ToolbarActions);
		string toolbarTip = "Adds \"Drill Sergeant Start/Pause\" and \"Drill Sergeant Pause\" to the actions of rig rotors, for your cockpit or character toolbar. They only work from your own toolbar (button panels and timers run on the server, where the plugin isn't). The action ids are saved in the grid's toolbar, like any action; other players see an empty slot.";
		toolbar.SetToolTip(toolbarTip);
		toolbar.IsCheckedChanged = b =>
		{
			Settings.ToolbarActions = b.IsChecked;
			Settings.Save();
		};
		Add(toolbar, _settingsControls);
		AddLabel(-0.375f, -0.28f, "Offer Start/Pause and Pause as toolbar actions on rig rotors", _settingsControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER).SetToolTip(toolbarTip);

		AddLabel(-0.42f, -0.19f, "CHAT", _settingsControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		AddLabel(-0.42f, -0.13f, "Sarge's name in chat:", _settingsControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		_chatNameBox = new MyGuiControlTextbox(new Vector2(-0.2f, -0.13f), Personality.DisplayName == SargeSession.ChatSender ? "" : Personality.DisplayName, Personality.MaxNameLength)
		{
			Size = new Vector2(0.22f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		Add(_chatNameBox, _settingsControls);
		AddButton(0.15f, -0.13f, "Set name", () => Chatter.SetDisplayName(_chatNameBox.Text), _settingsControls, "Empty puts back \"Drill Sergeant\". The Chat setting at the top right sets how much Sarge comments; rig alerts are set per rig in Setup.");
		AddButton(0.315f, ButtonRow2, "Back", () => ShowPage(_mainControls), _settingsControls, null);
	}

	private void CreateHelp()
	{
		MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, -0.34f), new Vector2(0.84f, 0.67f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		help.AppendText(SargeHelp.Text);
		Add(help, _helpControls);
		AddButton(0.315f, ButtonRow2, "Back", () => ShowPage(_mainControls), _helpControls, null);
	}

	private void ShowPage(List<MyGuiControlBase> page)
	{
		foreach (MyGuiControlBase control in _mainControls.Concat(_settingsControls).Concat(_helpControls).Concat(_setupControls))
		{
			control.Visible = false;
		}
		foreach (MyGuiControlBase control in page)
		{
			control.Visible = true;
		}
		if (page == _setupControls)
		{
			UpdateSetupVisibility();
		}
		SetStatus("");
		if (page == _mainControls)
		{
			RefreshRows();
		}
	}

	// ---------------------------------------------------------------- rows

	public override bool Update(bool hasFocus)
	{
		bool result = base.Update(hasFocus);
		// The table follows the rigs every second (they're looked at twice a second).
		if (++_frames % 60 == 0 && _table.Visible)
		{
			RefreshRows();
		}
		return result;
	}

	private Rig Selected()
	{
		return _table.SelectedRow?.UserData as Rig;
	}

	private void RefreshRows()
	{
		int scroll = _table.ScrollBar?.Value is float value ? (int)value : 0;
		_table.Clear();
		int selectIndex = -1;
		foreach (Rig rig in RigStore.Rigs)
		{
			RigRuntime live = rig.Live;
			Color? color = live.State == "Warning" || live.State == "Conflict" || live.State == "Cargo full" ? WarningColor : live.State == "Finished" ? DoneColor : (Color?)null;
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(rig);
			row.AddCell(new MyGuiControlTable.Cell(rig.Name, rig, rig.Name, color));
			row.AddCell(new MyGuiControlTable.Cell(rig.Mode == RigMode.Plugin ? "Plugin" : "Script", rig, rig.Mode == RigMode.Plugin ? "Sarge drives this rig." : "The Automatic Mining Platform script drives this rig.", color));
			row.AddCell(new MyGuiControlTable.Cell(string.IsNullOrEmpty(live.State) ? "..." : live.State, rig, live.Detail, color));
			row.AddCell(new MyGuiControlTable.Cell((live.Progress * 100).ToString("0") + "%", rig, null, color));
			row.AddCell(new MyGuiControlTable.Cell(live.MaxDepth > 0 ? $"{live.Depth:0.0} / {live.MaxDepth:0} m" : "-", rig, null, color));
			row.AddCell(new MyGuiControlTable.Cell(live.CargoFill >= 0 ? (live.CargoFill * 100).ToString("0") + "%" : "-", rig, null, color));
			row.AddCell(new MyGuiControlTable.Cell(live.SecondsLeft > 0 ? Format.Duration(live.SecondsLeft) : "-", rig, "An estimate from the rotor and piston speeds; cargo pauses come on top.", color));
			_table.Add(row);
			if (rig.RotorId == s_selectedRotor)
			{
				selectIndex = _table.RowsCount - 1;
			}
		}
		if (selectIndex < 0 && _table.RowsCount > 0)
		{
			selectIndex = 0;
			s_selectedRotor = (_table.GetRow(0).UserData as Rig)?.RotorId ?? 0;
		}
		if (selectIndex >= 0)
		{
			_table.SelectedRowIndex = selectIndex;
		}
		if (_table.ScrollBar != null && scroll > 0)
		{
			_table.ScrollBar.Value = scroll;
		}
		ShowDetail();
	}

	private void ShowDetail()
	{
		Rig rig = Selected();
		_detail.Clear();
		if (rig == null)
		{
			_detail.AppendText(RigStore.Rigs.Count == 0 ? "No rigs yet. Press New Rig, stand near your mining rig and pick its rotor." : "Select a rig.");
			_stopButton.Text = "Stop";
			return;
		}
		_detail.AppendText($"{rig.Name}: {rig.Live.State}\n{rig.Live.Detail}");
		_stopButton.Text = rig.Mode == RigMode.Script ? "Set" : "Stop";
		_stopButton.SetToolTip(rig.Mode == RigMode.Script ? "Sends \"set\" to the script: it reads its blocks and settings again and lines the rig up at step 0." : "Ends the job: drills and rotor off, pistons back to the start, progress back to step 0. Asks first.");
	}

	// ---------------------------------------------------------------- buttons

	private void OnRig(Func<Rig, string> command)
	{
		Rig rig = Selected();
		if (rig == null)
		{
			SetStatus("Select a rig first.");
			return;
		}
		SetStatus(command(rig));
		RefreshRows();
	}

	private void StopOrSet()
	{
		Rig rig = Selected();
		if (rig == null)
		{
			SetStatus("Select a rig first.");
			return;
		}
		if (rig.Mode == RigMode.Script)
		{
			SetStatus(ScriptLink.Send(rig, "set"));
			return;
		}
		Confirm("Stop " + rig.Name, $"End the job on {rig.Name}? The drills and rotor go off, the pistons go back to the start, and its progress starts over at step 0.", () =>
		{
			SetStatus(Driver.Stop(rig));
			RefreshRows();
		});
	}

	private void RemoveRig()
	{
		Rig rig = Selected();
		if (rig == null)
		{
			SetStatus("Select a rig first.");
			return;
		}
		Confirm("Remove " + rig.Name, $"Forget {rig.Name}? Sarge stops looking after it; its blocks stay exactly as they are right now (a running rig finishes its current step and stands still).", () =>
		{
			RigStore.Rigs.Remove(rig);
			RigStore.Save();
			s_selectedRotor = 0;
			SetStatus($"{rig.Name} removed.");
			RefreshRows();
		});
	}

	private static void Confirm(string caption, string text, Action yes)
	{
		MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder(caption), messageText: new StringBuilder(text), callback: answer =>
		{
			if (answer == MyGuiScreenMessageBox.ResultEnum.YES)
			{
				yes();
			}
		}));
	}

	// ---------------------------------------------------------------- helpers

	private void SetStatus(string text)
	{
		_status.Text = text ?? "";
		_status.SetToolTip(text);
	}

	private void Add(MyGuiControlBase control, List<MyGuiControlBase> page)
	{
		Controls.Add(control);
		page?.Add(control);
	}

	private MyGuiControlLabel AddLabel(float x, float y, string text, List<MyGuiControlBase> page, MyGuiDrawAlignEnum align)
	{
		MyGuiControlLabel label = new MyGuiControlLabel(new Vector2(x, y), null, text, null, 0.8f, "Blue", align);
		if (page != null)
		{
			Add(label, page);
		}
		return label;
	}

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick, List<MyGuiControlBase> page, string toolTip)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, toolTip, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) =>
		{
			try
			{
				onClick();
			}
			catch (Exception ex)
			{
				SetStatus("Something went wrong: " + ex.Message);
				MyLog.Default.WriteLineAndConsole($"[DrillSergeant] Button '{text}': {ex}");
			}
		});
		Add(button, page);
		return button;
	}
}
