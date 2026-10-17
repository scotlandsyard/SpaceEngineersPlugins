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

namespace QuietSkies;

/// <summary>The window: one row per kind of signal marker with its mode and Nearby only distance, and a Help page.</summary>
public class QuietSkiesScreen : MyGuiScreenBase
{
	private const float HeaderY = -0.17f;

	private const float FirstRowY = -0.11f;

	private const float RowStep = 0.07f;

	private const float ButtonsY = 0.235f;

	private readonly List<MyGuiControlBase> _mainControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _helpControls = new List<MyGuiControlBase>();

	private readonly Dictionary<SignalKind, MyGuiControlLabel> _counts = new Dictionary<SignalKind, MyGuiControlLabel>();

	private MyGuiControlLabel _status;

	private int _frames;

	public QuietSkiesScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.72f, 0.6f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "QuietSkiesScreen";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Quiet Skies");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);
		_mainControls.Clear();
		_helpControls.Clear();
		_counts.Clear();

		AddLabel(-0.32f, HeaderY, "Marker", _mainControls);
		AddLabel(-0.09f, HeaderY, "On the HUD", _mainControls);
		AddLabel(0.1f, HeaderY, "Nearby km", _mainControls);
		AddLabel(0.21f, HeaderY, "Markers", _mainControls);

		float y = FirstRowY;
		foreach (SignalKind kind in Signals.Kinds)
		{
			AddRow(kind, y);
			y += RowStep;
		}

		_status = AddLabel(-0.32f, 0.135f, "", _mainControls);
		AddLabel(-0.32f, 0.175f, "Hide also takes markers off the HUD on your other PCs. Nearby only is this PC only.", _mainControls).ColorMask = new Vector4(0.7f, 0.7f, 0.7f, 1f);
		AddButton(0.24f, ButtonsY, "Help", () => ShowPage(_helpControls), _mainControls);

		MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, -0.2f), new Vector2(0.66f, 0.4f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		help.AppendText(HelpText);
		Add(help, _helpControls);
		AddButton(0.24f, ButtonsY, "Back", () => ShowPage(_mainControls), _helpControls);

		ShowPage(_mainControls);
		RefreshCounts();
	}

	private void AddRow(SignalKind kind, float y)
	{
		AddLabel(-0.32f, y, Signals.Name(kind), _mainControls);

		MyGuiControlCombobox combo = new MyGuiControlCombobox(new Vector2(-0.09f, y), new Vector2(0.17f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		foreach (SignalMode mode in new[] { SignalMode.Show, SignalMode.Hide, SignalMode.Nearby })
		{
			combo.AddItem((long)mode, Signals.ModeName(mode), sort: false);
		}
		combo.SelectItemByKey((long)Settings.Mode(kind), sendEvent: false);
		combo.SetToolTip("Show: as the game does. Hide: off the HUD with the game's own Show on HUD switch; still in the G list. Nearby only: on the HUD only within the distance to the right.");
		combo.ItemSelected += () =>
		{
			QuietSkiesSession.SetMode(kind, (SignalMode)combo.GetSelectedKey());
			RefreshCounts();
		};
		Add(combo, _mainControls);

		MyGuiControlTextbox km = new MyGuiControlTextbox(new Vector2(0.1f, y), QuietSkiesSession.FormatKm(Settings.Km(kind)), 6)
		{
			Size = new Vector2(0.08f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		km.SetToolTip($"Distance for Nearby only, {Settings.MinKm:0} to {Settings.MaxKm:0} km. Measured from you (or the ship you control).");
		km.TextChanged += box =>
		{
			string text = (box.Text ?? "").Trim().Replace(',', '.');
			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0)
			{
				Settings.NearbyKm[kind] = Settings.ClampKm(value);
				Settings.SaveWorld();
				Filter.ScanSoon();
			}
		};
		Add(km, _mainControls);

		_counts[kind] = AddLabel(0.21f, y, "", _mainControls);
	}

	public override bool Update(bool hasFocus)
	{
		if (++_frames % 30 == 0)
		{
			RefreshCounts();
		}
		return base.Update(hasFocus);
	}

	private void RefreshCounts()
	{
		try
		{
			foreach (KeyValuePair<SignalKind, MyGuiControlLabel> pair in _counts)
			{
				Filter.Count(pair.Key, out int total, out int onHud);
				pair.Value.Text = total == 0 ? "none" : $"{total}, {onHud} shown";
			}
			_status.Text = Filter.Queued > 0 ? $"Sending {Filter.Queued} change(s) to the server..." : $"Show on HUD requests sent this session: {Filter.RequestsSent}";
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] Window: {ex.Message}");
		}
	}

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

	private void AddButton(float x, float y, string text, Action onClick, List<MyGuiControlBase> page)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick());
		Add(button, page);
	}

	private const string HelpText =
		"WHAT IT DOES\n" +
		"Quiet Skies filters the signal markers the game puts on your HUD: Unidentified Signal (global encounters), Unknown Signal (personal cargo drops) and Strong Unknown Signal (cargo drops anyone can claim). It never touches any other marker: not your own GPS, not Body Location, not contracts or mod markers.\n\n" +
		"THE MODES\n" +
		"Show: the game's normal behaviour.\n" +
		"Hide: takes the markers off your HUD with the game's own Show on HUD switch, the same as unticking it in the G menu. They stay in your G list, and the server remembers the switch, so they stay off the HUD even without the plugin.\n" +
		"Nearby only: the markers show on your HUD only within the distance you set, measured from you or the ship you control. This one only changes your own screen and sends nothing; in the G list they still show as on the HUD. A marker comes back inside the distance and goes again a little beyond it, so it doesn't flicker at the edge.\n" +
		"New markers follow the setting as they arrive (the game still plays its arrival sound). Settings are kept for each world.\n\n" +
		"YOUR OWN CHOICES\n" +
		"Show and Nearby only only turn a marker back on if Quiet Skies hid it. A marker you took off the HUD yourself in the G menu stays off. When a setting changes, or when you load the world, Quiet Skies applies it to every marker again.\n\n" +
		"TOOLBAR\n" +
		"In a cockpit or remote control, the toolbar has Quiet Skies: signal filter (opens this window) and one action per kind that steps through Show, Hide and Nearby only.\n\n" +
		"COMMANDS\n" +
		"/skies opens this window. /skies status lists each kind's mode and markers. /skies unidentified hide, /skies unknown show, /skies strong nearby 20 or /skies all hide set a mode (nearby takes an optional distance in km). /skies chat off, quiet, normal or chatty sets how much it comments. /skies name and a name changes what its chat lines show under; on its own it puts it back. /tim opens whichever of our plugin windows you used last, and the list at the top left switches to another of our plugins.\n\n" +
		"WHAT THE SERVER SEES\n" +
		"Hide sends one Show on HUD request per marker. Show sends two for each marker Quiet Skies hid: Show on HUD, and Always visible (the game turns that off when a marker is hidden). They're the same requests the G menu sends, at most five a second. Nearby only, the window, the commands and the toolbar actions send nothing. A toolbar action saved in a cockpit's toolbar is stored with the ship, like any other toolbar slot.\n\n" +
		"FILES\n" +
		"The chat setting is in QuietSkies_Settings.txt and each world's modes in QuietSkies_<world>_Settings.txt, in %AppData%\\SpaceEngineers\\Storage.";
}
