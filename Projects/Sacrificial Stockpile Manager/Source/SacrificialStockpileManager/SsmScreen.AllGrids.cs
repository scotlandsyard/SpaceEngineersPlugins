using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRageMath;

namespace SacrificialStockpileManager;

// Ticking grids in All grids to remove several from the list at once.
public partial class SsmScreen
{
	/// <summary>The tick column's share of the table width (the first column in All grids).</summary>
	private const float TickWidth = 0.05f;

	private static readonly string[] TickChoices = { "Tick...", "Not loaded now", "Not seen in 7 days", "Not seen in 30 days", "Not seen in 90 days", "Has no settings", "Clear ticks" };

	/// <summary>Ticked grids by key; kept while the window is closed, until the grids are removed.</summary>
	private static readonly HashSet<long> s_ticked = new HashSet<long>();

	private MyGuiControlCombobox _tickCombo;

	private MyGuiControlButton _removeTickedButton;

	private static string TickText(long key) => s_ticked.Contains(key) ? "[x]" : "[ ]";

	/// <summary>The Tick row above the All grids table.</summary>
	private void CreateAllGridsControls()
	{
		AddLabel(Left, Row2Y, "Tick");
		_tickCombo = AddCombo(-0.36f, Row2Y, 0.25f, TickChoices.Length, "Ticks the grids that match, for Remove ticked. Click a grid's first column or press Space to tick it yourself.");
		for (int i = 0; i < TickChoices.Length; i++)
		{
			_tickCombo.AddItem(i, TickChoices[i], i, null, sort: false);
		}
		_tickCombo.SelectItemByKey(0, sendEvent: false);
		_tickCombo.ItemSelected += () =>
		{
			int choice = (int)_tickCombo.GetSelectedKey();
			if (choice == 0)
			{
				return;
			}
			TickMatching(choice);
			_tickCombo.SelectItemByKey(0, sendEvent: false);
		};
	}

	/// <summary>Grids that can't be removed: loaded ones would come straight back, docked ones go with their dock.</summary>
	private static bool Removable(GridSnapshot grid) => grid.DockedTo == null && Session?.FindConstruct(grid.Key) == null;

	private void TickMatching(int choice)
	{
		s_ticked.Clear();
		if (choice != TickChoices.Length - 1)
		{
			DateTime now = DateTime.UtcNow;
			foreach (GridSnapshot grid in Store.Grids.Where(Removable))
			{
				bool match = choice switch
				{
					2 => now - grid.LastSeenUtc > TimeSpan.FromDays(7),
					3 => now - grid.LastSeenUtc > TimeSpan.FromDays(30),
					4 => now - grid.LastSeenUtc > TimeSpan.FromDays(90),
					5 => !Store.HasSettings(grid),
					_ => true
				};
				if (match)
				{
					s_ticked.Add(grid.Key);
				}
			}
		}
		ShowMessage(s_ticked.Count > 0 ? $"Ticked {s_ticked.Count} grid(s): {TickChoices[choice]}." : choice == TickChoices.Length - 1 ? "Cleared the ticks." : "No grid matches.", null);
		RefreshRows();
	}

	/// <summary>Ticks or unticks a grid row.</summary>
	private void ToggleTick(string key)
	{
		if (key == null || !key.StartsWith("g:", StringComparison.Ordinal) || !long.TryParse(key.Substring(2), out long gridKey))
		{
			return;
		}
		if (!s_ticked.Remove(gridKey))
		{
			s_ticked.Add(gridKey);
		}
		RefreshRows();
	}

	/// <summary>The column under the mouse; the table's own events only say which row.</summary>
	private static readonly FieldInfo s_mouseOverColumn = typeof(MyGuiControlTable).GetField("m_mouseOverColumnIndex", BindingFlags.Instance | BindingFlags.NonPublic);

	/// <summary>A left click on a row's tick column.</summary>
	private bool ClickedTickColumn()
	{
		return MyInput.Static.IsNewPrimaryButtonPressed() && s_mouseOverColumn?.GetValue(_table) is int column && column == 0;
	}

	/// <summary>Space ticks the selected grid.</summary>
	public override void HandleInput(bool receivedFocusInThisUpdate)
	{
		base.HandleInput(receivedFocusInThisUpdate);
		if (s_view == View.AllGrids && _table != null && FocusedControl == _table && MyInput.Static.IsNewKeyPressed(MyKeys.Space))
		{
			ToggleTick(SelectedKey);
		}
	}

	private void UpdateRemoveTickedButton()
	{
		s_ticked.RemoveWhere(key => Store.Grid(key) == null);
		if (_removeTickedButton != null)
		{
			_removeTickedButton.Text = $"Remove ticked ({s_ticked.Count})";
		}
	}

	private void RemoveTicked()
	{
		List<GridSnapshot> ticked = s_ticked.Select(Store.Grid).Where(g => g != null).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
		if (ticked.Count == 0)
		{
			ShowMessage("Tick grids first: click a grid's first column, press Space, or use the Tick list.", WarningColor);
			return;
		}
		List<GridSnapshot> remove = ticked.Where(Removable).ToList();
		List<GridSnapshot> skipped = ticked.Where(g => !Removable(g)).ToList();
		if (remove.Count == 0)
		{
			ShowMessage("Every ticked grid is loaded or docked, so none can be removed.", WarningColor);
			return;
		}
		int settings = remove.Sum(Store.SettingsCount);
		StringBuilder text = new StringBuilder($"Remove {remove.Count} grid(s) from the list?\n\n");
		text.Append(string.Join("\n", remove.Take(10).Select(g => "- " + g.Name)));
		if (remove.Count > 10)
		{
			text.Append($"\n- and {remove.Count - 10} more");
		}
		text.Append($"\n\nTheir settings go with them: {settings} block and display setting(s), and their grid settings and quotas. Screens on other grids that showed them go back to their own grid. Nothing on the grids themselves changes. This can't be undone: a grid that still exists comes back with default settings the next time it's in range.");
		if (skipped.Count > 0)
		{
			text.Append($"\n\nSkipped (loaded or docked): {string.Join(", ", skipped.Take(10).Select(g => g.Name))}{(skipped.Count > 10 ? $" and {skipped.Count - 10} more" : "")}.");
		}
		MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder("Remove ticked grids"), messageText: text, callback: answer =>
		{
			if (answer != MyGuiScreenMessageBox.ResultEnum.YES)
			{
				return;
			}
			int removed = 0;
			foreach (GridSnapshot grid in remove.Where(Removable))
			{
				Store.ForgetGrid(grid.Key);
				s_ticked.Remove(grid.Key);
				removed++;
			}
			ShowMessage($"Removed {removed} grid(s) and their settings.", null);
			_gridsSignature = null;
			_rowsSignature = null;
			_recreatePending = true;
		}));
	}
}
