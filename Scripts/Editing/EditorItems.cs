using System;
using System.Collections.Generic;
using System.Linq;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness.Editing;

public enum ItemKind { Part, Hazard, Checkpoint, Goal, Spawn }

/// <summary>An object in the level being edited: which list it's in and where.</summary>
public readonly record struct EditorItem(ItemKind Kind, int Index)
{
	public static readonly EditorItem Spawn = new(ItemKind.Spawn, 0);
}

/// <summary>An oriented box around an object, for picking, outlines and checks.</summary>
public readonly record struct Obb(Transform3D Transform, Vector3 Center, Vector3 Half)
{
	public IEnumerable<Vector3> Corners()
	{
		for (int i = 0; i < 8; i++)
		{
			var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
			yield return Transform * (Center + Half * sign);
		}
	}

	public float MinY => Corners().Min(c => c.Y);
	public float MaxY => Corners().Max(c => c.Y);
	public Vector3 WorldCenter => Transform * Center;

	/// <summary>Distance along the ray to where it enters the box, or null if it misses.</summary>
	public float? Intersect(Vector3 origin, Vector3 dir)
	{
		var inverse = Transform.AffineInverse();
		Vector3 o = inverse * origin - Center;
		Vector3 d = inverse.Basis * dir;
		float near = float.NegativeInfinity, far = float.PositiveInfinity;
		for (int axis = 0; axis < 3; axis++)
		{
			float oa = o[axis], da = d[axis], h = Half[axis];
			if (Mathf.Abs(da) < 1e-6f)
			{
				if (oa < -h || oa > h)
					return null;
				continue;
			}
			float t1 = (-h - oa) / da, t2 = (h - oa) / da;
			if (t1 > t2)
				(t1, t2) = (t2, t1);
			near = Mathf.Max(near, t1);
			far = Mathf.Min(far, t2);
			if (near > far)
				return null;
		}
		return far < 0 ? null : Mathf.Max(near, 0);
	}
}

/// <summary>
/// Reads and changes objects of a <see cref="LevelFile"/> through
/// <see cref="EditorItem"/>s, so the editor treats parts, hazards and markers alike.
/// </summary>
public static class EditorItems
{
	public static IEnumerable<EditorItem> All(LevelFile level)
	{
		for (int i = 0; i < level.Course.Count; i++)
			yield return new EditorItem(ItemKind.Part, i);
		for (int i = 0; i < level.Hazards.Count; i++)
			yield return new EditorItem(ItemKind.Hazard, i);
		for (int i = 0; i < level.Checkpoints.Count; i++)
			yield return new EditorItem(ItemKind.Checkpoint, i);
		for (int i = 0; i < level.Goals.Count; i++)
			yield return new EditorItem(ItemKind.Goal, i);
		yield return EditorItem.Spawn;
	}

	public static bool Exists(LevelFile level, EditorItem item) => item.Kind switch
	{
		ItemKind.Part => item.Index < level.Course.Count,
		ItemKind.Hazard => item.Index < level.Hazards.Count,
		ItemKind.Checkpoint => item.Index < level.Checkpoints.Count,
		ItemKind.Goal => item.Index < level.Goals.Count,
		_ => true,
	} && item.Index >= 0;

	public static string Label(LevelFile level, EditorItem item)
	{
		switch (item.Kind)
		{
			case ItemKind.Part:
				var part = level.Course[item.Index];
				return part.Name ?? $"{Capitalize(IsRamp(part) ? "ramp" : part.Shape)} {item.Index + 1}";
			case ItemKind.Hazard:
				var hazard = level.Hazards[item.Index];
				return hazard.Name ?? $"{Capitalize(hazard.Type)} {item.Index + 1}";
			case ItemKind.Checkpoint:
				return $"Checkpoint {item.Index + 1}";
			case ItemKind.Goal:
				return level.Goals.Count == 1 ? "Goal" : $"Goal {item.Index + 1}";
			default:
				return "Spawn";
		}
	}

	public static Vector3 GetPos(LevelFile level, EditorItem item) => item.Kind switch
	{
		ItemKind.Part => level.Course[item.Index].Pos,
		ItemKind.Hazard => level.Hazards[item.Index].Pos,
		ItemKind.Checkpoint => level.Checkpoints[item.Index].Pos,
		ItemKind.Goal => level.Goals[item.Index].Pos,
		_ => level.Spawn,
	};

	public static void SetPos(LevelFile level, EditorItem item, Vector3 pos)
	{
		switch (item.Kind)
		{
			case ItemKind.Part: level.Course[item.Index].Pos = pos; break;
			case ItemKind.Hazard: level.Hazards[item.Index].Pos = pos; break;
			case ItemKind.Checkpoint: level.Checkpoints[item.Index].Pos = pos; break;
			case ItemKind.Goal: level.Goals[item.Index].Pos = pos; break;
			default: level.Spawn = pos; break;
		}
	}

