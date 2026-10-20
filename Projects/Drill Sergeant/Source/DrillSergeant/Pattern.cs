using System;

namespace DrillSergeant;

/// <summary>
/// The mining pattern as plain numbers, with no game types, so it can be tested on its own.
///
/// The arm visits a grid of piston positions: rings (how far the horizontal pistons are out) inside layers (how far
/// the vertical pistons are down). At every position the rotor sweeps once from one angle limit to the other. Rings
/// go out on even layers and back in on odd ones, so the arm never travels empty, unless RetractFirst is set: then
/// every layer starts from the inside, and the arm pulls in before it goes down.
///
/// Steps are numbered like this, for n positions:
///   step 2k     move the pistons to position k (k = 0 also turns the rotor to its start limit)
///   step 2k + 1 sweep at position k
///   step 2n     done: pull everything back to the start and switch the drills off
/// The same numbering as the Automatic Mining Platform script uses, so a rig handed to the script can carry on at
/// the same place when both use the same step lengths.
/// </summary>
internal sealed class Pattern
{
	/// <summary>Total extension the horizontal pistons can add together, metres.</summary>
	public readonly double HorizontalRange;

	/// <summary>Total extension the vertical pistons can add together, metres.</summary>
	public readonly double VerticalRange;

	public readonly double HorizontalStep;

	public readonly double VerticalStep;

	/// <summary>Horizontal moves per layer (rings - 1).</summary>
	public readonly int Rings;

	/// <summary>Vertical moves (layers - 1).</summary>
	public readonly int Layers;

	public readonly bool RetractFirst;

	public Pattern(double horizontalRange, double verticalRange, double horizontalStep, double verticalStep, bool retractFirst)
	{
		HorizontalRange = Math.Max(0, horizontalRange);
		VerticalRange = Math.Max(0, verticalRange);
		HorizontalStep = horizontalStep > 0.05 ? horizontalStep : Math.Max(HorizontalRange, 0.05);
		VerticalStep = verticalStep > 0.05 ? verticalStep : Math.Max(VerticalRange, 0.05);
		Rings = StageCount(HorizontalRange, HorizontalStep);
		Layers = StageCount(VerticalRange, VerticalStep);
		RetractFirst = retractFirst;
	}

	private static int StageCount(double range, double step)
	{
		// 1e-4 keeps a range that is an exact multiple of the step from getting an extra, empty stage.
		return range < 0.01 ? 0 : (int)Math.Ceiling(range / step - 1e-4);
	}

	public int Positions => (Rings + 1) * (Layers + 1);

	/// <summary>The last step: retract and finish.</summary>
	public int LastStep => 2 * Positions;

	public static bool IsSweep(int step) => step % 2 == 1;

	/// <summary>The piston position a step works at. The final step goes back to the start.</summary>
	public int PositionOf(int step)
	{
		return step >= LastStep ? 0 : step / 2;
	}

	public int LayerOf(int position) => position / (Rings + 1);

	public int RingOf(int position)
	{
		int layer = LayerOf(position);
		int ring = position % (Rings + 1);
		return RetractFirst || layer % 2 == 0 ? ring : Rings - ring;
	}

	/// <summary>
	/// Total horizontal extension at a position, metres. The range is shared out evenly over the rings, so no ring is
	/// a thin leftover at the end; the steps come out at most the length asked for, and the ring count stays the
	/// script's.
	/// </summary>
	public double HorizontalAt(int position) => Rings == 0 ? 0 : HorizontalRange * RingOf(position) / Rings;

	/// <summary>Total vertical extension at a position, metres (the depth below the start), shared out evenly like the rings.</summary>
	public double VerticalAt(int position) => Layers == 0 ? 0 : VerticalRange * LayerOf(position) / Layers;

	/// <summary>One piston's goal when its group adds up to <paramref name="total"/> metres: every piston covers the same share of its own travel.</summary>
	public static double PistonGoal(double low, double high, double total, double groupRange, bool inverted)
	{
		double share = groupRange > 0.001 ? Math.Max(0, Math.Min(1, total / groupRange)) : 0;
		double travel = (high - low) * share;
		return inverted ? high - travel : low + travel;
	}

	/// <summary>Rotor speed in rpm that gives the drill tips at <paramref name="radius"/> metres the speed a tip has at 10 m and <paramref name="rpmAt10"/> rpm.</summary>
	public static double RotorRpm(double rpmAt10, double radius)
	{
		return rpmAt10 * 10.0 / Math.Max(radius, 1.0);
	}

	/// <summary>
	/// Seconds left from the start of <paramref name="step"/>: every sweep still to do at its own radius, every piston move,
	/// and a few seconds per step for noticing it's done. <paramref name="radiusRetracted"/> is the outer drill radius with
	/// the horizontal pistons all the way in.
	/// </summary>
	public double SecondsLeft(int step, double arcDegrees, double rpmAt10, double radiusRetracted, double horizontalSpeed, double verticalSpeed)
	{
		if (step >= LastStep)
		{
			return 0;
		}
		double seconds = 0;
		double turns = Math.Abs(arcDegrees) / 360.0;
		for (int s = Math.Max(step, 0); s <= LastStep; s++)
		{
			int position = PositionOf(s);
			if (IsSweep(s))
			{
				double rpm = RotorRpm(rpmAt10, radiusRetracted + HorizontalAt(position));
				seconds += rpm > 0 ? turns / rpm * 60.0 : 0;
			}
			else if (s > 0)
			{
				int from = PositionOf(s - 1);
				seconds += Math.Abs(HorizontalAt(position) - HorizontalAt(from)) / Math.Max(horizontalSpeed, 0.01);
				seconds += Math.Abs(VerticalAt(position) - VerticalAt(from)) / Math.Max(verticalSpeed, 0.01);
			}
			seconds += 2;
		}
		return seconds;
	}
}
