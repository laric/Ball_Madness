using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace BallMadness.LevelFormat;

/// <summary>
/// A level as stored on disk (<c>levels/*.json</c> inside a pack). Plain data
/// only: <see cref="LevelBuilder"/> maps every type name to one of the game's
/// own scripts, so a level from a user pack can't run code.
///
/// Positions are in metres in level space; rotations are Euler angles in
/// degrees (Godot's YXZ order). Optional values left out of the file take
/// the defaults below.
/// </summary>
public class LevelFile
{
	/// <summary>The newest format this build reads and writes.</summary>
	public const int CurrentFormat = 1;

	public int Format { get; set; } = CurrentFormat;
	public string Name { get; set; } = "";
	public string Author { get; set; }

	/// <summary>From 0 (gentlest) to 1 (hardest). Optional; set on generated levels.</summary>
	public float? Difficulty { get; set; }

	/// <summary>How a generated level was made (generator version and seed), so it can be made again.</summary>
	public string Generator { get; set; }

	/// <summary>Seconds on the clock when the level starts.</summary>
	public float TimeLimit { get; set; } = 60.0f;

	/// <summary>Anything below this height counts as having fallen off the course.</summary>
	public float KillHeight { get; set; } = -12.0f;

	/// <summary>
	/// Named looks that parts refer to by key. The pack's theme replaces the
	/// ones it has a look for, unless they're marked <see cref="MaterialDef.Override"/>.
	/// </summary>
	public Dictionary<string, MaterialDef> Materials { get; set; } = new();

	public Vec3 Spawn { get; set; }

	/// <summary>Static course geometry.</summary>
	public List<Part> Course { get; set; } = new();

	/// <summary>Moving obstacles and enemies.</summary>
	public List<Hazard> Hazards { get; set; } = new();

	public List<Checkpoint> Checkpoints { get; set; } = new();
	public List<Goal> Goals { get; set; } = new();

	/// <summary>Invisible areas the autopilot watches to time its run past a hazard.</summary>
	public List<Gate> Gates { get; set; } = new();

	/// <summary>Autopilot waypoints, in order. Optional for user levels.</summary>
	public List<Waypoint> Route { get; set; } = new();
}

/// <summary>
/// Either the course's grid shader (<see cref="Line"/> or <see cref="Texture"/>
/// set) or a plain surface (both left out).
/// </summary>
public class MaterialDef
{
	public Vec3 Color { get; set; } = new(0.55f, 0.62f, 0.75f);

	/// <summary>Grid line colour; set for the grid shader.</summary>
	public Vec3? Line { get; set; }

	public float? Roughness { get; set; }
	public float? Metallic { get; set; }

	/// <summary>Makes the grid lines (or a plain surface) give off their colour at this strength.</summary>
	public float? Glow { get; set; }

	/// <summary>
	/// A procedural surface pattern, see <see cref="LevelBuilder.Patterns"/>:
	/// "grass", "moss", "sand", "stone", "planks", "ice", "basalt", "metal" or "crystal".
	/// </summary>
	public string Texture { get; set; }

	/// <summary>The texture's second colour (soil, moss, mortar, cracks, seams).</summary>
	public Vec3? Detail { get; set; }

	/// <summary>Makes the texture bigger (above 1) or smaller.</summary>
	public float? Scale { get; set; }

	/// <summary>True to keep this look whatever the pack's theme says for the same key.</summary>
	public bool? Override { get; set; }
}

/// <summary>
/// A box, cylinder or sphere: the building block of courses and hazards.
/// Course parts can also be terrain: a "bump" (a smooth ridge across the
/// course), a "mound" (a round hill) or a "rock", each filling its <see cref="Size"/>.
/// </summary>
public class Part
{
	public string Name { get; set; }

	/// <summary>"box", "cylinder" or "sphere"; in the course also "bump", "mound" or "rock".</summary>
	public string Shape { get; set; } = "box";

	public Vec3 Pos { get; set; }
	public Vec3? Rot { get; set; }

	/// <summary>Box, bump, mound and rock size.</summary>
	public Vec3? Size { get; set; }

	/// <summary>Cylinder and sphere radius.</summary>
	public float? Radius { get; set; }

	/// <summary>Cylinder height.</summary>
	public float? Height { get; set; }

	/// <summary>Key into <see cref="LevelFile.Materials"/>.</summary>
	public string Material { get; set; }

	/// <summary>False for decoration the ball passes through.</summary>
	public bool? Collide { get; set; }

	/// <summary>False for an invisible wall.</summary>
	public bool? Visible { get; set; }
}