	public static bool CanRotate(EditorItem item) => item.Kind is ItemKind.Part or ItemKind.Hazard or ItemKind.Checkpoint;

	/// <summary>Rotation in degrees (Godot's YXZ Euler order); zero for objects that can't turn.</summary>
	public static Vector3 GetRot(LevelFile level, EditorItem item)
	{
		Vec3? rot = item.Kind switch
		{
			ItemKind.Part => level.Course[item.Index].Rot,
			ItemKind.Hazard => level.Hazards[item.Index].Rot,
			ItemKind.Checkpoint => level.Checkpoints[item.Index].Rot,
			_ => null,
		};
		return rot ?? Vector3.Zero;
	}

	public static void SetRot(LevelFile level, EditorItem item, Vector3 degrees)
	{
		degrees = new Vector3(WrapDegrees(degrees.X), WrapDegrees(degrees.Y), WrapDegrees(degrees.Z));
		Vec3? rot = degrees.IsZeroApprox() ? null : degrees;
		switch (item.Kind)
		{
			case ItemKind.Part: level.Course[item.Index].Rot = rot; break;
			case ItemKind.Hazard: level.Hazards[item.Index].Rot = rot; break;
			case ItemKind.Checkpoint: level.Checkpoints[item.Index].Rot = rot; break;
		}
	}

	/// <summary>The box the editor outlines and picks for an object.</summary>
	public static Obb Bounds(LevelFile level, EditorItem item)
	{
		switch (item.Kind)
		{
			case ItemKind.Part:
				var part = level.Course[item.Index];
				return new Obb(LevelBuilder.Place(part.Pos, part.Rot), Vector3.Zero, PartHalf(part));
			case ItemKind.Hazard:
				return HazardBounds(level.Hazards[item.Index]);
			case ItemKind.Checkpoint:
				var checkpoint = level.Checkpoints[item.Index];
				Vector3 size = checkpoint.Size;
				return new Obb(LevelBuilder.Place(checkpoint.Pos, checkpoint.Rot), new Vector3(0, size.Y / 2, 0), size / 2);
			case ItemKind.Goal:
				var goal = level.Goals[item.Index];
				float r = goal.Radius ?? LevelBuilder.DefaultGoalRadius;
				return new Obb(new Transform3D(Basis.Identity, goal.Pos), new Vector3(0, 2, 0), new Vector3(r, 2, r));
			default:
				return new Obb(new Transform3D(Basis.Identity, level.Spawn), Vector3.Zero, Vector3.One * 0.5f);
		}
	}

	/// <summary>The lowest point an object ever reaches, counting a mover's far end.</summary>
	public static float LowestPoint(LevelFile level, EditorItem item)
	{
		var bounds = Bounds(level, item);
		float low = bounds.MinY;
		if (item.Kind == ItemKind.Hazard && level.Hazards[item.Index] is { Type: "mover" } mover)
		{
			Vector3 offset = mover.Offset ?? new Vector3(0, 0, 4);
			low = Mathf.Min(low, low + (bounds.Transform.Basis * offset).Y);
		}
		return low;
	}

	public static void Remove(LevelFile level, EditorItem item)
	{
		switch (item.Kind)
		{
			case ItemKind.Part: level.Course.RemoveAt(item.Index); break;
			case ItemKind.Hazard: level.Hazards.RemoveAt(item.Index); break;
			case ItemKind.Checkpoint: level.Checkpoints.RemoveAt(item.Index); break;
			case ItemKind.Goal: level.Goals.RemoveAt(item.Index); break;
		}
	}

	/// <summary>Copies an object, puts the copy right next to it (towards +X) and returns it.</summary>
	public static EditorItem? Duplicate(LevelFile level, EditorItem item)
	{
		if (item.Kind == ItemKind.Spawn)
			return null;

		// Round-trip through JSON for a deep copy.
		var holder = new LevelFile();
		switch (item.Kind)
		{
			case ItemKind.Part: holder.Course.Add(level.Course[item.Index]); break;
			case ItemKind.Hazard: holder.Hazards.Add(level.Hazards[item.Index]); break;
			case ItemKind.Checkpoint: holder.Checkpoints.Add(level.Checkpoints[item.Index]); break;
			case ItemKind.Goal: holder.Goals.Add(level.Goals[item.Index]); break;
		}
		var copy = Packs.ParseLevel(Packs.Serialize(holder), "copy");

		EditorItem added;
		switch (item.Kind)
		{
			case ItemKind.Part: level.Course.Add(copy.Course[0]); added = new(item.Kind, level.Course.Count - 1); break;
			case ItemKind.Hazard: level.Hazards.Add(copy.Hazards[0]); added = new(item.Kind, level.Hazards.Count - 1); break;
			case ItemKind.Checkpoint: level.Checkpoints.Add(copy.Checkpoints[0]); added = new(item.Kind, level.Checkpoints.Count - 1); break;
			default: level.Goals.Add(copy.Goals[0]); added = new(item.Kind, level.Goals.Count - 1); break;
		}

		var bounds = Bounds(level, item);
		float width = bounds.Corners().Max(c => c.X) - bounds.Corners().Min(c => c.X);
		SetPos(level, added, GetPos(level, added) + Vector3.Right * Mathf.Ceil(width * 2) / 2);
		if (added.Kind == ItemKind.Part && copy.Course[0].Name != null)
			copy.Course[0].Name = null;
		if (added.Kind == ItemKind.Hazard && copy.Hazards[0].Name != null)
			copy.Hazards[0].Name = null;
		return added;
	}

