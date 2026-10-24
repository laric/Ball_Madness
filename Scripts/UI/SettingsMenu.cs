using System;
using Godot;

namespace BallMadness;

/// <summary>The settings screen, shared by the title screen and the pause menu.</summary>
public partial class SettingsMenu : Control
{
	/// <summary>Called when the player leaves the screen (Back button, Esc or gamepad B).</summary>
	public Action Closed;

	private Button _cameraButton;
	private Button _muteButton;
	private ulong _lastPreview;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		Theme = MenuUi.Theme;

		var column = MenuUi.CenteredColumn(this, "SETTINGS", 520);
		_cameraButton = MenuUi.AddButton(column, "", CycleCamera);
		AddVolume(column, "Volume", Settings.MasterVolume, v => Settings.MasterVolume = v, "checkpoint");
		AddVolume(column, "Effects", Settings.EffectsVolume, v => Settings.EffectsVolume = v, "impact");
		AddVolume(column, "Menu sounds", Settings.MenuVolume, v => Settings.MenuVolume = v, "menu_click", Sfx.MenuBus);
		_muteButton = MenuUi.AddButton(column, "", ToggleMute);
		MenuUi.AddButton(column, "Back", Close);
		UpdateLabels();
		MenuUi.FocusFirst(this);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Close();
		}
	}

	private void CycleCamera()
	{
		Settings.CameraMode = Settings.CameraMode == Settings.CameraModes.Fixed
			? Settings.CameraModes.Follow
			: Settings.CameraModes.Fixed;
		Settings.Save();
		UpdateLabels();
	}

	/// <summary>A labelled 0-100% slider; arrows or the stick move it in 5% steps once it has focus.</summary>
	private void AddVolume(Container parent, string label, float value, Action<float> set, string preview, string bus = Sfx.EffectsBus)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);
		parent.AddChild(row);

		var name = new Label { Text = label, CustomMinimumSize = new Vector2(170, 0) };
		row.AddChild(name);
		var slider = new HSlider
		{
			MinValue = 0,
			MaxValue = 100,
			Step = 5,
			Value = Mathf.Round(value * 100),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(0, 32),
		};
		row.AddChild(slider);
		var percent = new Label { Text = $"{slider.Value:0}%", CustomMinimumSize = new Vector2(70, 0), HorizontalAlignment = HorizontalAlignment.Right };
		row.AddChild(percent);

		slider.ValueChanged += v =>
		{
			set((float)v / 100f);
			percent.Text = $"{v:0}%";
			Sfx.ApplySettings();
			// Let the player hear the new level, without a burst while dragging.
			ulong now = Time.GetTicksMsec();
			if (now - _lastPreview > 150)
			{
				_lastPreview = now;
				Sfx.Play(preview, bus: bus);
			}
		};
		// Light the label up like a focused button's text, so keyboard and pad players can see where they are.
		slider.FocusEntered += () => name.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.5f));
		slider.FocusExited += () => name.RemoveThemeColorOverride("font_color");
		slider.DragEnded += _ => Settings.Save();
		// Keyboard and gamepad changes don't drag, so save when focus moves on.
		slider.FocusExited += Settings.Save;
	}

	private void ToggleMute()
	{
		Settings.Muted = !Settings.Muted;
		Settings.Save();
		Sfx.ApplySettings();
		UpdateLabels();
	}

	private void UpdateLabels()
	{
		_muteButton.Text = Settings.Muted ? "Sound: Off (M)" : "Sound: On (M)";
		_cameraButton.Text = Settings.CameraMode == Settings.CameraModes.Follow
			? "Camera: Follow ball"
			: "Camera: Fixed isometric";
	}

	private void Close()
	{
		Settings.Save();
		QueueFree();
		Closed?.Invoke();
	}
}
