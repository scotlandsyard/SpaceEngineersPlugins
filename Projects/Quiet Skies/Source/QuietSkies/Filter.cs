using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace QuietSkies;

/// <summary>
/// Applies each kind's mode to the encounter and cargo drop markers in your GPS list.
/// Hide uses the game's own Show on HUD request (the G menu checkbox); Show sends it back, plus the Always visible
/// request the game clears when a marker is hidden, and only for markers this plugin hid. Nearby only sends nothing:
/// it takes the marker off this PC's HUD while it's out of range. Delete all and Keep newest use the G menu's
/// Delete request.
/// </summary>
internal static class Filter
{
	private enum Op
	{
		Hide,
		Show,
		AlwaysVisible,
		Delete
	}

	private struct Request
	{
		public long EntityId;

		public Op Op;
	}

	private struct Marker
	{
		public MyGps Gps;

		public SignalKind Kind;

		public int Index;
	}

	private const int ScanTicks = 60;

	/// <summary>At most one request every 12 ticks (5 a second), however many markers change at once.</summary>
	private const int SendTicks = 12;

	/// <summary>A marker hidden by Nearby only comes back within the distance, and goes again 10% beyond it.</summary>
	private const double Margin = 1.1;

	/// <summary>Markers missing for this long after loading are dropped from the saved lists.</summary>
	private const int PruneAfterTicks = 60 * 30;

	/// <summary>The mode each marker was last brought in line with this session. A marker whose kind's mode changes, or a new one, is handled again.</summary>
	private static readonly Dictionary<long, SignalMode> s_applied = new Dictionary<long, SignalMode>();

	/// <summary>Markers taken off this PC's HUD by Nearby only.</summary>
	private static readonly HashSet<MyGps> s_localHidden = new HashSet<MyGps>();

	private static readonly Queue<Request> s_queue = new Queue<Request>();

	private static readonly Dictionary<long, int> s_pending = new Dictionary<long, int>();

	/// <summary>Markers with a Delete request queued: left alone by everything else until they're gone.</summary>
	private static readonly HashSet<long> s_deleting = new HashSet<long>();

	private static readonly List<IMyGps> s_list = new List<IMyGps>();

	private static readonly List<Marker> s_markers = new List<Marker>();

	private static readonly HashSet<long> s_seen = new HashSet<long>();

	private static int s_tick;

	private static int s_nextScan;

	private static int s_lastSend = -SendTicks;

	private static bool s_subscribed;

	public static int RequestsSent { get; private set; }

	public static int Queued => s_queue.Count;

	/// <summary>Markers still waiting for their Delete request.</summary>
	public static int DeletesLeft => s_deleting.Count;

	public static void Start()
	{
		Reset();
		if (MySession.Static?.Gpss != null)
		{
			MySession.Static.Gpss.GpsAdded += OnGpsAdded;
			s_subscribed = true;
		}
	}

	public static void Stop()
	{
		if (s_subscribed && MySession.Static?.Gpss != null)
		{
			MySession.Static.Gpss.GpsAdded -= OnGpsAdded;
		}
		s_subscribed = false;
		Reset();
	}

	private static void Reset()
	{
		s_applied.Clear();
		s_localHidden.Clear();
		s_queue.Clear();
		s_pending.Clear();
		s_deleting.Clear();
		s_list.Clear();
		s_markers.Clear();
		s_seen.Clear();
		s_tick = 0;
		s_nextScan = 0;
		s_lastSend = -SendTicks;
		RequestsSent = 0;
	}

	private static void OnGpsAdded(long identityId, int hash)
	{
		// A new marker follows the current setting straight away (the game has already put it on the HUD and played its sound).
		if (MySession.Static != null && identityId == MySession.Static.LocalPlayerId)
		{
			s_nextScan = s_tick;
		}
	}

	/// <summary>Called after a setting changes, so it applies on the next tick instead of within a second.</summary>
	public static void ScanSoon()
	{
		s_nextScan = s_tick;
	}

	public static void Update()
	{
		s_tick++;
		if (s_tick >= s_nextScan)
		{
			s_nextScan = s_tick + ScanTicks;
			Scan();
		}
		if (s_queue.Count > 0 && s_tick - s_lastSend >= SendTicks)
		{
			s_lastSend = s_tick;
			Send(s_queue.Dequeue());
		}
	}

	/// <summary>The encounter and cargo drop markers in your GPS list, in the list's order. Nothing else is ever returned.</summary>
	private static void Collect()
	{
		s_list.Clear();
		s_markers.Clear();
		MySession session = MySession.Static;
		if (session?.Gpss == null)
		{
			return;
		}
		session.Gpss.GetGpsList(session.LocalPlayerId, s_list);
		for (int i = 0; i < s_list.Count; i++)
		{
			if (s_list[i] is MyGps gps && gps.EntityId != 0 && Signals.KindOf(gps) is SignalKind kind)
			{
				s_markers.Add(new Marker { Gps = gps, Kind = kind, Index = i });
			}
		}
	}

