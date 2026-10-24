using Godot;

namespace BallMadness;

/// <summary>
/// The player's ball. Input is read relative to the active camera so that
/// "up" on the stick always rolls the ball towards the top of the screen.
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

	/// <summary>When false the ball ignores player input (countdown, finish, falling).</summary>
	public bool ControlEnabled { get; set; }

	/// <summary>Optional override for input, used by the autopilot smoke test.</summary>
	public Vector3? SteerOverride { get; set; }

	public bool OnGround { get; private set; }

	/// <summary>A sudden change in velocity bigger than this (m/s) in one step makes an impact sound.</summary>
	private const float ImpactThreshold = 2.5f;

	private AudioStreamPlayer _roll;
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
		if (_roll != null)
		{
			_roll.VolumeDb = -80;
			AddChild(_roll);
			_roll.Play();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		OnGround = GetContactCount() > 0;
		UpdateSounds((float)delta);

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
		_lastVelocity = Vector3.Zero;
		_rollLevel = 0;
	}

	private void UpdateSounds(float dt)
	{
		// Rolling: louder and higher the faster the ball goes, silent in the air.
		float speed = LinearVelocity.Length();
		float target = OnGround ? Mathf.Clamp(speed / MaxSpeed, 0, 1) : 0;
		_rollLevel = Mathf.MoveToward(_rollLevel, target, dt * (target > _rollLevel ? 3.0f : 6.0f));
		if (_roll != null)
		{
			_roll.VolumeDb = _rollLevel < 0.02f ? -80 : Mathf.LinearToDb(_rollLevel * 0.9f);
			_roll.PitchScale = 0.55f + 0.95f * Mathf.Clamp(speed / MaxSpeed, 0, 1.2f);
		}

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
