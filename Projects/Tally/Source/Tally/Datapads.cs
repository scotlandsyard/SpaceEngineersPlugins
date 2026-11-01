using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Game.Entity;

namespace Tally;

/// <summary>
/// Writes a bill onto datapads in the player's own character inventory, with the request the game's datapad screen
/// sends when you press OK (MyInventory.ModifyDatapad: inventory index, item id, name, text). The plugin can't make
/// datapads, only fill ones you carry. A datapad holds 1000 characters, so a long bill goes over several.
/// Taken from Digby's Datapads.cs (same request, same checks); only the pages differ.
/// </summary>
internal static class Datapads
{
	/// <summary>The game's editor counts UTF-8 bytes and keeps 999 of them, so stay within that and hand edits can't cut the text.</summary>
	private const int MaxBytes = 999;

	private const string NamePrefix = "Tally: ";

	public class Pad
	{
		public uint ItemId;

		public string Name;

		public string Data;

		public bool Empty => string.IsNullOrWhiteSpace(Data);

		public string Label => (string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name) + (Empty ? ", empty" : $", {Data.Length} characters");
	}

	/// <summary>One datapad to write: which pad, and its new name and text.</summary>
	public class Write
	{
		public Pad Pad;

		public string Name;

		public string Text;
	}

	private static MyInventory Inventory()
	{
		return MySession.Static?.LocalCharacter?.GetInventory(0) as MyInventory;
	}

	/// <summary>The datapads in your own character inventory.</summary>
	public static List<Pad> List()
	{
		List<Pad> pads = new List<Pad>();
		MyInventory inventory = Inventory();
		if (inventory == null)
		{
			return pads;
		}
		foreach (MyPhysicalInventoryItem item in inventory.GetItems())
		{
			if (item.Content is MyObjectBuilder_Datapad pad)
			{
				pads.Add(new Pad { ItemId = item.ItemId, Name = pad.Name ?? "", Data = pad.Data ?? "" });
			}
		}
		return pads;
	}

	private static string BaseName(Bill bill)
	{
		string name = NamePrefix + bill.Name;
		return name.Length > 200 ? name.Substring(0, 200) : name;
	}

	/// <summary>The bill's text split into datapad pages; page 1 starts with what was tallied and how.</summary>
	public static List<string> Pages(Bill bill)
	{
		List<string> pages = new List<string>();
		StringBuilder page = new StringBuilder();
		foreach (string line in Text.Lines(bill).SelectMany(Split))
		{
			if (page.Length > 0 && Bytes(page + "\n" + line) > MaxBytes)
			{
				pages.Add(page.ToString().TrimEnd());
				page.Clear().Append($"{NamePrefix}{bill.Name}, continued:");
			}
			if (page.Length > 0)
			{
				page.Append('\n');
			}
			page.Append(line);
		}
		pages.Add(page.ToString().TrimEnd());
		return pages;
	}

	/// <summary>A line short enough for one page, split at spaces if needed.</summary>
	private static IEnumerable<string> Split(string line)
	{
		const int budget = MaxBytes - 100;
		while (Bytes(line) > budget)
		{
			int cut = line.LastIndexOf(' ', Math.Min(line.Length - 1, budget / 2));
			cut = cut <= 0 ? budget / 2 : cut;
			yield return line.Substring(0, cut);
			line = line.Substring(cut).TrimStart();
		}
		yield return line;
	}

	private static int Bytes(object text)
	{
		return Encoding.UTF8.GetByteCount(text.ToString());
	}

	/// <summary>
	/// Datapads to use, in order: the one picked (if any), then pads already holding this bill, then empty ones.
	/// Never an unpicked pad with other text on it.
	/// </summary>
	public static List<Pad> Choose(Bill bill, uint picked, int needed)
	{
		List<Pad> pads = List();
		List<Pad> chosen = new List<Pad>();
		Pad first = pads.FirstOrDefault(p => p.ItemId == picked);
		if (first != null)
		{
			chosen.Add(first);
		}
		string baseName = BaseName(bill);
		chosen.AddRange(pads.Where(p => !chosen.Contains(p) && IsThisBill(p.Name, baseName)).OrderBy(p => p.Name, StringComparer.Ordinal));
		chosen.AddRange(pads.Where(p => !chosen.Contains(p) && p.Empty));
		return chosen.Take(needed).ToList();
	}

	public static bool IsThisBill(string padName, string baseName)
	{
		padName = padName ?? "";
		return padName == baseName || padName.StartsWith(baseName + " (", StringComparison.Ordinal) || padName.StartsWith(baseName + " #", StringComparison.Ordinal);
	}

