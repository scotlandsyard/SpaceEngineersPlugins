using System;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game.Components;
using VRage.Utils;

namespace Hush;

/// <summary>
/// Loads the world's mutes while the world loads (before blocks start their sounds), and wires up /hush and the plugin
/// switcher. Client only: muting changes what this PC plays and sends nothing to the server.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class HushSession : MySessionComponentBase
{
	public const string ChatSender = "Hush";

	private static readonly string[] ChatCommands = { "/hush" };

	private bool _started;

	public override void LoadData()
	{
		try
		{
			if (MyAPIGateway.Utilities?.IsDedicated == true)
			{
				return;
			}
			Mutes.LoadWorld();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] LoadData failed: {ex}");
		}
	}

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			PluginSwitcher.Register(Plugin.Name, OpenMenu);
			Personality.Level = Mutes.Chattiness;
			Personality.DisplayName = Mutes.DisplayName;
			Personality.Changed += Chatter.Save;
			Personality.Register(ChatSender);
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] BeforeStart failed: {ex}");
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
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Hush] Unload: {ex}");
			}
		}
		Mutes.UnloadWorld();
		_started = false;
	}

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		string[] words = (messageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0 || Array.FindIndex(ChatCommands, c => c.Equals(words[0], StringComparison.OrdinalIgnoreCase)) < 0)
		{
			return;
		}
		sendToOthers = false;
		string word = words.Length > 1 ? words[1].ToLowerInvariant() : "";
		switch (word)
		{
		case "":
			OpenMenu();
			break;
		case "chat":
			SetChatLevel(words.Length > 2 ? words[2] : null);
			break;
		case "name":
		{
			// Everything after "/hush name", spaces kept; nothing puts Hush's own name back.
			int start = messageText.IndexOf(words[1], StringComparison.OrdinalIgnoreCase) + words[1].Length;
			Chatter.SetDisplayName(messageText.Substring(start).Trim());
			break;
		}
		default:
			MyAPIGateway.Utilities.ShowMessage(Plugin.Name, "Commands: /hush (window), /hush chat off|quiet|normal|chatty, /hush name <name>.");
			break;
		}
	}

	/// <summary>/hush chat [off|quiet|normal|chatty]: sets how much Hush talks, or says what it's set to.</summary>
	private static void SetChatLevel(string word)
	{
		if (string.IsNullOrEmpty(word) || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"Chat personality is {Personality.Level}. Change it with /hush chat off, quiet, normal or chatty.");
			return;
		}
		Chatter.SetLevel(level);
		MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"Chat personality: {level}. {Chatter.LevelHint(level)}");
	}

	private static void OpenMenu()
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			MyGuiSandbox.AddScreen(new HushScreen());
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(Plugin.Name, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Hush] {ex}");
		}
	}
}
