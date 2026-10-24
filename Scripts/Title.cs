using System;
using System.Collections.Generic;
using System.Linq;
using BallMadness.Editing;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness;

/// <summary>
/// The title screen and the game's main scene: Continue, Level select, the
/// pack to play (when there's more than one), the Level editor, Settings and
/// Quit over a slowly turning view of the pack's first level.
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
	private Control _packPage;

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
		Levels.ApplyCommandLine(); // -- --pack ID opens on that pack
		Levels.ReloadPack();
		// The title screen is dressed in the current pack's world.
		var world = Levels.CurrentPack.Theme;
		MenuUi.Use(world);
		_ui.Theme = MenuUi.Theme;
		AddBackgroundLevel(world);
		ShowMainMenu();

		// Back from the level editor: reopen the pack manager on the same pack.
		if (EditorSession.ReturnToPack is string pack)
		{
			EditorSession.ReturnToPack = null;
			ShowPackManager(pack);
		}
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
	private void AddBackgroundLevel(ThemeFile world)
	{
		if (Levels.Count == 0)
		{
			Themes.ApplyEnvironment(this, world);
			return;
		}

		var file = Packs.LoadLevel(Levels.CurrentPack.LevelPaths[0]);
		Themes.ApplyEnvironment(this, world, file.KillHeight);
		var level = LevelBuilder.Build(file, world);
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
		if (Levels.CurrentPack.Theme.Id != Themes.ClassicId || PlayablePacks().Count > 1)
			AddWorldLine(column, Levels.CurrentPack);

		if (Campaign.IsWorld(Levels.CurrentPack))
		{
			// The campaign carries on from the first world not yet finished.
			var world = Campaign.ContinueWorld();
			int level = ContinueIndex(world);
			string text = Campaign.IndexOf(world) == 0 && SaveGame.FurthestCompleted(world.Id) < 0
				? "Start"
				: $"Continue  ·  world {Campaign.IndexOf(world) + 1}-{level + 1}";
			MenuUi.AddButton(column, text, () => StartLevel(world, level));
			MenuUi.AddButton(column, "Level select", ShowWorldSelect);
		}
		else
		{
			var pack = Levels.CurrentPack;
			int next = ContinueIndex(pack);
			string continueText = SaveGame.FurthestCompleted(pack.Id) < 0
				? "Start"
				: $"Continue  ·  level {next + 1}";
			MenuUi.AddButton(column, continueText, () => StartLevel(pack, next));
			MenuUi.AddButton(column, "Level select", ShowLevelSelect);
		}
		if (PlayablePacks().Count > 1)
			MenuUi.AddButton(column, Campaign.IsWorld(Levels.CurrentPack) ? "Pack: Campaign" : $"Pack: {Levels.CurrentPack.Name}", ShowPackSelect);
		MenuUi.AddButton(column, "Level editor", () => ShowPackManager(null));
		MenuUi.AddButton(column, "Settings", ShowSettings);
		MenuUi.AddButton(column, "Quit", () => GetTree().Quit());
		MenuUi.FocusFirst(column);
	}

	/// <summary>The pack's name and world under the heading, in the world's accent colour.</summary>
	private static void AddWorldLine(Container column, Pack pack)
	{
		var line = MenuUi.AddHeading(column, $"{pack.Name}  ·  {pack.Theme.Name}", 22);
		line.AddThemeColorOverride("font_color", MenuUi.Accent.Lerp(Colors.White, 0.3f));
	}

	/// <summary>The level after the last one completed in the pack (its last level once all are done).</summary>
	public static int ContinueIndex(Pack pack) =>
		Mathf.Clamp(SaveGame.FurthestCompleted(pack.Id) + 1, 0, Mathf.Max(pack.LevelPaths.Count - 1, 0));

	public void ShowLevelSelect()
	{
		var pack = Levels.CurrentPack;
		var select = new LevelSelect { LevelChosen = i => StartLevel(pack, i), Closed = BackToMainMenu };
		OpenSubmenu(select);
	}

	/// <summary>The campaign's worlds, each in its own colours; locked worlds are greyed out.</summary>
	public void ShowWorldSelect()
	{
		var page = new Control();
		page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		var column = MenuUi.CenteredColumn(page, "CHOOSE A WORLD", 900);
		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 14);
		grid.AddThemeConstantOverride("v_separation", 10);
		column.AddChild(grid);
		var focus = Campaign.ContinueWorld();
		foreach (var world in Campaign.Worlds)
		{
			var pack = world;
			bool unlocked = Campaign.IsUnlocked(pack);
			int done = SaveGame.FurthestCompleted(pack.Id) + 1;
			string status = unlocked ? $"{done}/{pack.LevelPaths.Count} completed" : "🔒 finish the world before";
			var button = MenuUi.AddButton(grid, $"{pack.Name}\n{status}", () => ShowWorldLevels(pack));
			button.Disabled = !unlocked;
			button.Alignment = HorizontalAlignment.Left;
			button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			button.AddThemeFontSizeOverride("font_size", 20);
			button.Theme = MenuUi.ThemeFor(pack.Theme);
			if (pack.Id == focus?.Id)
				button.CallDeferred(Control.MethodName.GrabFocus);
		}
		MenuUi.AddButton(column, "Back", BackToMainMenu);
		OpenSubmenu(page);
		_packPage = page;
	}

	/// <summary>One world's levels, shown in that world's colours.</summary>
	private void ShowWorldLevels(Pack world)
	{
		var previous = Levels.CurrentPack;
		Levels.SelectPack(world.Id);
		MenuUi.Use(world.Theme);
		_ui.Theme = MenuUi.Theme;
		var select = new LevelSelect
		{
			LevelChosen = i => StartLevel(world, i),
			// Back to the title, redressed in this world if it changed.
			Closed = () =>
			{
				if (world.Id != previous.Id)
					GetTree().ReloadCurrentScene();
				else
					BackToMainMenu();
			},
		};
		OpenSubmenu(select);
	}

	/// <summary>
	/// What the pack button offers: the campaign once (as its first world),
	/// then every other pack with at least one level.
	/// </summary>
	private static List<Pack> PlayablePacks()
	{
		var packs = Packs.All.Where(p => p.LevelPaths.Count > 0 && !Campaign.IsWorld(p)).ToList();
		if (Campaign.Exists)
			packs.Insert(0, Campaign.Worlds[0]);
		return packs;
	}

	public void ShowPackSelect()
	{
		var page = new Control();
		page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		var column = MenuUi.CenteredColumn(page, "PLAY WHICH PACK?", 620);
		foreach (var pack in PlayablePacks())
		{
			bool campaign = Campaign.IsWorld(pack);
			// The campaign opens on the world to carry on in.
			string id = campaign ? Campaign.ContinueWorld().Id : pack.Id;
			int done = campaign
				? Campaign.Worlds.Sum(w => SaveGame.FurthestCompleted(w.Id) + 1)
				: SaveGame.FurthestCompleted(id) + 1;
			int total = campaign ? Campaign.LevelCount : pack.LevelPaths.Count;
			string count = total == 1 ? "1 level" : $"{total} levels";
			var button = MenuUi.AddButton(column, $"{pack.Name}\n{count}  ·  {done} completed", () =>
			{
				Levels.SelectPack(id);
				GetTree().ReloadCurrentScene();
			});
			button.AddThemeFontSizeOverride("font_size", 22);
			// Each pack's button in its own world's colours.
			button.Theme = MenuUi.ThemeFor(pack.Theme);
			if (campaign)
				button.Text = $"Campaign  ·  {Campaign.Worlds.Count} worlds\n{count}  ·  {done} completed";
			else if (pack.Theme.Id != Themes.ClassicId)
				button.Text = $"{pack.Name}  ·  {pack.Theme.Name}\n{count}  ·  {done} completed";
			if (pack.Id == Levels.CurrentPack.Id || (campaign && Campaign.IsWorld(Levels.CurrentPack)))
				button.CallDeferred(Control.MethodName.GrabFocus);
		}
		MenuUi.AddButton(column, "Back", BackToMainMenu);
		OpenSubmenu(page);
		_packPage = page;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// The other submenus handle Esc / B themselves.
		if (_packPage != null && IsInstanceValid(_packPage) && _mainMenu == _packPage && @event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			BackToMainMenu();
		}
	}

	public void ShowPackManager(string packId)
	{
		var manager = new PackManager { InitialPackId = packId, Closed = BackToMainMenu };
		OpenSubmenu(manager);
	}

	public void ShowSettings()
	{
		var settings = new SettingsMenu { Closed = BackToMainMenu };
		OpenSubmenu(settings);
	}

	private void OpenSubmenu(Control menu)
	{
		_mainMenu?.QueueFree();
		_mainMenu = menu;
		_ui.AddChild(menu);
	}

	private void BackToMainMenu()
	{
		_mainMenu?.QueueFree();
		_mainMenu = null;
		ShowMainMenu();
	}

	private void StartLevel(Pack pack, int index)
	{
		if (pack.Id != Levels.CurrentPack.Id)
			Levels.SelectPack(pack.Id);
		Levels.StartAt(index);
		GetTree().ChangeSceneToFile(Game.ScenePath);
	}
}
