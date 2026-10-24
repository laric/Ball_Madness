using Godot;

namespace BallMadness;

/// <summary>
/// Runs one level: loads it from the <see cref="Levels"/> list, then handles
/// the countdown, race timer, falling off and respawning at the last
/// checkpoint, reaching the goal, running out of time, and moving on to the
/// next level.
///
/// The level is built from its file by <see cref="LevelFormat.LevelBuilder"/>:
/// Area3D nodes in the "checkpoint" and "goal" groups, a Marker3D named
/// "Spawn", and a <see cref="LevelInfo"/> root with the time limit.
///
/// Completed levels are written to the save file as soon as the goal is
/// reached. Esc or the gamepad's Start button opens the <see cref="PauseMenu"/>.
/// </summary>
public partial class Game : Node3D
{
	public enum State { Countdown, Playing, Falling, Finished, TimeUp, GameComplete }

	public const string ScenePath = "res://Scenes/Main.tscn";

	/// <summary>Seconds on the clock; set from the level's file when it loads.</summary>
	[Export] public float TimeLimit = 60.0f;
	[Export] public float CountdownTime = 3.0f;
	[Export] public float RespawnDelay = 1.2f;

	/// <summary>Anything below this height counts as having fallen off the course.</summary>
	[Export] public float KillHeight = -12.0f;

	/// <summary>The clock ticks every second once this few are left.</summary>
	[Export] public float WarningTime = 10.0f;

	public State CurrentState => _state;
	public float TimeLeft => _timeLeft;
	public int Falls { get; private set; }

	private Ball _ball;
	private CameraRig _cameraRig;
	private Label _timeLabel;
	private Label _levelLabel;
	private Label _messageLabel;
	private Control _dim;
	private PauseMenu _pauseMenu;
	private string _levelName = "";

	private State _state;
	private float _timeLeft;
	private float _stateTimer;
	private Vector3 _respawnPoint;
	private Node _lastCheckpoint;
	private float _flashTimer;
	private bool _leaving;
	private int _lastCountdownBeep;

	public override void _Ready()
	{
		_ball = GetNode<Ball>("Ball");
		_cameraRig = GetNode<CameraRig>("CameraRig");
		_timeLabel = GetNode<Label>("Hud/TimeLabel");
		_levelLabel = GetNode<Label>("Hud/LevelLabel");
		_messageLabel = GetNode<Label>("Hud/MessageLabel");
		_dim = GetNode<Control>("Hud/Dim");

		// Testing aid: only in debug builds (the editor and debug exports).
		var skipButton = GetNode<Button>("Hud/SkipButton");
		skipButton.Visible = OS.IsDebugBuild() && !Levels.Testing;
		skipButton.Pressed += SkipLevel;

		_pauseMenu = new PauseMenu();
		AddChild(_pauseMenu);

		LoadLevel();

		var spawn = GetNode<Node3D>("Level/Spawn");
		_respawnPoint = spawn.GlobalPosition;
		_ball.Respawn(_respawnPoint, StartHeading(_respawnPoint));
		_cameraRig.SnapToTarget();
		_ball.Teleported += _cameraRig.SnapToTarget;

		foreach (Node node in GetTree().GetNodesInGroup("checkpoint"))
		{
			if (node is Area3D area)
				area.BodyEntered += body => OnCheckpointEntered(area, body);
		}
		foreach (Node node in GetTree().GetNodesInGroup("goal"))
		{
			if (node is Area3D area)
				area.BodyEntered += OnGoalEntered;
		}

		_timeLeft = TimeLimit;
		EnterState(State.Countdown);

		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--autopilot") >= 0)
			AddChild(new AutoPilot());
	}

	private void LoadLevel()
	{
		Levels.ApplyCommandLine();
		LevelInfo level = Levels.BuildCurrent();
		level.Name = "Level";
		AddChild(level);

		TimeLimit = level.TimeLimit;
		KillHeight = level.KillHeight;
		_levelName = level.DisplayName;
		_levelLabel.Text = Levels.Testing
			? $"TEST  {_levelName}"
			: $"LEVEL {Levels.CurrentIndex + 1}/{Levels.Count}  {_levelName}";
		GetNode<Button>("Hud/SkipButton").Text = Levels.IsLastLevel ? "Back to level 1 ▶" : "Next level ▶";
	}

