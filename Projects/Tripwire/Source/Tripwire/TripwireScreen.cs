using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Tripwire;

/// <summary>
/// The window: one table with this plugin's alerts and the script logs it has read, filtered by source and grid,
/// sortable by any column. A Settings page and a Help page take the table's place when opened.
/// </summary>
public class TripwireScreen : MyGuiScreenBase
{
	private const int ColumnTime = 0;

	private const int ColumnGrid = 1;

	private const int ColumnEvent = 2;

	private const int ColumnSubject = 3;

	private const int ColumnDetail = 4;

	private const int ColumnNearby = 5;

	private static readonly string[] Views = { "Everything", "My alerts", "Script logs", "New since last visit" };

	private const string AllGrids = "All grids";

	private static int s_view;

	private static string s_grid = AllGrids;

	private MyGuiControlTable _table;

	private MyGuiControlCombobox _viewBox;

	private MyGuiControlCombobox _gridBox;

	private MyGuiControlLabel _status;

	private MyGuiControlButton _muteButton;

	private List<string> _gridOptions = new List<string>();

	private readonly List<MyGuiControlBase> _mainControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _settingsControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _helpControls = new List<MyGuiControlBase>();

	public TripwireScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.86f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "TripwireScreen";
	}

	protected override void OnClosed()
	{
		// Everything you could see has now been seen: "since your last visit" starts again from here.
		ScriptLogs.MarkRead();
		base.OnClosed();
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Tripwire");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);
		_mainControls.Clear();
		_settingsControls.Clear();
		_helpControls.Clear();

		// The filter row sits just under the chat setting and right of where the switcher's list drops down.
		AddLabel(-0.115f, -0.34f, "Show:", _mainControls, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_viewBox = new MyGuiControlCombobox(new Vector2(-0.105f, -0.34f), new Vector2(0.23f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		for (int i = 0; i < Views.Length; i++)
		{
			_viewBox.AddItem(i, Views[i], sort: false);
		}
		_viewBox.SelectItemByKey(s_view, sendEvent: false);
		_viewBox.ItemSelected += () =>
		{
			s_view = (int)_viewBox.GetSelectedKey();
			RefreshRows();
		};
		_viewBox.SetToolTip("My alerts: what this plugin saw happen to your blocks while the grid was loaded. Script logs: what the Tripwire PB script recorded on grids you can open in the terminal. New since last visit: script log lines you haven't seen yet.");
		Add(_viewBox, _mainControls);
		AddLabel(0.185f, -0.34f, "Grid:", _mainControls, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_gridBox = new MyGuiControlCombobox(new Vector2(0.195f, -0.34f), new Vector2(0.225f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, openAreaItemsCount: 12);
		_gridBox.SetToolTip("Show only one grid.");
		Add(_gridBox, _mainControls);

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.315f),
			Size = new Vector2(0.84f, 0.48f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 6,
			VisibleRowsCount = 12
		};
		_table.SetCustomColumnWidths(new float[] { 0.12f, 0.17f, 0.11f, 0.22f, 0.21f, 0.17f });
		_table.SetColumnName(ColumnTime, new StringBuilder("Time"));
		_table.SetColumnName(ColumnGrid, new StringBuilder("Grid"));
		_table.SetColumnName(ColumnEvent, new StringBuilder("Event"));
		_table.SetColumnName(ColumnSubject, new StringBuilder("Block / visitor"));
		_table.SetColumnName(ColumnDetail, new StringBuilder("Detail"));
		_table.SetColumnName(ColumnNearby, new StringBuilder("Nearby"));
		_table.SetColumnComparison(ColumnTime, (a, b) => Entry(a).Time.CompareTo(Entry(b).Time));
		_table.SetColumnComparison(ColumnGrid, (a, b) => Compare(a, b, e => e.Grid));
		_table.SetColumnComparison(ColumnEvent, (a, b) => Compare(a, b, e => e.EventText));
		_table.SetColumnComparison(ColumnSubject, (a, b) => Compare(a, b, e => e.Subject));
		_table.SetColumnComparison(ColumnDetail, (a, b) => Compare(a, b, e => e.Detail));
		_table.SetColumnComparison(ColumnNearby, (a, b) => Compare(a, b, e => e.Nearby));
		Add(_table, _mainControls);

		_status = AddLabel(-0.42f, 0.2f, "", _mainControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);

		AddButton(-0.315f, 0.255f, "Copy to Datapad", CopyToDatapad, _mainControls);
		AddButton(-0.105f, 0.255f, "Mark All Read", MarkAllRead, _mainControls);
		AddButton(0.105f, 0.255f, "Refresh", () =>
		{
			ScriptLogs.Scan();
			RefreshRows();
		}, _mainControls);
		AddButton(0.315f, 0.255f, "Clear My Alerts", ClearAlerts, _mainControls);
		_muteButton = AddButton(-0.315f, 0.315f, "", ToggleMute, _mainControls);
		AddButton(0.105f, 0.375f, "Settings", () => ShowPage(_settingsControls), _mainControls);
		AddButton(0.315f, 0.375f, "Help", () => ShowPage(_helpControls), _mainControls);

		CreateSettings();
		CreateHelp();
		ShowPage(_mainControls);
		RefreshRows();
	}

	// ---------------------------------------------------------------- pages

	private void CreateSettings()
	{
		AddLabel(-0.42f, -0.34f, "ALERTS FROM THIS PLUGIN (no script needed)", _settingsControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		float y = -0.28f;
		AddCheck(ref y, "Chat line when my blocks are damaged, ground or lost", "One line per grid at most every 30 seconds. Every event also goes into the log here.", Settings.ChatAlerts, v => Settings.ChatAlerts = v);
		AddCheck(ref y, "Play a sound with each chat line", "A short HUD sound with each alert line.", Settings.Sound, v => Settings.Sound = v);
		AddCheck(ref y, "Also watch my faction's grids", "Grids whose main owner is in your faction, not just your own.", Settings.WatchFaction, v => Settings.WatchFaction = v);
		AddCheck(ref y, "No alerts for the ship I'm piloting", "You already know when your own ship is being hit.", Settings.QuietWhenPiloting, v => Settings.QuietWhenPiloting = v);
		AddCheck(ref y, "No grinding alerts while I grind", "Ignores grinding within 20 m of you while you hold a grinder, and near the ship you pilot while its grinders are on.", Settings.QuietWhenGrinding, v => Settings.QuietWhenGrinding = v);
		AddCheck(ref y, "Name players near a damaged block (only names the HUD is already showing you)", "Off by default. Adds the names of players within 50 m of the damaged block, but only players whose name the game's HUD is drawing for you at that moment (relayed by your antennas), and only if this world shows player names on the HUD. Never a position, distance or direction.", Settings.NameNearby, v => Settings.NameNearby = v);
		y += 0.03f;
		AddLabel(-0.42f, y, "SCRIPT LOGS", _settingsControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		y += 0.06f;
		AddCheck(ref y, "Tell me when a grid's log has news since my last visit", "When a grid with a Tripwire log comes within terminal reach and its log has events you haven't seen.", Settings.VisitNews, v => Settings.VisitNews = v);
		AddButton(0.315f, 0.375f, "Back", () => ShowPage(_mainControls), _settingsControls);
	}

	private void AddCheck(ref float y, string text, string toolTip, bool value, Action<bool> set)
	{
		MyGuiControlCheckbox box = new MyGuiControlCheckbox(new Vector2(-0.40f, y), null, toolTip, value);
		box.IsCheckedChanged = b =>
		{
			set(b.IsChecked);
			Settings.Save();
		};
		Add(box, _settingsControls);
		MyGuiControlLabel label = AddLabel(-0.375f, y, text, _settingsControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		label.SetToolTip(toolTip);
		y += 0.05f;
	}

	private void CreateHelp()
	{
		MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, -0.315f), new Vector2(0.84f, 0.62f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		help.AppendText(HelpText);
		Add(help, _helpControls);
		AddButton(0.315f, 0.375f, "Back", () => ShowPage(_mainControls), _helpControls);
	}

	/// <summary>Shows one page's controls and hides the others, without rebuilding the window from inside a click.</summary>
	private void ShowPage(List<MyGuiControlBase> page)
	{
		foreach (MyGuiControlBase control in _mainControls.Concat(_settingsControls).Concat(_helpControls))
		{
			control.Visible = false;
		}
		foreach (MyGuiControlBase control in page)
		{
			control.Visible = true;
		}
	}

	// ---------------------------------------------------------------- rows

	private IEnumerable<LogEntry> AllEntries()
	{
		IEnumerable<LogEntry> alerts = s_view == 0 || s_view == 1 ? AlertLog.Entries : Enumerable.Empty<LogEntry>();
		IEnumerable<LogEntry> logs = s_view == 0 || s_view == 2 ? ScriptLogs.Entries : s_view == 3 ? ScriptLogs.Entries.Where(e => e.IsNew) : Enumerable.Empty<LogEntry>();
		return alerts.Concat(logs);
	}

	private List<LogEntry> Shown()
	{
		List<LogEntry> rows = new List<LogEntry>();
		for (int i = 0; i < _table.RowsCount; i++)
		{
			if (_table.GetRow(i)?.UserData is LogEntry entry)
			{
				rows.Add(entry);
			}
		}
		return rows;
	}

	private void RefreshRows()
	{
		List<LogEntry> all = AllEntries().ToList();
		_gridOptions = new List<string> { AllGrids };
		_gridOptions.AddRange(all.Select(e => e.Grid).Where(g => g.Length > 0).Distinct().OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
		if (!_gridOptions.Contains(s_grid))
		{
			s_grid = AllGrids;
		}
		_gridBox.ItemSelected -= OnGridSelected;
		_gridBox.ClearItems();
		for (int i = 0; i < _gridOptions.Count; i++)
		{
			_gridBox.AddItem(i, _gridOptions[i], sort: false);
		}
		_gridBox.SelectItemByKey(_gridOptions.IndexOf(s_grid), sendEvent: false);
		_gridBox.ItemSelected += OnGridSelected;

		_table.Clear();
		// Newest first; a header click sorts by that column instead.
		foreach (LogEntry entry in all.Where(e => s_grid == AllGrids || e.Grid == s_grid).OrderByDescending(e => e.Time))
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(entry);
			Color? color = entry.IsNew ? Color.Yellow : entry.IsAlert ? (Color?)null : Color.LightGray;
			row.AddCell(new MyGuiControlTable.Cell(entry.TimeText, entry, null, color));
			row.AddCell(new MyGuiControlTable.Cell(entry.Grid, entry, null, color));
			row.AddCell(new MyGuiControlTable.Cell(entry.EventText, entry, null, color));
			row.AddCell(new MyGuiControlTable.Cell(entry.Subject, entry, entry.Subject, color));
			row.AddCell(new MyGuiControlTable.Cell(entry.Detail, entry, entry.Detail, color));
			row.AddCell(new MyGuiControlTable.Cell(entry.Nearby, entry, entry.Nearby, color));
			_table.Add(row);
		}
		int newCount = ScriptLogs.Entries.Count(e => e.IsNew);
		string source = $"{AlertLog.Entries.Count} alert(s), {ScriptLogs.Entries.Count()} script log line(s) from {ScriptLogs.ConstructCount} grid(s)";
		SetStatus($"{_table.RowsCount} shown. {source}{(newCount > 0 ? $", {newCount} new (yellow)" : "")}. Watching {Watcher.GridCount} grid(s).");
		UpdateMuteButton();
	}

	private void OnGridSelected()
	{
		int index = (int)_gridBox.GetSelectedKey();
		if (index >= 0 && index < _gridOptions.Count)
		{
			s_grid = _gridOptions[index];
			RefreshRows();
		}
	}

	// ---------------------------------------------------------------- buttons

	private void CopyToDatapad()
	{
		List<LogEntry> rows = Shown();
		string title = s_grid == AllGrids ? $"Tripwire {DateTime.Now:yyyy-MM-dd HH:mm}" : $"Tripwire {s_grid} {DateTime.Now:yyyy-MM-dd HH:mm}";
		SetStatus(Datapad.Copy(title, rows));
		if (rows.Count > 0)
		{
			Personality.Say("datapad_saved");
		}
	}

	private void MarkAllRead()
	{
		ScriptLogs.MarkRead();
		RefreshRows();
	}

	private void ClearAlerts()
	{
		if (AlertLog.Entries.Count == 0)
		{
			SetStatus("There are no alerts to clear.");
			return;
		}
		MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder("Clear my alerts"), messageText: new StringBuilder($"Delete all {AlertLog.Entries.Count} alert(s) this plugin logged in this world? Script logs aren't touched."), callback: answer =>
		{
			if (answer == MyGuiScreenMessageBox.ResultEnum.YES)
			{
				AlertLog.Clear();
				RefreshRows();
			}
		}));
	}

	private void ToggleMute()
	{
		Alerts.MutedUntil = Muted ? 0 : TripwireSession.Now + 600.0;
		UpdateMuteButton();
	}

	private static bool Muted => Alerts.MutedUntil > TripwireSession.Now;

	private void UpdateMuteButton()
	{
		_muteButton.Text = Muted ? "Unmute Alerts" : "Mute 10 min";
		_muteButton.SetToolTip(Muted ? $"Chat alerts are muted for {Math.Ceiling((Alerts.MutedUntil - TripwireSession.Now) / 60):0} more min. They still go into the log." : "Stops the chat alerts for 10 minutes. They still go into the log.");
	}

	// ---------------------------------------------------------------- helpers

	private void SetStatus(string text)
	{
		_status.Text = text;
	}

	private void Add(MyGuiControlBase control, List<MyGuiControlBase> page)
	{
		Controls.Add(control);
		page.Add(control);
	}

	private MyGuiControlLabel AddLabel(float x, float y, string text, List<MyGuiControlBase> page, MyGuiDrawAlignEnum align)
	{
		MyGuiControlLabel label = new MyGuiControlLabel(new Vector2(x, y), null, text, null, 0.8f, "Blue", align);
		Add(label, page);
		return label;
	}

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick, List<MyGuiControlBase> page)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick());
		Add(button, page);
		return button;
	}

	private static LogEntry Entry(MyGuiControlTable.Cell cell)
	{
		return (LogEntry)cell.UserData;
	}

	private static int Compare(MyGuiControlTable.Cell a, MyGuiControlTable.Cell b, Func<LogEntry, string> text)
	{
		int result = string.Compare(text(Entry(a)), text(Entry(b)), StringComparison.OrdinalIgnoreCase);
		return result != 0 ? result : Entry(b).Time.CompareTo(Entry(a).Time);
	}

	private const string HelpText =
		"WHAT IT DOES\n" +
		"Tripwire tells you when your blocks are damaged, ground down or lost, and shows the logs of the Tripwire programmable block script. The two work on their own or together: the plugin needs no script, and the script needs no plugin.\n\n" +
		"ALERTS (NO SCRIPT NEEDED)\n" +
		"While a grid is loaded on your PC, Tripwire watches your blocks on it: every block of a grid you own (or your faction owns, if you tick that in Settings), and the blocks you own on other grids. When one loses integrity, it says which block on which grid, and whether it's being ground (its build level drops) or damaged. A block that disappears after losing integrity is reported as lost; one that moves to another grid in one piece (a split or merge) isn't. A terminal block that stops being yours shows as Owner changed. Small losses add up until they reach 2% of the block, and the same block is reported at most once a minute. Chat gets one line per grid at most every 30 seconds, summing up what happened; the log here keeps every event.\n" +
		"Tripwire never says where an attacker is: no marker, no distance, no direction, no name, only your own blocks. The one exception is optional and off by default (Settings): it can add the names of players within 50 m of the damaged block, but only players whose name the game's HUD is already drawing for you, in a world that shows player names. It never uses who the game says did the damage.\n" +
		"Grids far away aren't loaded on your PC, so nothing can be seen there until you come back. That's what the script is for.\n\n" +
		"THE SCRIPT\n" +
		"The Tripwire PB script runs on the server in a programmable block, so it keeps watching while you're away. It records visitors its sensors (and turrets) see, before they touch anything, and what happens to the grid's blocks. Its log goes into the PB's Custom Data and into the Custom Data of every block with [Tripwire] in its name (LCDs also show it on screen), so grinding one copy doesn't erase it. The game only tells a script the names of owners, faction members and friends; anyone else is logged as a numbered visitor such as V3 Human, and the script can't know more than that.\n" +
		"Tripwire reads those logs from grids you could open in the terminal right now: you have access to the block, and you're on the grid, standing at it, or connected to it through antennas. New lines since your last visit show in yellow, and chat tells you when a grid with news comes within reach. Closing this window or Mark All Read marks everything as seen.\n\n" +
		"THE WINDOW\n" +
		"Show picks your alerts, the script logs, both, or only the new script lines. Grid shows one grid. Click a header to sort, again to reverse. Copy to Datapad writes the rows shown onto an empty datapad in your inventory (up to 10,000 characters; it never writes over a datapad with text), as a record you can keep or hand to an admin. Mark All Read marks the script logs as seen. Refresh reads the logs again. Clear My Alerts deletes this world's alerts. Mute 10 min silences the chat lines; the log keeps going. Esc closes the window.\n\n" +
		"WHAT THE SERVER SEES\n" +
		"Alerts and reading logs send nothing: they use what the game has already sent to your PC. Copy to Datapad sends the same request as editing a datapad by hand.\n\n" +
		"COMMANDS\n" +
		"/tripwire opens this window. /tripwire mute 30 mutes the chat alerts for 30 minutes (10 if you leave out the number); /tripwire unmute turns them back on. /tripwire chat off, quiet, normal or chatty sets how much it comments. /tripwire name and a name changes what its chat lines show under; on its own it puts it back. /tim opens whichever of our plugin windows you used last, and the list at the top left of this window switches to another of our plugins.\n\n" +
		"FILES\n" +
		"Settings are in Tripwire_Settings.txt, this world's alerts in Tripwire_<world>_Alerts.txt and what you've already read in Tripwire_<world>_Visits.txt, all in %AppData%\\SpaceEngineers\\Storage.";
}
