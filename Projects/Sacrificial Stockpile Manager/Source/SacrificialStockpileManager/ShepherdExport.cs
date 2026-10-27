using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities.Blocks;
using Sandbox.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using UpdateType = Sandbox.ModAPI.Ingame.UpdateType;

namespace SacrificialStockpileManager;

/// <summary>
/// Hands a grid's setup over to its SHEPHERD script, once, when the player asks: tags in block names for what
/// Stockpile Manager's settings say about each block, and quota and category lines in the script's Custom Data.
/// Only the grid itself (not docked ships) and only blocks the player owns. Each rename and the Custom Data write is
/// the same request as editing them in the terminal, so the server checks the player's access, and then the script
/// is run with configReload, as the terminal's Run button would.
/// </summary>
internal static class ShepherdExport
{
	/// <summary>The reserve pool the grid's quotas go into. It has no container: the script counts its A quotas grid-wide (and from 1.0.5 doesn't warn about an all-A pool without one).</summary>
	public const string QuotaPool = "Quotas";

	private static readonly string[] CategoryTokens = { "Ores", "Ingots", "Components", "Ammo", "Tools", "Bottles", "Consumables", "Others" };

	internal sealed class Plan
	{
		public IMyProgrammableBlock Block;

		public readonly List<KeyValuePair<IMyTerminalBlock, string>> Renames = new List<KeyValuePair<IMyTerminalBlock, string>>();

		public readonly Dictionary<string, string> Categories = new Dictionary<string, string>();

		/// <summary>Whole sections written over (their old lines are replaced).</summary>
		public readonly Dictionary<string, List<KeyValuePair<string, string>>> Sections = new Dictionary<string, List<KeyValuePair<string, string>>>();

		public int Containers;

		public int CatchAll;

		public int Reserves;

		public int Off;

		public int Assemblers;

		public int QuotaLines;

		/// <summary>Accepted single items, each moved into a category of its own.</summary>
		public int SingleItems;

		/// <summary>Stock-block lines with only a maximum (L).</summary>
		public int MaxOnly;

		/// <summary>Quota lines lowered by what other pools already keep.</summary>
		public int Lowered;

		public readonly List<string> Notes = new List<string>();

		public string Problem;

		public bool Empty => Renames.Count == 0 && Categories.Count == 0 && Sections.Count == 0;
	}

	/// <summary>How long after configReload the [Categories] lines are checked again.</summary>
	private const double RecheckSeconds = 5.0;

	private sealed class Pending
	{
		public Construct Construct;

		public IMyProgrammableBlock Block;

		public Dictionary<string, string> Categories;

		public double Due;
	}

	private static Pending s_pending;

	/// <summary>
	/// A few seconds after an export to SHEPHERD before 1.0.21, puts back any [Categories] line a save of its own
	/// dropped, and reloads it once more. Called on every refresh of a construct.
	/// </summary>
	public static void CheckPending(Construct construct, double now)
	{
		Pending pending = s_pending;
		if (pending == null || pending.Construct != construct || now < pending.Due)
		{
			return;
		}
		s_pending = null;
		MyIni ini = new MyIni();
		if (pending.Block.Closed || !ini.TryParse(pending.Block.CustomData ?? ""))
		{
			return;
		}
		List<KeyValuePair<string, string>> missing = pending.Categories.Where(c => ini.Get("Categories", c.Key).ToString(null) != c.Value).ToList();
		if (missing.Count == 0)
		{
			return;
		}
		foreach (KeyValuePair<string, string> category in missing)
		{
			ini.Set("Categories", category.Key, category.Value);
		}
		pending.Block.CustomData = ini.ToString();
		if (pending.Block is MyProgrammableBlock block && pending.Block.IsWorking)
		{
			block.Run("configReload", UpdateType.Terminal);
		}
		construct.AddLog($"SHEPHERD export: put back {missing.Count} [Categories] line(s) a save of its own had dropped, and reloaded it");
	}

