using System;
using Godot;

namespace BallMadness;

/// <summary>The settings screen, shared by the title screen and the pause menu.</summary>
public partial class SettingsMenu : Control
{
	/// <summary>Called when the player leaves the screen (Back button, Esc or gamepad B).</summary>
	public Action Closed;

	private Button _cameraButton;
	private Button _steeringButton;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		Theme = MenuUi.Theme;

		var column = MenuUi.CenteredColumn(this, "SETTINGS", 520);
		_cameraButton = MenuUi.AddButton(column, "", CycleCamera);
		_steeringButton = MenuUi.AddButton(column, "", CycleSteering);
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

	private void CycleSteering()
	{
		Settings.SteeringMode = Settings.SteeringMode == Settings.SteeringModes.Camera
			? Settings.SteeringModes.Tank
			: Settings.SteeringModes.Camera;
		Settings.Save();
		UpdateLabels();
	}

	private void UpdateLabels()
	{
		_steeringButton.Text = Settings.SteeringMode == Settings.SteeringModes.Tank
			? "Steering: Tank (left/right turn)"
			: "Steering: Screen direction";
		_cameraButton.Text = Settings.CameraMode == Settings.CameraModes.Follow
			? "Camera: Follow ball"
			: "Camera: Fixed isometric";
	}

	private void Close()
	{
		QueueFree();
		Closed?.Invoke();
	}
}
