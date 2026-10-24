using System.Collections.Generic;
using Godot;

namespace BallMadness;

/// <summary>
/// Plays the game's sound effects. Loaded as an autoload (see project.godot),
/// so it lives across scene changes and keeps playing while the game is paused.
///
/// Sounds are <c>res://Sounds/NAME.wav</c>, made by <c>tools/make-sounds.py</c>.
/// Anything can make a noise with <see cref="Play"/> (heard the same everywhere)
/// or <see cref="PlayAt"/> (comes from a spot in the level), or keep a hum
/// going with <see cref="Loop"/>. A name with no file stays silent and warns once.
///
/// It also sets up the audio buses, applies the volume settings, and gives
/// every button in every menu its hover and click sounds.
///
/// Start the game with <c>-- --log-sounds</c> to print each sound as it plays,
/// for checking them in headless runs where nothing is heard.
/// </summary>
public partial class Sfx : Node
{
	/// <summary>Gameplay sounds: the ball, hazards, checkpoints, the clock.</summary>
	public const string EffectsBus = "Effects";

	/// <summary>Menu clicks and focus ticks.</summary>
	public const string MenuBus = "Menu";

	private const string Folder = "res://Sounds/";

	/// <summary>How many sounds can play at once in each kind of pool.</summary>
	private const int Voices = 12;

	private static Sfx _instance;
	private static bool _log;
	private static readonly Dictionary<string, AudioStream> Streams = new();
	private static readonly HashSet<string> Missing = new();

	private readonly List<AudioStreamPlayer> _flat = new();
	private readonly List<AudioStreamPlayer3D> _spatial = new();
	private int _nextFlat;
	private int _nextSpatial;
	private ulong _lastMenuSound;

	public override void _EnterTree()
	{
		_instance = this;
		_log = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--log-sounds") >= 0;
		ProcessMode = ProcessModeEnum.Always;
		AddBus(EffectsBus);
		AddBus(MenuBus);
		ApplySettings();
		GetTree().NodeAdded += OnNodeAdded;
	}

	public override void _ExitTree()
	{
		if (_instance == this)
			_instance = null;
	}

	/// <summary>Plays a sound that isn't tied to a place, such as the clock or a menu click.</summary>
	public static void Play(string name, float volumeDb = 0, float pitch = 1, string bus = EffectsBus)
	{
		if (_instance == null || Stream(name) is not { } stream)
			return;

		Log(name, volumeDb, pitch);
		var player = _instance.NextFlat();
		player.Stream = stream;
		player.VolumeDb = volumeDb;
		player.PitchScale = pitch;
		player.Bus = bus;
		player.Play();
	}

	/// <summary>Plays a sound from a point in the level; it gets quieter further from the camera.</summary>
	public static void PlayAt(string name, Vector3 position, float volumeDb = 0, float pitch = 1)
	{
		if (_instance == null || Stream(name) is not { } stream)
			return;

		Log(name, volumeDb, pitch, position);
		var player = _instance.NextSpatial();
		player.Stream = stream;
		player.VolumeDb = volumeDb;
		player.PitchScale = pitch;
		player.Position = position;
		player.Play();
	}

	/// <summary>
	/// Starts a looping sound that follows <paramref name="parent"/> and stops
	/// when it is freed, for things that hum while they run. Change the
	/// returned player's VolumeDb or PitchScale as needed. Returns null when
	/// the sound doesn't exist.
	/// </summary>
	public static AudioStreamPlayer3D Loop(Node3D parent, string name, float volumeDb = 0)
	{
		if (Stream(name, loop: true) is not { } stream)
			return null;

		var player = Spatial();
		player.Name = $"{name}Sound";
		player.Stream = stream;
		player.VolumeDb = volumeDb;
		player.Autoplay = true;
		parent.AddChild(player);
		return player;
	}

	/// <summary>A looping sound that isn't tied to a place, such as the ball's roll. Add it as a child.</summary>
	public static AudioStreamPlayer LoopFlat(string name)
	{
		if (Stream(name, loop: true) is not { } stream)
			return null;
		return new AudioStreamPlayer { Stream = stream, Bus = EffectsBus, Name = $"{name}Sound" };
	}

	/// <summary>Sets the bus volumes and mute from <see cref="Settings"/>; call after changing them.</summary>
	public static void ApplySettings()
	{
		SetVolume("Master", Settings.MasterVolume);
		SetVolume(EffectsBus, Settings.EffectsVolume);
		SetVolume(MenuBus, Settings.MenuVolume);
		AudioServer.SetBusMute(0, Settings.Muted);
	}

