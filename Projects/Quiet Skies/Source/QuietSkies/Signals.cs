using System;
using Sandbox.Game.Localization;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;
using VRage;
using VRage.Game;
using VRage.Game.Components.Session;
using VRage.Game.Definitions.SessionComponents;
using VRageMath;

namespace QuietSkies;

/// <summary>The three kinds of marker the plugin filters. Nothing else is ever touched.</summary>
public enum SignalKind
{
	/// <summary>"Unidentified Signal": global encounter markers (IsGlobalEncounterGPS).</summary>
	Encounter,

	/// <summary>"Unknown Signal": personal cargo drops (IsContainerGPS).</summary>
	Unknown,

	/// <summary>"Strong Unknown Signal": competitive cargo drops (IsContainerGPS).</summary>
	Strong
}

public enum SignalMode
{
	Show,
	Hide,
	Nearby
}

/// <summary>Tells which kind a marker is, or that it isn't one of ours.</summary>
internal static class Signals
{
	public static readonly SignalKind[] Kinds = { SignalKind.Encounter, SignalKind.Unknown, SignalKind.Strong };

	// The game's death marker ("Body Location") is a container marker too, with this fixed colour.
	private static readonly Color BodyColor = new Color(117, 201, 241);

	private static MyContainerDropSystemDefinition s_dropDefinition;

	private static bool s_dropDefinitionRead;

	public static string Name(SignalKind kind)
	{
		switch (kind)
		{
		case SignalKind.Encounter:
			return "Unidentified Signal";
		case SignalKind.Unknown:
			return "Unknown Signal";
		default:
			return "Strong Unknown Signal";
		}
	}

	public static string ModeName(SignalMode mode)
	{
		return mode == SignalMode.Nearby ? "Nearby only" : mode.ToString();
	}

	public static void Reset()
	{
		s_dropDefinition = null;
		s_dropDefinitionRead = false;
	}

	/// <summary>
	/// The kind of a marker, or null for every marker that isn't an encounter or cargo drop marker (your own GPS,
	/// the Body Location marker, contracts, mod markers).
	/// </summary>
	public static SignalKind? KindOf(MyGps gps)
	{
		if (gps == null)
		{
			return null;
		}
		if (gps.IsGlobalEncounterGPS)
		{
			return SignalKind.Encounter;
		}
		if (!gps.IsContainerGPS)
		{
			return null;
		}
		string name = gps.Name ?? "";
		if (name.Equals(MyTexts.GetString(MySpaceTexts.GPS_Body_Location_Name), StringComparison.OrdinalIgnoreCase) || name.Equals("Body Location", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		// The server names cargo drops "<Strong Unknown Signal|Unknown Signal> <number>" in its own language.
		// Check the large name first: in English it ends in the small one.
		if (StartsWith(name, MyTexts.GetString(MySpaceTexts.ContainerDropSystemContainerLarge)) || StartsWith(name, "Strong Unknown Signal"))
		{
			return SignalKind.Strong;
		}
		if (StartsWith(name, MyTexts.GetString(MySpaceTexts.ContainerDropSystemContainerSmall)) || StartsWith(name, "Unknown Signal"))
		{
			return SignalKind.Unknown;
		}
		// A server in another language: fall back to the colours the drop system gives each kind.
		MyContainerDropSystemDefinition definition = DropDefinition();
		if (definition == null || SameColor(gps.GPSColor, BodyColor))
		{
			return null;
		}
		if (SameColor(gps.GPSColor, definition.CompetetiveContainerGPSColorFree) || SameColor(gps.GPSColor, definition.CompetetiveContainerGPSColorClaimed))
		{
			return SignalKind.Strong;
		}
		if (SameColor(gps.GPSColor, definition.PersonalContainerGPSColor))
		{
			return SignalKind.Unknown;
		}
		return null;
	}

	private static bool StartsWith(string name, string prefix)
	{
		return !string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
	}

	private static bool SameColor(Color a, Color b)
	{
		return a.R == b.R && a.G == b.G && a.B == b.B;
	}

	private static MyContainerDropSystemDefinition DropDefinition()
	{
		if (!s_dropDefinitionRead)
		{
			s_dropDefinitionRead = true;
			try
			{
				MyDefinitionId? id = MySession.Static?.GetComponent<MySessionComponentContainerDropSystem>()?.Definition;
				if (id.HasValue && !id.Value.TypeId.IsNull)
				{
					s_dropDefinition = MyDefinitionManagerBase.Static?.GetDefinition<MySessionComponentDefinition>(id.Value) as MyContainerDropSystemDefinition;
				}
			}
			catch (Exception)
			{
				s_dropDefinition = null;
			}
		}
		return s_dropDefinition;
	}
}
