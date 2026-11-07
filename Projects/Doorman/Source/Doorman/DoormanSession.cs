using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TimShared;
using VRage.Game.Components;
using VRage.Utils;

namespace Doorman;

/// <summary>
/// Runs the door closer and wires up the /doorman chat command, the toolbar actions on cockpits and remote controls,
/// and the plugin switcher. Client only. The only thing it sends is the terminal's own Open switch, set to closed
/// (Doors.cs).
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class DoormanSession : MySessionComponentBase
{
	public const string ChatSender = "Doorman";

	private const string ChatCommand = "/doorman";

	private const string ActionPrefix = "Doorman_";

	private readonly List<IMyTerminalAction> _actions = new List<IMyTerminalAction>();

	private bool _started;

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Settings.Load();
			_actions.Add(new MyTerminalAction<MyTerminalBlock>(ActionPrefix + "OpenWindow", new StringBuilder("Doorman: door settings"), _ => OpenWindow(), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false
			});
			_actions.Add(new MyTerminalAction<MyTerminalBlock>(ActionPrefix + "Toggle", new StringBuilder("Doorman: auto-close On/Off"), _ =>
			{
				SetEnabled(!Settings.Enabled);
				MyAPIGateway.Utilities.ShowNotification("Doorman: " + (Settings.Enabled ? "closing doors" : "off"), 2500);
			}, "Textures\\GUI\\Icons\\Actions\\Toggle.dds")
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
			MyLog.Default.WriteLineAndConsole($"[Doorman] BeforeStart failed: {ex}");
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
				MyLog.Default.WriteLineAndConsole($"[Doorman] Unload: {ex}");
			}
		}
		Doors.Reset();
		_actions.Clear();
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
			Doors.Update(MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Doorman] Update: {ex}");
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
			MyLog.Default.WriteLineAndConsole($"[Doorman] CustomActionGetter: {ex}");
		}
	}

	internal static void SetEnabled(bool on)
	{
		if (Settings.Enabled == on)
		{
			return;
		}
		Settings.Enabled = on;
		Settings.SaveWorld();
		if (on)
		{
			Personality.Say("doorman_on");
		}
		else
		{
			Personality.Say("doorman_off");
		}
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
			SetEnabled(true);
			ShowStatus();
			return;
		case "off":
			SetEnabled(false);
			ShowStatus();
			return;
		case "status":
			ShowStatus();
			return;
		case "delay":
			if (words.Length < 3 || !Settings.TryDelay(words[2], out double delay))
			{
				MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Use /doorman delay <seconds>, {Settings.MinDelay:0} to {Settings.MaxDelay:0}. Sets the default for grids without their own.");
				return;
			}
			Settings.Delay = delay;
			Settings.SaveWorld();
			ShowStatus();
			return;
		case "chat":
			SetChatLevel(words.Length > 2 ? words[2] : null);
			return;
		case "name":
			// Everything after "/doorman name", spaces kept; nothing resets it to the character's own name.
			int start = messageText.IndexOf(words[1], StringComparison.OrdinalIgnoreCase) + words[1].Length;
			Chatter.SetDisplayName(messageText.Substring(start));
			return;
		}
		MyAPIGateway.Utilities.ShowMessage(ChatSender, "Use /doorman on, off, status, delay <seconds>, chat <level>, name <name>, or /doorman on its own for the window.");
	}

	private static void ShowStatus()
	{
		int grids = 0;
		foreach (Doors.GridDoors _ in Doors.Grids)
		{
			grids++;
		}
		MyAPIGateway.Utilities.ShowMessage(ChatSender, $"{(Settings.Enabled ? "Closing doors" : "Off")}. Default delay {Settings.FormatDelay(Settings.Delay)} s. Watching {Doors.DoorCount} door(s) on {grids} grid(s); {Doors.ClosesSent} closed this session.");
	}

	private static void SetChatLevel(string word)
	{
		if (word == null || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality is {Settings.Chattiness}. Change it with /doorman chat off, quiet, normal or chatty.");
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
			MyGuiSandbox.AddScreen(new DoormanScreen());
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Doorman] {ex}");
		}
	}
}
