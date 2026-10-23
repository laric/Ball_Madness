using Godot;

namespace BallMadness;

/// <summary>
/// Smoke test: drives the ball along the level's "Route" markers and quits
/// with exit code 0 if it reaches the goal. Enabled by running the game with
/// <c>-- --autopilot</c> (works headless, see README). With <c>--all</c> it
/// carries on to the next level instead and quits after the last one.
/// </summary>
public partial class AutoPilot : Node
{
	[Export] public float CruiseSpeed = 6.0f;
	[Export] public float WaypointRadius = 1.5f;

	private Game _game;
	private Ball _ball;
	private Vector3[] _route;
	private int _next;
	private int _lastFalls;
	private bool _relocate;
	private bool _allLevels;

	public override void _Ready()
	{
		_game = GetParent<Game>();
		_ball = _game.GetNode<Ball>("Ball");

		var routeNode = _game.GetNode<Node>("Level/Route");
		_route = new Vector3[routeNode.GetChildCount()];
		for (int i = 0; i < _route.Length; i++)
			_route[i] = routeNode.GetChild<Node3D>(i).GlobalPosition;

		_allLevels = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--all") >= 0;
		GD.Print($"[autopilot] level {Levels.CurrentNumber}: following {_route.Length} waypoints");
	}

	public override void _PhysicsProcess(double delta)
	{
		switch (_game.CurrentState)
		{
			case Game.State.Finished:
				GD.Print($"[autopilot] FINISHED level {Levels.CurrentNumber} with {_game.TimeLeft:0.0}s left, {_game.Falls} fall(s)");
				if (_allLevels)
					_game.Continue();
				else
					GetTree().Quit(0);
				return;
			case Game.State.GameComplete:
				GD.Print($"[autopilot] FINISHED level {Levels.CurrentNumber} with {_game.TimeLeft:0.0}s left, {_game.Falls} fall(s)");
				GD.Print($"[autopilot] GAME COMPLETE: {Levels.Count} level(s) in {Levels.RunTime:0.0}s, {Levels.RunFalls} fall(s)");
				GetTree().Quit(0);
				return;
			case Game.State.TimeUp:
				GD.Print($"[autopilot] TIME UP on level {Levels.CurrentNumber} at waypoint {_next}/{_route.Length}");
				GetTree().Quit(1);
				return;
		}

		if (_game.Falls != _lastFalls)
		{
			_lastFalls = _game.Falls;
			GD.Print($"[autopilot] fell off near {_ball.GlobalPosition.Round()} heading for waypoint {_next}");
			_relocate = true;
		}
		if (_game.CurrentState != Game.State.Playing)
			return;

		Vector3 pos = _ball.GlobalPosition;
		Vector3 flatPos = new Vector3(pos.X, 0, pos.Z);

		if (_relocate)
		{
			// Back from a respawn: resume from the waypoint nearest to the checkpoint.
			_relocate = false;
			_next = 0;
			for (int i = 1; i < _route.Length; i++)
			{
				if (flatPos.DistanceTo(Flat(_route[i])) < flatPos.DistanceTo(Flat(_route[_next])))
					_next = i;
			}
		}

		// Skip forward to the furthest waypoint we're already close to.
		while (_next < _route.Length - 1 && flatPos.DistanceTo(Flat(_route[_next])) < WaypointRadius)
			_next++;

		Vector3 toTarget = Flat(_route[_next]) - flatPos;
		Vector3 desiredVelocity = toTarget.Normalized() * CruiseSpeed;
		Vector3 flatVelocity = new Vector3(_ball.LinearVelocity.X, 0, _ball.LinearVelocity.Z);
		Vector3 steer = (desiredVelocity - flatVelocity) / CruiseSpeed;
		_ball.SteerOverride = steer.LengthSquared() > 1 ? steer.Normalized() : steer;
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
}
