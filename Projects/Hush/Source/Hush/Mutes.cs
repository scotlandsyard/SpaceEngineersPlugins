using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Hush;

/// <summary>
/// What's muted and the emitters that can be muted. Two kinds of mute:
/// - a block, kept per world as grid EntityId + the block's Min position (both survive a reload);
/// - a block type ("all drills everywhere"), kept for every world, which also covers grids you can't see into.
/// Files (Storage\Hush): Hush_Mutes_&lt;world&gt;.txt with one "grid x y z" line per block, Hush_Settings.txt with one
/// "Type=..." line per muted type. Nothing is stored on a grid or sent anywhere.
/// </summary>
internal static class Mutes
{
	private const string SettingsFile = "Hush_Settings.txt";

	private static readonly object s_lock = new object();

	/// <summary>Every emitter made since the game started; dead ones are dropped now and then.</summary>
	private static readonly List<WeakReference<MyEntity3DSoundEmitter>> s_emitters = new List<WeakReference<MyEntity3DSoundEmitter>>();

	private static int s_addsSincePrune;

	private static readonly HashSet<(long Grid, int X, int Y, int Z)> s_blocks = new HashSet<(long, int, int, int)>();

	private static readonly HashSet<MyObjectBuilderType> s_types = new HashSet<MyObjectBuilderType>();

	/// <summary>Blocks that have tried to play a sound this session (by EntityId), so blocks with sounds no definition names show up too.</summary>
	private static readonly HashSet<long> s_heard = new HashSet<long>();

	private static readonly Dictionary<MyCubeBlockDefinition, bool> s_definitionHasSound = new Dictionary<MyCubeBlockDefinition, bool>();

	private static string s_worldFile;

	private static bool s_settingsLoaded;

	public static List<MyObjectBuilderType> MutedTypes()
	{
		lock (s_lock)
		{
			return s_types.ToList();
		}
	}

	public static void AddEmitter(MyEntity3DSoundEmitter emitter)
	{
		lock (s_lock)
		{
			s_emitters.Add(new WeakReference<MyEntity3DSoundEmitter>(emitter));
			if (++s_addsSincePrune >= 2000)
			{
				s_addsSincePrune = 0;
				s_emitters.RemoveAll(w => !w.TryGetTarget(out _));
			}
		}
	}

	/// <summary>Called on every play attempt: remembers that the block makes sound and says whether it's muted.</summary>
	public static bool OnPlay(MyEntity3DSoundEmitter emitter)
	{
		MyCubeBlock block = BlockOf(emitter.Entity);
		if (block == null)
		{
			return false;
		}
		lock (s_lock)
		{
			s_heard.Add(block.EntityId);
			return IsMutedLocked(block);
		}
	}

	/// <summary>The block an entity belongs to: itself, or the block a subpart (door wing, rotor head part...) hangs from.</summary>
	public static MyCubeBlock BlockOf(MyEntity entity)
	{
		for (int depth = 0; entity != null && depth < 8; depth++)
		{
			if (entity is MyCubeBlock block)
			{
				return block;
			}
			entity = entity.Parent;
		}
		return null;
	}

	public static bool IsMuted(MyCubeBlock block)
	{
		lock (s_lock)
		{
			return IsMutedLocked(block);
		}
	}

	private static bool IsMutedLocked(MyCubeBlock block)
	{
		if (s_types.Count == 0 && s_blocks.Count == 0)
		{
			return false;
		}
		return s_types.Contains(block.BlockDefinition.Id.TypeId) || (s_blocks.Count > 0 && block.CubeGrid != null && s_blocks.Contains(Key(block)));
	}

	public static bool IsBlockMuted(MyCubeBlock block)
	{
		lock (s_lock)
		{
			return block.CubeGrid != null && s_blocks.Contains(Key(block));
		}
	}

	public static bool IsTypeMuted(MyObjectBuilderType type)
	{
		lock (s_lock)
		{
			return s_types.Contains(type);
		}
	}

	private static (long, int, int, int) Key(MyCubeBlock block)
	{
		VRageMath.Vector3I min = block.Min;
		return (block.CubeGrid.EntityId, min.X, min.Y, min.Z);
	}

