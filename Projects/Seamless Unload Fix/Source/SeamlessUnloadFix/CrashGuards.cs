using System;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using VRage.Utils;

namespace SeamlessUnloadFix;

/// <summary>
/// Two game handlers that assume a fully loaded client, and crash the game in the seconds after a Seamless switch,
/// when the client's player and identity lists don't match the new server yet. Each guard acts only in the state
/// that would otherwise throw; everything else runs the game's own code unchanged.
///
/// Faction discovered: MyCharacterDiscoveryComponent.OnFactionDiscovered (SpaceEngineers.Game) reads
///   MySession.Static.LocalHumanPlayer.Id without a null check. A switch clears the players, and the buffered
///   messages the new server sent during the switch are replayed (MyTransportLayer.ProcessBuffer) before the
///   server has created the local player. A replayed "faction discovered" event then throws. The discovery is
///   already stored before the event fires; the handler only shows a HUD notification, so we skip it while there
///   is no local player.
///
/// Identity removed: MyPlayerCollection.RemoveIdentityInternal dereferences the identity it looked up, at
///   Factions.DeletePlayerRelations(value.IdentityId), even when the lookup failed. A switch keeps the old server's
///   identities and doesn't load the new server's, so the new server can remove an identity this client never had.
///   On a client every earlier step is null-safe, so when the identity is unknown we let the method run, swallow
///   only that NullReferenceException, and finish the last two steps ourselves.
/// </summary>
internal static class CrashGuards
{
	private const string Tag = "[SeamlessUnloadFix] ";

	private const string DiscoveryType = "SpaceEngineers.Game.EntityComponents.GameLogic.Discovery.MyCharacterDiscoveryComponent";

	private static AccessTools.FieldRef<MyPlayerCollection, ConcurrentDictionary<long, MyIdentity>> s_allIdentities;

	private static MethodInfo s_deletePlayerRelations;

	private static FieldInfo s_identitiesChanged;

	private static int s_skippedDiscoveries;

	internal static void Apply(Harmony harmony)
	{
		try
		{
			Type discovery = AccessTools.TypeByName(DiscoveryType) ?? throw new MissingMemberException(DiscoveryType);
			MethodInfo handler = AccessTools.Method(discovery, "OnFactionDiscovered") ?? throw new MissingMethodException(DiscoveryType, "OnFactionDiscovered");
			harmony.Patch(handler, prefix: new HarmonyMethod(typeof(CrashGuards), nameof(FactionDiscoveredPrefix)));
			Log("Faction discovered guard applied");
		}
		catch (Exception e)
		{
			Log("Faction discovered guard failed, the game's handler is unchanged:\n" + e);
		}
		try
		{
			s_allIdentities = AccessTools.FieldRefAccess<MyPlayerCollection, ConcurrentDictionary<long, MyIdentity>>("m_allIdentities");
			s_deletePlayerRelations = AccessTools.Method(typeof(MyFactionCollection), "DeletePlayerRelations", new[] { typeof(long) })
				?? throw new MissingMethodException(nameof(MyFactionCollection), "DeletePlayerRelations");
			s_identitiesChanged = AccessTools.Field(typeof(MyPlayerCollection), nameof(MyPlayerCollection.IdentitiesChanged));
			MethodInfo remove = AccessTools.Method(typeof(MyPlayerCollection), "RemoveIdentityInternal", new[] { typeof(long), typeof(MyPlayer.PlayerId) })
				?? throw new MissingMethodException(nameof(MyPlayerCollection), "RemoveIdentityInternal");
			harmony.Patch(remove,
				prefix: new HarmonyMethod(typeof(CrashGuards), nameof(RemoveIdentityPrefix)),
				finalizer: new HarmonyMethod(typeof(CrashGuards), nameof(RemoveIdentityFinalizer)));
			Log("Identity removed guard applied");
		}
		catch (Exception e)
		{
			Log("Identity removed guard failed, the game's handler is unchanged:\n" + e);
		}
	}

	private static bool FactionDiscoveredPrefix()
	{
		if (MySession.Static == null || MySession.Static.LocalHumanPlayer != null || Sync.IsDedicated)
		{
			s_skippedDiscoveries = 0;
			return true;
		}
		// Once per stretch without a player is enough for the log.
		if (s_skippedDiscoveries++ == 0)
		{
			Log("A faction was discovered while there was no local player yet (during a server switch); skipped its HUD notification. The discovery itself is kept");
		}
		return false;
	}

	/// <summary>__state = the identity is unknown on this client, so the original will throw at its end.</summary>
	private static void RemoveIdentityPrefix(MyPlayerCollection __instance, long identityId, out bool __state)
	{
		__state = false;
		try
		{
			__state = !Sync.IsServer && !s_allIdentities(__instance).ContainsKey(identityId);
		}
		catch (Exception e)
		{
			Log("Identity removed guard check failed:\n" + e);
		}
	}

	private static Exception RemoveIdentityFinalizer(MyPlayerCollection __instance, long identityId, Exception __exception, bool __state, ref bool __result)
	{
		if (!__state || !(__exception is NullReferenceException))
		{
			return __exception;
		}
		Log($"The server removed identity {identityId}, which this client didn't know (its identity list wasn't reloaded after a server switch); removed it without the crash");
		try
		{
			s_deletePlayerRelations.Invoke(MySession.Static.Factions, new object[] { identityId });
			(s_identitiesChanged?.GetValue(__instance) as Action)?.Invoke();
		}
		catch (Exception e)
		{
			Log("Finishing the identity removal failed (ignored):\n" + e);
		}
		__result = true;
		return null;
	}

	private static void Log(string message)
	{
		MyLog.Default?.WriteLineAndConsole(Tag + message);
	}
}
