using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities.Blocks;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using IngameSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace SacrificialStockpileManager;

/// <summary>
/// The jobs a SHEPHERD script (an inventory-sorting programmable block script that runs on the server) also does.
/// While one runs on a grid, Stockpile Manager leaves each of these to it unless the player takes the job back in
/// the Overview (<see cref="GridRules.ShepherdMine"/>).
/// </summary>
internal enum ShepherdJob
{
	/// <summary>Sorting storage, draining connectors and collectors.</summary>
	Sort,
	/// <summary>Unloading docked ships and restocking them for their quotas.</summary>
	Docked,
	/// <summary>Keeping reactors and O2/H2 generators fuelled.</summary>
	Fuel,
	/// <summary>Feeding refineries and the refinery priority (refine by need included).</summary>
	Refineries,
	/// <summary>Emptying refinery and assembler output.</summary>
	Output,
	/// <summary>Crafting and disassembling items that have an A quota in its Custom Data.</summary>
	Craft,
	/// <summary>Refilling gas bottles.</summary>
	Bottles
}

/// <summary>What a block's name says to a SHEPHERD script: its bracket tags (the script's README, "Tags").</summary>
internal sealed class ShepherdTag
{
	public const string Keyword = "SHEPHERD";

	/// <summary>[SHEPHERD]: the status screen.</summary>
	public bool Status;

	/// <summary>[SHEPHERD-Off]: the script leaves the block alone.</summary>
	public bool Off;

	/// <summary>[SHEPHERD-Split]: on a connector, nothing beyond it is managed.</summary>
	public bool Split;

	public bool Main;

	public bool Coop;

	/// <summary>[SHEPHERD:...] on a block with an inventory: a category container.</summary>
	public bool Destination;

	/// <summary>[SHEPHERD-RESERVE] or [SHEPHERD-RESERVE:Name]: a reserve-pool container.</summary>
	public bool Reserve;

	/// <summary>Containers the script fills itself: Stockpile Manager never moves items into or out of them.</summary>
	public bool Owned => Destination || Reserve;

