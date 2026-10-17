using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Audio;
using VRageMath;

namespace Tripwire;

/// <summary>
/// Turns the watcher's block events into the alert log and chat lines. Every event goes into the log; chat gets at most
/// one line per grid every 30 seconds, summing up what happened since the last one. An alert names the grid and your own
/// blocks only: never where the attacker is.
/// </summary>
internal static class Alerts
{
	private const double ChatInterval = 30.0;

	private const int NamesPerKind = 3;

	private class Pending
	{
		public string Grid;

		public readonly List<string> Ground = new List<string>();

		public readonly List<string> Damaged = new List<string>();

		public readonly List<string> Lost = new List<string>();

		public readonly List<string> Owner = new List<string>();

		public readonly HashSet<string> Nearby = new HashSet<string>();

		public bool Any => Ground.Count + Damaged.Count + Lost.Count + Owner.Count > 0;
	}

	private static readonly Dictionary<long, Pending> s_pending = new Dictionary<long, Pending>();

	private static readonly Dictionary<long, double> s_lastSent = new Dictionary<long, double>();

	/// <summary>Chat alerts are muted until this time (session seconds).</summary>
	public static double MutedUntil;

	public static void Add(long gridId, string grid, string kind, string block, string detail, Vector3D blockPosition)
	{
		string nearby = NearbyNames.Find(blockPosition);
		AlertLog.Add(new LogEntry { Time = DateTime.UtcNow, Kind = kind, Grid = grid, Subject = block, Detail = detail, Nearby = nearby });
		if (!s_pending.TryGetValue(gridId, out Pending pending))
		{
			pending = new Pending();
			s_pending[gridId] = pending;
		}
		pending.Grid = grid;
		List<string> list = kind == "GRIND" ? pending.Ground : kind == "DMG" ? pending.Damaged : kind == "LOST" ? pending.Lost : pending.Owner;
		if (!list.Contains(block))
		{
			list.Add(block);
		}
		if (nearby.Length > 0)
		{
			foreach (string name in nearby.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
			{
				pending.Nearby.Add(name);
			}
		}
	}

	public static void Update(double now)
	{
		if (s_pending.Count == 0)
		{
			return;
		}
		List<long> sent = null;
		foreach (KeyValuePair<long, Pending> kv in s_pending)
		{
			if (!kv.Value.Any)
			{
				continue;
			}
			if (s_lastSent.TryGetValue(kv.Key, out double last) && now - last < ChatInterval)
			{
				continue;
			}
			s_lastSent[kv.Key] = now;
			(sent ??= new List<long>()).Add(kv.Key);
			Say(kv.Value, now);
		}
		if (sent != null)
		{
			foreach (long id in sent)
			{
				s_pending.Remove(id);
			}
		}
	}

	private static void Say(Pending p, double now)
	{
		if (!Settings.ChatAlerts || now < MutedUntil)
		{
			return;
		}
		List<string> parts = new List<string>();
		AddPart(parts, "being ground", p.Ground);
		AddPart(parts, "damaged", p.Damaged);
		AddPart(parts, "lost", p.Lost);
		AddPart(parts, "lost ownership", p.Owner);
		string text = $"{p.Grid}: {string.Join("; ", parts)}.";
		if (p.Nearby.Count > 0)
		{
			text += " Near: " + string.Join(", ", p.Nearby) + ".";
		}
		MyAPIGateway.Utilities.ShowMessage(Plugin.Name, text);
		if (Settings.Sound)
		{
			MyGuiAudio.PlaySound(MyGuiSounds.HudGPSNotification3);
		}
		if (p.Lost.Count > 0 || p.Ground.Count > 0 || p.Owner.Count > 0)
		{
			Personality.Say("under_attack", "grid", p.Grid);
		}
		else
		{
			Personality.Say("damaged", "grid", p.Grid);
		}
	}

	private static void AddPart(List<string> parts, string what, List<string> blocks)
	{
		if (blocks.Count == 0)
		{
			return;
		}
		string names = string.Join(", ", blocks.Take(NamesPerKind));
		if (blocks.Count > NamesPerKind)
		{
			names += $" and {blocks.Count - NamesPerKind} more";
		}
		parts.Add($"{what}: {names}");
	}

	public static void Reset()
	{
		s_pending.Clear();
		s_lastSent.Clear();
		MutedUntil = 0;
	}
}