	private static void Scan()
	{
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (MySession.Static?.Gpss == null || player == null)
		{
			return;
		}
		Vector3D position = player.GetPosition();
		Collect();
		s_seen.Clear();
		foreach (Marker marker in s_markers)
		{
			s_seen.Add(marker.Gps.EntityId);
		}
		bool changed = Number();
		Trim();
		foreach (Marker marker in s_markers)
		{
			long id = marker.Gps.EntityId;
			if (s_deleting.Contains(id))
			{
				continue;
			}
			SignalMode mode = Settings.Mode(marker.Kind);
			if (!s_pending.ContainsKey(id) && (!s_applied.TryGetValue(id, out SignalMode applied) || applied != mode))
			{
				changed |= ApplyServerSide(marker.Gps, id, mode);
				s_applied[id] = mode;
			}
			ApplyLocal(marker.Gps, marker.Kind, mode, position);
		}

		// Forget markers that are gone (deleted, or the encounter or container went), once the world has had time to send them.
		s_localHidden.RemoveWhere(g => !s_seen.Contains(g.EntityId));
		s_deleting.RemoveWhere(id => !s_seen.Contains(id) && !s_pending.ContainsKey(id));
		if (s_tick > PruneAfterTicks)
		{
			if (Settings.HiddenByUs.RemoveWhere(id => !s_seen.Contains(id) && !s_pending.ContainsKey(id)) > 0)
			{
				changed = true;
			}
			foreach (long id in Settings.Seen.Keys.Where(id => !s_seen.Contains(id)).ToList())
			{
				Settings.Seen.Remove(id);
				changed = true;
			}
		}
		if (changed)
		{
			Settings.SaveWorld();
		}
	}

	/// <summary>
	/// Gives each marker not seen before the next arrival number. Markers already there when the world loads have no
	/// known arrival time (the game keeps none), so they're put in order by the cargo drop's number, which the server
	/// counts up, or by an encounter's time left, then by their place in the list.
	/// </summary>
	private static bool Number()
	{
		List<Marker> fresh = s_markers.Where(m => !Settings.Seen.ContainsKey(m.Gps.EntityId)).ToList();
		if (fresh.Count == 0)
		{
			return false;
		}
		foreach (Marker marker in fresh.OrderBy(m => m.Kind).ThenBy(m => Newness(m.Gps, m.Kind) ?? long.MinValue).ThenBy(m => m.Index))
		{
			Settings.Seen[marker.Gps.EntityId] = Settings.NextSeen++;
		}
		return true;
	}

	/// <summary>Higher is newer, or null when the marker doesn't say.</summary>
	private static long? Newness(MyGps gps, SignalKind kind)
	{
		if (kind == SignalKind.Encounter)
		{
			// "12 min" or "less than a minute" left: the game counts each encounter down from the same lifetime.
			string left = gps.ContainerRemainingTime;
			if (string.IsNullOrEmpty(left))
			{
				return null;
			}
			return FirstNumber(left) ?? 0;
		}
		// "Unknown Signal 12": the number at the end.
		string name = (gps.Name ?? "").TrimEnd();
		int start = name.Length;
		while (start > 0 && char.IsDigit(name[start - 1]))
		{
			start--;
		}
		return start < name.Length && long.TryParse(name.Substring(start), out long number) ? number : (long?)null;
	}

	private static long? FirstNumber(string text)
	{
		int start = 0;
		while (start < text.Length && !char.IsDigit(text[start]))
		{
			start++;
		}
		int end = start;
		while (end < text.Length && char.IsDigit(text[end]))
		{
			end++;
		}
		return end > start && long.TryParse(text.Substring(start, end - start), out long number) ? number : (long?)null;
	}

	/// <summary>Keep newest: queues a Delete for the oldest markers of each kind over its limit, hidden or not.</summary>
	private static void Trim()
	{
		foreach (SignalKind kind in Signals.Kinds)
		{
			int keep = Settings.Keep(kind);
			if (keep <= 0)
			{
				continue;
			}
			List<Marker> live = s_markers.Where(m => m.Kind == kind && !s_deleting.Contains(m.Gps.EntityId)).ToList();
			int extra = live.Count - keep;
			if (extra <= 0)
			{
				continue;
			}
			foreach (Marker marker in live.OrderBy(m => Settings.Seen.TryGetValue(m.Gps.EntityId, out long order) ? order : 0).Take(extra))
			{
				QueueDelete(marker.Gps.EntityId);
			}
			// One at a time as new ones arrive is the normal case; say so only when a batch goes.
			if (extra > 1)
			{
				MyAPIGateway.Utilities.ShowMessage(QuietSkiesSession.ChatSender, $"Keep newest {keep}: deleting the {extra} oldest {Signals.Name(kind)}s.");
			}
		}
	}

	/// <summary>Delete all: queues a Delete for every marker of the kind. Returns how many.</summary>
	public static int DeleteAll(SignalKind kind)
	{
		Collect();
		int count = 0;
		foreach (Marker marker in s_markers)
		{
			if (marker.Kind == kind && !s_deleting.Contains(marker.Gps.EntityId))
			{
				QueueDelete(marker.Gps.EntityId);
				count++;
			}
		}
		return count;
	}

