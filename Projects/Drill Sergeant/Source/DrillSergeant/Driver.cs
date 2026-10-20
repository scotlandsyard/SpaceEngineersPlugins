using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using TimShared;
using VRageMath;

namespace DrillSergeant;

/// <summary>
/// Runs a plugin-driven rig. Every step is sent once, as the same settings a player would type into the terminal:
/// a sweep sets the rotor's velocity towards its other limit, a move sets each piston's limits and velocity. The
/// blocks then stop by themselves at those limits, so a rig that loses contact (you leave, log off, or go out of
/// reach) finishes the step it's on and stands still. All the plugin does in between is read angles, positions and
/// inventories on this PC, which sends nothing.
/// </summary>
internal static class Driver
{
	private const double AngleTolerance = 1.0;

	private const double PositionTolerance = 0.05;

	/// <summary>No movement for this long while a step isn't done: warn that the rig looks stuck.</summary>
	private const double StuckSeconds = 90;

	/// <summary>A setting we sent that reads different for this long was changed by something else, or refused.</summary>
	private const double InterferenceSeconds = 4;

	public static Pattern PatternFor(Rig rig, RigParts parts)
	{
		return new Pattern(parts.HorizontalRange, parts.VerticalRange, rig.PlanHorizontalStep, rig.PlanVerticalStep, rig.RetractFirst);
	}

	// ---------------------------------------------------------------- player commands

	public static string Start(Rig rig)
	{
		RigParts parts = RigParts.Resolve(rig);
		string why = CanSend(rig, parts);
		if (why != null)
		{
			return why;
		}
		if (rig.Command == RigCommand.Finished || rig.Command == RigCommand.Idle)
		{
			ResetProgress(rig);
		}
		rig.Command = RigCommand.Running;
		rig.CargoPaused = false;
		rig.PauseOwed = false;
		rig.Live.Issued = false;
		rig.Live.Warning = null;
		rig.Live.StuckWarned = false;
		rig.Live.LastAlertState = null;
		string note = rig.ShareTensor ? ShareTensor(parts) : null;
		RigStore.Changed();
		Personality.Say("rig_started", "rig", rig.Name);
		return note == null ? $"{rig.Name}: started." : $"{rig.Name}: started. {note}";
	}

	public static string Pause(Rig rig)
	{
		RigParts parts = RigParts.Resolve(rig);
		if (rig.Command != RigCommand.Running)
		{
			return $"{rig.Name} isn't running.";
		}
		rig.Command = RigCommand.Paused;
		rig.Live.Issued = false;
		RigStore.Changed();
		string why = CanSend(rig, parts);
		if (why != null)
		{
			// Switched off as soon as it's back in reach; until then the step it was on finishes by itself.
			rig.PauseOwed = true;
			return why + " It finishes the step it was on and is switched off as soon as it's back in reach.";
		}
		HoldStill(rig, parts);
		return $"{rig.Name}: paused where it is.";
	}

	/// <summary>Ends the job: drills and rotor off, pistons back to the start, progress back to step 0.</summary>
	public static string Stop(Rig rig)
	{
		RigParts parts = RigParts.Resolve(rig);
		string why = CanSend(rig, parts);
		if (why != null)
		{
			return why;
		}
		ResetProgress(rig);
		rig.Command = RigCommand.Idle;
		RigStore.Changed();
		SetDrills(parts, false);
		if (parts.Rotor.Enabled)
		{
			parts.Rotor.Enabled = false;
		}
		Pattern pattern = PatternFor(rig, parts);
		MovePistons(rig, parts, pattern, 0, true, true);
		return $"{rig.Name}: stopped. The pistons go back to the start; progress starts over.";
	}

	public static void ResetProgress(Rig rig)
	{
		rig.Step = 0;
		rig.AtMinSide = true;
		rig.SweepLeftStart = false;
		rig.MoveHorizontalDone = false;
		rig.CargoPaused = false;
		rig.Live.Issued = false;
	}

	/// <summary>Null when requests for the rig would be accepted, else why not.</summary>
	public static string CanSend(Rig rig, RigParts parts)
	{
		if (!parts.Loaded)
		{
			return $"{rig.Name} isn't loaded here (too far away?).";
		}
		if (!Reach.InReach(parts.Rotor.CubeGrid))
		{
			return $"{rig.Name} is out of reach: be within {Reach.Distance:0} m of it or linked to it by antenna.";
		}
		if (parts.Missing > 0)
		{
			return $"{rig.Name}: {parts.Missing} of its blocks are gone. Check Setup.";
		}
		if (parts.Drills.Count == 0)
		{
			return $"{rig.Name} has no drills.";
		}
		IMyTerminalBlock noAccess = parts.Moving.Concat(parts.Drills).FirstOrDefault(b => !b.HasLocalPlayerAccess());
		return noAccess != null ? $"You have no access to {noAccess.CustomName}." : null;
	}

