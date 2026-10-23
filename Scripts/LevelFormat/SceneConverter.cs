using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BallMadness.LevelFormat;

/// <summary>
/// One-off tool: reads the hand-made levels/levelNN.tscn scenes and writes
/// them as level files in packs/main/. Run with
/// <c>godot --headless --path . -- --convert-levels</c>.
/// </summary>
public static class SceneConverter
{
	public static void ConvertAll()
	{
		var names = new List<string>();
		DirAccess.MakeDirRecursiveAbsolute("res://packs/main/levels");
		for (int n = 1; n <= 99; n++)
		{
			string path = $"res://levels/level{n:00}.tscn";
			if (!ResourceLoader.Exists(path))
				continue;
			var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>();
			LevelFile level = Convert(scene, path);
			scene.Free();

			string name = $"{n:00}-{Slug(level.Name)}.json";
			using (var file = FileAccess.Open($"res://packs/main/levels/{name}", FileAccess.ModeFlags.Write))
				file.StoreString(Packs.Serialize(level));
			names.Add(name);
			GD.Print($"[convert] {path} -> {name}: {level.Course.Count} parts, {level.Hazards.Count} hazards, " +
				$"{level.Checkpoints.Count} checkpoints, {level.Gates.Count} gates, {level.Route.Count} waypoints");
		}

		var pack = new PackFile
		{
			Id = Packs.MainPackId,
			Name = "Ball Madness",
			Author = "Ball Madness",
			Version = "1",
			Levels = names,
		};
		using (var file = FileAccess.Open("res://packs/main/pack.json", FileAccess.ModeFlags.Write))
			file.StoreString(Packs.Serialize(pack));
	}

	public static LevelFile Convert(Node3D root, string source)
	{
		var level = new LevelFile();
		if (root is LevelInfo info)
		{
			level.Name = info.DisplayName;
			level.TimeLimit = info.TimeLimit;
			level.KillHeight = info.KillHeight;
		}

		var materials = new Dictionary<Material, string>();
		string MaterialKey(Material material)
		{
			if (material == null)
				return null;
			if (materials.TryGetValue(material, out string key))
				return key;
			string path = material.ResourcePath ?? "";
			key = path.Contains("::") ? path[(path.IndexOf("::") + 2)..] : $"material{materials.Count + 1}";
			while (level.Materials.ContainsKey(key))
				key += "_";
			level.Materials[key] = Describe(material, source);
			materials[material] = key;
			return key;
		}

		void Walk(Node node)
		{
			foreach (Node child in node.GetChildren())
			{
				if (!Visit(child))
					Walk(child);
			}
		}

		// Returns true when the node (and everything under it) has been handled.
		bool Visit(Node node)
		{
			Transform3D global = GlobalOf(node, root);
			switch (node)
			{
				case Marker3D when node.Name == "Spawn" && node.GetParent() == root:
					level.Spawn = global.Origin;
					return true;

				case Marker3D when node.GetParent()?.Name == "Route":
					var waypoint = new Waypoint { Pos = global.Origin };
					if (node.HasMeta("speed"))
						waypoint.Speed = (float)node.GetMeta("speed");
					if (node.HasMeta("wait_area"))
					{
						waypoint.Gate = ((NodePath)node.GetMeta("wait_area")).GetName(((NodePath)node.GetMeta("wait_area")).GetNameCount() - 1);
						waypoint.WaitFor = (string)node.GetMeta("wait_for", "clear");
					}
					level.Route.Add(waypoint);
					return true;

				case CsgShape3D csg:
					if (csg.GetChildCount() > 0)
						throw Unsupported(source, node, "CSG with children");
					var part = PartOf(csg, global, MaterialKey, source);
					level.Course.Add(part);
					return true;

				case Area3D area when area.IsInGroup("checkpoint"):
					level.Checkpoints.Add(CheckpointOf(area, global, source));
					return true;

				case Area3D area when area.IsInGroup("goal"):
					level.Goals.Add(GoalOf(area, global, source));
					return true;

				case Area3D area when area.GetParent()?.Name == "Gates":
					level.Gates.Add(GateOf(area, global, source));
					return true;

				case AnimatableBody3D body when body is Mover mover:
					var moverHazard = HazardOf(body, "mover", global, MaterialKey, source);
					moverHazard.Offset = ParentBasis(body, root) * mover.Offset;
					moverHazard.Period = mover.Period;
					moverHazard.Phase = mover.PhaseOffset == 0 ? null : mover.PhaseOffset;
					level.Hazards.Add(moverHazard);
					return true;

				case AnimatableBody3D body when body is Spinner spinner:
					var spinnerHazard = HazardOf(body, "spinner", global, MaterialKey, source);
					spinnerHazard.Speed = spinner.DegreesPerSecond;
					if (spinner.Axis != Vector3.Up)
						spinnerHazard.Axis = spinner.Axis;
					level.Hazards.Add(spinnerHazard);
					return true;

				case AnimatableBody3D body when SpinAnimation(body) is float speed:
					var animated = HazardOf(body, "spinner", global, MaterialKey, source);
					animated.Name = body.GetParent().Name;
					animated.Speed = speed;
					level.Hazards.Add(animated);
					return true;

				case RigidBody3D body when body is Chaser chaser:
					level.Hazards.Add(ChaserOf(chaser, global, MaterialKey, source));
					return true;

				case AnimationPlayer:
					// Only the spin animations handled above are supported.
					if (node.GetParent().GetChildren().OfType<AnimatableBody3D>().All(b => SpinAnimation(b) == null))
						throw Unsupported(source, node, "an animation that isn't a spin");
					return true;

				case Node3D when node.GetType() == typeof(Node3D):
					return false; // a plain grouping node: look inside

				default:
					throw Unsupported(source, node, node.GetType().Name);
			}
		}

		Walk(root);
		return level;
	}

