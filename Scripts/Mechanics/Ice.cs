using Godot;

namespace BallMadness;

/// <summary>
/// A frictionless surface. While the ball touches ice it slides instead of
/// rolling and the player only has <see cref="Ball.IceControl"/> of the
/// usual push, so turning and stopping take a long time.
/// </summary>
public partial class Ice : StaticBody3D
{
	public Ice()
	{
		PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.0f, Bounce = 0.0f };
	}
}
