using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Utils;
using VRageMath;

namespace Digby;

/// <summary>
/// Other plugins' marker libraries, read (never written) from their own files so Digby needs no reference to them:
/// OreScout keeps its library in Storage\OreScout\OreScout_Markers_&lt;world&gt;.xml. Markers are copied into Digby's
/// library on request. A source shows only when its file exists for this world.
/// </summary>
internal static class ForeignLibraries
{
	public class Source
	{
		public string Name;

		/// <summary>The plugin's folder under %AppData%\SpaceEngineers\Storage.</summary>
		public string Folder;

		/// <summary>File name before the world name; the file is &lt;prefix&gt;&lt;world&gt;.xml.</summary>
		public string FilePrefix;

		public string Path => System.IO.Path.Combine(MyFileSystem.UserDataPath, "Storage", Folder, FilePrefix + Settings.World + ".xml");
	}

	/// <summary>The sources that have a library for this world.</summary>
	public static List<Source> Available()
	{
		List<Source> sources = new List<Source> { new Source { Name = "OreScout", Folder = "OreScout", FilePrefix = "OreScout_Markers_" } };
		return sources.Where(s => File.Exists(s.Path)).ToList();
	}

	/// <summary>The source's markers as Digby entries (names made from their type, label and info). Empty if unreadable.</summary>
	public static List<LibraryEntry> Read(Source source)
	{
		List<LibraryEntry> entries = new List<LibraryEntry>();
		try
		{
			ForeignFile file = MyAPIGateway.Utilities.SerializeFromXML<ForeignFile>(File.ReadAllText(source.Path));
			foreach (ForeignEntry entry in file?.Entries ?? new List<ForeignEntry>())
			{
				string name = ((entry.Prefix ?? "") + " " + (entry.Label ?? "")).Trim();
				string info = Info(entry.Details);
				if (info.Length > 0)
				{
					name += " (" + info + ")";
				}
				entries.Add(new LibraryEntry { Name = Library.Clean(name), Source = source.Name, X = entry.X, Y = entry.Y, Z = entry.Z, Color = entry.Color, Saved = entry.Saved });
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Digby] Could not read the {source.Name} library: {ex.Message}");
		}
		return entries;
	}

	/// <summary>Copies entries into Digby's library, skipping ones already there. Returns how many were added.</summary>
	public static int CopyToLibrary(IEnumerable<LibraryEntry> entries)
	{
		int added = 0;
		foreach (LibraryEntry entry in entries)
		{
			if (!Library.Has(entry.Name, entry.Position))
			{
				Library.Add(entry.Name, entry.Position, entry.Source, new Color(entry.Color), replaceSameName: false);
				added++;
			}
		}
		if (added > 0)
		{
			Library.Save();
		}
		return added;
	}

	/// <summary>The details without the ore amount, e.g. "3 asteroids".</summary>
	private static string Info(string details)
	{
		string text = System.Text.RegularExpressions.Regex.Replace(details ?? "", @",?\s*~([0-9][0-9.,\s  ]*)\s*kg", "");
		return text.Trim(' ', ',');
	}
}

/// <summary>The parts of another plugin's library file that Digby reads; anything else in it is ignored.</summary>
[XmlRoot("LibraryFile")]
public class ForeignFile
{
	[XmlArray("Entries")]
	[XmlArrayItem("LibraryEntry")]
	public List<ForeignEntry> Entries = new List<ForeignEntry>();
}

public class ForeignEntry
{
	public string Key;

	public string Prefix;

	public string Label;

	public string Details;

	public string Description;

	public double X;

	public double Y;

	public double Z;

	public uint Color;

	public double MassKg;

	public DateTime Saved;
}
