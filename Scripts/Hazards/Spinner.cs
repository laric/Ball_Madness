using Godot;

namespace BallMadness;

/// <summary>
/// Turns steadily around <see cref="Axis"/>. Use it for sweeper bars and
/// turntables; give it a child mesh and collision shape offset from the centre.
/// </summary>
public partial class Spinner : AnimatableBody3D, IImpactSound
{
	public string ImpactSound => "hazard_hit";

	[Export] public float DegreesPerSecond = 90.0f;

	/// <summary>Axis to turn around, in the body's own space.</summary>
	[Export] public Vector3 Axis = Vector3.Up;

	public override void _PhysicsProcess(double delta)
	{
		RotateObjectLocal(Axis.Normalized(), Mathf.DegToRad(DegreesPerSecond) * (float)delta);
	}
}
