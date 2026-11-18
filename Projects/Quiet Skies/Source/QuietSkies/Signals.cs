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

/// <summary>The four kinds of marker the plugin filters. Nothing else is ever touched.</summary>
public enum SignalKind
{
	/// <summary>"Unidentified Signal": global encounter markers (IsGlobalEncounterGPS).</summary>
	Encounter,

	/// <summary>"Unknown Signal": personal cargo drops (IsContainerGPS).</summary>
	Unknown,

	/// <summary>"Strong Unknown Signal": competitive cargo drops (IsContainerGPS).</summary>
	Strong,

	/// <summary>
	/// "Jump Signature": temporary markers where a heavy ship came out of a jump (IsJumpSignatureGPS, entity id 0).
	/// Any Show on HUD, Always visible or edit request makes one permanent, so this kind is only ever hidden on this PC
	/// or deleted.
	/// </summary>
	Jump
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
	public static readonly SignalKind[] Kinds = { SignalKind.Encounter, SignalKind.Unknown, SignalKind.Strong, SignalKind.Jump };

	/// <summary>Kinds that Hide hides on this PC only, sending nothing to the server.</summary>
	public static bool LocalOnly(SignalKind kind) => kind == SignalKind.Jump;

	/// <summary>
	/// The id Quiet Skies keeps a marker under: its entity id, or for a Jump Signature (no entity) a negative number
	/// made from its hash, which no entity id uses. 0 means the marker can't be tracked.
	/// </summary>
	public static long Key(MyGps gps) => gps.IsJumpSignatureGPS ? -1L - (uint)gps.Hash : gps.EntityId;

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
		case SignalKind.Jump:
			return "Jump Signature";
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
		// The game clears this flag when the marker is made permanent (you edit it or tick Show on HUD in the G menu):
		// from then on it's your own GPS and never ours.
		if (gps.IsJumpSignatureGPS)
		{
			return SignalKind.Jump;
		}
		// After a world load the game marks every GPS that has an entity id as a container marker (LoadGpss), so
		// contract, objective and mod markers can carry the flag too: never count those.
		if (!gps.IsContainerGPS || gps.ContractId != 0 || gps.IsObjective)
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
		// A server in another language: fall back to the colours the drop system gives each kind, and only for names
		// shaped like a cargo drop's ("<text> <number>"), so a mod marker that happens to share a colour isn't taken.
		MyContainerDropSystemDefinition definition = DropDefinition();
		if (definition == null || SameColor(gps.GPSColor, BodyColor) || !EndsInNumber(name))
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

	private static bool EndsInNumber(string name)
	{
		string trimmed = name.TrimEnd();
		int digits = 0;
		while (digits < trimmed.Length && char.IsDigit(trimmed[trimmed.Length - 1 - digits]))
		{
			digits++;
		}
		return digits > 0 && digits < trimmed.Length && trimmed[trimmed.Length - 1 - digits] == ' ';
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
