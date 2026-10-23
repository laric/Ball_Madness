using System;
using System.Collections.Generic;
using Godot;

namespace BallMadness.LevelFormat;

/// <summary>
/// Turns a <see cref="LevelFile"/> into the node tree the game plays:
/// <code>
/// Level (LevelInfo)
///   Spawn        Marker3D where the ball starts
///   Course       static parts (CSG with collision)
///   Hazards      Mover / Spinner / Chaser bodies
///   Checkpoints  Area3Ds in the "checkpoint" group, with a yellow stripe
///   Goal, Goal2  Area3Ds in the "goal" group, with pad, pole and flag
///   Gates        Area3Ds the autopilot watches
///   Route        hidden Marker3D waypoints for the autopilot
/// </code>
/// Every type name in the file maps to one of the game's own scripts, so
/// building a level never runs code from the file.
/// </summary>
public static class LevelBuilder
{
	private static readonly Shader GridShader = GD.Load<Shader>("res://Shaders/grid.gdshader");

	private static readonly MaterialDef CheckpointLook = new() { Color = new(0.95f, 0.85f, 0.2f), Line = new(0.5f, 0.4f, 0.05f) };
	private static readonly MaterialDef GoalLook = new() { Color = new(0.3f, 0.9f, 0.4f), Line = new(0.05f, 0.4f, 0.1f) };
	private static readonly MaterialDef PoleLook = new() { Color = new(0.9f, 0.3f, 0.2f), Line = new(0.4f, 0.1f, 0.08f) };
	private static readonly MaterialDef DefaultLook = new();
	private static readonly MaterialDef ChaserLook = new() { Color = new(0.08f, 0.08f, 0.1f), Roughness = 0.25f, Metallic = 0.8f };

	public const float DefaultGoalRadius = 2.0f;

	public static LevelInfo Build(LevelFile file)
	{
		var materials = new Dictionary<string, Material>();
		Material MaterialFor(string key)
		{
			key ??= "";
			if (!materials.TryGetValue(key, out var material))
			{
				if (!file.Materials.TryGetValue(key, out var def))
				{
					if (key != "")
						GD.PushWarning($"Level \"{file.Name}\" has no material \"{key}\"");
					def = DefaultLook;
				}
				material = MakeMaterial(def);
				materials[key] = material;
			}
			return material;
		}

		var root = new LevelInfo
		{
			Name = "Level",
			DisplayName = file.Name ?? "",
			TimeLimit = file.TimeLimit,
			KillHeight = file.KillHeight,
		};

		root.AddChild(new Marker3D { Name = "Spawn", Position = file.Spawn });

		var course = AddGroup(root, "Course");
		foreach (var part in file.Course)
			Add(course, MakeStaticPart(part, MaterialFor), part.Name);

		var hazards = AddGroup(root, "Hazards");
		foreach (var hazard in file.Hazards)
			Add(hazards, MakeHazard(hazard, MaterialFor), hazard.Name ?? Capitalize(hazard.Type));

		var checkpoints = AddGroup(root, "Checkpoints");
		var stripe = MakeMaterial(CheckpointLook);
		foreach (var checkpoint in file.Checkpoints)
			Add(checkpoints, MakeCheckpoint(checkpoint, stripe), "Checkpoint");

		var goalLook = MakeMaterial(GoalLook);
		var poleLook = MakeMaterial(PoleLook);
		foreach (var goal in file.Goals)
			Add(root, MakeGoal(goal, goalLook, poleLook), "Goal");

		var gates = AddGroup(root, "Gates");
		foreach (var gate in file.Gates)
			Add(gates, MakeGate(gate), gate.Name ?? "Gate");

		var route = AddGroup(root, "Route");
		route.Visible = false;
		foreach (var waypoint in file.Route)
		{
			var marker = new Marker3D { Position = waypoint.Pos };
			if (waypoint.Speed is float speed)
				marker.SetMeta("speed", speed);
			if (!string.IsNullOrEmpty(waypoint.Gate))
			{
				marker.SetMeta("wait_area", new NodePath($"../../Gates/{waypoint.Gate}"));
				marker.SetMeta("wait_for", waypoint.WaitFor ?? "clear");
			}
			Add(route, marker, "Waypoint");
		}

		return root;
	}