	/// <summary>Mutes or unmutes blocks, stops the ones just muted, and saves.</summary>
	public static void SetBlocks(IEnumerable<MyCubeBlock> blocks, bool mute)
	{
		List<MyCubeBlock> list = blocks.Where(b => b.CubeGrid != null).ToList();
		lock (s_lock)
		{
			foreach (MyCubeBlock block in list)
			{
				if (mute)
				{
					s_blocks.Add(Key(block));
				}
				else
				{
					s_blocks.Remove(Key(block));
				}
			}
		}
		SaveWorld();
		if (mute)
		{
			HashSet<MyCubeBlock> set = new HashSet<MyCubeBlock>(list);
			StopPlaying(set.Contains);
		}
	}

	public static void SetType(MyObjectBuilderType type, bool mute)
	{
		lock (s_lock)
		{
			if (mute)
			{
				s_types.Add(type);
			}
			else
			{
				s_types.Remove(type);
			}
		}
		SaveSettings();
		if (mute)
		{
			StopPlaying(b => b.BlockDefinition.Id.TypeId == type);
		}
	}

	/// <summary>Stops what's playing now on the matching blocks. The emitters keep their loop, so unmuting lets them start again.</summary>
	private static void StopPlaying(Func<MyCubeBlock, bool> match)
	{
		foreach (MyEntity3DSoundEmitter emitter in LiveEmitters())
		{
			MyCubeBlock block = BlockOf(emitter.Entity);
			if (block != null && emitter.IsPlaying && match(block))
			{
				emitter.StopSound(forced: true, cleanUp: false);
			}
		}
	}

	private static List<MyEntity3DSoundEmitter> LiveEmitters()
	{
		List<MyEntity3DSoundEmitter> live = new List<MyEntity3DSoundEmitter>();
		lock (s_lock)
		{
			s_emitters.RemoveAll(w => !w.TryGetTarget(out _));
			foreach (WeakReference<MyEntity3DSoundEmitter> weak in s_emitters)
			{
				if (weak.TryGetTarget(out MyEntity3DSoundEmitter emitter))
				{
					live.Add(emitter);
				}
			}
		}
		return live;
	}

	/// <summary>Every block in the world that has an emitter and makes sound (or is muted), distinct.</summary>
	public static HashSet<MyCubeBlock> SoundBlocks()
	{
		HashSet<MyCubeBlock> blocks = new HashSet<MyCubeBlock>();
		foreach (MyEntity3DSoundEmitter emitter in LiveEmitters())
		{
			MyCubeBlock block = BlockOf(emitter.Entity);
			if (block != null && !block.Closed && block.CubeGrid != null && !blocks.Contains(block) && MakesSound(block))
			{
				blocks.Add(block);
			}
		}
		return blocks;
	}

	private static bool MakesSound(MyCubeBlock block)
	{
		lock (s_lock)
		{
			if (s_heard.Contains(block.EntityId) || IsMutedLocked(block))
			{
				return true;
			}
		}
		return DefinitionMakesSound(block.BlockDefinition);
	}

	/// <summary>True if blocks of this definition make sound (see DefinitionHasSound); cached.</summary>
	public static bool DefinitionMakesSound(MyCubeBlockDefinition definition)
	{
		if (definition == null)
		{
			return false;
		}
		if (!s_definitionHasSound.TryGetValue(definition, out bool hasSound))
		{
			hasSound = DefinitionHasSound(definition);
			s_definitionHasSound[definition] = hasSound;
		}
		return hasSound;
	}

