using System;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;

namespace DrillSergeant;

/// <summary>
/// Plugin mode: the rig's status on an LCD of your own. Written at most every 10 seconds and only when the text
/// changed, because every change is synced to the server. In script mode the script writes its own screens.
/// </summary>
internal static class LcdOutput
{
	public static void Update(Rig rig, double now)
	{
		RigRuntime live = rig.Live;
		if (rig.Mode != RigMode.Plugin || rig.LcdId == 0 || now - live.LcdWrittenAt < 10 || !live.InReach)
		{
			return;
		}
		IMyTextPanel lcd = RigBlocks.Get<IMyTextPanel>(rig.LcdId);
		// Never another player's screen.
		if (lcd == null || !RigBlocks.IsMine(lcd) || !lcd.IsWorking)
		{
			return;
		}
		string text = Compose(rig);
		if (text == live.LastLcdText && lcd.GetText() == text)
		{
			return;
		}
		live.LcdWrittenAt = now;
		live.LastLcdText = text;
		if (lcd.ContentType != ContentType.TEXT_AND_IMAGE)
		{
			lcd.ContentType = ContentType.TEXT_AND_IMAGE;
		}
		lcd.WriteText(text);
	}

	private static string Compose(Rig rig)
	{
		RigRuntime live = rig.Live;
		StringBuilder text = new StringBuilder();
		text.Append(rig.Name).Append(": ").Append(live.State).Append('\n');
		text.Append("Progress ").Append(Bar(live.Progress)).Append(' ').Append((live.Progress * 100).ToString("0")).Append("%\n");
		if (live.MaxDepth > 0)
		{
			text.Append("Depth ").Append(live.Depth.ToString("0.0")).Append(" of ").Append(live.MaxDepth.ToString("0.0")).Append(" m\n");
		}
		if (live.CargoFill >= 0)
		{
			text.Append("Cargo ").Append(Bar(live.CargoFill)).Append(' ').Append((live.CargoFill * 100).ToString("0")).Append("%\n");
		}
		if (live.SecondsLeft > 0)
		{
			text.Append("Time left ").Append(Format.Duration(live.SecondsLeft)).Append('\n');
		}
		return text.ToString();
	}

	private static string Bar(double share)
	{
		int filled = (int)Math.Round(Math.Max(0, Math.Min(1, share)) * 20);
		return "[" + new string('|', filled) + new string('.', 20 - filled) + "]";
	}
}

internal static class Format
{
	public static string Duration(double seconds)
	{
		if (seconds < 0)
		{
			return "-";
		}
		TimeSpan time = TimeSpan.FromSeconds(Math.Ceiling(seconds / 60) * 60);
		return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m" : $"{time.Minutes}m";
	}
}
