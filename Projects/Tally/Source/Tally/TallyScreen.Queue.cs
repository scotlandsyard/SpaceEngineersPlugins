using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace Tally;

/// <summary>The Queue page: pick an assembler or group, see what would be queued, then queue it (or take it back out).</summary>
public partial class TallyScreen
{
	private MyGuiControlCombobox _target;

	private MyGuiControlCheckbox _countQueued;

	private MyGuiControlCheckbox _minusCargo;

	private MyGuiControlMultilineText _plan;

	private List<Target> _targets = new List<Target>();

	private QueuePlan _queuePlan;

	private void BuildQueuePage()
	{
		List<MyGuiControlBase> page = _pages[Page.Queue];
		RightLabel(-0.355f, "Queue in:", page);
		_target = AddCombo(-0.31f, -0.355f, 0.55f, "The assembler, or group of assemblers, to queue the components in. Only assemblers you have access to.", page);
		_target.ItemSelected += () =>
		{
			if (!_filling)
			{
				Settings.QueueTarget = SelectedTarget()?.Key ?? Settings.QueueTarget;
				ShowPlan();
			}
		};
		AddButton(0.34f, -0.355f, "Refresh list", () =>
		{
			FillTargets();
			ShowPlan();
		}, page).SetToolTip("Looks again for assemblers and groups.");

		_countQueued = AddCheckbox(-0.31f, -0.3f, "Count what's already queued there", "What these assemblers already have queued, or finished and waiting in their output, counts as on its way, so pressing Queue twice doesn't queue the bill twice.", Settings.QueueCountQueued, page);
		_minusCargo = AddCheckbox(0.12f, -0.3f, "Minus the assemblers' grid", "Takes off what's already in the inventories of the assemblers' grid and what's docked to it (blocks you have access to), so only the shortfall is queued. Open the grid's inventory first for current numbers. Greyed out when the tally already took that grid's cargo off.", Settings.QueueMinusCargo, page);
		foreach (MyGuiControlCheckbox box in new[] { _countQueued, _minusCargo })
		{
			box.IsCheckedChanged += _ =>
			{
				if (!_filling)
				{
					Settings.QueueCountQueued = _countQueued.IsChecked;
					Settings.QueueMinusCargo = _minusCargo.IsChecked;
					ShowPlan();
				}
			};
		}

		_plan = new MyGuiControlMultilineText(new Vector2(0f, -0.26f), new Vector2(0.84f, 0.6f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
		};
		page.Add(AddControl(_plan));

		AddButton(-0.34f, ButtonsY, "Queue", Queue, page).SetToolTip("Queues the components listed above, after asking.");
		AddButton(-0.17f, ButtonsY, "Undo last queue", UndoQueue, page).SetToolTip("Takes Tally's last queue back out of the assemblers, as far as it hasn't been started or passed on to co-op assemblers.");
		AddButton(0.34f, ButtonsY, "Back", () => ShowPage(Page.Tally), page);
	}

	/// <summary>Tallies again (so the queue matches what's built and carried now), then shows the Queue page.</summary>
	private void OpenQueue()
	{
		if (SelectedSource() == null && _bill != null)
		{
			ShowPage(Page.Queue);
			return;
		}
		Calculate(() => ShowPage(Page.Queue));
	}

	private void FillTargets()
	{
		string kept = SelectedTarget()?.Key ?? Settings.QueueTarget;
		_filling = true;
		try
		{
			_targets = Assemblers.Targets();
			_target.ClearItems();
			for (int i = 0; i < _targets.Count; i++)
			{
				string label = _targets[i].Label;
				_target.AddItem(i, label.Length > 80 ? label.Substring(0, 80) + "..." : label);
			}
			if (_targets.Count == 0)
			{
				_target.AddItem(-1, "(no assembler you have access to nearby)");
			}
			_target.SelectItemByKey(_targets.Count == 0 ? -1 : Math.Max(0, _targets.FindIndex(t => t.Key == kept)));
		}
		finally
		{
			_filling = false;
		}
	}

	private Target SelectedTarget()
	{
		long key = _target?.GetSelectedKey() ?? -1;
		return key >= 0 && key < _targets.Count ? _targets[(int)key] : null;
	}

	/// <summary>Works out and shows what Queue would do. Nothing is sent.</summary>
	private void ShowPlan()
	{
		_queuePlan = null;
		_plan.Clear();
		Target target = SelectedTarget();
		if (_bill == null)
		{
			_plan.AppendText("Tally something first (Back, then Tally).");
			return;
		}
		if (target == null)
		{
			_plan.AppendText("Pick an assembler or a group. Only assemblers you have access to are listed; Refresh list looks again.");
			return;
		}
		bool cargoTaken = Assemblers.SameCargo(_bill.CargoGridId, target.GridId);
		_minusCargo.Enabled = !cargoTaken;
		try
		{
			_queuePlan = Assemblers.Plan(_bill, target, _countQueued.IsChecked, _minusCargo.IsChecked && !cargoTaken);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Tally] Queue plan: {ex}");
			_plan.AppendText("That couldn't be worked out: " + ex.Message);
			return;
		}
		QueuePlan plan = _queuePlan;
		StringBuilder text = new StringBuilder();
		text.Append($"{Text.Summary(_bill)}");
		text.Append(_bill.Subtracted ? $" Queuing what's still to get after what's in {_bill.HaveFrom}.\n\n" : "\n\n");
		if (plan.Orders.Count == 0)
		{
			text.Append(plan.Assemblers.Count == 0 ? "Nothing can be queued there.\n" : "Nothing to queue: every component is covered or can't be made there.\n");
		}
		else
		{
			int assemblers = plan.Orders.Select(o => o.Assembler).Distinct().Count();
			text.Append($"Queue will add {plan.Runs:N0} runs of {plan.Orders.Count} component{(plan.Orders.Count == 1 ? "" : "s")} on {assemblers} assembler{(assemblers == 1 ? "" : "s")}:\n");
			foreach (Order order in plan.Orders.OrderBy(o => o.Assembler.CustomName.ToString()).ThenByDescending(o => o.Makes))
			{
				text.Append($"- {order.Name}: {Text.Amount(order.Makes)} on {order.Assembler.CustomName}{(order.Makes > order.Runs + 1e-6 ? $" ({order.Runs:N0} runs)" : "")}\n");
			}
		}
		if (plan.Covered.Count > 0)
		{
			text.Append($"\nAlready covered by what's queued or in the cargo: {string.Join(", ", plan.Covered)}.\n");
		}
		if (plan.Skipped.Count > 0)
		{
			text.Append("\nNot queued:\n");
			foreach (string skipped in plan.Skipped)
			{
				text.Append($"- {skipped}\n");
			}
		}
		foreach (string warning in plan.Warnings)
		{
			text.Append($"\n{warning}\n");
		}
		text.Append("\nOnly components are queued: the ingots and ore come from refineries.");
		_plan.AppendText(text.ToString());
	}

