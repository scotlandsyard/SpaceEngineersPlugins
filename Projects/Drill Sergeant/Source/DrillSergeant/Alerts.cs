using Sandbox.Game.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Audio;

namespace DrillSergeant;

/// <summary>Rig alerts, on your screen only: a chat line in Sarge's colour, a HUD sound, both or neither, per rig.</summary>
internal static class Alerts
{
	public static void Send(Rig rig, string text, bool warning)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		if (rig.Alerts == AlertKind.Chat || rig.Alerts == AlertKind.ChatAndSound)
		{
			Personality.Show(SargeSession.ChatSender, Personality.DisplayName ?? SargeSession.ChatSender, text);
		}
		if (rig.Alerts == AlertKind.Sound || rig.Alerts == AlertKind.ChatAndSound)
		{
			MyGuiAudio.PlaySound(warning ? MyGuiSounds.HudUnable : MyGuiSounds.HudGPSNotification3);
		}
	}
}
