using System;
using System.Collections.Generic;
using System.Linq;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness.LevelGen;

/// <summary>
/// The course sections the generator chains together. Each is introduced at
/// a difficulty (see <see cref="LevelGenerator.Unlocks"/>) and gets harder
/// as difficulty rises: narrower, steeper, faster.
/// </summary>
public enum Section
{
	Run,
	Ramp,
	Steps,
	Slalom,
	Zigzag,
	Planks,
	Sweeper,
	Beam,
	SlidingBridge,
	Pistons,
	Chasers,
}

/// <summary>
/// Builds a level from a seed and a difficulty between 0 (the gentlest
/// level) and 1 (the hardest). The same seed and difficulty always give the
/// same level. Every level gets an autopilot route, so the result can be
/// checked (and its time limit set) by running the autopilot over it.
/// </summary>
public static class LevelGenerator
{
	/// <summary>Bumped when a change makes the same seed give a different level.</summary>
	public const int Version = 1;

	/// <summary>Time limit written before the autopilot has measured the level.</summary>
	public const float UnmeasuredTimeLimit = 300;

	/// <summary>Difficulty at which each section first appears.</summary>
	public static readonly IReadOnlyDictionary<Section, float> Unlocks = new Dictionary<Section, float>
	{
		[Section.Run] = 0.0f,
		[Section.Ramp] = 0.0f,
		[Section.Steps] = 0.05f,
		[Section.Slalom] = 0.1f,
		[Section.Zigzag] = 0.2f,
		[Section.Planks] = 0.3f,
		[Section.Sweeper] = 0.4f,
		[Section.Beam] = 0.5f,
		[Section.SlidingBridge] = 0.6f,
		[Section.Pistons] = 0.7f,
		[Section.Chasers] = 0.8f,
	};

	private static readonly Dictionary<string, MaterialDef> Palette = new()
	{
		["start"] = new() { Color = new(0.45f, 0.7f, 0.9f), Line = new(0.15f, 0.25f, 0.35f) },
		["grid"] = new() { Color = new(0.55f, 0.62f, 0.75f), Line = new(0.18f, 0.2f, 0.3f) },
		["ramp"] = new() { Color = new(0.62f, 0.58f, 0.8f), Line = new(0.2f, 0.18f, 0.32f) },
		["bridge"] = new() { Color = new(0.85f, 0.65f, 0.4f), Line = new(0.35f, 0.22f, 0.12f) },
		["dark"] = new() { Color = new(0.32f, 0.3f, 0.45f), Line = new(0.12f, 0.1f, 0.2f) },
		["rail"] = new() { Color = new(0.4f, 0.42f, 0.55f), Line = new(0.2f, 0.2f, 0.3f) },
		["post"] = new() { Color = new(0.9f, 0.3f, 0.2f), Line = new(0.4f, 0.1f, 0.08f) },
		["moving"] = new() { Color = new(0.85f, 0.35f, 0.75f), Roughness = 0.6f },
		["hazard"] = new() { Color = new(0.95f, 0.45f, 0.15f), Roughness = 0.6f },
		["sweeper"] = new() { Color = new(0.9f, 0.25f, 0.15f), Roughness = 0.4f, Metallic = 0.2f },
		["hub"] = new() { Color = new(0.25f, 0.22f, 0.3f), Roughness = 0.4f, Metallic = 0.6f },
		["steelie"] = new() { Color = new(0.08f, 0.08f, 0.1f), Roughness = 0.25f, Metallic = 0.9f },
	};

	/// <summary>
	/// How much longer than the autopilot a player gets: lots of spare time
	/// on the first levels, little on the last.
	/// </summary>
	public static float TimeLimitFor(float autopilotSeconds, float difficulty)
	{
		float slack = Mathf.Lerp(2.6f, 1.3f, Mathf.Clamp(difficulty, 0, 1));
		return Mathf.Ceil(autopilotSeconds * slack / 5) * 5;
	}

	public static LevelFile Generate(int seed, float difficulty, string name = null)
	{
		// A layout whose sections overlap is thrown away and laid again from
		// the next random numbers; the result is still fixed by the seed.
		var rng = new Random(seed);
		for (int attempt = 0; ; attempt++)
		{
			var level = TryGenerate(rng, Mathf.Clamp(difficulty, 0, 1), out bool overlaps);
			if (!overlaps || attempt >= 50)
			{
				level.Name = name ?? $"Level {seed}";
				level.Generator = $"v{Version} seed {seed}";
				if (overlaps)
					GD.PushWarning($"Seed {seed}: couldn't find a layout without overlaps");
				return level;
			}
		}
	}

