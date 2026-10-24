using Godot;

namespace BallMadness;

/// <summary>
/// A post that kicks the ball away. The ball calls <see cref="Hit"/> when it
/// touches the bumper, which sends it off at <see cref="Kick"/> metres a
/// second (or faster, if it came in faster), straight away from the
/// bumper's centre line, and makes the bumper pulse. The ball's impact
/// sound for it is the bumper's boing.
/// </summary>
public partial class Bumper : StaticBody3D, IImpactSound
{
	public string ImpactSound => "bumper";

	[Export] public float Kick = 10.0f;

	/// <summary>Seconds before the same bumper can kick again.</summary>
	[Export] public float Cooldown = 0.15f;

	private float _cooldown;
	private float _pulse;

	public override void _Ready()
	{
		PhysicsMaterialOverride ??= new PhysicsMaterial { Friction = 0.2f, Bounce = 0.0f };
	}

	public override void _PhysicsProcess(double delta)
	{
		_cooldown -= (float)delta;
		if (_pulse > 0)
		{
			_pulse = Mathf.Max(_pulse - (float)delta * 5, 0);
			float scale = 1 + 0.18f * _pulse;
			foreach (Node child in GetChildren())
			{
				if (child is MeshInstance3D mesh)
					mesh.Scale = new Vector3(scale, 1, scale);
			}
		}
	}

	public void Hit(RigidBody3D ball)
	{
		if (_cooldown > 0)
			return;

		Vector3 away = ball.GlobalPosition - GlobalPosition;
		// Away from the bumper's own axis, so a tall post kicks sideways.
		Vector3 up = GlobalBasis.Y.Normalized();
		away -= up * away.Dot(up);
		if (away.LengthSquared() < 1e-4f)
			return;
		away = away.Normalized();

		float outward = ball.LinearVelocity.Dot(away);
		if (outward < Kick)
			ball.LinearVelocity += away * (Kick - outward);
		_cooldown = Cooldown;
		_pulse = 1;
	}
}
