using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TimShared;
using VRage.Game.Components;
using VRage.Utils;

namespace DrillSergeant;

/// <summary>
/// Looks after every rig twice a second, and wires up the /sarge chat command, the plugin switcher and the optional
/// toolbar actions. Client only: everything it changes goes to the server as the same requests the terminal sends.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class SargeSession : MySessionComponentBase
{
	public const string ChatSender = "Drill Sergeant";

	private const string ChatCommand = "/sarge";

	private const string StartPauseActionId = "DrillSergeant_StartPause";

	private const string PauseActionId = "DrillSergeant_Pause";

	/// <summary>Rigs are looked at every 30 ticks (0.5 s).</summary>
	private const int RigTicks = 30;

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

	private readonly List<IMyTerminalAction> _actions = new List<IMyTerminalAction>();

	private bool _started;

	private int _tick;

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Settings.Load();
			RigStore.Load();
			CreateActions();
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
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] BeforeStart failed: {ex}");
		}
	}

	protected override void UnloadData()
	{
		if (!_started)
		{
			return;
		}
		try
		{
			ScriptLink.Subscribe(null);
			MyAPIGateway.TerminalControls.CustomActionGetter -= CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
			PluginSwitcher.Unregister();
			Personality.Unregister();
			Personality.Changed -= Chatter.Save;
			RigStore.Save();
			Settings.Save();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] Unload: {ex}");
		}
		_started = false;
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		_tick++;
		if (_tick % RigTicks != 0)
		{
			return;
		}
		double now = Now;
		foreach (Rig rig in RigStore.Rigs)
		{
			try
			{
				if (rig.Mode == RigMode.Script)
				{
					ScriptLink.Update(rig, now);
				}
				else
				{
					Driver.Update(rig, now);
					LcdOutput.Update(rig, now);
				}
			}
			catch (Exception ex)
			{
				rig.Live.State = "Warning";
				rig.Live.Detail = "Something went wrong: " + ex.Message;
				MyLog.Default.WriteLineAndConsole($"[DrillSergeant] {rig.Name}: {ex}");
			}
		}
		RigStore.Update(now);
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
		case "chat":
			SetChatLevel(words.Length > 2 ? words[2] : null);
			return;
		case "name":
			// Everything after "/sarge name", spaces kept; nothing resets it to the character's own name.
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
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality is {Settings.Chattiness}. Change it with /sarge chat off, quiet, normal or chatty.");
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
			MyGuiSandbox.AddScreen(new SargeScreen());
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] {ex}");
		}
	}

	// ---------------------------------------------------------------- toolbar actions (optional)

	private void CreateActions()
	{
		AddAction(StartPauseActionId, "Drill Sergeant Start/Pause", "Toggle", rig =>
		{
			bool running = rig.Mode == RigMode.Script ? rig.Live.ScriptRunning : rig.Command == RigCommand.Running;
			return running ? Commands.Pause(rig) : Commands.Start(rig);
		});
		AddAction(PauseActionId, "Drill Sergeant Pause", "SwitchOff", Commands.Pause);
	}

	private void AddAction(string id, string name, string icon, Func<Rig, string> run)
	{
		IMyTerminalAction action = MyAPIGateway.TerminalControls.CreateAction<IMyMotorStator>(id);
		action.Name = new StringBuilder(name);
		action.Icon = $"Textures\\GUI\\Icons\\Actions\\{icon}.dds";
		action.ValidForGroups = false;
		action.Enabled = b => Settings.ToolbarActions && RigStore.Find(b.EntityId) != null;
		action.Action = b =>
		{
			Rig rig = RigStore.Find(b.EntityId);
			if (rig != null && MyAPIGateway.Session?.Player != null)
			{
				MyAPIGateway.Utilities.ShowNotification(run(rig), 3000);
			}
		};
		action.Writer = (b, text) => text.Append(RigStore.Find(b.EntityId)?.Live.State ?? "-");
		_actions.Add(action);
	}

	/// <summary>Offers the actions only on rotors that are rigs, and only with the setting on, so nothing shows anywhere else.</summary>
	private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
	{
		try
		{
			if (Settings.ToolbarActions && block is IMyMotorStator && RigStore.Find(block.EntityId) != null)
			{
				actions.AddRange(_actions.Where(a => !actions.Contains(a)));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[DrillSergeant] CustomActionGetter: {ex}");
		}
	}
}

/// <summary>Start and Pause for either mode, shared by the window and the toolbar actions.</summary>
internal static class Commands
{
	public static string Start(Rig rig)
	{
		return rig.Mode == RigMode.Script ? ScriptLink.Send(rig, "start") : Driver.Start(rig);
	}

	public static string Pause(Rig rig)
	{
		return rig.Mode == RigMode.Script ? ScriptLink.Send(rig, "pause") : Driver.Pause(rig);
	}
}
