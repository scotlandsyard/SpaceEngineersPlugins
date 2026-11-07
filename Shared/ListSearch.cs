using System;
using System.Collections.Generic;

namespace TimShared;

/// <summary>
/// The match behind the Find boxes that shorten our plugins' long lists. Case-insensitive; the search splits on
/// spaces and every word must appear in at least one of the row's text fields, so "large cargo" finds
/// "Cargo Container (Large)". An empty search matches everything. This one file is linked into each plugin that has a
/// Find box (see Shared/README.md), and it knows nothing about the game or the window.
/// </summary>
internal static class ListSearch
{
	public const string Label = "Find";

	public const string ToolTip = "Type part of a name to shorten the list.";

	public const int MaxLength = 60;

	/// <summary>True when the search would hide something, so a bulk action has to care about it.</summary>
	public static bool IsActive(string search)
	{
		return !string.IsNullOrWhiteSpace(search);
	}

	public static bool Matches(string search, params string[] fields)
	{
		return Matches(search, (IEnumerable<string>)fields);
	}

	public static bool Matches(string search, IEnumerable<string> fields)
	{
		if (!IsActive(search))
		{
			return true;
		}
		foreach (string word in search.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
		{
			if (!AnyContains(fields, word))
			{
				return false;
			}
		}
		return true;
	}

	private static bool AnyContains(IEnumerable<string> fields, string word)
	{
		foreach (string field in fields)
		{
			if (field != null && field.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}
}
