using System;
using VRage.Plugins;

namespace Hush;

public class Plugin : IPlugin, IDisposable
{
	public const string Name = "Hush";

	public void Init(object gameInstance)
	{
		Patches.Apply();
	}

	public void Update()
	{
	}

	public void Dispose()
	{
		Patches.Remove();
	}
}
