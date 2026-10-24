using System;
using System.Collections.Generic;
using Godot;

namespace BallMadness.LevelFormat;

/// <summary>
/// Meshes and collision shapes for the terrain part shapes, each filling the
/// part's size (width, height, depth) centred on its position:
/// <list type="bullet">
/// <item>"bump": a smooth ridge across the part's local X, rising and falling along Z.</item>
/// <item>"mound": a round hill with an oval footprint.</item>
/// <item>"rock": a lumpy boulder with a flat bottom. Its shape comes from its position, so a level always gets the same rocks.</item>
/// </list>
/// Bumps and mounds meet the floor at a tangent, so the ball rolls on and
/// off them without a jolt.
/// </summary>
public static class Terrain
{
	public static readonly string[] Shapes = { "bump", "mound", "rock" };

	public static bool IsTerrain(string shape) => Array.IndexOf(Shapes, shape) >= 0;

	public static readonly Vector3 DefaultSize = new(4, 0.6f, 3);

	/// <summary>The visible mesh and the matching collision shape for a terrain part.</summary>
	public static (Mesh Mesh, Shape3D Shape) Make(Part part)
	{
		Vector3 size = (Vector3)(part.Size ?? DefaultSize);
		size = size.Max(Vector3.One * 0.05f);
		switch (part.Shape)
		{
			case "bump":
				return Concave(Bump(size));
			case "mound":
				return Concave(Mound(size));
			case "rock":
				var rock = Rock(size, Seed(part.Pos));
				var points = new List<Vector3>();
				foreach (var (a, b, c) in rock)
				{
					points.Add(a);
					points.Add(b);
					points.Add(c);
				}
				return (ToMesh(rock, smooth: false), new ConvexPolygonShape3D { Points = points.ToArray() });
			default:
				throw new InvalidOperationException($"\"{part.Shape}\" is not a terrain shape");
		}
	}

	private static (Mesh, Shape3D) Concave(List<(Vector3, Vector3, Vector3)> tris)
	{
		var faces = new Vector3[tris.Count * 3];
		for (int i = 0; i < tris.Count; i++)
		{
			faces[i * 3] = tris[i].Item1;
			faces[i * 3 + 1] = tris[i].Item2;
			faces[i * 3 + 2] = tris[i].Item3;
		}
		return (ToMesh(tris, smooth: true), new ConcavePolygonShape3D { Data = faces });
	}

	/// <summary>
	/// Height of a cosine bump from 0 at <paramref name="s"/> = ±1 to 1 at
	/// 0, with a level tangent at both ends.
	/// </summary>
	private static float Profile(float s) => Mathf.Abs(s) >= 1 ? 0 : 0.5f + 0.5f * Mathf.Cos(Mathf.Pi * s);

	private static List<(Vector3, Vector3, Vector3)> Bump(Vector3 size)
	{
		const int steps = 24;
		float hw = size.X / 2, hh = size.Y / 2, hl = size.Z / 2;
		var tris = new List<(Vector3, Vector3, Vector3)>();
		Vector3 Top(float x, int k)
		{
			float s = -1 + 2f * k / steps;
			return new Vector3(x, -hh + size.Y * Profile(s), s * hl);
		}
		// A short skirt under the ends, out of sight inside the floor.
		const float skirt = 0.05f;
		for (int k = 0; k < steps; k++)
		{
			Quad(tris, Top(-hw, k), Top(hw, k), Top(hw, k + 1), Top(-hw, k + 1), Vector3.Up);
			foreach (float x in new[] { -hw, hw })
			{
				var bottom0 = new Vector3(x, -hh - skirt, Top(x, k).Z);
				var bottom1 = new Vector3(x, -hh - skirt, Top(x, k + 1).Z);
				Quad(tris, bottom0, bottom1, Top(x, k + 1), Top(x, k), new Vector3(Mathf.Sign(x), 0, 0));
			}
		}
		return tris;
	}

