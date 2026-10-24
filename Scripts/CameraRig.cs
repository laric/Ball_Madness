using Godot;

namespace BallMadness;

/// <summary>
/// Isometric follow camera. The rig sits on the target and holds the rotation;
/// the child Camera3D is pushed back along the rig's local Z axis.
/// Hold the right mouse button and move the mouse to orbit around the ball;
/// scroll the mouse wheel to zoom.
///
/// In <see cref="Settings.CameraModes.Follow"/> (see <see cref="Settings"/>) the yaw also
/// eases round to look the way the ball is rolling, gently enough that the
/// camera-relative controls don't feel like they're shifting under you.
/// With tank steering it stays behind the ball's heading instead.
/// </summary>
public partial class CameraRig : Node3D
{
	[Export] public Node3D Target;

	/// <summary>Higher values make the camera catch up faster.</summary>
	[Export] public float FollowSharpness = 6.0f;

	/// <summary>Radians of rotation per pixel of mouse movement.</summary>
	[Export] public float MouseSensitivity = 0.005f;

	/// <summary>Pitch limits in degrees (negative looks down). Keeps the camera above the course.</summary>
	[Export] public float MinPitchDegrees = -80.0f;
	[Export] public float MaxPitchDegrees = -15.0f;

	/// <summary>Zoom limits, as the orthographic camera's visible height in metres.</summary>
	[Export] public float MinZoom = 8.0f;
	[Export] public float MaxZoom = 45.0f;

	/// <summary>How much one wheel notch zooms (1.1 = 10% per notch).</summary>
	[Export] public float ZoomStep = 1.1f;

	/// <summary>Follow mode: below this horizontal speed (m/s) the camera doesn't turn.</summary>
	[Export] public float FollowMinSpeed = 2.5f;

	/// <summary>Follow mode: how quickly the tracked travel direction reacts to the ball's velocity.</summary>
	[Export] public float FollowVelocitySmoothing = 2.0f;

	/// <summary>Follow mode: how quickly the yaw closes the gap to the travel direction.</summary>
	[Export] public float FollowYawSharpness = 1.2f;

	/// <summary>Follow mode: the fastest the camera will ever turn by itself, in degrees per second.</summary>
	[Export] public float FollowMaxTurnDegrees = 75.0f;

	/// <summary>Follow mode: seconds after a right-drag before the camera starts following again.</summary>
	[Export] public float FollowOrbitHoldSeconds = 2.0f;

	/// <summary>Follow mode with tank steering: how quickly the yaw swings behind the ball's heading.</summary>
	[Export] public float HeadingYawSharpness = 5.0f;

	/// <summary>Follow mode with tank steering: the fastest the camera turns to keep up, in degrees per second.</summary>
	[Export] public float HeadingMaxTurnDegrees = 270.0f;

	/// <summary>When false the camera stays where it is (e.g. while the ball falls off the course).</summary>
	public bool Following { get; set; } = true;

	private bool _orbiting;
	private float _yaw;
	private float _pitch;
	private Camera3D _camera;
	private float _targetZoom;
	private Vector3 _travel;
	private float _orbitHold;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		_targetZoom = _camera.Size;

