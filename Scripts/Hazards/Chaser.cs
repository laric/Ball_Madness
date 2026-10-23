using Godot;

namespace BallMadness;

/// <summary>
/// An enemy marble. It rolls after the player's ball whenever the ball comes
/// within <see cref="ChaseRadius"/> of the chaser's starting spot, tries to
/// bump it off the course, and rolls back home otherwise. If it falls off it
/// reappears at home.
/// </summary>
public partial class Chaser : RigidBody3D
{
	[Export] public float ChaseRadius = 10.0f;
	[Export] public float PushForce = 6.0f;
	[Export] public float MaxSpeed = 5.0f;

	/// <summary>How far below home the chaser can drop before it is put back.</summary>
	[Export] public float ResetDepth = 15.0f;

	private Game _game;
	private Node3D _ball;
	private Vector3 _home;

	public override void _Ready()
	{
		_game = GetTree().CurrentScene as Game;
		_ball = _game?.GetNodeOrNull<Node3D>("Ball");
		_home = GlobalPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (GlobalPosition.Y < _home.Y - ResetDepth)
		{
			GlobalTransform = new Transform3D(Basis.Identity, _home);
			LinearVelocity = Vector3.Zero;
			AngularVelocity = Vector3.Zero;
			return;
		}

		if (_ball == null || _game.CurrentState != Game.State.Playing)
			return;

		Vector3 target = _home;
		Vector3 ballPos = _ball.GlobalPosition;
		if (Flat(ballPos - _home).Length() < ChaseRadius && Mathf.Abs(ballPos.Y - _home.Y) < 3.0f)
			target = ballPos;

		Vector3 toTarget = Flat(target - GlobalPosition);
		if (toTarget.Length() < 0.3f)
			return;

		Vector3 dir = toTarget.Normalized();
		if (LinearVelocity.Dot(dir) > MaxSpeed)
			return;

		ApplyCentralForce(dir * PushForce * Mass);
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
}