	public static Plan Build(Construct construct, GridRules rules)
	{
		Plan plan = new Plan { Block = construct.Shepherd?.Block };
		long me = MyAPIGateway.Session?.Player?.IdentityId ?? 0;
		if (plan.Block == null)
		{
			plan.Problem = "No SHEPHERD programmable block on this grid.";
			return plan;
		}
		if (plan.Block.OwnerId != me)
		{
			plan.Problem = $"{plan.Block.CustomName} isn't yours, so its Custom Data can't be written.";
			return plan;
		}
		rules ??= new GridRules();
		List<LiveBlock> own = construct.Blocks.Where(b => b.Unit == construct.CoreUnit && b.Block.OwnerId == me && !b.Block.Closed).ToList();
		HashSet<string> pools = new HashSet<string>(StringComparer.Ordinal);
		bool bottles = false;
		foreach (LiveBlock block in own.Where(b => b.Input != null && b.Tag == null))
		{
			int priority = block.Rules?.Priority ?? 0;
			string priorityText = priority > 0 ? ":#" + priority : "";
			if (block.Role == Effective.Storage && block.Rules != null && block.Rules.Accept.Count > 0)
			{
				List<string> tokens = new List<string>();
				foreach (string accept in block.Rules.Accept)
				{
					if (Enum.TryParse(accept, out ItemCategory category))
					{
						tokens.Add(CategoryTokens[(int)category]);
						bottles |= category == ItemCategory.Bottle;
					}
					else
					{
						// A single item gets a category of its own, named with its type (IronIngot, not Iron, which the ore would share).
						string token = ItemToken(accept);
						plan.Categories[accept] = token;
						plan.SingleItems++;
						tokens.Add(token);
					}
				}
				Rename(plan, block, $"[SHEPHERD:{string.Join(",", tokens.Distinct())}{priorityText}]");
				plan.Containers++;
			}
			else if (block.Role == Effective.Storage && block.Kind == BlockKind.Cargo)
			{
				// General storage takes anything: the script's catch-all.
				Rename(plan, block, $"[SHEPHERD:*{priorityText}]");
				plan.CatchAll++;
			}
			else if (block.Role == Effective.Stock)
			{
				List<KeyValuePair<string, string>> lines = (block.Rules?.Limits ?? new List<ItemLimit>())
					.Select(l => new KeyValuePair<string, string>(l.Item, StockLine(l)))
					.Where(l => l.Value != null)
					.ToList();
				plan.MaxOnly += lines.Count(l => l.Value.EndsWith("L", StringComparison.Ordinal));
				string pool = Unique(pools, Token(block.Name));
				Rename(plan, block, $"[SHEPHERD-RESERVE:{pool}]");
				plan.Sections["Reserve:" + pool] = lines;
				plan.Reserves++;
			}
			else if (block.Role == Effective.Manual && block.Rules?.Role == BlockRole.Manual)
			{
				Rename(plan, block, "[SHEPHERD-Off]");
				plan.Off++;
			}
		}
		if (bottles)
		{
			// The script files bottles under Tools; a container accepting bottles gets them through a category of their own.
			foreach (string key in Items.Catalog.Where(k => Items.Category(k) == ItemCategory.Bottle))
			{
				plan.Categories[key] = "Bottles";
			}
		}
		if (rules.Autocraft)
		{
			List<KeyValuePair<string, string>> quotas = new List<KeyValuePair<string, string>>();
			int rangesCut = 0;
			int lowered = 0;
			int ingotsKept = 0;
			// SHEPHERD adds up every pool's lines for an item, and SSM's grid quota already counts what the stock blocks
			// keep, so each quota line is written less what the other pools keep (the stock blocks exported now, and
			// pools already in its Custom Data that this export doesn't replace).
			List<ShepherdQuota> others = plan.Sections
				.SelectMany(section => section.Value.Select(line => ShepherdQuota.Parse(section.Key.Substring(8), line.Key, line.Value)))
				.Concat((construct.Shepherd?.Config ?? new ShepherdConfig()).Quotas.Where(q => q.Pool != QuotaPool && !plan.Sections.ContainsKey("Reserve:" + q.Pool)))
				.Where(q => q != null && q.Mode != "All")
				.ToList();
			bool refines = (construct.Shepherd?.Config ?? new ShepherdConfig()).RefineryFillPct > 0.0;
			foreach (ItemLimit quota in rules.Quotas.Where(q => !q.Item.StartsWith("Ore/", StringComparison.Ordinal)))
			{
				if (!refines && Items.RefineryOnly(quota.Item))
				{
					ingotsKept++;
					continue;
				}
				double kept = others.Where(q => q.Item == quota.Item && q.Mode != "L").Sum(q => q.Target);
				double held = others.Where(q => q.Item == quota.Item).Sum(q => q.Target);
				string line = QuotaLine(quota, rules.Disassemble, kept, held, ref rangesCut);
				if (held > 0.0)
				{
					lowered++;
				}
				if (line != null)
				{
					quotas.Add(new KeyValuePair<string, string>(quota.Item, line));
				}
			}
			if (lowered > 0)
			{
				plan.Lowered = lowered;
			}
			if (ingotsKept > 0)
			{
				plan.Notes.Add($"{ingotsKept} ingot quota(s): SHEPHERD has refineryFillPct=0 and feeds no refinery, so Stockpile Manager's refine by ingot need keeps them");
			}
			if (quotas.Count > 0)
			{
				plan.Sections["Reserve:" + QuotaPool] = quotas;
				plan.QuotaLines = quotas.Count;
				AddAssemblers(plan, own);
			}
			if (rangesCut > 0)
			{
				plan.Notes.Add($"the maximum of {rangesCut} quota(s) that also have a lower quota amount (SHEPHERD keeps one target per line, so those are made up to the quota and not disassembled)");
			}
		}
		else if (rules.Quotas.Count > 0)
		{
			plan.Notes.Add("quotas (Autocraft is off here)");
		}
		if (rules.OrePriority.Count > 0)
		{
			plan.Notes.Add("refinery priority (SHEPHERD has none)");
		}
		if (rules.TypeLimits.Any(t => t.Limits.Count > 0))
		{
			plan.Notes.Add("block type limits (SHEPHERD fills machines to a % instead)");
		}
		if (construct.Units.Count > 1)
		{
			plan.Notes.Add("docked ships");
		}
		return plan;
	}

