using Godot;

namespace BallMadness;

/// <summary>
/// Isometric follow camera. The rig sits on the target and holds the rotation;
/// the child Camera3D is pushed back along the rig's local Z axis.
/// </summary>
public partial class CameraRig : Node3D
{
	[Export] public Node3D Target;

	/// <summary>Higher values make the camera catch up faster.</summary>
	[Export] public float FollowSharpness = 6.0f;

	/// <summary>When false the camera stays where it is (e.g. while the ball falls off the course).</summary>
	public bool Following { get; set; } = true;

	public override void _Ready()
	{
		SnapToTarget();
	}

	public override void _Process(double delta)
	{
		if (Target == null || !Following)
			return;

		float t = 1.0f - Mathf.Exp(-FollowSharpness * (float)delta);
		GlobalPosition = GlobalPosition.Lerp(Target.GlobalPosition, t);
	}

	public void SnapToTarget()
	{
		if (Target != null)
			GlobalPosition = Target.GlobalPosition;
	}
}
