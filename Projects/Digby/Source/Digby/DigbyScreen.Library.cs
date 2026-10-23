using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Digby;

/// <summary>
/// The Library page: Digby's own points (saved plan markers, copies from other libraries), or another plugin's
/// library read straight from its file, with buttons to use a point in the plan, show it on the HUD, or copy it over.
/// </summary>
public partial class DigbyScreen
{
	private MyGuiControlCombobox _source;

	private MyGuiControlTable _table;

	private MyGuiControlLabel _libraryStatus;

	private MyGuiControlButton _removeOrCopy;

	private MyGuiControlButton _copyAll;

	/// <summary>Index 0 is Digby's own library; the rest match <see cref="_foreign"/>.</summary>
	private List<ForeignLibraries.Source> _foreign = new List<ForeignLibraries.Source>();

	private List<LibraryEntry> _shown = new List<LibraryEntry>();

	private bool _sortedOnce;

	private bool Foreign => _source.GetSelectedKey() > 0;

	private void BuildLibraryPage()
	{
		List<MyGuiControlBase> page = _pages[Page.Library];
		RightLabel(-0.355f, "Library:", page);
		_source = AddCombo(-0.31f, -0.355f, 0.4f, "Digby's own library, or another plugin's marker library for this world (read only: copy points over to use them later).", page);
		_source.ItemSelected += FillTable;

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.32f),
			Size = new Vector2(0.84f, 0.56f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 3,
			VisibleRowsCount = 13
		};
		_table.SetCustomColumnWidths(new float[] { 0.5f, 0.32f, 0.18f });
		_table.SetColumnName(0, new StringBuilder("Name"));
		_table.SetColumnName(1, new StringBuilder("From"));
		_table.SetColumnName(2, new StringBuilder("From you"));
		_table.SetColumnComparison(0, (a, b) => string.Compare(Entry(a)?.Name, Entry(b)?.Name, StringComparison.OrdinalIgnoreCase));
		_table.SetColumnComparison(1, (a, b) => string.Compare(Entry(a)?.Source, Entry(b)?.Source, StringComparison.OrdinalIgnoreCase));
		_table.SetColumnComparison(2, (a, b) => DistanceFromYou(Entry(a)).CompareTo(DistanceFromYou(Entry(b))));
		page.Add(AddControl(_table));

		_libraryStatus = AddLabel(-0.42f, 0.28f, "", page);
		_libraryStatus.ColorMask = new Vector4(0.8f, 0.8f, 0.8f, 1f);

