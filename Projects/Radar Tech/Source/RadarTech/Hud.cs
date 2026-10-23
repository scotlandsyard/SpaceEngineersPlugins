using System;
using System.Linq;
using Sandbox.Game.Gui;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace RadarTech;

/// <summary>
/// The readout: one line just under the crosshair, and the hotkey that turns it on and off (Ctrl+Alt+R by default).
/// The screen and hotkey code follow Fat Albert's Hud.cs.
/// </summary>
internal static class Hud
{
	private static HudScreen s_screen;

	private static string s_keyText;

	private static MyKeys s_key;

	private static bool s_ctrl;

	private static bool s_alt;

	private static bool s_shift;

	public static void Show(string text, Color color)
	{
		if (s_screen == null || s_screen.State == MyGuiScreenState.CLOSED)
		{
			s_screen = new HudScreen();
			MyGuiSandbox.AddScreen(s_screen);
		}
		s_screen.SetText(text, color);
	}

	/// <summary>Called every tick: hides the line with the game's own HUD (Tab).</summary>
	public static void Update()
	{
		if (s_screen != null)
		{
			s_screen.Hidden = MyAPIGateway.Session?.Config?.HudState == 0;
			s_screen.FollowCrosshair();
		}
	}

	public static void Close()
	{
		if (s_screen != null)
		{
			s_screen.CloseScreen();
			s_screen = null;
		}
	}

	/// <summary>True on the tick the hotkey from the settings is pressed; ignored while chat or a menu with a cursor is open.</summary>
	public static bool HotkeyPressed()
	{
		VRage.ModAPI.IMyInput input = MyAPIGateway.Input;
		if (input == null || MyAPIGateway.Gui == null || MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible)
		{
			return false;
		}
		if (s_keyText != Settings.Hotkey)
		{
			ParseKey(Settings.Hotkey);
		}
		if (s_key == MyKeys.None || !input.IsNewKeyPressed(s_key))
		{
			return false;
		}
		return s_ctrl == input.IsAnyCtrlKeyPressed() && s_alt == input.IsAnyAltKeyPressed() && s_shift == input.IsAnyShiftKeyPressed();
	}

	private static void ParseKey(string text)
	{
		s_keyText = text;
		s_key = MyKeys.None;
		s_ctrl = s_alt = s_shift = false;
		foreach (string part in (text ?? "").Split('+').Select(p => p.Trim()).Where(p => p.Length > 0))
		{
			switch (part.ToLowerInvariant())
			{
			case "ctrl":
			case "control":
				s_ctrl = true;
				break;
			case "alt":
				s_alt = true;
				break;
			case "shift":
				s_shift = true;
				break;
			default:
				if (Enum.TryParse(part, ignoreCase: true, out MyKeys key))
				{
					s_key = key;
				}
				else
				{
					MyLog.Default.WriteLineAndConsole($"[Radar Tech] Hotkey '{text}': unknown key '{part}'");
				}
				break;
			}
		}
	}

	/// <summary>
	/// A screen that only draws: set up like the game's own HUD screen (MyGuiScreenHudBase), so it never takes focus
	/// or input and stays under menus.
	/// </summary>
	private class HudScreen : MyGuiScreenBase
	{
		private const float LineBelow = 0.045f;

		private MyGuiControlLabel _line;

		private bool _hidden;

		public HudScreen()
			: base(new Vector2(0.5f, 0.5f))
		{
			CanBeHidden = true;
			CanHideOthers = false;
			CanHaveFocus = false;
			m_drawEvenWithoutFocus = true;
			m_closeOnEsc = false;
			RecreateControls(constructor: true);
		}

		public override string GetFriendlyName()
		{
			return "RadarTechHud";
		}

		public override void RecreateControls(bool constructor)
		{
			base.RecreateControls(constructor);
			// Just under the crosshair, centred.
			_line = new MyGuiControlLabel(new Vector2(0f, LineBelow), null, "", null, 0.75f, "White", MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP);
			Controls.Add(_line);
		}

		/// <summary>
		/// Keeps the line under the game's crosshair, which in a cockpit in third person or free look marks the ship's aim
		/// rather than the middle of the screen. Same conversion as the game's MyHudWeaponHitIndicator.
		/// </summary>
		public void FollowCrosshair()
		{
			Vector2? hud = MyHud.Crosshair?.Position;
			if (!hud.HasValue)
			{
				return;
			}
			float x = (hud.Value.X - 0.5f) * MyGuiManager.GetSafeFullscreenRectangle().Width / MyGuiManager.GetSafeGuiRectangle().Width;
			float y = hud.Value.Y / MyGuiManager.GetHudSize().Y - 0.5f;
			_line.Position = new Vector2(x, y + LineBelow);
		}

		public bool Hidden
		{
			set
			{
				if (_hidden != value)
				{
					_hidden = value;
					_line.Visible = !value;
				}
			}
		}

		public void SetText(string text, Color color)
		{
			_line.Text = text;
			_line.ColorMask = color.ToVector4();
			_line.Visible = !_hidden;
		}
	}
}
