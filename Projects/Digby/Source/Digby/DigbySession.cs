using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game.Components;
using VRage.Utils;

namespace Digby;

/// <summary>
/// Wires up the window, the /digby chat command and the plugin switcher, keeps the last plan, and checks that a
/// datapad write arrived. Client only.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class DigbySession : MySessionComponentBase
{
	public const string ChatSender = "Digby";

	public const string ChatCommand = "/digby";

	/// <summary>The last plan calculated this session (the datapad is written from it).</summary>
	internal static DigPlanner.Plan LastPlan;

	internal static DigPlanner.Result LastResult;

	private static readonly List<KeyValuePair<Datapads.Write, Action<string>>> s_expected = new List<KeyValuePair<Datapads.Write, Action<string>>>();

	private static double s_expectedAt;

	private bool _started;

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Settings.Load();
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			PluginSwitcher.Register(Plugin.Name, () => OpenMenu(DigbyScreen.Page.Plan));
			Personality.Level = Settings.Chattiness;
			Personality.DisplayName = Settings.DisplayName;
			Personality.Changed += Chatter.Save;
			Personality.Register(ChatSender);
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Digby] BeforeStart failed: {ex}");
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
				Settings.Save();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Digby] Unload: {ex}");
			}
		}
		Markers.Unload();
		Library.Unload();
		LastPlan = null;
		LastResult = null;
		s_expected.Clear();
		_started = false;
	}

	public override void UpdateAfterSimulation()
	{
		if (s_expected.Count == 0 || Now < s_expectedAt)
		{
			return;
		}
		try
		{
			CheckPads();
		}
		catch (Exception ex)
		{
			s_expected.Clear();
			MyLog.Default.WriteLineAndConsole($"[Digby] Datapad check: {ex}");
		}
	}

	/// <summary>Checks a few seconds after a write that the pads carry their new names, and says so.</summary>
	internal static void ExpectPads(List<Datapads.Write> writes, Action<string> report)
	{
		s_expected.Clear();
		s_expected.AddRange(writes.Select(w => new KeyValuePair<Datapads.Write, Action<string>>(w, report)));
		s_expectedAt = Now + 3.0;
	}

	private static void CheckPads()
	{
		List<KeyValuePair<Datapads.Write, Action<string>>> expected = s_expected.ToList();
		s_expected.Clear();
		int done = expected.Count(e => Datapads.CurrentName(e.Key.Pad.ItemId) == e.Key.Name);
		Action<string> report = expected[0].Value;
		if (done == expected.Count)
		{
			report(expected.Count == 1 ? $"Datapad written: \"{expected[0].Key.Name}\" is in your inventory." : $"All {done} datapads written.");
			Personality.Say("pad_written");
		}
		else
		{
			report($"{expected.Count - done} of {expected.Count} datapad(s) don't show the new text yet. The server may be slow, or it didn't accept the change; open the datapad to check.");
		}
	}

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		string text = (messageText ?? "").Trim();
		if (!text.Equals(ChatCommand, StringComparison.OrdinalIgnoreCase) && !text.StartsWith(ChatCommand + " ", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		sendToOthers = false;
		try
		{
			string args = text.Substring(ChatCommand.Length).Trim();
			string word = args.Split(' ')[0].ToLowerInvariant();
			string rest = args.Substring(word.Length).Trim();
			switch (word)
			{
			case "":
				OpenMenu(DigbyScreen.Page.Plan);
				break;
			case "help":
				OpenMenu(DigbyScreen.Page.Help);
				break;
			case "library":
			case "lib":
				OpenMenu(DigbyScreen.Page.Library);
				break;
			case "dig":
				DigFromChat(rest);
				break;
			case "rig":
				RigFromChat(rest);
				break;
			case "pad":
			case "datapad":
				PadFromChat();
				break;
			case "clear":
				Say($"{DigPlanner.ClearMarkers()} dig marker(s) removed.");
				break;
			case "planet":
				PlanetFromChat(rest);
				break;
			case "chat":
				SetChatLevel(rest);
				break;
			case "name":
				Chatter.SetDisplayName(rest);
				break;
			default:
				Say("Commands: /digby (window), /digby dig [from] <target> [depth], /digby dig (last plan again), /digby rig <P1> <P2> [up] [small|large], /digby pad (write the plan to a datapad), /digby clear, /digby planet, /digby library, /digby help, /digby chat, /digby name.");
				break;
			}
		}
		catch (Exception ex)
		{
			Say("That didn't work: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Digby] Chat command: {ex}");
		}
	}

	internal static void Say(string line)
	{
		MyAPIGateway.Utilities.ShowMessage(ChatSender, line);
	}

	private static bool SessionReady()
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			Say("Load into a world first.");
			return false;
		}
		return true;
	}

	/// <summary>/digby dig: no arguments reruns the last plan of this world. A new plan keeps the saved rig line (it belongs to the grid).</summary>
	private static void DigFromChat(string args)
	{
		if (!SessionReady())
		{
			return;
		}
		DigPlanner.Plan saved = Settings.LoadPlan();
		DigPlanner.Plan plan;
		if (args.Length == 0)
		{
			plan = saved;
			if (plan.Target.Length == 0)
			{
				Say("No dig plan saved for this world yet. Use /digby dig [from] <target> [depth], or the window (/digby).");
				return;
			}
		}
		else if (!DigPlanner.TryParseArgs(args, out plan, out string error))
		{
			Say(error);
			return;
		}
		else
		{
			plan.RigA = saved.RigA;
			plan.RigB = saved.RigB;
			plan.RigUp = saved.RigUp;
			plan.SmallGrid = saved.SmallGrid;
		}
		foreach (string line in Run(plan))
		{
			Say(line);
		}
	}

	/// <summary>/digby rig &lt;P1&gt; &lt;P2&gt; [&lt;up point&gt;] [small|large], or /digby rig off: sets the rig line of the saved plan.</summary>
	private static void RigFromChat(string args)
	{
		if (!SessionReady())
		{
			return;
		}
		DigPlanner.Plan plan = Settings.LoadPlan();
		List<string> words = DigPlanner.Tokenize(args);
		if (words.RemoveAll(w => w.Equals("small", StringComparison.OrdinalIgnoreCase)) > 0)
		{
			plan.SmallGrid = true;
		}
		if (words.RemoveAll(w => w.Equals("large", StringComparison.OrdinalIgnoreCase)) > 0)
		{
			plan.SmallGrid = false;
		}
		if (words.Count == 1 && words[0].Equals("off", StringComparison.OrdinalIgnoreCase))
		{
			plan.RigA = plan.RigB = plan.RigUp = "";
		}
		else if (words.Count == 2 || words.Count == 3)
		{
			plan.RigA = words[0];
			plan.RigB = words[1];
			plan.RigUp = words.Count == 3 ? words[2] : "";
		}
		else if (words.Count != 0)
		{
			Say("Use /digby rig <P1> <P2> [<point above P1>] [small|large]: P1 -> P2 is your grid's forward. /digby rig off removes it.");
			return;
		}
		if (plan.Target.Length == 0)
		{
			Settings.SavePlan(DigPlanner.Pinned(plan));
			Say("Rig line saved. Now plan the tunnel with /digby dig [from] <target> [depth].");
			return;
		}
		foreach (string line in Run(plan))
		{
			Say(line);
		}
	}

	/// <summary>/digby pad: writes the last plan (this session's, else the saved one recalculated) to datapads, auto-picked.</summary>
	private static void PadFromChat()
	{
		if (!SessionReady())
		{
			return;
		}
		DigPlanner.Result result = LastResult;
		if (result == null)
		{
			DigPlanner.Plan saved = Settings.LoadPlan();
			if (saved.Target.Length == 0)
			{
				Say("No dig plan yet. Make one with /digby dig or the window first.");
				return;
			}
			result = DigPlanner.Calculate(saved);
			if (saved.From.Equals("here", StringComparison.OrdinalIgnoreCase))
			{
				Say("Note: that plan starts \"here\", so it was worked out again from where you are now.");
			}
		}
		Datapads.Request(result, 0, Say);
	}

	/// <summary>/digby planet: where you are. /digby planet name &lt;name&gt; names the planet you're on; without a name it forgets it.</summary>
	private static void PlanetFromChat(string args)
	{
		if (!SessionReady())
		{
			return;
		}
		MyPlanet planet = Planets.YourPlanet();
		if (args.StartsWith("name", StringComparison.OrdinalIgnoreCase))
		{
			if (planet == null)
			{
				Say("You're in space, so there's no planet to name.");
				return;
			}
			string name = args.Substring(4).Trim().Replace("=", " ");
			if (name.Length == 0)
			{
				Settings.PlanetNames.Remove(planet.EntityId);
				Say($"Name forgotten. This planet is {Planets.Describe(planet)}.");
			}
			else
			{
				Settings.PlanetNames[planet.EntityId] = name.Length > 60 ? name.Substring(0, 60) : name;
				Say($"This planet is now called {Planets.Describe(planet)} in this world.");
			}
			Settings.SavePlanetNames();
			return;
		}
		Say("You are " + Planets.WhereYouAre() + "." + (planet != null && Planets.GivenName(planet, out _) == null ? $" The game calls this planet type {Planets.TypeName(planet)}; if the server calls it something else, name it with /digby planet name <name>." : ""));
	}

	/// <summary>/digby chat [off|quiet|normal|chatty]: sets how much Digby talks, or says what it's set to.</summary>
	private static void SetChatLevel(string word)
	{
		if (string.IsNullOrEmpty(word) || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			Say($"Chat personality is {Settings.Chattiness}. Change it with /digby chat off, quiet, normal or chatty.");
			return;
		}
		Chatter.SetLevel(level);
		Say($"Chat personality: {level}. {Chatter.LevelHint(level)}");
	}

	/// <summary>Calculates a plan, makes its markers and remembers it for this world. Returns the lines to show.</summary>
	internal static List<string> Run(DigPlanner.Plan plan)
	{
		DigPlanner.Result result = DigPlanner.Calculate(plan);
		List<string> lines = new List<string>(result.Lines);
		if (result.Ok)
		{
			LastPlan = plan;
			LastResult = result;
			string marker = DigPlanner.MakeMarkers(plan, result);
			if (marker != null)
			{
				lines.Add(marker);
			}
			Settings.SavePlan(DigPlanner.Pinned(plan));
			Personality.Say("plan_made", "length", Markers.FormatDistance(result.Length));
		}
		return lines;
	}

	internal static void OpenMenu(DigbyScreen.Page page)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			MyGuiSandbox.AddScreen(new DigbyScreen(page));
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			Say("Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Digby] {ex}");
		}
	}
}
