using Godot;

namespace BallMadness;

/// <summary>
/// A swing door the ball can only pass along the gate's local +Z. Coming the
/// other way it is a solid wall. The posts at its sides are always solid.
/// It clacks when it swings open and when the ball hits it shut.
/// </summary>
public partial class OneWayGate : StaticBody3D, IImpactSound
{
	public string ImpactSound => "gate";

	[Export] public float Width = 4.0f;
	[Export] public float Height = 1.2f;
	[Export] public float Thickness = 0.15f;

	/// <summary>The door panel's hinge (at the top), swung open while the ball goes through.</summary>
	public Node3D Hinge { get; set; }

	private const float BallRadius = 0.5f;

	private Ball _ball;
	private bool _passing;

	public override void _Ready()
	{
		_ball = (GetTree().CurrentScene as Game)?.GetNodeOrNull<Ball>("Ball");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_ball == null)
			return;

		Vector3 local = ToLocal(_ball.GlobalPosition);
		bool near = Mathf.Abs(local.X) < Width / 2 + BallRadius && local.Y > -BallRadius && local.Y < Height + BallRadius;
		float clear = BallRadius + Thickness / 2 + 0.05f;

		if (!_passing && near && local.Z < 0 && local.Z > -clear - 0.5f)
		{
			// Coming from the open side: let it through.
			_passing = true;
			AddCollisionExceptionWith(_ball);
			Sfx.PlayAt("gate", GlobalPosition + Vector3.Up * Height / 2);
		}
		else if (_passing && (!near || local.Z > clear || local.Z < -clear - 1.0f))
		{
			_passing = false;
			RemoveCollisionExceptionWith(_ball);
		}

		if (Hinge != null)
		{
			float target = _passing ? -Mathf.DegToRad(75) : 0;
			Vector3 rot = Hinge.Rotation;
			rot.X = Mathf.MoveToward(rot.X, target, (float)delta * 8);
			Hinge.Rotation = rot;
		}
	}
}
