using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities.Blocks;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
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

/// <summary>One line of a [Reserve] or [Reserve:Name] section: an item kept at an amount, maybe with automatic production.</summary>
internal sealed class ShepherdQuota
{
	public string Pool;

	public string Item;

	/// <summary>"", "M", "L" or "All".</summary>
	public string Mode;

	public double Target;

	/// <summary>The trailing A: the script makes the shortfall (and with Exact or L, disassembles the surplus).</summary>
	public bool Auto;

	/// <summary>The script assembles or refines what's short of it.</summary>
	public bool Makes => Auto && Mode != "L" && Mode != "All" && !Item.StartsWith("Ore/", StringComparison.Ordinal);

	/// <summary>The script disassembles what's above it (never ingots).</summary>
	public bool TakesApart => Auto && (Mode == "" || Mode == "L") && !Item.StartsWith("Ingot/", StringComparison.Ordinal) && !Item.StartsWith("Ore/", StringComparison.Ordinal);

	public string Text => Mode == "All" ? "All" : Items.Amount(Target) + Mode + (Auto ? "A" : "");

	/// <summary>Reads a quota value the way the script does: amount, then M or L, then A; or All.</summary>
	public static ShepherdQuota Parse(string pool, string item, string value)
	{
		string s = (value ?? "").Trim();
		if (s.Length == 0)
		{
			return null;
		}
		if (s.Equals("All", StringComparison.OrdinalIgnoreCase))
		{
			return new ShepherdQuota { Pool = pool, Item = item, Mode = "All" };
		}
		bool auto = char.ToUpperInvariant(s[s.Length - 1]) == 'A';
		if (auto)
		{
			s = s.Substring(0, s.Length - 1);
		}
		string mode = "";
		char last = s.Length > 0 ? char.ToUpperInvariant(s[s.Length - 1]) : ' ';
		if (last == 'M' || last == 'L')
		{
			mode = last.ToString();
			s = s.Substring(0, s.Length - 1);
		}
		if (!double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double target) || target < 0.0)
		{
			return null;
		}
		return new ShepherdQuota { Pool = pool, Item = item, Mode = mode, Target = target, Auto = auto };
	}
}

/// <summary>The parts of a SHEPHERD programmable block's Custom Data that decide what the script does.</summary>
internal sealed class ShepherdConfig
{
	public int ReactorFillPct = 15;

	public int GasGenFillPct = 50;

	public int RefineryFillPct = 30;

	public double BottleSweepMinutes = 3.0;

	public int Priority;

	public bool FactionShare;

	public bool DockedProduction;

	public bool DockedFuel;

	/// <summary>[Categories]: item key to category, for items moved out of their built-in category.</summary>
	public readonly Dictionary<string, string> Categories = new Dictionary<string, string>();

	public readonly List<ShepherdQuota> Quotas = new List<ShepherdQuota>();

	/// <summary>True when the text looks like a SHEPHERD block's Custom Data (it writes every [Settings] key at startup).</summary>
	public static bool Looks(string customData)
	{
		return !string.IsNullOrEmpty(customData) && customData.IndexOf("[Settings]", StringComparison.Ordinal) >= 0 && customData.IndexOf("refineryFillPct", StringComparison.Ordinal) >= 0 && customData.IndexOf("sortPassSeconds", StringComparison.Ordinal) >= 0;
	}