	// ---------------------------------------------------------------- every half second

	public static void Update(Rig rig, double now)
	{
		RigRuntime live = rig.Live;
		RigParts parts = RigParts.Resolve(rig);
		if (!parts.Loaded)
		{
			live.InReach = false;
			live.State = "Not loaded";
			live.Detail = "The rotor isn't loaded on your PC: you're too far away, or it was removed.";
			return;
		}
		Pattern pattern = PatternFor(rig, parts);
		if (now - live.ReachCheckedAt >= 3)
		{
			live.ReachCheckedAt = now;
			live.InReach = Reach.InReach(parts.Rotor.CubeGrid);
		}
		if (now - live.CargoCheckedAt >= 5)
		{
			live.CargoCheckedAt = now;
			live.CargoFill = parts.CargoFill();
		}
		live.Depth = parts.Depth;
		live.MaxDepth = parts.VerticalRange;
		live.Progress = pattern.LastStep > 0 ? Math.Min(1, (double)rig.Step / pattern.LastStep) : 0;
		live.SecondsLeft = rig.Command == RigCommand.Finished ? 0 : pattern.SecondsLeft(rig.Step, rig.MaxAngle - rig.MinAngle, rig.RpmAt10, rig.RadiusRetracted, rig.HorizontalSpeed, rig.VerticalSpeed);

		string conflict = ScriptLink.ConflictFor(rig, now);
		if (conflict != null)
		{
			live.State = "Conflict";
			live.Detail = conflict;
			return;
		}
		switch (rig.Command)
		{
		case RigCommand.Idle:
			Show(rig, "Ready", "Press Start to begin at step 0.");
			return;
		case RigCommand.Finished:
			Show(rig, "Finished", "Done: everything in reach is mined. Start runs it again from the top.");
			return;
		case RigCommand.Paused:
			if (rig.PauseOwed && live.InReach && CanSend(rig, parts) == null)
			{
				rig.PauseOwed = false;
				HoldStill(rig, parts);
			}
			Show(rig, live.Warning != null ? "Warning" : "Paused", live.Warning ?? $"Paused at step {rig.Step} of {pattern.LastStep}.");
			return;
		}

		if (!live.InReach)
		{
			Show(rig, "Out of reach", $"Waiting at step {rig.Step} of {pattern.LastStep}. The step in progress finishes by itself; the next one starts when you're within {Reach.Distance:0} m or linked by antenna.");
			return;
		}
		if (parts.Missing > 0 || parts.Problems.Count > 0)
		{
			string problem = parts.Missing > 0 ? $"{parts.Missing} block(s) are gone" : string.Join(", ", parts.Problems.Take(3));
			Show(rig, "Warning", $"Waiting: {problem}.");
			Alert(rig, "Warning", $"{rig.Name} needs you: {problem}.", true);
			return;
		}
		if (CheckCargo(rig, parts, live))
		{
			return;
		}
		if (!live.Issued)
		{
			Issue(rig, parts, pattern, now);
			return;
		}
		string interference = Interference(rig, parts, now);
		if (interference != null)
		{
			rig.Command = RigCommand.Paused;
			live.Warning = interference + " Sarge stopped driving the rig; check it and press Start.";
			RigStore.Changed();
			Alert(rig, "Warning", $"{rig.Name}: {interference}", true);
			return;
		}
		TensorRefresh(rig, parts, now);
		if (StepDone(rig, parts, pattern, now))
		{
			Advance(rig, parts, pattern);
			return;
		}
		if (live.LastAlertState == "Warning")
		{
			// The problem is gone; the next one gets its own alert.
			live.LastAlertState = null;
		}
		Show(rig, Pattern.IsSweep(rig.Step) ? "Mining" : rig.Step == 0 ? "Lining up" : rig.Step >= pattern.LastStep ? "Retracting" : "Moving arm", StepText(rig, pattern));
	}

	private static void Show(Rig rig, string state, string detail)
	{
		rig.Live.State = state;
		rig.Live.Detail = detail;
	}