		AddButton(-0.34f, ButtonsY2, "Use as From", () => UsePoint(from: true), page).SetToolTip("Puts the selected point in the plan's From (as a typed GPS) and goes back to the plan.");
		AddButton(-0.17f, ButtonsY2, "Use as Target", () => UsePoint(from: false), page).SetToolTip("Puts the selected point in the plan's Target and goes back to the plan.");
		AddButton(0f, ButtonsY2, "Show on HUD", ShowSelected, page).SetToolTip("Adds the selected point as a local marker (never sent to the server, gone when you leave the world).");
		AddButton(0.17f, ButtonsY2, "Hide markers", HideShown, page).SetToolTip("Removes the library markers Show on HUD added.");
		_removeOrCopy = AddButton(0.34f, ButtonsY2, "Remove", RemoveOrCopy, page);
		_copyAll = AddButton(-0.34f, ButtonsY, "Copy all", CopyAll, page);
		_copyAll.SetToolTip("Copies every point of this library into Digby's, skipping ones already there.");
		AddButton(0.17f, ButtonsY, "Plan", () => ShowPage(Page.Plan), page);
		AddButton(0.34f, ButtonsY, "Help", () => ShowPage(Page.Help), page);
	}

	/// <summary>Refills the source list (other libraries come and go with their files) and the table.</summary>
	private void ShowLibrary()
	{
		long selected = _source.GetSelectedKey();
		_foreign = ForeignLibraries.Available();
		_source.ClearItems();
		_source.AddItem(0, "Digby");
		for (int i = 0; i < _foreign.Count; i++)
		{
			_source.AddItem(i + 1, _foreign[i].Name + " (read only)");
		}
		_source.SelectItemByKey(selected > 0 && selected <= _foreign.Count ? selected : 0, sendEvent: false);
		FillTable();
	}

	private void FillTable()
	{
		bool foreign = Foreign;
		_shown = foreign ? ForeignLibraries.Read(_foreign[(int)_source.GetSelectedKey() - 1]) : global::Digby.Library.Entries.ToList();
		_removeOrCopy.Text = foreign ? "Copy to Digby" : "Remove";
		_removeOrCopy.SetToolTip(foreign ? "Copies the selected point into Digby's library." : "Removes the selected point from Digby's library.");
		_copyAll.Visible = foreign && _page == Page.Library;
		_table.Clear();
		foreach (LibraryEntry entry in _shown)
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(entry);
			row.AddCell(new MyGuiControlTable.Cell(entry.Name, entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.Source ?? "", entry));
			row.AddCell(new MyGuiControlTable.Cell(Markers.FormatDistance(DistanceFromYou(entry)), entry));
			_table.Add(row);
		}
		if (_sortedOnce)
		{
			_table.Sort(switchSort: false);
		}
		else
		{
			_table.SortByColumn(0);
			_sortedOnce = true;
		}
		_libraryStatus.Text = _shown.Count == 0
			? (foreign ? "That library has no points for this world." : "No points yet. Save to library on the Plan page keeps a plan's markers here; other plugins' libraries show in the list above when they have one for this world.")
			: $"{_shown.Count} point(s)." + (foreign ? " Read from the other plugin's file; nothing is changed there." : "");
	}

	private static LibraryEntry Entry(MyGuiControlTable.Cell cell)
	{
		return cell?.UserData as LibraryEntry;
	}

	private static double DistanceFromYou(LibraryEntry entry)
	{
		Vector3D? you = MyAPIGateway.Session?.Player?.GetPosition();
		return entry == null || you == null ? 0 : Vector3D.Distance(you.Value, entry.Position);
	}

	private LibraryEntry Selected()
	{
		LibraryEntry entry = _table.SelectedRow?.UserData as LibraryEntry;
		if (entry == null)
		{
			_libraryStatus.Text = "Select a point in the list first.";
		}
		return entry;
	}

	private void UsePoint(bool from)
	{
		LibraryEntry entry = Selected();
		if (entry == null)
		{
			return;
		}
		string gps = DigPlanner.ToGps(entry.Name, entry.Position);
		if (from)
		{
			_fromText.Text = gps;
		}
		else
		{
			_targetText.Text = gps;
		}
		ShowPage(Page.Plan);
		// FillPoints picks "The GPS typed below" for a box with text in it.
	}

	private void ShowSelected()
	{
		LibraryEntry entry = Selected();
		if (entry == null)
		{
			return;
		}
		int added = global::Digby.Library.Show(new[] { entry });
		_libraryStatus.Text = added > 0 ? $"\"{entry.Name}\" is on your HUD as a local marker." : $"\"{entry.Name}\" is already on your HUD.";
	}

	private void HideShown()
	{
		_libraryStatus.Text = $"{Markers.RemoveLocal(global::Digby.Library.MarkerKind)} library marker(s) removed from your HUD.";
	}

	private void RemoveOrCopy()
	{
		LibraryEntry entry = Selected();
		if (entry == null)
		{
			return;
		}
		if (Foreign)
		{
			int added = ForeignLibraries.CopyToLibrary(new[] { entry });
			_libraryStatus.Text = added > 0 ? $"Copied \"{entry.Name}\" to Digby's library." : $"\"{entry.Name}\" is already in Digby's library.";
			return;
		}
		MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder("Remove point"), messageText: new StringBuilder($"Remove \"{entry.Name}\" from Digby's library?"), callback: answer =>
		{
			if (answer == MyGuiScreenMessageBox.ResultEnum.YES && !_closed)
			{
				global::Digby.Library.Remove(entry);
				FillTable();
			}
		}));
	}

	private void CopyAll()
	{
		if (!Foreign)
		{
			return;
		}
		int added = ForeignLibraries.CopyToLibrary(_shown);
		_libraryStatus.Text = $"Copied {added} point(s) to Digby's library ({_shown.Count - added} were already there).";
	}
}
