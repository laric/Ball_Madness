using Godot;

namespace BallMadness;

/// <summary>
/// The player's ball. Input is read relative to the active camera so that
/// "up" on the stick always rolls the ball towards the top of the screen.
/// </summary>
public partial class Ball : RigidBody3D
{
	/// <summary>Torque used to spin the ball; friction turns this into rolling.</summary>
	[Export] public float RollTorque = 4.0f;

	/// <summary>Direct push applied on top of the torque, so control feels responsive.</summary>
	[Export] public float PushForce = 7.0f;

	/// <summary>Fraction of the push still available while airborne.</summary>
	[Export(PropertyHint.Range, "0,1")] public float AirControl = 0.35f;

	/// <summary>Speed above which the player can no longer accelerate.</summary>
	[Export] public float MaxSpeed = 14.0f;

	/// <summary>When false the ball ignores player input (countdown, finish, falling).</summary>
	public bool ControlEnabled { get; set; }

	/// <summary>Optional override for input, used by the autopilot smoke test.</summary>
	public Vector3? SteerOverride { get; set; }

	public bool OnGround { get; private set; }

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 4;
	}

	public override void _PhysicsProcess(double delta)
	{
		OnGround = GetContactCount() > 0;

		if (!ControlEnabled)
			return;

		Vector3 dir = SteerOverride ?? ReadInputDirection();
		if (dir == Vector3.Zero)
			return;

		// Stop accelerating in a direction once we're already fast in that direction.
		float speedAlong = LinearVelocity.Dot(dir);
		if (speedAlong > MaxSpeed)
			return;

		float control = OnGround ? 1.0f : AirControl;
		ApplyCentralForce(dir * PushForce * control);
		if (OnGround)
			ApplyTorque(Vector3.Up.Cross(dir) * RollTorque);
	}

	/// <summary>Moves the ball to <paramref name="position"/> and stops it dead.</summary>
	public void Respawn(Vector3 position)
	{
		GlobalTransform = new Transform3D(Basis.Identity, position);
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}

	private Vector3 ReadInputDirection()
	{
		Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
		if (input == Vector2.Zero)
			return Vector3.Zero;

		Camera3D camera = GetViewport().GetCamera3D();
		Basis basis = camera?.GlobalBasis ?? Basis.Identity;

		// Flatten the camera axes onto the ground plane.
		Vector3 right = new Vector3(basis.X.X, 0, basis.X.Z).Normalized();
		Vector3 forward = new Vector3(-basis.Z.X, 0, -basis.Z.Z).Normalized();

		Vector3 dir = right * input.X - forward * input.Y;
		return dir.LengthSquared() > 1.0f ? dir.Normalized() : dir;
	}
}
