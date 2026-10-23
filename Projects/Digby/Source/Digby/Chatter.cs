using Sandbox.ModAPI;
using TimShared;

namespace Digby;

/// <summary>Saves Digby's chat level and name (Shared/Personality.cs, lines in Personality.txt).</summary>
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
		MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"Chat name: {Personality.DisplayName}. /digby name on its own puts it back.");
	}

	/// <summary>Runs whenever the level or name changes (the window's Chat dropdown, /digby chat, Wilson).</summary>
	public static void Save()
	{
		string name = Personality.DisplayName;
		Settings.DisplayName = name == null || name == DigbySession.ChatSender ? "" : name;
		Settings.Chattiness = Personality.Level;
		Settings.Save();
	}

	public static string LevelHint(Personality.Chattiness level)
	{
		switch (level)
		{
		case Personality.Chattiness.Off:
			return "No comments.";
		case Personality.Chattiness.Quiet:
			return "Hardly a word.";
		case Personality.Chattiness.Chatty:
			return "Comments often.";
		default:
			return "Comments now and then.";
		}
	}
}
