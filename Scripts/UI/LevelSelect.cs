using System;
using Godot;

namespace BallMadness;

/// <summary>
/// A grid of the pack's levels with each one's best time and fewest falls.
/// Completed levels and the next one are playable; the rest are greyed out.
/// </summary>
public partial class LevelSelect : Control
{
	/// <summary>Called with the chosen level index.</summary>
	public Action<int> LevelChosen;

	/// <summary>Called when the player backs out (Back button, Esc or gamepad B).</summary>
	public Action Closed;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		Theme = MenuUi.Theme;

		var column = MenuUi.CenteredColumn(this, "LEVEL SELECT", 900);
		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 14);
		grid.AddThemeConstantOverride("v_separation", 10);
		column.AddChild(grid);

		string pack = SaveGame.CurrentPack;
		for (int i = 0; i < Levels.Count; i++)
		{
			int index = i;
			bool unlocked = SaveGame.IsUnlocked(pack, index);
			var button = MenuUi.AddButton(grid, LevelText(pack, index, unlocked), () => LevelChosen?.Invoke(index));
			button.Disabled = !unlocked;
			button.Alignment = HorizontalAlignment.Left;
			button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			button.AddThemeFontSizeOverride("font_size", 20);
		}

		MenuUi.AddButton(column, "Back", Close);
		FocusNextLevel(grid);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Close();
		}
	}

	private static string LevelText(string pack, int index, bool unlocked)
	{
		string name = $"{index + 1}. {LevelName(index)}";
		if (!unlocked)
			return $"{name}\n🔒 locked";

		var record = SaveGame.Record(pack, index);
		if (record == null)
			return $"{name}\nnot completed yet";
		string falls = record.FewestFalls == 1 ? "1 fall" : $"{record.FewestFalls} falls";
		return $"{name}\nbest {record.BestTime:0.0}s  ·  fewest {falls}";
	}

	private static string LevelName(int index) => Levels.NameAt(index);

	/// <summary>Starts the cursor on the level Continue would play.</summary>
	private void FocusNextLevel(GridContainer grid)
	{
		int next = Mathf.Min(SaveGame.FurthestCompleted(SaveGame.CurrentPack) + 1, Levels.Count - 1);
		if (next >= 0 && next < grid.GetChildCount() && grid.GetChild(next) is Button { Disabled: false } button)
			button.CallDeferred(Control.MethodName.GrabFocus);
		else
			MenuUi.FocusFirst(this);
	}

	private void Close()
	{
		QueueFree();
		Closed?.Invoke();
	}
}
