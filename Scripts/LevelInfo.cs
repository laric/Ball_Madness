using Godot;

namespace BallMadness;

/// <summary>
/// The root node of a built level (see <see cref="LevelFormat.LevelBuilder"/>),
/// holding the per-level settings the game reads when it loads the level.
/// </summary>
public partial class LevelInfo : Node3D
{
	/// <summary>Shown on the HUD and in the countdown.</summary>
	[Export] public string DisplayName = "";

	/// <summary>Seconds on the clock when the level starts.</summary>
	[Export] public float TimeLimit = 60.0f;

	/// <summary>Anything below this height counts as having fallen off the course.</summary>
	[Export] public float KillHeight = -12.0f;
}
