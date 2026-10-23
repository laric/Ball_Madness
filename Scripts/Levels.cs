using System.Collections.Generic;
using Godot;

namespace BallMadness;

/// <summary>
/// The ordered list of levels and the progress of the current run.
///
/// Levels are found by file name: every res://levels/levelNN.tscn that exists
/// (level01 up to level99) is played in number order, so adding a level is
/// just adding its scene. The run state is static so it survives reloading
/// the main scene between levels.
/// </summary>
public static class Levels
{
	private const int MaxLevelNumber = 99;

	private static List<int> _numbers;

	/// <summary>Level numbers that exist, in play order.</summary>
	public static IReadOnlyList<int> Numbers => _numbers ??= Discover();

	public static int Count => Numbers.Count;

	/// <summary>Index into <see cref="Numbers"/> of the level being played.</summary>
	public static int CurrentIndex { get; private set; }

	public static int CurrentNumber => Numbers[CurrentIndex];
	public static bool IsLastLevel => CurrentIndex >= Count - 1;

	/// <summary>Seconds spent on the levels finished so far in this run.</summary>
	public static float RunTime { get; private set; }
	public static int RunFalls { get; private set; }

	private static bool _argsApplied;

	public static string PathFor(int number) => $"res://levels/level{number:00}.tscn";

	public static PackedScene LoadCurrent() => GD.Load<PackedScene>(PathFor(CurrentNumber));

	/// <summary>
	/// Picks the starting level from <c>-- --level N</c> the first time it's
	/// called; later calls (after a scene reload) do nothing.
	/// </summary>
	public static void ApplyCommandLine()
	{
		if (_argsApplied)
			return;
		_argsApplied = true;

		string[] args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			string value = null;
			if (args[i] == "--level" && i + 1 < args.Length)
				value = args[i + 1];
			else if (args[i].StartsWith("--level="))
				value = args[i]["--level=".Length..];

			if (value != null && int.TryParse(value, out int number))
			{
				int index = ((List<int>)Numbers).IndexOf(number);
				if (index >= 0)
					CurrentIndex = index;
				else
					GD.PushWarning($"No level {number}, starting at level {CurrentNumber}");
			}
		}
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

	private static List<int> Discover()
	{
		var numbers = new List<int>();
		for (int n = 1; n <= MaxLevelNumber; n++)
		{
			if (ResourceLoader.Exists(PathFor(n)))
				numbers.Add(n);
		}
		if (numbers.Count == 0)
			GD.PushError("No levels found in res://levels/");
		return numbers;
	}
}
