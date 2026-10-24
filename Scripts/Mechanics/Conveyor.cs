using Godot;

namespace BallMadness;

/// <summary>
/// A belt that carries the ball along the body's local +Z at
/// <see cref="Speed"/> metres a second. The ball calls <see cref="Carry"/>
/// while it touches the belt; the pull eases the ball's speed along the belt
/// towards the belt's speed, so a cross belt drifts the ball sideways and a
/// player can still steer against it.
/// </summary>
public partial class Conveyor : StaticBody3D
{
	public const string ShaderPath = "res://Shaders/conveyor.gdshader";

	[Export] public float Speed = 4.0f;

	/// <summary>How quickly the ball is brought up to belt speed (per second).</summary>
	[Export] public float Grip = 3.0f;

	/// <summary>The strongest pull, in m/s², so the ball can always fight a cross belt.</summary>
	[Export] public float MaxPull = 4.0f;

	/// <summary>The belt's look; its stripes are lined up with the belt here.</summary>
	public ShaderMaterial Belt { get; set; }

	public override void _Ready()
	{
		// Hum while the game runs (not in the editor's frozen preview).
		if (GetTree().CurrentScene is Game)
			Sfx.Loop(this, "conveyor", -6);

		if (Belt == null)
			return;
		Vector3 dir = GlobalBasis.Z.Normalized();
		Belt.SetShaderParameter("belt_dir", dir);
		Belt.SetShaderParameter("belt_side", GlobalBasis.X.Normalized());
		Belt.SetShaderParameter("center", GlobalPosition);
		Belt.SetShaderParameter("speed", Speed);
	}

	public void Carry(RigidBody3D ball, float delta)
	{
		Vector3 dir = GlobalBasis.Z.Normalized();
		float along = ball.LinearVelocity.Dot(dir);
		float pull = Mathf.Clamp(Grip * (Speed - along), -MaxPull, MaxPull);
		ball.ApplyCentralForce(dir * pull * ball.Mass);
	}
}
