using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GUI;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Tally;

/// <summary>The components a source needs, before any maths: block count and component totals.</summary>
internal class Count
{
	public string Name;

	public string Kind;

	public bool StillNeeded;

	public int Blocks;

	public int Done;

	public int Unknown;

	public readonly Dictionary<MyDefinitionId, double> Components = new Dictionary<MyDefinitionId, double>();

	public void AddBlock(MyCubeBlockDefinition definition)
	{
		if (definition?.Components == null)
		{
			return;
		}
		foreach (MyCubeBlockDefinition.Component component in definition.Components)
		{
			if (component?.Definition != null)
			{
				Add(component.Definition.Id, component.Count);
			}
		}
	}

	public void Add(MyDefinitionId id, double amount)
	{
		Components.TryGetValue(id, out double old);
		Components[id] = old + amount;
	}
}

/// <summary>Something Tally can count: a projector, a blueprint in your F10 library, or a grid you own.</summary>
internal class Source
{
	public const string Projector = "Projector";

	public const string Library = "Blueprint";

	public const string Grid = "Grid";

	public string Kind;

	public string Label;

	/// <summary>The projector's or the grid's entity id.</summary>
	public long EntityId;

	/// <summary>The blueprint's folder (library only).</summary>
	public string Folder;

	public string Key => Kind + ":" + (Folder ?? EntityId.ToString());
}

/// <summary>
/// Finds and counts sources. Only what the player already has: projectors they have terminal access to, grids they
/// own, and their own local blueprint folder. Everything is read from what the game already has loaded or from local
/// files; nothing is asked of the server.
/// </summary>
internal static class Sources
{
	private static long Me => MySession.Static?.LocalPlayerId ?? 0;

	private static Vector3D Here => MySession.Static?.LocalCharacter?.PositionComp.GetPosition() ?? MyAPIGateway.Session?.Camera?.Position ?? Vector3D.Zero;

	private static IEnumerable<MyCubeGrid> Grids()
	{
		return MyEntities.GetEntities().OfType<MyCubeGrid>().Where(g => !g.Closed && !g.MarkedForClose && !g.IsPreview && g.Projector == null).ToList();
	}

	private static string Distance(Vector3D at)
	{
		double metres = Vector3D.Distance(Here, at);
		return metres < 1000 ? $"{metres:0} m" : $"{metres / 1000:0.0} km";
	}

	/// <summary>Projectors with a projection loaded that you have access to, nearest first.</summary>
	public static List<Source> Projectors()
	{
		List<KeyValuePair<double, Source>> found = new List<KeyValuePair<double, Source>>();
		foreach (MyCubeGrid grid in Grids())
		{
			foreach (MyProjectorBase projector in grid.GetFatBlocks<MyProjectorBase>())
			{
				if (projector.Closed || projector.ProjectedGrid == null || !projector.HasLocalPlayerAccess())
				{
					continue;
				}
				Vector3D at = projector.PositionComp.GetPosition();
				found.Add(new KeyValuePair<double, Source>(Vector3D.DistanceSquared(Here, at), new Source
				{
					Kind = Source.Projector,
					EntityId = projector.EntityId,
					Label = $"{projector.ProjectedGrid.DisplayName} (in {projector.DisplayNameText} on {grid.DisplayName}, {Distance(at)})"
				}));
			}
		}
		return found.OrderBy(f => f.Key).Select(f => f.Value).ToList();
	}

	/// <summary>Your grids (each mechanical group once, under its biggest grid), nearest first.</summary>
	public static List<Source> OwnGrids()
	{
		long me = Me;
		HashSet<long> listed = new HashSet<long>();
		List<KeyValuePair<double, Source>> found = new List<KeyValuePair<double, Source>>();
		foreach (MyCubeGrid grid in Grids())
		{
			if (listed.Contains(grid.EntityId) || !grid.BigOwners.Contains(me))
			{
				continue;
			}
			List<MyCubeGrid> group = Group(grid);
			foreach (MyCubeGrid member in group)
			{
				listed.Add(member.EntityId);
			}
			MyCubeGrid main = group.OrderByDescending(g => g.BlocksCount).First();
			Vector3D at = main.PositionComp.WorldAABB.Center;
			int blocks = group.Sum(g => g.BlocksCount);
			found.Add(new KeyValuePair<double, Source>(Vector3D.DistanceSquared(Here, at), new Source
			{
				Kind = Source.Grid,
				EntityId = main.EntityId,
				Label = $"{main.DisplayName} ({blocks} blocks, {Distance(at)})"
			}));
		}
		return found.OrderBy(f => f.Key).Select(f => f.Value).ToList();
	}

