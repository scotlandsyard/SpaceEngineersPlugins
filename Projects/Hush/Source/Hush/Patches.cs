using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Utils;

namespace Hush;

/// <summary>
/// Hooks the game's 3D sound emitter, the one object every block sound goes through (modded blocks too):
/// - its constructor, to keep a list of emitters (Mutes.Emitters), so a mute can stop a sound that's already playing;
/// - PlaySoundInternal, which every play path ends in (PlaySound, PlaySoundWithDistance, the sound queue, a loop
///   being restarted by the emitter's Update), skipped for a muted block;
/// - PlaySound(byte[]), the buffered path, skipped the same way.
/// A muted emitter keeps its Loop flag, so the emitter keeps asking to play and the sound comes back by itself when
/// the block is unmuted. Nothing here touches the network: it only changes what this PC plays.
/// </summary>
internal static class Patches
{
	private const string HarmonyId = "Hush";

	private static Harmony s_harmony;

	private static bool s_loggedPlayError;

	public static void Apply()
	{
		if (s_harmony != null)
		{
			return;
		}
		s_harmony = new Harmony(HarmonyId);
		try
		{
			s_harmony.Patch(AccessTools.Constructor(typeof(MyEntity3DSoundEmitter), new[] { typeof(MyEntity), typeof(bool), typeof(float) }),
				postfix: new HarmonyMethod(typeof(Patches), nameof(ConstructorPostfix)));
			s_harmony.Patch(AccessTools.Method(typeof(MyEntity3DSoundEmitter), "PlaySoundInternal"),
				prefix: new HarmonyMethod(typeof(Patches), nameof(PlayPrefix)));
			// Found by its first parameter, so the project doesn't need VRage.Audio for the last one's type.
			MethodInfo buffered = AccessTools.GetDeclaredMethods(typeof(MyEntity3DSoundEmitter))
				.FirstOrDefault(m => m.Name == nameof(MyEntity3DSoundEmitter.PlaySound) && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(byte[]));
			if (buffered != null)
			{
				s_harmony.Patch(buffered, prefix: new HarmonyMethod(typeof(Patches), nameof(PlayPrefix)));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Hush] Could not patch the sound emitter, muting won't work: {ex}");
			Remove();
		}
	}

	public static void Remove()
	{
		s_harmony?.UnpatchAll(HarmonyId);
		s_harmony = null;
	}

	private static void ConstructorPostfix(MyEntity3DSoundEmitter __instance)
	{
		Mutes.AddEmitter(__instance);
	}

	/// <summary>False (skip the game's method) when the emitter belongs to a muted block.</summary>
	private static bool PlayPrefix(MyEntity3DSoundEmitter __instance)
	{
		try
		{
			return !Mutes.OnPlay(__instance);
		}
		catch (Exception ex)
		{
			// Logged once: this runs for every sound, so a repeating error would flood the log.
			if (!s_loggedPlayError)
			{
				s_loggedPlayError = true;
				MyLog.Default.WriteLineAndConsole($"[Hush] Sound check failed: {ex}");
			}
			return true;
		}
	}
}
