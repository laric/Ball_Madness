using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace BallMadness.LevelFormat;

/// <summary>
/// A pack's <c>pack.json</c>. A pack is a folder holding this file and a
/// <c>levels/</c> folder of level files:
/// <code>
/// my-pack/
///   pack.json
///   levels/
///     01-warm-up.json
///     02-the-drop.json
/// </code>
/// </summary>
public class PackFile
{
	public int Format { get; set; } = LevelFile.CurrentFormat;

	/// <summary>Unique, lower case letters, digits and dashes. Also the folder name for user packs.</summary>
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";
	public string Author { get; set; }
	public string Version { get; set; }
	public string Description { get; set; }

	/// <summary>
	/// Level file names inside <c>levels/</c>, in play order. Left out, every
	/// <c>levels/*.json</c> is played in file name order.
	/// </summary>
	public List<string> Levels { get; set; }
}

/// <summary>A pack found on disk, ready to play.</summary>
public class Pack
{
	public PackFile Info { get; init; }

	/// <summary>The pack's folder, e.g. res://packs/main or user://packs/my-pack.</summary>
	public string Folder { get; init; }

	/// <summary>True for the packs that ship with the game.</summary>
	public bool BuiltIn { get; init; }

	/// <summary>Full paths of the level files, in play order.</summary>
	public IReadOnlyList<string> LevelPaths { get; init; }

	public string Id => Info.Id;
	public string Name => Info.Name;
}

/// <summary>
/// Finds packs (built in at res://packs/, made or imported by players at
/// user://packs/), reads level files, and imports and exports packs as zips.
/// </summary>
public static class Packs
{
	public const string BuiltInRoot = "res://packs";
	public const string UserRoot = "user://packs";
	public const string MainPackId = "main";

	/// <summary>Biggest level file accepted from a zip, in bytes.</summary>
	private const long MaxLevelBytes = 4 * 1024 * 1024;
	private const int MaxLevelsPerPack = 500;

	private static List<Pack> _all;

	/// <summary>Every pack that loaded, built-in ones first.</summary>
	public static IReadOnlyList<Pack> All => _all ??= Discover();

	public static Pack Find(string id) => All.FirstOrDefault(p => p.Id == id);

	/// <summary>Forgets the pack list so the next call to <see cref="All"/> looks again.</summary>
	public static void Refresh() => _all = null;

	public static LevelFile LoadLevel(string path)
	{
		string text = FileAccess.GetFileAsString(path);
		if (string.IsNullOrEmpty(text))
			throw new InvalidOperationException($"Can't read {path}: {FileAccess.GetOpenError()}");
		return ParseLevel(text, path);
	}

	public static LevelFile ParseLevel(string json, string source)
	{
		LevelFile level;
		try
		{
			level = JsonSerializer.Deserialize(json, LevelJsonContext.Default.LevelFile);
		}
		catch (JsonException e)
		{
			throw new InvalidOperationException($"{source} is not a valid level: {e.Message}", e);
		}
		if (level == null)
			throw new InvalidOperationException($"{source} is empty");
		if (level.Format > LevelFile.CurrentFormat)
			throw new InvalidOperationException($"{source} needs a newer version of the game (format {level.Format})");
		return level;
	}

	public static string Serialize(LevelFile level) =>
		JsonSerializer.Serialize(level, LevelJsonContext.Default.LevelFile) + "\n";

	public static string Serialize(PackFile pack) =>
		JsonSerializer.Serialize(pack, LevelJsonContext.Default.PackFile) + "\n";