	private static LevelFile TryGenerate(Random rng, float d, out bool overlaps)
	{
		var level = new LevelFile
		{
			TimeLimit = UnmeasuredTimeLimit,
			Difficulty = d,
			Materials = new Dictionary<string, MaterialDef>(Palette),
		};
		var c = new CourseWriter(level, rng);

		// Start pad.
		c.BeginSection();
		c.Box("StartPad", 4, 0, -0.5f, new Vector3(8, 1, 8), 0, "start");
		level.Spawn = c.At(4, 0, 1.1f);
		c.Waypoint(4);
		c.Advance(8);
		c.EndSection();

		var available = Unlocks.Where(u => u.Value <= d + 0.001f).Select(u => u.Key).ToList();
		Section newest = available.OrderBy(s => Unlocks[s]).Last();
		int count = Mathf.RoundToInt(Mathf.Lerp(3, 7, d)) + c.Range(0, 1);

		var sections = new List<Section>();
		for (int i = 0; i < count; i++)
			sections.Add(Pick(c, available, newest, sections));
		// Make sure the newest section shows up at least once.
		if (!sections.Contains(newest) && newest != Section.Ramp)
			sections[c.Range(1, sections.Count - 1)] = newest;

		int sinceCheckpoint = 0;
		for (int i = 0; i < sections.Count; i++)
		{
			c.BeginSection();
			Build(c, sections[i], d);
			c.EndSection();
			sinceCheckpoint++;

			bool last = i == sections.Count - 1;
			if (last)
				break;

			// A rest pad between sections: sometimes a corner, sometimes a
			// checkpoint, always somewhere the ball can line up again.
			bool turn = c.Chance(0.45f);
			bool checkpoint = sinceCheckpoint >= 3 || (sinceCheckpoint >= 2 && c.Chance(0.5f));
			c.BeginSection();
			if (turn)
			{
				if (checkpoint)
				{
					c.Checkpoint(4, 8);
					sinceCheckpoint = 0;
				}
				c.Turn(8, "grid");
			}
			else if (checkpoint || IsHazard(sections[i]) || IsHazard(sections[i + 1]))
			{
				c.Box("Pad", 3, 0, -0.5f, new Vector3(7, 1, 6), 0, "grid");
				if (checkpoint)
				{
					c.Checkpoint(3, 7);
					sinceCheckpoint = 0;
				}
				c.Waypoint(3);
				c.Advance(6);
			}
			c.EndSection();
		}

		// Finish pad.
		c.BeginSection();
		c.Box("FinishPad", 4, 0, -0.5f, new Vector3(8, 1, 8), 0, "grid");
		level.Goals.Add(new Goal { Pos = c.At(4) });
		c.Waypoint(4);
		c.EndSection();

		level.KillHeight = Mathf.Floor(c.LowestY - 8);
		overlaps = c.HasOverlaps();
		return level;
	}

	private static bool IsHazard(Section s) =>
		s is Section.Planks or Section.Sweeper or Section.SlidingBridge or Section.Pistons or Section.Chasers;

	private static Section Pick(CourseWriter c, List<Section> available, Section newest, List<Section> sofar)
	{
		for (int tries = 0; tries < 20; tries++)
		{
			// Favour the newest section a little, so each level shows off what it introduces.
			Section s = c.Chance(0.25f) ? newest : available[c.Range(0, available.Count - 1)];
			// No hazard twice in a row, nothing twice in a row, and nothing
			// more than twice in a level.
			if (sofar.Count > 0 && (s == sofar[^1] || (IsHazard(s) && IsHazard(sofar[^1]))))
				continue;
			if (sofar.Count(x => x == s) >= 2)
				continue;
			return s;
		}
		return Section.Ramp;
	}

	/// <summary>Course width for difficulty <paramref name="d"/>: wide and railed at first, narrow later.</summary>
	private static float Width(CourseWriter c, float d) =>
		Mathf.Snapped(Mathf.Lerp(6.5f, 2.6f, d) + c.Range(-0.4f, 0.4f), 0.1f);