	private static Part PartOf(CsgShape3D csg, Transform3D global, Func<Material, string> materialKey, string source)
	{
		var part = new Part { Name = csg.Name, Pos = global.Origin, Rot = Euler(global.Basis) };
		if (!csg.UseCollision)
			part.Collide = false;
		if (!csg.Visible)
			part.Visible = false;
		switch (csg)
		{
			case CsgBox3D box:
				part.Shape = "box";
				part.Size = box.Size;
				part.Material = materialKey(box.Material);
				break;
			case CsgCylinder3D cylinder when !cylinder.Cone:
				part.Shape = "cylinder";
				part.Radius = cylinder.Radius;
				part.Height = cylinder.Height;
				part.Material = materialKey(cylinder.Material);
				break;
			case CsgSphere3D sphere:
				part.Shape = "sphere";
				part.Radius = sphere.Radius;
				part.Material = materialKey(sphere.Material);
				break;
			default:
				throw Unsupported(source, csg, csg.GetType().Name);
		}
		return part;
	}

	private static Hazard HazardOf(PhysicsBody3D body, string type, Transform3D global, Func<Material, string> materialKey, string source)
	{
		var hazard = new Hazard { Name = body.Name, Type = type, Pos = global.Origin, Rot = Euler(global.Basis), Parts = new List<Part>() };
		var meshes = body.GetChildren().OfType<MeshInstance3D>().ToList();
		var shapes = body.GetChildren().OfType<CollisionShape3D>().ToList();
		if (body.GetChildCount() != meshes.Count + shapes.Count)
			throw Unsupported(source, body, "a moving body with children other than meshes and collision shapes");

		foreach (var mesh in meshes)
		{
			var part = BodyPart(mesh.Name, mesh.Transform, mesh.Mesh, source, mesh);
			Material material = mesh.MaterialOverride ?? mesh.GetSurfaceOverrideMaterial(0) ?? mesh.Mesh.SurfaceGetMaterial(0);
			part.Material = materialKey(material);
			var twin = shapes.FirstOrDefault(s => s.Transform.IsEqualApprox(mesh.Transform) && SameSize(BodyPart(s.Name, s.Transform, s.Shape, source, s), part));
			if (twin != null)
				shapes.Remove(twin);
			else
				part.Collide = false;
			hazard.Parts.Add(part);
		}
		foreach (var shape in shapes)
		{
			var part = BodyPart(shape.Name, shape.Transform, shape.Shape, source, shape);
			part.Visible = false;
			hazard.Parts.Add(part);
		}
		foreach (var part in hazard.Parts)
			part.Name = part.Name.Replace("Mesh", "").Replace("Shape", "") is { Length: > 0 } trimmed ? trimmed : null;
		return hazard;
	}

