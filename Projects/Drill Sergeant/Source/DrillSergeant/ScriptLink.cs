using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using TimShared;
using VRage.Game.ModAPI;

namespace DrillSergeant;

/// <summary>
/// Script mode: the Automatic Mining Platform script by Kezeslabas runs the rig from a programmable block on the
/// server, untouched. The plugin only does what a player does with it by hand: puts the script's tags in block
/// names, edits the settings block in the PB's Custom Data, presses Run with an argument, and reads what the script
/// shows. The script writes its status to the PB's own screen every 100 ticks, and screen text reaches every client,
/// so that's where the status comes from; the PB's detailed info only reaches a client that asks for it (as the
/// terminal does while the PB is selected), so it's only read while the rig is open in the window.
/// </summary>
internal static class ScriptLink
{
	public const string WorkshopLink = "https://steamcommunity.com/sharedfiles/filedetails/?id=1695500366";

	/// <summary>The script redraws its screen every 100 ticks (1.7 s): a screen unchanged for longer isn't running.</summary>
	private const double RunningWindow = 5;

	// Field positions in the script's "|"-separated settings block (its own layout, read from what it writes).
	private const int FieldCount = 64;
	private const int FMainTag = 2, FAutoPause = 4, FHigh = 6, FLow = 8, FShareTensor = 14, FDynamicTensor = 16;
	private const int FMaxAngle = 20, FMinAngle = 22, FExcess = 24, FVerticalStep = 26, FUniqueHorizontal = 28, FHorizontalStep = 30, FRetract = 32;
	private const int FVerticalSpeed = 48, FHorizontalSpeed = 50, FRotorSpeed = 52;
	private const int FHorizontalTag = 56, FVerticalTag = 58, FInverseTag = 60, FAdvancedTag = 62;

	private static readonly Regex StepPattern = new Regex(@"Step: (\d+)/(\d+)", RegexOptions.Compiled);

	private static readonly Regex EtaPattern = new Regex(@"ETA: (\d+)h (\d+)m", RegexOptions.Compiled);

	private static readonly Regex CargoPattern = new Regex(@"Cargo:.*\] (\d+)%", RegexOptions.Compiled);

	public static IMyProgrammableBlock Pb(Rig rig) => RigBlocks.Get<IMyProgrammableBlock>(rig.ScriptPbId);

	/// <summary>The script's settings fields, or null when the PB doesn't have them (script not loaded yet).</summary>
	public static string[] Fields(IMyProgrammableBlock pb)
	{
		string data = pb?.CustomData ?? "";
		if (!data.StartsWith("@Configuration", StringComparison.Ordinal))
		{
			return null;
		}
		string[] fields = data.Split('|');
		return fields.Length == FieldCount ? fields : null;
	}

	private static string ScreenText(IMyProgrammableBlock pb)
	{
		return pb is Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider provider && provider.SurfaceCount > 0 ? provider.GetSurface(0).GetText() ?? "" : "";
	}

	/// <summary>Tracks when the PB's screen last changed; true while the script is redrawing it.</summary>
	private static bool Running(Rig rig, IMyProgrammableBlock pb, double now)
	{
		RigRuntime live = rig.Live;
		string text = ScreenText(pb);
		if (text != live.ScriptScreen)
		{
			// The first read after loading isn't a change.
			if (live.ScriptScreen != null)
			{
				live.ScriptScreenChangedAt = now;
			}
			live.ScriptScreen = text;
		}
		live.ScriptRunning = pb.IsWorking && now - live.ScriptScreenChangedAt < RunningWindow;
		return live.ScriptRunning;
	}

	// ---------------------------------------------------------------- plugin mode: is a script driving this rig?

	/// <summary>Null, or why the plugin mustn't drive a rig: a mining script is still running it.</summary>
	public static string ConflictFor(Rig rig, double now)
	{
		if (now - rig.Live.ScriptSearchedAt >= 10)
		{
			rig.Live.ScriptSearchedAt = now;
			rig.Live.FoundPbId = FindScriptPb(rig)?.EntityId ?? 0;
		}
		IMyProgrammableBlock pb = Pb(rig) ?? RigBlocks.Get<IMyProgrammableBlock>(rig.Live.FoundPbId);
		if (pb == null || !pb.Enabled || !Running(rig, pb, now))
		{
			return null;
		}
		return $"The mining script in {pb.CustomName} is running this rig. Sarge won't drive it at the same time: switch the rig to Script mode, or pick Plugin mode again in Setup to have the script paused and its PB switched off.";
	}

