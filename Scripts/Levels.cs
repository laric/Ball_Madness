using System.Collections.Generic;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness;

/// <summary>
/// The pack being played, which of its levels is current, and the progress
/// of the current run.
///
/// Levels come from packs (see <see cref="Packs"/>): the built-in campaign is
/// res://packs/main/, and players' packs live in user://packs/. The run state
/// is static so it survives reloading the main scene between levels.
/// </summary>
public static class Levels
{
	private static Pack _pack;

	/// <summary>The pack being played; the main campaign unless another was picked.</summary>
	public static Pack CurrentPack => _pack ??= Packs.Find(Packs.MainPackId) ?? Packs.All[0];

	public static int Count => CurrentPack.LevelPaths.Count;

	/// <summary>Index into the pack's levels of the level being played.</summary>
	public static int CurrentIndex { get; private set; }

	/// <summary>The level's number as players see it (1 for the first level).</summary>
	public static int CurrentNumber => CurrentIndex + 1;

	public static bool IsLastLevel => CurrentIndex >= Count - 1;

	/// <summary>Seconds spent on the levels finished so far in this run.</summary>
	public static float RunTime { get; private set; }
	public static int RunFalls { get; private set; }

	private static bool _argsApplied;

	public static string CurrentPath => CurrentPack.LevelPaths[CurrentIndex];

	/// <summary>Reads the current level's file and builds its nodes.</summary>
	public static LevelInfo BuildCurrent() => LevelBuilder.Build(Packs.LoadLevel(CurrentPath));

	/// <summary>Display name of a level in the current pack, read from its file.</summary>
	public static string NameAt(int index)
	{
		try
		{
			return Packs.LoadLevel(CurrentPack.LevelPaths[index]).Name;
		}
		catch (System.Exception e)
		{
			GD.PushWarning(e.Message);
			return "";
		}
	}

	/// <summary>
	/// Picks the pack and starting level from <c>-- --pack ID</c> and
	/// <c>-- --level N</c> the first time it's called; later calls (after a
	/// scene reload) do nothing.
	/// </summary>
	public static void ApplyCommandLine()
	{
		if (_argsApplied)
			return;
		_argsApplied = true;

		string pack = Arg("--pack");
		if (pack != null && !SelectPack(pack))
			GD.PushWarning($"No pack \"{pack}\", playing {CurrentPack.Id}");

		string level = Arg("--level");
		if (level != null && int.TryParse(level, out int number))
		{
			if (number >= 1 && number <= Count)
				CurrentIndex = number - 1;
			else
				GD.PushWarning($"No level {number}, starting at level {CurrentNumber}");
		}
	}

	/// <summary>Switches to another pack and starts a new run on its first level.</summary>
	public static bool SelectPack(string id)
	{
		var pack = Packs.Find(id);
		if (pack == null)
			return false;
		_pack = pack;
		StartNewRun();
		return true;
	}

	/// <summary>Banks the finished level's time and falls and moves on to the next level.</summary>
	public static void CompleteCurrent(float time, int falls)
	{
		RunTime += time;
		RunFalls += falls;
		if (!IsLastLevel)
			CurrentIndex++;
	}

	/// <summary>
	/// Testing shortcut: moves to the next level without banking anything,
	/// wrapping round to level 1 after the last one.
	/// </summary>
	public static void SkipCurrent()
	{
		CurrentIndex = IsLastLevel ? 0 : CurrentIndex + 1;
	}

	/// <summary>Starts a new run at the level with this index (from the title screen or level select).</summary>
	public static void StartAt(int index)
	{
		StartNewRun();
		CurrentIndex = Mathf.Clamp(index, 0, Count - 1);
		_argsApplied = true;
	}

	public static void StartNewRun()
	{
		CurrentIndex = 0;
		RunTime = 0;
		RunFalls = 0;
	}

	private static string Arg(string name)
	{
		string[] args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == name && i + 1 < args.Length)
				return args[i + 1];
			if (args[i].StartsWith(name + "="))
				return args[i][(name.Length + 1)..];
		}
		return null;
	}
}
