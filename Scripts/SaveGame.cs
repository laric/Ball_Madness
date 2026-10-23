using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace BallMadness;

/// <summary>
/// The player's progress, kept in user://save.json. Progress is stored per
/// level pack (only "main" for now) and per level index: the furthest level
/// completed, and the best time and fewest falls on each level.
///
/// Runs with <c>--autopilot</c> don't touch the save file unless one is given
/// with <c>-- --save PATH</c>, so smoke tests can't unlock levels.
/// </summary>
public static class SaveGame
{
	public const string MainPack = "main";
	public const string DefaultPath = "user://save.json";

	public class LevelRecord
	{
		[JsonPropertyName("best_time")] public float BestTime { get; set; }
		[JsonPropertyName("fewest_falls")] public int FewestFalls { get; set; }
	}

	public class PackProgress
	{
		/// <summary>Index of the furthest level completed, or -1 for none.</summary>
		[JsonPropertyName("furthest_completed")] public int FurthestCompleted { get; set; } = -1;

		/// <summary>Records keyed by level index (as a string, for JSON).</summary>
		[JsonPropertyName("levels")] public Dictionary<string, LevelRecord> Levels { get; set; } = new();
	}

	private class SaveFile
	{
		[JsonPropertyName("version")] public int Version { get; set; } = 1;
		[JsonPropertyName("packs")] public Dictionary<string, PackProgress> Packs { get; set; } = new();
	}

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private static SaveFile _data;
	private static string _path;
	private static bool _enabled;

	/// <summary>The pack being played. Only the main pack exists for now.</summary>
	public static string CurrentPack => MainPack;

	private static SaveFile Data
	{
		get
		{
			if (_data == null)
				Load();
			return _data;
		}
	}

	/// <summary>Reads the save file (called on first use). A missing or broken file means no progress.</summary>
	public static void Load()
	{
		string[] args = OS.GetCmdlineUserArgs();
		_path = DefaultPath;
		_enabled = System.Array.IndexOf(args, "--autopilot") < 0;
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--save" && i + 1 < args.Length)
			{
				_path = args[i + 1];
				_enabled = true;
			}
		}

		_data = new SaveFile();
		if (!_enabled || !FileAccess.FileExists(_path))
			return;

		try
		{
			string json = FileAccess.GetFileAsString(_path);
			_data = JsonSerializer.Deserialize<SaveFile>(json, JsonOptions) ?? new SaveFile();
			_data.Packs ??= new Dictionary<string, PackProgress>();
		}
		catch (JsonException e)
		{
			GD.PushWarning($"Ignoring unreadable save file {_path}: {e.Message}");
			_data = new SaveFile();
		}
	}

	public static void Save()
	{
		if (!_enabled)
			return;

		using var file = FileAccess.Open(_path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PushError($"Couldn't write save file {_path}: {FileAccess.GetOpenError()}");
			return;
		}
		file.StoreString(JsonSerializer.Serialize(Data, JsonOptions));
	}

	public static PackProgress Progress(string pack)
	{
		if (!Data.Packs.TryGetValue(pack, out var progress))
		{
			progress = new PackProgress();
			Data.Packs[pack] = progress;
		}
		return progress;
	}

	public static int FurthestCompleted(string pack) => Progress(pack).FurthestCompleted;

	/// <summary>The level's best time and fewest falls, or null if it was never completed.</summary>
	public static LevelRecord Record(string pack, int index) =>
		Progress(pack).Levels.GetValueOrDefault(index.ToString());

	/// <summary>A level can be played once every level before it is completed (or it was completed itself).</summary>
	public static bool IsUnlocked(string pack, int index) =>
		index <= FurthestCompleted(pack) + 1 || Record(pack, index) != null;

	/// <summary>Banks a completed level and writes the save file.</summary>
	public static void RecordCompletion(string pack, int index, float time, int falls)
	{
		var progress = Progress(pack);
		progress.FurthestCompleted = Mathf.Max(progress.FurthestCompleted, index);

		string key = index.ToString();
		if (progress.Levels.TryGetValue(key, out var record))
		{
			record.BestTime = Mathf.Min(record.BestTime, time);
			record.FewestFalls = Mathf.Min(record.FewestFalls, falls);
		}
		else
		{
			progress.Levels[key] = new LevelRecord { BestTime = time, FewestFalls = falls };
		}
		Save();
	}
}