	private static string StepText(Rig rig, Pattern pattern)
	{
		int position = pattern.PositionOf(rig.Step);
		return $"Step {rig.Step} of {pattern.LastStep}: ring {pattern.RingOf(position) + 1}/{pattern.Rings + 1}, layer {pattern.LayerOf(position) + 1}/{pattern.Layers + 1}.";
	}

	/// <summary>True while the cargo pause holds the rig.</summary>
	private static bool CheckCargo(Rig rig, RigParts parts, RigRuntime live)
	{
		if (!rig.CargoPause || live.CargoFill < 0 || rig.Step >= PatternFor(rig, parts).LastStep)
		{
			if (rig.CargoPaused)
			{
				rig.CargoPaused = false;
				live.Issued = false;
			}
			return false;
		}
		if (!rig.CargoPaused && live.CargoFill >= rig.CargoHigh)
		{
			rig.CargoPaused = true;
			RigStore.Changed();
			HoldStill(rig, parts);
			StartTimer(rig.CargoTimerId);
			Alert(rig, "Cargo full", $"{rig.Name}: cargo {live.CargoFill:P0} full, pausing until it's down to {rig.CargoLow:P0}.", false);
			Personality.Say("cargo_full", "rig", rig.Name);
		}
		else if (rig.CargoPaused && live.CargoFill <= rig.CargoLow)
		{
			rig.CargoPaused = false;
			live.Issued = false;
			RigStore.Changed();
			Alert(rig, "Resumed", $"{rig.Name}: cargo down to {live.CargoFill:P0}, mining again.", false);
		}
		if (rig.CargoPaused)
		{
			Show(rig, "Cargo full", $"Paused at {live.CargoFill:P0} full; carries on at {rig.CargoLow:P0}.");
			return true;
		}
		return false;
	}

	// ---------------------------------------------------------------- sending a step

	private static readonly Dictionary<long, Vector3> s_expected = new Dictionary<long, Vector3>();

	private static readonly Dictionary<long, double> s_mismatchSince = new Dictionary<long, double>();

	private static void Issue(Rig rig, RigParts parts, Pattern pattern, double now)
	{
		RigRuntime live = rig.Live;
		live.Issued = true;
		live.IssuedAt = now;
		live.LastMovedAt = now;
		live.LastValue = double.NaN;
		live.StuckWarned = false;
		ClearExpected(parts);
		int step = rig.Step;
		bool finishing = step >= pattern.LastStep;
		SetDrills(parts, !finishing);
		if (Pattern.IsSweep(step))
		{
			SetRotorLimits(parts.Rotor, rig.MinAngle, rig.MaxAngle);
			float rpm = (float)Pattern.RotorRpm(rig.RpmAt10, rig.RadiusRetracted + pattern.HorizontalAt(pattern.PositionOf(step)));
			SetRotorVelocity(parts.Rotor, rig.AtMinSide ? rpm : -rpm);
			Expect(parts.Rotor.EntityId, rig.MinAngle, rig.MaxAngle, rig.AtMinSide ? rpm : -rpm);
			Show(rig, "Mining", StepText(rig, pattern));
			return;
		}
		if (step == 0)
		{
			LineUpRotor(rig, parts);
		}
		bool both = step > 0 && BothChange(pattern, step);
		MovePistons(rig, parts, pattern, pattern.PositionOf(step), !both || !rig.MoveHorizontalDone, !both || rig.MoveHorizontalDone);
	}

	/// <summary>A move that changes both groups does the horizontal half first: pull in before going down or up.</summary>
	private static bool BothChange(Pattern pattern, int step)
	{
		int to = pattern.PositionOf(step);
		int from = pattern.PositionOf(step - 1);
		return Math.Abs(pattern.HorizontalAt(to) - pattern.HorizontalAt(from)) > 0.01 && Math.Abs(pattern.VerticalAt(to) - pattern.VerticalAt(from)) > 0.01;
	}

	private static void MovePistons(Rig rig, RigParts parts, Pattern pattern, int position, bool horizontal, bool vertical)
	{
		if (horizontal)
		{
			double total = pattern.HorizontalAt(position);
			foreach (IMyPistonBase piston in parts.Horizontal)
			{
				SetPiston(piston, Pattern.PistonGoal(piston.LowestPosition, piston.HighestPosition, total, pattern.HorizontalRange, false), Share(piston, parts.HorizontalRange) * rig.HorizontalSpeed);
			}
		}
		if (vertical)
		{
			double total = pattern.VerticalAt(position);
			foreach (IMyPistonBase piston in parts.Vertical)
			{
				SetPiston(piston, Pattern.PistonGoal(piston.LowestPosition, piston.HighestPosition, total, pattern.VerticalRange, parts.Inverted.Contains(piston.EntityId)), Share(piston, parts.VerticalRange) * rig.VerticalSpeed);
			}
		}
	}

