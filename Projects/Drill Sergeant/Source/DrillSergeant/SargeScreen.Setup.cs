using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace DrillSergeant;

/// <summary>The Setup page: a rig's blocks picked from lists, and its settings. Changes only count after Save.</summary>
public partial class SargeScreen
{
	private const float LeftLabel = -0.42f, LeftControl = -0.27f, LeftWidth = 0.25f;

	private const float RightLabel = 0.02f, RightControl = 0.29f;

	private static readonly string[] RoleNames = { "Not used", "Horizontal", "Vertical", "Vertical, inverted" };

	private static readonly string[] AlertNames = { "Off", "Chat", "Sound", "Chat and sound" };

	private readonly List<MyGuiControlBase> _setupControls = new List<MyGuiControlBase>();

	/// <summary>Shown only for a script-mode rig.</summary>
	private readonly List<MyGuiControlBase> _scriptOnly = new List<MyGuiControlBase>();

	private Rig _draft;

	private Rig _editing;

	private MyGuiControlTextbox _nameBox;

	private MyGuiControlCombobox _rotorBox, _modeBox, _pbBox, _roleBox, _cargoBox, _lcdBox, _doneTimerBox, _fullTimerBox, _alertBox;

	private MyGuiControlTable _pistonTable;

	private MyGuiControlLabel _drillLabel, _autoLabel;

	private MyGuiControlButton _applyButton, _continueButton;

	private readonly Dictionary<string, MyGuiControlTextbox> _numbers = new Dictionary<string, MyGuiControlTextbox>();

	private readonly Dictionary<string, MyGuiControlCheckbox> _checks = new Dictionary<string, MyGuiControlCheckbox>();

	private List<IMyMotorStator> _rotors = new List<IMyMotorStator>();

	private List<IMyTerminalBlock> _pbs = new List<IMyTerminalBlock>(), _cargos = new List<IMyTerminalBlock>(), _lcds = new List<IMyTerminalBlock>(), _timers = new List<IMyTerminalBlock>();

	private RigBlocks.Reachout _reach;

