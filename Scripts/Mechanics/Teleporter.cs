using Godot;

namespace BallMadness;

/// <summary>
/// A pad that sends the ball to <see cref="Target"/> (a point on the floor;
/// the ball appears 1 m above it). The ball stops dead there unless
/// <see cref="KeepSpeed"/> is set.
/// </summary>
public partial class Teleporter : Area3D
{
	[Export] public Vector3 Target;
	[Export] public bool KeepSpeed;

	/// <summary>The spinning ring above the pad.</summary>
	public Node3D Ring { get; set; }

	public override void _Ready()
	{
		Monitorable = false;
		BodyEntered += OnBodyEntered;
	}

	public override void _Process(double delta)
	{
		if (Ring != null)
			Ring.RotateY((float)delta * 2.0f);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is Ball ball)
			// Moving a rigid body from inside a physics callback isn't reliable.
			Callable.From(() => ball.Teleport(Target + Vector3.Up, KeepSpeed)).CallDeferred();
	}
}
