using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Utils;

namespace TimShared;

/// <summary>
/// Drop-in replacement for MyAPIGateway.Utilities' FileExists/Read/WriteFileInLocalStorage. The game names the
/// folder after the calling type's DLL, but Pulsar's PluginHub compiles each plugin under a new random assembly
/// name on every rebuild, so settings saved there would be lost after each update. This keeps the folder the game
/// would use for our own DLL name, Storage\&lt;first part of the type's namespace&gt;, which is the plugin's assembly
/// name, so files saved by a plugin installed as a DLL are found again when it's built by the hub.
///
/// Saving is safe against losing a file: a write goes to a temp file that replaces the old one only once it's
/// complete, and a file that failed to load this session is kept as "&lt;name&gt;.unreadable" before it's saved over.
/// </summary>
internal static class PluginStorage
{
	/// <summary>Full paths of files that failed to load this session, so the next save keeps them.</summary>
	private static readonly HashSet<string> s_failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public static bool FileExists(string file, Type callingType)
	{
		string path = GetPath(file, callingType);
		return path != null && File.Exists(path);
	}

	/// <summary>Opens a file for reading. A file that can't be opened counts as failed to load.</summary>
	public static TextReader Read(string file, Type callingType)
	{
		string path = GetPath(file, callingType) ?? throw new FileNotFoundException();
		try
		{
			Stream stream = MyFileSystem.OpenRead(path) ?? throw new FileNotFoundException();
			lock (s_failed)
			{
				s_failed.Remove(path);
			}
			return new StreamReader(stream);
		}
		catch
		{
			MarkFailed(path);
			throw;
		}
	}

	/// <summary>
	/// Reads and deserializes an XML file. If it can't be read or parsed, the exception is passed on and the file
	/// counts as failed to load, so the next save keeps it.
	/// </summary>
	public static T ReadXml<T>(string file, Type callingType)
	{
		string path = GetPath(file, callingType) ?? throw new FileNotFoundException();
		try
		{
			using TextReader reader = Read(file, callingType);
			return MyAPIGateway.Utilities.SerializeFromXML<T>(reader.ReadToEnd());
		}
		catch
		{
			MarkFailed(path);
			throw;
		}
	}

	/// <summary>
	/// Collects the text and saves it when the writer is disposed: to a temp file first, which then replaces the
	/// old file. Nothing is saved if nothing was written, so a save that failed before writing leaves the old file.
	/// </summary>
	public static TextWriter Write(string file, Type callingType)
	{
		return new SafeWriter(GetPath(file, callingType) ?? throw new FileNotFoundException());
	}

	private static void MarkFailed(string path)
	{
		lock (s_failed)
		{
			s_failed.Add(path);
		}
	}

	private static void Save(string path, string text)
	{
		if (text.Length == 0)
			return;
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		bool failed;
		lock (s_failed)
		{
			failed = s_failed.Remove(path);
		}
		if (failed && File.Exists(path))
		{
			string keep = path + ".unreadable";
			if (File.Exists(keep))
				keep += "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
			File.Move(path, keep);
			MyLog.Default.WriteLineAndConsole($"[{Path.GetFileName(Path.GetDirectoryName(path))}] {Path.GetFileName(path)} could not be loaded, kept it as {Path.GetFileName(keep)}");
		}
		string temp = path + ".tmp";
		File.WriteAllText(temp, text, new UTF8Encoding(false));
		if (File.Exists(path))
			File.Replace(temp, path, null);
		else
			File.Move(temp, path);
	}

	/// <summary>Same checks as the game's: no invalid file name characters, no way out of the folder.</summary>
	private static string GetPath(string file, Type callingType)
	{
		if (file.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
			return null;
		string folder = Path.Combine(MyFileSystem.UserDataPath, "Storage", callingType.Namespace.Split('.')[0]);
		string path = Path.Combine(folder, file);
		return Path.GetFullPath(path).StartsWith(folder) ? path : null;
	}

	private sealed class SafeWriter : StringWriter
	{
		private readonly string _path;
		private bool _saved;

		public SafeWriter(string path)
		{
			_path = path;
		}

		protected override void Dispose(bool disposing)
		{
			try
			{
				if (disposing && !_saved)
				{
					_saved = true;
					Save(_path, ToString());
				}
			}
			finally
			{
				base.Dispose(disposing);
			}
		}
	}
}
