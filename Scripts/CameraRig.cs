using Godot;

namespace BallMadness;

/// <summary>
/// Isometric follow camera. The rig sits on the target and holds the rotation;
/// the child Camera3D is pushed back along the rig's local Z axis.
/// Hold the right mouse button and move the mouse to orbit around the ball;
/// scroll the mouse wheel to zoom.
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

	/// <summary>When false the camera stays where it is (e.g. while the ball falls off the course).</summary>
	public bool Following { get; set; } = true;

	private bool _orbiting;
	private float _yaw;
	private float _pitch;
	private Camera3D _camera;
	private float _targetZoom;

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
	}

	public void SnapToTarget()
	{
		if (Target != null)
			GlobalPosition = Target.GlobalPosition;
	}
}
