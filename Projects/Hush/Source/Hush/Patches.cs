using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using VRage.Audio;
using VRage.Game.Entity;
using VRage.Utils;

namespace Hush;

/// <summary>
/// Hooks the game's 3D sound emitter, the one object every block sound goes through (modded blocks too):
/// - its constructor, to keep a list of emitters (Mutes.Emitters), so a mute can stop a sound that's already playing;
/// - PlaySoundInternal, which every play path ends in (PlaySound, PlaySoundWithDistance, the sound queue, a loop
///   being restarted by the emitter's Update), skipped for a muted block.
/// The buffered PlaySound(byte[]) path is left alone on purpose: in the game only voice chat uses it, and voice chat
/// is never muted. Character sounds are never muted either (Mutes.BlockOf stops at a character).
/// A muted emitter keeps its Loop flag, so the emitter keeps asking to play and the sound comes back by itself when
/// the block is unmuted. A loop that first starts while muted gets the flag set here as the game would have set it
/// (PlaySoundInternal), so it comes back on unmute too. Nothing here touches the network: it only changes what this PC plays.
/// </summary>
internal static class Patches
{
	private const string HarmonyId = "Hush";

	private static Harmony s_harmony;

	private static bool s_loggedPlayError;

	private static Action<MyEntity3DSoundEmitter, bool> s_setLoop;

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
			s_setLoop = AccessTools.MethodDelegate<Action<MyEntity3DSoundEmitter, bool>>(AccessTools.PropertySetter(typeof(MyEntity3DSoundEmitter), nameof(MyEntity3DSoundEmitter.Loop)));
			s_harmony.Patch(AccessTools.Method(typeof(MyEntity3DSoundEmitter), "PlaySoundInternal"),
				prefix: new HarmonyMethod(typeof(Patches), nameof(PlayPrefix)));
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

	/// <summary>False (skip the game's method) when the emitter belongs to a muted block. SoundId is the cue about to play.</summary>
	private static bool PlayPrefix(MyEntity3DSoundEmitter __instance, bool skipToEnd)
	{
		bool play = Check(__instance, __instance.SoundId);
		if (!play)
		{
			MarkLoop(__instance, skipToEnd);
		}
		return play;
	}

	/// <summary>
	/// The game sets Loop inside PlaySoundInternal, which a muted block skips. Set it the same way, so the emitter's
	/// Update keeps asking to play and the loop comes back by itself on unmute. The block's own StopSound clears it.
	/// </summary>
	private static void MarkLoop(MyEntity3DSoundEmitter emitter, bool skipToEnd)
	{
		try
		{
			if (s_setLoop != null && !emitter.Loop && !skipToEnd && emitter.CanPlayLoopSounds && !emitter.SoundId.IsNull && MyAudio.Static != null && MyAudio.Static.IsLoopable(emitter.SoundId))
			{
				s_setLoop(emitter, true);
			}
		}
		catch (Exception)
		{
			// Not worth a log line per sound: the loop just won't come back until the block asks again, as before.
			s_setLoop = null;
		}
	}

	private static bool Check(MyEntity3DSoundEmitter emitter, MyCueId? cue)
	{
		try
		{
			return !Mutes.OnPlay(emitter, cue);
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