		// Start from the rotation set in the scene.
		_pitch = Rotation.X;
		_yaw = Rotation.Y;
		SnapToTarget();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
		{
			// Some touchpads report fractional scroll amounts in Factor; mouse wheels report 1.
			float notches = wheel.Factor > 0 ? wheel.Factor : 1.0f;
			float scale = Mathf.Pow(ZoomStep, wheel.ButtonIndex == MouseButton.WheelUp ? -notches : notches);
			_targetZoom = Mathf.Clamp(_targetZoom * scale, MinZoom, MaxZoom);
		}
		else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } button)
		{
			_orbiting = button.Pressed;
			_orbitHold = FollowOrbitHoldSeconds;
			// Hide and lock the cursor while orbiting so the mouse can't leave the window.
			Input.MouseMode = _orbiting ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
		}
		else if (_orbiting && @event is InputEventMouseMotion motion)
		{
			_yaw = Mathf.Wrap(_yaw - motion.Relative.X * MouseSensitivity, -Mathf.Pi, Mathf.Pi);
			_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity,
				Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
			Rotation = new Vector3(_pitch, _yaw, 0);
		}
	}

	public override void _Notification(int what)
	{
		// Don't leave the cursor captured if the window loses focus mid-drag.
		if (what == NotificationApplicationFocusOut && _orbiting)
		{
			_orbiting = false;
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}
	}

	public override void _Process(double delta)
	{
		float t = 1.0f - Mathf.Exp(-FollowSharpness * (float)delta);
		_camera.Size = Mathf.Lerp(_camera.Size, _targetZoom, t);

		if (Target == null || !Following)
			return;

		GlobalPosition = GlobalPosition.Lerp(Target.GlobalPosition, t);

		if (Settings.CameraMode == Settings.CameraModes.Follow)
			FollowTravelDirection((float)delta);
	}

	/// <summary>Forgets the ball's previous travel direction, e.g. after it respawns.</summary>
	public void ResetFollow()
	{
		_travel = Vector3.Zero;
		_orbitHold = 0;
	}

	private void FollowTravelDirection(float delta)
	{
		if (Target is not RigidBody3D body)
			return;

		// Right-drag wins: hold off while orbiting and for a moment after.
		if (_orbiting)
		{
			_orbitHold = FollowOrbitHoldSeconds;
			return;
		}
		if (_orbitHold > 0)
		{
			_orbitHold -= delta;
			_travel = Vector3.Zero;
			return;
		}

		if (body is Ball { Heading: Vector3 heading })
		{
			TurnTowards(Mathf.Atan2(-heading.X, -heading.Z), HeadingYawSharpness, HeadingMaxTurnDegrees, delta);
			return;
		}

		Vector3 velocity = body.LinearVelocity with { Y = 0 };
		_travel = _travel.Lerp(velocity, 1.0f - Mathf.Exp(-FollowVelocitySmoothing * delta));

		float speed = _travel.Length();
		if (speed < FollowMinSpeed)
			return;

		// The camera looks along the rig's -Z, which for yaw y is (-sin y, 0, -cos y).
		// Ease in just above the minimum speed instead of snapping into motion.
		float strength = Mathf.Clamp((speed - FollowMinSpeed) / FollowMinSpeed, 0.0f, 1.0f);
		TurnTowards(Mathf.Atan2(-_travel.X, -_travel.Z), FollowYawSharpness * strength, FollowMaxTurnDegrees, delta);
	}

	private void TurnTowards(float wantedYaw, float sharpness, float maxTurnDegrees, float delta)
	{
		float gap = Mathf.Wrap(wantedYaw - _yaw, -Mathf.Pi, Mathf.Pi);
		float step = gap * (1.0f - Mathf.Exp(-sharpness * delta));
		float maxStep = Mathf.DegToRad(maxTurnDegrees) * delta;
		step = Mathf.Clamp(step, -maxStep, maxStep);

		_yaw = Mathf.Wrap(_yaw + step, -Mathf.Pi, Mathf.Pi);
		Rotation = new Vector3(_pitch, _yaw, 0);
	}

	public float Yaw => _yaw;
	public float Pitch => _pitch;
	public float Zoom => _targetZoom;

	/// <summary>Sets the view straight away, e.g. to put the editor's camera back where it was.</summary>
	public void SetView(float yaw, float pitch, float zoom)
	{
		_yaw = yaw;
		_pitch = Mathf.Clamp(pitch, Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
		_targetZoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
		if (_camera != null)
			_camera.Size = _targetZoom;
		Rotation = new Vector3(_pitch, _yaw, 0);
	}

	public void SnapToTarget()
	{
		if (Target != null)
			GlobalPosition = Target.GlobalPosition;
	}
}