/// <summary>
/// A moving body built from <see cref="Parts"/> placed relative to it.
/// <list type="bullet">
/// <item>"mover": slides to <see cref="Offset"/> and back every <see cref="Period"/> seconds.</item>
/// <item>"spinner": turns <see cref="Speed"/> degrees a second around <see cref="Axis"/>.</item>
/// <item>"chaser": an enemy marble of <see cref="Radius"/> that rolls after the ball.</item>
/// <item>"conveyor": belt <see cref="Parts"/> that carry the ball along +Z at <see cref="Speed"/> m/s.</item>
/// <item>"ice": frictionless <see cref="Parts"/>.</item>
/// <item>"bumper": <see cref="Parts"/> that kick the ball away at <see cref="Force"/> m/s.</item>
/// <item>"hole": a hole of <see cref="Radius"/> cut into the course; with a <see cref="Period"/> it's a trapdoor.</item>
/// <item>"oneway": a door <see cref="Width"/> wide the ball can only pass along +Z.</item>
/// <item>"teleporter": a pad of <see cref="Radius"/> that sends the ball to <see cref="Target"/>.</item>
/// </list>
/// Mechanics without <see cref="Parts"/> (hole, oneway, teleporter) stand on
/// the floor at <see cref="Pos"/>.
/// </summary>
public class Hazard
{
	public string Name { get; set; }
	public string Type { get; set; } = "mover";
	public Vec3 Pos { get; set; }
	public Vec3? Rot { get; set; }

	// mover
	public Vec3? Offset { get; set; }
	public float? Period { get; set; }
	public float? Phase { get; set; }

	// spinner (degrees a second) and conveyor (metres a second)
	public float? Speed { get; set; }
	public Vec3? Axis { get; set; }

	// bumper
	public float? Force { get; set; }

	// hole: also radius, and period and phase for a trapdoor
	public float? Open { get; set; }
	public float? Depth { get; set; }

	// oneway
	public float? Width { get; set; }
	public float? Height { get; set; }

	// teleporter: also radius
	public Vec3? Target { get; set; }
	public bool? KeepSpeed { get; set; }

	// chaser, hole and teleporter
	public float? Radius { get; set; }
	public float? ChaseRadius { get; set; }
	public float? PushForce { get; set; }
	public float? MaxSpeed { get; set; }
	public float? Mass { get; set; }
	public float? Friction { get; set; }
	public float? Bounce { get; set; }
	public string Material { get; set; }

	public List<Part> Parts { get; set; }
}

/// <summary>
/// A respawn line. <see cref="Size"/> is the trigger box (width, height,
/// depth) standing on <see cref="Pos"/>; the yellow stripe is drawn under it.
/// </summary>
public class Checkpoint
{
	public Vec3 Pos { get; set; }
	public Vec3? Rot { get; set; }
	public Vec3 Size { get; set; } = new(8, 3, 2);
}

/// <summary>The finish pad, with its pole and flag, centred on <see cref="Pos"/>.</summary>
public class Goal
{
	public Vec3 Pos { get; set; }
	public float? Radius { get; set; }
}

public class Gate
{
	public string Name { get; set; }
	public Vec3 Pos { get; set; }
	public Vec3? Rot { get; set; }
	public Vec3 Size { get; set; } = new(2, 2, 2);
}

public class Waypoint
{
	public Vec3 Pos { get; set; }

	/// <summary>Caps the autopilot's speed on the way to this waypoint.</summary>
	public float? Speed { get; set; }

	/// <summary>Name of a <see cref="Gate"/> to wait for before heading here.</summary>
	public string Gate { get; set; }

	/// <summary>"clear" (wait until nothing moving is in the gate) or "occupied".</summary>
	public string WaitFor { get; set; }
}

/// <summary>A vector stored as a compact <c>[x, y, z]</c> array.</summary>
[JsonConverter(typeof(Vec3JsonConverter))]
public readonly record struct Vec3(float X, float Y, float Z)
{
	public static implicit operator Vector3(Vec3 v) => new(v.X, v.Y, v.Z);
	public static implicit operator Vec3(Vector3 v) => new(v.X, v.Y, v.Z);
}

public class Vec3JsonConverter : JsonConverter<Vec3>
{
	public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException("Expected [x, y, z]");
		var values = new float[3];
		int count = 0;
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
		{
			if (count >= 3)
				throw new JsonException("Expected [x, y, z]");
			values[count++] = reader.GetSingle();
		}
		if (count != 3)
			throw new JsonException("Expected [x, y, z]");
		return new Vec3(values[0], values[1], values[2]);
	}

	public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
	{
		// Raw so an indented file keeps each vector on one line.
		writer.WriteRawValue($"[{Num(value.X)}, {Num(value.Y)}, {Num(value.Z)}]");
	}

	/// <summary>Up to four decimals, no trailing zeros, never an exponent.</summary>
	public static string Num(float value)
	{
		double rounded = Math.Round(value, 4);
		if (rounded == 0)
			rounded = 0; // no "-0"
		return rounded.ToString("0.####", CultureInfo.InvariantCulture);
	}
}

/// <summary>Rounds plain numbers like <see cref="Vec3JsonConverter"/> does.</summary>
public class RoundedFloatConverter : JsonConverter<float>
{
	public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> reader.GetSingle();

	public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
		=> writer.WriteRawValue(Vec3JsonConverter.Num(value));
}

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	WriteIndented = true,
	IndentCharacter = '\t',
	IndentSize = 1,
	ReadCommentHandling = JsonCommentHandling.Skip,
	AllowTrailingCommas = true,
	Converters = new[] { typeof(RoundedFloatConverter) })]
[JsonSerializable(typeof(LevelFile))]
[JsonSerializable(typeof(PackFile))]
[JsonSerializable(typeof(CampaignFile))]
[JsonSerializable(typeof(ThemeFile))]
internal partial class LevelJsonContext : JsonSerializerContext
{
}