	/// <summary>The grids joined to this one by rotors, pistons and hinges.</summary>
	private static List<MyCubeGrid> Group(MyCubeGrid grid)
	{
		List<IMyCubeGrid> group = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, group);
		List<MyCubeGrid> grids = group.OfType<MyCubeGrid>().Where(g => !g.Closed).ToList();
		if (!grids.Contains(grid))
		{
			grids.Add(grid);
		}
		return grids;
	}

	/// <summary>The blueprints in your local F10 folder (sub folders too), by folder name.</summary>
	public static List<Source> Library()
	{
		List<Source> found = new List<Source>();
		string root = MyBlueprintUtils.BLUEPRINT_FOLDER_LOCAL;
		try
		{
			if (Directory.Exists(root))
			{
				Scan(root, root, 0, found);
			}
		}
		catch (Exception)
		{
			// An odd folder (no access, bad name) just isn't listed.
		}
		return found.OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static void Scan(string root, string folder, int depth, List<Source> found)
	{
		foreach (string sub in Directory.GetDirectories(folder))
		{
			if (found.Count >= 5000)
			{
				return;
			}
			if (File.Exists(Path.Combine(sub, MyBlueprintUtils.BLUEPRINT_LOCAL_NAME)))
			{
				found.Add(new Source { Kind = Source.Library, Folder = sub, Label = sub.Substring(root.Length).TrimStart('\\', '/') });
			}
			else if (depth < 4)
			{
				Scan(root, sub, depth + 1, found);
			}
		}
	}

	/// <summary>Finds a projector or grid again by its id (it may have streamed out and back in as a new object).</summary>
	public static MyEntity Entity(Source source)
	{
		return MyEntities.TryGetEntityById(source.EntityId, out MyEntity entity) && !entity.Closed ? entity : null;
	}

	/// <summary>
	/// Counts a projection. Full cost: every projected block. Still needed: projected blocks that aren't built yet,
	/// plus the missing components of built ones that aren't finished, the way the projector itself matches them
	/// (MyProjectorBase.UpdateStats: same position, same block).
	/// </summary>
	public static Count CountProjector(MyProjectorBase projector, bool stillNeeded)
	{
		Count count = new Count { Name = projector.ProjectedGrid?.DisplayName ?? projector.DisplayNameText, Kind = Source.Projector, StillNeeded = stillNeeded };
		List<MyCubeGrid> built = Group(projector.CubeGrid);
		Dictionary<string, int> missing = new Dictionary<string, int>();
		foreach (MyCubeGrid preview in projector.Clipboard.PreviewGrids.ToList())
		{
			foreach (MySlimBlock block in preview.CubeBlocks.ToList())
			{
				count.Blocks++;
				if (!stillNeeded)
				{
					count.AddBlock(block.BlockDefinition);
					continue;
				}
				MySlimBlock real = Built(preview, block, built);
				if (real == null)
				{
					count.AddBlock(block.BlockDefinition);
				}
				else if (!AddMissing(real, missing, count))
				{
					count.Done++;
				}
			}
		}
		return count;
	}

	private static MySlimBlock Built(MyCubeGrid preview, MySlimBlock block, List<MyCubeGrid> built)
	{
		Vector3D world = preview.GridIntegerToWorld(block.Position);
		foreach (MyCubeGrid grid in built)
		{
			if (grid.GridSizeEnum != preview.GridSizeEnum)
			{
				continue;
			}
			MySlimBlock real = grid.GetCubeBlock(grid.WorldToGridInteger(world));
			if (real != null && real.BlockDefinition.Id == block.BlockDefinition.Id)
			{
				return real;
			}
		}
		return null;
	}

	/// <summary>Adds what a built block still lacks to be finished (or repaired). False if it lacks nothing.</summary>
	private static bool AddMissing(MySlimBlock block, Dictionary<string, int> missing, Count count)
	{
		missing.Clear();
		block.GetMissingComponents(missing);
		bool any = false;
		foreach (KeyValuePair<string, int> pair in missing)
		{
			if (pair.Value > 0)
			{
				count.Add(new MyDefinitionId(typeof(MyObjectBuilder_Component), pair.Key), pair.Value);
				any = true;
			}
		}
		return any;
	}

	/// <summary>
	/// Counts one of your grids with what's joined to it by rotors, pistons and hinges (only the parts you own or
	/// nobody owns). Full cost: every block in full. Still needed: what unfinished or damaged blocks lack.
	/// </summary>
	public static Count CountGrid(MyCubeGrid grid, bool stillNeeded)
	{
		long me = Me;
		Count count = new Count { Name = grid.DisplayName, Kind = Source.Grid, StillNeeded = stillNeeded };
		Dictionary<string, int> missing = new Dictionary<string, int>();
		foreach (MyCubeGrid member in Group(grid))
		{
			if (member.BigOwners.Count > 0 && !member.BigOwners.Contains(me))
			{
				continue;
			}
			foreach (MySlimBlock block in member.CubeBlocks.ToList())
			{
				count.Blocks++;
				if (!stillNeeded)
				{
					count.AddBlock(block.BlockDefinition);
				}
				else if (!AddMissing(block, missing, count))
				{
					count.Done++;
				}
			}
		}
		return count;
	}

	/// <summary>Reads a blueprint from your F10 folder and counts it in full. Safe on a background thread.</summary>
	public static Count CountBlueprint(Source source)
	{
		string folder = source.Folder;
		Count count = new Count { Name = Path.GetFileName(folder), Kind = Source.Library };
		MyObjectBuilder_Definitions prefab = MyBlueprintUtils.LoadPrefab(Path.Combine(folder, MyBlueprintUtils.BLUEPRINT_LOCAL_NAME));
		MyObjectBuilder_ShipBlueprintDefinition blueprint = prefab?.ShipBlueprints?.FirstOrDefault();
		if (blueprint?.CubeGrids == null)
		{
			throw new InvalidOperationException("the blueprint file couldn't be read");
		}
		foreach (MyObjectBuilder_CubeGrid grid in blueprint.CubeGrids)
		{
			foreach (MyObjectBuilder_CubeBlock block in grid?.CubeBlocks ?? new List<MyObjectBuilder_CubeBlock>())
			{
				count.Blocks++;
				if (MyDefinitionManager.Static.TryGetCubeBlockDefinition(block.GetId(), out MyCubeBlockDefinition definition))
				{
					count.AddBlock(definition);
				}
				else
				{
					count.Unknown++;
				}
			}
		}
		return count;
	}

	/// <summary>What's in your own inventory (always up to date: the server keeps your client in step with it).</summary>
	public static void AddCarried(Dictionary<MyDefinitionId, double> have)
	{
		AddInventory(MySession.Static?.LocalCharacter?.GetInventory(0) as MyInventory, have);
	}

	/// <summary>
	/// What's in the inventories of a grid and everything docked to it, in blocks you have access to. The server only
	/// keeps these up to date on your PC while you have one of them open in the terminal, so they can be out of date.
	/// </summary>
	public static void AddCargo(MyCubeGrid grid, Dictionary<MyDefinitionId, double> have)
	{
		List<IMyCubeGrid> group = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, group);
		foreach (MyCubeGrid member in group.OfType<MyCubeGrid>())
		{
			foreach (MyCubeBlock block in member.GetFatBlocks())
			{
				if (!block.HasInventory || !(block is MyTerminalBlock terminal) || !terminal.HasLocalPlayerAccess())
				{
					continue;
				}
				for (int i = 0; i < block.InventoryCount; i++)
				{
					AddInventory(block.GetInventory(i) as MyInventory, have);
				}
			}
		}
	}

	private static void AddInventory(MyInventory inventory, Dictionary<MyDefinitionId, double> have)
	{
		if (inventory == null)
		{
			return;
		}
		foreach (MyPhysicalInventoryItem item in inventory.GetItems())
		{
			if (item.Content == null)
			{
				continue;
			}
			MyDefinitionId id = item.Content.GetId();
			have.TryGetValue(id, out double old);
			have[id] = old + (double)item.Amount;
		}
	}

	/// <summary>The grid whose cargo counts for a projector or grid source.</summary>
	public static MyCubeGrid CargoGrid(Source source)
	{
		MyEntity entity = Entity(source);
		return entity is MyProjectorBase projector ? projector.CubeGrid : entity as MyCubeGrid;
	}
}