	/// <summary>Every piston of a group covers the same share of its travel, so it moves at that share of the group's speed.</summary>
	private static float Share(IMyPistonBase piston, double groupRange)
	{
		return groupRange > 0.001 ? (float)((piston.HighestPosition - piston.LowestPosition) / groupRange) : 1f;
	}

	private static void SetPiston(IMyPistonBase piston, double goal, float speed)
	{
		float target = (float)goal;
		float velocity = MathHelper.Clamp(speed, 0.01f, piston.MaxVelocity) * (target >= piston.CurrentPosition ? 1 : -1);
		// Widen before narrowing, so the limits never cross on the way.
		if (target > piston.MaxLimit)
		{
			Set(piston.MaxLimit, target, v => piston.MaxLimit = v);
			Set(piston.MinLimit, target, v => piston.MinLimit = v);
		}
		else
		{
			Set(piston.MinLimit, target, v => piston.MinLimit = v);
			Set(piston.MaxLimit, target, v => piston.MaxLimit = v);
		}
		Set(piston.Velocity, velocity, v => piston.Velocity = v);
		if (!piston.Enabled)
		{
			piston.Enabled = true;
		}
		Expect(piston.EntityId, target, target, velocity);
	}

	/// <summary>Sends a value only when it would change: an unchanged one is no request at all.</summary>
	private static void Set(float current, float wanted, Action<float> set)
	{
		if (Math.Abs(current - wanted) > 0.001f)
		{
			set(wanted);
		}
	}

	private static void SetRotorLimits(IMyMotorStator rotor, float min, float max)
	{
		if (Math.Abs(rotor.LowerLimitDeg - min) > 0.05f || Math.Abs(rotor.UpperLimitDeg - max) > 0.05f)
		{
			// The game keeps lower <= upper by moving the other limit, so set the side that opens the range first.
			if (min > rotor.UpperLimitDeg)
			{
				rotor.UpperLimitDeg = max;
				rotor.LowerLimitDeg = min;
			}
			else
			{
				rotor.LowerLimitDeg = min;
				rotor.UpperLimitDeg = max;
			}
		}
	}

	private static void SetRotorVelocity(IMyMotorStator rotor, float rpm)
	{
		Set(rotor.TargetVelocityRPM, rpm, v => rotor.TargetVelocityRPM = v);
		if (!rotor.Enabled)
		{
			rotor.Enabled = true;
		}
	}

	/// <summary>Step 0: turn the rotor to the nearer end of its range. A rotor outside its range is pulled to that end by setting both limits there until it arrives.</summary>
	private static void LineUpRotor(Rig rig, RigParts parts)
	{
		double angle = AngleDeg(parts.Rotor);
		double fromMin = Mod360(angle - rig.MinAngle);
		double range = rig.MaxAngle - rig.MinAngle;
		bool inside = fromMin <= range + AngleTolerance;
		bool toMin;
		if (inside)
		{
			toMin = fromMin <= range - fromMin;
		}
		else
		{
			toMin = Circular(angle, rig.MinAngle) <= Circular(angle, rig.MaxAngle);
		}
		float target = toMin ? rig.MinAngle : rig.MaxAngle;
		float rpm = (float)Pattern.RotorRpm(rig.RpmAt10, Math.Max(rig.RadiusRetracted, 1));
		if (inside)
		{
			SetRotorLimits(parts.Rotor, rig.MinAngle, rig.MaxAngle);
			Expect(parts.Rotor.EntityId, rig.MinAngle, rig.MaxAngle, toMin ? -rpm : rpm);
			SetRotorVelocity(parts.Rotor, toMin ? -rpm : rpm);
		}
		else
		{
			SetRotorLimits(parts.Rotor, target, target);
			bool up = Mod360(target - angle) <= 180;
			Expect(parts.Rotor.EntityId, target, target, up ? rpm : -rpm);
			SetRotorVelocity(parts.Rotor, up ? rpm : -rpm);
		}
		rig.AtMinSide = toMin;
	}

	private static void SetDrills(RigParts parts, bool on)
	{
		foreach (IMyShipDrill drill in parts.Drills)
		{
			if (drill.Enabled != on)
			{
				drill.Enabled = on;
			}
		}
	}

