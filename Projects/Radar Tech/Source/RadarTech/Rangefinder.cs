using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace RadarTech;

internal enum TargetKind
{
	Nothing,
	Asteroid,
	Planet,
	Station,
	LargeShip,
	SmallShip,
	Character,
	Object
}

/// <summary>One measurement: what's under the crosshair and how far it is from you.</summary>
internal sealed class Reading
{
	public TargetKind Kind;

	/// <summary>Metres from you (the cockpit or your character) to the point under the crosshair.</summary>
	public double Distance;

	/// <summary>Metres per second, positive while you're getting closer.</summary>
	public double Closing;

	public double RangeMetres;

	public bool Hit => Kind != TargetKind.Nothing;
}

/// <summary>
/// Measures along the crosshair, the way a camera block's raycast does: the game's physics ray for grids, characters
/// and nearby voxels, then the voxel shapes themselves for asteroids and planets further out. Only the first thing
/// the line meets counts, so it never sees through rock or a ship, and only entities already loaded on this PC can
/// be hit. The voxel part runs on a background thread.
/// </summary>
internal static class Rangefinder
{
	private const int MaxPlanetSteps = 5000;

	private static readonly List<MyPhysics.HitInfo> s_hits = new List<MyPhysics.HitInfo>();

	private static readonly List<MyLineSegmentOverlapResult<MyVoxelBase>> s_overlaps = new List<MyLineSegmentOverlapResult<MyVoxelBase>>();

	private static readonly HashSet<long> s_ignore = new HashSet<long>();

	private static readonly List<IMyCubeGrid> s_group = new List<IMyCubeGrid>();

	public static bool Busy { get; private set; }

	/// <summary>Starts a measurement; done runs on the game thread. False if one is already running or there's no camera.</summary>
	public static bool Start(double rangeMetres, Action<Reading> done)
	{
		if (Busy || MyAPIGateway.Session?.Camera == null)
		{
			return false;
		}
		MatrixD view = MyAPIGateway.Session.Camera.WorldMatrix;
		Vector3D from = view.Translation;
		Vector3D dir = view.Forward;
		IMyEntity self = MyAPIGateway.Session.IsCameraUserControlledSpectator ? null : MyAPIGateway.Session.ControlledObject?.Entity;
		IMyEntity selfTop = self?.GetTopMostParent();
		Vector3D origin = self?.GetPosition() ?? from;
		// In third person the camera sits behind you: reach far enough that the range still counts from you.
		double length = rangeMetres + Vector3D.Distance(from, origin);
		Vector3D to = from + dir * length;

		s_ignore.Clear();
		if (selfTop is IMyCubeGrid grid)
		{
			s_group.Clear();
			MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Physical, s_group);
			foreach (IMyCubeGrid g in s_group)
			{
				s_ignore.Add(g.EntityId);
			}
			s_ignore.Add(grid.EntityId);
		}
		else if (selfTop != null)
		{
			s_ignore.Add(selfTop.EntityId);
		}
		IMyCharacter character = MyAPIGateway.Session.Player?.Character;
		if (character != null)
		{
			s_ignore.Add(character.EntityId);
		}

		// Physics first, on the game thread: grids, characters, loose objects, and voxels close enough to have physics.
		double best = length;
		IMyEntity bestEntity = null;
		s_hits.Clear();
		MyPhysics.CastRay(from, to, s_hits);
		foreach (MyPhysics.HitInfo hit in s_hits)
		{
			IMyEntity top = hit.HkHitInfo.GetHitEntity()?.GetTopMostParent();
			if (top == null || s_ignore.Contains(top.EntityId))
			{
				continue;
			}
			double d = Vector3D.Distance(from, hit.Position);
			if (d < best)
			{
				best = d;
				bestEntity = top;
			}
		}

		// Then the voxel shapes, which reach asteroids and planets beyond physics range.
		LineD ray = new LineD(from, from + dir * best);
		s_overlaps.Clear();
		MyGamePruningStructure.GetVoxelMapsOverlappingRay(ref ray, s_overlaps);
		List<MyVoxelBase> voxels = new List<MyVoxelBase>();
		foreach (MyLineSegmentOverlapResult<MyVoxelBase> overlap in s_overlaps)
		{
			MyVoxelBase root = overlap.Element?.RootVoxel;
			if (root != null && !root.MarkedForClose && root.Storage != null && !voxels.Contains(root))
			{
				voxels.Add(root);
			}
		}

