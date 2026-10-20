using Sandbox.ModAPI;
using TimShared;

namespace DrillSergeant;

/// <summary>Saves the chat level and name (Shared/Personality.cs, lines in Personality.txt).</summary>
internal static class Chatter
{
	public static void SetLevel(Personality.Chattiness level)
	{
		Personality.Level = level;
		Save();
	}

	/// <summary>Renames the character in chat; null or blank goes back to its own name.</summary>
	public static void SetDisplayName(string name)
	{
		Personality.DisplayName = name;
		// Changed only fires on a real change, so save here too.
		Save();
		MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"Chat name: {Personality.DisplayName}. /sarge name on its own puts it back.");
	}

	/// <summary>Runs whenever the level or name changes (the window's Chat dropdown, /sarge chat, Wilson).</summary>
	public static void Save()
	{
		string name = Personality.DisplayName;
		Settings.DisplayName = name == null || name == SargeSession.ChatSender ? "" : name;
		Settings.Chattiness = Personality.Level;
		Settings.Save();
	}

	public static string LevelHint(Personality.Chattiness level)
	{
		switch (level)
		{
		case Personality.Chattiness.Off:
			return "Only the rig alerts you asked for; no comments.";
		case Personality.Chattiness.Quiet:
			return "Comments only when a rig finishes or needs you.";
		case Personality.Chattiness.Chatty:
			return "Comments often.";
		default:
			return "Comments now and then.";
		}
	}
}
