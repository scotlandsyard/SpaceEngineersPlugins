using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Tally;

/// <summary>
/// Whether the server would accept a queue request for a grid's assemblers. Add and remove queue requests are checked
/// on the server with ValidationType.Access, which for a grid is MyReplicableRightsValidator.GetAccess: the player's
/// character has to be within 3 x DEFAULT_INTERACTIVE_DISTANCE (15 m) of a grid in the grid's logical group (grids
/// joined by rotors, hinges and connectors), or one of the group's grids has to carry an antenna the character is
/// connected to (its own radio network, or that of the grid it controls). Anything else is refused and logged on the
/// server as a failed validation, so Tally doesn't send it. The host of a game and single player aren't checked.
/// Same rule as Stockpile Manager's.
/// </summary>
internal static class Reach
{
	public static double Metres => VRage.Game.MyConstants.DEFAULT_INTERACTIVE_DISTANCE * 3.0;

	/// <summary>True when the server would take requests for this grid; else <paramref name="why"/> says why not.</summary>
	public static bool Check(MyCubeGrid grid, out string why)
	{
		why = null;
		try
		{
			if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer || grid == null || grid.Closed)
			{
				return true;
			}
			IMyCharacter character = MyAPIGateway.Session?.Player?.Character;
			if (character == null || character.Closed)
			{
				why = "out of reach: you have no character right now, and the server only takes queue changes from one";
				return false;
			}
			List<IMyCubeGrid> group = new List<IMyCubeGrid>();
			MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, group);
			if (!group.Contains(grid))
			{
				group.Add(grid);
			}
			Vector3D position = character.GetPosition();
			double allowedSquared = Metres * Metres;
			if (group.Any(g => !g.Closed && g.WorldAABB.DistanceSquared(position) <= allowedSquared))
			{
				return true;
			}
			HashSet<long> linked = LinkedGrids(character);
			if (group.Any(g => linked.Contains(g.EntityId)))
			{
				return true;
			}
			why = $"out of reach: you're more than {Metres:0} m from {grid.DisplayName} and not linked to it by antenna, so the server would refuse it";
			return false;
		}
		catch (Exception ex)
		{
			// If the check itself breaks, send nothing: the server might refuse it.
			VRage.Utils.MyLog.Default.WriteLineAndConsole($"[Tally] Reach check: {ex.Message}");
			why = "the reach check failed (see the log), so nothing was sent";
			return false;
		}
	}

	/// <summary>The grids that carry a broadcaster the player's character is connected to (itself or through the grid it controls).</summary>
	private static HashSet<long> LinkedGrids(IMyCharacter character)
	{
		HashSet<long> grids = new HashSet<long>();
		MyAntennaSystem antennas = MyAntennaSystem.Static;
		if (antennas == null || !(character is MyEntity characterEntity))
		{
			return grids;
		}
		HashSet<MyDataBroadcaster> broadcasters = new HashSet<MyDataBroadcaster>();
		antennas.GetAllRelayedBroadcasters(characterEntity, MyAPIGateway.Session.Player.IdentityId, false, broadcasters);
		IMyEntity controlled = MyAPIGateway.Session.ControlledObject?.Entity?.GetTopMostParent();
		if (controlled is MyEntity controlledEntity && controlled.EntityId != character.EntityId)
		{
			antennas.GetAllRelayedBroadcasters(controlledEntity, 0L, false, broadcasters);
		}
		foreach (MyDataBroadcaster broadcaster in broadcasters)
		{
			IMyEntity top = broadcaster == null || broadcaster.Closed ? null : broadcaster.Entity?.GetTopMostParent();
			if (top != null)
			{
				grids.Add(top.EntityId);
			}
		}
		return grids;
	}
}