	/// <summary>A PB on the rig's grids whose script settings name a tag the rotor carries.</summary>
	private static IMyProgrammableBlock FindScriptPb(Rig rig)
	{
		IMyMotorStator rotor = RigBlocks.Get<IMyMotorStator>(rig.RotorId);
		if (rotor == null)
		{
			return null;
		}
		foreach (IMyProgrammableBlock pb in RigBlocks.BlocksOn<IMyProgrammableBlock>(RigBlocks.Construct(rotor)))
		{
			string[] fields = Fields(pb);
			if (fields != null && fields[FMainTag].Length > 0 && rotor.CustomName.Contains(fields[FMainTag]))
			{
				return pb;
			}
		}
		return null;
	}

	// ---------------------------------------------------------------- script mode: status

	public static void Update(Rig rig, double now)
	{
		RigRuntime live = rig.Live;
		IMyProgrammableBlock pb = Pb(rig);
		RigParts parts = RigParts.Resolve(rig);
		if (parts.Loaded)
		{
			live.Depth = parts.Depth;
			live.MaxDepth = parts.VerticalRange;
			if (now - live.CargoCheckedAt >= 5)
			{
				live.CargoCheckedAt = now;
				live.CargoFill = parts.CargoFill();
			}
		}
		if (pb == null)
		{
			live.InReach = false;
			Show(rig, rig.ScriptPbId == 0 ? "Not set up" : "Not loaded", rig.ScriptPbId == 0 ? "Pick the programmable block in Setup." : "The script's PB isn't loaded on your PC: you're too far away, or it was removed.");
			return;
		}
		if (now - live.ReachCheckedAt >= 3)
		{
			live.ReachCheckedAt = now;
			live.InReach = Reach.InReach(pb.CubeGrid);
		}
		bool running = Running(rig, pb, now);
		if (live.PendingPluginMode)
		{
			FinishSwitchToPlugin(rig, pb, running);
			return;
		}
		string text = live.ScriptScreen ?? "";
		string firstLine = text.Split('\n')[0];
		int bracket = firstLine.IndexOf("] ", StringComparison.Ordinal);
		string status = bracket >= 0 ? firstLine.Substring(bracket + 2).Trim() : firstLine.Trim();
		Match step = StepPattern.Match(text);
		if (step.Success && int.TryParse(step.Groups[1].Value, out int current) && int.TryParse(step.Groups[2].Value, out int last) && last > 0)
		{
			live.ScriptStep = current;
			live.Progress = Math.Min(1, (double)current / last);
		}
		Match eta = EtaPattern.Match(text);
		live.SecondsLeft = eta.Success ? int.Parse(eta.Groups[1].Value) * 3600 + int.Parse(eta.Groups[2].Value) * 60 : -1;
		Match cargo = CargoPattern.Match(text);
		if (cargo.Success && parts.Cargo.Count == 0 && rig.CargoIds.Count == 0)
		{
			live.CargoFill = int.Parse(cargo.Groups[1].Value) / 100.0;
		}

		string info = live.SubscribedPb == pb.EntityId ? pb.DetailedInfo : "";
		string detail = string.IsNullOrWhiteSpace(status) ? "" : "Script: " + status;
		if (!string.IsNullOrWhiteSpace(info))
		{
			detail += (detail.Length > 0 ? "\n" : "") + info.Trim().Replace("\r", "").Replace("\n\n", "\n");
		}
		if (Fields(pb) == null)
		{
			Show(rig, "Not set up", $"Load the Automatic Mining Platform script into {pb.CustomName} (Edit > Browse Scripts), press OK, then Apply to script in Setup.");
		}
		else if (!pb.IsWorking)
		{
			Show(rig, "Script off", $"{pb.CustomName} is off or has no power.");
		}
		else if (status.IndexOf("Completed", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			Show(rig, "Finished", detail);
			Driver.Alert(rig, "Finished", $"{rig.Name} is finished (the script says so).", false);
		}
		else if (running && status.IndexOf("Auto Paused", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			Show(rig, "Cargo full", detail);
			Driver.Alert(rig, "Cargo full", $"{rig.Name}: the script paused for full cargo.", false);
		}
		else if (running)
		{
			Show(rig, live.InReach ? "Mining" : "Mining (script)", detail);
			live.LastAlertState = null;
		}
		else if (info.IndexOf("Not Ready", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			Show(rig, "Warning", detail);
			Driver.Alert(rig, "Warning", $"{rig.Name}: the script reports a problem. Open Drill Sergeant to see it.", true);
		}
		else
		{
			Show(rig, "Paused", detail.Length > 0 ? detail : "The script isn't running. Press Start.");
		}
	}

	private static void Show(Rig rig, string state, string detail)
	{
		rig.Live.State = state;
		rig.Live.Detail = detail;
	}

	/// <summary>Asks the server for the PB's detailed info while the rig is open in the window, the way selecting it in the terminal does; null stops it.</summary>
	public static void Subscribe(Rig rig)
	{
		long wanted = rig != null && rig.Mode == RigMode.Script ? rig.ScriptPbId : 0;
		foreach (Rig other in RigStore.Rigs)
		{
			if (other.Live.SubscribedPb != 0 && other.Live.SubscribedPb != wanted)
			{
				SetSubscription(other.Live.SubscribedPb, false);
				other.Live.SubscribedPb = 0;
			}
		}
		if (rig != null && wanted != 0 && rig.Live.SubscribedPb != wanted && SetSubscription(wanted, true))
		{
			rig.Live.SubscribedPb = wanted;
		}
	}

	private static bool SetSubscription(long pbId, bool on)
	{
		if (RigBlocks.Get<IMyProgrammableBlock>(pbId) is MyTerminalBlock block)
		{
			block.OnOpenedInTerminal(on);
			return true;
		}
		return false;
	}

	// ---------------------------------------------------------------- commands

	/// <summary>Presses Run with an argument, the same request as the terminal's Run button.</summary>
	public static string Send(Rig rig, string argument)
	{
		IMyProgrammableBlock pb = Pb(rig);
		if (pb == null)
		{
			return "The script's PB isn't loaded here.";
		}
		if (!Reach.InReach(pb.CubeGrid))
		{
			return $"{pb.CustomName} is out of reach: be within {Reach.Distance:0} m of it or linked to it by antenna.";
		}
		if (!pb.HasLocalPlayerAccess())
		{
			return $"You have no access to {pb.CustomName}.";
		}
		if (!pb.IsWorking)
		{
			return $"{pb.CustomName} is off or has no power.";
		}
		pb.Run(argument, Sandbox.ModAPI.Ingame.UpdateType.Terminal);
		return $"{rig.Name}: sent \"{argument}\" to the script.";
	}

	/// <summary>The step the script can be set to so it carries on where the plugin got to ("set;N" only takes even steps).</summary>
	public static int HandoverStep(Rig rig)
	{
		return Math.Max(0, rig.Step - rig.Step % 2);
	}

	// ---------------------------------------------------------------- switching modes

	public static string SwitchToScript(Rig rig)
	{
		string note = "";
		if (rig.Mode == RigMode.Plugin && rig.Command == RigCommand.Running)
		{
			note = Driver.Pause(rig) + " ";
		}
		rig.Mode = RigMode.Script;
		rig.Live.PendingPluginMode = false;
		IMyProgrammableBlock pb = Pb(rig);
		if (pb != null && !pb.Enabled && pb.HasLocalPlayerAccess() && Reach.InReach(pb.CubeGrid))
		{
			// Sarge switched it off when it took the rig over; the script gets its PB back.
			pb.Enabled = true;
		}
		RigStore.Changed();
		int step = HandoverStep(rig);
		return note + (step > 0 ? $"Script mode. Continue at step {step} sends \"set;{step}\" and \"start\", so the script carries on where Sarge stopped." : "Script mode. Use Set, then Start.");
	}

	/// <summary>Plugin mode only once the script has stopped and its PB is off, so they never both drive the rig.</summary>
	public static string SwitchToPlugin(Rig rig, double now)
	{
		IMyProgrammableBlock pb = Pb(rig);
		if (pb == null)
		{
			if (rig.ScriptPbId != 0)
			{
				return "Can't switch yet: the script's PB isn't loaded here, so Sarge can't make sure the script has stopped.";
			}
			rig.Mode = RigMode.Plugin;
			RigStore.Changed();
			return "Plugin mode.";
		}
		if (!pb.Enabled)
		{
			rig.Mode = RigMode.Plugin;
			rig.Command = rig.Command == RigCommand.Running ? RigCommand.Paused : rig.Command;
			RigStore.Changed();
			return "Plugin mode: the script's PB is off.";
		}
		string why = Send(rig, "pause");
		if (!why.StartsWith(rig.Name, StringComparison.Ordinal))
		{
			return "Can't switch yet: " + why;
		}
		rig.Live.PendingPluginMode = true;
		return $"Pausing the script; Sarge switches {pb.CustomName} off and takes over as soon as it has stopped.";
	}

	private static void FinishSwitchToPlugin(Rig rig, IMyProgrammableBlock pb, bool running)
	{
		Show(rig, "Switching", "Waiting for the script to stop, then its PB goes off and Sarge takes over.");
		if (running && rig.Live.ScriptScreen?.IndexOf("Paused", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return;
		}
		if (pb.Enabled)
		{
			if (pb.HasLocalPlayerAccess() && Reach.InReach(pb.CubeGrid))
			{
				pb.Enabled = false;
			}
			return;
		}
		rig.Live.PendingPluginMode = false;
		rig.Mode = RigMode.Plugin;
		if (rig.Live.ScriptStep > 0)
		{
			rig.Step = rig.Live.ScriptStep - rig.Live.ScriptStep % 2;
			rig.SweepLeftStart = false;
			rig.MoveHorizontalDone = false;
			rig.Command = RigCommand.Paused;
		}
		rig.Live.Issued = false;
		RigStore.Changed();
		Alerts.Send(rig, $"{rig.Name}: the script is stopped and its PB is off. Sarge has the rig{(rig.Live.ScriptStep > 0 ? $" at step {rig.Step}" : "")}; press Start.", false);
	}

	// ---------------------------------------------------------------- setting the script up

	/// <summary>Puts the script's tags on the rig's blocks and writes the rig's settings into the script's settings block.</summary>
	public static string Apply(Rig rig)
	{
		IMyProgrammableBlock pb = Pb(rig);
		RigParts parts = RigParts.Resolve(rig);
		if (pb == null || !parts.Loaded)
		{
			return "The PB or the rotor isn't loaded here.";
		}
		if (!Reach.InReach(pb.CubeGrid))
		{
			return $"Out of reach: be within {Reach.Distance:0} m of the rig or linked to it by antenna.";
		}
		string[] fields = Fields(pb);
		if (fields == null)
		{
			return $"Load the script into {pb.CustomName} first: Edit > Browse Scripts > Automatic Mining Platform, then OK. The script writes its settings block, and Apply fills it in. Workshop: {ScriptLink.WorkshopLink}";
		}
		if (!(parts.Rotor is IMyMotorAdvancedStator))
		{
			return "The script only drives an advanced rotor; this rig's rotor is a plain one. Plugin mode works with either.";
		}
		List<IMyTerminalBlock> all = RigBlocks.BlocksOn<IMyTerminalBlock>(LogicalGrids(pb));
		string tag = FreeTag(rig, pb, fields[FMainTag], all);
		string hor = fields[FHorizontalTag], ver = fields[FVerticalTag], inv = fields[FInverseTag], adv = fields[FAdvancedTag];
		string[] ourTags = { tag, hor, ver, inv, adv };

		// Who gets which tags.
		Dictionary<long, string> wanted = new Dictionary<long, string> { [parts.Rotor.EntityId] = tag };
		foreach (IMyPistonBase piston in parts.Horizontal)
		{
			wanted[piston.EntityId] = tag + hor;
		}
		foreach (IMyPistonBase piston in parts.Vertical)
		{
			wanted[piston.EntityId] = tag + ver + (parts.Inverted.Contains(piston.EntityId) ? inv : "");
		}
		foreach (IMyShipDrill drill in parts.Drills)
		{
			wanted[drill.EntityId] = tag;
		}
		foreach (IMyTerminalBlock cargo in parts.Cargo)
		{
			wanted[cargo.EntityId] = tag;
		}
		if (RigBlocks.Get<IMyTextPanel>(rig.LcdId) is IMyTextPanel lcd)
		{
			wanted[lcd.EntityId] = tag;
		}
		if (RigBlocks.Get<IMyTimerBlock>(rig.FinishedTimerId) is IMyTimerBlock done)
		{
			wanted[done.EntityId] = tag;
		}
		if (RigBlocks.Get<IMyTimerBlock>(rig.CargoTimerId) is IMyTimerBlock full)
		{
			wanted[full.EntityId] = tag + adv;
		}

		int renamed = 0;
		int refused = 0;
		foreach (IMyTerminalBlock block in all)
		{
			bool ours = wanted.TryGetValue(block.EntityId, out string tags);
			bool hasTag = block.CustomName.Contains(tag);
			if (!ours && !hasTag)
			{
				continue;
			}
			// Blocks that carry the tag but aren't part of the rig lose it, or the script would pick them up.
			string name = Untagged(block.CustomName, ours || hasTag ? ourTags : new string[0]) + (ours ? tags : "");
			if (name == block.CustomName)
			{
				continue;
			}
			if (!block.HasLocalPlayerAccess())
			{
				refused++;
				continue;
			}
			block.CustomName = name;
			renamed++;
		}

		// The settings block: only the values the window sets; everything else stays as the script wrote it.
		NumberFormatInfo number = fields[FHigh].Contains(",") ? new NumberFormatInfo { NumberDecimalSeparator = "," } : NumberFormatInfo.InvariantInfo;
		string F(double value) => value.ToString("0.###", number);
		fields[FMainTag] = tag;
		fields[FAutoPause] = rig.CargoPause ? "True" : "False";
		fields[FHigh] = F(rig.CargoHigh);
		fields[FLow] = F(rig.CargoLow);
		fields[FShareTensor] = rig.ShareTensor ? "True" : "False";
		fields[FDynamicTensor] = rig.TensorRefresh ? "True" : "False";
		fields[FMaxAngle] = F(rig.MaxAngle);
		fields[FMinAngle] = F(rig.MinAngle);
		fields[FExcess] = F(ExcessMetres(rig, parts));
		fields[FVerticalStep] = F(rig.PlanVerticalStep);
		fields[FUniqueHorizontal] = "True";
		fields[FHorizontalStep] = F(rig.PlanHorizontalStep);
		fields[FRetract] = rig.RetractFirst ? "True" : "False";
		fields[FVerticalSpeed] = F(rig.VerticalSpeed);
		fields[FHorizontalSpeed] = F(rig.HorizontalSpeed);
		fields[FRotorSpeed] = F(rig.RpmAt10);
		string data = string.Join("|", fields);
		bool dataChanged = data != pb.CustomData;
		if (dataChanged)
		{
			pb.CustomData = data;
		}
		rig.ScriptTag = tag;
		RigStore.Changed();
		string result = $"Script set up with tag {tag}: {renamed} block name(s) changed{(dataChanged ? ", settings written" : "")}.";
		if (refused > 0)
		{
			result += $" {refused} block(s) aren't yours to rename.";
		}
		return result + " Now press Set (the script reads its settings then), then Start.";
	}

	private static List<IMyCubeGrid> LogicalGrids(IMyProgrammableBlock pb)
	{
		// The script searches its whole terminal system, docked ships included.
		List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(pb.CubeGrid, GridLinkTypeEnum.Logical, grids);
		if (!grids.Contains(pb.CubeGrid))
		{
			grids.Add(pb.CubeGrid);
		}
		return grids;
	}

	/// <summary>The PB's own tag, unless another mining PB on the same terminal system uses it: then the first free "/Mine NN/".</summary>
	private static string FreeTag(Rig rig, IMyProgrammableBlock pb, string current, List<IMyTerminalBlock> all)
	{
		HashSet<string> used = new HashSet<string>(all.OfType<IMyProgrammableBlock>().Where(p => p.EntityId != pb.EntityId).Select(Fields).Where(f => f != null).Select(f => f[FMainTag]));
		if (!string.IsNullOrEmpty(rig.ScriptTag) && !used.Contains(rig.ScriptTag))
		{
			return rig.ScriptTag;
		}
		if (!string.IsNullOrEmpty(current) && !used.Contains(current))
		{
			return current;
		}
		for (int i = 1; i < 100; i++)
		{
			string tag = $"/Mine {i:00}/";
			if (!used.Contains(tag))
			{
				return tag;
			}
		}
		return current;
	}

	private static string Untagged(string name, string[] tags)
	{
		foreach (string tag in tags.Where(t => !string.IsNullOrEmpty(t)))
		{
			name = name.Replace(tag, "");
		}
		return name.TrimEnd();
	}

	/// <summary>
	/// The script works out the arm's length as (horizontal pistons x piston body length) + "non-piston metres";
	/// this fills in the second part from where the drills really are.
	/// </summary>
	private static double ExcessMetres(Rig rig, RigParts parts)
	{
		double body = parts.Horizontal.Count > 0 && parts.Horizontal[0].HighestPosition <= 2.01f ? 1 : 5;
		return Math.Max(0, Math.Round(rig.RadiusRetracted - parts.Horizontal.Count * body, 1));
	}
}
