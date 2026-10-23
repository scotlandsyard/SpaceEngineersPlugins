using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TimShared;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace RadarTech;

/// <summary>
/// Runs the range finder and wires up the /radar chat command, the hotkey, the toolbar actions on cockpits and remote
/// controls, and the plugin switcher. Client only, and it sends nothing to the server.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class RadarTechSession : MySessionComponentBase
{
	public const string ChatSender = "Radar";

	private const string ChatCommand = "/radar";

	private const string ActionPrefix = "RadarTech_";

	/// <summary>The readout measures every this many ticks (six times a second).</summary>
	private const int ReadoutTicks = 10;

	/// <summary>How long a ping's answer stays under the crosshair.</summary>
	private const int PingTicks = 300;

	private static readonly Color HitColor = Color.White;

	private static readonly Color CautionColor = new Color(255, 220, 60);

	private static readonly Color DangerColor = new Color(255, 70, 50);

	private static readonly Color MutedColor = new Color(150, 160, 170);

	private readonly List<IMyTerminalAction> _actions = new List<IMyTerminalAction>();

	private static bool s_pingPending;

	private static int s_pingTicksLeft;

	private int _tick;

	private bool _started;

	/// <summary>The last measurement, for the window.</summary>
	internal static Reading Last { get; private set; }

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Settings.Load();
			_actions.Add(new MyTerminalAction<MyTerminalBlock>(ActionPrefix + "Readout", new StringBuilder("Radar Tech: readout on/off"), _ => SetReadout(!Settings.Readout), "Textures\\GUI\\Icons\\Actions\\Toggle.dds")
			{
				ValidForGroups = false
			});
			_actions.Add(new MyTerminalAction<MyTerminalBlock>(ActionPrefix + "Ping", new StringBuilder("Radar Tech: ping"), _ => Ping(), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false
			});
			_actions.Add(new MyTerminalAction<MyTerminalBlock>(ActionPrefix + "OpenWindow", new StringBuilder("Radar Tech: window"), _ => OpenWindow(), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false
			});
			MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			PluginSwitcher.Register(Plugin.Name, OpenWindow);
			Personality.Level = Settings.Chattiness;
			Personality.DisplayName = Settings.DisplayName;
			Personality.Changed += Chatter.Save;
			Personality.Register(ChatSender);
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Radar Tech] BeforeStart failed: {ex}");
		}
	}

	protected override void UnloadData()
	{
		if (_started)
		{
			try
			{
				MyAPIGateway.TerminalControls.CustomActionGetter -= CustomActionGetter;
				MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
				PluginSwitcher.Unregister();
				Personality.Unregister();
				Personality.Changed -= Chatter.Save;
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Radar Tech] Unload: {ex}");
			}
		}
		Hud.Close();
		_actions.Clear();
		Last = null;
		s_pingPending = false;
		s_pingTicksLeft = 0;
		_started = false;
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			if (Hud.HotkeyPressed())
			{
				SetReadout(!Settings.Readout);
			}
			if (s_pingTicksLeft > 0 && --s_pingTicksLeft == 0 && !Settings.Readout)
			{
				Hud.Close();
			}
			bool due = s_pingPending || Settings.Readout && ++_tick >= ReadoutTicks;
			if (due && !Rangefinder.Busy)
			{
				_tick = 0;
				bool ping = s_pingPending;
				s_pingPending = false;
				Rangefinder.Start(Settings.EffectiveKm * 1000, reading => Show(reading, ping));
			}
			Hud.Update();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Radar Tech] Update: {ex}");
		}
	}

	private static void Show(Reading reading, bool ping)
	{
		Last = reading;
		if (!InWorld)
		{
			return;
		}
		if (ping)
		{
			s_pingTicksLeft = PingTicks;
			if (reading.Hit)
			{
				Personality.Say("ping_hit", "target", Rangefinder.KindName(reading.Kind), "distance", Rangefinder.FormatDistance(reading.Distance));
			}
			else
			{
				Personality.Say("ping_none", "range", Rangefinder.FormatDistance(reading.RangeMetres));
			}
		}
		if (!Settings.Readout && s_pingTicksLeft == 0)
		{
			return;
		}
		string text = "Radar: " + Rangefinder.Describe(reading, Settings.ShowClosing);
		Color color = reading.Hit ? HitColor : MutedColor;
		double contact = reading.Hit && reading.Closing > 0.5 ? reading.Distance / reading.Closing : double.MaxValue;
		if (contact < Settings.DangerSeconds)
		{
			color = DangerColor;
			text += "  |  COLLISION IMMINENT";
		}
		else if (contact < Settings.CautionSeconds)
		{
			color = CautionColor;
			text += "  |  slow down";
		}
		Hud.Show(text, color);
	}

	/// <summary>False after the world unloaded while a measurement was still running.</summary>
	private static bool InWorld => MyAPIGateway.Session?.Player != null;

	private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
	{
		try
		{
			if (block is IMyShipController && block.HasLocalPlayerAccess())
			{
				foreach (IMyTerminalAction action in _actions)
				{
					if (!actions.Contains(action))
					{
						actions.Add(action);
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Radar Tech] CustomActionGetter: {ex}");
		}
	}

	// ---------------------------------------------------------------- readout and ping

	internal static void SetReadout(bool on)
	{
		bool changed = Settings.Readout != on;
		Settings.Readout = on;
		Settings.Save();
		if (!on && s_pingTicksLeft == 0)
		{
			Hud.Close();
		}
		MyAPIGateway.Utilities.ShowNotification($"Radar Tech readout {(on ? "on" : "off")} ({Settings.Hotkey})", 2000);
		if (changed && on)
		{
			Personality.Say("readout_on");
		}
		else if (changed)
		{
			Personality.Say("readout_off");
		}
	}

	/// <summary>One measurement, shown under the crosshair for five seconds and said in chat.</summary>
	internal static void Ping()
	{
		s_pingPending = true;
	}

	/// <summary>Seconds to contact for the yellow (slow down) and red (collision imminent) readout; 0 turns one off.</summary>
	internal static void SetWarnings(double caution, double danger)
	{
		Settings.CautionSeconds = Settings.ClampSeconds(caution);
		Settings.DangerSeconds = Settings.ClampSeconds(danger);
		Settings.Save();
	}

	internal static bool TryParseSeconds(string text, out double seconds)
	{
		return double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0;
	}

	internal static void SetRange(double km)
	{
		Settings.RangeKm = Settings.ClampKm(km);
		Settings.Save();
	}

	// ---------------------------------------------------------------- chat

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		string[] words = (messageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0 || !words[0].Equals(ChatCommand, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		sendToOthers = false;
		string sub = words.Length > 1 ? words[1].ToLowerInvariant() : "";
		switch (sub)
		{
		case "":
			OpenWindow();
			return;
		case "on":
			SetReadout(true);
			return;
		case "off":
			SetReadout(false);
			return;
		case "ping":
			Ping();
			return;
		case "status":
			ShowStatus();
			return;
		case "range":
			if (words.Length > 2 && double.TryParse(words[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double km) && km > 0)
			{
				SetRange(km);
				ShowStatus();
			}
			else
			{
				MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Use /radar range <km>, {Settings.MinKm:0} to {Settings.MaxKm:0}.");
			}
			return;
		case "warn":
			if (words.Length > 3 && TryParseSeconds(words[2], out double caution) && TryParseSeconds(words[3], out double danger))
			{
				SetWarnings(caution, danger);
				ShowStatus();
			}
			else
			{
				MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Use /radar warn <slow down> <imminent>, seconds to contact (0 turns one off). Now {Settings.CautionSeconds:0} and {Settings.DangerSeconds:0}.");
			}
			return;
		case "chat":
			SetChatLevel(words.Length > 2 ? words[2] : null);
			return;
		case "name":
			// Everything after "/radar name", spaces kept; nothing resets it to the character's own name.
			int start = messageText.IndexOf(words[1], StringComparison.OrdinalIgnoreCase) + words[1].Length;
			Chatter.SetDisplayName(messageText.Substring(start));
			return;
		}
		MyAPIGateway.Utilities.ShowMessage(ChatSender, "Use /radar on, off, ping, status, range <km>, warn <s> <s>, or /radar on its own for the window.");
	}

	private static void ShowStatus()
	{
		string last = Last == null ? "no reading yet" : Rangefinder.Describe(Last, Settings.ShowClosing);
		MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Readout {(Settings.Readout ? "on" : "off")} ({Settings.Hotkey}), range {FormatKm(Settings.RangeKm)} km{(Settings.RangeKm > Settings.VisualKm ? $", held to this world's view distance of {FormatKm(Settings.VisualKm)} km" : "")}, warnings at {Settings.CautionSeconds:0} s and {Settings.DangerSeconds:0} s to contact. Last: {last}.");
	}

	internal static string FormatKm(double km)
	{
		return km.ToString("0.#", CultureInfo.InvariantCulture);
	}

	private static void SetChatLevel(string word)
	{
		if (word == null || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality is {Settings.Chattiness}. Change it with /radar chat off, quiet, normal or chatty.");
			return;
		}
		Chatter.SetLevel(level);
		MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality: {level}. {Chatter.LevelHint(level)}");
	}

	internal static void OpenWindow()
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			MyGuiSandbox.AddScreen(new RadarTechScreen());
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Radar Tech] {ex}");
		}
	}
}
