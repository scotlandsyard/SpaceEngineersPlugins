using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digby;

public class Marker
{
	/// <summary>Identifies the marker within its kind, e.g. "start" or "turn0".</summary>
	public string Key;

	public string Name;

	public Vector3D Position;

	public Color Color;
}

/// <summary>
/// GPS markers in two flavours. Local ones (IMyGpsCollection.AddLocalGps) show on the HUD and in the GPS list, but
/// the game never sends them to the server and never saves them, so they're gone after a relog. Server ones are
/// normal GPS entries (the same requests as adding a GPS by hand). Each carries a hidden key line in its
/// description ("DigbyL:kind:key" or "Digby:kind:key") so a new plan replaces the old markers instead of piling up.
/// Started as a copy of OreScout's GpsMarkers/LocalGps, cut down to what Digby needs.
/// </summary>
internal static class Markers
{
	private const string ServerKeyPrefix = "Digby:";

	private const string LocalKeyPrefix = "DigbyL:";

	// kind:key -> GPS hash of the local markers made since the world loaded.
	private static readonly Dictionary<string, int> s_local = new Dictionary<string, int>();

	/// <summary>Replaces the local markers of one kind with these.</summary>
	public static void ReplaceLocal(string kind, IEnumerable<Marker> markers, string description)
	{
		RemoveLocal(kind);
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		if (gpsCollection == null)
		{
			return;
		}
		foreach (Marker marker in markers)
		{
			AddLocal(gpsCollection, kind, marker, description);
		}
	}

	/// <summary>Adds local markers, keeping the ones of this kind already shown. Returns how many were added.</summary>
	public static int AddLocal(string kind, IEnumerable<Marker> markers, string description)
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		return gpsCollection == null ? 0 : markers.Count(marker => AddLocal(gpsCollection, kind, marker, description));
	}

	private static bool AddLocal(IMyGpsCollection gpsCollection, string kind, Marker marker, string description)
	{
		string key = kind + ":" + marker.Key;
		if (s_local.ContainsKey(key))
		{
			return false;
		}
		IMyGps gps = gpsCollection.Create(marker.Name, description + "\n" + LocalKeyPrefix + key, marker.Position, showOnHud: true);
		gps.GPSColor = marker.Color;
		gpsCollection.AddLocalGps(gps);
		s_local[key] = gps.Hash;
		return true;
	}

	public static int RemoveLocal(string kind)
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		List<string> keys = s_local.Keys.Where(k => k.StartsWith(kind + ":", StringComparison.Ordinal)).ToList();
		foreach (string key in keys)
		{
			gpsCollection?.RemoveLocalGps(s_local[key]);
			s_local.Remove(key);
		}
		return keys.Count;
	}

	/// <summary>Makes the server markers of one kind match these: existing ones are updated in place, others added or removed.</summary>
	public static void ReplaceServer(string kind, List<Marker> markers, string description)
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (gpsCollection == null || player == null)
		{
			return;
		}
		long identity = player.IdentityId;
		Dictionary<string, IMyGps> existing = new Dictionary<string, IMyGps>();
		List<IMyGps> remove = new List<IMyGps>();
		foreach (IMyGps gps in new List<IMyGps>(gpsCollection.GetGpsList(identity)))
		{
			string key = ReadKey(gps.Description, ServerKeyPrefix);
			if (key == null || !key.StartsWith(kind + ":", StringComparison.Ordinal))
			{
				continue;
			}
			if (existing.ContainsKey(key))
			{
				remove.Add(gps);
			}
			else
			{
				existing[key] = gps;
			}
		}
		foreach (Marker marker in markers)
		{
			string key = kind + ":" + marker.Key;
			string text = description + "\n" + ServerKeyPrefix + key;
			if (existing.TryGetValue(key, out IMyGps gps))
			{
				existing.Remove(key);
				// The game finds the marker by its old hash, so edit the live object and let ModifyGps rehash it.
				gps.Name = marker.Name;
				gps.Description = text;
				gps.Coords = marker.Position;
				gps.GPSColor = marker.Color;
				gpsCollection.ModifyGps(identity, gps);
			}
			else
			{
				IMyGps created = gpsCollection.Create(marker.Name, text, marker.Position, showOnHud: true);
				created.GPSColor = marker.Color;
				gpsCollection.AddGps(identity, created);
			}
		}
		remove.AddRange(existing.Values);
		foreach (IMyGps gps in remove)
		{
			gpsCollection.RemoveGps(identity, gps);
		}
	}

	/// <summary>Removes this kind's server markers; sends nothing when there are none.</summary>
	public static int RemoveServer(string kind)
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (gpsCollection == null || player == null)
		{
			return 0;
		}
		int removed = 0;
		foreach (IMyGps gps in new List<IMyGps>(gpsCollection.GetGpsList(player.IdentityId)))
		{
			string key = ReadKey(gps.Description, ServerKeyPrefix);
			if (key != null && key.StartsWith(kind + ":", StringComparison.Ordinal))
			{
				gpsCollection.RemoveGps(player.IdentityId, gps);
				removed++;
			}
		}
		return removed;
	}

	private static string ReadKey(string description, string prefix)
	{
		if (string.IsNullOrEmpty(description))
		{
			return null;
		}
		foreach (string line in description.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
			{
				return trimmed.Substring(prefix.Length);
			}
		}
		return null;
	}

	/// <summary>Forgets what was made (local markers vanish with the world).</summary>
	public static void Unload()
	{
		s_local.Clear();
	}

	public static string FormatDistance(double meters)
	{
		return meters >= 1000.0 ? $"{meters / 1000.0:F1} km" : $"{meters:F0} m";
	}
}
