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

	/// <summary>Tab key, well away from the game's own pages (0-6).</summary>
	private const int ToolboxPageKey = 5524813;

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
		if (tabs.Pages.Any(p => p.PageKey == ToolboxPageKey))
		{
			return;
		}
		float buttonScale = tabs.TabButtonScale;
		int before = Math.Max(1, tabs.PagesCount);
		MyGuiControlTabPage page = tabs.GetTabSubControl(ToolboxPageKey);
		page.Name = ToolboxPageName;
		page.Text = new StringBuilder("Toolbox");
		page.TextScale = 0.7005405f;
		// Narrower tab buttons, so the row of tabs keeps the width it had without this one.
		tabs.TabButtonScale = buttonScale * before / (before + 1);
		page.Visible = tabs.SelectedPage == ToolboxPageKey;
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

		List<string> names = s_plugins.Keys.ToList();
		float y = top + 0.06f;
		float maxRows = (0.6f / RowHeight);
		int i = 0;
		foreach (string name in names)
		{
			if (i++ >= maxRows)
			{
				break;
			}
			page.Controls.Add(new MyGuiControlLabel
			{
				Position = new Vector2(left + 0.01f, y + RowHeight / 2f),
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
				Text = name
			});
			MyGuiControlLabel status = new MyGuiControlLabel
			{
				Position = new Vector2(left + 0.27f, y + RowHeight / 2f),
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
				ColorMask = new Vector4(0.75f, 0.8f, 0.85f, 1f),
				Text = StatusOf(name)
			};
			page.Controls.Add(status);
			s_statusLabels.Add(new KeyValuePair<string, MyGuiControlLabel>(name, status));
			string target = name;
			MyGuiControlButton open = new MyGuiControlButton(new Vector2(left + width - 0.01f, y + RowHeight / 2f), MyGuiControlButtonStyleEnum.Small, null, null, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER, "Open " + name + "'s window", new StringBuilder("Open"), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, _ => SwitchTo(screen, target));
			open.Name = "ToolboxOpen " + name;
			page.Controls.Add(open);
			y += RowHeight;
		}
		page.Controls.Add(new MyGuiControlLabel
		{
			Position = new Vector2(left + 0.01f, 0.33f),
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