	/// <summary>Pause: rotor, pistons and drills off, exactly where they are.</summary>
	private static void HoldStill(Rig rig, RigParts parts)
	{
		foreach (IMyTerminalBlock block in parts.Moving)
		{
			if (block is IMyFunctionalBlock functional && functional.Enabled)
			{
				functional.Enabled = false;
			}
		}
		SetDrills(parts, false);
		if (rig.TensorRefresh)
		{
			RigBlocks.SetTensor(parts.Rotor, true);
		}
		rig.Live.Issued = false;
		ClearExpected(parts);
	}

	// ---------------------------------------------------------------- watching a step

	private static bool StepDone(Rig rig, RigParts parts, Pattern pattern, double now)
	{
		RigRuntime live = rig.Live;
		int step = rig.Step;
		double movement;
		bool done;
		if (Pattern.IsSweep(step))
		{
			double angle = AngleDeg(parts.Rotor);
			float target = rig.AtMinSide ? rig.MaxAngle : rig.MinAngle;
			double distance = Circular(angle, target);
			if (distance > (rig.MaxAngle - rig.MinAngle) / 4.0)
			{
				rig.SweepLeftStart = true;
			}
			done = rig.SweepLeftStart && distance <= AngleTolerance;
			movement = angle;
		}
		else
		{
			bool both = step > 0 && BothChange(pattern, step);
			int position = pattern.PositionOf(step);
			bool horizontalThere = PistonsAt(parts.Horizontal, parts, pattern.HorizontalAt(position), pattern.HorizontalRange);
			bool verticalThere = PistonsAt(parts.Vertical, parts, pattern.VerticalAt(position), pattern.VerticalRange);
			if (both && !rig.MoveHorizontalDone)
			{
				if (horizontalThere)
				{
					// Second half of the move: now the vertical pistons.
					rig.MoveHorizontalDone = true;
					RigStore.Changed();
					live.Issued = false;
				}
				done = false;
			}
			else
			{
				done = horizontalThere && verticalThere;
			}
			if (done && step == 0)
			{
				double angle = AngleDeg(parts.Rotor);
				done = Circular(angle, rig.AtMinSide ? rig.MinAngle : rig.MaxAngle) <= AngleTolerance;
				if (done)
				{
					SetRotorLimits(parts.Rotor, rig.MinAngle, rig.MaxAngle);
				}
			}
			movement = parts.Horizontal.Concat(parts.Vertical).Sum(p => p.CurrentPosition) + AngleDeg(parts.Rotor);
		}
		if (done)
		{
			return true;
		}
		if (double.IsNaN(live.LastValue) || Math.Abs(movement - live.LastValue) > 0.02)
		{
			live.LastValue = movement;
			live.LastMovedAt = now;
		}
		else if (!live.StuckWarned && now - live.LastMovedAt > StuckSeconds)
		{
			live.StuckWarned = true;
			Alert(rig, "Stuck", $"{rig.Name} hasn't moved for {StuckSeconds:0} s at step {step}. Something may be blocking it (or a block is off).", true);
		}
		return false;
	}

	private static bool PistonsAt(List<IMyPistonBase> pistons, RigParts parts, double total, double groupRange)
	{
		foreach (IMyPistonBase piston in pistons)
		{
			double goal = Pattern.PistonGoal(piston.LowestPosition, piston.HighestPosition, total, groupRange, parts.Inverted.Contains(piston.EntityId));
			if (Math.Abs(piston.CurrentPosition - goal) > PositionTolerance)
			{
				return false;
			}
		}
		return true;
	}

	private static void Advance(Rig rig, RigParts parts, Pattern pattern)
	{
		if (Pattern.IsSweep(rig.Step))
		{
			rig.AtMinSide = !rig.AtMinSide;
		}
		if (rig.Step >= pattern.LastStep)
		{
			rig.Command = RigCommand.Finished;
			SetDrills(parts, false);
			if (rig.TensorRefresh)
			{
				RigBlocks.SetTensor(parts.Rotor, true);
			}
			StartTimer(rig.FinishedTimerId);
			Show(rig, "Finished", "Done: everything in reach is mined.");
			Alert(rig, "Finished", $"{rig.Name} is finished: everything in reach is mined and the arm is back in.", false);
			Personality.Say("rig_finished", "rig", rig.Name);
		}
		else
		{
			rig.Step++;
		}
		rig.SweepLeftStart = false;
		rig.MoveHorizontalDone = false;
		rig.Live.Issued = false;
		RigStore.Changed();
	}

