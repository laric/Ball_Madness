using System;
using System.Collections.Generic;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness.LevelGen;

/// <summary>
/// Lays a course out piece by piece into a <see cref="LevelFile"/>.
///
/// The writer keeps a cursor: a point on the top surface of the course at
/// the middle of its open end, and a heading of +Z or +X. Sections place
/// parts in the cursor's own frame (u forward, v sideways, h up, all in
/// metres from the cursor) and then move the cursor on. Because the course
/// only ever heads +Z or +X and always moves forward, a new piece can never
/// overlap an earlier one, and it always runs down and away from the
/// isometric camera.
/// </summary>
public class CourseWriter
{
	public LevelFile Level { get; }
	public Random Rng { get; }

	/// <summary>Top surface at the middle of the course's open end.</summary>
	public Vector3 Pos { get; private set; }

	/// <summary>True when heading +X, false when heading +Z.</summary>
	public bool AlongX { get; private set; }

	public Vector3 Dir => AlongX ? Vector3.Right : Vector3.Back;

	/// <summary>Sideways: the yawed box's local +X, so sizes map straight on.</summary>
	public Vector3 Side => AlongX ? Vector3.Forward : Vector3.Right;

	public float Yaw => AlongX ? 90 : 0;

	/// <summary>Lowest top surface so far, for the kill height.</summary>
	public float LowestY { get; private set; }

	private int _gates;
	private readonly Dictionary<string, int> _names = new();

	// Ground covered by each section, to check that sections which don't
	// follow each other never overlap (wide hazards reach far to the side).
	private readonly List<(Rect2 Rect, int Section)> _footprints = new();
	private int _section;
	private int _firstHazard;

	public CourseWriter(LevelFile level, Random rng)
	{
		Level = level;
		Rng = rng;
	}

	/// <summary>Starts a new section; call <see cref="EndSection"/> when it's laid.</summary>
	public void BeginSection()
	{
		_section++;
		_firstHazard = Level.Hazards.Count;
	}

	/// <summary>Records the ground the section's hazards sweep over.</summary>
	public void EndSection()
	{
		for (int i = _firstHazard; i < Level.Hazards.Count; i++)
		{
			var hazard = Level.Hazards[i];
			// Mechanics mark their own ground with Cover.
			if (hazard.Type is not ("mover" or "spinner" or "chaser"))
				continue;
			Vector3 pos = hazard.Pos;
			if (hazard.Type == "spinner")
			{
				float reach = 0;
				foreach (var part in hazard.Parts)
					reach = Mathf.Max(reach, Mathf.Max((part.Size ?? default).X, (part.Size ?? default).Z) / 2);
				Mark(pos, reach, reach);
				continue;
			}
			float halfV = 0.5f, halfU = 0.5f;
			foreach (var part in hazard.Parts ?? new List<Part>())
			{
				halfV = Mathf.Max(halfV, (part.Size ?? default).X / 2);
				halfU = Mathf.Max(halfU, (part.Size ?? default).Z / 2);
			}
			Mark(pos, halfU, halfV);
			if (hazard.Offset is Vec3 offset)
				Mark(pos + (Vector3)offset, halfU, halfV);
		}
	}

	/// <summary>True if two sections that aren't neighbours cover the same ground.</summary>
	public bool HasOverlaps()
	{
		for (int i = 0; i < _footprints.Count; i++)
		{
			for (int j = i + 1; j < _footprints.Count; j++)
			{
				if (Mathf.Abs(_footprints[i].Section - _footprints[j].Section) < 2)
					continue;
				if (_footprints[i].Rect.Grow(-0.05f).Intersects(_footprints[j].Rect.Grow(-0.05f)))
					return true;
			}
		}
		return false;
	}

	/// <summary>Records ground at (u, v) in the cursor's frame as covered by this section.</summary>
	public void Cover(float u, float v, float halfU, float halfV) => Mark(At(u, v), halfU, halfV);

	private void Mark(Vector3 centre, float halfU, float halfV)
	{
		var half = AlongX ? new Vector2(halfU, halfV) : new Vector2(halfV, halfU);
		_footprints.Add((new Rect2(new Vector2(centre.X, centre.Z) - half, half * 2), _section));
	}

	public Vector3 At(float u, float v = 0, float h = 0) => Pos + Dir * u + Side * v + Vector3.Up * h;

	/// <summary>Moves the cursor <paramref name="u"/> forward and <paramref name="drop"/> down.</summary>
	public void Advance(float u, float drop = 0, float v = 0)
	{
		Pos = At(u, v, -drop);
		LowestY = Mathf.Min(LowestY, Pos.Y);
	}

	/// <summary>
	/// Turns on a square pad starting at the cursor: the pad's centre is
	/// <paramref name="size"/>/2 ahead, and the course leaves it on the other heading.
	/// </summary>
	public void Turn(float size, string material)
	{
		Box("Corner", size / 2, 0, -0.5f, new Vector3(size, 1, size), 0, material);
		Waypoint(size / 2);
		Vector3 centre = At(size / 2);
		AlongX = !AlongX;
		Pos = centre;
		Pos = At(size / 2);
	}

