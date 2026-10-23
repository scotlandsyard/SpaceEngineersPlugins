using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digby;

/// <summary>
/// Plans a tunnel from one point to a spot a given depth under the surface somewhere else on the same planet (or,
/// in space, straight to a point). Planet centre, radii and the real terrain height come from the game. Pure local
/// maths; markers are local unless asked.
/// </summary>
public static class DigPlanner
{
	/// <summary>Points are "here", a GPS string, or a name in the GPS list or Digby's library.</summary>
	public class Plan
	{
		public string From = "here";

		public string Target = "";

		public double Depth;

		/// <summary>none, local or server.</summary>
		public string Marker = "local";

		/// <summary>Also mark the starting end (same local/server choice as the end marker).</summary>
		public bool StartMarker = true;

		/// <summary>Optional rig line on the player's grid: P1 -> P2 is the grid's forward, RigUp a point straight above P1 on the grid.</summary>
		public string RigA = "";

		public string RigB = "";

		public string RigUp = "";

		public bool SmallGrid;
	}

	public const string MarkerKind = "Dig";

	public static readonly Color StartColor = new Color(255, 230, 120);

	public static readonly Color TargetColor = new Color(255, 160, 0);

	public static readonly Color TurnColor = new Color(255, 200, 60);

	private static readonly Regex GpsPattern = new Regex(@"GPS:([^:]*):([-+]?[\d.]+(?:[eE][-+]?\d+)?):([-+]?[\d.]+(?:[eE][-+]?\d+)?):([-+]?[\d.]+(?:[eE][-+]?\d+)?):(?:#[0-9A-Fa-f]{6,8}:)?", RegexOptions.IgnoreCase);

	public static string ToGps(string name, Vector3D position)
	{
		string clean = (name ?? "Point").Replace(":", " ");
		return string.Format(CultureInfo.InvariantCulture, "GPS:{0}:{1:F2}:{2:F2}:{3:F2}:", clean, position.X, position.Y, position.Z);
	}

