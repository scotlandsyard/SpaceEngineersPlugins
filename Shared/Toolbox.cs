using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace TimShared;

/// <summary>
/// The Toolbox tab in the terminal (K or I): one row per loaded plugin of ours, with its status line and an Open
/// button. Every plugin links this file, but only the first one alphabetically that has it adds the tab, so it
/// shows once. The terminal is only read and added to, never patched: if the game changes its tabs, the tab is
/// left out and the terminal works as before.
/// </summary>
internal static partial class PluginSwitcher
{
	private const string ToolboxPageName = "PageTimToolbox";

	/// <summary>
	/// Key of the Toolbox page: the one after the game's own. Ctrl+Tab and the gamepad bumpers step through the
	/// pages with (key ± 1) % count and look the result up, so the keys must stay 0..count-1.
	/// </summary>
	private static int s_pageKey = -1;

	private const float RowHeight = 0.052f;

	/// <summary>Draws between status refreshes while the tab is showing.</summary>
	private const int RefreshFrames = 60;

	private static FieldInfo s_tabsField;

	private static bool s_toolboxBroken;

	private static int s_refreshIn;

	private static readonly List<KeyValuePair<string, MyGuiControlLabel>> s_statusLabels = new List<KeyValuePair<string, MyGuiControlLabel>>();

	static partial void ToolboxAttach()
	{
		s_hasToolbox = true;
		MyScreenManager.BeforeScreenDraw -= ToolboxBeforeDraw;
		MyScreenManager.BeforeScreenDraw += ToolboxBeforeDraw;
	}

	static partial void ToolboxDetach()
	{
		MyScreenManager.BeforeScreenDraw -= ToolboxBeforeDraw;
		s_statusLabels.Clear();
	}

	/// <summary>The plugin that adds the tab: the first loaded one, alphabetically, whose copy has the Toolbox.</summary>
	private static bool OwnsToolbox()
	{
		foreach (string name in s_plugins.Keys)
		{
			if (s_toolbox.Contains(name))
			{
				return name.Equals(s_name, StringComparison.OrdinalIgnoreCase);
			}
		}
		return false;
	}

	private static void ToolboxBeforeDraw(MyGuiScreenBase screen)
	{
		if (s_toolboxBroken || !s_registered || screen == null || screen.GetType().FullName != "Sandbox.Game.Gui.MyGuiScreenTerminal")
		{
			return;
		}
		try
		{
			if (!OwnsToolbox())
			{
				return;
			}
			if (s_tabsField == null)
			{
				s_tabsField = screen.GetType().GetField("m_terminalTabs", BindingFlags.Instance | BindingFlags.NonPublic);
				if (s_tabsField == null || !typeof(MyGuiControlTabControl).IsAssignableFrom(s_tabsField.FieldType))
				{
					DisableToolbox("the terminal's tab control wasn't found");
					return;
				}
			}
			// The terminal rebuilds its tabs (new control) when you switch grids or the resolution changes.
			if (!(s_tabsField.GetValue(screen) is MyGuiControlTabControl tabs))
			{
				return;
			}
			if (tabs.Controls.GetControlByName(ToolboxPageName) is MyGuiControlTabPage page)
			{
				if (page.Visible && --s_refreshIn <= 0)
				{
					RefreshStatus();
				}
				return;
			}
			AddToolboxPage(screen, tabs);
		}
		catch (Exception ex)
		{
			DisableToolbox(ex.ToString());
		}
	}

	private static void DisableToolbox(string why)
	{
		s_toolboxBroken = true;
		MyLog.Default.WriteLineAndConsole($"[{s_name}] Toolbox tab left out: {why}");
	}

	private static void AddToolboxPage(MyGuiScreenBase screen, MyGuiControlTabControl tabs)
	{
		int before = tabs.PagesCount;
		List<int> keys = tabs.Pages.Select(p => p.PageKey).OrderBy(k => k).ToList();
		// Only when the game's pages are numbered 0..N-1, so the new page N keeps tab cycling working.
		if (before == 0 || keys.Where((k, i) => k != i).Any())
		{
			DisableToolbox("the terminal's tabs aren't numbered 0.." + (before - 1) + " (" + string.Join(",", keys) + ")");
			return;
		}
		s_pageKey = before;
		float buttonScale = tabs.TabButtonScale;
		MyGuiControlTabPage page = tabs.GetTabSubControl(s_pageKey);
		page.Name = ToolboxPageName;
		page.Text = new StringBuilder("Toolbox");
		page.TextScale = 0.7005405f;
		// Narrower tab buttons, so the row of tabs keeps the width it had without this one.
		tabs.TabButtonScale = buttonScale * before / (before + 1);
		page.Visible = tabs.SelectedPage == s_pageKey;
		FillToolboxPage(screen, page);
	}