	private static void Build(CourseWriter c, Section section, float d)
	{
		switch (section)
		{
			case Section.Run: Run(c, d); break;
			case Section.Ramp: Ramp(c, d); break;
			case Section.Steps: Steps(c, d); break;
			case Section.Slalom: Slalom(c, d); break;
			case Section.Zigzag: Zigzag(c, d); break;
			case Section.Planks: Planks(c, d); break;
			case Section.Sweeper: Sweeper(c, d); break;
			case Section.Beam: Beam(c, d); break;
			case Section.SlidingBridge: SlidingBridge(c, d); break;
			case Section.Pistons: Pistons(c, d); break;
			case Section.Chasers: Chasers(c, d); break;
		}
	}

	private static void Run(CourseWriter c, float d)
	{
		float w = Width(c, d);
		float len = Mathf.Round(c.Range(6, 12));
		c.Slab("Run", 0, len, 0, w, "grid");
		if (d < 0.25f)
			c.Rails(0, len, 0, w, "rail");
		c.Waypoint(len);
		c.Advance(len);
	}

	private static void Ramp(CourseWriter c, float d)
	{
		float w = Width(c, d);
		float len = Mathf.Round(c.Range(8, 13));
		float slope = Mathf.DegToRad(Mathf.Lerp(12, 26, d) + c.Range(-2, 2));
		float drop = Mathf.Snapped(len * Mathf.Tan(slope), 0.5f);
		c.Slab("Ramp", 0, len, drop, w, "ramp");
		if (d < 0.25f)
			c.Rails(0, len, drop, w, "rail");
		c.Waypoint(len, 0, -drop, speed: w < 3 ? 5 : null);
		c.Advance(len, drop);
	}

	/// <summary>A staircase of 1 m drops.</summary>
	private static void Steps(CourseWriter c, float d)
	{
		float w = Width(c, d);
		int n = c.Range(3, 5);
		float tread = Mathf.Lerp(3.5f, 2.5f, d);
		for (int i = 0; i < n; i++)
		{
			c.Box("Step", i * tread + tread / 2, 0, -i - 0.5f, new Vector3(w, 1, tread), 0, "bridge");
			c.Waypoint(i * tread + tread / 2, 0, -i, speed: 4);
		}
		c.Advance(n * tread, n - 1);
	}

	/// <summary>A wide ramp with posts to weave between.</summary>
	private static void Slalom(CourseWriter c, float d)
	{
		const float w = 8;
		int posts = c.Range(3, 5);
		float gap = Mathf.Lerp(5, 3.5f, d);
		float len = posts * gap + 3;
		float slope = Mathf.Tan(Mathf.DegToRad(Mathf.Lerp(8, 14, d)));
		float drop = Mathf.Snapped(len * slope, 0.5f);
		c.Slab("Slalom", 0, len, drop, w, "ramp");
		int side = c.Sign();
		float offset = Mathf.Lerp(1.6f, 2.2f, d);
		for (int i = 0; i < posts; i++)
		{
			float u = 2 + i * gap + gap / 2;
			float floor = -u * drop / len;
			c.Post(u, side * offset, floor, 0.6f, 3.5f, "post");
			// Pass on the other side of each post.
			c.Waypoint(u, -side * 2.6f, floor, speed: 5);
			side = -side;
		}
		c.Waypoint(len, 0, -drop);
		c.Advance(len, drop);
	}

