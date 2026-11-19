using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Doorman;

/// <summary>
/// Whether the server would accept a request for a block on this grid right now. Block changes are checked on the
/// server for access (MyReplicableRightsValidator.GetAccess): the player's character must be within 3 x 5 m of any
/// grid joined to the block's grid (rotors, pistons, connectors), or connected by antenna to one of those grids.
/// Anything else is refused, so Doorman sends nothing for a door out of reach (and nothing while you are dead or respawning). Single player and the host of a
/// game aren't checked by anyone, so there everything is in reach.
/// </summary>
internal static class Reach
{
	public static readonly double Distance = VRage.Game.MyConstants.DEFAULT_INTERACTIVE_DISTANCE * 3.0;

	private static readonly List<IMyCubeGrid> s_group = new List<IMyCubeGrid>();

	private static readonly HashSet<MyDataBroadcaster> s_broadcasters = new HashSet<MyDataBroadcaster>();

	private static readonly Dictionary<long, KeyValuePair<double, bool>> s_cache = new Dictionary<long, KeyValuePair<double, bool>>();

	/// <summary>InReach, worked out at most once a second per grid (every door on a grid asks).</summary>
	public static bool InReachCached(IMyCubeGrid grid, double now)
	{
		if (grid == null)
		{
			return true;
		}
		if (s_cache.TryGetValue(grid.EntityId, out KeyValuePair<double, bool> hit) && now - hit.Key < 1.0 && now >= hit.Key)
		{
			return hit.Value;
		}
		bool result = InReach(grid);
		s_cache[grid.EntityId] = new KeyValuePair<double, bool>(now, result);
		return result;
	}

	public static void Clear()
	{
		s_cache.Clear();
	}

	/// <summary>For automated tests: apply the rule in a single-player world too, where nobody checks it.</summary>
	internal static bool CheckOffline { get; set; }

	public static bool InReach(IMyCubeGrid grid)
	{
		if (grid == null || (!CheckOffline && (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer)))
		{
			return true;
		}
		IMyCharacter character = MyAPIGateway.Session?.Player?.Character;
		if (character == null || character.Closed || character.IsDead)
		{
			return false;
		}
		s_group.Clear();
		MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, s_group);
		if (!s_group.Contains(grid))
		{
			s_group.Add(grid);
		}
		Vector3D position = character.GetPosition();
		foreach (IMyCubeGrid member in s_group)
		{
			if (!member.Closed && member.WorldAABB.DistanceSquared(position) <= Distance * Distance)
			{
				return true;
			}
		}
		return LinkedByAntenna(character);
	}

	/// <summary>True when one of the group's grids carries a broadcaster the character reaches, itself or through the grid it controls.</summary>
	private static bool LinkedByAntenna(IMyCharacter character)
	{
		MyAntennaSystem antennas = MyAntennaSystem.Static;
		if (antennas == null || !(character is MyEntity characterEntity))
		{
			return false;
		}
		s_broadcasters.Clear();
		antennas.GetAllRelayedBroadcasters(characterEntity, MyAPIGateway.Session.Player.IdentityId, false, s_broadcasters);
		IMyEntity controlled = MyAPIGateway.Session.ControlledObject?.Entity?.GetTopMostParent();
		if (controlled is MyEntity controlledEntity && controlled.EntityId != character.EntityId)
		{
			antennas.GetAllRelayedBroadcasters(controlledEntity, 0L, false, s_broadcasters);
		}
		foreach (MyDataBroadcaster broadcaster in s_broadcasters)
		{
			IMyEntity top = broadcaster == null || broadcaster.Closed ? null : broadcaster.Entity?.GetTopMostParent();
			if (top != null && s_group.Exists(g => g.EntityId == top.EntityId))
			{
				return true;
			}
		}
		return false;
	}
}
