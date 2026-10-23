using System;
using Godot;

namespace BallMadness;

/// <summary>
/// Builds the menus' look in code: one theme for buttons and labels, and
/// helpers for a centred column of buttons. Every menu works with the mouse,
/// the keyboard (arrows, Enter, Esc) and a gamepad (D-pad or stick, A, B).
/// </summary>
public static class MenuUi
{
	private static Theme _theme;

	public static Theme Theme => _theme ??= CreateTheme();

	private static Theme _compactTheme;

	public static Theme CompactTheme => _compactTheme ??= CreateCompactTheme();

	private static Theme CreateTheme()
	{
		var theme = new Theme { DefaultFontSize = 26 };

		theme.SetStylebox("normal", "Button", Box(new Color(0.12f, 0.1f, 0.28f, 0.9f), new Color(0.45f, 0.4f, 0.8f)));
		theme.SetStylebox("hover", "Button", Box(new Color(0.22f, 0.18f, 0.48f, 0.95f), new Color(0.7f, 0.65f, 1f)));
		theme.SetStylebox("pressed", "Button", Box(new Color(0.3f, 0.25f, 0.6f, 1f), new Color(1f, 0.85f, 0.3f)));
		theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), new Color(1f, 0.85f, 0.3f), 3));
		theme.SetStylebox("disabled", "Button", Box(new Color(0.08f, 0.08f, 0.1f, 0.75f), new Color(0.25f, 0.25f, 0.3f)));
		theme.SetColor("font_color", "Button", new Color(0.95f, 0.95f, 1f));
		theme.SetColor("font_hover_color", "Button", Colors.White);
		theme.SetColor("font_focus_color", "Button", new Color(1f, 0.9f, 0.5f));
		theme.SetColor("font_pressed_color", "Button", new Color(1f, 0.9f, 0.5f));
		theme.SetColor("font_disabled_color", "Button", new Color(0.45f, 0.45f, 0.5f));

		theme.SetColor("font_outline_color", "Label", Colors.Black);
		theme.SetConstant("outline_size", "Label", 6);
		theme.SetStylebox("panel", "PanelContainer", Box(new Color(0.04f, 0.03f, 0.1f, 0.85f), new Color(0.45f, 0.4f, 0.8f), 2, 24));
		return theme;
	}

	/// <summary>The same look at a smaller size, for the editor and pack manager.</summary>
	private static Theme CreateCompactTheme()
	{
		var theme = new Theme { DefaultFontSize = 16 };
		theme.SetStylebox("normal", "Button", Box(new Color(0.12f, 0.1f, 0.28f, 0.9f), new Color(0.45f, 0.4f, 0.8f), 1, 4));
		theme.SetStylebox("hover", "Button", Box(new Color(0.22f, 0.18f, 0.48f, 0.95f), new Color(0.7f, 0.65f, 1f), 1, 4));
		theme.SetStylebox("pressed", "Button", Box(new Color(0.35f, 0.28f, 0.7f, 1f), new Color(1f, 0.85f, 0.3f), 2, 4));
		theme.SetStylebox("hover_pressed", "Button", Box(new Color(0.4f, 0.32f, 0.75f, 1f), new Color(1f, 0.85f, 0.3f), 2, 4));
		theme.SetStylebox("disabled", "Button", Box(new Color(0.08f, 0.08f, 0.1f, 0.75f), new Color(0.25f, 0.25f, 0.3f), 1, 4));
		theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), new Color(1f, 0.85f, 0.3f), 2, 4));
		theme.SetColor("font_disabled_color", "Button", new Color(0.45f, 0.45f, 0.5f));
		theme.SetColor("font_outline_color", "Label", Colors.Black);
		theme.SetConstant("outline_size", "Label", 4);
		theme.SetStylebox("panel", "PanelContainer", Box(new Color(0.04f, 0.03f, 0.1f, 0.85f), new Color(0.45f, 0.4f, 0.8f), 1, 6));
		foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
			theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty());
		return theme;
	}

	public static StyleBoxFlat Box(Color fill, Color border, int borderWidth = 2, int padding = 10)
	{
		var box = new StyleBoxFlat
		{
			BgColor = fill,
			BorderColor = border,
			ContentMarginLeft = padding * 2,
			ContentMarginRight = padding * 2,
			ContentMarginTop = padding,
			ContentMarginBottom = padding,
		};
		box.SetBorderWidthAll(borderWidth);
		box.SetCornerRadiusAll(8);
		return box;
	}

	/// <summary>A full-screen control that centres a panel holding a column; returns the column.</summary>
	public static VBoxContainer CenteredColumn(Control parent, string heading = null, int width = 420)
	{
		var center = new CenterContainer();
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		parent.AddChild(center);

		var panel = new PanelContainer();
		center.AddChild(panel);

		var column = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
		column.AddThemeConstantOverride("separation", 14);
		panel.AddChild(column);

		if (heading != null)
			AddHeading(column, heading, 40);
		return column;
	}

	public static Label AddHeading(Container parent, string text, int size)
	{
		var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
		label.AddThemeFontSizeOverride("font_size", size);
		parent.AddChild(label);
		return label;
	}

	public static Button AddButton(Container parent, string text, Action pressed)
	{
		var button = new Button { Text = text };
		button.Pressed += pressed;
		parent.AddChild(button);
		return button;
	}

	/// <summary>Focuses the first enabled button under <paramref name="node"/> so keys and gamepads work at once.</summary>
	public static void FocusFirst(Node node)
	{
		var button = FindFirstEnabled(node);
		button?.CallDeferred(Control.MethodName.GrabFocus);
	}

	private static Button FindFirstEnabled(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is Button { Disabled: false, Visible: true } button)
				return button;
			var found = FindFirstEnabled(child);
			if (found != null)
				return found;
		}
		return null;
	}
}
