using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digby;

/// <summary>
/// Digby's window: the Plan page (From, Target, Depth, rig line, markers, datapad), the Library page
/// (DigbyScreen.Library.cs) and Help. Pages are shown and hidden, never rebuilt from inside a click.
/// </summary>
public partial class DigbyScreen : MyGuiScreenBase
{
	public enum Page
	{
		Plan,
		Library,
		Help
	}

	private const float ButtonsY = 0.39f;

	private const float ButtonsY2 = 0.335f;

	private readonly Dictionary<Page, List<MyGuiControlBase>> _pages = new Dictionary<Page, List<MyGuiControlBase>>();

	private Page _page;

	/// <summary>The page Help's Back button returns to.</summary>
	private Page _beforeHelp = Page.Plan;

	private int _frames;

	private MyGuiControlLabel _where;

	private MyGuiControlCombobox _from;

	private MyGuiControlCombobox _target;

	private MyGuiControlTextbox _fromText;

	private MyGuiControlTextbox _targetText;

	private MyGuiControlTextbox _depth;

	private MyGuiControlCheckbox _marker;

	private MyGuiControlCheckbox _server;

	private MyGuiControlCheckbox _start;

	private MyGuiControlCombobox _rigA;

	private MyGuiControlCombobox _rigB;

	private MyGuiControlCombobox _rigUp;

	private MyGuiControlCheckbox _rigSmall;

	private MyGuiControlCombobox _pad;

	private MyGuiControlMultilineText _result;

	// Point specs behind the dropdown items: "here", "" (= the typed GPS) or a GPS string.
	private readonly List<string> _fromSpecs = new List<string>();

	private readonly List<string> _targetSpecs = new List<string>();

	private readonly List<string> _rigASpecs = new List<string>();

	private readonly List<string> _rigBSpecs = new List<string>();

	private readonly List<string> _rigUpSpecs = new List<string>();

	private DigPlanner.Plan _saved;

	private bool _closed;

