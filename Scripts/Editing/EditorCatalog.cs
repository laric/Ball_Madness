using System;
using System.Collections.Generic;
using System.Linq;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness.Editing;

/// <summary>One button in the editor's palette: what it places and, for hazards, the settings it has.</summary>
public sealed class CatalogEntry
{
	public string Id { get; init; }
	public string Label { get; init; }
	public string Category { get; init; }
	public string Tooltip { get; init; }

	/// <summary>
	/// Adds a new object to the level standing on <c>floor</c> (a point on the
	/// build grid) and returns it.
	/// </summary>
	public Func<LevelFile, Vector3, EditorItem> Add { get; init; }

	/// <summary>For a hazard entry: the hazard <c>type</c> it makes.</summary>
	public string HazardType { get; init; }

	/// <summary>For a hazard entry: the property panel rows for that type's own settings.</summary>
	public Func<Hazard, IEnumerable<Field>> HazardFields { get; init; }
}

/// <summary>
/// Everything the editor can place, in palette order, plus the material
/// presets. To add a new hazard type, add its entry here (and its builder
/// case in <see cref="LevelBuilder"/>); the palette and property panel are
/// built from this list.
/// </summary>
public static class EditorCatalog
{
	public const string Parts = "Parts";
	public const string Hazards = "Hazards";
	public const string Mechanics = "Mechanics";
	public const string Markers = "Markers";

