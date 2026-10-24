using Godot;

namespace BallMadness;

/// <summary>
/// The player's ball. Input is read relative to the active camera so that
/// "up" on the stick always rolls the ball towards the top of the screen.
///
/// With <see cref="Settings.SteeringModes.Tank"/> steering, left and right
/// turn a heading instead (shown by an arrow beside the ball), and up and
/// down push the ball forward and back along it.
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

	/// <summary>Tank steering: how fast left and right turn the heading, in degrees per second.</summary>
	[Export] public float TankTurnDegrees = 150.0f;

	/// <summary>When false the ball ignores player input (countdown, finish, falling).</summary>
	public bool ControlEnabled { get; set; }

	/// <summary>Optional override for input, used by the autopilot smoke test.</summary>
	public Vector3? SteerOverride { get; set; }

	public bool OnGround { get; private set; }

	/// <summary>The flat direction tank steering pushes along, or null when tank steering is off.</summary>
	public Vector3? Heading => TankSteering && _headingSet ? _heading : null;

	private static bool TankSteering => Settings.SteeringMode == Settings.SteeringModes.Tank;

	private Vector3 _heading = Vector3.Forward;
	private bool _headingSet;
	private MeshInstance3D _arrow;

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 4;
		_arrow = MakeArrow();
		AddChild(_arrow);
	}

	public override void _Process(double delta)
	{
		if (SteerOverride == null && TankSteering)
			EnsureHeading();
		_arrow.Visible = SteerOverride == null && Heading != null;
		if (!_arrow.Visible)
			return;

		// The arrow lies flat on the floor just ahead of the ball, pointing along the heading.
		float yaw = Mathf.Atan2(-_heading.X, -_heading.Z);
		var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, -Mathf.Pi / 2);
		_arrow.GlobalTransform = new Transform3D(basis, GlobalPosition + _heading * 1.0f + Vector3.Down * 0.45f);
	}

	public override void _PhysicsProcess(double delta)
	{
		OnGround = GetContactCount() > 0;

		if (!ControlEnabled)
			return;

		Vector3 dir = SteerOverride ?? (TankSteering ? ReadTankDirection((float)delta) : ReadInputDirection());
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

	/// <summary>
	/// Moves the ball to <paramref name="position"/> and stops it dead. Tank
	/// steering then faces <paramref name="heading"/>, or the way the camera
	/// looks if that's null.
	/// </summary>
	public void Respawn(Vector3 position, Vector3? heading = null)
	{
		GlobalTransform = new Transform3D(Basis.Identity, position);
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		_headingSet = heading is Vector3 h && h.LengthSquared() > 0.0001f;
		if (_headingSet)
			_heading = heading.Value with { Y = 0 };
		_heading = _heading.Normalized();
	}

	private Vector3 ReadTankDirection(float delta)
	{
		EnsureHeading();
		float turn = Input.GetAxis("move_left", "move_right");
		if (turn != 0)
			_heading = _heading.Rotated(Vector3.Up, -turn * Mathf.DegToRad(TankTurnDegrees) * delta).Normalized();
		float throttle = Input.GetAxis("move_down", "move_up");
		return _heading * throttle;
	}

	private void EnsureHeading()
	{
		if (_headingSet)
			return;
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
			return;
		Vector3 forward = new Vector3(-camera.GlobalBasis.Z.X, 0, -camera.GlobalBasis.Z.Z);
		if (forward.LengthSquared() < 0.0001f)
			return;
		_heading = forward.Normalized();
		_headingSet = true;
	}

	private static MeshInstance3D MakeArrow()
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(1.0f, 0.85f, 0.2f, 0.85f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		return new MeshInstance3D
		{
			Name = "HeadingArrow",
			TopLevel = true,
			Visible = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Mesh = new PrismMesh { Size = new Vector3(0.6f, 0.7f, 0.04f), Material = material },
		};
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