	private void CreateSetup()
	{
		float y = -0.33f;
		AddLabel(LeftLabel, y, "Name", _setupControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		_nameBox = new MyGuiControlTextbox(new Vector2(LeftControl, y), "", 32)
		{
			Size = new Vector2(LeftWidth, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		Add(_nameBox, _setupControls);
		y += 0.05f;
		_rotorBox = AddCombo(LeftLabel, y, "Rotor", "The rig's rotor. Listed: rotors with a head within 150 m of you (or on the ship you control) that you may use. Sarge then finds the pistons and drills on the rotor's head side.", _setupControls);
		_rotorBox.ItemSelected += OnRotorSelected;
		y += 0.05f;
		_modeBox = AddCombo(LeftLabel, y, "Mode", "Plugin: Sarge drives the rig itself, no script needed, while you're in reach. Script: the Automatic Mining Platform script by Kezeslabas drives it from a programmable block; Sarge sets it up and sends its commands.", _setupControls);
		_modeBox.AddItem(0, "Plugin drives the rig", sort: false);
		_modeBox.AddItem(1, "Script drives the rig", sort: false);
		_modeBox.ItemSelected += () =>
		{
			_draft.Mode = _modeBox.GetSelectedKey() == 1 ? RigMode.Script : RigMode.Plugin;
			UpdateSetupVisibility();
		};
		y += 0.05f;
		_pbBox = AddCombo(LeftLabel, y, "Script PB", "The programmable block that runs (or will run) the Automatic Mining Platform script for this rig.", _scriptOnly);
		_setupControls.AddRange(_scriptOnly);

		_pistonTable = new MyGuiControlTable
		{
			Position = new Vector2(LeftLabel, y + 0.03f),
			Size = new Vector2(0.4f, 0.16f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
			ColumnsCount = 2,
			VisibleRowsCount = 3
		};
		_pistonTable.SetCustomColumnWidths(new float[] { 0.6f, 0.4f });
		_pistonTable.SetColumnName(0, new StringBuilder("Piston on the arm"));
		_pistonTable.SetColumnName(1, new StringBuilder("Job"));
		_pistonTable.ItemSelected += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) => ShowRole();
		Add(_pistonTable, _setupControls);
		y += 0.23f;
		_roleBox = AddCombo(LeftLabel, y, "Job", "Horizontal pistons push the drills outwards, vertical ones push them down. Inverted: a vertical piston mounted the other way round, that retracts to go down. Sarge guesses from the way each piston points.", _setupControls);
		for (int i = 0; i < RoleNames.Length; i++)
		{
			_roleBox.AddItem(i, RoleNames[i], sort: false);
		}
		_roleBox.ItemSelected += OnRoleSelected;
		y += 0.05f;
		_drillLabel = AddLabel(LeftLabel, y, "", _setupControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		y += 0.05f;
		_cargoBox = AddCombo(LeftLabel, y, "Cargo", "What the cargo pause watches. The drills' own inventories, or containers your conveyors fill.", _setupControls);
		y += 0.05f;
		_lcdBox = AddCombo(LeftLabel, y, "LCD", "Optional: an LCD of yours that shows the rig's state. Plugin mode writes it at most every 10 s; script mode has the script write it.", _setupControls);
		y += 0.05f;
		_doneTimerBox = AddCombo(LeftLabel, y, "When done", "Optional: a timer block started when the rig has finished.", _setupControls);
		y += 0.05f;
		_fullTimerBox = AddCombo(LeftLabel, y, "When full", "Optional: a timer block started when the cargo pause sets in.", _setupControls);

		y = -0.33f;
		AddNumber(ref y, "min", "Rotor angle from", "Where each sweep starts or ends, degrees (-358 to 358). 0 to 358 sweeps a full circle.");
		AddNumber(ref y, "max", "Rotor angle to", "The other end of the sweep. At least 10 degrees more than the start, at most 360.");
		AddNumber(ref y, "rpm", "Rotor rpm at 10 m", "Rotor speed for drills 10 m from the axis. Drills nearer or farther turn the rotor faster or slower, so the tips always cut at the same speed. The script's default is 0.5.");
		AddNumber(ref y, "hspeed", "Horizontal speed m/s", "How fast the arm moves out or in, all horizontal pistons together.");
		AddNumber(ref y, "vspeed", "Vertical speed m/s", "How fast the drills go down, all vertical pistons together.");
		AddNumber(ref y, "hstep", "Horizontal step m", "How far the arm moves out between sweeps. 0 = worked out from the drills: the width they cut, less 10%.");
		AddNumber(ref y, "vstep", "Vertical step m", "How far the drills go down between layers. 0 = worked out from the drills (2.4 m for large drills).");
		_autoLabel = AddLabel(RightLabel, y - 0.012f, "", _setupControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		y += 0.03f;
		AddNumber(ref y, "high", "Pause at cargo %", "The rig pauses when the cargo is this full...");
		AddNumber(ref y, "low", "Carry on at cargo %", "...and carries on once it's down to this.");
		_alertBox = AddCombo(RightLabel, y, "Alerts", "How Sarge tells you a rig finished, paused for cargo, or needs you. Only on your screen.", _setupControls, RightControl, 0.13f);
		for (int i = 0; i < AlertNames.Length; i++)
		{
			_alertBox.AddItem(i, AlertNames[i], sort: false);
		}
		y += 0.05f;
		AddCheck(ref y, "cargo", "Pause when the cargo is full", "Stops rotor, pistons and drills at the % above and carries on at the lower one.");
		AddCheck(ref y, "retract", "Pull the arm in before going down", "Every layer starts from the inside. Off: the arm works its way back in on the next layer, which saves the empty trip.");
		AddCheck(ref y, "tensor", "Share inertia tensor on start", "Ticks Share inertia tensor on the rotor and pistons when the rig starts, which keeps big rigs steadier. Only where the world allows it and the game has Experimental mode on, like the terminal box.");
		AddCheck(ref y, "refresh", "Rotor tensor refresh (script's option)", "While sweeping, the rotor's tensor sharing is off and comes on for 2 s once a minute: the script's fix for rotors that stall when sharing. 2 requests a minute. Off by default.");

		_applyButton = AddButton(-0.105f, ButtonRow2, "Apply to script", ApplyToScript, _scriptOnly, "Saves, then puts the script's tags in the block names and writes these settings into the script's settings in the PB's Custom Data. Load the script into the PB first.");
		_continueButton = AddButton(0.105f, ButtonRow2, "Continue here", ContinueInScript, _scriptOnly, "Sends \"set;N\" and \"start\" to the script, so it carries on from the step Sarge got to in plugin mode.");
		_setupControls.Add(_applyButton);
		_setupControls.Add(_continueButton);
		AddButton(-0.315f, ButtonRow2, "Save", () => SaveSetup(), _setupControls, "Saves the rig. Mode changes happen here too.");
		AddButton(0.315f, ButtonRow2, "Back", () => ShowPage(_mainControls), _setupControls, "Leaves without saving.");
	}

	private MyGuiControlCombobox AddCombo(float labelX, float y, string label, string toolTip, List<MyGuiControlBase> page, float controlX = LeftControl, float width = LeftWidth)
	{
		AddLabel(labelX, y, label, page, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER).SetToolTip(toolTip);
		MyGuiControlCombobox combo = new MyGuiControlCombobox(new Vector2(controlX, y), new Vector2(width, 0.04f), null, null, 10, null, false, toolTip, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Add(combo, page);
		return combo;
	}

	private void AddNumber(ref float y, string key, string label, string toolTip)
	{
		AddLabel(RightLabel, y, label, _setupControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER).SetToolTip(toolTip);
		MyGuiControlTextbox box = new MyGuiControlTextbox(new Vector2(RightControl, y), "", 8)
		{
			Size = new Vector2(0.13f, 0.04f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		box.SetToolTip(toolTip);
		Add(box, _setupControls);
		_numbers[key] = box;
		y += 0.045f;
	}

	private void AddCheck(ref float y, string key, string label, string toolTip)
	{
		MyGuiControlCheckbox box = new MyGuiControlCheckbox(new Vector2(RightLabel + 0.015f, y), null, toolTip);
		Add(box, _setupControls);
		AddLabel(RightLabel + 0.04f, y, label, _setupControls, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER).SetToolTip(toolTip);
		_checks[key] = box;
		y += 0.04f;
	}

	private void UpdateSetupVisibility()
	{
		bool script = _draft?.Mode == RigMode.Script;
		foreach (MyGuiControlBase control in _scriptOnly)
		{
			control.Visible = script;
		}
		if (_draft != null)
		{
			_continueButton.Text = $"Continue at {ScriptLink.HandoverStep(_draft)}";
		}
	}

	// ---------------------------------------------------------------- opening

	private void OpenSetup(Rig rig)
	{
		_editing = rig;
		_draft = rig == null ? new Rig { Name = $"Rig {RigStore.Rigs.Count + 1}" } : Clone(rig);
		_rotors = RigBlocks.NearbyRotors();
		IMyMotorStator current = RigBlocks.Get<IMyMotorStator>(_draft.RotorId);
		if (current != null && !_rotors.Contains(current))
		{
			_rotors.Insert(0, current);
		}
		if (rig != null && current == null)
		{
			ShowPage(_mainControls);
			SetStatus($"{rig.Name}'s rotor isn't loaded here. Go closer to change its setup.");
			return;
		}
		_nameBox.Text = _draft.Name;
		_rotorBox.ItemSelected -= OnRotorSelected;
		_rotorBox.ClearItems();
		for (int i = 0; i < _rotors.Count; i++)
		{
			IMyMotorStator rotor = _rotors[i];
			string used = RigStore.Find(rotor.EntityId) is Rig other && other.RotorId != _draft.RotorId ? $" (rig {other.Name})" : "";
			_rotorBox.AddItem(i, $"{rotor.CustomName} on {rotor.CubeGrid.CustomName}{used}", sort: false);
		}
		_rotorBox.ItemSelected += OnRotorSelected;
		_modeBox.SelectItemByKey(_draft.Mode == RigMode.Script ? 1 : 0, sendEvent: false);
		FillNumbers();
		ShowPage(_setupControls);
		if (_rotors.Count == 0)
		{
			SetStatus("No rotors with a head near you. Stand within 150 m of the rig (or control a ship that carries it).");
			ClearBlockLists();
			return;
		}
		int index = Math.Max(0, _rotors.IndexOf(current));
		_rotorBox.SelectItemByKey(index, sendEvent: false);
		OnRotorSelected();
	}

	private static Rig Clone(Rig rig)
	{
		Rig copy = MyAPIGateway.Utilities.SerializeFromXML<Rig>(MyAPIGateway.Utilities.SerializeToXML(rig));
		copy.Live = rig.Live;
		return copy;
	}

	private void FillNumbers()
	{
		CultureInfo invariant = CultureInfo.InvariantCulture;
		_numbers["min"].Text = _draft.MinAngle.ToString("0.#", invariant);
		_numbers["max"].Text = _draft.MaxAngle.ToString("0.#", invariant);
		_numbers["rpm"].Text = _draft.RpmAt10.ToString("0.###", invariant);
		_numbers["hspeed"].Text = _draft.HorizontalSpeed.ToString("0.###", invariant);
		_numbers["vspeed"].Text = _draft.VerticalSpeed.ToString("0.###", invariant);
		_numbers["hstep"].Text = _draft.HorizontalStep.ToString("0.#", invariant);
		_numbers["vstep"].Text = _draft.VerticalStep.ToString("0.#", invariant);
		_numbers["high"].Text = (_draft.CargoHigh * 100).ToString("0", invariant);
		_numbers["low"].Text = (_draft.CargoLow * 100).ToString("0", invariant);
		_checks["cargo"].IsChecked = _draft.CargoPause;
		_checks["retract"].IsChecked = _draft.RetractFirst;
		_checks["tensor"].IsChecked = _draft.ShareTensor;
		_checks["refresh"].IsChecked = _draft.TensorRefresh;
		_alertBox.SelectItemByKey((int)_draft.Alerts, sendEvent: false);
	}

	private void ClearBlockLists()
	{
		_pistonTable.Clear();
		foreach (MyGuiControlCombobox box in new[] { _pbBox, _cargoBox, _lcdBox, _doneTimerBox, _fullTimerBox })
		{
			box.ClearItems();
		}
		_drillLabel.Text = "";
		_autoLabel.Text = "";
	}

	/// <summary>A rotor was picked: find what's on its arm and fill the lists, keeping earlier choices for blocks still there.</summary>
	private void OnRotorSelected()
	{
		int index = (int)_rotorBox.GetSelectedKey();
		if (index < 0 || index >= _rotors.Count)
		{
			return;
		}
		IMyMotorStator rotor = _rotors[index];
		bool sameRotor = rotor.EntityId == _draft.RotorId;
		_draft.RotorId = rotor.EntityId;
		HashSet<IMyCubeGrid> arm = RigBlocks.ArmGrids(rotor);
		List<IMyCubeGrid> construct = RigBlocks.Construct(rotor);
		List<IMyShipDrill> drills = RigBlocks.BlocksOn<IMyShipDrill>(arm);
		_draft.DrillIds = drills.Select(d => d.EntityId).ToList();
		_reach = RigBlocks.Measure(rotor, drills);

		Dictionary<long, PistonRole> known = sameRotor ? _draft.Pistons.ToDictionary(p => p.Id, p => p.Role) : new Dictionary<long, PistonRole>();
		_draft.Pistons = RigBlocks.BlocksOn<IMyPistonBase>(arm).Select(p => new RigPiston
		{
			Id = p.EntityId,
			Role = known.TryGetValue(p.EntityId, out PistonRole role) ? role : RigBlocks.GuessRole(p, rotor, _reach?.Down ?? -rotor.WorldMatrix.Up)
		}).ToList();
		FillPistonTable();

		_drillLabel.Text = drills.Count == 0 ? "No drills on this rotor's arm." : $"Drills: all {drills.Count} on the arm" + (_reach != null ? $", cutting {_reach.Inner:0.0} to {_reach.Outer:0.0} m from the axis." : ".");
		_autoLabel.Text = _reach == null ? "" : $"(0 = auto: horizontal {RigBlocks.AutoHorizontalStep(_reach):0.0} m, vertical {RigBlocks.AutoVerticalStep(_reach):0.0} m)";

		_cargos = RigBlocks.BlocksOn<IMyCargoContainer>(construct).Cast<IMyTerminalBlock>().ToList();
		FillBlockCombo(_cargoBox, _cargos, _draft.CargoIds.Count == 1 ? _draft.CargoIds[0] : 0, "The drills themselves", _cargos.Count > 1 ? "All cargo containers on the rig" : null, _draft.CargoIds.Count > 1);
		_lcds = RigBlocks.BlocksOn<IMyTextPanel>(construct).Cast<IMyTerminalBlock>().ToList();
		FillBlockCombo(_lcdBox, _lcds, _draft.LcdId, "None");
		_timers = RigBlocks.BlocksOn<IMyTimerBlock>(construct).Cast<IMyTerminalBlock>().ToList();
		FillBlockCombo(_doneTimerBox, _timers, _draft.FinishedTimerId, "None");
		FillBlockCombo(_fullTimerBox, _timers, _draft.CargoTimerId, "None");
		List<IMyCubeGrid> logical = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(rotor.CubeGrid, GridLinkTypeEnum.Logical, logical);
		_pbs = RigBlocks.BlocksOn<IMyProgrammableBlock>(logical.Count > 0 ? logical : construct).Cast<IMyTerminalBlock>().ToList();
		long pb = _draft.ScriptPbId != 0 ? _draft.ScriptPbId : _pbs.FirstOrDefault(p => ScriptLink.Fields(p as IMyProgrammableBlock) != null)?.EntityId ?? 0;
		FillBlockCombo(_pbBox, _pbs, pb, "None");
		if (string.IsNullOrWhiteSpace(_nameBox.Text) || (_editing == null && _nameBox.Text.StartsWith("Rig ", StringComparison.Ordinal)))
		{
			_nameBox.Text = rotor.CubeGrid.CustomName.Length <= 24 ? rotor.CubeGrid.CustomName : _nameBox.Text;
		}
		SetStatus(RigStore.Find(rotor.EntityId) is Rig other && other != _editing ? $"That rotor already belongs to {other.Name}." : "");
	}

	private void FillPistonTable()
	{
		_pistonTable.Clear();
		foreach (RigPiston entry in _draft.Pistons)
		{
			IMyPistonBase piston = RigBlocks.Get<IMyPistonBase>(entry.Id);
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(entry);
			string name = piston?.CustomName ?? "(gone)";
			row.AddCell(new MyGuiControlTable.Cell(name, entry, name));
			row.AddCell(new MyGuiControlTable.Cell(RoleNames[(int)entry.Role], entry));
			_pistonTable.Add(row);
		}
		if (_pistonTable.RowsCount > 0)
		{
			_pistonTable.SelectedRowIndex = 0;
		}
		ShowRole();
	}

	private void ShowRole()
	{
		if (_pistonTable.SelectedRow?.UserData is RigPiston entry)
		{
			_roleBox.SelectItemByKey((int)entry.Role, sendEvent: false);
		}
	}

	private void OnRoleSelected()
	{
		if (_pistonTable.SelectedRow?.UserData is RigPiston entry)
		{
			entry.Role = (PistonRole)_roleBox.GetSelectedKey();
			int selected = _pistonTable.SelectedRowIndex ?? 0;
			FillPistonTable();
			_pistonTable.SelectedRowIndex = selected;
			ShowRole();
		}
	}

	/// <summary>Key -1: the "none" entry; -2: the "all" entry; others index the list.</summary>
	private static void FillBlockCombo(MyGuiControlCombobox box, List<IMyTerminalBlock> blocks, long selectedId, string noneText, string allText = null, bool allSelected = false)
	{
		box.ClearItems();
		box.AddItem(-1, noneText, sort: false);
		if (allText != null)
		{
			box.AddItem(-2, allText, sort: false);
		}
		for (int i = 0; i < blocks.Count; i++)
		{
			box.AddItem(i, blocks[i].CustomName, sort: false);
		}
		int index = blocks.FindIndex(b => b.EntityId == selectedId);
		box.SelectItemByKey(allSelected && allText != null ? -2 : index, sendEvent: false);
	}

	private static long PickedId(MyGuiControlCombobox box, List<IMyTerminalBlock> blocks)
	{
		long key = box.GetSelectedKey();
		return key >= 0 && key < blocks.Count ? blocks[(int)key].EntityId : 0;
	}

	// ---------------------------------------------------------------- saving

	private bool SaveSetup()
	{
		Rig rig = _draft;
		if (rig == null || RigBlocks.Get<IMyMotorStator>(rig.RotorId) == null)
		{
			SetStatus("Pick a rotor first.");
			return false;
		}
		Rig owner = RigStore.Find(rig.RotorId);
		if (owner != null && owner != _editing)
		{
			SetStatus($"That rotor already belongs to {owner.Name}.");
			return false;
		}
		string problem = ReadNumbers(rig);
		if (problem != null)
		{
			SetStatus(problem);
			return false;
		}
		rig.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "Rig" : _nameBox.Text.Trim();
		long cargoKey = _cargoBox.GetSelectedKey();
		rig.CargoIds = cargoKey == -2 ? _cargos.Select(c => c.EntityId).ToList() : cargoKey >= 0 ? new List<long> { PickedId(_cargoBox, _cargos) } : new List<long>();
		rig.LcdId = PickedId(_lcdBox, _lcds);
		rig.FinishedTimerId = PickedId(_doneTimerBox, _timers);
		rig.CargoTimerId = PickedId(_fullTimerBox, _timers);
		rig.ScriptPbId = PickedId(_pbBox, _pbs);
		rig.Alerts = (AlertKind)Math.Max(0, _alertBox.GetSelectedKey());
		rig.CargoPause = _checks["cargo"].IsChecked;
		rig.RetractFirst = _checks["retract"].IsChecked;
		rig.ShareTensor = _checks["tensor"].IsChecked;
		rig.TensorRefresh = _checks["refresh"].IsChecked;
		if (rig.DrillIds.Count == 0)
		{
			SetStatus("This rotor has no drills on its arm.");
			return false;
		}
		if (rig.Mode == RigMode.Script && rig.ScriptPbId == 0)
		{
			SetStatus("Script mode needs the programmable block: pick it under Script PB.");
			return false;
		}

		// Measure the arm and fix the step lengths the plan uses.
		RigParts parts = RigParts.Resolve(rig);
		if (_reach != null)
		{
			rig.RadiusRetracted = (float)Math.Max(1, _reach.Outer - RigBlocks.HorizontalExtension(parts.Horizontal));
		}
		float oldH = rig.PlanHorizontalStep, oldV = rig.PlanVerticalStep;
		rig.PlanHorizontalStep = rig.HorizontalStep > 0 ? rig.HorizontalStep : RigBlocks.AutoHorizontalStep(_reach);
		rig.PlanVerticalStep = rig.VerticalStep > 0 ? rig.VerticalStep : RigBlocks.AutoVerticalStep(_reach);
		Pattern pattern = Driver.PatternFor(rig, parts);
		string note = "";
		bool planChanged = _editing != null && (Math.Abs(oldH - rig.PlanHorizontalStep) > 0.001f || Math.Abs(oldV - rig.PlanVerticalStep) > 0.001f || !SamePistons(_editing, rig));
		if (rig.Step > pattern.LastStep || (planChanged && rig.Step > 0))
		{
			rig.Step = Math.Min(rig.Step - rig.Step % 2, pattern.LastStep);
			rig.SweepLeftStart = false;
			rig.MoveHorizontalDone = false;
			note = $" The plan changed: progress kept at step {rig.Step} of {pattern.LastStep}.";
		}
		rig.Live.Issued = false;

		// Mode changes go through the switch, so the plugin and the script never both drive the rig.
		RigMode wanted = rig.Mode;
		RigMode was = _editing?.Mode ?? wanted;
		rig.Mode = was;
		if (_editing != null)
		{
			RigStore.Rigs[RigStore.Rigs.IndexOf(_editing)] = rig;
		}
		else
		{
			RigStore.Rigs.Add(rig);
		}
		_editing = rig;
		_draft = Clone(rig);
		string switched = "";
		if (wanted != was)
		{
			switched = " " + (wanted == RigMode.Script ? ScriptLink.SwitchToScript(rig) : ScriptLink.SwitchToPlugin(rig, SargeSession.Now));
			_draft.Mode = rig.Mode;
			_modeBox.SelectItemByKey(rig.Mode == RigMode.Script ? 1 : 0, sendEvent: false);
		}
		s_selectedRotor = rig.RotorId;
		RigStore.Save();
		UpdateSetupVisibility();
		SetStatus($"{rig.Name} saved: {pattern.Rings + 1} ring(s) x {pattern.Layers + 1} layer(s), {pattern.LastStep} steps.{note}{switched}");
		return true;
	}

	private static bool SamePistons(Rig a, Rig b)
	{
		return a.Pistons.Where(p => p.Role != PistonRole.Unused).OrderBy(p => p.Id).Select(p => p.Id + ":" + p.Role)
			.SequenceEqual(b.Pistons.Where(p => p.Role != PistonRole.Unused).OrderBy(p => p.Id).Select(p => p.Id + ":" + p.Role));
	}

	private string ReadNumbers(Rig rig)
	{
		if (!Number("min", -358, 358, out float min) || !Number("max", -358, 358, out float max))
		{
			return "Rotor angles are degrees from -358 to 358.";
		}
		if (max - min < 10 || max - min > 360)
		{
			return "The rotor's sweep (to minus from) has to be 10 to 360 degrees.";
		}
		if (!Number("rpm", 0.05f, 5, out float rpm))
		{
			return "Rotor rpm at 10 m: 0.05 to 5.";
		}
		if (!Number("hspeed", 0.01f, 5, out float hspeed) || !Number("vspeed", 0.01f, 5, out float vspeed))
		{
			return "Piston speeds: 0.01 to 5 m/s.";
		}
		if (!Number("hstep", 0, 50, out float hstep) || !Number("vstep", 0, 50, out float vstep) || (hstep > 0 && hstep < 0.2f) || (vstep > 0 && vstep < 0.2f))
		{
			return "Steps: 0 (auto) or 0.2 to 50 m.";
		}
		if (!Number("high", 5, 100, out float high) || !Number("low", 0, 99, out float low) || low >= high)
		{
			return "Cargo: pause at 5-100%, carry on at a lower % than that.";
		}
		rig.MinAngle = min;
		rig.MaxAngle = max;
		rig.RpmAt10 = rpm;
		rig.HorizontalSpeed = hspeed;
		rig.VerticalSpeed = vspeed;
		// Tenths, like the automatic steps: the script reads finer values with a rounding that nudges them up.
		rig.HorizontalStep = (float)Math.Round(hstep, 1);
		rig.VerticalStep = (float)Math.Round(vstep, 1);
		rig.CargoHigh = high / 100f;
		rig.CargoLow = low / 100f;
		return null;
	}

	private bool Number(string key, float min, float max, out float value)
	{
		string text = (_numbers[key].Text ?? "").Trim().Replace(',', '.');
		return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
	}

	// ---------------------------------------------------------------- script mode buttons

	private void ApplyToScript()
	{
		if (!SaveSetup())
		{
			return;
		}
		if (_editing.Mode != RigMode.Script)
		{
			SetStatus("Pick Script mode and Save first.");
			return;
		}
		SetStatus(ScriptLink.Apply(_editing));
	}

	private void ContinueInScript()
	{
		Rig rig = _editing;
		if (rig == null || rig.Mode != RigMode.Script)
		{
			SetStatus("Pick Script mode and Save first.");
			return;
		}
		int step = ScriptLink.HandoverStep(rig);
		string result = ScriptLink.Send(rig, $"set;{step}");
		if (result.StartsWith(rig.Name, StringComparison.Ordinal))
		{
			result = ScriptLink.Send(rig, "start");
		}
		SetStatus(result.StartsWith(rig.Name, StringComparison.Ordinal) ? $"{rig.Name}: the script carries on at step {step}." : result);
	}
}