	/// <summary>Narrow beams that dog-leg sideways and back.</summary>
	private static void Zigzag(CourseWriter c, float d)
	{
		float beam = Mathf.Snapped(Mathf.Lerp(3, 1.6f, d), 0.1f);
		float speed = beam < 2 ? 2.5f : beam < 2.4f ? 3 : 4;
		int legs = c.Range(1, 2);
		float v = 0;
		float u = 0;
		int side = c.Sign();
		for (int i = 0; i < legs; i++)
		{
			float run = Mathf.Round(c.Range(5, 8));
			c.Box("Beam", u + run / 2, v, -0.5f, new Vector3(beam, 1, run), 0, "bridge");
			u += run;
			c.Box("Corner", u + beam / 2, v, -0.5f, new Vector3(beam, 1, beam), 0, "bridge");
			c.Waypoint(u + beam / 2, v, 0, speed);
			float across = Mathf.Round(c.Range(3, 6));
			float nv = v + side * across;
			c.Box("Beam", u + beam / 2, (v + nv) / 2, -0.5f, new Vector3(across - beam, 1, beam), 0, "bridge");
			c.Box("Corner", u + beam / 2, nv, -0.5f, new Vector3(beam, 1, beam), 0, "bridge");
			c.Waypoint(u + beam / 2, nv, 0, speed);
			u += beam;
			v = nv;
			side = -side;
		}
		// Back to the middle line for the next section.
		float back = Mathf.Abs(v);
		if (back > 0.01f)
		{
			c.Box("Beam", u + 2, v, -0.5f, new Vector3(beam, 1, 4), 0, "bridge");
			c.Box("Corner", u + 4 + beam / 2, v, -0.5f, new Vector3(beam, 1, beam), 0, "bridge");
			c.Waypoint(u + 4 + beam / 2, v, 0, speed);
			c.Box("Beam", u + 4 + beam / 2, v / 2, -0.5f, new Vector3(back - beam, 1, beam), 0, "bridge");
			c.Box("Corner", u + 4 + beam / 2, 0, -0.5f, new Vector3(beam, 1, beam), 0, "bridge");
			c.Waypoint(u + 4 + beam / 2, 0, 0, speed);
			u += 4 + beam;
		}
		c.Box("Beam", u + 1.5f, 0, -0.5f, new Vector3(beam, 1, 3), 0, "bridge");
		c.Waypoint(u + 3, 0, 0, speed);
		c.Advance(u + 3);
	}

	/// <summary>A gap crossed on planks that sway from side to side.</summary>
	private static void Planks(CourseWriter c, float d)
	{
		int n = c.Range(3, 5);
		const float plank = 3;
		float amp = Mathf.Lerp(2, 3, d);
		float period = Mathf.Lerp(3.6f, 2.4f, d);
		for (int i = 0; i < n; i++)
		{
			var h = c.AddHazard("Plank", "mover", i * plank + plank / 2, -amp / 2, -0.5f);
			h.Offset = c.Vector(0, amp);
			h.Period = Mathf.Snapped(period, 0.1f);
			h.Phase = Mathf.Snapped((float)i / n, 0.01f);
			h.Parts.Add(new Part { Shape = "box", Pos = default, Size = new Vector3(4, 1, plank), Material = "moving" });
		}
		c.Waypoint(n * plank + 1, 0, 0, speed: 4);
		c.Box("Landing", n * plank + 2, 0, -0.5f, new Vector3(6, 1, 4), 0, "grid");
		c.Waypoint(n * plank + 3);
		c.Advance(n * plank + 4);
	}

	/// <summary>A square plaza with a sweeper bar turning on it; the path runs along one side.</summary>
	private static void Sweeper(CourseWriter c, float d)
	{
		const float size = 12, radius = 5.5f, line = 4.8f;
		int side = c.Sign();
		float centreV = -side * line;
		c.Box("Plaza", size / 2, centreV, -0.5f, new Vector3(size, 1, size), 0, "dark");
		var sweeper = c.AddHazard("Sweeper", "spinner", size / 2, centreV, 0.45f);
		sweeper.Speed = Mathf.Snapped(Mathf.Lerp(28, 45, d), 1) * c.Sign();
		sweeper.Parts.Add(new Part { Shape = "box", Pos = default, Size = new Vector3(radius * 2, 0.8f, 0.6f), Material = "sweeper" });
		sweeper.Parts.Add(new Part { Shape = "cylinder", Pos = default, Radius = 0.6f, Height = 0.9f, Material = "hub" });

		// Wait outside the bar's reach until it has just swept past the path, then run.
		float halfChord = Mathf.Sqrt(radius * radius - line * line);
		c.Waypoint(0.5f, 0, 0);
		string gate = c.Gate(size / 2, 0, 0.45f, new Vector3(1.2f, 0.8f, halfChord * 2 + 0.6f));
		c.Waypoint(size - 0.5f, 0, 0, gate: gate);
		c.Advance(size);
	}