	private static Part BodyPart(string name, Transform3D local, Resource resource, string source, Node node)
	{
		var part = new Part { Name = name, Pos = local.Origin, Rot = Euler(local.Basis) };
		switch (resource)
		{
			case BoxMesh box:
				part.Shape = "box";
				part.Size = box.Size;
				break;
			case BoxShape3D box:
				part.Shape = "box";
				part.Size = box.Size;
				break;
			case CylinderMesh cylinder when Mathf.IsEqualApprox(cylinder.TopRadius, cylinder.BottomRadius):
				part.Shape = "cylinder";
				part.Radius = cylinder.TopRadius;
				part.Height = cylinder.Height;
				break;
			case CylinderShape3D cylinder:
				part.Shape = "cylinder";
				part.Radius = cylinder.Radius;
				part.Height = cylinder.Height;
				break;
			case SphereMesh sphere:
				part.Shape = "sphere";
				part.Radius = sphere.Radius;
				break;
			case SphereShape3D sphere:
				part.Shape = "sphere";
				part.Radius = sphere.Radius;
				break;
			default:
				throw Unsupported(source, node, resource?.GetType().Name ?? "no mesh or shape");
		}
		return part;
	}

	private static bool SameSize(Part a, Part b) =>
		a.Shape == b.Shape &&
		((Vector3)(a.Size ?? default)).IsEqualApprox(b.Size ?? default) &&
		Mathf.IsEqualApprox(a.Radius ?? 0, b.Radius ?? 0) &&
		Mathf.IsEqualApprox(a.Height ?? 0, b.Height ?? 0);

	private static Hazard ChaserOf(Chaser chaser, Transform3D global, Func<Material, string> materialKey, string source)
	{
		var shape = chaser.GetChildren().OfType<CollisionShape3D>().Single().Shape as SphereShape3D
			?? throw Unsupported(source, chaser, "a chaser that isn't a sphere");
		var mesh = chaser.GetChildren().OfType<MeshInstance3D>().Single();
		var physics = chaser.PhysicsMaterialOverride;
		return new Hazard
		{
			Name = chaser.Name,
			Type = "chaser",
			Pos = global.Origin,
			Radius = shape.Radius,
			ChaseRadius = chaser.ChaseRadius,
			PushForce = chaser.PushForce,
			MaxSpeed = chaser.MaxSpeed,
			Mass = chaser.Mass,
			Friction = physics?.Friction ?? 1,
			Bounce = physics?.Bounce ?? 0,
			Material = materialKey(mesh.MaterialOverride ?? mesh.GetSurfaceOverrideMaterial(0) ?? mesh.Mesh.SurfaceGetMaterial(0)),
		};
	}

	private static Checkpoint CheckpointOf(Area3D area, Transform3D global, string source)
	{
		var shape = area.GetNode<CollisionShape3D>("Shape");
		var box = shape.Shape as BoxShape3D ?? throw Unsupported(source, area, "a checkpoint that isn't a box");
		if (!shape.Position.IsEqualApprox(new Vector3(0, box.Size.Y / 2, 0)))
			throw Unsupported(source, area, "a checkpoint box that doesn't stand on the floor");
		var stripe = area.GetNodeOrNull<CsgBox3D>("Stripe");
		if (stripe == null || !Mathf.IsEqualApprox(stripe.Size.X, box.Size.X) || !Mathf.IsEqualApprox(stripe.Size.Z, box.Size.Z))
			throw Unsupported(source, area, "a checkpoint stripe that doesn't match its box");
		return new Checkpoint { Pos = global.Origin, Rot = Euler(global.Basis), Size = box.Size };
	}

