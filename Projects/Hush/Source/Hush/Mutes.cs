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
using VRage.Audio;
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

	/// <summary>
	/// Blocks that have tried to play a working sound this session (EntityId to the cue's name), so blocks with sounds no
	/// definition names show up too. Damage and destroy sounds don't count: every block has those.
	/// </summary>
	private static readonly Dictionary<long, string> s_heard = new Dictionary<long, string>();

	private static readonly Dictionary<MyCubeBlockDefinition, DefinitionSounds> s_definitionSounds = new Dictionary<MyCubeBlockDefinition, DefinitionSounds>();

	private static string s_worldFile;

	private static bool s_settingsLoaded;

	/// <summary>Hush's chat level and chat name, saved in Hush_Settings.txt with the type mutes.</summary>
	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static string DisplayName = "";

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

	/// <summary>
	/// Called on every play attempt: remembers that the block makes a working sound (not for its definition's damage or
	/// destroy sounds) and says whether it's muted. <paramref name="cue"/> is null for buffered (recorded) audio.
	/// </summary>
	public static bool OnPlay(MyEntity3DSoundEmitter emitter, MyCueId? cue)
	{
		MyCubeBlock block = BlockOf(emitter.Entity);
		if (block == null)
		{
			return false;
		}
		bool working = cue == null || (!cue.Value.IsNull && !(SoundsOf(block.BlockDefinition)?.Damage.Contains(cue.Value) ?? false));
		lock (s_lock)
		{
			if (working && !s_heard.ContainsKey(block.EntityId))
			{
				s_heard[block.EntityId] = cue == null ? "recorded" : cue.Value.Hash.String;
			}
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
		return Why(block) != null;
	}

	/// <summary>
	/// Why a block is listed, or null if it isn't: "Sound: cue" (its type has a working sound), "Played: cue" (it played a
	/// working sound since the world loaded; "Played: recorded" for buffered audio), or "Muted" (muted, nothing else known).
	/// </summary>
	public static string Why(MyCubeBlock block)
	{
		DefinitionSounds sounds = SoundsOf(block.BlockDefinition);
		if (sounds?.Working != null)
		{
			return "Sound: " + sounds.Working;
		}
		lock (s_lock)
		{
			if (s_heard.TryGetValue(block.EntityId, out string cue))
			{
				return "Played: " + cue;
			}
			if (IsMutedLocked(block))
			{
				return "Muted";
			}
		}
		return null;
	}

	/// <summary>True if blocks of this definition have a working sound (see ReadSounds); cached.</summary>
	public static bool DefinitionMakesSound(MyCubeBlockDefinition definition)
	{
		return SoundsOf(definition)?.Working != null;
	}

	/// <summary>The definition's first working sound, as the Types view shows it, or "".</summary>
	public static string WorkingSoundName(MyCubeBlockDefinition definition)
	{
		return SoundsOf(definition)?.Working ?? "";
	}

	/// <summary>What a definition's sounds are: the first working one by name, and the damage/destroy cues to ignore.</summary>
	private class DefinitionSounds
	{
		public string Working;

		public readonly List<MyCueId> Damage = new List<MyCueId>();
	}

	private static DefinitionSounds SoundsOf(MyCubeBlockDefinition definition)
	{
		if (definition == null)
		{
			return null;
		}
		lock (s_lock)
		{
			if (!s_definitionSounds.TryGetValue(definition, out DefinitionSounds sounds))
			{
				sounds = ReadSounds(definition);
				s_definitionSounds[definition] = sounds;
			}
			return sounds;
		}
	}

	/// <summary>
	/// Reads every MySoundPair field and property of the definition. Working sounds are all of them (PrimarySound,
	/// ActionSound, a door's open/close sounds, modded definitions' own) except the damage and destruction sounds every
	/// block has; those are kept separately so OnPlay can ignore them. Sound blocks and jukeboxes count as working,
	/// since the player picks their sound.
	/// </summary>
	private static DefinitionSounds ReadSounds(MyCubeBlockDefinition definition)
	{
		DefinitionSounds sounds = new DefinitionSounds();
		if (definition is MySoundBlockDefinition)
		{
			sounds.Working = "sound block";
		}
		const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		try
		{
			foreach (FieldInfo field in definition.GetType().GetFields(Flags))
			{
				if (field.FieldType == typeof(MySoundPair))
				{
					AddSound(sounds, field.Name, field.GetValue(definition) as MySoundPair);
				}
			}
			foreach (PropertyInfo property in definition.GetType().GetProperties(Flags))
			{
				if (property.PropertyType == typeof(MySoundPair) && property.GetIndexParameters().Length == 0 && property.CanRead)
				{
					AddSound(sounds, property.Name, property.GetValue(definition) as MySoundPair);
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not read the sounds of {definition.Id}: {ex.Message}");
		}
		return sounds;
	}

	private static void AddSound(DefinitionSounds sounds, string memberName, MySoundPair pair)
	{
		if (pair == null || (pair.Arcade.IsNull && pair.Realistic.IsNull))
		{
			return;
		}
		if (IsDamageSound(memberName))
		{
			sounds.Damage.Add(pair.Arcade);
			sounds.Damage.Add(pair.Realistic);
		}
		else if (sounds.Working == null)
		{
			sounds.Working = pair.GetCueName();
		}
	}

	private static bool IsDamageSound(string name)
	{
		return name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Destr", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0;
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
					if (equals <= 0)
					{
						continue;
					}
					string key = line.Substring(0, equals).Trim();
					string value = line.Substring(equals + 1).Trim();
					if (key == "Type" && MyObjectBuilderType.TryParse(value, out MyObjectBuilderType type))
					{
						s_types.Add(type);
					}
					else if (key == "Personality" && Enum.TryParse(value, true, out Personality.Chattiness level) && Enum.IsDefined(typeof(Personality.Chattiness), level))
					{
						Chattiness = level;
					}
					else if (key == "DisplayName")
					{
						DisplayName = value;
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not read {SettingsFile}: {ex}");
		}
	}

	/// <summary>Saves the type mutes and the chat settings (Hush_Settings.txt, every world).</summary>
	public static void SaveSettings()
	{
		try
		{
			List<MyObjectBuilderType> types;
			lock (s_lock)
			{
				types = s_types.ToList();
			}
			using TextWriter writer = PluginStorage.Write(SettingsFile, typeof(Mutes));
			writer.WriteLine("# Hush: block types muted in every world, and the chat settings.");
			writer.WriteLine($"Personality={Chattiness}");
			writer.WriteLine($"DisplayName={DisplayName}");
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