	public static readonly IReadOnlyList<CatalogEntry> Entries = new List<CatalogEntry>
	{
		new()
		{
			Id = "box", Label = "Box", Category = Parts,
			Tooltip = "A floor tile, wall or block.",
			Add = (level, floor) => AddPart(level, new Part { Shape = "box", Pos = floor, Size = new Vec3(4, 1, 4), Material = "grid" }),
		},
		new()
		{
			Id = "ramp", Label = "Ramp", Category = Parts,
			Tooltip = "A box tilted down towards +Z. Change the tilt with T and G or in the rotation's X.",
			Add = (level, floor) => AddPart(level, new Part { Name = "Ramp", Shape = "box", Pos = floor, Rot = new Vec3(20, 0, 0), Size = new Vec3(4, 1, 8), Material = "grid" }),
		},
		new()
		{
			Id = "cylinder", Label = "Cylinder", Category = Parts,
			Tooltip = "A round platform or post.",
			Add = (level, floor) => AddPart(level, new Part { Shape = "cylinder", Pos = floor, Radius = 2, Height = 1, Material = "grid" }),
		},
		new()
		{
			Id = "mover", Label = "Mover", Category = Hazards, HazardType = "mover",
			Tooltip = "Slides to its offset and back: moving platforms, pistons, sliding bridges.",
			Add = (level, floor) => AddHazard(level, new Hazard
			{
				Type = "mover", Pos = floor, Offset = new Vec3(0, 0, 6), Period = 4,
				Parts = new List<Part> { new() { Shape = "box", Pos = default, Size = new Vec3(4, 0.5f, 4), Material = "slider" } },
			}),
			HazardFields = h => new Field[]
			{
				new VectorField("Offset", () => h.Offset ?? new Vector3(0, 0, 4), v => h.Offset = v),
				new NumberField("Period (s)", () => h.Period ?? 4, v => h.Period = v) { Min = 0.2f, Max = 120, Arrow = 0.5f },
				new NumberField("Phase (0-1)", () => h.Phase ?? 0, v => h.Phase = v) { Min = 0, Max = 1, Arrow = 0.05f },
			},
		},
		new()
		{
			Id = "spinner", Label = "Spinner", Category = Hazards, HazardType = "spinner",
			Tooltip = "Turns steadily: sweeper bars and turntables.",
			Add = (level, floor) => AddHazard(level, new Hazard
			{
				Type = "spinner", Pos = floor, Speed = 60,
				Parts = new List<Part> { new() { Shape = "box", Pos = default, Size = new Vec3(8, 0.8f, 0.8f), Material = "sweeper" } },
			}),
			HazardFields = h => new Field[]
			{
				new NumberField("Speed (°/s)", () => h.Speed ?? 90, v => h.Speed = v) { Min = -720, Max = 720, Arrow = 15 },
				new VectorField("Axis", () => h.Axis ?? Vector3.Up, v => h.Axis = v) { Min = -1, Max = 1, Arrow = 1 },
			},
		},
		new()
		{
			Id = "chaser", Label = "Chaser", Category = Hazards, HazardType = "chaser",
			Tooltip = "An enemy marble that rolls after the ball when it comes near.",
			Add = (level, floor) => AddHazard(level, new Hazard { Type = "chaser", Pos = floor, Radius = 0.5f, ChaseRadius = 10 }),
			HazardFields = h => new Field[]
			{
				new NumberField("Radius", () => h.Radius ?? 0.5f, v => h.Radius = v) { Min = 0.2f, Max = 5, Arrow = 0.1f },
				new NumberField("Chase radius", () => h.ChaseRadius ?? 10, v => h.ChaseRadius = v) { Min = 0, Max = 100, Arrow = 1 },
				new NumberField("Push force", () => h.PushForce ?? 6, v => h.PushForce = v) { Min = 0, Max = 100, Arrow = 1 },
				new NumberField("Max speed", () => h.MaxSpeed ?? 5, v => h.MaxSpeed = v) { Min = 0, Max = 50, Arrow = 0.5f },
				new MaterialField("Material", () => h.Material, v => h.Material = v),
			},
		},
		new()
		{
			Id = "conveyor", Label = "Conveyor", Category = Mechanics, HazardType = "conveyor",
			Tooltip = "A belt that carries the ball along its arrows. Turn it to point the belt.",
			Add = (level, floor) => AddHazard(level, new Hazard
			{
				Type = "conveyor", Pos = floor, Speed = 4,
				Parts = new List<Part> { new() { Shape = "box", Pos = default, Size = new Vec3(4, 0.5f, 8) } },
			}),
			HazardFields = h => new Field[]
			{
				new NumberField("Belt speed (m/s)", () => h.Speed ?? 4, v => h.Speed = v) { Min = -30, Max = 30, Arrow = 0.5f },
				new NoteField("The belt runs along the conveyor's +Z; negative speeds run it backwards."),
			},
		},
		new()
		{
			Id = "ice", Label = "Ice", Category = Mechanics, HazardType = "ice",
			Tooltip = "A slippery floor: the ball slides and barely steers. Tilt it with T and G for an icy ramp.",
			Add = (level, floor) => AddHazard(level, new Hazard
			{
				Type = "ice", Pos = floor,
				Parts = new List<Part> { new() { Shape = "box", Pos = default, Size = new Vec3(4, 0.5f, 8) } },
			}),
		},
		new()
		{
			Id = "bumper", Label = "Bumper", Category = Mechanics, HazardType = "bumper",
			Tooltip = "A post that kicks the ball away when it touches.",
			Add = (level, floor) => AddHazard(level, new Hazard
			{
				Type = "bumper", Pos = floor, Force = 10,
				Parts = new List<Part> { new() { Shape = "cylinder", Pos = default, Radius = 0.8f, Height = 1 } },
			}),
			HazardFields = h => new Field[]
			{
				new NumberField("Kick (m/s)", () => h.Force ?? 10, v => h.Force = v) { Min = 0, Max = 50, Arrow = 1 },
			},
		},
		new()
		{
			Id = "hole", Label = "Hole", Category = Mechanics, HazardType = "hole",
			Tooltip = "A round hole cut into the floor it's placed on. Make it timed for a trapdoor that opens and closes.",
			Add = (level, floor) => AddHazard(level, new Hazard { Type = "hole", Pos = floor, Radius = LevelBuilder.DefaultHoleRadius }),
			HazardFields = HoleFields,
		},
		new()
		{
			Id = "oneway", Label = "One-way gate", Category = Mechanics, HazardType = "oneway",
			Tooltip = "A door the ball can only pass the way its arrows point. Turn it with R.",
			Add = (level, floor) => AddHazard(level, new Hazard { Type = "oneway", Pos = floor, Width = LevelBuilder.DefaultGateWidth }),
			HazardFields = h => new Field[]
			{
				new NumberField("Width", () => h.Width ?? LevelBuilder.DefaultGateWidth, v => h.Width = v) { Min = 1, Max = 40, Arrow = 0.5f },
				new NumberField("Height", () => h.Height ?? LevelBuilder.DefaultGateHeight, v => h.Height = v) { Min = 1, Max = 10, Arrow = 0.1f },
			},
		},
		new()
		{
			Id = "teleporter", Label = "Teleporter", Category = Mechanics, HazardType = "teleporter",
			Tooltip = "A pad that sends the ball to its exit. Set the exit in the panel on the right.",
			Add = (level, floor) => AddHazard(level, new Hazard
			{
				Type = "teleporter", Pos = floor, Radius = LevelBuilder.DefaultTeleporterRadius, Target = floor + new Vector3(0, 0, 8),
			}),
			HazardFields = h => new Field[]
			{
				new VectorField("Exit", () => h.Target ?? (Vector3)h.Pos + new Vector3(0, 0, 8), v => h.Target = v),
				new NoteField("The exit is a point on the floor; the ball comes out 1 m above it."),
				new NumberField("Radius", () => h.Radius ?? LevelBuilder.DefaultTeleporterRadius, v => h.Radius = v) { Min = 0.6f, Max = 10, Arrow = 0.1f },
				new ToggleField("Keep speed", () => h.KeepSpeed ?? false, v => h.KeepSpeed = v ? true : null),
			},
		},
		new()
		{
			Id = "checkpoint", Label = "Checkpoint", Category = Markers,
			Tooltip = "Rolling through it makes it the respawn point.",
			Add = (level, floor) =>
			{
				level.Checkpoints.Add(new Checkpoint { Pos = floor, Size = new Vec3(8, 3, 2) });
				return new EditorItem(ItemKind.Checkpoint, level.Checkpoints.Count - 1);
			},
		},
		new()
		{
			Id = "goal", Label = "Goal", Category = Markers,
			Tooltip = "The finish pad. A level needs at least one.",
			Add = (level, floor) =>
			{
				level.Goals.Add(new Goal { Pos = floor });
				return new EditorItem(ItemKind.Goal, level.Goals.Count - 1);
			},
		},
		new()
		{
			Id = "spawn", Label = "Spawn", Category = Markers,
			Tooltip = "Where the ball starts. A level has one, so this moves it.",
			Add = (level, floor) =>
			{
				level.Spawn = floor + Vector3.Up;
				return EditorItem.Spawn;
			},
		},
	};