	private static Goal GoalOf(Area3D area, Transform3D global, string source)
	{
		var shape = area.GetNode<CollisionShape3D>("Shape").Shape as CylinderShape3D
			?? throw Unsupported(source, area, "a goal that isn't a cylinder");
		if (Euler(global.Basis) != null)
			throw Unsupported(source, area, "a rotated goal");
		return new Goal
		{
			Pos = global.Origin,
			Radius = Mathf.IsEqualApprox(shape.Radius, LevelBuilder.DefaultGoalRadius) ? null : shape.Radius,
		};
	}

	private static Gate GateOf(Area3D area, Transform3D global, string source)
	{
		var shape = area.GetChildren().OfType<CollisionShape3D>().Single();
		var box = shape.Shape as BoxShape3D ?? throw Unsupported(source, area, "a gate that isn't a box");
		Transform3D placed = global * shape.Transform;
		return new Gate { Name = area.Name, Pos = placed.Origin, Rot = Euler(placed.Basis), Size = box.Size };
	}

	/// <summary>Degrees a second of a looping "spin" animation on the body's rotation, if it has one.</summary>
	private static float? SpinAnimation(AnimatableBody3D body)
	{
		var player = body.GetParent()?.GetChildren().OfType<AnimationPlayer>().FirstOrDefault();
		if (player == null)
			return null;
		foreach (string name in player.GetAnimationList())
		{
			var animation = player.GetAnimation(name);
			if (animation.GetTrackCount() != 1 || animation.TrackGetPath(0) != new NodePath($"{body.Name}:rotation"))
				continue;
			if (animation.TrackGetKeyCount(0) != 2)
				continue;
			var from = (Vector3)animation.TrackGetKeyValue(0, 0);
			var to = (Vector3)animation.TrackGetKeyValue(0, 1);
			Vector3 turn = to - from;
			if (from != Vector3.Zero || turn.X != 0 || turn.Z != 0)
				continue;
			return Mathf.RadToDeg(turn.Y) / (float)animation.Length;
		}
		return null;
	}

	private static MaterialDef Describe(Material material, string source) => material switch
	{
		ShaderMaterial shader => new MaterialDef
		{
			Color = ToVec((Color)shader.GetShaderParameter("base_color")),
			Line = ToVec((Color)shader.GetShaderParameter("line_color")),
		},
		StandardMaterial3D standard => new MaterialDef
		{
			Color = ToVec(standard.AlbedoColor),
			Roughness = standard.Roughness,
			Metallic = standard.Metallic == 0 ? null : standard.Metallic,
		},
		_ => throw new InvalidOperationException($"{source}: unsupported material {material.GetType().Name}"),
	};

	private static Vec3 ToVec(Color c) => new(c.R, c.G, c.B);

	/// <summary>Euler angles in degrees, or null when there's no rotation.</summary>
	private static Vec3? Euler(Basis basis)
	{
		Vector3 radians = basis.Orthonormalized().GetEuler();
		Vector3 degrees = new(Mathf.RadToDeg(radians.X), Mathf.RadToDeg(radians.Y), Mathf.RadToDeg(radians.Z));
		return degrees.IsZeroApprox() ? null : degrees;
	}

	private static Transform3D GlobalOf(Node node, Node root)
	{
		Transform3D t = Transform3D.Identity;
		for (Node n = node; n != null && n != root; n = n.GetParent())
		{
			if (n is Node3D n3)
				t = n3.Transform * t;
		}
		return t;
	}

	private static Basis ParentBasis(Node node, Node root) =>
		node.GetParent() is Node3D parent ? GlobalOf(parent, root).Basis : Basis.Identity;

	private static InvalidOperationException Unsupported(string source, Node node, string what) =>
		new($"{source}: {node.Name} is {what}, which the level format doesn't support");

	private static string Slug(string name)
	{
		var chars = name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
		string slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
		return slug.Length > 0 ? slug : "level";
	}
}
