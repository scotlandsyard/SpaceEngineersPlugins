using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>
/// Whether the server would accept the plugin's requests for a ship or station. Item moves, queue changes and
/// screen text are validated on the server with ValidationType.Access, which for a grid is
/// MyReplicableRightsValidator.GetAccess: the player's character has to be within 3 x DEFAULT_INTERACTIVE_DISTANCE
/// (15 m) of any grid in the grid's logical group (grids joined by rotors, hinges and connectors), or one of the
/// group's grids has to be an antenna the character is connected to (the character's own radio network, or that of
/// the grid it is controlling). Anything else is refused and logged as a failed validation, so the plugin pauses
/// instead of sending requests for such a grid. The host of a game and single player aren't validated.
/// The antenna part follows the server's own code (MyCharacterReplicable.GetDependencies) as closely as the client
/// can; the server's "selected grid" is taken to be the controlled one.
/// </summary>
internal static class Reach
{
	/// <summary>Seconds a result is reused: the antenna search costs a little, and a pause shouldn't flicker.</summary>
	private const double CacheSeconds = 3.0;

	/// <summary>True when the server would accept requests for the construct's grids; also stores the result on it.</summary>
	public static bool Check(Construct construct, double now)
	{
		if (construct == null)
		{
			return true;
		}
		if (now - construct.ReachChecked < CacheSeconds)
		{
			return construct.InReach;
		}
		construct.ReachChecked = now;
		try
		{
			construct.InReach = Compute(construct, out string note);
			construct.ReachNote = note;
		}
		catch (Exception ex)
		{
			// If the check itself breaks, don't stop working for it.
			construct.InReach = true;
			construct.ReachNote = null;
			VRage.Utils.MyLog.Default.WriteLineAndConsole($"[SSM] Reach check: {ex.Message}");
		}
		return construct.InReach;
	}

	private static bool Compute(Construct construct, out string note)
	{
		note = null;
		if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer)
		{
			return true;
		}
		IMyCharacter character = MyAPIGateway.Session?.Player?.Character;
		IMyCubeGrid main = construct.MainGrid;
		if (main == null || main.Closed)
		{
			return true;
		}
		if (character == null || character.Closed)
		{
			note = "Paused: you have no character right now (the server only accepts requests from one).";
			return false;
		}
		List<IMyCubeGrid> group = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(main, GridLinkTypeEnum.Logical, group);
		if (!group.Contains(main))
		{
			group.Add(main);
		}
		Vector3D position = character.GetPosition();
		double allowed = VRage.Game.MyConstants.DEFAULT_INTERACTIVE_DISTANCE * 3.0;
		double allowedSquared = allowed * allowed;
		if (group.Any(g => !g.Closed && g.WorldAABB.DistanceSquared(position) <= allowedSquared))
		{
			return true;
		}
		// Not close enough: an antenna link also counts.
		HashSet<long> linked = LinkedGrids(character);
		if (group.Any(g => linked.Contains(g.EntityId)))
		{
			return true;
		}
		note = $"Paused: you're more than {allowed:0} m from {construct.MainGrid.CustomName} and not linked to it by antenna, so the server would refuse every request. Move closer or link an antenna.";
		return false;
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
		long identity = MyAPIGateway.Session.Player.IdentityId;
		HashSet<MyDataBroadcaster> broadcasters = new HashSet<MyDataBroadcaster>();
		antennas.GetAllRelayedBroadcasters(characterEntity, identity, false, broadcasters);
		IMyEntity controlled = MyAPIGateway.Session.ControlledObject?.Entity?.GetTopMostParent();
		if (controlled is MyEntity controlledEntity && controlled.EntityId != character.EntityId)
		{
			antennas.GetAllRelayedBroadcasters(controlledEntity, 0L, false, broadcasters);
		}
		foreach (MyDataBroadcaster broadcaster in broadcasters)
		{
			if (broadcaster == null || broadcaster.Closed)
			{
				continue;
			}
			IMyEntity top = broadcaster.Entity?.GetTopMostParent();
			if (top != null)
			{
				grids.Add(top.EntityId);
			}
		}
		return grids;
	}
}
