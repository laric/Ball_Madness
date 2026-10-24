using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BallMadness.LevelFormat;
using BallMadness.LevelGen;
using Godot;

namespace BallMadness.Tools;

/// <summary>
/// Command line tools for making level packs, run headless from the project
/// folder (they write into res://packs/, which is the project's packs/ folder):
/// <code>
/// godot --headless --path . res://Scenes/Tools.tscn -- COMMAND ...
///
/// --generate-pack ID --count N [--seed S] [--from D] [--to D] [--name NAME] [--feature SECTION] [--theme THEME]
///     Writes a pack of N generated levels whose difficulty rises from D to D
///     (0 to 1 by default). Level i uses seed S*1000+i. With --feature, every
///     level has that section (a Section name such as Ice) in it. THEME is a
///     built-in world theme (res://themes/), e.g. lava.
/// --regenerate ID INDEX SEED
///     Replaces level INDEX (1-based) of the pack with one from another seed,
///     keeping its name and difficulty.
/// --rename-levels ID [SEED]
///     Gives every level of the pack a new name in the style of its world
///     (Meadow levels get names like "Clover Glide"); the courses don't change.
/// --set-time ID INDEX SECONDS
///     Sets the level's time limit from the autopilot's time on it.
/// --validate [ID ...]
///     Loads and builds every level of the given packs (all built-in packs by
///     default) in its pack's world theme without playing them, and fails if
///     any level, built-in theme (res://themes/) or pack theme doesn't load.
/// </code>
/// tools/build-pack.sh runs these with the autopilot to make a pack where
/// every level is known to be finishable.
/// </summary>
public partial class ToolRunner : Node
{
	public override void _Ready()
	{
		int code;
		try
		{
			code = Run(OS.GetCmdlineUserArgs());
		}
		catch (Exception e)
		{
			GD.PrintErr($"[tools] {e.Message}");
			code = 1;
		}
		GetTree().Quit(code);
	}

	private static int Run(string[] args)
	{
		if (args.Length == 0)
		{
			GD.PrintErr("[tools] No command; see Scripts/Tools/ToolRunner.cs");
			return 2;
		}
		switch (args[0])
		{
			case "--generate-pack":
				GeneratePack(args[1], Int(Option(args, "--count") ?? "10"), Int(Option(args, "--seed") ?? "1"),
					Float(Option(args, "--from") ?? "0"), Float(Option(args, "--to") ?? "1"), Option(args, "--name"),
					Option(args, "--feature") is string feature ? Enum.Parse<Section>(feature, true) : null, Option(args, "--theme"));
				return 0;
			case "--regenerate":
				Regenerate(args[1], Int(args[2]), Int(args[3]));
				return 0;
			case "--rename-levels":
				RenameLevels(args[1], args.Length > 2 ? Int(args[2]) : 1);
				return 0;
			case "--set-time":
				SetTime(args[1], Int(args[2]), Float(args[3]));
				return 0;
			case "--validate":
				return Validate(args.Skip(1).ToArray());
			default:
				GD.PrintErr($"[tools] Unknown command {args[0]}");
				return 2;
		}
	}

	private static void GeneratePack(string id, int count, int seed, float from, float to, string name, Section? feature, string theme)
	{
		if (!Packs.IsValidId(id))
			throw new InvalidOperationException($"Bad pack id \"{id}\"");
		if (theme != null && Themes.Find(theme) == null)
			throw new InvalidOperationException($"No built-in theme \"{theme}\" (have {string.Join(", ", Themes.BuiltIn.Select(t => t.Id))})");
		string folder = $"{Packs.BuiltInRoot}/{id}";
		DirAccess.MakeDirRecursiveAbsolute($"{folder}/levels");

		var names = new LevelNames(seed, theme);
		var files = new List<string>();
		for (int i = 0; i < count; i++)
		{
			float d = count == 1 ? from : Mathf.Lerp(from, to, (float)i / (count - 1));
			var level = LevelGenerator.Generate(seed * 1000 + i, d, names.Next(), feature);
			string file = $"{i + 1:000}.json";
			Write($"{folder}/levels/{file}", Packs.Serialize(level));
			files.Add(file);
			GD.Print($"[tools] {file}: {level.Name}, difficulty {d:0.00}, {level.Course.Count} parts, {level.Hazards.Count} hazards");
		}

		var pack = new PackFile { Id = id, Name = name ?? id, Author = "Ball Madness", Version = "1", Levels = files, Theme = Themes.WithBase(null, theme ?? Themes.ClassicId) };
		Write($"{folder}/pack.json", Packs.Serialize(pack));
	}

