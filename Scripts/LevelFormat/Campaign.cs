using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace BallMadness.LevelFormat;

/// <summary>
/// <c>res://packs/campaign.json</c>: the built-in packs that make up the
/// campaign, one per world, in play order.
/// <code>
/// { "worlds": ["w01-meadow", "w02-desert", ...] }
/// </code>
/// </summary>
public class CampaignFile
{
	public List<string> Worlds { get; set; } = new();
}

/// <summary>
/// The campaign: worlds played in order, each a built-in pack in its own
/// theme. A world opens once every level of the world before it is
/// completed, and finishing a world's last level leads straight into the next.
/// </summary>
public static class Campaign
{
	public const string FilePath = Packs.BuiltInRoot + "/campaign.json";

	private static List<string> _ids;

	/// <summary>The campaign's worlds that are installed, in play order (empty if there's no campaign).</summary>
	public static IReadOnlyList<Pack> Worlds =>
		Ids.Select(Packs.Find).Where(p => p != null && p.BuiltIn && p.LevelPaths.Count > 0).ToList();

	public static bool Exists => Worlds.Count > 0;

	private static List<string> Ids => _ids ??= Load();

	/// <summary>The pack's place in the campaign (0 for world 1), or -1 if it isn't a campaign world.</summary>
	public static int IndexOf(Pack pack)
	{
		if (pack == null)
			return -1;
		var worlds = Worlds;
		for (int i = 0; i < worlds.Count; i++)
		{
			if (worlds[i].Id == pack.Id)
				return i;
		}
		return -1;
	}

	public static bool IsWorld(Pack pack) => IndexOf(pack) >= 0;

	/// <summary>The world after this one, or null for the last world or a pack outside the campaign.</summary>
	public static Pack NextWorld(Pack pack)
	{
		int index = IndexOf(pack);
		return index >= 0 && index + 1 < Worlds.Count ? Worlds[index + 1] : null;
	}

	public static bool IsComplete(Pack pack) =>
		SaveGame.FurthestCompleted(pack.Id) >= pack.LevelPaths.Count - 1;

	/// <summary>World 1 is always open; every later world once the one before it is complete.</summary>
	public static bool IsUnlocked(Pack pack)
	{
		int index = IndexOf(pack);
		return index <= 0 || IsComplete(Worlds[index - 1]);
	}

	/// <summary>The first world not yet completed (the last world once all are), or null without a campaign.</summary>
	public static Pack ContinueWorld()
	{
		var worlds = Worlds;
		return worlds.FirstOrDefault(w => !IsComplete(w)) ?? worlds.LastOrDefault();
	}

	/// <summary>"World 3" for a campaign world, or null.</summary>
	public static string WorldLabel(Pack pack)
	{
		int index = IndexOf(pack);
		return index >= 0 ? $"World {index + 1}" : null;
	}

	public static int LevelCount => Worlds.Sum(w => w.LevelPaths.Count);

	private static List<string> Load()
	{
		if (!FileAccess.FileExists(FilePath))
			return new List<string>();
		try
		{
			var file = JsonSerializer.Deserialize(FileAccess.GetFileAsString(FilePath), LevelJsonContext.Default.CampaignFile);
			return file?.Worlds ?? new List<string>();
		}
		catch (JsonException e)
		{
			GD.PushWarning($"Ignoring {FilePath}: {e.Message}");
			return new List<string>();
		}
	}
}
