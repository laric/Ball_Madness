namespace BallMadness;

/// <summary>
/// A body that makes its own noise when the ball hits it, such as a bumper.
/// The ball plays <see cref="ImpactSound"/> (a name for <see cref="Sfx"/>)
/// instead of its usual thud, louder the harder it hits.
/// </summary>
public interface IImpactSound
{
	string ImpactSound { get; }
}