	/// <summary>Looks that a part can be given in one click. Keys match the built-in levels' materials.</summary>
	public static readonly IReadOnlyList<(string Key, MaterialDef Look)> Materials = new List<(string, MaterialDef)>
	{
		("grid", new MaterialDef { Color = new(0.55f, 0.62f, 0.75f), Line = new Vec3(0.18f, 0.2f, 0.3f) }),
		("start", new MaterialDef { Color = new(0.45f, 0.7f, 0.9f), Line = new Vec3(0.15f, 0.25f, 0.35f) }),
		("bridge", new MaterialDef { Color = new(0.85f, 0.65f, 0.4f), Line = new Vec3(0.35f, 0.22f, 0.12f) }),
		("post", new MaterialDef { Color = new(0.9f, 0.3f, 0.2f), Line = new Vec3(0.4f, 0.1f, 0.08f) }),
		("rail", new MaterialDef { Color = new(0.7f, 0.45f, 0.85f), Line = new Vec3(0.3f, 0.15f, 0.4f) }),
		("moss", new MaterialDef { Color = new(0.45f, 0.78f, 0.45f), Line = new Vec3(0.15f, 0.32f, 0.15f) }),
		("dark", new MaterialDef { Color = new(0.32f, 0.3f, 0.45f), Line = new Vec3(0.12f, 0.1f, 0.2f) }),
		("slider", new MaterialDef { Color = new(0.95f, 0.55f, 0.15f), Roughness = 0.6f, Metallic = 0.1f }),
		("pusher", new MaterialDef { Color = new(0.85f, 0.2f, 0.45f), Roughness = 0.5f, Metallic = 0.1f }),
		("sweeper", new MaterialDef { Color = new(0.9f, 0.25f, 0.15f), Roughness = 0.4f, Metallic = 0.2f }),
		("steelie", new MaterialDef { Color = new(0.08f, 0.08f, 0.1f), Roughness = 0.25f, Metallic = 0.9f }),
	};

