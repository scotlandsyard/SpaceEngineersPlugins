using Sandbox.ModAPI;
using TimShared;

namespace Tripwire;

/// <summary>Saves Tripwire's chat level and name (Shared/Personality.cs, lines in Personality.txt). Alerts themselves are plain chat lines.</summary>
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
		MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"Chat name: {Personality.DisplayName}. /tripwire name on its own puts it back.");
	}

	/// <summary>Runs whenever the level or name changes (the window's Chat dropdown, /tripwire chat, Wilson).</summary>
	public static void Save()
	{
		string name = Personality.DisplayName;
		Settings.DisplayName = name == null || name == TripwireSession.ChatSender ? "" : name;
		Settings.Chattiness = Personality.Level;
		Settings.Save();
	}

	public static string LevelHint(Personality.Chattiness level)
	{
		switch (level)
		{
		case Personality.Chattiness.Off:
			return "Only the alerts themselves; no comments.";
		case Personality.Chattiness.Quiet:
			return "Comments only when your blocks are being ground or lost.";
		case Personality.Chattiness.Chatty:
			return "Comments often.";
		default:
			return "Comments now and then.";
		}
	}
}
