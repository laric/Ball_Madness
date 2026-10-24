using Godot;

namespace BallMadness;

/// <summary>
/// Player preferences, stored in user://settings.cfg. Values are loaded the
/// first time any of them is read; call <see cref="Save"/> after changing one.
/// Keys this version doesn't know about are kept when saving.
/// </summary>
public static class Settings
{
	/// <summary>How the camera's yaw behaves while playing.</summary>
	public enum CameraModes
	{
		/// <summary>The camera keeps its angle; only right-drag turns it.</summary>
		Fixed,

		/// <summary>The camera slowly swings round to look the way the ball is rolling.</summary>
		Follow,
	}

	/// <summary>How the stick or arrow keys steer the ball.</summary>
	public enum SteeringModes
	{
		/// <summary>Up rolls the ball towards the top of the screen.</summary>
		Camera,

		/// <summary>Left and right turn the ball's heading; up rolls it forward along it.</summary>
		Tank,
	}

	public const string Path = "user://settings.cfg";

	private const string CameraSection = "camera";
	private const string ControlsSection = "controls";
	private const string AudioSection = "audio";

	private static ConfigFile _file;

	private static ConfigFile File
	{
		get
		{
			if (_file == null)
				Load();
			return _file;
		}
	}

	public static CameraModes CameraMode
	{
		get => System.Enum.TryParse((string)File.GetValue(CameraSection, "mode", nameof(CameraModes.Fixed)), out CameraModes mode)
			? mode
			: CameraModes.Fixed;
		set => File.SetValue(CameraSection, "mode", value.ToString());
	}

	public static SteeringModes SteeringMode
	{
		get => System.Enum.TryParse((string)File.GetValue(ControlsSection, "steering", nameof(SteeringModes.Camera)), out SteeringModes mode)
			? mode
			: SteeringModes.Camera;
		set => File.SetValue(ControlsSection, "steering", value.ToString());
	}

	/// <summary>Overall volume, 0 to 1. Apply changes with <see cref="Sfx.ApplySettings"/>.</summary>
	public static float MasterVolume
	{
		get => Volume("master", 1.0f);
		set => File.SetValue(AudioSection, "master", Mathf.Clamp(value, 0, 1));
	}

	/// <summary>Volume of the gameplay sounds, 0 to 1.</summary>
	public static float EffectsVolume
	{
		get => Volume("effects", 1.0f);
		set => File.SetValue(AudioSection, "effects", Mathf.Clamp(value, 0, 1));
	}

	/// <summary>Volume of the menu clicks, 0 to 1.</summary>
	public static float MenuVolume
	{
		get => Volume("menu", 0.7f);
		set => File.SetValue(AudioSection, "menu", Mathf.Clamp(value, 0, 1));
	}

	/// <summary>Silences everything without losing the volume levels.</summary>
	public static bool Muted
	{
		get => File.GetValue(AudioSection, "muted", false).VariantType == Variant.Type.Bool && (bool)File.GetValue(AudioSection, "muted", false);
		set => File.SetValue(AudioSection, "muted", value);
	}

	private static float Volume(string key, float fallback)
	{
		Variant value = File.GetValue(AudioSection, key, fallback);
		return value.VariantType is Variant.Type.Float or Variant.Type.Int ? Mathf.Clamp((float)value, 0, 1) : fallback;
	}

	/// <summary>(Re)reads the settings file. A missing or broken file means all defaults.</summary>
	public static void Load()
	{
		_file = new ConfigFile();
		Error err = _file.Load(Path);
		if (err != Error.Ok && err != Error.FileNotFound)
		{
			GD.PushWarning($"Couldn't read {Path} ({err}); using default settings.");
			_file = new ConfigFile();
		}
	}

	public static void Save()
	{
		Error err = File.Save(Path);
		if (err != Error.Ok)
			GD.PushWarning($"Couldn't save {Path} ({err}).");
	}
}