	/// <summary>SSM's Main and Co-op assemblers become the script's Main and Coop, unless it already has a Main here.</summary>
	private static void AddAssemblers(Plan plan, List<LiveBlock> own)
	{
		if (own.Any(b => b.Tag?.Main == true))
		{
			return;
		}
		List<LiveBlock> mains = own.Where(b => b.Kind == BlockKind.Assembler && b.Tag == null && b.AssemblerMode == TimShared.AssemblerMode.Main).ToList();
		List<LiveBlock> coops = own.Where(b => b.Kind == BlockKind.Assembler && b.Tag == null && b.AssemblerMode == TimShared.AssemblerMode.Coop).ToList();
		if (mains.Count == 0 && coops.Count > 0)
		{
			mains.Add(coops[0]);
			coops.RemoveAt(0);
		}
		if (mains.Count == 0)
		{
			plan.Notes.Add("assemblers (none is Main or Co-op here: tag one [SHEPHERD-Main] so SHEPHERD can craft)");
			return;
		}
		// The script wants exactly one Main per grid; the other Mains help it.
		Rename(plan, mains[0], "[SHEPHERD-Main]");
		foreach (LiveBlock coop in mains.Skip(1).Concat(coops))
		{
			Rename(plan, coop, "[SHEPHERD-Coop]");
		}
		plan.Assemblers = mains.Count + coops.Count;
	}

