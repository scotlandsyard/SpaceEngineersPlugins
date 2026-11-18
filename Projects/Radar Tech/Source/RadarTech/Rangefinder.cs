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
using MyDetectedEntityInfo = Sandbox.ModAPI.Ingame.MyDetectedEntityInfo;
using MyDetectedEntityType = Sandbox.ModAPI.Ingame.MyDetectedEntityType;

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

	/// <summary>How far this reading reached (the camera's charge can make it shorter than the range setting).</summary>
	public double RangeMetres;

	/// <summary>Why there's no measurement at all ("Needs a camera", "Charging..."); null for a real reading.</summary>
	public string Note;

	public bool Hit => Note == null && Kind != TargetKind.Nothing;
}

/// <summary>
/// Measures with a camera block's own raycast (IMyCameraBlock.Raycast), so the game's camera rules apply exactly as
/// for a programmable block script: the camera must be working and on, the line must be inside its cone, and every
/// reading spends the camera's charge (about 2 km of range per second). Only what the raycast reports is used: the
/// kind of thing hit, where, and how fast it moves; never names, owners or factions.
///
/// Which camera: the one you're looking through, or in a cockpit a camera on your ship whose cone covers the ship's
/// aim (the best-aligned one). It must be a block you have access to and within the server's reach rule. Where a
/// world has in-game scripts switched off, nothing measures: vanilla only lets scripts use a camera's raycast.
/// </summary>
internal static class Rangefinder
{
	/// <summary>A reading shorter than this isn't worth the charge; Radar waits for the camera instead.</summary>
	private const double MinScan = 50;

	private static readonly List<IMyCubeGrid> s_group = new List<IMyCubeGrid>();

	private static readonly List<IMySlimBlock> s_blocks = new List<IMySlimBlock>();

	/// <summary>Cameras Radar switched raycasting on for, so it can switch them back off.</summary>
	private static readonly HashSet<IMyCameraBlock> s_enabled = new HashSet<IMyCameraBlock>();

	/// <summary>
	/// Takes one measurement now and hands it to done. The readout waits until the camera has charge for the full
	/// range (false, done not called, so the last reading stays up); a ping uses whatever charge there is.
	/// </summary>
	public static bool Start(double rangeMetres, bool ping, Action<Reading> done)
	{
		Reading reading = Measure(rangeMetres, ping, out bool wait);
		if (wait && s_shownOnce && !ping)
		{
			return false;
		}
		s_shownOnce = true;
		done(reading);
		return true;
	}

	private static bool s_shownOnce;

	/// <summary>Switches raycasting back off on every camera Radar switched it on for (on unload).</summary>
	public static void Release()
	{
		foreach (IMyCameraBlock camera in s_enabled)
		{
			try
			{
				if (camera != null && !camera.Closed)
				{
					camera.EnableRaycast = false;
				}
			}
			catch
			{
			}
		}
		s_enabled.Clear();
		s_shownOnce = false;
	}

	private static Reading Measure(double rangeMetres, bool ping, out bool wait)
	{
		wait = false;
		Reading reading = new Reading { RangeMetres = rangeMetres };
		if (MyAPIGateway.Session?.SessionSettings != null && !MyAPIGateway.Session.SessionSettings.EnableIngameScripts)
		{
			reading.Note = "Off: this world has in-game scripts switched off";
			return reading;
		}
		IMyEntity self = MyAPIGateway.Session?.ControlledObject?.Entity;
		IMyCameraBlock camera = MyAPIGateway.Session?.CameraController as IMyCameraBlock;
		Vector3D dir;
		Vector3D origin;
		if (camera != null)
		{
			if (!Usable(camera))
			{
				reading.Note = "Needs a camera: this one is off, out of reach or not yours";
				return reading;
			}
			dir = camera.WorldMatrix.Forward;
			origin = camera.WorldMatrix.Translation;
		}
		else if (self is IMyShipController controller)
		{
			dir = controller.WorldMatrix.Forward;
			origin = controller.GetPosition();
			camera = CameraFor(controller, dir);
			if (camera == null)
			{
				reading.Note = "Needs a camera: none on this ship faces where it points";
				return reading;
			}
		}
		else
		{
			reading.Note = "Needs a camera: look through one, or sit in a ship that has one";
			return reading;
		}

		if (!camera.EnableRaycast)
		{
			camera.EnableRaycast = true;
			s_enabled.Add(camera);
		}
		double limit = camera.RaycastDistanceLimit;
		double want = limit > 0 ? Math.Min(rangeMetres, limit) : rangeMetres;
		double available = camera.AvailableScanRange;
		double scan = ping ? Math.Min(want, available) : want;
		if (available < scan || scan < MinScan)
		{
			int ms = camera.TimeUntilScan(want);
			reading.Note = $"Charging the camera ({Math.Max(1, (ms + 999) / 1000)} s for {FormatDistance(want)})";
			wait = true;
			return reading;
		}
		reading.RangeMetres = scan;
		Vector3D from = camera.WorldMatrix.Translation;
		MyDetectedEntityInfo info = camera.Raycast(from + dir * scan);
		if (info.IsEmpty() || !info.HitPosition.HasValue)
		{
			return reading;
		}
		Vector3D point = info.HitPosition.Value;
		IMyEntity hitEntity = MyAPIGateway.Entities.GetEntityById(info.EntityId);
		IMyEntity selfTop = self?.GetTopMostParent() ?? camera.CubeGrid;
		if (hitEntity is IMyCubeGrid hitGrid && SameShip(hitGrid, camera.CubeGrid))
		{
			reading.Note = "Your own ship is in front of the camera";
			return reading;
		}
		reading.Kind = KindOf(info, hitEntity);
		reading.Distance = Vector3D.Distance(origin, point);
		Vector3D toward = Vector3D.Normalize(point - origin);
		Vector3D mine = camera.CubeGrid?.Physics?.LinearVelocity ?? selfTop?.Physics?.LinearVelocity ?? Vector3D.Zero;
		reading.Closing = Vector3D.Dot(mine - (Vector3D)info.Velocity, toward);
		return reading;
	}