	/// <summary>
	/// True if the definition names a working sound: any MySoundPair field or property (PrimarySound, ActionSound, a door's
	/// open/close sounds, modded definitions' own), except the damage and destruction sounds every block has. Sound
	/// blocks and jukeboxes count too, since the player picks their sound.
	/// </summary>
	private static bool DefinitionHasSound(MyCubeBlockDefinition definition)
	{
		if (definition is MySoundBlockDefinition)
		{
			return true;
		}
		const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		try
		{
			foreach (FieldInfo field in definition.GetType().GetFields(Flags))
			{
				if (field.FieldType == typeof(MySoundPair) && !IsDamageSound(field.Name) && IsSound(field.GetValue(definition) as MySoundPair))
				{
					return true;
				}
			}
			foreach (PropertyInfo property in definition.GetType().GetProperties(Flags))
			{
				if (property.PropertyType == typeof(MySoundPair) && property.GetIndexParameters().Length == 0 && property.CanRead && !IsDamageSound(property.Name) && IsSound(property.GetValue(definition) as MySoundPair))
				{
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not read the sounds of {definition.Id}: {ex.Message}");
		}
		return false;
	}

	private static bool IsDamageSound(string name)
	{
		return name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Destr", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsSound(MySoundPair pair)
	{
		return pair != null && (!pair.Arcade.IsNull || !pair.Realistic.IsNull);
	}

	/// <summary>"MyObjectBuilder_OxygenGenerator" becomes "Oxygen Generator".</summary>
	public static string TypeName(MyObjectBuilderType type)
	{
		string name = type.ToString();
		const string Prefix = "MyObjectBuilder_";
		if (name.StartsWith(Prefix, StringComparison.Ordinal))
		{
			name = name.Substring(Prefix.Length);
		}
		StringBuilder text = new StringBuilder(name.Length + 4);
		for (int i = 0; i < name.Length; i++)
		{
			char c = name[i];
			if (c == '_')
			{
				text.Append(' ');
				continue;
			}
			if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
			{
				text.Append(' ');
			}
			text.Append(c);
		}
		return text.ToString();
	}

	private static string WorldFile()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		string safe = new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
		return $"Hush_Mutes_{safe}.txt";
	}

	/// <summary>Loads this world's block mutes (and the type mutes, once). Called while the world loads, before blocks start their sounds.</summary>
	public static void LoadWorld()
	{
		LoadSettings();
		lock (s_lock)
		{
			s_blocks.Clear();
			s_heard.Clear();
		}
		s_worldFile = WorldFile();
		try
		{
			if (!PluginStorage.FileExists(s_worldFile, typeof(Mutes)))
			{
				return;
			}
			using TextReader reader = PluginStorage.Read(s_worldFile, typeof(Mutes));
			string line;
			lock (s_lock)
			{
				while ((line = reader.ReadLine()) != null)
				{
					string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length == 4 && long.TryParse(parts[0], out long grid) && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y) && int.TryParse(parts[3], out int z))
					{
						s_blocks.Add((grid, x, y, z));
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not read {s_worldFile}: {ex}");
		}
	}

	public static void UnloadWorld()
	{
		lock (s_lock)
		{
			s_blocks.Clear();
			s_heard.Clear();
		}
		s_worldFile = null;
	}

	/// <summary>
	/// Drops block mutes for grids that no longer exist. Only where the whole world is loaded (single player or hosting);
	/// on a server, far grids just aren't streamed to you yet, so their mutes are kept.
	/// </summary>
	public static int PruneMissingGrids()
	{
		if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer)
		{
			return 0;
		}
		int removed;
		lock (s_lock)
		{
			removed = s_blocks.RemoveWhere(k => !MyAPIGateway.Entities.EntityExists(k.Grid));
		}
		if (removed > 0)
		{
			SaveWorld();
		}
		return removed;
	}

	private static void SaveWorld()
	{
		if (s_worldFile == null)
		{
			return;
		}
		try
		{
			List<(long Grid, int X, int Y, int Z)> keys;
			lock (s_lock)
			{
				keys = s_blocks.ToList();
			}
			using TextWriter writer = PluginStorage.Write(s_worldFile, typeof(Mutes));
			foreach ((long grid, int x, int y, int z) in keys)
			{
				writer.WriteLine($"{grid} {x} {y} {z}");
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not save {s_worldFile}: {ex}");
		}
	}

	private static void LoadSettings()
	{
		if (s_settingsLoaded)
		{
			return;
		}
		s_settingsLoaded = true;
		try
		{
			if (!PluginStorage.FileExists(SettingsFile, typeof(Mutes)))
			{
				return;
			}
			using TextReader reader = PluginStorage.Read(SettingsFile, typeof(Mutes));
			string line;
			lock (s_lock)
			{
				while ((line = reader.ReadLine()) != null)
				{
					int equals = line.IndexOf('=');
					if (equals > 0 && line.Substring(0, equals).Trim() == "Type" && MyObjectBuilderType.TryParse(line.Substring(equals + 1).Trim(), out MyObjectBuilderType type))
					{
						s_types.Add(type);
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not read {SettingsFile}: {ex}");
		}
	}

	private static void SaveSettings()
	{
		try
		{
			List<MyObjectBuilderType> types;
			lock (s_lock)
			{
				types = s_types.ToList();
			}
			using TextWriter writer = PluginStorage.Write(SettingsFile, typeof(Mutes));
			writer.WriteLine("# Hush: block types muted in every world.");
			foreach (MyObjectBuilderType type in types)
			{
				writer.WriteLine($"Type={type}");
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not save {SettingsFile}: {ex}");
		}
	}
}
