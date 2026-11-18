using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRage.Utils;

namespace SacrificialStockpileManager;

/// <summary>
/// The entry point other plugins (Tally) use to send a bill of materials: a mod message on <see cref="Channel"/>,
/// documented in BILL-API.md. It only ever opens the Add block materials preview; the player applies it.
/// </summary>
internal static class BillApi
{
	/// <summary>"SSM_FILL" in ASCII.</summary>
	public const long Channel = 0x53534D5F46494C4CL;

	public const int Version = 1;

	/// <summary>The largest amount one bill line can ask for; more is cut to this.</summary>
	private const double MaxAmount = 1e7;

	private static bool s_registered;

	public static void Register()
	{
		if (s_registered || MyAPIGateway.Utilities == null)
		{
			return;
		}
		MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);
		s_registered = true;
		// For a plugin that registered before this one loaded.
		Send(new object[] { "ssm-here", Version });
	}

	public static void Unregister()
	{
		if (!s_registered)
		{
			return;
		}
		s_registered = false;
		MyAPIGateway.Utilities?.UnregisterMessageHandler(Channel, OnMessage);
	}

	private static void Send(object[] message)
	{
		try
		{
			MyAPIGateway.Utilities.SendModMessage(Channel, message);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] Bill reply: {ex.Message}");
		}
	}

	private static void OnMessage(object message)
	{
		if (!(message is object[] parts) || parts.Length == 0 || !(parts[0] is string kind))
		{
			return;
		}
		try
		{
			switch (kind)
			{
			case "ssm-ping":
				Send(new object[] { "ssm-here", Version });
				break;
			case "ssm-fill":
				long blockId = parts.Length > 2 && parts[2] is long id ? id : 0L;
				string reason = Receive(parts, blockId);
				Send(new object[] { "ssm-fill-result", Version, blockId, reason == null ? "preview" : "rejected", reason ?? "" });
				break;
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] Bill message: {ex}");
		}
	}

	/// <summary>Checks a bill and opens the preview. Returns why it was refused, or null when the preview opened.</summary>
	public static string Receive(object[] parts, long blockId)
	{
		SsmSession session = SsmSession.Instance;
		if (session == null || MyAPIGateway.Session?.Player == null)
		{
			return "Stockpile Manager isn't running in this game.";
		}
		// string[] and double[] as documented; other lists and number types are accepted too.
		string[] items = parts.Length > 3 && parts[3] is System.Collections.IEnumerable itemList && !(parts[3] is string) ? itemList.Cast<object>().Select(o => o as string).ToArray() : null;
		double[] amounts = null;
		try
		{
			amounts = parts.Length > 4 && parts[4] is System.Collections.IEnumerable amountList ? amountList.Cast<object>().Select(Convert.ToDouble).ToArray() : null;
		}
		catch (Exception)
		{
		}
		if (SsmScreen.IsOpen)
		{
			return "The Stockpile Manager window is already open, so it's in the way. Close it and try again.";
		}
		if (parts.Length < 7 || items == null || amounts == null || items.Length != amounts.Length)
		{
			return "The bill is malformed: it needs matching item and amount lists.";
		}
		IMyEntity entity = blockId == 0 ? null : MyAPIGateway.Entities.GetEntityById(blockId);
		if (!(entity is IMyTerminalBlock block) || block.Closed)
		{
			return "That block isn't loaded.";
		}
		if (!block.HasInventory)
		{
			return $"{block.CustomName} has no inventory.";
		}
		if (block.OwnerId != MyAPIGateway.Session.Player.IdentityId)
		{
			return $"{block.CustomName} isn't yours, so Stockpile Manager won't stock it.";
		}
		Dictionary<string, double> bill = new Dictionary<string, double>();
		for (int i = 0; i < items.Length; i++)
		{
			string raw = (items[i] ?? "").Trim();
			// Only finite, sensible amounts: Infinity or NaN would become a minimum nothing can meet.
			if (!(amounts[i] > 0.0) || double.IsInfinity(amounts[i]) || raw.Length == 0)
			{
				continue;
			}
			string key = MyDefinitionId.TryParse(raw, out MyDefinitionId id) ? Items.Key(id) : raw;
			if (!Items.TryParse(key, out _))
			{
				continue;
			}
			bill.TryGetValue(key, out double amount);
			bill[key] = Math.Min(MaxAmount, amount + Math.Min(MaxAmount, amounts[i]));
		}
		if (bill.Count == 0)
		{
			return "The bill has no items Stockpile Manager knows.";
		}
		bool keep = parts[5] as string == "keep";
		string source = (parts[6] as string ?? "").Trim();
		if (source.Length > 60)
		{
			source = source.Substring(0, 60).TrimEnd();
		}
		return session.OpenBill(block, bill, keep, source.Length > 0 ? source : "another plugin");
	}
}