	public static ShepherdConfig Parse(string customData)
	{
		ShepherdConfig config = new ShepherdConfig();
		MyIni ini = new MyIni();
		if (!ini.TryParse(customData ?? ""))
		{
			return config;
		}
		config.ReactorFillPct = Int(ini, "reactorFillPct", config.ReactorFillPct);
		config.GasGenFillPct = Int(ini, "gasGenFillPct", config.GasGenFillPct);
		config.RefineryFillPct = Int(ini, "refineryFillPct", config.RefineryFillPct);
		config.Priority = Int(ini, "priority", 0);
		config.BottleSweepMinutes = double.TryParse(Value(ini, "Settings", "bottleSweepMinutes"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double minutes) && minutes >= 0.0 ? minutes : config.BottleSweepMinutes;
		config.FactionShare = Bool(ini, "factionShare");
		config.DockedProduction = Bool(ini, "dockedProduction");
		config.DockedFuel = Bool(ini, "dockedFuel");
		List<MyIniKey> keys = new List<MyIniKey>();
		List<string> sections = new List<string>();
		ini.GetSections(sections);
		foreach (string section in sections)
		{
			bool categories = section == "Categories";
			string pool = null;
			if (section.StartsWith("Reserve", StringComparison.OrdinalIgnoreCase))
			{
				string after = section.Substring(7);
				pool = after.Length == 0 ? "" : after[0] == ':' ? after.Substring(1) : null;
			}
			if (!categories && pool == null)
			{
				continue;
			}
			keys.Clear();
			ini.GetKeys(section, keys);
			foreach (MyIniKey key in keys)
			{
				string item = key.Name.StartsWith("MyObjectBuilder_", StringComparison.Ordinal) ? key.Name.Substring(16) : key.Name;
				string value = Value(ini, section, key.Name);
				if (categories)
				{
					if (item.IndexOf('/') > 0 && !string.IsNullOrWhiteSpace(value))
					{
						config.Categories[item] = value.Trim();
					}
				}
				else if (key.Name != "docked")
				{
					ShepherdQuota quota = ShepherdQuota.Parse(pool, item, value);
					if (quota != null)
					{
						config.Quotas.Add(quota);
					}
				}
			}
		}
		return config;
	}

	/// <summary>The value with an inline ; comment taken off, as the script reads it.</summary>
	private static string Value(MyIni ini, string section, string key)
	{
		string value = ini.Get(section, key).ToString(null);
		if (value == null)
		{
			return null;
		}
		int semi = value.IndexOf(';');
		return (semi >= 0 ? value.Substring(0, semi) : value).Trim();
	}

	private static int Int(MyIni ini, string key, int fallback)
	{
		return int.TryParse(Value(ini, "Settings", key), out int value) ? value : fallback;
	}

	private static bool Bool(MyIni ini, string key)
	{
		return bool.TryParse(Value(ini, "Settings", key), out bool value) && value;
	}

	/// <summary>The quota lines that make or take apart an item, from every pool.</summary>
	public IEnumerable<ShepherdQuota> AutoQuotas(string item)
	{
		return Quotas.Where(q => q.Auto && q.Item == item);
	}
}

/// <summary>
/// A SHEPHERD script found on a loaded construct: which programmable block runs it, what its status screen says,
/// its settings, and which of the construct's units (mechanical groups) it manages. Everything is read from what the
/// game already has on this computer (block names, Custom Data, screen text), so finding it sends nothing to the
/// server. The block's detail pane isn't used: a multiplayer client only receives it while the block is open in the
/// terminal.
/// </summary>
internal sealed class ShepherdLink
{
	/// <summary>A status screen whose text hasn't changed for this long means the script isn't running.</summary>
	private const double StaleSeconds = 90.0;

	// Custom Data parsed per block, kept while the text is the same string.
	private static readonly Dictionary<long, KeyValuePair<string, ShepherdConfig>> s_configs = new Dictionary<long, KeyValuePair<string, ShepherdConfig>>();

	// Status screen text per block and when it last changed, to notice a script that stopped.
	private static readonly Dictionary<long, KeyValuePair<string, double>> s_screens = new Dictionary<long, KeyValuePair<string, double>>();

	public IMyProgrammableBlock Block;

	public long Unit;

	public ShepherdConfig Config;

	/// <summary>The block is on, built and powered.</summary>
	public bool Working;

	/// <summary>Its status screen stopped changing: the script isn't running (stopped, crashed, or scripts are off).</summary>
	public bool Stale;

	/// <summary>The health word on its status screen (working, idle, learning, standing by, HALTED, THROTTLED), or null without one.</summary>
	public string Health;

	public string Version;

	/// <summary>The lines under the rule on its status screen: what it's doing and what needs the player.</summary>
	public List<string> Problems = new List<string>();

	/// <summary>The units it manages: its own, and those docked to it unless a [SHEPHERD-Split] connector cuts them off.</summary>
	public HashSet<long> Units = new HashSet<long>();

	/// <summary>Running and in charge: Stockpile Manager steps back from its jobs.</summary>
	public bool Active => Working && !Stale && Health != "standing by";