	/// <summary>Reads every built-in theme strictly, checks the packs' theme names, and returns how many failed.</summary>
	private static int CheckThemes(List<Pack> packs)
	{
		int failed = 0;
		using var dir = DirAccess.Open(Themes.Root);
		foreach (string file in dir?.GetFiles() ?? Array.Empty<string>())
		{
			if (!file.EndsWith(".json", StringComparison.Ordinal))
				continue;
			try
			{
				var theme = Themes.Parse(FileAccess.GetFileAsString($"{Themes.Root}/{file}"), file);
				GD.Print($"[tools] {file}: {theme.Name} ({theme.Id}), {theme.Materials?.Count ?? 0} materials");
			}
			catch (Exception e)
			{
				GD.PrintErr($"[tools] {e.Message}");
				failed++;
			}
		}
		foreach (var pack in packs)
		{
			string id = pack.Info.Theme?.Base;
			if (id != null && Themes.Find(id) == null)
			{
				GD.PrintErr($"[tools] pack {pack.Id} names theme \"{id}\", which doesn't exist");
				failed++;
			}
		}
		GD.Print($"[tools] {Themes.BuiltIn.Count} world themes load");
		return failed;
	}

	private static void Regenerate(string id, int index, int seed)
	{
		string path = LevelPath(id, index);
		var old = Packs.LoadLevel(path);
		var level = LevelGenerator.Generate(seed, old.Difficulty ?? 0, old.Name, LevelGenerator.FeatureOf(old));
		Write(path, Packs.Serialize(level));
		GD.Print($"[tools] {path}: regenerated from seed {seed}");
	}

	private static void RenameLevels(string id, int seed)
	{
		var pack = Packs.Find(id) ?? throw new InvalidOperationException($"No pack \"{id}\"");
		var names = new LevelNames(seed, pack.Info.Theme?.Base);
		foreach (string path in pack.LevelPaths)
		{
			var level = Packs.LoadLevel(path);
			level.Name = names.Next();
			Write(path, Packs.Serialize(level));
			GD.Print($"[tools] {path}: {level.Name}");
		}
	}

	private static void SetTime(string id, int index, float seconds)
	{
		string path = LevelPath(id, index);
		var level = Packs.LoadLevel(path);
		level.TimeLimit = LevelGenerator.TimeLimitFor(seconds, level.Difficulty ?? 0.5f);
		Write(path, Packs.Serialize(level));
		GD.Print($"[tools] {path}: time limit {level.TimeLimit}s (autopilot {seconds:0.0}s)");
	}

	private static int Validate(string[] ids)
	{
		var packs = ids.Length > 0
			? ids.Select(id => Packs.Find(id) ?? throw new InvalidOperationException($"No pack \"{id}\"")).ToList()
			: Packs.All.Where(p => p.BuiltIn).ToList();
		int failed = CheckThemes(packs), count = 0;
		foreach (var pack in packs)
		{
			var theme = pack.Theme;
			foreach (string path in pack.LevelPaths)
			{
				count++;
				try
				{
					var level = Packs.LoadLevel(path);
					if (level.Goals.Count == 0)
						throw new InvalidOperationException("no goal");
					LevelBuilder.Build(level, theme).Free();
				}
				catch (Exception e)
				{
					GD.PrintErr($"[tools] {path}: {e.Message}");
					failed++;
				}
			}
		}
		GD.Print($"[tools] {count - failed} of {count} levels in {packs.Count} pack(s) load");
		GD.Print($"[tools] campaign: {Campaign.Worlds.Count} world(s), {Campaign.LevelCount} level(s)");
		return failed == 0 ? 0 : 1;
	}

	private static string LevelPath(string id, int index)
	{
		var pack = Packs.Find(id) ?? throw new InvalidOperationException($"No pack \"{id}\"");
		if (index < 1 || index > pack.LevelPaths.Count)
			throw new InvalidOperationException($"Pack {id} has no level {index}");
		return pack.LevelPaths[index - 1];
	}

