using System;
using VRage.Plugins;

namespace RadarTech;

public class Plugin : IPlugin, IDisposable
{
	public const string Name = "Radar Tech";

	public static Plugin Instance { get; private set; }

	public void Init(object gameInstance)
	{
		Instance = this;
	}

	public void Update()
	{
	}

	public void Dispose()
	{
		Instance = null;
	}
}