	private static void FillToolboxPage(MyGuiScreenBase screen, MyGuiControlTabPage page)
	{
		s_statusLabels.Clear();
		float left = -0.452f;
		float top = -0.332f;
		float width = 0.904f;
		page.Controls.Add(new MyGuiControlPanel
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
			Position = new Vector2(left, top),
			Size = new Vector2(width, 0.035f),
			BackgroundTexture = MyGuiConstants.TEXTURE_RECTANGLE_DARK_BORDER
		});
		MyGuiControlLabel title = new MyGuiControlLabel
		{
			Position = new Vector2(left + 0.01f, top + 0.005f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
			Text = "Our plugins loaded in this game"
		};
		// For anyone who remembers where /tim got its name.
		title.SetToolTip("More power!");
		page.Controls.Add(title);

		// The rows sit in a scrolling panel between the header and the /tim line, so any number of plugins fits
		// (it used to stop after 12 and the last row ran under the /tim line).
		List<string> names = s_plugins.Keys.ToList();
		float listTop = top + 0.045f;
		float listHeight = 0.29f - listTop;
		float contentHeight = Math.Max(names.Count * RowHeight, listHeight);
		MyGuiControlParent rows = new MyGuiControlParent
		{
			Size = new Vector2(width, contentHeight)
		};
		// Child positions are from the centre of the rows control.
		float rowLeft = -width / 2f;
		float y = -contentHeight / 2f;
		foreach (string name in names)
		{
			rows.Controls.Add(new MyGuiControlLabel
			{
				Position = new Vector2(rowLeft + 0.01f, y + RowHeight / 2f),
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
				Text = name
			});
			MyGuiControlLabel status = new MyGuiControlLabel
			{
				Position = new Vector2(rowLeft + 0.27f, y + RowHeight / 2f),
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
				ColorMask = new Vector4(0.75f, 0.8f, 0.85f, 1f),
				Text = StatusOf(name)
			};
			rows.Controls.Add(status);
			s_statusLabels.Add(new KeyValuePair<string, MyGuiControlLabel>(name, status));
			string target = name;
			// Room on the right for the scroll bar.
			MyGuiControlButton open = new MyGuiControlButton(new Vector2(rowLeft + width - 0.035f, y + RowHeight / 2f), MyGuiControlButtonStyleEnum.Small, null, null, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER, "Open " + name + "'s window", new StringBuilder("Open"), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, _ => SwitchTo(screen, target));
			open.Name = "ToolboxOpen " + name;
			rows.Controls.Add(open);
			y += RowHeight;
		}
		MyGuiControlScrollablePanel list = new MyGuiControlScrollablePanel(rows)
		{
			Name = "ToolboxRows",
			ScrollbarVEnabled = true,
			CanFocusChildren = true,
			Position = new Vector2(left + width / 2f, listTop + listHeight / 2f),
			Size = new Vector2(width, listHeight)
		};
		// Sizes the scroll bar to the rows; without it the bar stays hidden.
		list.RefreshInternals();
		page.Controls.Add(list);
		page.Controls.Add(new MyGuiControlLabel
		{
			Position = new Vector2(left + 0.01f, 0.325f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM,
			ColorMask = new Vector4(0.6f, 0.65f, 0.7f, 1f),
			Text = "/tim in chat reopens the window you used last."
		});
		s_refreshIn = RefreshFrames;
	}

	private static void RefreshStatus()
	{
		s_refreshIn = RefreshFrames;
		foreach (KeyValuePair<string, MyGuiControlLabel> row in s_statusLabels)
		{
			row.Value.Text = StatusOf(row.Key);
		}
	}

	private static string StatusOf(string name)
	{
		if (!s_status.TryGetValue(name, out Func<string> status) || status == null)
		{
			return "";
		}
		try
		{
			string text = status() ?? "";
			text = text.Replace('\n', ' ').Trim();
			return text.Length > 70 ? text.Substring(0, 69) + "…" : text;
		}
		catch
		{
			return "";
		}
	}
}
