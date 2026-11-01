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
		OpenMenu();
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
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(Plugin.Name, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Hush] {ex}");
		}
	}
}