	/// <summary>A long thin beam.</summary>
	private static void Beam(CourseWriter c, float d)
	{
		float w = Mathf.Snapped(Mathf.Lerp(2.2f, 1.3f, d), 0.1f);
		float len = Mathf.Round(c.Range(8, 14));
		float drop = c.Chance(0.5f) ? Mathf.Snapped(len * Mathf.Tan(Mathf.DegToRad(c.Range(6, 12))), 0.5f) : 0;
		c.Slab("Beam", 0, len, drop, w, "bridge");
		c.Waypoint(len * 0.5f, 0, -drop / 2, speed: 3);
		c.Waypoint(len, 0, -drop, speed: 3);
		c.Advance(len, drop);
	}

	/// <summary>A gap that a bridge slides into and out of.</summary>
	private static void SlidingBridge(CourseWriter c, float d)
	{
		float gap = Mathf.Snapped(Mathf.Lerp(3.5f, 4.5f, d), 0.5f);
		const float travel = 7;
		int side = c.Sign();
		float period = Mathf.Snapped(Mathf.Lerp(6, 4.2f, d), 0.1f);
		c.Box("Approach", 1.5f, 0, -0.5f, new Vector3(4, 1, 3), 0, "grid");
		var bridge = c.AddHazard("Slider", "mover", 3 + gap / 2, side * travel, -0.5f);
		bridge.Offset = c.Vector(0, -side * travel);
		bridge.Period = period;
		bridge.Phase = Mathf.Snapped(c.Range(0, 1), 0.01f);
		bridge.Parts.Add(new Part { Shape = "box", Pos = default, Size = new Vector3(3.5f, 1, gap + 0.4f), Material = "hazard" });

		c.Waypoint(1.5f);
		string gate = c.Gate(3 + gap / 2, 0, 0, new Vector3(0.6f, 1, 0.6f));
		c.Waypoint(3 + gap + 1.5f, gate: gate, waitFor: "occupied");
		c.Box("Landing", 3 + gap + 1.5f, 0, -0.5f, new Vector3(4, 1, 3), 0, "grid");
		c.Advance(3 + gap + 3);
	}

	/// <summary>A walkway with pistons punching across it from alternating sides.</summary>
	private static void Pistons(CourseWriter c, float d)
	{
		int n = c.Range(2, 4);
		const float spacing = 6, w = 3;
		float len = n * spacing + 4;
		float period = Mathf.Snapped(Mathf.Lerp(4.5f, 3.2f, d), 0.1f);
		c.Slab("Alley", 0, len, 0, w, "bridge");
		c.Waypoint(1.5f);
		int side = c.Sign();
		for (int i = 0; i < n; i++)
		{
			float u = 4 + i * spacing;
			c.Box("Housing", u, side * 8.5f, 0.4f, new Vector3(5, 2.4f, 2.8f), 0, "dark");
			var piston = c.AddHazard("Piston", "mover", u, side * 6, 0.62f);
			piston.Offset = c.Vector(0, -side * 6);
			piston.Period = period;
			piston.Phase = Mathf.Snapped((float)i / n, 0.01f);
			piston.Parts.Add(new Part { Shape = "box", Pos = default, Size = new Vector3(4, 1.2f, 2), Material = "hazard" });
			string gate = c.Gate(u, 0, 0.9f, new Vector3(w, 1, 2.2f));
			c.Waypoint(u + 3.5f, gate: gate);
			side = -side;
		}
		c.Waypoint(len);
		c.Advance(len);
	}

	/// <summary>An arena with enemy marbles that try to knock the ball off.</summary>
	private static void Chasers(CourseWriter c, float d)
	{
		const float size = 12;
		c.Box("Arena", size / 2, 0, -0.5f, new Vector3(size, 1, size), 0, "dark");
		int n = d > 0.9f ? 2 : 1;
		for (int i = 0; i < n; i++)
		{
			var chaser = c.AddHazard("Steelie", "chaser", 4 + i * 4, (i % 2 == 0 ? 1 : -1) * 4, 0.55f);
			chaser.Rot = null;
			chaser.Radius = 0.5f;
			chaser.ChaseRadius = 8;
			chaser.PushForce = Mathf.Snapped(Mathf.Lerp(8, 12, d), 0.5f);
			chaser.MaxSpeed = Mathf.Snapped(Mathf.Lerp(4.5f, 5.5f, d), 0.1f);
			chaser.Material = "steelie";
			chaser.Parts = null;
		}
		c.Waypoint(size / 2);
		c.Waypoint(size);
		c.Advance(size);
	}
}
