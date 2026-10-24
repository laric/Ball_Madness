using Godot;

namespace BallMadness;

/// <summary>
/// The lid of a timed hole. It covers the hole, flush with the floor, and
/// every <see cref="Period"/> seconds slides open for <see cref="OpenFraction"/>
/// of the cycle. It flashes for a moment before it opens.
/// </summary>
public partial class TrapDoor : AnimatableBody3D
{
	[Export] public float Period = 4.0f;

	/// <summary>Where in the cycle to start, from 0 to 1. The hole opens at 0.</summary>
	[Export(PropertyHint.Range, "0,1")] public float PhaseOffset = 0.0f;

	/// <summary>How much of each cycle the hole is open, from 0 to 1.</summary>
	[Export(PropertyHint.Range, "0,1")] public float OpenFraction = 0.5f;

	/// <summary>Seconds of warning flashes before the hole opens.</summary>
	[Export] public float Warning = 0.6f;

	public CollisionShape3D Shape { get; set; }
	public MeshInstance3D Lid { get; set; }
	public StandardMaterial3D LidLook { get; set; }

	private float _time;
	private bool _open;
	private Color _color;

	public override void _Ready()
	{
		if (LidLook != null)
			_color = LidLook.AlbedoColor;
		Apply(0);
	}

	public override void _PhysicsProcess(double delta)
	{
		_time += (float)delta;
		Apply(_time);
	}

	public bool IsOpen => _open;

	private void Apply(float time)
	{
		float cycle = Mathf.PosMod(time / Period + PhaseOffset, 1);
		bool open = cycle < OpenFraction;
		if (open != _open || time == 0)
		{
			_open = open;
			Shape?.SetDeferred(CollisionShape3D.PropertyName.Disabled, open);
		}

		// The lid shrinks away while open and grows back to close.
		float openTime = OpenFraction * Period;
		float t = cycle * Period;
		float shrink = open ? Mathf.Clamp(Mathf.Min(t, openTime - t) / 0.2f, 0, 1) : 0;
		if (Lid != null)
		{
			float s = 1 - shrink;
			Lid.Scale = new Vector3(s, 1, s);
			Lid.Visible = s > 0.01f;
		}

		if (LidLook != null)
		{
			float untilOpen = (1 - cycle) * Period;
			bool flash = !open && untilOpen < Warning && Mathf.PosMod(untilOpen, 0.2f) < 0.1f;
			LidLook.AlbedoColor = flash ? new Color(1f, 0.25f, 0.15f) : _color;
		}
	}
}