	/// <summary>A Stock block's limit as a reserve line: a minimum only is kept at least (M), a maximum only at most (L), both at the maximum.</summary>
	private static string StockLine(ItemLimit limit)
	{
		if (limit.HasMax)
		{
			return Number(limit.Max) + (limit.HasMin ? "" : "L");
		}
		return limit.HasMin ? Number(limit.Min) + "M" : null;
	}

	/// <summary>
	/// A grid quota as an A line. MA crafts up to the amount and never disassembles; LA disassembles above it and never
	/// crafts; A does both at one amount. So a quota is made up to (MA), a maximum alone with Disassemble surplus on is
	/// taken apart above (LA), and a quota equal to its maximum does both (A). A quota with a higher maximum has no
	/// single line: it keeps the MA, and <paramref name="rangesCut"/> counts it for the confirm box.
	/// </summary>
	/// <param name="kept">What other pools keep of the item (plain and M lines): taken off the amount it's made up to.</param>
	/// <param name="held">What other pools hold back from disassembly (L lines too): taken off the amount it's taken apart above.</param>
	private static string QuotaLine(ItemLimit quota, bool disassemble, double kept, double held, ref int rangesCut)
	{
		// Ingots are never disassembled: an ingot LA would do nothing but show a standing line on SHEPHERD's panel.
		bool canDisassemble = disassemble && quota.HasMax && !Items.RefineryOnly(quota.Item) && !quota.Item.StartsWith("Ingot/", StringComparison.Ordinal);
		if (quota.HasMin && quota.Min > 0.0)
		{
			if (canDisassemble && Math.Abs(quota.Max - quota.Min) < 1e-6 && Math.Abs(kept - held) < 1e-6)
			{
				return Amount(quota.Min - kept, "A");
			}
			if (canDisassemble)
			{
				rangesCut++;
			}
			return Amount(quota.Min - kept, "MA");
		}
		return canDisassemble ? Amount(quota.Max - held, "LA") : null;
	}

	/// <summary>An amount with its letters, or null when nothing is left once the other pools are counted.</summary>
	private static string Amount(double value, string letters)
	{
		return value > 1e-6 ? Number(value) + letters : null;
	}

	private static string Number(double value)
	{
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	/// <summary>A category name for one item, with its type so items of different types don't share it: Ingot/Iron is IronIngot.</summary>
	private static string ItemToken(string key)
	{
		int slash = key.IndexOf('/');
		return slash < 0 ? Token(key) : Token(key.Substring(slash + 1)) + Token(key.Substring(0, slash));
	}

	/// <summary>
	/// A category or pool name: letters and digits only. A tag ends at the first ], a category list splits at commas, a
	/// trailing :#N is read as a priority, and a pool name is also a Custom Data section header.
	/// </summary>
	private static string Token(string name)
	{
		string token = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray());
		return token.Length == 0 ? "Item" : token;
	}

	private static string Unique(HashSet<string> used, string name)
	{
		string candidate = name;
		for (int i = 2; !used.Add(candidate); i++)
		{
			candidate = name + i;
		}
		return candidate;
	}

	private static void Rename(Plan plan, LiveBlock block, string tag)
	{
		plan.Renames.Add(new KeyValuePair<IMyTerminalBlock, string>(block.Block, block.Name.TrimEnd() + " " + tag));
	}