	private static List<(Vector3, Vector3, Vector3)> Mound(Vector3 size)
	{
		const int rings = 10, segments = 32;
		float hw = size.X / 2, hh = size.Y / 2, hl = size.Z / 2;
		var tris = new List<(Vector3, Vector3, Vector3)>();
		Vector3 At(int ring, int segment)
		{
			float s = (float)ring / rings;
			float a = Mathf.Tau * segment / segments;
			return new Vector3(s * hw * Mathf.Cos(a), -hh + size.Y * Profile(s), s * hl * Mathf.Sin(a));
		}
		var peak = new Vector3(0, hh, 0);
		for (int j = 0; j < segments; j++)
		{
			Tri(tris, peak, At(1, j), At(1, j + 1), Vector3.Up);
			for (int i = 1; i < rings; i++)
				Quad(tris, At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1), Vector3.Up);
		}
		return tris;
	}

	/// <summary>A boulder: a once-split icosahedron with its corners pushed in and out, and a flat bottom.</summary>
	private static List<(Vector3, Vector3, Vector3)> Rock(Vector3 size, int seed)
	{
		var rng = new Random(seed);
		var (vertices, faces) = Icosphere();
		for (int i = 0; i < vertices.Count; i++)
			vertices[i] *= 0.78f + 0.4f * (float)rng.NextDouble();
		for (int i = 0; i < vertices.Count; i++)
			vertices[i] = new Vector3(vertices[i].X, Mathf.Max(vertices[i].Y, -0.55f), vertices[i].Z);

		// Stretch to fill the size exactly.
		Vector3 min = Vector3.One * float.MaxValue, max = -min;
		foreach (var v in vertices)
		{
			min = min.Min(v);
			max = max.Max(v);
		}
		for (int i = 0; i < vertices.Count; i++)
			vertices[i] = (vertices[i] - (min + max) / 2) / (max - min) * size;

		var tris = new List<(Vector3, Vector3, Vector3)>();
		foreach (var (a, b, c) in faces)
		{
			var centre = (vertices[a] + vertices[b] + vertices[c]) / 3;
			Tri(tris, vertices[a], vertices[b], vertices[c], centre);
		}
		return tris;
	}

	private static (List<Vector3>, List<(int, int, int)>) Icosphere()
	{
		float t = (1 + Mathf.Sqrt(5)) / 2;
		var vertices = new List<Vector3>
		{
			new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
			new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
			new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
		};
		for (int i = 0; i < vertices.Count; i++)
			vertices[i] = vertices[i].Normalized();
		var faces = new List<(int, int, int)>
		{
			(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
			(1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
			(3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
			(4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
		};
		var middles = new Dictionary<(int, int), int>();
		int Middle(int a, int b)
		{
			var key = a < b ? (a, b) : (b, a);
			if (!middles.TryGetValue(key, out int m))
			{
				m = vertices.Count;
				vertices.Add(((vertices[a] + vertices[b]) / 2).Normalized());
				middles[key] = m;
			}
			return m;
		}
		var split = new List<(int, int, int)>();
		foreach (var (a, b, c) in faces)
		{
			int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
			split.Add((a, ab, ca));
			split.Add((b, bc, ab));
			split.Add((c, ca, bc));
			split.Add((ab, bc, ca));
		}
		return (vertices, split);
	}

	/// <summary>Adds a triangle facing <paramref name="outward"/> (Godot draws clockwise faces).</summary>
	private static void Tri(List<(Vector3, Vector3, Vector3)> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
	{
		if ((b - a).Cross(c - a).Dot(outward) > 0)
			(b, c) = (c, b);
		tris.Add((a, b, c));
	}

	private static void Quad(List<(Vector3, Vector3, Vector3)> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
	{
		Tri(tris, a, b, c, outward);
		Tri(tris, a, c, d, outward);
	}

	/// <summary>
	/// Builds the mesh. Smooth meshes share normals between faces that meet at
	/// the same point, except across a sharp edge (the sides of a bump).
	/// </summary>
	private static ArrayMesh ToMesh(List<(Vector3, Vector3, Vector3)> tris, bool smooth)
	{
		var faceNormals = new Vector3[tris.Count];
		for (int i = 0; i < tris.Count; i++)
		{
			var (a, b, c) = tris[i];
			faceNormals[i] = (c - a).Cross(b - a).Normalized();
		}

		var shared = new Dictionary<Vector3, List<int>>();
		if (smooth)
		{
			for (int i = 0; i < tris.Count; i++)
			{
				foreach (var v in new[] { tris[i].Item1, tris[i].Item2, tris[i].Item3 })
				{
					var key = Snap(v);
					if (!shared.TryGetValue(key, out var list))
						shared[key] = list = new List<int>();
					list.Add(i);
				}
			}
		}

		Vector3 NormalAt(Vector3 v, int face)
		{
			if (!smooth)
				return faceNormals[face];
			var sum = Vector3.Zero;
			foreach (int other in shared[Snap(v)])
			{
				if (faceNormals[other].Dot(faceNormals[face]) > 0.7f)
					sum += faceNormals[other];
			}
			return sum.Normalized();
		}

		var positions = new Vector3[tris.Count * 3];
		var normals = new Vector3[tris.Count * 3];
		for (int i = 0; i < tris.Count; i++)
		{
			var (a, b, c) = tris[i];
			positions[i * 3] = a;
			positions[i * 3 + 1] = b;
			positions[i * 3 + 2] = c;
			normals[i * 3] = NormalAt(a, i);
			normals[i * 3 + 1] = NormalAt(b, i);
			normals[i * 3 + 2] = NormalAt(c, i);
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = positions;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static Vector3 Snap(Vector3 v) => (v * 1000).Round();

	/// <summary>A fixed number from a position, so the same rock always comes out the same shape.</summary>
	private static int Seed(Vec3 pos)
	{
		unchecked
		{
			int x = Mathf.RoundToInt(pos.X * 100), y = Mathf.RoundToInt(pos.Y * 100), z = Mathf.RoundToInt(pos.Z * 100);
			return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
		}
	}
}