	public static bool TryParseGps(string text, out string name, out Vector3D position)
	{
		name = null;
		position = Vector3D.Zero;
		Match match = GpsPattern.Match(text ?? "");
		if (!match.Success)
		{
			return false;
		}
		name = match.Groups[1].Value;
		position = new Vector3D(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture), double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture));
		return true;
	}

	/// <summary>Resolves "here", a GPS string, or a name in the GPS list or Digby's library (exact first, then starting with).</summary>
	public static bool TryResolve(string spec, out string name, out Vector3D position, out string error)
	{
		error = null;
		name = null;
		position = Vector3D.Zero;
		spec = (spec ?? "").Trim();
		if (spec.Equals("here", StringComparison.OrdinalIgnoreCase) || spec.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			IMyPlayer player = MyAPIGateway.Session?.Player;
			if (player?.Character == null && player?.Controller?.ControlledEntity == null)
			{
				error = "you have no character to measure from";
				return false;
			}
			name = "here";
			position = player.GetPosition();
			return true;
		}
		if (TryParseGps(spec, out name, out position))
		{
			return true;
		}
		if (spec.Length == 0)
		{
			error = "no point given";
			return false;
		}
		List<IMyGps> gpsList = MyAPIGateway.Session?.Player == null ? new List<IMyGps>() : MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);
		IMyGps gps = gpsList.FirstOrDefault(g => string.Equals(g.Name, spec, StringComparison.OrdinalIgnoreCase)) ?? gpsList.FirstOrDefault(g => g.Name != null && g.Name.StartsWith(spec, StringComparison.OrdinalIgnoreCase));
		if (gps != null)
		{
			name = gps.Name;
			position = gps.Coords;
			return true;
		}
		LibraryEntry entry = Library.Match(spec);
		if (entry != null)
		{
			name = entry.Name;
			position = entry.Position;
			return true;
		}
		error = $"no GPS or library point called \"{spec}\"";
		return false;
	}

	/// <summary>GPS strings (which may contain spaces), "quoted names" and words.</summary>
	public static List<string> Tokenize(string args)
	{
		List<string> tokens = new List<string>();
		int i = 0;
		while (i < args.Length)
		{
			if (char.IsWhiteSpace(args[i]))
			{
				i++;
				continue;
			}
			Match gps = GpsPattern.Match(args, i);
			if (gps.Success && gps.Index == i)
			{
				tokens.Add(gps.Value);
				i += gps.Length;
			}
			else if (args[i] == '"')
			{
				int end = args.IndexOf('"', i + 1);
				end = end < 0 ? args.Length : end;
				tokens.Add(args.Substring(i + 1, end - i - 1));
				i = end + 1;
			}
			else
			{
				int end = i;
				while (end < args.Length && !char.IsWhiteSpace(args[end]))
				{
					end++;
				}
				tokens.Add(args.Substring(i, end - i));
				i = end;
			}
		}
		return tokens;
	}

	/// <summary>
	/// Splits "/digby dig" arguments. One point means From = here; a trailing number (optionally with m) is the depth.
	/// Marker words anywhere: local (the default), server, nomarker, nostart.
	/// </summary>
	public static bool TryParseArgs(string args, out Plan plan, out string error)
	{
		plan = new Plan();
		error = null;
		List<string> tokens = Tokenize(args);
		foreach (string word in tokens.ToList())
		{
			string lower = word.ToLowerInvariant();
			if (lower == "local" || lower == "server" || lower == "nomarker")
			{
				plan.Marker = lower == "nomarker" ? "none" : lower;
				tokens.Remove(word);
			}
			else if (lower == "nostart")
			{
				plan.StartMarker = false;
				tokens.Remove(word);
			}
		}
		if (tokens.Count >= 2 && double.TryParse(tokens[tokens.Count - 1].TrimEnd('m', 'M'), NumberStyles.Float, CultureInfo.InvariantCulture, out double depth))
		{
			plan.Depth = depth;
			tokens.RemoveAt(tokens.Count - 1);
		}
		if (tokens.Count == 1)
		{
			plan.Target = tokens[0];
		}
		else if (tokens.Count == 2)
		{
			plan.From = tokens[0];
			plan.Target = tokens[1];
		}
		else
		{
			error = "use /digby dig [from] <target> [depth]: from is here (the default), a GPS or a \"name\"; target is a GPS or a \"name\"; depth in metres below the target's surface";
			return false;
		}
		return true;
	}

	public class Result
	{
		public List<string> Lines = new List<string>();

		/// <summary>"120 m under Gold" on a planet, "to Gold" in space.</summary>
		public string PlanName = "";

		/// <summary>The target's name as resolved, for short GPS names.</summary>
		public string TargetName = "";

		/// <summary>Null in space.</summary>
		public MyPlanet Planet;

		/// <summary>"planet Agaris (EarthLike)" or "space".</summary>
		public string Where = "";

		public Vector3D TargetPoint;

		public Vector3D StartPoint;

		public double Length;

		/// <summary>Where a route turns: marker name prefix and position, in route order.</summary>
		public List<KeyValuePair<string, Vector3D>> Turns = new List<KeyValuePair<string, Vector3D>>();

		public bool Ok;

		/// <summary>Start, target and turn markers, whatever the plan's marker settings.</summary>
		public List<Marker> AllMarkers()
		{
			List<Marker> markers = new List<Marker>
			{
				new Marker { Key = "start", Name = "Dig start: " + PlanName, Position = StartPoint, Color = StartColor },
				new Marker { Key = "target", Name = "Dig target: " + PlanName, Position = TargetPoint, Color = TargetColor }
			};
			for (int i = 0; i < Turns.Count; i++)
			{
				markers.Add(new Marker { Key = "turn" + i, Name = Turns[i].Key + ": " + PlanName, Position = Turns[i].Value, Color = TurnColor });
			}
			return markers;
		}
	}

	/// <summary>Game thread (planet surface queries).</summary>
	public static Result Calculate(Plan plan)
	{
		Result result = new Result();
		if (!TryResolve(plan.From, out string fromName, out Vector3D from, out string error))
		{
			result.Lines.Add("From: " + error + ".");
			return result;
		}
		if (!TryResolve(plan.Target, out string targetName, out Vector3D target, out error))
		{
			result.Lines.Add("Target: " + error + ".");
			return result;
		}
		result.TargetName = targetName;
		MyPlanet planet = Planets.At(from);
		MyPlanet targetPlanet = Planets.At(target);
		if (planet == null && targetPlanet == null)
		{
			CalculateInSpace(plan, result, fromName, from, targetName, target);
			return result;
		}
		if (planet == null || targetPlanet == null)
		{
			result.Lines.Add((planet == null ? "From is in space but the target is on " + Planets.Describe(targetPlanet) : "From is on " + Planets.Describe(planet) + " but the target is in space") + "; both ends must be on the same planet, or both in space.");
			return result;
		}
		if (planet != targetPlanet)
		{
			result.Lines.Add($"The points are on different planets ({Planets.Describe(planet)} and {Planets.Describe(targetPlanet)}).");
			return result;
		}
		result.Planet = planet;
		result.Where = "planet " + Planets.Describe(planet);
		result.PlanName = $"{plan.Depth:N0} m under {targetName}";
		Vector3D centre = planet.PositionComp.GetPosition();
		double average = planet.AverageRadius;

		// How deep From is now: its distance below the terrain right above it.
		Vector3D fromSurface = planet.GetClosestSurfacePointGlobal(ref from);
		double rFrom = (from - centre).Length();
		double fromDepth = (fromSurface - centre).Length() - rFrom;
		Vector3D upFrom = Vector3D.Normalize(from - centre);

		// The target: the real terrain at that spot, then straight down by the depth.
		Vector3D targetSurface = planet.GetClosestSurfacePointGlobal(ref target);
		Vector3D upTarget = Vector3D.Normalize(targetSurface - centre);
		Vector3D t = targetSurface - upTarget * plan.Depth;
		double rT = (t - centre).Length();
		result.TargetPoint = t;
		result.StartPoint = from;
		Vector3D shaftPoint = centre + upFrom * rT;

		Vector3D d = t - from;
		double length = d.Length();
		result.Length = length;
		double drop = -Vector3D.Dot(d, upFrom);
		double horizontal = (d + upFrom * drop).Length();
		double pitch = MathHelper.ToDegrees(Math.Atan2(drop, horizontal));
		double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(upFrom, upTarget), -1.0, 1.0));
		double ground = angle * (targetSurface - centre).Length();
		double down = rFrom - rT;
		double across = angle * rT;

		result.Lines.Add($"On {result.Where}. From ({fromName}): " + (fromDepth > 1 ? $"{fromDepth:N0} m below the surface." : fromDepth < -1 ? $"{-fromDepth:N0} m above the surface." : "at the surface."));
		result.Lines.Add($"Target ({targetName}) {plan.Depth:N0} m under its surface: sea-level altitude {rT - average:N0} m. Over the ground: {Markers.FormatDistance(ground)}.");
		result.Lines.Add($"Straight tunnel: {Markers.FormatDistance(length)}, " + (pitch >= 0 ? $"{pitch:F1}° below" : $"{-pitch:F1}° above") + $" the horizon ({(drop >= 0 ? "drop" : "climb")} {Math.Abs(drop):N0} m over {Markers.FormatDistance(horizontal)}). Aim at the Dig target marker; it sits on the end point.");
		result.Lines.Add($"Down then across: {(down >= 0 ? "dig down" : "climb")} {Math.Abs(down):N0} m to sea-level altitude {rT - average:N0} m (cockpit altitude, sea level), then {Markers.FormatDistance(across)} across at that altitude.");
		if (down > 5 && across > 5)
		{
			result.Turns.Add(new KeyValuePair<string, Vector3D>("Dig turn", shaftPoint));
			result.Lines.Add($"Turn point (Dig turn marker): {down:N0} m straight down from the start; turn there and go across.");
		}

		// Straight lines inside a sphere sag toward the centre; say so when the middle is noticeably deeper than the ends.
		double s = MathHelper.Clamp(Vector3D.Dot(centre - from, d) / Math.Max(length * length, 1e-9), 0.0, 1.0);
		double rMin = (from + d * s - centre).Length();
		double sag = Math.Min(rFrom, rT) - rMin;
		if (sag > 5)
		{
			result.Lines.Add($"Note: the straight tunnel runs up to {sag:N0} m deeper than its lower end in the middle (curvature).");
		}
		if (rT < planet.MinimumRadius)
		{
			result.Lines.Add($"Note: the end point is {planet.MinimumRadius - rT:N0} m below the planet's lowest terrain.");
		}
		if (length > 20000)
		{
			result.Lines.Add("Note: that is a very long tunnel; check the target GPS is the one you meant.");
		}
		result.Ok = true;
		if (!string.IsNullOrWhiteSpace(plan.RigA) && !string.IsNullOrWhiteSpace(plan.RigB))
		{
			AddRigLines(plan, result, d, upFrom);
		}
		return result;
	}

	/// <summary>In space there's no surface or horizon: the tunnel goes straight to the target point, and the depth is ignored.</summary>
	private static void CalculateInSpace(Plan plan, Result result, string fromName, Vector3D from, string targetName, Vector3D target)
	{
		result.Where = "space";
		result.PlanName = "to " + targetName;
		result.StartPoint = from;
		result.TargetPoint = target;
		Vector3D d = target - from;
		result.Length = d.Length();
		result.Lines.Add($"In space. From ({fromName}) straight to the target ({targetName}): {Markers.FormatDistance(result.Length)}. Aim at the Dig target marker.");
		if (Math.Abs(plan.Depth) > 0.5)
		{
			result.Lines.Add($"The depth ({plan.Depth:N0} m) is ignored in space: there's no surface to measure it from, so the end point is the target itself. Put the target GPS on the deposit.");
		}
		if (result.Length < 0.5)
		{
			result.Lines.Add("From and the target are the same point.");
			return;
		}
		result.Ok = true;
		if (!string.IsNullOrWhiteSpace(plan.RigA) && !string.IsNullOrWhiteSpace(plan.RigB))
		{
			AddRigLines(plan, result, d, null);
		}
	}

	/// <summary>
	/// The tunnel relative to the player's grid: P1->P2 is the grid's forward axis, the optional third point (straight
	/// above P1 on the grid) its up axis; without it the grid is taken as level (up = planet up, made square to forward).
	/// Gives the angles both against the true horizon and against the grid itself, and the staircase in blocks, since
	/// a grid extended without hinges or rotors can only step along its own axes. In space (no planet up) the third
	/// point is required and there's no horizon.
	/// </summary>
	private static void AddRigLines(Plan plan, Result result, Vector3D d, Vector3D? planetUp)
	{
		if (!TryResolve(plan.RigA, out _, out Vector3D a, out string error) || !TryResolve(plan.RigB, out _, out Vector3D b, out error))
		{
			result.Lines.Add("Rig line: " + error + ".");
			return;
		}
		if ((b - a).LengthSquared() < 0.01)
		{
			result.Lines.Add("Rig line: P1 and P2 are the same point.");
			return;
		}
		Vector3D forward = Vector3D.Normalize(b - a);
		bool levelAssumed = true;
		Vector3D gridUp = planetUp ?? Vector3D.Zero;
		if (!string.IsNullOrWhiteSpace(plan.RigUp) && TryResolve(plan.RigUp, out _, out Vector3D c, out _))
		{
			gridUp = c - a;
			levelAssumed = false;
		}
		else if (planetUp == null)
		{
			result.Lines.Add("Rig line: in space there's no up to go by, so it needs the third point (a GPS straight above P1 on the grid).");
			return;
		}
		gridUp -= forward * Vector3D.Dot(gridUp, forward);
		if (gridUp.LengthSquared() < 1e-6)
		{
			result.Lines.Add("Rig line: P1->P2 points straight up or down, so it needs the third point (a GPS straight above P1 on the grid).");
			return;
		}
		gridUp = Vector3D.Normalize(gridUp);
		// Space Engineers' convention: Right = Forward x Up.
		Vector3D right = Vector3D.Cross(forward, gridUp);

		if (planetUp.HasValue)
		{
			// Against the true horizon: the rig's heading flattened onto the horizon.
			Vector3D up = planetUp.Value;
			Vector3D flatForward = forward - up * Vector3D.Dot(forward, up);
			Vector3D flatTunnel = d - up * Vector3D.Dot(d, up);
			string horizon;
			if (flatForward.LengthSquared() < 1e-6 || flatTunnel.LengthSquared() < 1e-6)
			{
				horizon = "the rig line or the tunnel is vertical, so there is no left or right against the horizon";
			}
			else
			{
				flatForward = Vector3D.Normalize(flatForward);
				Vector3D flatRight = Vector3D.Cross(flatForward, up);
				double turn = MathHelper.ToDegrees(Math.Atan2(Vector3D.Dot(flatTunnel, flatRight), Vector3D.Dot(flatTunnel, flatForward)));
				double pitch = MathHelper.ToDegrees(Math.Atan2(-Vector3D.Dot(d, up), flatTunnel.Length()));
				double rigTilt = MathHelper.ToDegrees(Math.Asin(MathHelper.Clamp(Vector3D.Dot(forward, up), -1.0, 1.0)));
				horizon = $"turn {Side(turn)}, pitch {Math.Abs(pitch):F1}° {(pitch >= 0 ? "below" : "above")} the true horizon (P1->P2 itself points {Math.Abs(rigTilt):F1}° {(rigTilt >= 0 ? "up" : "down")})";
			}
			result.Lines.Add($"Rig line, against the horizon: {horizon}.");
		}

		// Against the grid's own axes.
		double f = Vector3D.Dot(d, forward);
		double r = Vector3D.Dot(d, right);
		double u = Vector3D.Dot(d, gridUp);
		double yaw = MathHelper.ToDegrees(Math.Atan2(r, f));
		double gridPitch = MathHelper.ToDegrees(Math.Atan2(-u, Math.Sqrt(f * f + r * r)));
		result.Lines.Add($"Rig line, against the grid{(levelAssumed ? " (taken as level; add a third point straight above P1 if it isn't)" : "")}: turn {Side(yaw)}, pitch {Math.Abs(gridPitch):F1}° {(gridPitch >= 0 ? "down" : "up")} from the grid's plane, {Markers.FormatDistance(d.Length())} long.");

		// The staircase: whole blocks along each grid axis, and the step pattern per 10 blocks of the main axis.
		double size = plan.SmallGrid ? 0.5 : 2.5;
		double nf = f / size;
		double nr = r / size;
		double nd = -u / size;
		double main = Math.Max(Math.Abs(nf), Math.Max(Math.Abs(nr), Math.Abs(nd)));
		string Axis(double n, string plus, string minus) => $"{Math.Abs(n):N0} {(n >= 0 ? plus : minus)}";
		string Per(double n) => main < 1e-9 ? "0" : (Math.Abs(n) * 10.0 / main).ToString("0.#", CultureInfo.InvariantCulture);
		result.Lines.Add($"In {(plan.SmallGrid ? "small" : "large")} blocks along the grid: {Axis(nf, "forward", "back")}, {Axis(nr, "right", "left")}, {Axis(nd, "down", "up")}. Per 10 blocks of the longest: {Per(nf)} forward, {Per(nr)} {(nr >= 0 ? "right" : "left")}, {Per(nd)} {(nd >= 0 ? "down" : "up")}.");

		// The same in straight legs along the grid (forward, then sideways, then down), with a marker where each leg turns.
		List<KeyValuePair<string, Vector3D>> legs = new List<KeyValuePair<string, Vector3D>>();
		if (Math.Abs(nf) >= 1)
		{
			legs.Add(new KeyValuePair<string, Vector3D>(Axis(nf, "forward", "back"), forward * f));
		}
		if (Math.Abs(nr) >= 1)
		{
			legs.Add(new KeyValuePair<string, Vector3D>(Axis(nr, "right", "left"), right * r));
		}
		if (Math.Abs(nd) >= 1)
		{
			legs.Add(new KeyValuePair<string, Vector3D>(Axis(nd, "down", "up"), gridUp * u));
		}
		if (legs.Count > 1)
		{
			Vector3D point = result.StartPoint;
			double walked = 0;
			List<string> steps = new List<string>();
			for (int i = 0; i < legs.Count; i++)
			{
				point += legs[i].Value;
				walked += legs[i].Value.Length();
				steps.Add(legs[i].Key);
				if (i < legs.Count - 1)
				{
					result.Turns.Add(new KeyValuePair<string, Vector3D>($"Dig rig turn {i + 1}", point));
					steps.Add($"turn {i + 1} at {Markers.FormatDistance(walked)} from the start (Dig rig turn {i + 1} marker)");
				}
			}
			result.Lines.Add("In straight legs along the grid: " + string.Join(", then ", steps) + ".");
		}
	}

	private static string Side(double degrees)
	{
		return Math.Abs(degrees) < 0.05 ? "0°" : $"{Math.Abs(degrees):F1}° {(degrees > 0 ? "right" : "left")}";
	}

	/// <summary>Replaces the last plan's markers with this one's: local (never sent to the server) or server GPS. Returns a chat line.</summary>
	public static string MakeMarkers(Plan plan, Result result)
	{
		if (!result.Ok)
		{
			return null;
		}
		ClearMarkers();
		if (plan.Marker == "none")
		{
			return null;
		}
		List<Marker> markers = result.AllMarkers().Where(m => m.Key != "start" || plan.StartMarker).ToList();
		string which = markers.Count == 1 ? "marker at the end point" : $"{markers.Count} dig markers (" + (plan.StartMarker ? "start, " : "") + (result.Turns.Count > 0 ? "turns and " : "") + "end point)";
		if (plan.Marker == "server")
		{
			Markers.ReplaceServer(MarkerKind, markers, "Digby dig plan");
			return $"Server GPS {which} made.";
		}
		Markers.ReplaceLocal(MarkerKind, markers, "Digby dig plan");
		return $"Local {which} made (gone when you leave the world; Save to library keeps them).";
	}

	/// <summary>Removes the plan markers, local and server. Returns how many went.</summary>
	public static int ClearMarkers()
	{
		return Markers.RemoveLocal(MarkerKind) + Markers.RemoveServer(MarkerKind);
	}

	/// <summary>Stores named points as GPS strings, so the saved plan still works if the GPS or library entry is renamed or deleted.</summary>
	public static Plan Pinned(Plan plan)
	{
		Plan copy = new Plan { From = plan.From, Target = plan.Target, Depth = plan.Depth, Marker = plan.Marker, StartMarker = plan.StartMarker, RigA = Pin(plan.RigA), RigB = Pin(plan.RigB), RigUp = Pin(plan.RigUp), SmallGrid = plan.SmallGrid };
		if (!copy.From.Equals("here", StringComparison.OrdinalIgnoreCase) && TryResolve(copy.From, out string fromName, out Vector3D from, out _))
		{
			copy.From = ToGps(fromName, from);
		}
		if (TryResolve(copy.Target, out string targetName, out Vector3D target, out _))
		{
			copy.Target = ToGps(targetName, target);
		}
		return copy;
	}

	private static string Pin(string spec)
	{
		return !string.IsNullOrWhiteSpace(spec) && TryResolve(spec, out string name, out Vector3D position, out _) ? ToGps(name, position) : spec ?? "";
	}
}
