using System.Collections.Generic;
using System.Xml.Serialization;

namespace DrillSergeant;

public enum RigMode
{
	/// <summary>The plugin runs the rotor, pistons and drills itself.</summary>
	Plugin,

	/// <summary>The Automatic Mining Platform script runs the rig in a programmable block; the plugin sets it up and sends it commands.</summary>
	Script
}

public enum PistonRole
{
	Unused,
	Horizontal,
	Vertical,
	VerticalInverted
}

public enum AlertKind
{
	Off,
	Chat,
	Sound,
	ChatAndSound
}

/// <summary>What the player asked a plugin-driven rig to do. The state shown in the window is worked out from this and from what the rig is doing.</summary>
public enum RigCommand
{
	/// <summary>Never started, or stopped: progress is back at the start.</summary>
	Idle,

	Running,

	Paused,

	Finished
}

public class RigPiston
{
	[XmlAttribute]
	public long Id;

	[XmlAttribute]
	public PistonRole Role;
}

/// <summary>One mining rig: the blocks, the settings and (for a plugin-driven rig) how far it got. Saved per world in DrillSergeant_&lt;world&gt;_Rigs.xml.</summary>
public class Rig
{
	public string Name = "Rig";

	public RigMode Mode = RigMode.Plugin;

	/// <summary>The rotor's entity id; it is also the rig's id.</summary>
	public long RotorId;

	public List<RigPiston> Pistons = new List<RigPiston>();

	public List<long> DrillIds = new List<long>();

	/// <summary>Inventories whose fill decides the cargo pause. Empty: the drills' own.</summary>
	public List<long> CargoIds = new List<long>();

	public long LcdId;

	public long FinishedTimerId;

	public long CargoTimerId;

	// ---- Settings (the script's defaults)

	public float MinAngle = 0f;

	public float MaxAngle = 358f;

	public bool CargoPause = true;

	/// <summary>Pause at this fill, 0..1.</summary>
	public float CargoHigh = 0.9f;

	/// <summary>Carry on at this fill, 0..1.</summary>
	public float CargoLow = 0.5f;

	/// <summary>Rotor speed at 10 m from the axis; nearer or farther drills turn slower or faster so the tips keep this speed.</summary>
	public float RpmAt10 = 0.5f;

	/// <summary>Combined speed of the horizontal pistons, m/s.</summary>
	public float HorizontalSpeed = 0.3f;

	/// <summary>Combined speed of the vertical pistons, m/s.</summary>
	public float VerticalSpeed = 0.3f;

	/// <summary>Metres per vertical step; 0 = worked out from the drills.</summary>
	public float VerticalStep;

	/// <summary>Metres per horizontal step; 0 = worked out from the drills.</summary>
	public float HorizontalStep;

	public bool RetractFirst;

	/// <summary>Turn on Share inertia tensor on the rotor and pistons when the rig starts (only where the world and the game allow it).</summary>
	public bool ShareTensor = true;

	/// <summary>The script's "dynamic rotor tensor": while mining, the rotor's tensor sharing is off and comes on for two seconds once a minute. Off by default.</summary>
	public bool TensorRefresh;

	public AlertKind Alerts = AlertKind.Chat;

	// ---- Plugin mode progress

	public RigCommand Command = RigCommand.Idle;

	public int Step;

	/// <summary>The rotor starts the current or next sweep from its min limit.</summary>
	public bool AtMinSide = true;

	/// <summary>The current sweep has been well away from its target, so arriving there means it's done.</summary>
	public bool SweepLeftStart;

	/// <summary>The current move has finished its horizontal half (moves that change both go horizontal first).</summary>
	public bool MoveHorizontalDone;

	public bool CargoPaused;

	/// <summary>Paused while out of reach: the blocks still have to be switched off once it's back in reach.</summary>
	public bool PauseOwed;

	/// <summary>Step lengths and ranges the progress was made with; a changed rig restarts its plan from the nearest position.</summary>
	public float PlanHorizontalStep;

	public float PlanVerticalStep;

	/// <summary>Outer drill radius with the horizontal pistons all the way in, measured at setup.</summary>
	public float RadiusRetracted;

	// ---- Script mode

	public long ScriptPbId;

	public string ScriptTag = "";

	[XmlIgnore]
	public RigRuntime Live = new RigRuntime();
}

/// <summary>What a rig is doing right now. Never saved.</summary>
public class RigRuntime
{
	public string State = "";

	public string Detail = "";

	public double Progress;

	public double Depth;

	public double MaxDepth;

	public double CargoFill = -1;

	public double SecondsLeft = -1;

	public bool InReach = true;

	public bool Issued;

	public double IssuedAt;

	public double LastMovedAt;

	public double LastValue = double.NaN;

	public bool StuckWarned;

	public string Warning;

	public double CargoCheckedAt = -100;

	public string LastLcdText;

	public double LcdWrittenAt = -100;

	public double ReachCheckedAt = -100;

	public double TensorAt = -100;

	public bool TensorOn = true;

	public string LastAlertState;

	// Script mode

	public string ScriptScreen;

	public double ScriptScreenChangedAt = -100;

	public bool ScriptRunning;

	/// <summary>The step the script's screen shows.</summary>
	public int ScriptStep;

	public long SubscribedPb;

	/// <summary>Plugin mode was picked; waiting for the script to stop and its PB to go off.</summary>
	public bool PendingPluginMode;

	/// <summary>Plugin mode: a PB found running the script for this rig, looked for every 10 s.</summary>
	public long FoundPbId;

	public double ScriptSearchedAt = -100;
}