		double physicsBest = best;
		MyVoxelBase voxelHit = null;
		Busy = true;
		MyAPIGateway.Parallel.StartBackground(() =>
		{
			foreach (MyVoxelBase voxel in voxels)
			{
				try
				{
					double? d = voxel is MyPlanet planet ? TracePlanet(planet, from, dir, best) : TraceVoxel(voxel, from, dir, best);
					if (d.HasValue && d.Value < best)
					{
						best = d.Value;
						voxelHit = voxel;
					}
				}
				catch (Exception ex)
				{
					MyLog.Default.WriteLineAndConsole($"[Radar Tech] Voxel trace: {ex.Message}");
				}
			}
		}, () =>
		{
			Busy = false;
			try
			{
				IMyEntity target = voxelHit != null && best < physicsBest ? voxelHit : bestEntity;
				Reading reading = new Reading { RangeMetres = rangeMetres };
				if (target != null)
				{
					Vector3D point = from + dir * best;
					reading.Distance = Vector3D.Distance(origin, point);
					reading.Kind = KindOf(target);
					if (reading.Distance > rangeMetres)
					{
						reading.Kind = TargetKind.Nothing;
					}
					else
					{
						Vector3D toward = Vector3D.Normalize(point - origin);
						Vector3D mine = selfTop?.Physics?.LinearVelocity ?? Vector3D.Zero;
						Vector3D theirs = target.Physics?.LinearVelocity ?? Vector3D.Zero;
						reading.Closing = Vector3D.Dot(mine - theirs, toward);
					}
				}
				done(reading);
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Radar Tech] Reading: {ex}");
			}
		});
		return true;
	}

	/// <summary>An asteroid (or any voxel map that isn't a planet): the exact voxel surface, within its box.</summary>
	private static double? TraceVoxel(MyVoxelBase voxel, Vector3D from, Vector3D dir, double maxLength)
	{
		BoundingBoxD box = voxel.PositionComp.WorldAABB;
		double? enter = box.Contains(from) == ContainmentType.Contains ? 0 : box.Intersects(new RayD(from, dir));
		if (!enter.HasValue || enter.Value > maxLength)
		{
			return null;
		}
		// Only the part of the line inside the box, so the cell walk stays short.
		double leave = Math.Min(maxLength, enter.Value + box.Size.Length());
		LineD inside = new LineD(from + dir * enter.Value, from + dir * leave);
		if (voxel.GetIntersectionWithLine(ref inside, out MyIntersectionResultLineTriangleEx? hit, IntersectionFlags.DIRECT_TRIANGLES) && hit.HasValue)
		{
			return Vector3D.Distance(from, hit.Value.IntersectionPointInWorldSpace);
		}
		return null;
	}

	/// <summary>
	/// A planet: steps along the line by half the height above the ground until it's at or below the surface, then
	/// finds the exact voxel surface on that short stretch. Walking a planet's whole shape cell by cell would take far
	/// too long over kilometres.
	/// </summary>
	private static double? TracePlanet(MyPlanet planet, Vector3D from, Vector3D dir, double maxLength)
	{
		Vector3D centre = planet.PositionComp.GetPosition();
		double radius = planet.MaximumRadius;
		double t;
		if (Vector3D.DistanceSquared(from, centre) <= radius * radius)
		{
			t = 0;
		}
		else
		{
			double? enter = new BoundingSphereD(centre, radius).Intersects(new RayD(from, dir));
			if (!enter.HasValue || enter.Value > maxLength)
			{
				return null;
			}
			t = enter.Value;
		}
		double last = t;
		for (int step = 0; step < MaxPlanetSteps && t <= maxLength; step++)
		{
			Vector3D point = from + dir * t;
			Vector3D up = point - centre;
			double height = up.Length();
			if (height > radius + 1 && Vector3D.Dot(up, dir) > 0)
			{
				return null;
			}
			Vector3D surface = planet.GetClosestSurfacePointGlobal(ref point);
			double altitude = height - (surface - centre).Length();
			if (altitude <= 0.5)
			{
				double a = Math.Max(0, last - 1);
				double b = Math.Min(maxLength, t + 30);
				LineD stretch = new LineD(from + dir * a, from + dir * b);
				if (planet.GetIntersectionWithLine(ref stretch, out MyIntersectionResultLineTriangleEx? hit, IntersectionFlags.DIRECT_TRIANGLES) && hit.HasValue)
				{
					return Vector3D.Distance(from, hit.Value.IntersectionPointInWorldSpace);
				}
				return t;
			}
			last = t;
			t += Math.Max(altitude * 0.5, 1.0);
		}
		return null;
	}

	private static TargetKind KindOf(IMyEntity entity)
	{
		switch (entity)
		{
		case MyVoxelBase voxel:
			return voxel.RootVoxel is MyPlanet ? TargetKind.Planet : TargetKind.Asteroid;
		case IMyCubeGrid grid:
			return grid.IsStatic ? TargetKind.Station : grid.GridSizeEnum == MyCubeSize.Large ? TargetKind.LargeShip : TargetKind.SmallShip;
		case IMyCharacter _:
			return TargetKind.Character;
		default:
			return TargetKind.Object;
		}
	}

	public static string KindName(TargetKind kind)
	{
		switch (kind)
		{
		case TargetKind.Asteroid:
			return "Asteroid";
		case TargetKind.Planet:
			return "Planet surface";
		case TargetKind.Station:
			return "Station";
		case TargetKind.LargeShip:
			return "Large ship";
		case TargetKind.SmallShip:
			return "Small ship";
		case TargetKind.Character:
			return "Character";
		case TargetKind.Object:
			return "Object";
		default:
			return "Nothing";
		}
	}

	public static string FormatDistance(double metres)
	{
		if (metres < 1000)
		{
			return metres.ToString("0", CultureInfo.InvariantCulture) + " m";
		}
		return (metres / 1000).ToString(metres < 100000 ? "0.00" : "0", CultureInfo.InvariantCulture) + " km";
	}

	public static string FormatTime(double seconds)
	{
		if (seconds >= 3600)
		{
			return $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60):00}m";
		}
		return $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
	}

	/// <summary>"Asteroid 4.21 km, closing 85 m/s, 0:49 to contact", or "Nothing within 20 km".</summary>
	public static string Describe(Reading reading, bool closing)
	{
		if (!reading.Hit)
		{
			return $"Nothing within {FormatDistance(reading.RangeMetres)}";
		}
		string text = $"{KindName(reading.Kind)} {FormatDistance(reading.Distance)}";
		if (!closing)
		{
			return text;
		}
		double speed = reading.Closing;
		if (speed > 0.5)
		{
			return text + $"  |  closing {speed:0} m/s  |  {FormatTime(reading.Distance / speed)} to contact";
		}
		if (speed < -0.5)
		{
			return text + $"  |  opening {-speed:0} m/s";
		}
		return text + "  |  holding";
	}
}