	public static PackFile ParsePack(string json, string source)
	{
		PackFile pack;
		try
		{
			pack = JsonSerializer.Deserialize(json, LevelJsonContext.Default.PackFile);
		}
		catch (JsonException e)
		{
			throw new InvalidOperationException($"{source} is not a valid pack.json: {e.Message}", e);
		}
		if (pack == null)
			throw new InvalidOperationException($"{source} is empty");
		if (!IsValidId(pack.Id))
			throw new InvalidOperationException($"{source}: id \"{pack.Id}\" must be lower case letters, digits and dashes");
		if (pack.Format > LevelFile.CurrentFormat)
			throw new InvalidOperationException($"{source} needs a newer version of the game (format {pack.Format})");
		if (string.IsNullOrWhiteSpace(pack.Name))
			pack.Name = pack.Id;
		return pack;
	}

	public static bool IsValidId(string id) =>
		!string.IsNullOrEmpty(id) && id.Length <= 64 &&
		id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') &&
		id[0] != '-';

	/// <summary>
	/// A level file name as it may appear in a pack: a plain <c>.json</c> name
	/// with no folders, so nothing outside the pack's <c>levels/</c> can be named.
	/// </summary>
	public static bool IsValidLevelFileName(string name) =>
		!string.IsNullOrEmpty(name) && name.Length <= 128 &&
		name.EndsWith(".json", StringComparison.Ordinal) &&
		!name.StartsWith('.') &&
		name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ' ');

	/// <summary>
	/// Copies a pack zip into user://packs/&lt;id&gt;/, replacing an older copy
	/// of the same user pack. Only pack.json and levels/*.json are taken, and
	/// every file is checked before anything is written. Returns the pack.
	/// </summary>
	public static Pack ImportZip(string zipPath)
	{
		var zip = new ZipReader();
		if (zip.Open(zipPath) != Error.Ok)
			throw new InvalidOperationException($"Can't open {zipPath}");

		try
		{
			string[] files = zip.GetFiles();

			// pack.json may sit at the top of the zip or inside one folder.
			string packEntry = files
				.Where(f => f == "pack.json" || (f.Count(c => c == '/') == 1 && f.EndsWith("/pack.json", StringComparison.Ordinal)))
				.OrderBy(f => f.Length)
				.FirstOrDefault()
				?? throw new InvalidOperationException("The zip has no pack.json");
			string prefix = packEntry[..^"pack.json".Length];

			PackFile info = ParsePack(zip.ReadFile(packEntry).GetStringFromUtf8(), "pack.json");
			if (BuiltInIds().Contains(info.Id))
				throw new InvalidOperationException($"\"{info.Id}\" is the id of a built-in pack; pick another one");

			var levels = new Dictionary<string, string>();
			foreach (string file in files)
			{
				if (!file.StartsWith(prefix + "levels/", StringComparison.Ordinal))
					continue;
				string name = file[(prefix.Length + "levels/".Length)..];
				if (name.Length == 0 || name.EndsWith('/'))
					continue;
				if (!IsValidLevelFileName(name))
					throw new InvalidOperationException($"Unexpected file in levels/: {name}");
				byte[] bytes = zip.ReadFile(file);
				if (bytes.LongLength > MaxLevelBytes)
					throw new InvalidOperationException($"{name} is too big");
				string json = bytes.GetStringFromUtf8();
				ParseLevel(json, name); // throws if it isn't a level
				levels[name] = json;
			}
			if (levels.Count == 0)
				throw new InvalidOperationException("The pack has no levels");
			if (levels.Count > MaxLevelsPerPack)
				throw new InvalidOperationException($"The pack has more than {MaxLevelsPerPack} levels");
			if (info.Levels != null)
			{
				foreach (string name in info.Levels)
				{
					if (!levels.ContainsKey(name))
						throw new InvalidOperationException($"pack.json lists {name}, which isn't in levels/");
				}
			}

			string folder = $"{UserRoot}/{info.Id}";
			DeleteFolder(folder);
			DirAccess.MakeDirRecursiveAbsolute($"{folder}/levels");
			WriteText($"{folder}/pack.json", Serialize(info));
			foreach (var (name, json) in levels)
				WriteText($"{folder}/levels/{name}", json);

			Refresh();
			return Find(info.Id) ?? throw new InvalidOperationException($"Imported {info.Id} but it didn't load");
		}
		finally
		{
			zip.Close();
		}
	}

	/// <summary>Writes a pack to a zip that <see cref="ImportZip"/> can read.</summary>
	public static void ExportZip(Pack pack, string zipPath)
	{
		var zip = new ZipPacker();
		if (zip.Open(zipPath) != Error.Ok)
			throw new InvalidOperationException($"Can't write {zipPath}");
		try
		{
			var info = pack.Info;
			AddFile(zip, $"{info.Id}/pack.json", Serialize(info));
			foreach (string path in pack.LevelPaths)
				AddFile(zip, $"{info.Id}/levels/{path.GetFile()}", FileAccess.GetFileAsString(path));
		}
		finally
		{
			zip.Close();
		}
	}

	private static List<Pack> Discover()
	{
		var packs = new List<Pack>();
		var ids = new HashSet<string>();
		foreach (var (root, builtIn) in new[] { (BuiltInRoot, true), (UserRoot, false) })
		{
			foreach (string folder in SubFolders(root))
			{
				try
				{
					var pack = LoadPack($"{root}/{folder}", builtIn);
					if (!ids.Add(pack.Id))
					{
						GD.PushWarning($"Skipping {root}/{folder}: another pack already has the id \"{pack.Id}\"");
						continue;
					}
					packs.Add(pack);
				}
				catch (Exception e)
				{
					GD.PushWarning($"Skipping pack {root}/{folder}: {e.Message}");
				}
			}
		}
		if (packs.Count == 0)
			GD.PushError($"No level packs found in {BuiltInRoot}/");
		return packs;
	}

	private static Pack LoadPack(string folder, bool builtIn)
	{
		string text = FileAccess.GetFileAsString($"{folder}/pack.json");
		if (string.IsNullOrEmpty(text))
			throw new InvalidOperationException("no pack.json");
		PackFile info = ParsePack(text, $"{folder}/pack.json");

		List<string> names = info.Levels ?? Files($"{folder}/levels")
			.Where(IsValidLevelFileName)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();
		var paths = new List<string>();
		foreach (string name in names)
		{
			if (!IsValidLevelFileName(name))
				throw new InvalidOperationException($"bad level file name \"{name}\"");
			string path = $"{folder}/levels/{name}";
			if (!FileAccess.FileExists(path))
				throw new InvalidOperationException($"missing levels/{name}");
			paths.Add(path);
		}
		if (paths.Count == 0)
			throw new InvalidOperationException("no levels");

		return new Pack { Info = info, Folder = folder, BuiltIn = builtIn, LevelPaths = paths };
	}

	private static HashSet<string> BuiltInIds() =>
		All.Where(p => p.BuiltIn).Select(p => p.Id).ToHashSet();

	private static IEnumerable<string> SubFolders(string path)
	{
		using var dir = DirAccess.Open(path);
		return dir == null ? Array.Empty<string>() : dir.GetDirectories().OrderBy(n => n, StringComparer.Ordinal).ToArray();
	}

	private static IEnumerable<string> Files(string path)
	{
		using var dir = DirAccess.Open(path);
		return dir == null ? Array.Empty<string>() : dir.GetFiles();
	}

	private static void WriteText(string path, string text)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write)
			?? throw new InvalidOperationException($"Can't write {path}: {FileAccess.GetOpenError()}");
		file.StoreString(text);
	}

	private static void DeleteFolder(string path)
	{
		using var dir = DirAccess.Open(path);
		if (dir == null)
			return;
		foreach (string sub in dir.GetDirectories())
			DeleteFolder($"{path}/{sub}");
		foreach (string file in dir.GetFiles())
			dir.Remove(file);
		DirAccess.RemoveAbsolute(path);
	}

	private static void AddFile(ZipPacker zip, string name, string text)
	{
		zip.StartFile(name);
		zip.WriteFile(text.ToUtf8Buffer());
		zip.CloseFile();
	}
}
