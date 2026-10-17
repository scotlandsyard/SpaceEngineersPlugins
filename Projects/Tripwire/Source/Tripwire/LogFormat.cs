using System;
using System.Globalization;

namespace Tripwire;

/// <summary>
/// Reads the Tripwire script's log lines (Script/LOG FORMAT.md). Version 1 lines start with "TW1|"; lines of an unknown
/// version are skipped, and unknown event kinds are kept as they are so a newer script still shows something.
/// </summary>
internal static class LogFormat
{
	public const string Marker = "--- Tripwire log ---";

	public const string Prefix = "TW1|";

	public static bool TryParseTime(string text, out DateTime time)
	{
		return DateTime.TryParseExact(text, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time);
	}

	/// <summary>Parses one script line; <paramref name="logGrid"/> names the grid for visitor lines, which don't carry one.</summary>
	public static LogEntry Parse(string line, string logGrid)
	{
		if (line == null || !line.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return null;
		}
		string[] f = line.Split('|');
		if (f.Length < 3 || !TryParseTime(f[1], out DateTime time))
		{
			return null;
		}
		LogEntry entry = new LogEntry { Time = time, Kind = f[2], Grid = logGrid ?? "", Raw = line };
		switch (f[2])
		{
		case "SEEN":
		case "LEFT":
			if (f.Length < 8)
			{
				return null;
			}
			// SEEN|visitor|relation|type|name|source and LEFT|visitor|relation|type|name|minutes
			entry.Subject = $"{f[3]} {f[6]}";
			entry.Detail = f[2] == "SEEN" ? $"{f[4]} {TypeText(f[5])}, seen by {f[7]}" : $"{f[4]} {TypeText(f[5])}, after {f[7]} min";
			break;
		case "DMG":
		case "GRIND":
		case "LOST":
		case "OWNER":
		case "MANY":
			if (f.Length < 8)
			{
				return null;
			}
			// <kind>|grid|block|x,y,z|percent|present
			entry.Grid = f[3];
			entry.Subject = f[4];
			entry.Detail = f[6].Length > 0 ? (f[2] == "MANY" ? $"lowest {f[6]}%" : $"{f[6]}%") : "";
			entry.Nearby = Present(f[7]);
			break;
		case "START":
			entry.Subject = f.Length > 3 ? f[3] : "";
			break;
		default:
			entry.Subject = f.Length > 3 ? f[3] : "";
			entry.Detail = f.Length > 4 ? string.Join(" ", f, 4, f.Length - 4) : "";
			break;
		}
		return entry;
	}

	private static string TypeText(string type)
	{
		switch (type)
		{
		case "LargeGrid": return "large grid";
		case "SmallGrid": return "small grid";
		case "NPC": return "NPC";
		case "Human": return "player";
		default: return type;
		}
	}

	/// <summary>"V3:Human;V7:Large Grid" -> "V3 Human, V7 Large Grid".</summary>
	private static string Present(string present)
	{
		return present.Replace(':', ' ').Replace(";", ", ");
	}
}