	// ---------------------------------------------------------------- someone else at the controls

	private static void Expect(long id, float a, float b, float velocity)
	{
		s_expected[id] = new Vector3(a, b, velocity);
		s_mismatchSince.Remove(id);
	}

	private static void ClearExpected(RigParts parts)
	{
		foreach (IMyTerminalBlock block in parts.Moving)
		{
			s_expected.Remove(block.EntityId);
			s_mismatchSince.Remove(block.EntityId);
		}
	}

	/// <summary>Null, or what reads different from what we sent for a few seconds: another player, a timer or a script changed it, or the server refused it.</summary>
	private static string Interference(Rig rig, RigParts parts, double now)
	{
		foreach (IMyTerminalBlock block in parts.Moving)
		{
			if (!s_expected.TryGetValue(block.EntityId, out Vector3 expected))
			{
				continue;
			}
			Vector3 actual;
			if (block is IMyMotorStator rotor)
			{
				actual = new Vector3(rotor.LowerLimitDeg, rotor.UpperLimitDeg, rotor.TargetVelocityRPM);
			}
			else if (block is IMyPistonBase piston)
			{
				actual = new Vector3(piston.MinLimit, piston.MaxLimit, piston.Velocity);
			}
			else
			{
				continue;
			}
			bool same = Math.Abs(actual.X - expected.X) < 0.1f && Math.Abs(actual.Y - expected.Y) < 0.1f && Math.Abs(actual.Z - expected.Z) < 0.02f + Math.Abs(expected.Z) * 0.05f;
			if (same)
			{
				s_mismatchSince.Remove(block.EntityId);
			}
			else if (!s_mismatchSince.TryGetValue(block.EntityId, out double since))
			{
				s_mismatchSince[block.EntityId] = now;
			}
			else if (now - since >= InterferenceSeconds)
			{
				s_mismatchSince.Remove(block.EntityId);
				return $"{block.CustomName} was changed by something else (another player, a timer or a script), or the server refused the change.";
			}
		}
		return null;
	}

	// ---------------------------------------------------------------- extras

	/// <summary>Turns on tensor sharing for the rotor and pistons, like ticking the box in the terminal. A note when the box isn't offered.</summary>
	private static string ShareTensor(RigParts parts)
	{
		bool offered = true;
		foreach (IMyTerminalBlock block in parts.Moving)
		{
			offered &= RigBlocks.SetTensor(block, true);
		}
		return offered ? null : "Share inertia tensor isn't offered here (the world must allow it and the game needs Experimental mode), so the rig runs without it.";
	}

	/// <summary>The script's "dynamic rotor tensor", when ticked: while sweeping, the rotor's sharing is off and comes on for 2 s once a minute.</summary>
	private static void TensorRefresh(Rig rig, RigParts parts, double now)
	{
		if (!rig.TensorRefresh)
		{
			return;
		}
		RigRuntime live = rig.Live;
		bool want = now - live.TensorAt >= 60 ? true : now - live.TensorAt < 2;
		if (want && now - live.TensorAt >= 60)
		{
			live.TensorAt = now;
		}
		if (want != live.TensorOn)
		{
			live.TensorOn = want;
			RigBlocks.SetTensor(parts.Rotor, want);
		}
	}

	private static void StartTimer(long id)
	{
		IMyTimerBlock timer = RigBlocks.Get<IMyTimerBlock>(id);
		if (timer != null && timer.HasLocalPlayerAccess())
		{
			// The terminal's own Start button.
			timer.GetActionWithName("Start")?.Apply(timer);
		}
	}

	/// <summary>One alert per state change, the way the rig asked for it.</summary>
	public static void Alert(Rig rig, string state, string text, bool warning)
	{
		if (rig.Live.LastAlertState == state)
		{
			return;
		}
		rig.Live.LastAlertState = state;
		Alerts.Send(rig, text, warning);
	}

	// ---------------------------------------------------------------- angles

	public static double AngleDeg(IMyMotorStator rotor) => MathHelper.ToDegrees(rotor.Angle);

	private static double Mod360(double degrees)
	{
		double result = degrees % 360.0;
		return result < 0 ? result + 360.0 : result;
	}

	/// <summary>Distance between two angles the short way round.</summary>
	private static double Circular(double a, double b)
	{
		double difference = Mod360(a - b);
		return Math.Min(difference, 360.0 - difference);
	}
}
