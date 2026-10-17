using System;
using System.Globalization;

namespace Tripwire;

/// <summary>One row of the window: an alert from this plugin, or a line from a Tripwire script's log.</summary>
internal class LogEntry
{
	public DateTime Time;

	/// <summary>Raw event kind: SEEN, LEFT, DMG, GRIND, LOST, OWNER, MANY, START (or a newer kind this version doesn't know).</summary>
	public string Kind = "";

	public string Grid = "";

	/// <summary>The block, the visitor, or a summary.</summary>
	public string Subject = "";

	public string Detail = "";

	/// <summary>Who was near: the script's visitors, or (only with the optional setting) names the HUD was showing.</summary>
	public string Nearby = "";

	/// <summary>True for this plugin's own alerts, false for script log lines.</summary>
	public bool IsAlert;

	/// <summary>Script logs: which construct the log came from (for "since your last visit").</summary>
	public long Construct;

	public bool IsNew;

	/// <summary>The line as stored: the script's own line, or the alert log's line.</summary>
	public string Raw = "";

	public string EventText
	{
		get
		{
			switch (Kind)
			{
			case "SEEN": return "Arrived";
			case "LEFT": return "Left";
			case "DMG": return "Damaged";
			case "GRIND": return "Ground";
			case "LOST": return "Lost";
			case "OWNER": return "Owner changed";
			case "MANY": return "Attack";
			case "START": return "Script";
			default: return Kind;
			}
		}
	}

	public string TimeText
	{
		get
		{
			DateTime local = Time.ToLocalTime();
			return local.Date == DateTime.Now.Date ? local.ToString("HH:mm:ss", CultureInfo.CurrentCulture) : local.ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
		}
	}

	/// <summary>One line for a datapad or chat.</summary>
	public override string ToString()
	{
		string text = $"{Time.ToLocalTime():yyyy-MM-dd HH:mm} {Grid}: {EventText} {Subject}";
		if (Detail.Length > 0)
		{
			text += $" ({Detail})";
		}
		if (Nearby.Length > 0)
		{
			text += $" near: {Nearby}";
		}
		return text;
	}
}
