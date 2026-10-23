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
/// </summary>
public partial class Game : Node3D
{
	public enum State { Countdown, Playing, Falling, Finished, TimeUp, GameComplete }

	/// <summary>Seconds on the clock; set from the level's file when it loads.</summary>
	[Export] public float TimeLimit = 60.0f;
	[Export] public float CountdownTime = 3.0f;
	[Export] public float RespawnDelay = 1.2f;

	/// <summary>Anything below this height counts as having fallen off the course.</summary>
	[Export] public float KillHeight = -12.0f;

	public State CurrentState => _state;
	public float TimeLeft => _timeLeft;
	public int Falls { get; private set; }

	private Ball _ball;
	private CameraRig _cameraRig;
	private Label _timeLabel;
	private Label _levelLabel;
	private Label _messageLabel;
	private Control _dim;
	private string _levelName = "";

	private State _state;
	private float _timeLeft;
	private float _stateTimer;
	private Vector3 _respawnPoint;
	private Node _lastCheckpoint;
	private float _flashTimer;
	private bool _leaving;

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
		skipButton.Visible = OS.IsDebugBuild();
		skipButton.Pressed += SkipLevel;

		LoadLevel();

		var spawn = GetNode<Node3D>("Level/Spawn");
		_respawnPoint = spawn.GlobalPosition;
		_ball.Respawn(_respawnPoint);
		_cameraRig.SnapToTarget();

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
		_levelLabel.Text = $"LEVEL {Levels.CurrentIndex + 1}/{Levels.Count}  {_levelName}";
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
		if (@event.IsActionPressed("restart"))
		{
			if (_state == State.GameComplete)
				Levels.StartNewRun();
			GetTree().ReloadCurrentScene();
		}
		else if (@event.IsActionPressed("next"))
		{
			Continue();
		}
	}

	/// <summary>
	/// Leaves an end-of-level screen: next level after finishing, the same
	/// level after running out of time, level 1 after beating the game.
	/// </summary>
	public void Continue()
	{
		if (_leaving)
			return;

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

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_stateTimer += dt;

		switch (_state)
		{
			case State.Countdown:
				int remaining = Mathf.CeilToInt(CountdownTime - _stateTimer);
				_messageLabel.Text = $"LEVEL {Levels.CurrentIndex + 1}\n{_levelName}\n\n{remaining}";
				if (_stateTimer >= CountdownTime)
				{
					EnterState(State.Playing);
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
					_ball.Respawn(_respawnPoint);
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
		_timeLeft -= dt;
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
				_cameraRig.Following = false;
				_messageLabel.Text = "Oops!";
				break;
			case State.Finished:
				_messageLabel.Text =
					$"LEVEL {Levels.CurrentIndex + 1} COMPLETE!\n" +
					$"{TimeLimit - _timeLeft:0.0}s  ·  {_timeLeft:0.0}s to spare  ·  {FallsText(Falls)}\n\n" +
					$"Enter for level {Levels.CurrentIndex + 2}  ·  R to replay";
				break;
			case State.GameComplete:
				_messageLabel.Text =
					$"YOU BEAT ALL {Levels.Count} LEVELS!\n" +
					$"Total time {Levels.RunTime:0.0}s  ·  {FallsText(Levels.RunFalls)}\n\n" +
					"Enter to play again";
				break;
			case State.TimeUp:
				_messageLabel.Text = "TIME'S UP\n\nEnter or R to try again";
				break;
		}
	}

	private void OnCheckpointEntered(Area3D checkpoint, Node3D body)
	{
		if (body != _ball || _state != State.Playing || checkpoint == _lastCheckpoint)
			return;

		_lastCheckpoint = checkpoint;
		_respawnPoint = checkpoint.GlobalPosition + Vector3.Up * 1.0f;
		Flash("Checkpoint!");
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