	/// <summary>Pairs pages with pads. With fewer pads than pages the last pad says the rest was cut.</summary>
	public static List<Write> Assign(Bill bill, List<string> pages, List<Pad> pads)
	{
		List<Write> writes = new List<Write>();
		int count = Math.Min(pages.Count, pads.Count);
		if (count == 0)
		{
			return writes;
		}
		// A name no other datapad you carry has: "Tally: <name>", or "Tally: <name> #2" if that's taken by another pad.
		string baseName = BaseName(bill);
		HashSet<string> others = new HashSet<string>(List().Where(p => !pads.Take(count).Any(c => c.ItemId == p.ItemId)).Select(p => p.Name ?? ""), StringComparer.OrdinalIgnoreCase);
		string unique = baseName;
		for (int n = 2; others.Any(o => o == unique || o.StartsWith(unique + " (", StringComparison.OrdinalIgnoreCase)); n++)
		{
			unique = $"{baseName} #{n}";
		}
		for (int i = 0; i < count; i++)
		{
			string text = pages[i];
			if (i == count - 1 && count < pages.Count)
			{
				string note = $"\n(Cut: {pages.Count - count} more datapad(s) didn't fit. Carry more empty datapads and write it again for all of it.)";
				List<string> lines = text.Split('\n').ToList();
				while (lines.Count > 1 && Bytes(string.Join("\n", lines) + note) > MaxBytes)
				{
					lines.RemoveAt(lines.Count - 1);
				}
				text = string.Join("\n", lines) + note;
			}
			writes.Add(new Write { Pad = pads[i], Name = count == 1 ? unique : $"{unique} ({i + 1}/{count})", Text = text });
		}
		return writes;
	}

	/// <summary>Sends the game's own datapad edit request for each pad. Returns false if your inventory isn't there.</summary>
	public static bool Send(List<Write> writes)
	{
		MyInventory inventory = Inventory();
		if (inventory?.Owner == null)
		{
			return false;
		}
		int index = -1;
		for (int i = 0; i < inventory.Owner.InventoryCount; i++)
		{
			if (inventory.Owner.GetInventory(i) == inventory)
			{
				index = i;
				break;
			}
		}
		if (index < 0)
		{
			return false;
		}
		foreach (Write write in writes)
		{
			MyMultiplayer.RaiseEvent(inventory, x => x.ModifyDatapad, index, write.Pad.ItemId, write.Name, write.Text);
		}
		return true;
	}

	/// <summary>
	/// Writes a bill, asking first when it needs more datapads than you have free, or when the picked pad has other
	/// text on it. <paramref name="report"/> gets the outcome lines; the check that the server applied it comes later.
	/// </summary>
	public static void Request(Bill bill, uint picked, Action<string> report)
	{
		if (bill == null)
		{
			report("Tally something first.");
			return;
		}
		if (Inventory() == null)
		{
			report("You need to be in your character (on foot or seated) to write a datapad.");
			return;
		}
		List<Pad> all = List();
		if (all.Count == 0)
		{
			report("You have no datapad in your inventory. Tally can't make one: craft one (or pick one up), put it in your own inventory and try again.");
			TimShared.Personality.Say("no_datapad");
			return;
		}
		List<string> pages = Pages(bill);
		Pad pickedPad = all.FirstOrDefault(p => p.ItemId == picked);
		Action next = () => CheckCount(bill, pages, picked, report);
		if (pickedPad != null && !pickedPad.Empty && !IsThisBill(pickedPad.Name, BaseName(bill)))
		{
			Ask("Overwrite datapad", $"The datapad you picked, \"{pickedPad.Name}\", already has text on it. Overwrite it with this list?", next, () => report("Nothing written."));
			return;
		}
		next();
	}

	private static void CheckCount(Bill bill, List<string> pages, uint picked, Action<string> report)
	{
		List<Pad> pads = Choose(bill, picked, pages.Count);
		if (pads.Count == 0)
		{
			report("None of your datapads is free: each has other text on it. Pick one in the Datapad list to overwrite it, or carry an empty one.");
			TimShared.Personality.Say("no_datapad");
			return;
		}
		Action write = () => DoWrite(bill, pages, pads, report);
		if (pads.Count < pages.Count)
		{
			Ask("Not enough datapads", $"This list needs {pages.Count} datapads, and you have {pads.Count} free (empty, picked, or already holding this list). Tally can't make datapads.\n\nWrite what fits on {pads.Count} now (the end of the list is cut), or cancel and bring {pages.Count - pads.Count} more empty datapad(s)?", write, () => report($"Nothing written: the list needs {pages.Count} datapads."));
			return;
		}
		write();
	}

	private static void DoWrite(Bill bill, List<string> pages, List<Pad> pads, Action<string> report)
	{
		List<Write> writes = Assign(bill, pages, pads);
		if (!Send(writes))
		{
			report("Your inventory couldn't be found, so nothing was written.");
			return;
		}
		TallySession.ExpectPads(writes, report);
		report(writes.Count == 1 ? $"Sent the list to datapad \"{writes[0].Name}\"." : $"Sent the list to {writes.Count} datapads, \"{writes[0].Name}\" and on.");
	}

	private static void Ask(string caption, string text, Action yes, Action no)
	{
		MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder(caption), messageText: new StringBuilder(text), callback: answer =>
		{
			if (answer == MyGuiScreenMessageBox.ResultEnum.YES)
			{
				yes();
			}
			else
			{
				no();
			}
		}));
	}

	/// <summary>Name of the pad with this item id now, or null if it's gone.</summary>
	public static string CurrentName(uint itemId)
	{
		return List().FirstOrDefault(p => p.ItemId == itemId)?.Name;
	}
}
