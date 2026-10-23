using Godot;

namespace BallMadness;

/// <summary>
/// Runs one level: countdown, race timer, falling off and respawning at the
/// last checkpoint, reaching the goal, and running out of time.
///
/// The level only needs Area3D nodes in the "checkpoint" and "goal" groups and
/// a Marker3D named "Spawn"; everything else is plain geometry.
/// </summary>
public partial class Game : Node3D
{
	public enum State { Countdown, Playing, Falling, Finished, TimeUp }

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
	private Label _messageLabel;

	private State _state;
	private float _timeLeft;
	private float _stateTimer;
	private Vector3 _respawnPoint;
	private Node _lastCheckpoint;
	private float _flashTimer;

	public override void _Ready()
	{
		_ball = GetNode<Ball>("Ball");
		_cameraRig = GetNode<CameraRig>("CameraRig");
		_timeLabel = GetNode<Label>("Hud/TimeLabel");
		_messageLabel = GetNode<Label>("Hud/MessageLabel");

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

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("restart"))
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
				_messageLabel.Text = remaining > 0 ? remaining.ToString() : "";
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

		switch (next)
		{
			case State.Falling:
				Falls++;
				_cameraRig.Following = false;
				_messageLabel.Text = "Oops!";
				break;
			case State.Finished:
				_messageLabel.Text = $"FINISHED!\n{_timeLeft:0.0} seconds to spare\n\nPress R to race again";
				break;
			case State.TimeUp:
				_messageLabel.Text = "TIME'S UP\n\nPress R to try again";
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
		if (body == _ball && _state == State.Playing)
			EnterState(State.Finished);
	}
}
