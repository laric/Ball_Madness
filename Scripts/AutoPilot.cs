using Godot;

namespace BallMadness;

/// <summary>
/// Smoke test: drives the ball along the level's "Route" markers and quits
/// with exit code 0 if it reaches the goal. Enabled by running the game with
/// <c>-- --autopilot</c> (works headless, see README). With <c>--all</c> it
/// carries on to the next level instead and quits after the last one.
///
/// A waypoint can make the pilot wait for a timed hazard: give it the metadata
/// <c>wait_area</c> (a NodePath to an Area3D) and <c>wait_for</c> ("clear" or
/// "occupied"). The pilot then holds at the previous waypoint and heads on the
/// moment the area turns clear of moving bodies (or a moving body arrives in
/// it), so it always starts at the beginning of the hazard's safe window.
/// A waypoint with a <c>speed</c> metadata value caps the pilot's speed on the
/// way to it, for thin beams and tight corners.
/// </summary>
public partial class AutoPilot : Node
{
	[Export] public float CruiseSpeed = 6.0f;
	[Export] public float WaypointRadius = 1.5f;

	private Game _game;
	private Ball _ball;
	private Vector3[] _route;
	private Area3D[] _waitAreas;
	private bool[] _waitOccupied;
	private float[] _speeds;
	private int _gatePassed = -1;
	private bool _gateArmed;
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
		_waitAreas = new Area3D[_route.Length];
		_waitOccupied = new bool[_route.Length];
		_speeds = new float[_route.Length];
		for (int i = 0; i < _route.Length; i++)
		{
			var marker = routeNode.GetChild<Node3D>(i);
			_route[i] = marker.GlobalPosition;
			_speeds[i] = (float)marker.GetMeta("speed", CruiseSpeed);
			if (i > 0 && marker.HasMeta("wait_area"))
			{
				_waitAreas[i] = marker.GetNode<Area3D>((NodePath)marker.GetMeta("wait_area"));
				_waitOccupied[i] = (string)marker.GetMeta("wait_for", "clear") == "occupied";
			}
		}

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
			_gatePassed = -1;
			_gateArmed = false;
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

		Vector3 target = _route[_next];
		float speed = _speeds[_next];
		if (_waitAreas[_next] != null && _gatePassed != _next)
		{
			// Hold at the previous waypoint until the hazard ahead is in the right state.
			Vector3 hold = _route[_next - 1];
			bool open = IsGateOpen(_next);
			if (flatPos.DistanceTo(Flat(hold)) < WaypointRadius)
			{
				if (open && _gateArmed)
				{
					_gatePassed = _next;
					_gateArmed = false;
				}
				else if (!open)
				{
					_gateArmed = true;
				}
			}
			if (_gatePassed != _next)
			{
				target = hold;
				speed = Mathf.Min(speed, flatPos.DistanceTo(Flat(hold)) * 2.0f);
			}
		}

		Vector3 toTarget = Flat(target) - flatPos;
		Vector3 desiredVelocity = toTarget.Normalized() * speed;
		Vector3 flatVelocity = new Vector3(_ball.LinearVelocity.X, 0, _ball.LinearVelocity.Z);
		Vector3 steer = (desiredVelocity - flatVelocity) / CruiseSpeed;
		_ball.SteerOverride = steer.LengthSquared() > 1 ? steer.Normalized() : steer;
	}

	private bool IsGateOpen(int waypoint)
	{
		bool occupied = false;
		foreach (Node3D body in _waitAreas[waypoint].GetOverlappingBodies())
		{
			if (body != _ball && body is AnimatableBody3D or RigidBody3D)
				occupied = true;
		}
		return occupied == _waitOccupied[waypoint];
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
}
