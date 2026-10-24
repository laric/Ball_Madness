using Godot;

namespace BallMadness;

/// <summary>
/// The player's ball. Input is read relative to the active camera so that
/// "up" on the stick always rolls the ball towards the top of the screen.
///
/// With <see cref="Settings.SteeringModes.Tank"/> steering, left and right
/// turn a heading instead (shown by an arrow beside the ball), and up and
/// down push the ball forward and back along it.
///
/// It also makes the ball's noises: a rolling rumble whose pitch and volume
/// follow its speed, and a thud (or the other body's <see cref="IImpactSound"/>)
/// when it hits something hard.
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

	/// <summary>Fraction of the push still available on <see cref="Ice"/>.</summary>
	[Export(PropertyHint.Range, "0,1")] public float IceControl = 0.3f;

	/// <summary>Tank steering: how fast left and right turn the heading, in degrees per second.</summary>
	[Export] public float TankTurnDegrees = 150.0f;

	/// <summary>When false the ball ignores player input (countdown, finish, falling).</summary>
	public bool ControlEnabled { get; set; }

	/// <summary>Optional override for input, used by the autopilot smoke test.</summary>
	public Vector3? SteerOverride { get; set; }

	public bool OnGround { get; private set; }
	public bool OnIce { get; private set; }

	/// <summary>How many times a <see cref="Teleporter"/> has moved the ball.</summary>
	public int Teleports { get; private set; }

	/// <summary>Raised after a <see cref="Teleporter"/> has moved the ball.</summary>
	public event System.Action Teleported;

	/// <summary>The flat direction tank steering pushes along, or null when tank steering is off.</summary>
	public Vector3? Heading => TankSteering && _headingSet ? _heading : null;

	private static bool TankSteering => Settings.SteeringMode == Settings.SteeringModes.Tank;

	private Vector3 _heading = Vector3.Forward;
	private bool _headingSet;
	private MeshInstance3D _arrow;

	/// <summary>A sudden change in velocity bigger than this (m/s) in one step makes an impact sound.</summary>
	private const float ImpactThreshold = 2.5f;

	private AudioStreamPlayer _roll;
	private AudioStreamPlayer _slide;
	private float _rollLevel;
	private Vector3 _lastVelocity;
	private float _impactCooldown;
	private readonly System.Collections.Generic.List<Node> _touched = new();

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 4;
		BodyEntered += body => _touched.Add(body);

		_roll = Sfx.LoopFlat("roll");
		_slide = Sfx.LoopFlat("ice");
		foreach (var loop in new[] { _roll, _slide })
		{
			if (loop == null)
				continue;
			loop.VolumeDb = -80;
			AddChild(loop);
			loop.Play();
		}

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
		OnIce = false;
		foreach (Node3D body in GetCollidingBodies())
		{
			switch (body)
			{
				case Ice:
					OnIce = true;
					break;
				case Conveyor conveyor:
					conveyor.Carry(this, (float)delta);
					break;
				case Bumper bumper:
					bumper.Hit(this);
					break;
			}
		}
		UpdateSounds((float)delta);

		if (!ControlEnabled)
			return;

		Vector3 dir = SteerOverride ?? (TankSteering ? ReadTankDirection((float)delta) : ReadInputDirection());
		if (dir == Vector3.Zero)
			return;

		// Stop accelerating in a direction once we're already fast in that direction.
		float speedAlong = LinearVelocity.Dot(dir);
		if (speedAlong > MaxSpeed)
			return;

		float control = !OnGround ? AirControl : OnIce ? IceControl : 1.0f;
		ApplyCentralForce(dir * PushForce * control);
		if (OnGround && !OnIce)
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
		_lastVelocity = Vector3.Zero;
		_rollLevel = 0;
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

	private void UpdateSounds(float dt)
	{
		// Rolling: louder and higher the faster the ball goes, silent in the
		// air. On ice the rumble gives way to a hiss.
		float speed = LinearVelocity.Length();
		float target = OnGround ? Mathf.Clamp(speed / MaxSpeed, 0, 1) : 0;
		_rollLevel = Mathf.MoveToward(_rollLevel, target, dt * (target > _rollLevel ? 3.0f : 6.0f));
		var (active, quiet) = OnIce && _slide != null ? (_slide, _roll) : (_roll, _slide);
		if (active != null)
		{
			active.VolumeDb = _rollLevel < 0.02f ? -80 : Mathf.LinearToDb(_rollLevel * 0.9f);
			active.PitchScale = 0.55f + 0.95f * Mathf.Clamp(speed / MaxSpeed, 0, 1.2f);
		}
		if (quiet != null)
			quiet.VolumeDb = -80;

		// Impacts: the velocity jumped since last step, more than gravity or rolling could do.
		_impactCooldown -= dt;
		Vector3 change = LinearVelocity - _lastVelocity;
		_lastVelocity = LinearVelocity;
		float hit = change.Length();
		if (hit > ImpactThreshold && OnGround && _impactCooldown <= 0)
		{
			_impactCooldown = 0.12f;
			float volume = Mathf.Clamp((hit - ImpactThreshold) / 10.0f, 0.15f, 1.0f);
			Sfx.Play(ImpactSoundFor(change), Mathf.LinearToDb(volume), (float)GD.RandRange(0.9, 1.1));
		}
		_touched.Clear();
	}

	/// <summary>
	/// What a hit sounds like: whatever was just touched, if it has a sound of
	/// its own and the ball didn't simply land on top of it.
	/// </summary>
	private string ImpactSoundFor(Vector3 change)
	{
		bool landing = change.Y > 0.7f * change.Length();
		foreach (Node body in _touched)
		{
			if (body is IImpactSound sound && !(landing && body is Mover or Spinner))
				return sound.ImpactSound;
		}
		return "impact";
	}

	/// <summary>Moves the ball to <paramref name="position"/>, stopping it unless <paramref name="keepSpeed"/>.</summary>
	public void Teleport(Vector3 position, bool keepSpeed)
	{
		Vector3 velocity = LinearVelocity, spin = AngularVelocity;
		Respawn(position);
		if (keepSpeed)
		{
			LinearVelocity = velocity;
			AngularVelocity = spin;
		}
		Teleports++;
		Sfx.Play("teleport");
		Teleported?.Invoke();
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