	public DigbyScreen(Page page)
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.92f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		_page = page;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "DigbyScreen";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Digby");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);
		_pages.Clear();
		foreach (Page page in Enum.GetValues(typeof(Page)))
		{
			_pages[page] = new List<MyGuiControlBase>();
		}
		BuildPlanPage();
		BuildLibraryPage();
		BuildHelpPage();
		Page wanted = _page;
		_page = Page.Plan;
		ShowPage(wanted);
	}

	protected override void OnClosed()
	{
		_closed = true;
		base.OnClosed();
	}

	public override bool Update(bool hasFocus)
	{
		if (++_frames % 60 == 1 && _page == Page.Plan)
		{
			try
			{
				_where.Text = "You are " + Planets.WhereYouAre() + ".";
			}
			catch (Exception)
			{
			}
		}
		return base.Update(hasFocus);
	}

	private void BuildPlanPage()
	{
		List<MyGuiControlBase> page = _pages[Page.Plan];
		_where = AddLabel(-0.42f, -0.355f, "", page);
		_where.ColorMask = new Vector4(0.8f, 0.8f, 0.8f, 1f);

		RightLabel(-0.31f, "From:", page);
		_from = AddCombo(-0.31f, -0.31f, 0.6f, "Where the tunnel starts: where you are now, a GPS from your list, a library point, or the GPS typed in the box below.", page);
		RightLabel(-0.265f, "or GPS:", page);
		_fromText = AddTextbox(-0.31f, -0.265f, 0.6f, "Paste a GPS here (GPS:Name:X:Y:Z:) and pick \"The GPS typed below\" above.", page);
		RightLabel(-0.21f, "Target:", page);
		_target = AddCombo(-0.31f, -0.21f, 0.6f, "Roughly the spot on the surface above the deposit (in space: the deposit itself): a GPS from your list, a library point, or the GPS typed below.", page);
		RightLabel(-0.165f, "or GPS:", page);
		_targetText = AddTextbox(-0.31f, -0.165f, 0.6f, "Paste the target GPS here and pick \"The GPS typed below\" above.", page);
		RightLabel(-0.11f, "Depth (m):", page);
		_depth = AddTextbox(-0.31f, -0.11f, 0.12f, "How far below the surface at the target the deposit is, in metres. 0 means at the surface. Ignored in space.", page);
		AddButton(0.31f, -0.11f, "Clear markers", ClearMarkers, page).SetToolTip("Removes this plan's markers from your GPS list (local and server).");

		_marker = AddCheckbox(-0.31f, -0.06f, "Markers", "Put markers on the end point, the turns and (if ticked) the start.", true, page);
		_server = AddCheckbox(0.0f, -0.06f, "As server GPS", "Make them server GPS markers instead of local ones. Local markers never leave your PC and are gone when you leave the world.", false, page);
		_start = AddCheckbox(0.2f, -0.06f, "At the start too", "Also put a Dig start marker where the tunnel starts, so the pair shows the line to dig along.", true, page);

		RightLabel(-0.01f, "Rig P1:", page);
		_rigA = AddCombo(-0.31f, -0.01f, 0.29f, "Optional: a GPS on your grid. P1 -> P2 is the grid's forward axis (for example two blocks along the drill rig). With it, the plan also gives the tunnel as turns and blocks along your grid.", page);
		page.Add(AddControl(new MyGuiControlLabel(new Vector2(0.02f, -0.01f), null, "P2:", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER)));
		_rigB = AddCombo(0.03f, -0.01f, 0.29f, "The second GPS on your grid, further along its forward axis.", page);
		RightLabel(0.035f, "Up point:", page);
		_rigUp = AddCombo(-0.31f, 0.035f, 0.29f, "Optional on a planet: a GPS straight above P1 on your grid. Leave it on (none) if the grid is level. Needed in space.", page);
		_rigSmall = AddCheckbox(0.03f, 0.035f, "Small grid", "The grid is a small grid (0.5 m blocks instead of 2.5 m).", false, page);

		RightLabel(0.085f, "Datapad:", page);
		_pad = AddCombo(-0.31f, 0.085f, 0.6f, "Which datapad in your inventory gets the plan. Auto uses an empty one, or the ones already holding this plan. Digby can't make datapads.", page);

		_result = new MyGuiControlMultilineText(new Vector2(0f, 0.12f), new Vector2(0.84f, 0.2f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		page.Add(AddControl(_result));

		AddButton(-0.34f, ButtonsY, "Calculate", () => Calculate(), page).SetToolTip("Works out the plan and makes its markers.");
		AddButton(-0.17f, ButtonsY, "Write datapad", WriteDatapad, page).SetToolTip("Calculates, then writes the plan onto the datapad picked above, with a GPS line for each marker.");
		AddButton(0f, ButtonsY, "Save to library", SaveToLibrary, page).SetToolTip("Moves this plan's markers into Digby's library (kept for this world) and off your GPS list.");
		AddButton(0.17f, ButtonsY, "Library", () => ShowPage(Page.Library), page);
		AddButton(0.34f, ButtonsY, "Help", () => ShowPage(Page.Help), page);

		_saved = DigbySession.LastPlan ?? Settings.LoadPlan();
		_fromText.Text = _saved.From.Equals("here", StringComparison.OrdinalIgnoreCase) ? "" : _saved.From;
		_targetText.Text = _saved.Target;
		_depth.Text = _saved.Depth.ToString("0.#", CultureInfo.InvariantCulture);
		_marker.IsChecked = _saved.Marker != "none";
		_server.IsChecked = _saved.Marker == "server";
		_start.IsChecked = _saved.StartMarker;
		_rigSmall.IsChecked = _saved.SmallGrid;
		_result.AppendText(DigbySession.LastResult != null ? string.Join("\n\n", DigbySession.LastResult.Lines) : "Pick where you dig from and the spot on the surface above the deposit, type how deep it is, then press Calculate. Your last plan in this world is filled in.");
		FillPoints();
	}

	/// <summary>Refills the point lists from the GPS list and the library, keeping what was picked.</summary>
	private void FillPoints()
	{
		bool fromTyped = _fromText.Text.Length > 0;
		bool targetTyped = _targetText.Text.Length > 0;
		_from.ClearItems();
		_target.ClearItems();
		_fromSpecs.Clear();
		_targetSpecs.Clear();
		AddPoint(_from, _fromSpecs, "Here (where you are now)", "here");
		AddPoint(_from, _fromSpecs, "The GPS typed below", "");
		AddPoint(_target, _targetSpecs, "The GPS typed below", "");
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (player != null)
		{
			foreach (IMyGps gps in MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
			{
				string spec = DigPlanner.ToGps(gps.Name, gps.Coords);
				AddPoint(_from, _fromSpecs, "GPS: " + gps.Name, spec);
				AddPoint(_target, _targetSpecs, "GPS: " + gps.Name, spec);
			}
		}
		foreach (LibraryEntry entry in global::Digby.Library.Entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
		{
			string spec = DigPlanner.ToGps(entry.Name, entry.Position);
			AddPoint(_from, _fromSpecs, "Library: " + entry.Name, spec);
			AddPoint(_target, _targetSpecs, "Library: " + entry.Name, spec);
		}
		_from.SelectItemByKey(fromTyped ? 1 : 0);
		// With no saved target, start the target list on the first GPS rather than the empty box.
		_target.SelectItemByKey(!targetTyped && _targetSpecs.Count > 1 ? 1 : 0);
		FillRig(_rigA, _rigASpecs, _saved?.RigA);
		FillRig(_rigB, _rigBSpecs, _saved?.RigB);
		FillRig(_rigUp, _rigUpSpecs, _saved?.RigUp);
		FillPads();
	}

	private void FillPads()
	{
		long selected = _pad.GetSelectedKey();
		_pad.ClearItems();
		_pad.AddItem(0, "Auto: an empty datapad, or the ones holding this plan");
		List<Datapads.Pad> pads = Datapads.List();
		foreach (Datapads.Pad pad in pads)
		{
			string label = pad.Label;
			_pad.AddItem(pad.ItemId, label.Length > 70 ? label.Substring(0, 70) + "..." : label);
		}
		if (pads.Count == 0)
		{
			_pad.AddItem(-1, "(no datapad in your inventory; Digby can't make one)");
		}
		_pad.SelectItemByKey(selected > 0 && pads.Any(p => p.ItemId == selected) ? selected : 0);
	}

	private static void AddPoint(MyGuiControlCombobox box, List<string> specs, string text, string spec)
	{
		box.AddItem(specs.Count, text.Length > 60 ? text.Substring(0, 60) + "..." : text);
		specs.Add(spec);
	}

	private static string Spec(MyGuiControlCombobox box, List<string> specs, MyGuiControlTextbox typed)
	{
		long index = box.GetSelectedKey();
		string spec = index >= 0 && index < specs.Count ? specs[(int)index] : "";
		return spec.Length > 0 ? spec : typed.Text.Trim();
	}

	/// <summary>(none), then every GPS and library point; selects the saved point, adding it if it is no longer listed.</summary>
	private void FillRig(MyGuiControlCombobox box, List<string> specs, string saved)
	{
		long selected = box.GetSelectedKey();
		bool first = specs.Count == 0;
		box.ClearItems();
		specs.Clear();
		AddPoint(box, specs, "(none)", "");
		for (int i = 2; i < _fromSpecs.Count; i++)
		{
			AddPoint(box, specs, _from.GetItemByIndex(i).Value.ToString(), _fromSpecs[i]);
		}
		int index = 0;
		if (first && !string.IsNullOrEmpty(saved))
		{
			index = specs.IndexOf(saved);
			if (index < 0)
			{
				index = specs.Count;
				AddPoint(box, specs, "Saved: " + (DigPlanner.TryParseGps(saved, out string name, out _) ? name : saved), saved);
			}
		}
		else if (!first && selected >= 0 && selected < specs.Count)
		{
			index = (int)selected;
		}
		box.SelectItemByKey(index);
	}

	private static string RigSpec(MyGuiControlCombobox box, List<string> specs)
	{
		long index = box.GetSelectedKey();
		return index > 0 && index < specs.Count ? specs[(int)index] : "";
	}

	/// <summary>Works out the plan from the page. Returns false (with the reason shown) if it couldn't.</summary>
	private bool Calculate()
	{
		_result.Clear();
		if (!double.TryParse(_depth.Text.Trim().TrimEnd('m', 'M'), NumberStyles.Float, CultureInfo.InvariantCulture, out double depth))
		{
			_result.AppendText("Type the depth as a number of metres, e.g. 120.");
			return false;
		}
		DigPlanner.Plan plan = new DigPlanner.Plan
		{
			From = Spec(_from, _fromSpecs, _fromText),
			Target = Spec(_target, _targetSpecs, _targetText),
			Depth = depth,
			Marker = !_marker.IsChecked ? "none" : _server.IsChecked ? "server" : "local",
			StartMarker = _start.IsChecked,
			RigA = RigSpec(_rigA, _rigASpecs),
			RigB = RigSpec(_rigB, _rigBSpecs),
			RigUp = RigSpec(_rigUp, _rigUpSpecs),
			SmallGrid = _rigSmall.IsChecked
		};
		try
		{
			_result.AppendText(string.Join("\n\n", DigbySession.Run(plan)));
			return DigbySession.LastPlan == plan;
		}
		catch (Exception ex)
		{
			_result.AppendText("The plan could not be calculated: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Digby] Dig planner: {ex}");
			return false;
		}
	}

	private void WriteDatapad()
	{
		if (!Calculate())
		{
			return;
		}
		long key = _pad.GetSelectedKey();
		Datapads.Request(DigbySession.LastResult, key > 0 ? (uint)key : 0, Report);
	}

	/// <summary>A result line in the window (while it's open) and in chat.</summary>
	private void Report(string line)
	{
		DigbySession.Say(line);
		if (!_closed)
		{
			_result.AppendText("\n\n" + line);
			FillPads();
		}
	}

	private void SaveToLibrary()
	{
		DigPlanner.Result result = DigbySession.LastResult;
		if (result == null || !result.Ok)
		{
			Report("Calculate a plan first.");
			return;
		}
		List<Marker> markers = result.AllMarkers();
		foreach (Marker marker in markers)
		{
			global::Digby.Library.Add(marker.Name, marker.Position, "Plan: " + result.PlanName, marker.Color, replaceSameName: true);
		}
		global::Digby.Library.Save();
		int removed = DigPlanner.ClearMarkers();
		FillPoints();
		Report($"Saved {markers.Count} plan points to the library" + (removed > 0 ? " and took the markers off your GPS list" : "") + ". Show on HUD in the Library brings them back.");
	}

	private void ClearMarkers()
	{
		Report($"{DigPlanner.ClearMarkers()} dig marker(s) removed.");
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
		AddButton(0.34f, ButtonsY, "Back", () => ShowPage(_beforeHelp), page);
	}

	private void ShowPage(Page page)
	{
		if (page == Page.Help && _page != Page.Help)
		{
			_beforeHelp = _page;
		}
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
		if (page == Page.Plan && _from != null)
		{
			FillPoints();
			_frames = 0;
		}
		else if (page == Page.Library)
		{
			ShowLibrary();
		}
	}

	private MyGuiControlBase AddControl(MyGuiControlBase control)
	{
		Controls.Add(control);
		return control;
	}

	private MyGuiControlLabel AddLabel(float x, float y, string text, List<MyGuiControlBase> page)
	{
		MyGuiControlLabel label = new MyGuiControlLabel(new Vector2(x, y), null, text, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		page.Add(AddControl(label));
		return label;
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
		MyGuiControlTextbox box = new MyGuiControlTextbox(new Vector2(x, y), "", 200)
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