	private static IEnumerable<Field> HoleFields(Hazard h)
	{
		yield return new NumberField("Radius", () => h.Radius ?? LevelBuilder.DefaultHoleRadius, v => h.Radius = v) { Min = 0.6f, Max = 20, Arrow = 0.1f };
		yield return new NumberField("Depth", () => h.Depth ?? 3, v => h.Depth = v) { Min = 0.5f, Max = 50, Arrow = 0.5f };
		yield return new ToggleField("Timed", () => h.Period != null, v =>
		{
			h.Period = v ? 4 : null;
			h.Open = v ? 0.5f : null;
			if (!v)
				h.Phase = null;
		}) { Rebuilds = true };
		if (h.Period == null)
			yield break;
		yield return new NumberField("Period (s)", () => h.Period ?? 4, v => h.Period = v) { Min = 0.5f, Max = 120, Arrow = 0.5f };
		yield return new NumberField("Open (0-1)", () => h.Open ?? 0.5f, v => h.Open = v) { Min = 0, Max = 1, Arrow = 0.05f };
		yield return new NumberField("Phase (0-1)", () => h.Phase ?? 0, v => h.Phase = v) { Min = 0, Max = 1, Arrow = 0.05f };
	}

	public static CatalogEntry ForHazard(string type) => Entries.FirstOrDefault(e => e.HazardType == type);

	/// <summary>Makes sure the level defines <paramref name="key"/> if it's a preset it doesn't have yet.</summary>
	public static void EnsureMaterial(LevelFile level, string key)
	{
		if (string.IsNullOrEmpty(key) || level.Materials.ContainsKey(key))
			return;
		foreach (var (presetKey, look) in Materials)
		{
			if (presetKey == key)
			{
				level.Materials[key] = new MaterialDef { Color = look.Color, Line = look.Line, Roughness = look.Roughness, Metallic = look.Metallic };
				return;
			}
		}
	}

	/// <summary>Every material key a part of this level can pick: the level's own, then the presets it lacks.</summary>
	public static List<string> MaterialKeys(LevelFile level)
	{
		var keys = level.Materials.Keys.ToList();
		keys.AddRange(Materials.Select(m => m.Key).Where(k => !level.Materials.ContainsKey(k)));
		return keys;
	}

	public static MaterialDef Look(LevelFile level, string key)
	{
		if (key != null && level.Materials.TryGetValue(key, out var def))
			return def;
		foreach (var (presetKey, look) in Materials)
		{
			if (presetKey == key)
				return look;
		}
		return null;
	}

	private static EditorItem AddPart(LevelFile level, Part part)
	{
		EnsureMaterial(level, part.Material);
		level.Course.Add(part);
		var item = new EditorItem(ItemKind.Part, level.Course.Count - 1);
		StandOnFloor(level, item, part.Pos.Y);
		return item;
	}

	private static EditorItem AddHazard(LevelFile level, Hazard hazard)
	{
		foreach (var part in hazard.Parts ?? new List<Part>())
			EnsureMaterial(level, part.Material);
		level.Hazards.Add(hazard);
		var item = new EditorItem(ItemKind.Hazard, level.Hazards.Count - 1);
		StandOnFloor(level, item, hazard.Pos.Y);
		return item;
	}

	/// <summary>Raises a new object so its lowest point is on the floor it was placed on.</summary>
	private static void StandOnFloor(LevelFile level, EditorItem item, float floor)
	{
		float bottom = EditorItems.Bounds(level, item).MinY;
		EditorItems.SetPos(level, item, EditorItems.GetPos(level, item) + Vector3.Up * (floor - bottom));
	}
}