	/// <summary>What the confirmation box says will happen.</summary>
	public static string Describe(Plan plan)
	{
		StringBuilder text = new StringBuilder();
		text.Append("Block names to change:\n");
		text.Append($"- {plan.Containers} sorted container(s) get [SHEPHERD:categories]\n");
		text.Append($"- {plan.CatchAll} general storage container(s) get [SHEPHERD:*]\n");
		text.Append($"- {plan.Reserves} Stock block(s) get [SHEPHERD-RESERVE:name]\n");
		text.Append($"- {plan.Off} Manual block(s) get [SHEPHERD-Off]\n");
		text.Append($"- {plan.Assemblers} assembler(s) get [SHEPHERD-Main] or [SHEPHERD-Coop]\n");
		text.Append($"\nCustom Data of {plan.Block.CustomName}: {plan.QuotaLines} quota line(s) in [Reserve:{QuotaPool}], {plan.Reserves} reserve section(s), {plan.Categories.Count} category line(s). Sections with these names are replaced; everything else stays.\n");
		text.Append("\nThen it runs the block with configReload, and every job is left to SHEPHERD (the Overview lets you keep some).");
		if (plan.SingleItems > 0)
		{
			text.Append($"\n\n{plan.SingleItems} single accepted item(s) get a category of their own: SHEPHERD then sends each only to its own containers (then the catch-all), never to its usual category's containers.");
		}
		if (plan.MaxOnly > 0)
		{
			text.Append($"\n\n{plan.MaxOnly} Stock limit(s) have only a maximum: Stockpile Manager only caps those, but SHEPHERD's L line also pulls items in up to it.");
		}
		if (plan.Lowered > 0)
		{
			text.Append($"\n\n{plan.Lowered} quota(s) are also kept by reserve pools: SHEPHERD adds pools up, so their quota lines are lowered by what those pools keep.");
		}
		if (plan.Notes.Count > 0)
		{
			text.Append("\n\nNot exported: " + string.Join(", ", plan.Notes) + ".");
		}
		text.Append("\n\nBlocks that already have a SHEPHERD tag are left as they are. Go ahead?");
		return text.ToString();
	}

	/// <summary>Sends the renames and the Custom Data, runs configReload, and hands every job to the script.</summary>
	public static void Apply(Construct construct, GridRules rules, Plan plan)
	{
		foreach (KeyValuePair<IMyTerminalBlock, string> rename in plan.Renames)
		{
			if (!rename.Key.Closed)
			{
				rename.Key.CustomName = rename.Value;
				construct.AddLog($"Renamed for SHEPHERD: {rename.Value}");
			}
		}
		MyIni ini = new MyIni();
		if (!ini.TryParse(plan.Block.CustomData ?? ""))
		{
			construct.AddLog($"SHEPHERD export: {plan.Block.CustomName}'s Custom Data couldn't be read, so it wasn't changed");
		}
		else
		{
			foreach (KeyValuePair<string, string> category in plan.Categories)
			{
				ini.Set("Categories", category.Key, category.Value);
			}
			foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> section in plan.Sections)
			{
				ini.DeleteSection(section.Key);
				foreach (KeyValuePair<string, string> line in section.Value)
				{
					ini.Set(section.Key, line.Key, line.Value);
				}
			}
			plan.Block.CustomData = ini.ToString();
			construct.AddLog($"SHEPHERD export: wrote {plan.QuotaLines} quota line(s), {plan.Reserves} reserve section(s) and {plan.Categories.Count} category line(s) to {plan.Block.CustomName}");
			// The script reads its Custom Data only at start and on configReload.
			if (plan.Block is MyProgrammableBlock block && plan.Block.IsWorking)
			{
				block.Run("configReload", UpdateType.Terminal);
				construct.AddLog($"Ran {plan.Block.CustomName} with configReload");
				// Before 1.0.21 a save SHEPHERD still had pending could rewrite [Categories] from memory and drop the new lines.
				if (plan.Categories.Count > 0 && !ShepherdPanel.AtLeast(construct.Shepherd?.Version, 1, 0, 21))
				{
					s_pending = new Pending { Construct = construct, Block = plan.Block, Categories = plan.Categories, Due = SsmSession.Now + RecheckSeconds };
				}
			}
		}
		rules.ShepherdMine.Clear();
		Store.SaveSettings();
	}
}
