using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digby;

/// <summary>
/// Which planet a point is on, and what to call it. The game only knows a planet's type (its generator, e.g.
/// EarthLike), and servers often call their planets something else, so a name can be typed with /digby planet name.
/// Without one, the name the player gave the planet in Fat Albert is used if that plugin has saved one (read only).
/// </summary>
internal static class Planets
{
	/// <summary>The planet whose gravity, air or terrain the point is in, or null in space.</summary>
	public static MyPlanet At(Vector3D position)
	{
		MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(position);
		if (planet == null)
		{
			return null;
		}
		double distance = (position - planet.PositionComp.GetPosition()).Length();
		float gravityLimit = planet.Components.Get<MyGravityProviderComponent>()?.GravityLimit ?? 0f;
		double reach = Math.Max(Math.Max(gravityLimit, planet.HasAtmosphere ? planet.AtmosphereRadius : 0f), planet.MaximumRadius);
		return distance <= reach ? planet : null;
	}

	public static string TypeName(MyPlanet planet)
	{
		return planet.Generator?.Id.SubtypeName ?? planet.StorageName ?? "Planet";
	}

	/// <summary>The typed name, then Fat Albert's, else null.</summary>
	public static string GivenName(MyPlanet planet, out string from)
	{
		from = null;
		if (Settings.PlanetNames.TryGetValue(planet.EntityId, out string typed))
		{
			from = "typed";
			return typed;
		}
		string fat = FatAlbertName(planet.EntityId);
		if (fat != null)
		{
			from = "Fat Albert";
			return fat;
		}
		return null;
	}

	/// <summary>"Agaris (EarthLike)" or "EarthLike".</summary>
	public static string Describe(MyPlanet planet)
	{
		string type = TypeName(planet);
		string name = GivenName(planet, out _);
		return name == null || name.Equals(type, StringComparison.OrdinalIgnoreCase) ? type : $"{name} ({type})";
	}

	/// <summary>"on planet Agaris (EarthLike), 40 m below the surface" or "in space".</summary>
	public static string Where(Vector3D position)
	{
		MyPlanet planet = At(position);
		if (planet == null)
		{
			return "in space";
		}
		Vector3D centre = planet.PositionComp.GetPosition();
		Vector3D surface = planet.GetClosestSurfacePointGlobal(ref position);
		double depth = (surface - centre).Length() - (position - centre).Length();
		string height = depth > 1 ? $"{depth:N0} m below the surface" : depth < -1 ? $"{-depth:N0} m above the surface" : "at the surface";
		return $"on planet {Describe(planet)}, {height}";
	}

	/// <summary>Where the player (or what they control) is now.</summary>
	public static string WhereYouAre()
	{
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (player?.Character == null && player?.Controller?.ControlledEntity == null)
		{
			return "nowhere yet (no character)";
		}
		return Where(player.GetPosition());
	}

	public static MyPlanet YourPlanet()
	{
		IMyPlayer player = MyAPIGateway.Session?.Player;
		return player == null ? null : At(player.GetPosition());
	}

	/// <summary>Fat Albert keeps "planet entity id=name|source" lines per world; read it if it's there, never write it.</summary>
	private static string FatAlbertName(long planetId)
	{
		try
		{
			string path = Path.Combine(MyFileSystem.UserDataPath, "Storage", "FatAlbert", $"FatAlbert_{Settings.World}_PlanetNames.txt");
			if (!File.Exists(path))
			{
				return null;
			}
			string prefix = planetId + "=";
			foreach (string line in File.ReadAllLines(path))
			{
				if (line.StartsWith(prefix, StringComparison.Ordinal))
				{
					string value = line.Substring(prefix.Length);
					int bar = value.IndexOf('|');
					value = (bar >= 0 ? value.Substring(0, bar) : value).Trim();
					return value.Length > 0 ? value : null;
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}
}