	/// <summary>
	/// Everything that would stop the level working: no goal, no ground under
	/// the spawn, objects below the kill height. Empty when it's ready to save.
	/// </summary>
	public static List<string> Problems(LevelFile level)
	{
		var problems = new List<string>();
		if (string.IsNullOrWhiteSpace(level.Name))
			problems.Add("Give the level a name.");
		if (level.TimeLimit <= 0)
			problems.Add("The time limit must be more than 0 seconds.");
		if (level.Goals.Count == 0)
			problems.Add("Add at least one goal.");

		if (level.Spawn.Y < level.KillHeight)
			problems.Add("The spawn is below the kill height.");
		else if (!HasGroundUnder(level, level.Spawn))
			problems.Add("The spawn isn't above any part of the course.");

		var below = All(level)
			.Where(i => i.Kind != ItemKind.Spawn && LowestPoint(level, i) < level.KillHeight)
			.Select(i => Label(level, i))
			.ToList();
		if (below.Count > 0)
		{
			string names = string.Join(", ", below.Take(3)) + (below.Count > 3 ? $" and {below.Count - 3} more" : "");
			problems.Add($"Below the kill height ({level.KillHeight:0.#} m): {names}. Raise them or lower the kill height.");
		}
		return problems;
	}

	/// <summary>Whether a solid part or hazard is somewhere straight below <paramref name="point"/>.</summary>
	public static bool HasGroundUnder(LevelFile level, Vector3 point) => GroundUnder(level, point) != null;

	/// <summary>The height of the first solid part or hazard straight below <paramref name="point"/>, if any.</summary>
	public static float? GroundUnder(LevelFile level, Vector3 point)
	{
		float? nearest = null;
		foreach (var item in All(level))
		{
			if (item.Kind is not (ItemKind.Part or ItemKind.Hazard))
				continue;
			if (item.Kind == ItemKind.Part && level.Course[item.Index].Collide == false)
				continue;
			if (Bounds(level, item).Intersect(point, Vector3.Down) is float t && (nearest == null || t < nearest))
				nearest = t;
		}
		return nearest is float distance ? point.Y - distance : null;
	}

	/// <summary>The lowest point of the whole level, ignoring the spawn.</summary>
	public static float LowestPoint(LevelFile level) =>
		All(level).Where(i => i.Kind != ItemKind.Spawn).Select(i => LowestPoint(level, i)).DefaultIfEmpty(level.Spawn.Y).Min();

	public static bool IsRamp(Part part) =>
		part.Shape == "box" && part.Rot is Vec3 rot && (Mathf.Abs(rot.X) > 0.01f || Mathf.Abs(rot.Z) > 0.01f);

	public static Vector3 PartHalf(Part part) => part.Shape switch
	{
		"cylinder" => new Vector3(part.Radius ?? 0.5f, (part.Height ?? 1) / 2, part.Radius ?? 0.5f),
		"sphere" => Vector3.One * (part.Radius ?? 0.5f),
		_ => (Vector3)(part.Size ?? Vector3.One) / 2,
	};

	private static Obb HazardBounds(Hazard hazard)
	{
		var transform = LevelBuilder.Place(hazard.Pos, hazard.Rot);
		if (hazard.Type == "chaser" || hazard.Parts == null || hazard.Parts.Count == 0)
			return new Obb(transform, Vector3.Zero, Vector3.One * (hazard.Radius ?? 0.5f));

		Vector3 min = Vector3.One * float.MaxValue, max = -min;
		foreach (var part in hazard.Parts)
		{
			var corners = new Obb(LevelBuilder.Place(part.Pos, part.Rot), Vector3.Zero, PartHalf(part)).Corners();
			foreach (var c in corners)
			{
				min = min.Min(c);
				max = max.Max(c);
			}
		}
		return new Obb(transform, (min + max) / 2, (max - min) / 2);
	}

	private static float WrapDegrees(float degrees)
	{
		float wrapped = Mathf.PosMod(degrees + 180, 360) - 180;
		return Mathf.IsEqualApprox(wrapped, -180) ? 180 : (float)Math.Round(wrapped, 3);
	}

	private static string Capitalize(string s) =>
		string.IsNullOrEmpty(s) ? "Part" : char.ToUpperInvariant(s[0]) + s[1..];
}
