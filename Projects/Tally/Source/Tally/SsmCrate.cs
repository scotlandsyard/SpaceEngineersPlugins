using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRageMath;

namespace Tally;

/// <summary>
/// Sends a bill's components to Sacrificial Stockpile Manager to stock a container, through SSM's documented mod
/// message (SpaceEngineers\Projects\Sacrificial Stockpile Manager\BILL-API.md, version 1). The messages stay inside
/// the player's own game. SSM opens its window with a preview and changes nothing until the player presses Apply.
/// </summary>
internal static class SsmCrate
{
	/// <summary>"SSM_FILL" in ASCII.</summary>
	private const long Channel = 0x53534D5F46494C4CL;

	private static long Me => MySession.Static?.LocalPlayerId ?? 0;

	private static Vector3D Here => MySession.Static?.LocalCharacter?.PositionComp.GetPosition() ?? MyAPIGateway.Session?.Camera?.Position ?? Vector3D.Zero;

	/// <summary>True when SSM answers a ping (it answers during the call).</summary>
	public static bool Loaded()
	{
		bool here = false;
		Action<object> reply = message =>
		{
			if (message is object[] parts && parts.Length >= 1 && parts[0] as string == "ssm-here")
			{
				here = true;
			}
		};
		MyAPIGateway.Utilities.RegisterMessageHandler(Channel, reply);
		try
		{
			MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { "ssm-ping" });
		}
		finally
		{
			MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, reply);
		}
		return here;
	}

	/// <summary>Cargo containers you own yourself (SSM refuses shared ones), nearest first.</summary>
	public static List<Target> Crates()
	{
		long me = Me;
		List<KeyValuePair<double, Target>> found = new List<KeyValuePair<double, Target>>();
		foreach (MyCubeGrid grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(g => !g.Closed && !g.MarkedForClose && !g.IsPreview && g.Projector == null))
		{
			foreach (MyCargoContainer crate in grid.GetFatBlocks<MyCargoContainer>())
			{
				if (crate.Closed || crate.OwnerId != me || !crate.HasInventory)
				{
					continue;
				}
				Vector3D at = crate.PositionComp.GetPosition();
				double metres = Vector3D.Distance(Here, at);
				string distance = metres < 1000 ? $"{metres:0} m" : $"{metres / 1000:0.0} km";
				found.Add(new KeyValuePair<double, Target>(metres, new Target
				{
					Label = $"{crate.CustomName} on {grid.DisplayName}, {distance}",
					Key = "C:" + crate.EntityId,
					GridId = grid.EntityId,
					AssemblerIds = new List<long> { crate.EntityId }
				}));
			}
		}
		return found.OrderBy(f => f.Key).Select(f => f.Value).ToList();
	}

	/// <summary>The components to send: what's still to get when something was taken off, else the whole bill, rounded up.</summary>
	public static List<KeyValuePair<string, double>> Items(Bill bill)
	{
		return bill.Components
			.Select(l => new KeyValuePair<string, double>(l.Id.ToString(), Math.Ceiling((bill.Subtracted ? l.Short : l.Needed) - 1e-6)))
			.Where(p => p.Value > 0)
			.ToList();
	}

	/// <summary>Sends the bill. Returns SSM's status ("preview" or "rejected"), or null if SSM didn't answer.</summary>
	public static string Send(Bill bill, long crateId, out string reason)
	{
		string status = null;
		string why = null;
		Action<object> reply = message =>
		{
			if (message is object[] parts && parts.Length >= 5 && parts[0] as string == "ssm-fill-result")
			{
				status = parts[3] as string;
				why = parts[4] as string;
			}
		};
		List<KeyValuePair<string, double>> items = Items(bill);
		string source = "Tally: " + bill.Name;
		MyAPIGateway.Utilities.RegisterMessageHandler(Channel, reply);
		try
		{
			MyAPIGateway.Utilities.SendModMessage(Channel, new object[]
			{
				"ssm-fill", 1, crateId,
				items.Select(i => i.Key).ToArray(),
				items.Select(i => i.Value).ToArray(),
				"once", source.Length > 60 ? source.Substring(0, 60) : source
			});
		}
		finally
		{
			MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, reply);
		}
		reason = why;
		return status;
	}
}
