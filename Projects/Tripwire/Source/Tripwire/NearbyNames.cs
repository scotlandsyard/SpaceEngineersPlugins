using System.Collections.Generic;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using VRage.Game.Gui;
using VRageMath;

namespace Tripwire;

/// <summary>
/// Optional and off by default (Settings.NameNearby): names the players near a damaged block, but only players whose
/// name the game's own HUD is drawing for you at that moment. Those are the characters in the HUD's location markers
/// (relayed to you by antennas, or every player for an admin with Show Players on), and only while the world shows
/// player names on the HUD. It never gives a position, distance or direction, only the name the HUD already shows.
///
/// Kept in this one file so it can be removed if the Plugin Store asks: delete this file, the NameNearby setting
/// (Settings.cs) and its checkbox (TripwireScreen.cs), and the single call in Alerts.Add.
/// </summary>
internal static class NearbyNames
{
	/// <summary>The same reach as a sensor block's largest field.</summary>
	public const double Range = 50.0;

	public static string Find(Vector3D blockPosition)
	{
		if (!Settings.NameNearby || MySession.Static == null || !MyHud.CheckShowPlayerNamesOnHud())
		{
			return "";
		}
		List<string> names = null;
		long me = MySession.Static.LocalCharacter?.EntityId ?? 0;
		foreach (MyHudEntityParams marker in MyHud.LocationMarkers.MarkerEntities)
		{
			if (!(marker.Entity is MyCharacter character) || character.EntityId == me || !character.IsPlayer || character.IsDead)
			{
				continue;
			}
			if ((marker.FlagsEnum & MyHudIndicatorFlagsEnum.SHOW_TEXT) == 0 || (marker.ShouldDraw != null && !marker.ShouldDraw()))
			{
				continue;
			}
			if (Vector3D.DistanceSquared(character.PositionComp.GetPosition(), blockPosition) > Range * Range)
			{
				continue;
			}
			// The text the HUD draws for them (faction tag and name).
			string name = marker.Text?.ToString() ?? character.DisplayNameText;
			(names ??= new List<string>()).Add(name);
		}
		return names == null ? "" : string.Join(", ", names);
	}
}
