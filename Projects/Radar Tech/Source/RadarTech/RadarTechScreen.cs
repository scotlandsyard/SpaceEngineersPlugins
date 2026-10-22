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

namespace RadarTech;

/// <summary>The window: readout on/off, range, closing speed, hotkey, the last reading and a Ping button, and a Help page.</summary>
public class RadarTechScreen : MyGuiScreenBase
{
	private const float Left = -0.32f;

	private const float Field = 0.02f;

	private const float ButtonsY = 0.235f;

	private readonly List<MyGuiControlBase> _mainControls = new List<MyGuiControlBase>();

	private readonly List<MyGuiControlBase> _helpControls = new List<MyGuiControlBase>();

	private MyGuiControlLabel _last;

	private MyGuiControlCheckbox _readout;

	private int _frames;

	public RadarTechScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.72f, 0.6f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "RadarTechScreen";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		MyGuiControlLabel caption = AddCaption("Radar Tech");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);
		_mainControls.Clear();
		_helpControls.Clear();

		AddLabel(Left, -0.17f, "Readout under the crosshair", _mainControls);
		_readout = new MyGuiControlCheckbox(new Vector2(Field + 0.012f, -0.17f), null, "Measures several times a second and shows the result just under the crosshair.", Settings.Readout);
		_readout.IsCheckedChanged = box => RadarTechSession.SetReadout(box.IsChecked);
		Add(_readout, _mainControls);

		AddLabel(Left, -0.11f, $"Range in km (up to {RadarTechSession.FormatKm(Settings.VisualKm)}, the view distance)", _mainControls);
		MyGuiControlTextbox range = new MyGuiControlTextbox(new Vector2(Field, -0.11f), RadarTechSession.FormatKm(Settings.RangeKm), 6)
		{
			Size = new Vector2(0.08f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		range.SetToolTip($"How far it measures, {Settings.MinKm:0} to {Settings.MaxKm:0} km. It never reaches past this world's view distance ({RadarTechSession.FormatKm(Settings.VisualKm)} km): what you can't see, it can't measure.");
		range.TextChanged += box =>
		{
			string text = (box.Text ?? "").Trim().Replace(',', '.');
			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0)
			{
				RadarTechSession.SetRange(value);
			}
		};
		Add(range, _mainControls);

		AddLabel(Left, -0.05f, "Closing speed and time to contact", _mainControls);
		MyGuiControlCheckbox closing = new MyGuiControlCheckbox(new Vector2(Field + 0.012f, -0.05f), null, "After the distance: how fast you're closing on it and how long until you reach it at that speed.", Settings.ShowClosing);
		closing.IsCheckedChanged = box =>
		{
			Settings.ShowClosing = box.IsChecked;
			Settings.Save();
		};
		Add(closing, _mainControls);

		AddLabel(Left, 0.01f, "Hotkey (readout on/off)", _mainControls);
		MyGuiControlTextbox key = new MyGuiControlTextbox(new Vector2(Field, 0.01f), Settings.Hotkey, 24)
		{
			Size = new Vector2(0.16f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		key.SetToolTip("Such as Ctrl+Alt+R, Shift+F7 or just F8. Key names are the game's own (F1 to F12, A to Z, NumPad0 ...). Empty for no hotkey.");
		key.TextChanged += box =>
		{
			Settings.Hotkey = (box.Text ?? "").Trim();
			Settings.Save();
		};
		Add(key, _mainControls);

		AddLabel(Left, 0.08f, "Last reading:", _mainControls);
		_last = AddLabel(Left, 0.115f, "", _mainControls);
		AddLabel(Left, 0.175f, "It measures only what your game has loaded, and stops at the first thing in the way.", _mainControls).ColorMask = new Vector4(0.7f, 0.7f, 0.7f, 1f);

		AddButton(-0.2f, ButtonsY, "Ping", () =>
		{
			RadarTechSession.Ping();
			CloseScreen();
		}, _mainControls);
		AddButton(0.24f, ButtonsY, "Help", () => ShowPage(_helpControls), _mainControls);

		MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, -0.2f), new Vector2(0.66f, 0.4f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		help.AppendText(HelpText);
		Add(help, _helpControls);
		AddButton(0.24f, ButtonsY, "Back", () => ShowPage(_mainControls), _helpControls);

		ShowPage(_mainControls);
		RefreshLast();
	}

	public override bool Update(bool hasFocus)
	{
		if (++_frames % 30 == 0)
		{
			RefreshLast();
			if (_readout.IsChecked != Settings.Readout)
			{
				_readout.IsChecked = Settings.Readout;
			}
		}
		return base.Update(hasFocus);
	}

	private void RefreshLast()
	{
		Reading last = RadarTechSession.Last;
		_last.Text = last == null ? "None yet. Turn the readout on, or Ping." : Rangefinder.Describe(last, Settings.ShowClosing);
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
		"Radar Tech is a range finder. It measures the distance from you to whatever is under your crosshair: an asteroid, a planet's surface, a ship, a station or a character. It works on foot and in a cockpit, in first and third person; the distance is always from you (your character or the cockpit), not from the camera.\n\n" +
		"THE READOUT\n" +
		"Turned on, it measures six times a second and shows one line just under the crosshair, such as: Asteroid 4.21 km | closing 85 m/s | 0:49 to contact. Closing speed is how fast the gap is shrinking; opening means it's growing. The line turns amber when you'd reach it in under 10 seconds at your current speed. It hides with the rest of the HUD.\n\n" +
		"PING\n" +
		"One measurement, shown under the crosshair for five seconds and said in chat. Handy when you don't want the readout on all the time.\n\n" +
		"WHAT IT CAN SEE\n" +
		"The same as a camera block's raycast: only what your game has loaded, and only the first thing in the line. It never sees through rock or a ship, and it never finds anything your game doesn't already know about. Your own ship, anything docked to it, and your character are skipped. Range is 1 to 50 km (20 by default), but never past the world's view distance (15 km unless the world or server changes it), the distance the game draws to: what you can't see, it can't measure. If your range is set higher, it's held to the view distance and /radar status says so.\n\n" +
		"TOOLBAR\n" +
		"In a cockpit or remote control, the toolbar has Radar Tech: readout on/off, Radar Tech: ping and Radar Tech: window.\n\n" +
		"HOTKEY\n" +
		"Ctrl+Alt+R turns the readout on and off, on foot too. Change it in the window. It does nothing while chat or a menu is open.\n\n" +
		"COMMANDS\n" +
		"/radar opens this window. /radar on and /radar off turn the readout on and off. /radar ping takes one reading. /radar range 30 sets the range in km. /radar status shows the settings and the last reading. /radar chat off, quiet, normal or chatty sets how much Radar comments. /radar name and a name changes what Radar's chat lines show under; on its own it puts it back. /tim opens whichever of our plugin windows you used last, and the list at the top left switches to another of our plugins.\n\n" +
		"WHAT THE SERVER SEES\n" +
		"Nothing. Radar Tech only reads what's already on your PC. A toolbar action saved in a cockpit's toolbar is stored with the ship, like any other toolbar slot.\n\n" +
		"FILES\n" +
		"The settings are in RadarTech_Settings.txt in %AppData%\\SpaceEngineers\\Storage.";
}