	public static Material MakeMaterial(MaterialDef def)
	{
		if (def.Line is Vec3 line)
		{
			var material = new ShaderMaterial { Shader = GridShader };
			material.SetShaderParameter("base_color", ToColor(def.Color));
			material.SetShaderParameter("line_color", ToColor(line));
			return material;
		}
		return new StandardMaterial3D
		{
			AlbedoColor = ToColor(def.Color),
			Roughness = def.Roughness ?? 1.0f,
			Metallic = def.Metallic ?? 0.0f,
		};
	}

	/// <summary>A course part: CSG, so it looks and collides like the hand-made levels did.</summary>
	private static Node3D MakeStaticPart(Part part, Func<string, Material> materialFor)
	{
		CsgPrimitive3D node = part.Shape switch
		{
			"box" => new CsgBox3D { Size = part.Size ?? Vector3.One },
			"cylinder" => new CsgCylinder3D { Radius = part.Radius ?? 0.5f, Height = part.Height ?? 1.0f, Sides = 16 },
			"sphere" => new CsgSphere3D { Radius = part.Radius ?? 0.5f, RadialSegments = 24, Rings = 12 },
			_ => throw new InvalidOperationException($"Unknown part shape \"{part.Shape}\""),
		};
		node.UseCollision = part.Collide ?? true;
		node.Visible = part.Visible ?? true;
		if (node is CsgBox3D box)
			box.Material = materialFor(part.Material);
		else if (node is CsgCylinder3D cylinder)
			cylinder.Material = materialFor(part.Material);
		else if (node is CsgSphere3D sphere)
			sphere.Material = materialFor(part.Material);
		node.Transform = Place(part.Pos, part.Rot);
		return node;
	}

	private static Node3D MakeHazard(Hazard hazard, Func<string, Material> materialFor)
	{
		PhysicsBody3D body;
		switch (hazard.Type)
		{
			case "mover":
				body = new Mover
				{
					Offset = hazard.Offset ?? new Vector3(0, 0, 4),
					Period = hazard.Period ?? 4.0f,
					PhaseOffset = hazard.Phase ?? 0.0f,
				};
				break;
			case "spinner":
				body = new Spinner
				{
					DegreesPerSecond = hazard.Speed ?? 90.0f,
					Axis = hazard.Axis ?? Vector3.Up,
				};
				break;
			case "chaser":
				return MakeChaser(hazard, materialFor);
			default:
				throw new InvalidOperationException($"Unknown hazard type \"{hazard.Type}\"");
		}

		body.Transform = Place(hazard.Pos, hazard.Rot);
		foreach (var part in hazard.Parts ?? new List<Part>())
			AddBodyPart(body, part, materialFor(part.Material));
		return body;
	}

	private static Node3D MakeChaser(Hazard hazard, Func<string, Material> materialFor)
	{
		float radius = hazard.Radius ?? 0.5f;
		var chaser = new Chaser
		{
			ChaseRadius = hazard.ChaseRadius ?? 10.0f,
			PushForce = hazard.PushForce ?? 6.0f,
			MaxSpeed = hazard.MaxSpeed ?? 5.0f,
			Mass = hazard.Mass ?? 1.6f,
			PhysicsMaterialOverride = new PhysicsMaterial { Friction = hazard.Friction ?? 0.9f, Bounce = hazard.Bounce ?? 0.3f },
			CanSleep = false,
			ContinuousCd = true,
			Transform = Place(hazard.Pos, hazard.Rot),
		};
		chaser.AddChild(new CollisionShape3D { Name = "Shape", Shape = new SphereShape3D { Radius = radius } });
		chaser.AddChild(new MeshInstance3D
		{
			Name = "Mesh",
			Mesh = new SphereMesh { Radius = radius, Height = radius * 2 },
			MaterialOverride = hazard.Material != null ? materialFor(hazard.Material) : MakeMaterial(ChaserLook),
		});
		return chaser;
	}