	/// <summary>Testing shortcut: jumps straight to the next level from any state.</summary>
	public void SkipLevel()
	{
		if (_leaving)
			return;

		_leaving = true;
		Levels.SkipCurrent();
		GetTree().ReloadCurrentScene();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("pause"))
		{
			GetViewport().SetInputAsHandled();
			_pauseMenu.Open();
		}
		else if (@event.IsActionPressed("restart"))
		{
			if (_state == State.GameComplete)
				Levels.StartNewRun();
			GetTree().ReloadCurrentScene();
		}
		else if (@event.IsActionPressed("next"))
		{
			Continue();
		}
		else if (@event.IsActionPressed("toggle_camera"))
		{
			ToggleCameraMode();
		}
		else if (@event.IsActionPressed("toggle_mute"))
		{
			Settings.Muted = !Settings.Muted;
			Settings.Save();
			Sfx.ApplySettings();
			if (_state == State.Playing)
				Flash(Settings.Muted ? "Sound off" : "Sound on");
		}
	}

	/// <summary>Switches between the fixed and follow cameras and remembers the choice.</summary>
	private void ToggleCameraMode()
	{
		Settings.CameraMode = Settings.CameraMode == Settings.CameraModes.Fixed ? Settings.CameraModes.Follow : Settings.CameraModes.Fixed;
		Settings.Save();
		_cameraRig.ResetFollow();
		if (_state == State.Playing)
			Flash($"Camera: {Settings.CameraMode}");
	}

	/// <summary>
	/// Leaves an end-of-level screen: next level after finishing, the same
	/// level after running out of time, level 1 after beating the game.
	/// </summary>
	public void Continue()
	{
		if (_leaving)
			return;

		if (Levels.Testing && _state == State.Finished)
		{
			_leaving = true;
			ReturnToEditor();
			return;
		}

		switch (_state)
		{
			case State.Finished:
				Levels.CompleteCurrent(TimeLimit - _timeLeft, Falls);
				break;
			case State.TimeUp:
				break;
			case State.GameComplete:
				Levels.StartNewRun();
				break;
			default:
				return;
		}
		// The reload happens at the end of the frame; don't bank the level twice.
		_leaving = true;
		GetTree().ReloadCurrentScene();
	}

	/// <summary>Ends a test play and goes back to the level editor where it left off.</summary>
	public void ReturnToEditor()
	{
		Levels.EndTest();
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(Editing.LevelEditor.ScenePath);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_stateTimer += dt;

		switch (_state)
		{
			case State.Countdown:
				int remaining = Mathf.CeilToInt(CountdownTime - _stateTimer);
				if (remaining != _lastCountdownBeep && remaining > 0)
				{
					_lastCountdownBeep = remaining;
					Sfx.Play("countdown");
				}
				_messageLabel.Text = Levels.Testing
					? $"TEST\n{_levelName}\n\n{remaining}"
					: $"LEVEL {Levels.CurrentIndex + 1}\n{_levelName}\n\n{remaining}";
				if (_stateTimer >= CountdownTime)
				{
					EnterState(State.Playing);
					Sfx.Play("go");
					Flash("GO!");
				}
				break;

			case State.Playing:
				if (_flashTimer > 0)
				{
					_flashTimer -= dt;
					if (_flashTimer <= 0)
						_messageLabel.Text = "";
				}
				TickClock(dt);
				if (_ball.GlobalPosition.Y < KillHeight)
					EnterState(State.Falling);
				break;

			case State.Falling:
				// The clock keeps running while you fall: that's the penalty.
				TickClock(dt);
				if (_state == State.Falling && _stateTimer >= RespawnDelay)
				{
					_ball.Respawn(_respawnPoint, StartHeading(_respawnPoint));
					Sfx.Play("respawn");
					_cameraRig.ResetFollow();
					_cameraRig.Following = true;
					EnterState(State.Playing);
					_messageLabel.Text = "";
				}
				break;
		}

		_timeLabel.Text = $"TIME {Mathf.Max(_timeLeft, 0):0.0}";
	}

	private void TickClock(float dt)
	{
		float before = _timeLeft;
		_timeLeft -= dt;
		// A tick each time the clock passes a whole second near the end, higher for the last three.
		if (_timeLeft > 0 && Mathf.Ceil(_timeLeft) <= WarningTime && Mathf.Ceil(_timeLeft) < Mathf.Ceil(before))
			Sfx.Play("timer_warning", 0, _timeLeft < 3 ? 1.35f : 1.0f);
		if (_timeLeft <= 0)
		{
			_timeLeft = 0;
			EnterState(State.TimeUp);
		}
	}

	private void EnterState(State next)
	{
		_state = next;
		_stateTimer = 0;
		_ball.ControlEnabled = next == State.Playing;
		_dim.Visible = next is State.Finished or State.TimeUp or State.GameComplete;

		switch (next)
		{
			case State.Falling:
				Falls++;
				Sfx.Play("fall");
				_cameraRig.Following = false;
				_messageLabel.Text = "Oops!";
				break;
			case State.Finished when Levels.Testing:
				_messageLabel.Text =
					"TEST COMPLETE!\n" +
					$"{TimeLimit - _timeLeft:0.0}s  ·  {_timeLeft:0.0}s to spare  ·  {FallsText(Falls)}\n\n" +
					"Enter to go back to the editor  ·  R to try again";
				break;
			case State.Finished:
				_messageLabel.Text =
					$"LEVEL {Levels.CurrentIndex + 1} COMPLETE!\n" +
					$"{TimeLimit - _timeLeft:0.0}s  ·  {_timeLeft:0.0}s to spare  ·  {FallsText(Falls)}\n\n" +
					$"Enter for level {Levels.CurrentIndex + 2}  ·  R to replay  ·  Esc for menu";
				break;
			case State.GameComplete:
				_messageLabel.Text =
					$"YOU BEAT ALL {Levels.Count} LEVELS!\n" +
					$"Total time {Levels.RunTime:0.0}s  ·  {FallsText(Levels.RunFalls)}\n\n" +
					"Enter to play again  ·  Esc for menu";
				break;
			case State.TimeUp:
				Sfx.Play("time_up");
				_messageLabel.Text = "TIME'S UP\n\nEnter or R to try again  ·  Esc for menu";
				break;
		}
	}

	private void OnCheckpointEntered(Area3D checkpoint, Node3D body)
	{
		if (body != _ball || _state != State.Playing || checkpoint == _lastCheckpoint)
			return;

		_lastCheckpoint = checkpoint;
		_respawnPoint = checkpoint.GlobalPosition + Vector3.Up * 1.0f;
		Sfx.Play("checkpoint");
		Flash("Checkpoint!");
	}

	/// <summary>
	/// Which way the course goes on from <paramref name="from"/>, for tank
	/// steering's starting heading: towards the next autopilot waypoint, or
	/// the nearest goal on a level without a route.
	/// </summary>
	private Vector3? StartHeading(Vector3 from)
	{
		static Vector3 Flat(Vector3 v) => v with { Y = 0 };

		var waypoints = new System.Collections.Generic.List<Vector3>();
		foreach (Node node in GetNodeOrNull("Level/Route")?.GetChildren() ?? new Godot.Collections.Array<Node>())
		{
			if (node is Node3D marker)
				waypoints.Add(marker.GlobalPosition);
		}
		int nearest = -1;
		for (int i = 0; i < waypoints.Count; i++)
		{
			if (nearest < 0 || Flat(waypoints[i] - from).Length() < Flat(waypoints[nearest] - from).Length())
				nearest = i;
		}
		for (int i = System.Math.Max(nearest, 0); i < waypoints.Count; i++)
		{
			Vector3 toward = Flat(waypoints[i] - from);
			if (toward.Length() > 1.5f)
				return toward.Normalized();
		}

		Vector3? best = null;
		foreach (Node node in GetTree().GetNodesInGroup("goal"))
		{
			if (node is Node3D goal && Flat(goal.GlobalPosition - from) is var toward && toward.Length() > 0.1f
				&& (best == null || toward.Length() < best.Value.Length()))
				best = toward;
		}
		return best?.Normalized();
	}

	/// <summary>Shows a short message that clears itself while playing.</summary>
	private void Flash(string text, float seconds = 1.0f)
	{
		_messageLabel.Text = text;
		_flashTimer = seconds;
	}

	private void OnGoalEntered(Node3D body)
	{
		if (body != _ball || _state != State.Playing)
			return;

		Sfx.Play("goal");
		if (Levels.Testing)
		{
			EnterState(State.Finished);
			return;
		}

		SaveGame.RecordCompletion(SaveGame.CurrentPack, Levels.CurrentIndex, TimeLimit - _timeLeft, Falls);
		if (Levels.IsLastLevel)
		{
			Levels.CompleteCurrent(TimeLimit - _timeLeft, Falls);
			EnterState(State.GameComplete);
		}
		else
		{
			EnterState(State.Finished);
		}
	}

	private static string FallsText(int falls) => falls == 1 ? "1 fall" : $"{falls} falls";
}
