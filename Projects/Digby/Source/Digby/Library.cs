using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;
using VRageMath;

namespace Digby;

/// <summary>A point kept in Digby's library: plan markers saved for later, or markers copied from another plugin's library.</summary>
public class LibraryEntry
{
	public string Name;

	/// <summary>Where it came from: "Plan: 120 m under Gold", "OreScout", "Typed"...</summary>
	public string Source;

	public double X;

	public double Y;

	public double Z;

	public uint Color;

	public DateTime Saved;

	[XmlIgnore]
	public Vector3D Position => new Vector3D(X, Y, Z);
}

public class LibraryFile
{
	public List<LibraryEntry> Entries = new List<LibraryEntry>();
}

/// <summary>Digby's own marker library, one XML file per world (Digby_&lt;world&gt;_library.xml).</summary>
internal static class Library
{
	public const string MarkerKind = "Lib";

	private static LibraryFile s_file;

	private static string s_world;

	private static string FileName => $"Digby_{Settings.World}_library.xml";

	public static List<LibraryEntry> Entries
	{
		get
		{
			EnsureLoaded();
			return s_file.Entries;
		}
	}

	private static void EnsureLoaded()
	{
		if (s_file != null && s_world == Settings.World)
		{
			return;
		}
		s_world = Settings.World;
		s_file = new LibraryFile();
		try
		{
			if (PluginStorage.FileExists(FileName, typeof(Library)))
			{
				s_file = PluginStorage.ReadXml<LibraryFile>(FileName, typeof(Library)) ?? new LibraryFile();
				s_file.Entries ??= new List<LibraryEntry>();
				s_file.Entries.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Name));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Digby] Could not read {FileName}: {ex.Message}");
		}
	}

	public static void Save()
	{
		EnsureLoaded();
		try
		{
			using TextWriter writer = PluginStorage.Write(FileName, typeof(Library));
			writer.Write(MyAPIGateway.Utilities.SerializeToXML(s_file));
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Digby] Could not save {FileName}: {ex.Message}");
		}
	}

	/// <summary>
	/// Adds a point, or updates the one with the same name if <paramref name="replaceSameName"/>; otherwise a taken
	/// name gets " #2", " #3"... Doesn't save; call <see cref="Save"/> after a batch.
	/// </summary>
	public static LibraryEntry Add(string name, Vector3D position, string source, Color color, bool replaceSameName)
	{
		EnsureLoaded();
		name = Clean(name);
		LibraryEntry same = Find(name);
		if (same != null && replaceSameName)
		{
			same.X = position.X;
			same.Y = position.Y;
			same.Z = position.Z;
			same.Source = source;
			same.Color = color.PackedValue;
			same.Saved = DateTime.Now;
			return same;
		}
		if (same != null)
		{
			int n = 2;
			while (Find($"{name} #{n}") != null)
			{
				n++;
			}
			name = $"{name} #{n}";
		}
		LibraryEntry entry = new LibraryEntry { Name = name, Source = source, X = position.X, Y = position.Y, Z = position.Z, Color = color.PackedValue, Saved = DateTime.Now };
		s_file.Entries.Add(entry);
		return entry;
	}

	/// <summary>True if an entry with this name already sits within a metre of the point (so a copy would be a duplicate).</summary>
	public static bool Has(string name, Vector3D position)
	{
		name = Clean(name);
		return Entries.Any(e => (e.Name == name || e.Name.StartsWith(name + " #", StringComparison.Ordinal)) && Vector3D.DistanceSquared(e.Position, position) < 1.0);
	}

	public static LibraryEntry Find(string name)
	{
		return Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Exact name first, then the first name starting with it.</summary>
	public static LibraryEntry Match(string spec)
	{
		return Find(spec) ?? Entries.Where(e => e.Name.StartsWith(spec, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
	}

	public static void Remove(LibraryEntry entry)
	{
		Entries.Remove(entry);
		Save();
	}

	/// <summary>Shows entries as local markers (never sent to the server). Returns how many were added.</summary>
	public static int Show(IEnumerable<LibraryEntry> entries)
	{
		return Markers.AddLocal(MarkerKind, entries.Select(e => new Marker { Key = e.Name, Name = e.Name, Position = e.Position, Color = e.Color == 0 ? new Color(255, 200, 60) : new Color(e.Color) }), "Digby library");
	}

	public static string Clean(string name)
	{
		string clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
		return clean.Length == 0 ? "Point" : clean.Length > 80 ? clean.Substring(0, 80) : clean;
	}

	public static void Unload()
	{
		s_file = null;
		s_world = null;
	}
}
