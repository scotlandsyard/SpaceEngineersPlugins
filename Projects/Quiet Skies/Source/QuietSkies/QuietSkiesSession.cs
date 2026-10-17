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

namespace QuietSkies;

/// <summary>
/// Runs the filter and wires up the /skies chat command, the toolbar actions on cockpits and remote controls, and the
/// plugin switcher. Client only. The only thing it sends is the game's own Show on HUD and Always visible requests
/// for encounter and cargo drop markers (Filter.cs).
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class QuietSkiesSession : MySessionComponentBase
{
	public const string ChatSender = "Quiet Skies";

	private const string ChatCommand = "/skies";

	private const string ActionPrefix = "QuietSkies_";

	private readonly List<IMyTerminalAction> _actions = new List<IMyTerminalAction>();

	private bool _started;

	private bool _filterStarted;

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Settings.Load();
			_actions.Add(new MyTerminalAction<MyTerminalBlock>(ActionPrefix + "OpenWindow", new StringBuilder("Quiet Skies: signal filter"), _ => OpenWindow(), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false
			});
			foreach (SignalKind kind in Signals.Kinds)
			{
				SignalKind k = kind;
				_actions.Add(new MyTerminalAction<MyTerminalBlock>(ActionPrefix + "Cycle" + kind, new StringBuilder("Quiet Skies: " + Signals.Name(kind) + " Show/Hide/Nearby"), _ => Cycle(k), "Textures\\GUI\\Icons\\Actions\\Toggle.dds")
				{
					ValidForGroups = false
				});
			}
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
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] BeforeStart failed: {ex}");
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
				Settings.SaveWorld();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Quiet Skies] Unload: {ex}");
			}
		}
		Filter.Stop();
		Signals.Reset();
		_actions.Clear();
		_started = false;
		_filterStarted = false;
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			if (!_filterStarted)
			{
				Filter.Start();
				_filterStarted = true;
			}
			Filter.Update();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] Update: {ex}");
		}
	}

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
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] CustomActionGetter: {ex}");
		}
	}

	// ---------------------------------------------------------------- modes

	/// <summary>Sets a kind's mode (and distance for Nearby only), saves it for this world and applies it on the next tick.</summary>
	internal static void SetMode(SignalKind kind, SignalMode mode, double? km = null)
	{
		bool changed = Settings.Mode(kind) != mode;
		Settings.Modes[kind] = mode;
		if (km.HasValue)
		{
			changed |= Math.Abs(Settings.Km(kind) - Settings.ClampKm(km.Value)) > 0.001;
			Settings.NearbyKm[kind] = Settings.ClampKm(km.Value);
		}
		Settings.SaveWorld();
		Filter.ScanSoon();
		if (!changed)
		{
			return;
		}
		string name = Signals.Name(kind);
		switch (mode)
		{
		case SignalMode.Hide:
			Personality.Say("signals_hidden", "kind", name);
			break;
		case SignalMode.Nearby:
			Personality.Say("signals_nearby", "kind", name, "km", FormatKm(Settings.Km(kind)));
			break;
		default:
			Personality.Say("signals_shown", "kind", name);
			break;
		}
	}

	private static void Cycle(SignalKind kind)
	{
		SignalMode next = (SignalMode)(((int)Settings.Mode(kind) + 1) % 3);
		SetMode(kind, next);
		MyAPIGateway.Utilities.ShowNotification($"{Signals.Name(kind)}: {Describe(kind)}", 2500);
	}

	internal static string Describe(SignalKind kind)
	{
		SignalMode mode = Settings.Mode(kind);
		return mode == SignalMode.Nearby ? $"Nearby only ({FormatKm(Settings.Km(kind))} km)" : Signals.ModeName(mode);
	}

	internal static string FormatKm(double km)
	{
		return km.ToString("0.#", CultureInfo.InvariantCulture);
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
		case "status":
			ShowStatus();
			return;
		case "chat":
			SetChatLevel(words.Length > 2 ? words[2] : null);
			return;
		case "name":
			// Everything after "/skies name", spaces kept; nothing resets it to the character's own name.
			int start = messageText.IndexOf(words[1], StringComparison.OrdinalIgnoreCase) + words[1].Length;
			Chatter.SetDisplayName(messageText.Substring(start));
			return;
		}
		SignalKind[] kinds = ParseKinds(sub);
		SignalMode? mode = words.Length > 2 ? ParseMode(words[2]) : null;
		if (kinds == null || !mode.HasValue)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Use /skies <unidentified|unknown|strong|all> <show|hide|nearby> [km], /skies status, or /skies on its own for the window.");
			return;
		}
		double? km = null;
		if (words.Length > 3)
		{
			if (!double.TryParse(words[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed <= 0)
			{
				MyAPIGateway.Utilities.ShowMessage(ChatSender, $"'{words[3]}' isn't a distance in km.");
				return;
			}
			km = parsed;
		}
		foreach (SignalKind kind in kinds)
		{
			SetMode(kind, mode.Value, km);
		}
		ShowStatus();
	}

	private static SignalKind[] ParseKinds(string word)
	{
		switch (word)
		{
		case "all":
			return Signals.Kinds;
		case "unidentified":
		case "encounter":
		case "encounters":
			return new[] { SignalKind.Encounter };
		case "unknown":
			return new[] { SignalKind.Unknown };
		case "strong":
			return new[] { SignalKind.Strong };
		default:
			return null;
		}
	}

	private static SignalMode? ParseMode(string word)
	{
		switch (word.ToLowerInvariant())
		{
		case "show":
		case "on":
			return SignalMode.Show;
		case "hide":
		case "off":
			return SignalMode.Hide;
		case "nearby":
		case "near":
			return SignalMode.Nearby;
		default:
			return null;
		}
	}

	private static void ShowStatus()
	{
		StringBuilder text = new StringBuilder();
		foreach (SignalKind kind in Signals.Kinds)
		{
			Filter.Count(kind, out int total, out int onHud);
			text.Append($"\n{Signals.Name(kind)}: {Describe(kind)}. {total} marker(s), {onHud} on your HUD.");
		}
		MyAPIGateway.Utilities.ShowMessage(ChatSender, text.ToString());
	}

	private static void SetChatLevel(string word)
	{
		if (word == null || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality is {Settings.Chattiness}. Change it with /skies chat off, quiet, normal or chatty.");
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
			MyGuiSandbox.AddScreen(new QuietSkiesScreen());
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] {ex}");
		}
	}
}