	private static void QueueDelete(long id)
	{
		if (s_deleting.Add(id))
		{
			Enqueue(id, Op.Delete);
		}
	}

	/// <summary>Queues the Show on HUD requests a mode needs. Returns true if the saved list of markers we hid changed.</summary>
	private static bool ApplyServerSide(MyGps gps, long id, SignalMode mode)
	{
		if (mode == SignalMode.Hide)
		{
			// A marker you already took off the HUD yourself stays yours: it isn't added to the list.
			if (!gps.ShowOnHud)
			{
				return false;
			}
			Enqueue(id, Op.Hide);
			return Settings.HiddenByUs.Add(id);
		}
		if (!Settings.HiddenByUs.Remove(id))
		{
			return false;
		}
		if (!gps.ShowOnHud)
		{
			Enqueue(id, Op.Show);
			Enqueue(id, Op.AlwaysVisible);
		}
		return true;
	}

	private static void ApplyLocal(MyGps gps, SignalKind kind, SignalMode mode, Vector3D position)
	{
		if (mode != SignalMode.Nearby || !gps.ShowOnHud)
		{
			// Hide takes the marker off the HUD itself, so it isn't put back for a moment first.
			if (s_localHidden.Remove(gps) && gps.ShowOnHud && mode != SignalMode.Hide)
			{
				MyHud.GpsMarkers.RegisterMarker(gps);
			}
			return;
		}
		double limit = Settings.Km(kind) * 1000.0;
		double distance = Vector3D.Distance(position, gps.Coords);
		bool hidden = s_localHidden.Contains(gps);
		bool hide = hidden ? distance > limit : distance > limit * Margin;
		if (hide)
		{
			// Every scan, because the game puts a marker back on the HUD when it's edited.
			MyHud.GpsMarkers.UnregisterMarker(gps);
			s_localHidden.Add(gps);
		}
		else if (hidden)
		{
			s_localHidden.Remove(gps);
			MyHud.GpsMarkers.RegisterMarker(gps);
		}
	}

	private static void Enqueue(long id, Op op)
	{
		s_queue.Enqueue(new Request { EntityId = id, Op = op });
		s_pending[id] = s_pending.TryGetValue(id, out int count) ? count + 1 : 1;
	}

	private static void Send(Request request)
	{
		if (s_pending.TryGetValue(request.EntityId, out int count))
		{
			if (count <= 1)
			{
				s_pending.Remove(request.EntityId);
			}
			else
			{
				s_pending[request.EntityId] = count - 1;
			}
		}
		try
		{
			MySession session = MySession.Static;
			if (session?.Gpss == null)
			{
				return;
			}
			long identityId = session.LocalPlayerId;
			// Look the marker up again and check its kind again: never send anything for any other marker.
			MyGps gps = session.Gpss.GetGpsByEntityId(identityId, request.EntityId);
			if (gps == null || !Signals.KindOf(gps).HasValue)
			{
				s_deleting.Remove(request.EntityId);
				return;
			}
			if (s_deleting.Contains(request.EntityId) && request.Op != Op.Delete)
			{
				return;
			}
			switch (request.Op)
			{
			case Op.Hide:
				if (gps.ShowOnHud)
				{
					session.Gpss.SendChangeShowOnHudRequest(identityId, gps.Hash, false);
					RequestsSent++;
				}
				break;
			case Op.Show:
				if (!gps.ShowOnHud)
				{
					session.Gpss.SendChangeShowOnHudRequest(identityId, gps.Hash, true);
					RequestsSent++;
				}
				break;
			case Op.AlwaysVisible:
				if (gps.ShowOnHud && !gps.AlwaysVisible)
				{
					session.Gpss.SendChangeAlwaysVisibleRequest(identityId, gps.Hash, true);
					RequestsSent++;
				}
				break;
			case Op.Delete:
				s_localHidden.Remove(gps);
				session.Gpss.SendDeleteGpsRequest(identityId, gps.Hash);
				RequestsSent++;
				s_deleting.Remove(request.EntityId);
				s_applied.Remove(request.EntityId);
				Settings.HiddenByUs.Remove(request.EntityId);
				Settings.Seen.Remove(request.EntityId);
				if (s_deleting.Count == 0)
				{
					Settings.SaveWorld();
				}
				break;
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Quiet Skies] Request failed: {ex.Message}");
		}
	}

	/// <summary>For the window: how many markers of a kind are in your GPS list, and how many are on your HUD right now.</summary>
	public static void Count(SignalKind kind, out int total, out int onHud)
	{
		total = 0;
		onHud = 0;
		MySession session = MySession.Static;
		if (session?.Gpss == null)
		{
			return;
		}
		List<IMyGps> list = new List<IMyGps>();
		session.Gpss.GetGpsList(session.LocalPlayerId, list);
		foreach (IMyGps item in list)
		{
			if (item is MyGps gps && gps.EntityId != 0 && Signals.KindOf(gps) == kind)
			{
				total++;
				if (gps.ShowOnHud && !s_localHidden.Contains(gps))
				{
					onHud++;
				}
			}
		}
	}
}
