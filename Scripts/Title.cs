using System;
using Godot;

namespace BallMadness;

/// <summary>
/// The title screen and the game's main scene: Continue, Level select,
/// Settings and Quit over a slowly turning view of the first level.
///
/// Running with <c>-- --level N</c> or <c>-- --autopilot</c> skips straight
/// to the game so testing works as before.
/// </summary>
public partial class Title : Node3D
{
	public const string ScenePath = "res://Scenes/Title.tscn";

	/// <summary>Radians per second the background camera turns.</summary>
	[Export] public float OrbitSpeed = 0.08f;

	private Camera3D _camera;
	private Vector3 _orbitCenter;
	private float _orbitAngle = Mathf.Pi / 4;
	private Control _ui;
	private Control _mainMenu;

	public override void _Ready()
	{
		string[] args = OS.GetCmdlineUserArgs();
		if (Array.Exists(args, a => a == "--autopilot" || a == "--level" || a.StartsWith("--level=")))
		{
			// Can't change scene while the tree is still adding this one.
			GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, Game.ScenePath);
			return;
		}

		_camera = GetNode<Camera3D>("Camera3D");
		_ui = GetNode<Control>("Ui/Root");
		_ui.Theme = MenuUi.Theme;
		AddBackgroundLevel();
		ShowMainMenu();
	}

	public override void _Process(double delta)
	{
		if (_camera == null)
			return;

		_orbitAngle += OrbitSpeed * (float)delta;
		var offset = new Vector3(Mathf.Sin(_orbitAngle), 0.9f, Mathf.Cos(_orbitAngle)) * 60.0f;
		_camera.LookAtFromPosition(_orbitCenter + offset, _orbitCenter);
	}

	/// <summary>Shows level 1 behind the menu, centred between its spawn and goal.</summary>
	private void AddBackgroundLevel()
	{
		if (Levels.Count == 0)
			return;

		var level = GD.Load<PackedScene>(Levels.PathFor(Levels.Numbers[0])).Instantiate<Node3D>();
		// Scenery only: no hazards moving or areas firing behind the menu.
		level.ProcessMode = ProcessModeEnum.Disabled;
		AddChild(level);

		var start = level.GetNodeOrNull<Node3D>("Spawn")?.GlobalPosition ?? Vector3.Zero;
		var end = start;
		foreach (Node node in GetTree().GetNodesInGroup("goal"))
		{
			if (node is Node3D goal && level.IsAncestorOf(goal))
				end = goal.GlobalPosition;
		}
		_orbitCenter = (start + end) / 2;
		_camera.Size = Mathf.Clamp(start.DistanceTo(end) * 1.1f, 25.0f, 70.0f);
	}

	private void ShowMainMenu()
	{
		var column = MenuUi.CenteredColumn(_ui, null, 420);
		_mainMenu = column.GetParent().GetParent<Control>();
		MenuUi.AddHeading(column, "BALL MADNESS", 56);

		string pack = SaveGame.CurrentPack;
		int next = ContinueIndex();
		string continueText = SaveGame.FurthestCompleted(pack) < 0
			? "Start"
			: $"Continue  ·  level {next + 1}";
		MenuUi.AddButton(column, continueText, () => StartLevel(next));
		MenuUi.AddButton(column, "Level select", ShowLevelSelect);
		MenuUi.AddButton(column, "Settings", ShowSettings);
		MenuUi.AddButton(column, "Quit", () => GetTree().Quit());
		MenuUi.FocusFirst(column);
	}

	/// <summary>The level after the last one completed (the last level once all are done).</summary>
	public static int ContinueIndex() =>
		Mathf.Clamp(SaveGame.FurthestCompleted(SaveGame.CurrentPack) + 1, 0, Mathf.Max(Levels.Count - 1, 0));

	public void ShowLevelSelect()
	{
		var select = new LevelSelect { LevelChosen = StartLevel, Closed = BackToMainMenu };
		OpenSubmenu(select);
	}

	public void ShowSettings()
	{
		var settings = new SettingsMenu { Closed = BackToMainMenu };
		OpenSubmenu(settings);
	}

	private void OpenSubmenu(Control menu)
	{
		_mainMenu.QueueFree();
		_mainMenu = null;
		_ui.AddChild(menu);
	}

	private void BackToMainMenu() => ShowMainMenu();

	private void StartLevel(int index)
	{
		Levels.StartAt(index);
		GetTree().ChangeSceneToFile(Game.ScenePath);
	}
}
