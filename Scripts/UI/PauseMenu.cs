using Godot;

namespace BallMadness;

/// <summary>
/// Pauses the game on Esc or the gamepad's Start button and offers Resume,
/// Restart level, Settings and Title screen. Keeps running while the tree is
/// paused.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	private Control _root;
	private Control _page;

	public bool IsOpen => _root.Visible;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 10;

		_root = new Control { Theme = MenuUi.Theme, Visible = false };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(dim);
	}

	public void Open()
	{
		if (IsOpen)
			return;

		GetTree().Paused = true;
		// The camera captures the mouse while orbiting; menus need it back.
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_root.Visible = true;
		ShowMain();
	}

	public void Resume()
	{
		ClosePage();
		_root.Visible = false;
		GetTree().Paused = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsOpen)
			return;

		// The settings page handles its own Esc / B to go back here.
		if (@event.IsActionPressed("pause") || (@event.IsActionPressed("ui_cancel") && _page is not SettingsMenu))
		{
			GetViewport().SetInputAsHandled();
			Resume();
		}
	}

	private void ShowMain()
	{
		ClosePage();
		var column = MenuUi.CenteredColumn(_root, "PAUSED", 420);
		_page = column.GetParent().GetParent<Control>();
		MenuUi.AddButton(column, "Resume", Resume);
		MenuUi.AddButton(column, "Restart level", Restart);
		MenuUi.AddButton(column, "Settings", ShowSettings);
		MenuUi.AddButton(column, "Title screen", ToTitle);
		MenuUi.FocusFirst(column);
	}

	private void ShowSettings()
	{
		ClosePage();
		var settings = new SettingsMenu { Closed = () => { _page = null; ShowMain(); } };
		_page = settings;
		_root.AddChild(settings);
	}

	private void ClosePage()
	{
		_page?.QueueFree();
		_page = null;
	}

	private void Restart()
	{
		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}

	private void ToTitle()
	{
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(Title.ScenePath);
	}
}
