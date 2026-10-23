using System;
using VRage.Plugins;

namespace Digby;

public class Plugin : IPlugin, IDisposable
{
	public const string Name = "Digby";

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
