using System;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game.Components;
using VRage.Utils;

namespace Tripwire;

/// <summary>
/// Runs the watcher and the script-log reader, and wires up the /tripwire chat command and the plugin switcher.
/// Client only. It reads what the game already has on this PC; the only thing it ever sends is the datapad edit
/// when you press Copy to Datapad.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class TripwireSession : MySessionComponentBase
{
	public const string ChatSender = "Tripwire";

	private const string ChatCommand = "/tripwire";

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

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
			AlertLog.Load();
			ScriptLogs.Load();
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
			MyLog.Default.WriteLineAndConsole($"[Tripwire] BeforeStart failed: {ex}");
		}
	}

	protected override void UnloadData()
	{
		if (_started)
		{
			try
			{
				MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
				PluginSwitcher.Unregister();
				Personality.Unregister();
				Personality.Changed -= Chatter.Save;
				AlertLog.Save();
				Settings.Save();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Tripwire] Unload: {ex}");
			}
		}
		Watcher.Unload();
		ScriptLogs.Unload();
		Alerts.Reset();
		_started = false;
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		double now = Now;
		try
		{
			Watcher.Update(now);
			Alerts.Update(now);
			ScriptLogs.Update(now);
			AlertLog.Update(now);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tripwire] Update: {ex}");
		}
	}

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
		case "mute":
			int minutes = words.Length > 2 && int.TryParse(words[2], out int m) && m > 0 ? Math.Min(m, 600) : 10;
			Alerts.MutedUntil = Now + minutes * 60.0;
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat alerts muted for {minutes} min. They still go into the log. /tripwire unmute turns them back on.");
			return;
		case "unmute":
			Alerts.MutedUntil = 0;
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Chat alerts are on again.");
			return;
		case "chat":
			SetChatLevel(words.Length > 2 ? words[2] : null);
			return;
		case "name":
			// Everything after "/tripwire name", spaces kept; nothing resets it to the character's own name.
			int start = messageText.IndexOf(words[1], StringComparison.OrdinalIgnoreCase) + words[1].Length;
			Chatter.SetDisplayName(messageText.Substring(start));
			return;
		default:
			OpenWindow();
			return;
		}
	}

	private static void SetChatLevel(string word)
	{
		if (word == null || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality is {Settings.Chattiness}. Change it with /tripwire chat off, quiet, normal or chatty.");
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
			ScriptLogs.Scan();
			MyGuiSandbox.AddScreen(new TripwireScreen());
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Tripwire] {ex}");
		}
	}
}