	private void Queue()
	{
		ShowPlan();
		QueuePlan plan = _queuePlan;
		if (plan == null || plan.Orders.Count == 0)
		{
			return;
		}
		int assemblers = plan.Orders.Select(o => o.Assembler).Distinct().Count();
		Ask("Queue in assemblers", $"Queue {plan.Runs:N0} runs of {plan.Orders.Count} component{(plan.Orders.Count == 1 ? "" : "s")} on {assemblers} assembler{(assemblers == 1 ? "" : "s")}?\n\nUndo last queue takes it back out.", () =>
		{
			try
			{
				Dictionary<long, double> before = Assemblers.Send(plan);
				TallySession.ExpectQueue(plan.Orders, before, QueueReport);
				QueueReport($"Sent {plan.Runs:N0} runs to {assemblers} assembler{(assemblers == 1 ? "" : "s")}.");
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Tally] Queue: {ex}");
				QueueReport("Queuing failed: " + ex.Message);
			}
		});
	}

	private void UndoQueue()
	{
		if (Assemblers.LastQueue == null)
		{
			QueueReport("Nothing queued by Tally to take back (only the last queue this session can be undone).");
			return;
		}
		Ask("Undo last queue", $"Take Tally's last queue ({Assemblers.LastQueue.Sum(o => o.Runs):N0} runs) back out of the assemblers? Runs already started, or passed on to co-op assemblers, stay.", () =>
		{
			int removed = Assemblers.Undo(out int queued);
			QueueReport(removed == queued ? $"Asked the assemblers to remove all {queued:N0} runs." : $"Asked the assemblers to remove {removed:N0} of the {queued:N0} runs; the rest was already started or passed on.");
		});
	}

	/// <summary>A queue result line in the window (while it's open) and in chat.</summary>
	private void QueueReport(string line)
	{
		TallySession.Say(line);
		if (!_closed)
		{
			_plan.AppendText("\n\n" + line);
		}
	}

	private static void Ask(string caption, string text, Action yes)
	{
		MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder(caption), messageText: new StringBuilder(text), callback: answer =>
		{
			if (answer == MyGuiScreenMessageBox.ResultEnum.YES)
			{
				yes();
			}
		}));
	}
}