	/// <summary>A camera you could use for a script: working, on, yours or shared with you, and in reach.</summary>
	private static bool Usable(IMyCameraBlock camera)
	{
		return camera != null && !camera.Closed && camera.IsFunctional && camera.Enabled && camera.IsWorking && camera.HasLocalPlayerAccess() && Reach.InReach(camera.CubeGrid);
	}

	/// <summary>The camera on the ship (rotors and pistons included, not docked ships) whose cone covers the aim best.</summary>
	private static IMyCameraBlock CameraFor(IMyShipController controller, Vector3D dir)
	{
		s_group.Clear();
		MyAPIGateway.GridGroups.GetGroup(controller.CubeGrid, GridLinkTypeEnum.Mechanical, s_group);
		if (!s_group.Contains(controller.CubeGrid))
		{
			s_group.Add(controller.CubeGrid);
		}
		IMyCameraBlock best = null;
		double bestDot = -2;
		foreach (IMyCubeGrid grid in s_group)
		{
			s_blocks.Clear();
			grid.GetBlocks(s_blocks, b => b.FatBlock is IMyCameraBlock);
			foreach (IMySlimBlock slim in s_blocks)
			{
				IMyCameraBlock camera = (IMyCameraBlock)slim.FatBlock;
				if (!Usable(camera))
				{
					continue;
				}
				Vector3D target = camera.WorldMatrix.Translation + dir * 1000;
				if (!((Sandbox.Game.Entities.MyCameraBlock)camera).CheckAngleLimits(Vector3D.Normalize(target - camera.WorldMatrix.Translation)))
				{
					continue;
				}
				double dot = Vector3D.Dot(camera.WorldMatrix.Forward, dir);
				if (dot > bestDot)
				{
					bestDot = dot;
					best = camera;
				}
			}
		}
		s_blocks.Clear();
		return best;
	}

	private static bool SameShip(IMyCubeGrid hit, IMyCubeGrid own)
	{
		if (own == null)
		{
			return false;
		}
		s_group.Clear();
		MyAPIGateway.GridGroups.GetGroup(own, GridLinkTypeEnum.Physical, s_group);
		return hit.EntityId == own.EntityId || s_group.Exists(g => g.EntityId == hit.EntityId);
	}

	/// <summary>The kind, from what the raycast reports (a station is a large grid this PC shows as static).</summary>
	private static TargetKind KindOf(MyDetectedEntityInfo info, IMyEntity entity)
	{
		switch (info.Type)
		{
		case MyDetectedEntityType.Asteroid:
			return TargetKind.Asteroid;
		case MyDetectedEntityType.Planet:
			return TargetKind.Planet;
		case MyDetectedEntityType.LargeGrid:
			return entity is IMyCubeGrid grid && grid.IsStatic ? TargetKind.Station : TargetKind.LargeShip;
		case MyDetectedEntityType.SmallGrid:
			return TargetKind.SmallShip;
		case MyDetectedEntityType.CharacterHuman:
		case MyDetectedEntityType.CharacterOther:
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
		if (reading.Note != null)
		{
			return reading.Note;
		}
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
