using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Tally;

/// <summary>
/// Tally's window: pick a source, tally it, and read the components, ingots and ore as a sortable table, or write
/// them to datapads. Help on its own page. Pages are shown and hidden, never rebuilt from inside a click.
/// </summary>
public partial class TallyScreen : MyGuiScreenBase
{
	public enum Page
	{
		Tally,
		Help
	}

	private enum Tab
	{
		Components,
		Ingots,
		Ore
	}

	private const float ButtonsY = 0.39f;

	private static readonly string[] Kinds = { Source.Projector, Source.Library, Source.Grid };

	private readonly Dictionary<Page, List<MyGuiControlBase>> _pages = new Dictionary<Page, List<MyGuiControlBase>>();

	private Page _page;

	private Tab _tab = Tab.Components;

	private MyGuiControlCombobox _kind;

	private MyGuiControlCombobox _source;

	private MyGuiControlTextbox _find;

	private MyGuiControlCheckbox _stillNeeded;

	private MyGuiControlCheckbox _carried;

	private MyGuiControlCheckbox _cargo;

	private MyGuiControlTextbox _yield;

	private MyGuiControlCombobox _pad;

	private readonly Dictionary<Tab, MyGuiControlButton> _tabs = new Dictionary<Tab, MyGuiControlButton>();

	private MyGuiControlTable _table;

	private MyGuiControlMultilineText _status;

	private List<Source> _sources = new List<Source>();

	private Bill _bill;

	private bool _sortedOnce;

	private bool _closed;

	/// <summary>Set while the controls are being filled, so their change events don't save or re-tally.</summary>
	private bool _filling;