	/// <summary>Adds a part to a moving body as a mesh plus a matching collision shape.</summary>
	private static void AddBodyPart(Node3D body, Part part, Material material)
	{
		Mesh mesh;
		Shape3D shape;
		switch (part.Shape)
		{
			case "box":
				var size = part.Size ?? Vector3.One;
				mesh = new BoxMesh { Size = size };
				shape = new BoxShape3D { Size = size };
				break;
			case "cylinder":
				float radius = part.Radius ?? 0.5f, height = part.Height ?? 1.0f;
				mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height };
				shape = new CylinderShape3D { Radius = radius, Height = height };
				break;
			case "sphere":
				float r = part.Radius ?? 0.5f;
				mesh = new SphereMesh { Radius = r, Height = r * 2 };
				shape = new SphereShape3D { Radius = r };
				break;
			default:
				throw new InvalidOperationException($"Unknown part shape \"{part.Shape}\"");
		}

		var transform = Place(part.Pos, part.Rot);
		string name = part.Name ?? Capitalize(part.Shape);
		if (part.Visible ?? true)
			body.AddChild(new MeshInstance3D { Name = name + "Mesh", Mesh = mesh, MaterialOverride = material, Transform = transform });
		if (part.Collide ?? true)
			body.AddChild(new CollisionShape3D { Name = name + "Shape", Shape = shape, Transform = transform });
	}

	private static Area3D MakeCheckpoint(Checkpoint checkpoint, Material stripe)
	{
		Vector3 size = checkpoint.Size;
		var area = new Area3D { Transform = Place(checkpoint.Pos, checkpoint.Rot) };
		area.AddToGroup("checkpoint", true);
		area.AddChild(new CollisionShape3D
		{
			Name = "Shape",
			Position = new Vector3(0, size.Y / 2, 0),
			Shape = new BoxShape3D { Size = size },
		});
		area.AddChild(new CsgBox3D
		{
			Name = "Stripe",
			Position = new Vector3(0, 0.01f, 0),
			Size = new Vector3(size.X, 0.02f, size.Z),
			Material = stripe,
		});
		return area;
	}

	private static Area3D MakeGoal(Goal goal, Material pad, Material pole)
	{
		float radius = goal.Radius ?? DefaultGoalRadius;
		var area = new Area3D { Position = goal.Pos };
		area.AddToGroup("goal", true);
		area.AddChild(new CollisionShape3D
		{
			Name = "Shape",
			Position = new Vector3(0, 1, 0),
			Shape = new CylinderShape3D { Radius = radius, Height = 2 },
		});
		area.AddChild(new CsgCylinder3D { Name = "Pad", Position = new Vector3(0, 0.02f, 0), Radius = radius, Height = 0.04f, Sides = 32, Material = pad });
		area.AddChild(new CsgCylinder3D { Name = "Pole", Position = new Vector3(0, 2, 0), Radius = 0.08f, Height = 4, Material = pole });
		area.AddChild(new CsgBox3D { Name = "Flag", Position = new Vector3(0.6f, 3.5f, 0), Size = new Vector3(1.2f, 0.8f, 0.05f), Material = pad });
		return area;
	}

	private static Area3D MakeGate(Gate gate)
	{
		var area = new Area3D { Transform = Place(gate.Pos, gate.Rot), Monitorable = false };
		area.AddChild(new CollisionShape3D { Name = "Shape", Shape = new BoxShape3D { Size = gate.Size } });
		return area;
	}

	public static Transform3D Place(Vec3 pos, Vec3? rotDegrees)
	{
		var basis = rotDegrees is Vec3 rot
			? Basis.FromEuler(new Vector3(Mathf.DegToRad(rot.X), Mathf.DegToRad(rot.Y), Mathf.DegToRad(rot.Z)))
			: Basis.Identity;
		return new Transform3D(basis, pos);
	}

	private static Node3D AddGroup(Node parent, string name)
	{
		var group = new Node3D { Name = name };
		parent.AddChild(group);
		return group;
	}

	/// <summary>Adds a child under its own name, or a numbered one if it has none or it's taken.</summary>
	private static void Add(Node parent, Node child, string name)
	{
		child.Name = string.IsNullOrEmpty(name) ? "Node" : name;
		parent.AddChild(child, forceReadableName: true);
	}

	private static Color ToColor(Vec3 v) => new(v.X, v.Y, v.Z);

	private static string Capitalize(string s) =>
		string.IsNullOrEmpty(s) ? "Part" : char.ToUpperInvariant(s[0]) + s[1..];
}
