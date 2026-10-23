using System;
using System.Collections.Generic;
using Godot;

namespace BallMadness.Editing;

/// <summary>
/// One row of the editor's property panel, bound to a value of the level
/// being edited. <see cref="PropertyPanel"/> turns each kind into controls.
/// </summary>
public abstract record Field(string Label)
{
	/// <summary>True if changing this field changes which fields the panel shows.</summary>
	public bool Rebuilds { get; init; }
}

public record NumberField(string Label, Func<float> Get, Action<float> Set) : Field(Label)
{
	public float Min { get; init; } = -1000;
	public float Max { get; init; } = 1000;

	/// <summary>How much the spin box's arrows change the value.</summary>
	public float Arrow { get; init; } = 0.5f;
}

public record VectorField(string Label, Func<Vector3> Get, Action<Vector3> Set) : Field(Label)
{
	public float Min { get; init; } = -1000;
	public float Max { get; init; } = 1000;
	public float Arrow { get; init; } = 0.5f;
}

public record TextField(string Label, Func<string> Get, Action<string> Set) : Field(Label);

public record ChoiceField(string Label, IReadOnlyList<string> Options, Func<int> Get, Action<int> Set) : Field(Label);

public record ToggleField(string Label, Func<bool> Get, Action<bool> Set) : Field(Label);

/// <summary>Picks one of the level's materials or a preset (see <see cref="EditorCatalog.Materials"/>).</summary>
public record MaterialField(string Label, Func<string> Get, Action<string> Set) : Field(Label);

public record ButtonField(string Label, Action Pressed) : Field(Label);

/// <summary>A line of text with no value, for headings and hints.</summary>
public record NoteField(string Label) : Field(Label);
