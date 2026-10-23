using System.Collections.Generic;
using Godot;

namespace BallMadness.Editing;

/// <summary>
/// What the editor is working on, kept while the game test plays the level
/// so the editor comes back exactly where it was: the level, its undo
/// history, the selection and the camera.
/// </summary>
public static class EditorSession
{
	/// <summary>The user pack the level belongs to.</summary>
	public static string PackId { get; private set; }

	/// <summary>The level's file name in the pack, or null until a new level is first saved.</summary>
	public static string FileName { get; set; }

	/// <summary>The level as it was when the editor was left for a test, or null to load it from the pack.</summary>
	public static string WorkingCopy { get; set; }

	public static List<string> Undo { get; } = new();
	public static List<string> Redo { get; } = new();
	public static bool Dirty { get; set; }
	public static EditorItem? Selected { get; set; }
	public static float BuildHeight { get; set; }

	/// <summary>Camera focus point, yaw, pitch and zoom; null to frame the level.</summary>
	public static (Vector3 Focus, float Yaw, float Pitch, float Zoom)? View { get; set; }

	/// <summary>Set when leaving the editor so the title screen reopens the pack manager on this pack.</summary>
	public static string ReturnToPack { get; set; }

	public static bool IsOpen => PackId != null;

	/// <summary>Starts editing a level of a pack (a new level when <paramref name="fileName"/> is null).</summary>
	public static void Open(string packId, string fileName)
	{
		PackId = packId;
		FileName = fileName;
		WorkingCopy = null;
		Undo.Clear();
		Redo.Clear();
		Dirty = fileName == null;
		Selected = null;
		BuildHeight = 0;
		View = null;
	}

	public static void Close()
	{
		ReturnToPack = PackId;
		PackId = null;
		FileName = null;
		WorkingCopy = null;
		Undo.Clear();
		Redo.Clear();
		Dirty = false;
		Selected = null;
		View = null;
	}
}