	private static void Write(string path, string text)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write)
			?? throw new InvalidOperationException($"Can't write {path}: {FileAccess.GetOpenError()}");
		file.StoreString(text);
	}

	private static string Option(string[] args, string name)
	{
		int i = Array.IndexOf(args, name);
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
	private static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>
/// Made-up level names like "Copper Cascade", never the same one twice in a
/// pack. A world theme gives the first word its flavour ("Clover Glide" in
/// the Meadow, "Magma Chute" in Lava).
/// </summary>
public class LevelNames
{
	private static readonly string[] First =
	{
		"Amber", "Basalt", "Cobalt", "Copper", "Crystal", "Dizzy", "Echo", "Ember", "Frost", "Glass",
		"Granite", "Hollow", "Iron", "Jade", "Lazy", "Lunar", "Marble", "Misty", "Neon", "Onyx",
		"Polar", "Quartz", "Rusty", "Silent", "Silver", "Slate", "Steel", "Sunny", "Tidal", "Velvet",
		"Violet", "Wobbly", "Zigzag", "Brass", "Cloud", "Dusky", "Gilded", "Hazel", "Indigo", "Paper",
	};

	/// <summary>First words for each built-in world, by theme id.</summary>
	private static readonly Dictionary<string, string[]> ThemeFirst = new()
	{
		["meadow"] = new[] { "Clover", "Daisy", "Buttercup", "Honey", "Bramble", "Willow", "Dandelion", "Bluebell", "Hedgerow", "Primrose", "Thistle", "Meadow" },
		["desert"] = new[] { "Dune", "Mirage", "Cactus", "Sandstone", "Sunbaked", "Oasis", "Saffron", "Dusty", "Scarab", "Sirocco", "Golden", "Sandy" },
		["forest"] = new[] { "Mossy", "Oak", "Fern", "Pine", "Acorn", "Toadstool", "Bark", "Owl", "Thicket", "Cedar", "Badger", "Woodland" },
		["ocean"] = new[] { "Coral", "Tidal", "Driftwood", "Lagoon", "Seashell", "Harbour", "Kelp", "Breaker", "Pebble", "Gull", "Anchor", "Salty" },
		["ice"] = new[] { "Frost", "Glacier", "Polar", "Snowy", "Icicle", "Blizzard", "Frozen", "Arctic", "Sleet", "Winter", "Tundra", "Avalanche" },
		["neon-city"] = new[] { "Neon", "Laser", "Pixel", "Chrome", "Electric", "Disco", "Arcade", "Midnight", "Turbo", "Synth", "Glow", "Static" },
		["clockwork"] = new[] { "Brass", "Cog", "Gear", "Copper", "Rusty", "Pendulum", "Spring", "Ticking", "Iron", "Piston", "Sprocket", "Steam" },
		["crystal-cave"] = new[] { "Crystal", "Amethyst", "Quartz", "Geode", "Glimmer", "Echo", "Sapphire", "Opal", "Prism", "Shimmer", "Gem", "Deep" },
		["lava"] = new[] { "Ember", "Magma", "Cinder", "Molten", "Inferno", "Obsidian", "Ash", "Blaze", "Smoulder", "Basalt", "Fire", "Lava" },
		["space"] = new[] { "Comet", "Lunar", "Nebula", "Orbit", "Rocket", "Stellar", "Cosmic", "Meteor", "Galaxy", "Quasar", "Solar", "Zero-G" },
	};

	private static readonly string[] Second =
	{
		"Alley", "Avenue", "Canyon", "Cascade", "Causeway", "Chute", "Circuit", "Climb", "Crossing", "Descent",
		"Drift", "Drop", "Falls", "Funnel", "Gallery", "Gauntlet", "Glide", "Gorge", "Highway", "Hollow",
		"Junction", "Ladder", "Maze", "Mesa", "Overpass", "Pass", "Plunge", "Promenade", "Ramble", "Ridge",
		"Run", "Slide", "Spiral", "Steps", "Switchback", "Terrace", "Tumble", "Viaduct", "Walk", "Way",
	};

	private readonly Random _rng;
	private readonly string[] _first;
	private readonly HashSet<string> _used = new();

	public LevelNames(int seed, string theme = null)
	{
		_rng = new Random(seed);
		_first = theme != null && ThemeFirst.TryGetValue(theme, out var words) ? words : First;
	}

	public string Next()
	{
		for (int tries = 0; tries < 1000; tries++)
		{
			string first = _first[_rng.Next(_first.Length)];
			string second = Second[_rng.Next(Second.Length)];
			string name = $"{first} {second}";
			if (first != second && _used.Add(name))
				return name;
		}
		return $"Level {_used.Count + 1}";
	}
}