	/// <summary>The tags in a block name, or null when it has none.</summary>
	public static ShepherdTag Parse(string name)
	{
		if (string.IsNullOrEmpty(name) || name.IndexOf("[" + Keyword, StringComparison.OrdinalIgnoreCase) < 0)
		{
			return null;
		}
		ShepherdTag tag = null;
		int i = 0;
		while (i < name.Length)
		{
			int open = name.IndexOf('[', i);
			if (open < 0)
			{
				break;
			}
			int close = name.IndexOf(']', open + 1);
			if (close < 0)
			{
				break;
			}
			string inner = name.Substring(open + 1, close - open - 1);
			i = close + 1;
			if (!inner.StartsWith(Keyword, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string rest = inner.Substring(Keyword.Length);
			tag ??= new ShepherdTag();
			if (rest.Length == 0)
			{
				tag.Status = true;
			}
			else if (rest[0] == ':')
			{
				tag.Destination = true;
			}
			else if (rest[0] == '-')
			{
				string scope = rest.Substring(1);
				if (scope.Equals("Off", StringComparison.OrdinalIgnoreCase))
				{
					tag.Off = true;
				}
				else if (scope.Equals("Split", StringComparison.OrdinalIgnoreCase))
				{
					tag.Split = true;
				}
				else if (scope.Equals("Main", StringComparison.OrdinalIgnoreCase))
				{
					tag.Main = true;
				}
				else if (scope.Equals("Coop", StringComparison.OrdinalIgnoreCase))
				{
					tag.Coop = true;
				}
				else if (scope.StartsWith("RESERVE", StringComparison.OrdinalIgnoreCase) && (scope.Length == 7 || scope[7] == ':'))
				{
					tag.Reserve = true;
				}
			}
		}
		return tag;
	}
}

/// <summary>
/// A SHEPHERD script on a loaded construct: whether it's doing its jobs, read from its [SHEPHERD] status panels (a
/// moving clock and a running health word), the settings of the player's SHEPHERD block, and which of the construct's
/// units (mechanical groups) it manages. Which block leads is SHEPHERD's own business and isn't worked out here.
/// Everything is read from what the game already has on this computer (block names, Custom Data, screen text), so
/// finding it sends nothing to the server. The block's detail pane isn't used: a multiplayer client only receives
/// it while the block is open in the terminal.
/// </summary>
internal sealed class ShepherdLink
{
	/// <summary>
	/// A panel whose text hasn't changed for this long is frozen. The script redraws its screens one at a time and
	/// pauses redraws during busy runs, so a grid with many of its screens gets 3 s per screen.
	/// </summary>
	private const double StaleSeconds = 90.0;

	private const double StaleSecondsPerScreen = 3.0;

	/// <summary>No frozen verdict this soon after a panel is first seen.</summary>
	private const double StaleGraceSeconds = 180.0;

	// When each panel was first seen this session.
	private static readonly Dictionary<long, double> s_firstSeen = new Dictionary<long, double>();

	// Custom Data parsed per block, kept while the text is the same string.
	private static readonly Dictionary<long, KeyValuePair<string, ShepherdConfig>> s_configs = new Dictionary<long, KeyValuePair<string, ShepherdConfig>>();

	// Panel text per block and when it last changed.
	private static readonly Dictionary<long, KeyValuePair<string, double>> s_screens = new Dictionary<long, KeyValuePair<string, double>>();

	/// <summary>The player's SHEPHERD programmable block whose settings apply (the one named on the panel when it can tell), or null when only a panel was found.</summary>
	public IMyProgrammableBlock Block;

	public long Unit;

	/// <summary>Forgets what was seen, when a world loads or unloads (game times start over).</summary>
	public static void Reset()
	{
		s_configs.Clear();
		s_screens.Clear();
		s_firstSeen.Clear();
	}

	public ShepherdConfig Config = new ShepherdConfig();

	/// <summary>One of the player's SHEPHERD blocks is on, built and powered (true when only a panel was found).</summary>
	public bool Working;

	/// <summary>It has [SHEPHERD] panels, and every one is frozen: the script isn't running (stopped, removed, or scripts are off).</summary>
	public bool Stale;

	/// <summary>The health word shown (working, idle, learning, standing by, HALTED), or null without a panel.</summary>
	public string Health;

	public string Version;

	/// <summary>The name on the panel shown, else the block's.</summary>
	public string PanelName;

	/// <summary>The lines under the panel's last rule: what it's doing and what needs the player (informative only).</summary>
	public List<string> Problems = new List<string>();

	/// <summary>The units it manages: its own, and those docked to it unless a [SHEPHERD-Split] connector cuts them off.</summary>
	public HashSet<long> Units = new HashSet<long>();

	private bool _active;

	/// <summary>
	/// Doing its jobs: Stockpile Manager steps back. True while any panel has a moving clock and says working, idle
	/// or learning (learning moves nothing on purpose), or standing by (another SHEPHERD block does the work); false
	/// when every panel is frozen or HALTED, or the player's SHEPHERD blocks are all off. Without a panel it's assumed
	/// while a block works. HALTED clears on its own: at the next refresh after a panel shows a running word again,
	/// the jobs go back to SHEPHERD.
	/// </summary>
	public bool Active => _active;

	public string Name => PanelName ?? Block?.CustomName ?? "SHEPHERD";

	/// <summary>What the Overview and the LCD say about it.</summary>
	public string StateText
	{
		get
		{
			if (!Working)
			{
				return $"{Name}: the programmable block is off, damaged or unpowered; Stockpile Manager does every job";
			}
			if (Stale)
			{
				return $"{Name}: its status screen stopped updating (script stopped?); Stockpile Manager does every job until it moves again";
			}
			string version = Version != null ? " " + Version : "";
			if (Health == ShepherdPanel.Halted)
			{
				return $"{Name}{version}: HALTED after repeated crashes (see its [SHEPHERD-LOG] screen); Stockpile Manager does every job until it runs again";
			}
			if (Health == ShepherdPanel.StandingBy)
			{
				return $"{Name}{version}: standing by, another SHEPHERD block does the work";
			}
			return Health == null ? $"{Name}: assumed running. Add an LCD named [SHEPHERD] so Stockpile Manager can tell when it stops or halts" : $"{Name}{version}: {Health}";
		}
	}

	/// <summary>Whether it manages this block (its unit, and for a docked unit only the kinds its settings say).</summary>
	public bool Covers(LiveBlock block)
	{
		return Units.Contains(block.Unit) && block.Tag?.Off != true;
	}

	/// <summary>Whether this job on this block is left to the script: it runs, manages the block, and the player hasn't taken the job back.</summary>
	public bool Handles(ShepherdJob job, LiveBlock block, GridRules rules)
	{
		if (!Active || !Covers(block) || IsMine(job, rules))
		{
			return false;
		}
		bool home = block.Unit == Unit;
		switch (job)
		{
		case ShepherdJob.Fuel:
			return block.Kind == BlockKind.Reactor ? Config.ReactorFillPct > 0 && (home || Config.DockedFuel) : block.Kind == BlockKind.GasGenerator && Config.GasGenFillPct > 0 && (home || Config.DockedFuel);
		case ShepherdJob.Refineries:
			return block.Kind == BlockKind.Refinery && Config.RefineryFillPct > 0 && (home || Config.DockedProduction);
		case ShepherdJob.Output:
			return Construct.IsProductionKind(block.Kind) && (home || Config.DockedProduction);
		default:
			return true;
		}
	}

	/// <summary>Whether a whole job is left to the script on its own unit.</summary>
	public bool Handles(ShepherdJob job, GridRules rules)
	{
		if (!Active || IsMine(job, rules))
		{
			return false;
		}
		switch (job)
		{
		case ShepherdJob.Bottles:
			return Config.BottleSweepMinutes > 0.0;
		case ShepherdJob.Refineries:
			return Config.RefineryFillPct > 0;
		default:
			return true;
		}
	}

	public static bool IsMine(ShepherdJob job, GridRules rules)
	{
		return rules?.ShepherdMine?.Contains(job.ToString()) == true;
	}

	/// <summary>The quota lines that put this item in the script's hands, or an empty list.</summary>
	public List<ShepherdQuota> CraftQuotas(string item)
	{
		return Config.AutoQuotas(item).ToList();
	}

	/// <summary>
	/// Finds SHEPHERD on this construct: the player's programmable blocks with SHEPHERD's Custom Data ([Settings] with
	/// refineryFillPct, and "SHEPHERD" in the script text when the client has it), and the player's [SHEPHERD] status
	/// panels. Another player's SHEPHERD only manages its owner's blocks, so it's left out.
	/// </summary>
	public static ShepherdLink Find(Construct construct, double now)
	{
		long me = MyAPIGateway.Session?.Player?.IdentityId ?? 0;
		List<IMyProgrammableBlock> blocks = new List<IMyProgrammableBlock>();
		construct.Terminal.GetBlocksOfType(blocks, b => !b.Closed && b.OwnerId == me && ShepherdConfig.Looks(b.CustomData) && HasScript(b));
		List<KeyValuePair<LiveBlock, ShepherdPanel>> panels = ReadPanels(construct, me, now, out List<bool> live);
		if (blocks.Count == 0 && panels.Count == 0)
		{
			return null;
		}
		ShepherdLink link = new ShepherdLink();
		// The panel to show: a running one first, then standing by, then any other moving one, then any.
		int shown = Enumerable.Range(0, panels.Count)
			.OrderByDescending(i => live[i] && panels[i].Value.Running)
			.ThenByDescending(i => live[i] && panels[i].Value.Health == ShepherdPanel.StandingBy)
			.ThenByDescending(i => live[i])
			.DefaultIfEmpty(-1)
			.First();
		ShepherdPanel panel = shown >= 0 ? panels[shown].Value : null;
		if (panel != null)
		{
			link.Health = panel.Health;
			link.Version = panel.Version;
			link.PanelName = panel.Name;
			link.Problems = panel.Messages;
		}
		// Settings from the block named on that panel when there's one (names can repeat: a working one first), else
		// from the first working block.
		link.Block = blocks.Where(b => panel != null && b.CustomName.Trim() == panel.Name).OrderByDescending(b => b.IsWorking).FirstOrDefault()
			?? blocks.OrderByDescending(b => b.IsWorking).FirstOrDefault();
		if (link.Block != null)
		{
			link.Config = ConfigOf(link.Block);
		}
		link.Working = blocks.Count == 0 || blocks.Any(b => b.IsWorking);
		link.Stale = panels.Count > 0 && !live.Any(l => l);
		bool running = Enumerable.Range(0, panels.Count).Any(i => live[i] && (panels[i].Value.Running || panels[i].Value.Health == ShepherdPanel.StandingBy));
		link._active = link.Working && (panels.Count == 0 || running);
		LiveBlock home = link.Block != null ? construct.Find(link.Block.EntityId) : panels[Math.Max(0, shown)].Key;
		link.Unit = home?.Unit ?? construct.CoreUnit;
		link.FindUnits(construct);
		return link;
	}

	/// <summary>A block that switched scripts keeps the old Custom Data; when the client has the script text, it must name SHEPHERD.</summary>
	private static bool HasScript(IMyProgrammableBlock block)
	{
		string program = (block as Sandbox.ModAPI.IMyProgrammableBlock)?.ProgramData;
		return string.IsNullOrEmpty(program) || program.IndexOf(ShepherdTag.Keyword, StringComparison.Ordinal) >= 0;
	}

	private static ShepherdConfig ConfigOf(IMyTerminalBlock block)
	{
		string text = block.CustomData;
		if (s_configs.TryGetValue(block.EntityId, out KeyValuePair<string, ShepherdConfig> cached) && ReferenceEquals(cached.Key, text))
		{
			return cached.Value;
		}
		ShepherdConfig config = ShepherdConfig.Parse(text);
		s_configs[block.EntityId] = new KeyValuePair<string, ShepherdConfig>(text, config);
		return config;
	}

	/// <summary>
	/// The player's [SHEPHERD] panels, and for each whether its clock moves. SHEPHERD redraws only the first panel on
	/// its construct, so a second one stays frozen; any one moving is enough. A panel counts as frozen when its text
	/// hasn't changed for max(90 s, 3 s x SHEPHERD screens), and never in its first 3 minutes.
	/// </summary>
	private static List<KeyValuePair<LiveBlock, ShepherdPanel>> ReadPanels(Construct construct, long me, double now, out List<bool> live)
	{
		List<KeyValuePair<LiveBlock, ShepherdPanel>> panels = new List<KeyValuePair<LiveBlock, ShepherdPanel>>();
		live = new List<bool>();
		// Its screens: blocks without an inventory whose names carry a SHEPHERD tag (status, log, debug, levels, stock).
		int screens = construct.Blocks.Count(b => b.Input == null && b.Tag != null);
		double timeout = Math.Max(StaleSeconds, StaleSecondsPerScreen * screens);
		foreach (LiveBlock screen in construct.Blocks.Where(b => b.Tag?.Status == true && b.Input == null && b.Block.OwnerId == me && b.Block is IngameSurfaceProvider))
		{
			IngameSurfaceProvider provider = (IngameSurfaceProvider)screen.Block;
			if (provider.SurfaceCount == 0)
			{
				continue;
			}
			string text = provider.GetSurface(0).GetText() ?? "";
			ShepherdPanel panel = ShepherdPanel.Parse(text);
			if (panel == null)
			{
				continue;
			}
			long id = screen.Block.EntityId;
			if (!s_firstSeen.ContainsKey(id))
			{
				s_firstSeen[id] = now;
			}
			bool moving = true;
			if (!s_screens.TryGetValue(id, out KeyValuePair<string, double> seen) || seen.Key != text)
			{
				s_screens[id] = new KeyValuePair<string, double>(text, now);
			}
			else if (now - s_firstSeen[id] > StaleGraceSeconds)
			{
				moving = now - seen.Value <= timeout;
			}
			panels.Add(new KeyValuePair<LiveBlock, ShepherdPanel>(screen, panel));
			live.Add(moving);
		}
		return panels;
	}

	/// <summary>Its own unit plus every unit reached through docked connectors, except across a [SHEPHERD-Split] connector.</summary>
	private void FindUnits(Construct construct)
	{
		Dictionary<long, List<long>> links = new Dictionary<long, List<long>>();
		foreach (LiveBlock live in construct.Blocks)
		{
			if (!(live.Block is IMyShipConnector connector) || !connector.IsConnected || connector.OtherConnector == null)
			{
				continue;
			}
			LiveBlock other = construct.Find(connector.OtherConnector.EntityId);
			if (other == null || other.Unit == live.Unit || live.Tag?.Split == true || other.Tag?.Split == true)
			{
				continue;
			}
			if (!links.TryGetValue(live.Unit, out List<long> list))
			{
				links[live.Unit] = list = new List<long>();
			}
			list.Add(other.Unit);
		}
		Queue<long> next = new Queue<long>();
		next.Enqueue(Unit);
		Units.Add(Unit);
		while (next.Count > 0)
		{
			if (!links.TryGetValue(next.Dequeue(), out List<long> list))
			{
				continue;
			}
			foreach (long unit in list.Where(u => Units.Add(u)))
			{
				next.Enqueue(unit);
			}
		}
	}

	/// <summary>
	/// Setups where the two would undo each other's work, as Overview warnings. At most a few of each kind, so a big
	/// grid doesn't fill the list.
	/// </summary>
	public List<string> Conflicts(Construct construct, GridRules rules)
	{
		List<string> warnings = new List<string>();
		if (!Active)
		{
			return warnings;
		}
		// The script drains every storage-like inventory (cargo, cockpits, connectors) into its containers, so a
		// block Stockpile Manager keeps items in needs [SHEPHERD-Off] or a reserve tag.
		List<string> drained = construct.Blocks
			.Where(b => !b.NotYours && Units.Contains(b.Unit) && b.Tag == null && b.Input != null && DrainedByScript(b.Kind) && (b.Role == Effective.Stock || b.Limits.Any(l => l.HasMin && l.Min > 0.0)))
			.Select(b => b.Name)
			.ToList();
		AddCapped(warnings, drained, name => $"{name}: SHEPHERD empties it into its containers; add [SHEPHERD-Off] to its name (or Export setup to SHEPHERD) to keep its stock");
		List<string> both = construct.Blocks
			.Where(b => (b.Tag?.Main == true || b.Tag?.Coop == true) && (b.AssemblerMode == TimShared.AssemblerMode.Main || b.AssemblerMode == TimShared.AssemblerMode.Coop))
			.Select(b => b.Name)
			.ToList();
		AddCapped(warnings, both, name => $"{name}: has a SHEPHERD assembler tag and a Stockpile Manager mode; only SHEPHERD queues on it");
		if (IsMine(ShepherdJob.Craft, rules) && rules != null)
		{
			List<string> crafted = rules.Quotas.Where(q => Config.AutoQuotas(q.Item).Any()).Select(q => q.Item).ToList();
			AddCapped(warnings, crafted, item => $"{Items.Name(item)}: both Stockpile Manager and SHEPHERD keep a quota for it; give it to one of them");
		}
		if (IsMine(ShepherdJob.Fuel, rules) && (Config.ReactorFillPct > 0 || Config.GasGenFillPct > 0))
		{
			warnings.Add("Fuel is Stockpile Manager's job here, but SHEPHERD also fills reactors and generators: set reactorFillPct=0 and gasGenFillPct=0 in its Custom Data");
		}
		if (IsMine(ShepherdJob.Refineries, rules) && Config.RefineryFillPct > 0)
		{
			warnings.Add("Refineries are Stockpile Manager's job here, but SHEPHERD also feeds them: set refineryFillPct=0 in its Custom Data");
		}
		return warnings;
	}

	/// <summary>Block kinds the script empties into its containers (its Storage and Collector roles).</summary>
	private static bool DrainedByScript(BlockKind kind)
	{
		return kind == BlockKind.Cargo || kind == BlockKind.Connector || kind == BlockKind.Collector || kind == BlockKind.Cockpit || kind == BlockKind.Tool || kind == BlockKind.Other;
	}

	private static void AddCapped(List<string> warnings, List<string> names, Func<string, string> text)
	{
		const int Shown = 3;
		foreach (string name in names.Take(Shown))
		{
			warnings.Add(text(name));
		}
		if (names.Count > Shown)
		{
			warnings.Add($"... and {names.Count - Shown} more like that");
		}
	}
}