	public TallyScreen(Page page)
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.92f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		_page = page;
		_bill = TallySession.LastBill;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "TallyScreen";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Tally");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);
		_pages.Clear();
		_tabs.Clear();
		foreach (Page page in Enum.GetValues(typeof(Page)))
		{
			_pages[page] = new List<MyGuiControlBase>();
		}
		BuildTallyPage();
		BuildHelpPage();
		Page wanted = _page;
		_page = Page.Tally;
		ShowPage(wanted);
	}

	protected override void OnClosed()
	{
		_closed = true;
		Settings.Save();
		base.OnClosed();
	}

	private void BuildTallyPage()
	{
		List<MyGuiControlBase> page = _pages[Page.Tally];
		_filling = true;

		RightLabel(-0.355f, "Tally:", page);
		_kind = AddCombo(-0.31f, -0.355f, 0.22f, "What to count: a projector's projection, a blueprint from your F10 library, or one of your grids.", page);
		_kind.AddItem(0, "Projector");
		_kind.AddItem(1, "Blueprint (F10)");
		_kind.AddItem(2, "My grid");
		_kind.SelectItemByKey(Math.Max(0, Array.IndexOf(Kinds, Settings.Kind)));
		_kind.ItemSelected += () =>
		{
			if (!_filling)
			{
				Settings.Kind = Kinds[(int)_kind.GetSelectedKey()];
				FillSources();
			}
		};
		_source = AddCombo(-0.08f, -0.355f, 0.5f, "The projector, blueprint or grid to tally.", page);

		RightLabel(-0.31f, "Find:", page);
		_find = AddTextbox(-0.31f, -0.31f, 0.22f, "Type part of a name to shorten the list.", page);
		_find.TextChanged += _ =>
		{
			if (!_filling)
			{
				FillSources();
			}
		};
		AddButton(0.0f, -0.31f, "Refresh list", FillSources, page).SetToolTip("Looks again for projectors, blueprints and grids.");

		_stillNeeded = AddCheckbox(-0.31f, -0.26f, "Still needed", "Projector: only what isn't built yet, plus what half-built blocks lack. Grid: what unfinished or damaged blocks lack. Off: the full cost. Blueprints are always the full cost.", Settings.StillNeeded, page);
		_carried = AddCheckbox(-0.08f, -0.26f, "Minus what I carry", "Takes off what's in your own inventory: components you carry aren't made, so their ingots and ore aren't needed either.", Settings.Carried, page);
		_cargo = AddCheckbox(0.17f, -0.26f, "Minus the grid's cargo", "Projector or grid: also takes off what's in the inventories of that grid and what's docked to it (blocks you have access to). The server only keeps those up to date while you have one open in the terminal, so open the grid's inventory first for current numbers.", Settings.Cargo, page);
		foreach (MyGuiControlCheckbox box in new[] { _stillNeeded, _carried, _cargo })
		{
			box.IsCheckedChanged += _ => OptionsChanged();
		}

		RightLabel(-0.21f, "Refinery yield %:", page);
		_yield = AddTextbox(-0.31f, -0.21f, 0.1f, "The yield of the refinery that makes your ingots, in percent, as its terminal shows it (Effectiveness), e.g. 141 for a vanilla refinery with four yield modules. Modded refineries differ. Leave it empty for this world's best refinery without modules.", page);
		_yield.Text = Settings.YieldPercent > 0 ? Settings.YieldPercent.ToString("0.##", CultureInfo.InvariantCulture) : "";
		_yield.TextChanged += box =>
		{
			if (TryYield(out _))
			{
				OptionsChanged();
			}
		};
		page.Add(AddControl(new MyGuiControlLabel(new Vector2(-0.07f, -0.21f), null, "Datapad:", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER)));
		_pad = AddCombo(-0.06f, -0.21f, 0.48f, "Which datapad in your inventory gets the list. Auto uses an empty one, or the ones already holding this list. Tally can't make datapads.", page);

		float x = -0.34f;
		foreach (Tab tab in Enum.GetValues(typeof(Tab)))
		{
			Tab shown = tab;
			_tabs[tab] = AddButton(x, -0.155f, tab.ToString(), () => ShowTab(shown), page);
			x += 0.17f;
		}

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.125f),
			Size = new Vector2(0.84f, 0.4f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 4,
			VisibleRowsCount = 9
		};
		_table.SetCustomColumnWidths(new float[] { 0.46f, 0.18f, 0.18f, 0.18f });
		_table.SetColumnName(0, new StringBuilder("Item"));
		_table.SetColumnName(1, new StringBuilder("Needed"));
		_table.SetColumnName(2, new StringBuilder("Have"));
		_table.SetColumnName(3, new StringBuilder("Still to get"));
		_table.SetColumnComparison(0, (a, b) => string.Compare(Item(a)?.Name, Item(b)?.Name, StringComparison.OrdinalIgnoreCase));
		_table.SetColumnComparison(1, (a, b) => (Item(a)?.Needed ?? 0).CompareTo(Item(b)?.Needed ?? 0));
		_table.SetColumnComparison(2, (a, b) => (Item(a)?.Have ?? 0).CompareTo(Item(b)?.Have ?? 0));
		_table.SetColumnComparison(3, (a, b) => (Item(a)?.Short ?? 0).CompareTo(Item(b)?.Short ?? 0));
		page.Add(AddControl(_table));

		_status = new MyGuiControlMultilineText(new Vector2(0f, 0.285f), new Vector2(0.84f, 0.075f), null, "Blue", 0.75f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		page.Add(AddControl(_status));

		AddButton(-0.34f, ButtonsY, "Tally", () => Calculate(), page).SetToolTip("Counts the picked projector, blueprint or grid and works out the components, ingots and ore.");
		AddButton(-0.17f, ButtonsY, "Write datapad", WriteDatapad, page).SetToolTip("Writes the list shown onto the datapad picked above (more than one if it's long).");
		AddButton(0f, ButtonsY, "Copy", Copy, page).SetToolTip("Copies the list as text, the same as on the datapad.");
		AddButton(0.34f, ButtonsY, "Help", () => ShowPage(Page.Help), page);
		_filling = false;
	}

	private static Line Item(MyGuiControlTable.Cell cell)
	{
		return cell?.Row?.UserData as Line;
	}

	/// <summary>Refills the source list for the kind picked, keeping the pick if it's still there.</summary>
	private void FillSources()
	{
		string kept = SelectedSource()?.Key ?? _bill?.SourceKey;
		_filling = true;
		try
		{
			string kind = Kinds[(int)Math.Max(0, _kind.GetSelectedKey())];
			List<Source> all = kind == Source.Projector ? Sources.Projectors() : kind == Source.Library ? Sources.Library() : Sources.OwnGrids();
			string find = _find.Text.Trim();
			_sources = find.Length == 0 ? all : all.Where(s => s.Label.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			_source.ClearItems();
			for (int i = 0; i < _sources.Count; i++)
			{
				string label = _sources[i].Label;
				_source.AddItem(i, label.Length > 75 ? label.Substring(0, 75) + "..." : label);
			}
			if (_sources.Count == 0)
			{
				_source.AddItem(-1, find.Length > 0 ? "(nothing matches)" : kind == Source.Projector ? "(no projector with a projection you have access to)" : kind == Source.Library ? "(no blueprints in your F10 folder)" : "(no grid of yours nearby)");
			}
			int index = Math.Max(0, _sources.FindIndex(s => s.Key == kept));
			_source.SelectItemByKey(_sources.Count == 0 ? -1 : index);
			_stillNeeded.Enabled = kind != Source.Library;
			_cargo.Enabled = kind != Source.Library;
		}
		finally
		{
			_filling = false;
		}
	}

	private Source SelectedSource()
	{
		long key = _source?.GetSelectedKey() ?? -1;
		return key >= 0 && key < _sources.Count ? _sources[(int)key] : null;
	}

	private void FillPads()
	{
		long selected = _pad.GetSelectedKey();
		_pad.ClearItems();
		_pad.AddItem(0, "Auto: an empty datapad, or the ones holding this list");
		List<Datapads.Pad> pads = Datapads.List();
		foreach (Datapads.Pad pad in pads)
		{
			string label = pad.Label;
			_pad.AddItem(pad.ItemId, label.Length > 70 ? label.Substring(0, 70) + "..." : label);
		}
		if (pads.Count == 0)
		{
			_pad.AddItem(-1, "(no datapad in your inventory; Tally can't make one)");
		}
		_pad.SelectItemByKey(selected > 0 && pads.Any(p => p.ItemId == selected) ? selected : 0);
	}

	/// <summary>The yield box: empty is 0 (this world's best refinery), else a percentage above 0 and up to 1000.</summary>
	private bool TryYield(out double percent)
	{
		string text = _yield.Text.Trim().TrimEnd('%').Trim().Replace(',', '.');
		if (text.Length == 0)
		{
			percent = 0;
			return true;
		}
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent) && percent > 0 && percent <= 1000;
	}

	private TallySession.Options ReadOptions()
	{
		Settings.StillNeeded = _stillNeeded.IsChecked;
		Settings.Carried = _carried.IsChecked;
		Settings.Cargo = _cargo.IsChecked;
		if (TryYield(out double yield))
		{
			Settings.YieldPercent = yield;
		}
		return new TallySession.Options();
	}

	/// <summary>A changed tick box or module count re-tallies what's shown, so the table always matches the boxes.</summary>
	private void OptionsChanged()
	{
		if (_filling)
		{
			return;
		}
		ReadOptions();
		if (_bill != null && SelectedSource() != null)
		{
			Calculate();
		}
	}

	private void Calculate(Action then = null)
	{
		Source source = SelectedSource();
		if (source == null)
		{
			SetStatus("Pick a projector, blueprint or grid first. Refresh list looks again.");
			return;
		}
		if (!TryYield(out _))
		{
			SetStatus("Type the refinery yield as a percentage (for example 100 or 141.4), or leave it empty for this world's best refinery.");
			return;
		}
		TallySession.Options options = ReadOptions();
		if (source.Kind == Source.Library)
		{
			options.StillNeeded = false;
			options.Cargo = false;
		}
		SetStatus(source.Kind == Source.Library ? $"Reading \"{source.Label}\"..." : "Counting...");
		TallySession.Run(source, options, (bill, error) =>
		{
			if (_closed)
			{
				return;
			}
			if (bill == null)
			{
				SetStatus(error);
				return;
			}
			_bill = bill;
			ShowBill();
			then?.Invoke();
		});
	}

	private void ShowBill()
	{
		ShowTab(_tab);
		if (_bill == null)
		{
			SetStatus("Pick a projector (one you have access to), a blueprint from your F10 library or one of your grids, then press Tally.");
			return;
		}
		string text = Text.Summary(_bill) + " " + Text.Settings(_bill);
		if (_bill.Subtracted)
		{
			text += $" Have: what's in {_bill.HaveFrom}.";
		}
		if (_bill.NoRecipe.Count > 0)
		{
			text += $" No recipe in this world for: {string.Join(", ", _bill.NoRecipe.Select(l => l.Name))}.";
		}
		SetStatus(text);
	}

	private void ShowTab(Tab tab)
	{
		_tab = tab;
		foreach (KeyValuePair<Tab, MyGuiControlButton> pair in _tabs)
		{
			pair.Value.Text = pair.Key == tab ? $"[ {pair.Key} ]" : pair.Key.ToString();
		}
		List<Line> lines = _bill == null ? new List<Line>() : tab == Tab.Components ? _bill.Components : tab == Tab.Ingots ? _bill.Ingots : _bill.Ores;
		bool subtracted = _bill?.Subtracted == true;
		_table.SetColumnName(1, new StringBuilder(tab == Tab.Components ? "Needed" : "Needed (kg)"));
		_table.Clear();
		foreach (Line line in lines)
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(line);
			row.AddCell(new MyGuiControlTable.Cell(line.Name, line));
			row.AddCell(new MyGuiControlTable.Cell(Text.Amount(line.Needed), line));
			row.AddCell(new MyGuiControlTable.Cell(subtracted ? Text.Have(line.Have) : "-", line));
			row.AddCell(new MyGuiControlTable.Cell(subtracted ? Text.Amount(line.Short) : "-", line));
			_table.Add(row);
		}
		if (_sortedOnce)
		{
			_table.Sort(switchSort: false);
		}
		else
		{
			_table.SortByColumn(1, MyGuiControlTable.SortStateEnum.Descending, switchSort: false);
			_sortedOnce = true;
		}
	}

	private void SetStatus(string text)
	{
		_status.Clear();
		_status.AppendText(text ?? "");
	}

	private void WriteDatapad()
	{
		long key = _pad.GetSelectedKey();
		uint picked = key > 0 ? (uint)key : 0;
		if (SelectedSource() == null && _bill != null)
		{
			Datapads.Request(_bill, picked, Report);
			return;
		}
		// Tally again first, so the datapad matches what's built and carried right now.
		Calculate(() => Datapads.Request(_bill, picked, Report));
	}

	private void Copy()
	{
		if (_bill == null)
		{
			SetStatus("Tally something first.");
			return;
		}
		SetStatus(Text.Copy(string.Join("\r\n", Text.Lines(_bill))) ? "Copied the list to the clipboard." : "The clipboard couldn't be reached.");
	}

	/// <summary>A result line in the window (while it's open) and in chat.</summary>
	private void Report(string line)
	{
		TallySession.Say(line);
		if (!_closed)
		{
			_status.AppendText("\n" + line);
			FillPads();
		}
	}

	private void BuildHelpPage()
	{
		List<MyGuiControlBase> page = _pages[Page.Help];
		MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, -0.37f), new Vector2(0.84f, 0.69f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		help.AppendText(HelpText);
		page.Add(AddControl(help));
		AddButton(0.34f, ButtonsY, "Back", () => ShowPage(Page.Tally), page);
	}

	private void ShowPage(Page page)
	{
		_page = page;
		foreach (List<MyGuiControlBase> controls in _pages.Values)
		{
			foreach (MyGuiControlBase control in controls)
			{
				control.Visible = false;
			}
		}
		foreach (MyGuiControlBase control in _pages[page])
		{
			control.Visible = true;
		}
		if (page == Page.Tally)
		{
			try
			{
				FillSources();
				FillPads();
				ShowBill();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Tally] Window: {ex}");
			}
		}
	}

	private MyGuiControlBase AddControl(MyGuiControlBase control)
	{
		Controls.Add(control);
		return control;
	}

	private void RightLabel(float y, string text, List<MyGuiControlBase> page)
	{
		page.Add(AddControl(new MyGuiControlLabel(new Vector2(-0.32f, y), null, text, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER)));
	}

	private MyGuiControlCombobox AddCombo(float x, float y, float width, string tip, List<MyGuiControlBase> page)
	{
		MyGuiControlCombobox box = new MyGuiControlCombobox(new Vector2(x, y), new Vector2(width, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, openAreaItemsCount: 12);
		box.SetToolTip(tip);
		page.Add(AddControl(box));
		return box;
	}

	private MyGuiControlTextbox AddTextbox(float x, float y, float width, string tip, List<MyGuiControlBase> page)
	{
		MyGuiControlTextbox box = new MyGuiControlTextbox(new Vector2(x, y), "", 100)
		{
			Size = new Vector2(width, 0.04f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		box.SetToolTip(tip);
		page.Add(AddControl(box));
		return box;
	}

	private MyGuiControlCheckbox AddCheckbox(float x, float y, string text, string tip, bool value, List<MyGuiControlBase> page)
	{
		MyGuiControlCheckbox box = new MyGuiControlCheckbox(new Vector2(x, y), null, tip, value, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		page.Add(AddControl(box));
		page.Add(AddControl(new MyGuiControlLabel(new Vector2(x + 0.04f, y), null, text, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER)));
		return box;
	}

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick, List<MyGuiControlBase> page)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick());
		page.Add(AddControl(button));
		return button;
	}
}