	private static void SetVolume(string bus, float linear)
	{
		int index = AudioServer.GetBusIndex(bus);
		if (index < 0)
			return;
		AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Clamp(linear, 0, 1)));
		AudioServer.SetBusMute(index, index != 0 && linear <= 0.001f);
	}

	private static void AddBus(string name)
	{
		if (AudioServer.GetBusIndex(name) >= 0)
			return;
		AudioServer.AddBus();
		int index = AudioServer.BusCount - 1;
		AudioServer.SetBusName(index, name);
		AudioServer.SetBusSend(index, "Master");
	}

	private static void Log(string name, float volumeDb, float pitch, Vector3? at = null)
	{
		if (_log)
			GD.Print($"[sound] {name} {volumeDb:0.0}dB x{pitch:0.00}{(at is { } p ? $" at {p.X:0.0},{p.Y:0.0},{p.Z:0.0}" : "")} frame {Engine.GetPhysicsFrames()}");
	}

	private static AudioStream Stream(string name, bool loop = false)
	{
		if (Streams.TryGetValue(name, out var cached))
			return cached;
		if (Missing.Contains(name))
			return null;

		string path = Folder + name + ".wav";
		var stream = Load(path);
		if (stream == null)
		{
			Missing.Add(name);
			GD.PushWarning($"No sound called \"{name}\" ({path}).");
			return null;
		}
		if (loop && stream is AudioStreamWav wav && wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
		{
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
		}
		Streams[name] = stream;
		return stream;
	}

	/// <summary>
	/// Loads a sound through Godot's import, or straight from the .wav when
	/// the import cache hasn't been built yet (a fresh checkout or a new sound
	/// run with <c>godot --path .</c> before the editor has opened the project).
	/// </summary>
	private static AudioStream Load(string path)
	{
		var import = new ConfigFile();
		if (import.Load(path + ".import") == Error.Ok
			&& import.GetValue("remap", "path", "").AsString() is { Length: > 0 } imported
			&& FileAccess.FileExists(imported))
			return GD.Load<AudioStream>(path);

		if (FileAccess.FileExists(path))
			return AudioStreamWav.LoadFromFile(path);
		return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
	}

	private AudioStreamPlayer NextFlat()
	{
		if (_flat.Count < Voices)
		{
			var player = new AudioStreamPlayer();
			AddChild(player);
			_flat.Add(player);
			return player;
		}
		// Reuse an idle voice if there is one, else cut off the oldest.
		for (int i = 0; i < _flat.Count; i++)
		{
			var idle = _flat[(_nextFlat + i) % _flat.Count];
			if (!idle.Playing)
				return idle;
		}
		_nextFlat = (_nextFlat + 1) % _flat.Count;
		return _flat[_nextFlat];
	}

	private AudioStreamPlayer3D NextSpatial()
	{
		if (_spatial.Count < Voices)
		{
			var player = Spatial();
			AddChild(player);
			_spatial.Add(player);
			return player;
		}
		for (int i = 0; i < _spatial.Count; i++)
		{
			var idle = _spatial[(_nextSpatial + i) % _spatial.Count];
			if (!idle.Playing)
				return idle;
		}
		_nextSpatial = (_nextSpatial + 1) % _spatial.Count;
		return _spatial[_nextSpatial];
	}

	/// <summary>A 3D player tuned for the course's scale: the camera sits 15 to 30 m from the action.</summary>
	private static AudioStreamPlayer3D Spatial() => new()
	{
		Bus = EffectsBus,
		UnitSize = 12,
		MaxDistance = 80,
		AttenuationFilterCutoffHz = 20500,
	};

	/// <summary>Gives every button its sounds as it appears, so menus need no code of their own.</summary>
	private void OnNodeAdded(Node node)
	{
		if (node is BaseButton button)
		{
			button.Pressed += () => MenuSound("menu_click");
			button.FocusEntered += () => MenuSound("menu_move");
		}
		else if (node is Slider slider)
		{
			slider.FocusEntered += () => MenuSound("menu_move");
		}
	}

	private void MenuSound(string name)
	{
		// Clicking a button focuses it too, and menus refocus when they open; one sound is enough.
		ulong now = Time.GetTicksMsec();
		if (name == "menu_move" && (now - _lastMenuSound < 120 || Input.IsMouseButtonPressed(MouseButton.Left)))
			return;
		_lastMenuSound = now;
		Play(name, bus: MenuBus);
	}
}