	public string Name => Block?.CustomName ?? "";

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
				return $"{Name}: its status screen stopped updating (script stopped?); Stockpile Manager does every job";
			}
			string version = Version != null ? " " + Version : "";
			return Health == null ? $"{Name}{version}: running (no [SHEPHERD] status screen to read)" : $"{Name}{version}: {Health}";
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
	/// Finds the SHEPHERD block that leads on this construct, if any. Among several, the script's own election
	/// decides (highest priority, then station over ship, then large grid over small, then the lower entity id);
	/// blocks that aren't working don't take part, as they don't broadcast.
	/// </summary>
	public static ShepherdLink Find(Construct construct, double now)
	{
		List<IMyProgrammableBlock> blocks = new List<IMyProgrammableBlock>();
		construct.Terminal.GetBlocksOfType(blocks, b => !b.Closed && ShepherdConfig.Looks(b.CustomData));
		long me = MyAPIGateway.Session?.Player?.IdentityId ?? 0;
		ShepherdLink best = null;
		long bestRank = long.MinValue;
		foreach (IMyProgrammableBlock block in blocks)
		{
			// A block that runs another script now keeps the old Custom Data; when the client has the script text, check it.
			string program = (block as Sandbox.ModAPI.IMyProgrammableBlock)?.ProgramData;
			if (!string.IsNullOrEmpty(program) && program.IndexOf(ShepherdTag.Keyword, StringComparison.Ordinal) < 0)
			{
				continue;
			}
			ShepherdConfig config = ConfigOf(block);
			// The script manages its owner's blocks, and faction-shared ones with factionShare on.
			if (block.OwnerId != me && !(config.FactionShare && SameFaction(block.OwnerId, me)))
			{
				continue;
			}
			MyCubeGridRank(block, out bool isStatic, out bool isLarge);
			long rank = config.Priority * 4L + (isStatic ? 2 : 0) + (isLarge ? 1 : 0);
			bool working = block.IsWorking;
			// Working blocks first; then the script's own order.
			long sortRank = (working ? 1L << 40 : 0) + rank;
			if (best != null && (sortRank < bestRank || (sortRank == bestRank && block.EntityId > best.Block.EntityId)))
			{
				continue;
			}
			best = new ShepherdLink { Block = block, Config = config, Working = working };
			bestRank = sortRank;
		}
		if (best == null)
		{
			return null;
		}
		LiveBlock own = construct.Find(best.Block.EntityId);
		best.Unit = own?.Unit ?? construct.CoreUnit;
		best.ReadStatus(construct, now);
		best.FindUnits(construct);
		return best;
	}

	private static void MyCubeGridRank(IMyTerminalBlock block, out bool isStatic, out bool isLarge)
	{
		IMyCubeGrid grid = block.CubeGrid;
		isStatic = grid.IsStatic;
		isLarge = grid.GridSizeEnum == VRage.Game.MyCubeSize.Large;
	}

	private static bool SameFaction(long owner, long me)
	{
		IMyFaction mine = MyAPIGateway.Session?.Factions?.TryGetPlayerFaction(me);
		return mine != null && MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner) == mine;
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
	/// Reads the first [SHEPHERD] status screen drawn by this block (its second line is the block's name):
	/// "SHEPHERD 1.0.3 · working", the name, a clock, then fill bars, a rule, and the lines that matter.
	/// </summary>
	private void ReadStatus(Construct construct, double now)
	{
		foreach (LiveBlock screen in construct.Blocks.Where(b => b.Tag?.Status == true && b.Block is IngameSurfaceProvider))
		{
			IngameSurfaceProvider provider = (IngameSurfaceProvider)screen.Block;
			if (provider.SurfaceCount == 0)
			{
				continue;
			}
			string text = provider.GetSurface(0).GetText() ?? "";
			string[] lines = text.Replace("\r", "").Split('\n');
			if (lines.Length < 2 || !lines[0].StartsWith(ShepherdTag.Keyword, StringComparison.Ordinal) || lines[1].Trim() != Name.Trim())
			{
				continue;
			}
			int dot = lines[0].IndexOf('·');
			if (dot > 0)
			{
				Version = lines[0].Substring(ShepherdTag.Keyword.Length, dot - ShepherdTag.Keyword.Length).Trim();
				Health = lines[0].Substring(dot + 1).Trim();
			}
			int rule = Array.FindLastIndex(lines, l => l.StartsWith("───", StringComparison.Ordinal) && l.Trim('─').Length == 0);
			if (rule >= 0)
			{
				Problems = lines.Skip(rule + 1).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
			}
			long id = screen.Block.EntityId;
			if (!s_screens.TryGetValue(id, out KeyValuePair<string, double> seen) || seen.Key != text)
			{
				s_screens[id] = new KeyValuePair<string, double>(text, now);
			}
			else
			{
				Stale = now - seen.Value > StaleSeconds;
			}
			return;
		}
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
