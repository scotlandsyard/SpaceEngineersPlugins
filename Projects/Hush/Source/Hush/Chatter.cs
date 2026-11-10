using Sandbox.ModAPI;
using TimShared;

namespace Hush;

/// <summary>Saves Hush's chat level and name (Shared/Personality.cs, lines in Personality.txt) with the type mutes.</summary>
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
		MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"Chat name: {Personality.DisplayName}. /hush name on its own puts it back.");
	}

	/// <summary>Runs whenever the level or name changes (the window's Chat dropdown, /hush chat, Wilson).</summary>
	public static void Save()
	{
		string name = Personality.DisplayName;
		Mutes.DisplayName = name == null || name == HushSession.ChatSender ? "" : name;
		Mutes.Chattiness = Personality.Level;
		Mutes.SaveSettings();
	}

	public static string LevelHint(Personality.Chattiness level)
	{
		switch (level)
		{
		case Personality.Chattiness.Off:
			return "Not a whisper.";
		case Personality.Chattiness.Quiet:
			return "Hardly a word.";
		case Personality.Chattiness.Chatty:
			return "Comments often.";
		default:
			return "Comments now and then.";
		}
	}
}
