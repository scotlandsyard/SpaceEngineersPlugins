using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Utils;

namespace Tally;

/// <summary>
/// Wires up the window, the /tally chat command and the plugin switcher, works out bills, keeps the last one, and
/// checks that a datapad write arrived. Client only.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class TallySession : MySessionComponentBase
{
	public const string ChatSender = "Tally";

	public const string ChatCommand = "/tally";

	/// <summary>The last bill worked out this session (the datapad is written from it).</summary>
	internal static Bill LastBill;

	/// <summary>Library blueprints already read this session, by folder (the file only changes if you save over it).</summary>
	private static readonly Dictionary<string, KeyValuePair<DateTime, Count>> s_blueprints = new Dictionary<string, KeyValuePair<DateTime, Count>>(StringComparer.OrdinalIgnoreCase);

	private static readonly List<KeyValuePair<Datapads.Write, Action<string>>> s_expected = new List<KeyValuePair<Datapads.Write, Action<string>>>();

	private static double s_expectedAt;

	private static bool s_reading;

	private bool _started;

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

	/// <summary>What to take off and how, from the window or the saved settings.</summary>
	internal class Options
	{
		public bool StillNeeded = Settings.StillNeeded;

		public bool Carried = Settings.Carried;

		public bool Cargo = Settings.Cargo;

		public double YieldPercent = Settings.YieldPercent;
	}

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
			PluginSwitcher.Register(Plugin.Name, () => OpenMenu(TallyScreen.Page.Tally));
			Personality.Level = Settings.Chattiness;
			Personality.DisplayName = Settings.DisplayName;
			Personality.Changed += Chatter.Save;
			Personality.Register(ChatSender);
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tally] BeforeStart failed: {ex}");
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
				MyLog.Default.WriteLineAndConsole($"[Tally] Unload: {ex}");
			}
		}
		Materials.Unload();
		LastBill = null;
		s_blueprints.Clear();
		s_expected.Clear();
		s_reading = false;
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
			MyLog.Default.WriteLineAndConsole($"[Tally] Datapad check: {ex}");
		}
	}

	/// <summary>
	/// Works out the bill for a source. Projections and grids are counted straight away; a library blueprint is read
	/// from its file on a background thread. <paramref name="done"/> runs on the game thread with the bill, or with
	/// null and the reason.
	/// </summary>
	internal static void Run(Source source, Options options, Action<Bill, string> done)
	{
		if (source == null)
		{
			done(null, "Pick something to tally first.");
			return;
		}
		if (source.Kind == Source.Library)
		{
			DateTime written = BlueprintTime(source.Folder);
			if (s_blueprints.TryGetValue(source.Folder, out KeyValuePair<DateTime, Count> cached) && cached.Key == written)
			{
				Finish(source, cached.Value, options, done);
				return;
			}
			if (s_reading)
			{
				done(null, "Still reading the last blueprint; try again in a moment.");
				return;
			}
			s_reading = true;
			Count count = null;
			string error = null;
			MyAPIGateway.Parallel.StartBackground(() =>
			{
				try
				{
					count = Sources.CountBlueprint(source);
				}
				catch (Exception ex)
				{
					error = ex.Message;
					MyLog.Default.WriteLineAndConsole($"[Tally] Reading {source.Label}: {ex}");
				}
			}, () =>
			{
				s_reading = false;
				if (count == null)
				{
					done(null, $"\"{source.Label}\" couldn't be read: {error}");
					return;
				}
				s_blueprints[source.Folder] = new KeyValuePair<DateTime, Count>(written, count);
				Finish(source, count, options, done);
			});
			return;
		}
		MyEntity entity = Sources.Entity(source);
		Count live;
		if (entity is MyProjectorBase projector && projector.ProjectedGrid != null)
		{
			live = Sources.CountProjector(projector, options.StillNeeded);
		}
		else if (entity is MyCubeGrid grid)
		{
			live = Sources.CountGrid(grid, options.StillNeeded);
		}
		else
		{
			done(null, source.Kind == Source.Projector ? "That projector isn't projecting any more, or it's out of range. Refresh the list." : "That grid is out of range or gone. Refresh the list.");
			return;
		}
		Finish(source, live, options, done);
	}

	private static DateTime BlueprintTime(string folder)
	{
		try
		{
			return System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(folder, Sandbox.Game.GUI.MyBlueprintUtils.BLUEPRINT_LOCAL_NAME));
		}
		catch (Exception)
		{
			return DateTime.MinValue;
		}
	}

	private static void Finish(Source source, Count count, Options options, Action<Bill, string> done)
	{
		Dictionary<MyDefinitionId, double> have = null;
		List<string> from = new List<string>();
		if (options.Carried)
		{
			have = new Dictionary<MyDefinitionId, double>();
			Sources.AddCarried(have);
			from.Add("your inventory");
		}
		MyCubeGrid cargo = options.Cargo && source.Kind != Source.Library ? Sources.CargoGrid(source) : null;
		if (cargo != null)
		{
			have = have ?? new Dictionary<MyDefinitionId, double>();
			Sources.AddCargo(cargo, have);
			from.Add($"the cargo of {cargo.DisplayName} and what's docked to it");
		}
		Bill bill = Materials.Compute(count, have, string.Join(" and ", from), options.YieldPercent);
		bill.SourceKey = source.Key;
		LastBill = bill;
		Personality.Say("tallied", "blocks", bill.Blocks.ToString("N0"));
		done(bill, null);
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
				OpenMenu(TallyScreen.Page.Tally);
				break;
			case "help":
				OpenMenu(TallyScreen.Page.Help);
				break;
			case "pad":
			case "datapad":
				PadFromChat();
				break;
			case "chat":
				SetChatLevel(rest);
				break;
			case "name":
				Chatter.SetDisplayName(rest);
				break;
			default:
				Say("Commands: /tally (window), /tally pad (write the last list to datapads), /tally help, /tally chat, /tally name.");
				break;
			}
		}
		catch (Exception ex)
		{
			Say("That didn't work: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Tally] Chat command: {ex}");
		}
	}

	internal static void Say(string line)
	{
		MyAPIGateway.Utilities.ShowMessage(ChatSender, line);
	}

	/// <summary>/tally pad: writes this session's last list to datapads, auto-picked.</summary>
	private static void PadFromChat()
	{
		if (LastBill == null)
		{
			Say("Nothing tallied yet this session. Open /tally, pick a projector, blueprint or grid and press Tally first.");
			return;
		}
		Datapads.Request(LastBill, 0, Say);
	}

	/// <summary>/tally chat [off|quiet|normal|chatty]: sets how much Tally talks, or says what it's set to.</summary>
	private static void SetChatLevel(string word)
	{
		if (string.IsNullOrEmpty(word) || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			Say($"Chat personality is {Settings.Chattiness}. Change it with /tally chat off, quiet, normal or chatty.");
			return;
		}
		Chatter.SetLevel(level);
		Say($"Chat personality: {level}. {Chatter.LevelHint(level)}");
	}

	internal static void OpenMenu(TallyScreen.Page page)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			MyGuiSandbox.AddScreen(new TallyScreen(page));
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			Say("Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Tally] {ex}");
		}
	}
}
