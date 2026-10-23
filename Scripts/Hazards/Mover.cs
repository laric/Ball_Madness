using Godot;

namespace BallMadness;

/// <summary>
/// Slides back and forth between where it was placed and <see cref="Offset"/>
/// away from there, easing in and out at both ends. Use it for moving
/// platforms (the ball rides along) and for pushers that sweep across a path.
/// </summary>
public partial class Mover : AnimatableBody3D
{
	/// <summary>How far the body travels from its starting position, in its parent's space.</summary>
	[Export] public Vector3 Offset = new(0, 0, 4);

	/// <summary>Seconds for a full trip out and back.</summary>
	[Export] public float Period = 4.0f;

	/// <summary>Where in the cycle to start, from 0 to 1 (0.5 starts at the far end).</summary>
	[Export(PropertyHint.Range, "0,1")] public float PhaseOffset = 0.0f;

	private Vector3 _origin;
	private float _time;

	public override void _Ready()
	{
		_origin = Position;
		Position = PositionAt(0);
	}

	public override void _PhysicsProcess(double delta)
	{
		_time += (float)delta;
		Position = PositionAt(_time);
	}

	private Vector3 PositionAt(float time)
	{
		float cycle = time / Period + PhaseOffset;
		// 0 at the start, 1 at the far end, with smooth turnarounds.
		float t = 0.5f - 0.5f * Mathf.Cos(cycle * Mathf.Tau);
		return _origin + Offset * t;
	}
}
