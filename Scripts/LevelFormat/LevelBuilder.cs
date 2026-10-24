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
///   Hazards      Mover / Spinner / Chaser bodies and the mechanics:
///                Conveyor, Ice, Bumper, Hole (with a TrapDoor), OneWayGate, Teleporter
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
	private static readonly Shader ConveyorShader = GD.Load<Shader>(Conveyor.ShaderPath);

	private static readonly MaterialDef CheckpointLook = new() { Color = new(0.95f, 0.85f, 0.2f), Line = new(0.5f, 0.4f, 0.05f) };
	private static readonly MaterialDef GoalLook = new() { Color = new(0.3f, 0.9f, 0.4f), Line = new(0.05f, 0.4f, 0.1f) };
	private static readonly MaterialDef PoleLook = new() { Color = new(0.9f, 0.3f, 0.2f), Line = new(0.4f, 0.1f, 0.08f) };
	private static readonly MaterialDef DefaultLook = new();
	private static readonly MaterialDef ChaserLook = new() { Color = new(0.08f, 0.08f, 0.1f), Roughness = 0.25f, Metallic = 0.8f };
	private static readonly MaterialDef IceLook = new() { Color = new(0.78f, 0.93f, 1.0f), Line = new(0.55f, 0.75f, 0.92f) };
	private static readonly MaterialDef BumperLook = new() { Color = new(0.2f, 0.85f, 0.95f), Roughness = 0.3f, Metallic = 0.3f };
	private static readonly MaterialDef HoleLook = new() { Color = new(0.05f, 0.05f, 0.08f) };
	private static readonly MaterialDef PostLook = new() { Color = new(0.3f, 0.3f, 0.38f), Roughness = 0.5f, Metallic = 0.4f };

	public const float DefaultHoleRadius = 1.2f;
	public const float DefaultTeleporterRadius = 1.0f;
	public const float DefaultGateWidth = 4.0f;
	public const float DefaultGateHeight = 1.2f;

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
		var built = new List<(Part Part, CsgPrimitive3D Node)>();
		foreach (var part in file.Course)
		{
			var node = MakeStaticPart(part, MaterialFor);
			built.Add((part, node));
			Add(course, node, part.Name);
		}
		foreach (var hazard in file.Hazards)
		{
			if (hazard.Type == "hole")
				CutHole(hazard, built);
		}

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
	private static CsgPrimitive3D MakeStaticPart(Part part, Func<string, Material> materialFor)
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
			case "conveyor":
				var belt = new ShaderMaterial { Shader = ConveyorShader };
				var conveyor = new Conveyor { Speed = hazard.Speed ?? 4.0f, Belt = belt, Transform = Place(hazard.Pos, hazard.Rot) };
				foreach (var part in hazard.Parts ?? new List<Part>())
					AddBodyPart(conveyor, part, belt);
				return conveyor;
			case "ice":
				body = new Ice();
				break;
			case "bumper":
				body = new Bumper { Kick = hazard.Force ?? 10.0f };
				break;
			case "hole":
				return MakeHole(hazard);
			case "oneway":
				return MakeOneWayGate(hazard);
			case "teleporter":
				return MakeTeleporter(hazard);
			default:
				throw new InvalidOperationException($"Unknown hazard type \"{hazard.Type}\"");
		}

		body.Transform = Place(hazard.Pos, hazard.Rot);
		var fallback = hazard.Type switch { "ice" => MakeMaterial(IceLook), "bumper" => MakeMaterial(BumperLook), _ => null };
		foreach (var part in hazard.Parts ?? new List<Part>())
			AddBodyPart(body, part, part.Material == null && fallback != null ? fallback : materialFor(part.Material));
		return body;
	}

	/// <summary>Cuts a hazard "hole" out of every course part it overlaps.</summary>
	private static void CutHole(Hazard hole, List<(Part Part, CsgPrimitive3D Node)> parts)
	{
		float radius = hole.Radius ?? DefaultHoleRadius;
		float depth = hole.Depth ?? 3.0f;
		var holePlace = Place(hole.Pos, hole.Rot);
		var walls = MakeMaterial(HoleLook);
		foreach (var (part, node) in parts)
		{
			if (part.Collide == false || part.Shape == "sphere")
				continue;
			var partPlace = Place(part.Pos, part.Rot);
			Vector3 local = partPlace.AffineInverse() * hole.Pos;
			Vector3 half = part.Shape == "cylinder"
				? new Vector3(part.Radius ?? 0.5f, (part.Height ?? 1) / 2, part.Radius ?? 0.5f)
				: (Vector3)(part.Size ?? Vector3.One) / 2;
			if (Mathf.Abs(local.X) > half.X + radius || Mathf.Abs(local.Z) > half.Z + radius
				|| local.Y < -half.Y - 0.01f || local.Y - depth > half.Y)
				continue;
			// From just above the floor down through the part.
			var cutter = new CsgCylinder3D
			{
				Name = "Hole",
				Operation = CsgShape3D.OperationEnum.Subtraction,
				Radius = radius,
				Height = depth + 0.5f,
				Sides = 24,
				Material = walls,
				Transform = partPlace.AffineInverse() * holePlace * new Transform3D(Basis.Identity, new Vector3(0, 0.25f - depth / 2, 0)),
			};
			node.AddChild(cutter, forceReadableName: true);
		}
	}

	/// <summary>The hole itself is cut into the course; this adds the trapdoor lid of a timed hole.</summary>
	private static Node3D MakeHole(Hazard hazard)
	{
		var place = Place(hazard.Pos, hazard.Rot);
		if (hazard.Period is not float period)
			return new Marker3D { Transform = place };

		float radius = (hazard.Radius ?? DefaultHoleRadius) - 0.03f;
		var look = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.6f, 0.15f), Roughness = 0.5f, Metallic = 0.3f };
		var door = new TrapDoor
		{
			Period = Mathf.Max(period, 0.2f),
			PhaseOffset = hazard.Phase ?? 0.0f,
			OpenFraction = Mathf.Clamp(hazard.Open ?? 0.5f, 0, 1),
			LidLook = look,
			Transform = place,
		};
		door.Shape = new CollisionShape3D { Name = "Shape", Position = new Vector3(0, -0.1f, 0), Shape = new CylinderShape3D { Radius = radius, Height = 0.2f } };
		door.Lid = new MeshInstance3D
		{
			Name = "Lid",
			Position = new Vector3(0, -0.1f, 0),
			Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.2f, RadialSegments = 24 },
			MaterialOverride = look,
		};
		door.AddChild(door.Shape);
		door.AddChild(door.Lid);
		return door;
	}

	private static Node3D MakeOneWayGate(Hazard hazard)
	{
		float width = hazard.Width ?? DefaultGateWidth, height = hazard.Height ?? DefaultGateHeight;
		var gate = new OneWayGate { Width = width, Height = height, Transform = Place(hazard.Pos, hazard.Rot) };
		var panelSize = new Vector3(width, height, gate.Thickness);
		gate.AddChild(new CollisionShape3D { Name = "Shape", Position = new Vector3(0, height / 2, 0), Shape = new BoxShape3D { Size = panelSize } });

		var panelLook = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.4f, 0.95f, 0.6f, 0.55f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.3f,
		};
		gate.Hinge = new Node3D { Name = "Hinge", Position = new Vector3(0, height, 0) };
		gate.Hinge.AddChild(new MeshInstance3D { Name = "Panel", Position = new Vector3(0, -height / 2, 0), Mesh = new BoxMesh { Size = panelSize }, MaterialOverride = panelLook });
		gate.AddChild(gate.Hinge);

		// Chevrons on the floor point the way through.
		var arrowLook = MakeMaterial(new MaterialDef { Color = new(0.4f, 0.95f, 0.6f) });
		for (int i = -1; i <= 1; i += 2)
		{
			foreach (float side in new[] { -1f, 1f })
			{
				var arm = new MeshInstance3D
				{
					Name = "Arrow",
					Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.02f, 0.9f) },
					MaterialOverride = arrowLook,
					Transform = new Transform3D(Basis.FromEuler(new Vector3(0, side * Mathf.DegToRad(40), 0)), new Vector3(side * 0.29f, 0.01f, i * 1.0f - 0.35f)),
				};
				gate.AddChild(arm, forceReadableName: true);
			}
		}

		// Posts at the sides: always solid, so they're their own body.
		var posts = new StaticBody3D { Name = "Posts" };
		var postLook = MakeMaterial(PostLook);
		foreach (float side in new[] { -1f, 1f })
		{
			var pos = new Vector3(side * (width / 2 + 0.15f), (height + 0.3f) / 2, 0);
			posts.AddChild(new CollisionShape3D { Name = "Shape", Position = pos, Shape = new BoxShape3D { Size = new Vector3(0.3f, height + 0.3f, 0.3f) } }, forceReadableName: true);
			posts.AddChild(new MeshInstance3D { Name = "Post", Position = pos, Mesh = new BoxMesh { Size = new Vector3(0.3f, height + 0.3f, 0.3f) }, MaterialOverride = postLook }, forceReadableName: true);
		}
		posts.AddChild(new MeshInstance3D
		{
			Name = "Lintel",
			Position = new Vector3(0, height + 0.22f, 0),
			Mesh = new BoxMesh { Size = new Vector3(width + 0.6f, 0.16f, 0.3f) },
			MaterialOverride = postLook,
		});
		gate.AddChild(posts);
		return gate;
	}

	private static Node3D MakeTeleporter(Hazard hazard)
	{
		float radius = hazard.Radius ?? DefaultTeleporterRadius;
		var glow = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.7f, 0.35f, 1.0f),
			EmissionEnabled = true,
			Emission = new Color(0.6f, 0.25f, 1.0f),
			EmissionEnergyMultiplier = 1.5f,
		};
		var place = Place(hazard.Pos, hazard.Rot);
		Vector3 target = hazard.Target is Vec3 t ? t : (Vector3)hazard.Pos + new Vector3(0, 0, 8);
		var teleporter = new Teleporter { Target = target, KeepSpeed = hazard.KeepSpeed ?? false, Transform = place };
		// The ball has to roll well onto the pad.
		teleporter.AddChild(new CollisionShape3D
		{
			Name = "Shape",
			Position = new Vector3(0, 0.75f, 0),
			Shape = new CylinderShape3D { Radius = Mathf.Max(radius - 0.4f, 0.1f), Height = 1.5f },
		});
		teleporter.AddChild(new MeshInstance3D
		{
			Name = "Pad",
			Position = new Vector3(0, 0.02f, 0),
			Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.04f, RadialSegments = 32 },
			MaterialOverride = glow,
		});
		teleporter.Ring = new MeshInstance3D
		{
			Name = "Ring",
			Position = new Vector3(0, 0.9f, 0),
			Mesh = new TorusMesh { InnerRadius = radius - 0.12f, OuterRadius = radius, Rings = 32, RingSegments = 6 },
			MaterialOverride = glow,
		};
		teleporter.AddChild(teleporter.Ring);

		// A faint ring where the ball comes out, in level space.
		var exit = new MeshInstance3D
		{
			Name = "Exit",
			Mesh = new TorusMesh { InnerRadius = 0.75f, OuterRadius = 0.85f, Rings = 32, RingSegments = 6 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.7f, 0.35f, 1.0f, 0.6f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			TopLevel = true,
			Position = target + Vector3.Up * 0.05f,
		};
		teleporter.AddChild(exit);
		return teleporter;
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
