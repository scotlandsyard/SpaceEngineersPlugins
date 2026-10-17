using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.Entity;

namespace Tripwire;

/// <summary>
/// Writes log rows onto an empty datapad in your character's inventory, as a record you can keep or hand over. It
/// sends the same request the game's own datapad editor sends when you press OK, so the server checks it the same way.
/// It never overwrites a datapad that already has text.
/// </summary>
internal static class Datapad
{
	/// <summary>Kept well below what a datapad can hold, so the request stays small.</summary>
	public const int MaxChars = 10000;

	/// <summary>Datapads written this session: the server echoes the new text a moment later, so a quick second copy must not pick the same one.</summary>
	private static readonly HashSet<uint> s_written = new HashSet<uint>();

	/// <summary>Returns a status line saying what happened.</summary>
	public static string Copy(string title, List<LogEntry> rows)
	{
		if (rows.Count == 0)
		{
			return "Nothing is shown to copy.";
		}
		if (!(MySession.Static?.LocalCharacter?.GetInventory(0) is MyInventory inventory))
		{
			return "No character inventory to find a datapad in.";
		}
		MyPhysicalInventoryItem? empty = null;
		foreach (MyPhysicalInventoryItem item in inventory.GetItems())
		{
			if (item.Content is MyObjectBuilder_Datapad pad && string.IsNullOrEmpty(pad.Data) && !s_written.Contains(item.ItemId))
			{
				empty = item;
				break;
			}
		}
		if (empty == null)
		{
			return "No empty datapad in your inventory. Build or find one; Tripwire never writes over a datapad that has text.";
		}
		StringBuilder text = new StringBuilder();
		text.AppendLine(title);
		text.AppendLine();
		int written = 0;
		foreach (LogEntry row in rows)
		{
			string line = row.ToString();
			if (text.Length + line.Length + 2 > MaxChars)
			{
				break;
			}
			text.AppendLine(line);
			written++;
		}
		if (written < rows.Count)
		{
			text.AppendLine($"... {rows.Count - written} more row(s) didn't fit.");
		}
		s_written.Add(empty.Value.ItemId);
		MyMultiplayer.RaiseEvent(inventory, (MyInventory x) => x.ModifyDatapad, 0, empty.Value.ItemId, title, text.ToString());
		return written < rows.Count ? $"Wrote {written} of {rows.Count} row(s) to a datapad (it's full). Copy the rest to another one." : $"Wrote {written} row(s) to a datapad in your inventory.";
	}
}