	/// <summary>
	/// A slab whose top surface runs from (u0, v, h0) for <paramref name="length"/>
	/// metres forward while dropping <paramref name="drop"/>: a floor, ramp or rail.
	/// </summary>
	public void Slab(string name, float u0, float length, float drop, float width, string material,
		float v = 0, float h0 = 0, float thickness = 1)
	{
		float angle = Mathf.Atan2(drop, length);
		float len3d = Mathf.Sqrt(length * length + drop * drop);
		float u = u0 + length / 2 - thickness / 2 * Mathf.Sin(angle);
		float h = h0 - drop / 2 - thickness / 2 * Mathf.Cos(angle);
		Box(name, u, v, h, new Vector3(width, thickness, len3d), Mathf.RadToDeg(angle), material);
		LowestY = Mathf.Min(LowestY, At(u0 + length, v, h0 - drop).Y);
	}

	/// <summary>Rails on both edges of a slab, 0.55 m high.</summary>
	public void Rails(float u0, float length, float drop, float width, string material, float v = 0)
	{
		for (int side = -1; side <= 1; side += 2)
			Slab("Rail", u0, length, drop, 0.3f, material, v + side * (width / 2 + 0.15f), 0.55f, 1.1f);
	}

	/// <summary>A box centred at (u, v, h) in the cursor's frame; size is (sideways, up, forward).</summary>
	public void Box(string name, float u, float v, float h, Vector3 size, float pitch, string material)
	{
		Mark(At(u, v, h), size.Z / 2, size.X / 2);
		Level.Course.Add(new Part
		{
			Name = Name(name),
			Shape = "box",
			Pos = At(u, v, h),
			Rot = Rot(pitch),
			Size = size,
			Material = material,
		});
	}

	public void Post(float u, float v, float floorH, float radius, float height, string material)
	{
		Mark(At(u, v), radius, radius);
		Level.Course.Add(new Part
		{
			Name = Name("Post"),
			Shape = "cylinder",
			Pos = At(u, v, floorH + height / 2 - 0.25f),
			Radius = radius,
			Height = height,
			Material = material,
		});
	}

	/// <summary>
	/// Like <see cref="Slab"/>, but the slab is a mechanic's body (ice, a
	/// conveyor belt) whose local +Z points down the slab.
	/// </summary>
	public Hazard SlabHazard(string name, string type, float u0, float length, float drop, float width,
		float v = 0, float h0 = 0, float thickness = 1)
	{
		float angle = Mathf.Atan2(drop, length);
		float len3d = Mathf.Sqrt(length * length + drop * drop);
		float u = u0 + length / 2 - thickness / 2 * Mathf.Sin(angle);
		float h = h0 - drop / 2 - thickness / 2 * Mathf.Cos(angle);
		Cover(u, v, length / 2, width / 2);
		LowestY = Mathf.Min(LowestY, At(u0 + length, v, h0 - drop).Y);
		var hazard = AddHazard(name, type, u, v, h);
		hazard.Rot = Rot(Mathf.RadToDeg(angle));
		hazard.Parts.Add(new Part { Shape = "box", Pos = default, Size = new Vector3(width, thickness, len3d) });
		return hazard;
	}

	/// <summary>Rotation for something tilted by <paramref name="pitch"/> and turned <paramref name="turn"/> degrees from the heading.</summary>
	public Vec3? Rot(float pitch, float turn)
	{
		var rot = new Vec3(pitch, Yaw + turn, 0);
		return rot == default ? null : rot;
	}

	public Hazard AddHazard(string name, string type, float u, float v, float h)
	{
		var hazard = new Hazard { Name = Name(name), Type = type, Pos = At(u, v, h), Rot = Rot(0), Parts = new List<Part>() };
		Level.Hazards.Add(hazard);
		return hazard;
	}

	/// <summary>Turns a direction in the cursor's frame (u, v, h) into level space.</summary>
	public Vector3 Vector(float u, float v, float h = 0) => Dir * u + Side * v + Vector3.Up * h;

	/// <summary>An area the autopilot watches; returns its name.</summary>
	public string Gate(float u, float v, float h, Vector3 size)
	{
		string name = $"Gate{++_gates}";
		Level.Gates.Add(new Gate { Name = name, Pos = At(u, v, h), Rot = Rot(0), Size = size });
		return name;
	}

	/// <summary>A route waypoint at ball height above the floor at (u, v, h).</summary>
	public void Waypoint(float u, float v = 0, float h = 0, float? speed = null, string gate = null, string waitFor = null)
	{
		Level.Route.Add(new Waypoint { Pos = At(u, v, h + 0.5f), Speed = speed, Gate = gate, WaitFor = gate != null ? waitFor ?? "clear" : null });
	}

	/// <summary>A checkpoint across the course on flat floor at (u, v).</summary>
	public void Checkpoint(float u, float width, float v = 0)
	{
		Level.Checkpoints.Add(new Checkpoint { Pos = At(u, v), Rot = Rot(0), Size = new Vector3(width, 3, 2) });
	}

	public float Range(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
	public int Range(int min, int maxInclusive) => Rng.Next(min, maxInclusive + 1);
	public bool Chance(float p) => Rng.NextDouble() < p;
	public int Sign() => Rng.Next(2) == 0 ? -1 : 1;

	private Vec3? Rot(float pitch) =>
		pitch == 0 && Yaw == 0 ? null : new Vec3(pitch, Yaw, 0);

	private string Name(string name)
	{
		_names.TryGetValue(name, out int n);
		_names[name] = ++n;
		return n == 1 ? name : $"{name}{n}";
	}
}
