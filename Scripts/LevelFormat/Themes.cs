using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace BallMadness.LevelFormat;

/// <summary>
/// How a world looks: sky, fog, sunlight, the ground far below the course,
/// menu colours and the course and hazard materials. The built-in themes are
/// <c>res://themes/*.json</c>; a pack picks one in its <c>pack.json</c>
/// (<c>"theme": { "base": "lava" }</c>) and may change any part of it there.
///
/// Everything is optional: a field left out comes from the base theme, and
/// a theme with no base starts from "classic".
/// </summary>
public class ThemeFile
{
	/// <summary>In pack.json: the built-in theme this one starts from.</summary>
	public string Base { get; set; }

	/// <summary>In themes/*.json: the id packs use as <see cref="Base"/>.</summary>
	public string Id { get; set; }

	public string Name { get; set; }
	public string Description { get; set; }

	public SkyDef Sky { get; set; }
	public FogDef Fog { get; set; }
	public LightDef Light { get; set; }
	public GroundDef Ground { get; set; }
	public UiDef Ui { get; set; }

	/// <summary>
	/// Looks by material key ("grid", "rail", "hazard", ...). They replace the
	/// level's own materials with the same key, unless the level marks its
	/// material <c>override</c>. Merged key by key over the base theme's.
	/// </summary>
	public Dictionary<string, MaterialDef> Materials { get; set; }
}

/// <summary>The background: a gradient from the top of the screen to the bottom, with optional stars.</summary>
public class SkyDef
{
	public Vec3 Top { get; set; } = new(0.08f, 0.06f, 0.2f);
	public Vec3 Horizon { get; set; } = new(0.35f, 0.3f, 0.55f);

	/// <summary>Below the horizon; only seen in the light it gives off.</summary>
	public Vec3? Ground { get; set; }

	/// <summary>0 for none up to about 1 for a sky full of stars.</summary>
	public float? Stars { get; set; }
}

public class FogDef
{
	public Vec3 Color { get; set; } = new(0.5f, 0.5f, 0.6f);

	/// <summary>Thickness everywhere. Keep it small (0.002 to 0.01): the camera is 40 m away.</summary>
	public float? Density { get; set; }

	/// <summary>Extra fog below <see cref="Height"/> (relative to the level's kill height), so drops look deep.</summary>
	public float? HeightDensity { get; set; }
	public float? Height { get; set; }
}

public class LightDef
{
	public Vec3 Color { get; set; } = new(1, 1, 1);
	public float? Energy { get; set; }

	/// <summary>Sun angle in degrees: pitch below the horizon and compass yaw.</summary>
	public float? Pitch { get; set; }
	public float? Yaw { get; set; }

	/// <summary>Light from the sky on everything; with <see cref="Ambient"/> a flat colour instead of the sky's.</summary>
	public float? AmbientEnergy { get; set; }
	public Vec3? Ambient { get; set; }
}

/// <summary>A huge plane under the course: grass, sand, water, lava. Decoration only.</summary>
public class GroundDef
{
	public Vec3 Color { get; set; } = new(0.2f, 0.2f, 0.25f);

	/// <summary>Metres below the level's kill height.</summary>
	public float? Depth { get; set; }

	/// <summary>Makes it give off its own colour (lava).</summary>
	public float? Glow { get; set; }
	public float? Roughness { get; set; }
	public float? Metallic { get; set; }
}

/// <summary>Menu colours for the pack's title screen, level select and pause menu.</summary>
public class UiDef
{
	/// <summary>Borders and headings.</summary>
	public Vec3? Accent { get; set; }

	/// <summary>Button fill.</summary>
	public Vec3? Panel { get; set; }
}

/// <summary>Loads the built-in themes, works out a pack's theme and puts it on a scene.</summary>
public static class Themes
{
	public const string Root = "res://themes";
	public const string ClassicId = "classic";

	private static readonly Shader SkyShader = GD.Load<Shader>("res://Shaders/sky.gdshader");

	private static List<ThemeFile> _builtIn;

	/// <summary>Every built-in theme, "classic" first, then the worlds in file name order.</summary>
	public static IReadOnlyList<ThemeFile> BuiltIn => _builtIn ??= LoadBuiltIn();

	public static ThemeFile Find(string id) => BuiltIn.FirstOrDefault(t => t.Id == id);

	public static ThemeFile Classic => Find(ClassicId) ?? new ThemeFile { Id = ClassicId, Name = "Classic" };

	/// <summary>
	/// The theme a pack plays in: its <c>theme</c> laid over its base. A
	/// <c>-- --theme ID</c> on the command line replaces every pack's theme,
	/// to preview one.
	/// </summary>
	public static ThemeFile For(Pack pack)
	{
		string forced = CommandLineTheme();
		if (forced != null && Find(forced) is ThemeFile preview)
			return preview;
		return Resolve(pack?.Info.Theme);
	}

	/// <summary>Lays a pack's theme block over the built-in theme it names.</summary>
	public static ThemeFile Resolve(ThemeFile theme)
	{
		var classic = Classic;
		if (theme == null)
			return classic;
		var baseTheme = string.IsNullOrEmpty(theme.Base) ? classic : Find(theme.Base);
		if (baseTheme == null)
		{
			GD.PushWarning($"No built-in theme \"{theme.Base}\", using {ClassicId}");
			baseTheme = classic;
		}
		return Merge(Merge(classic, baseTheme), theme);
	}

	/// <summary>The built-in theme a pack starts from: "classic" when it has none.</summary>
	public static string BaseId(PackFile info) =>
		string.IsNullOrEmpty(info?.Theme?.Base) ? ClassicId : info.Theme.Base;

	/// <summary>The theme block for pack.json after picking a built-in theme, keeping the pack's own changes.</summary>
	public static ThemeFile WithBase(ThemeFile theme, string baseId)
	{
		var copy = theme == null ? new ThemeFile() : Parse(Serialize(theme), "theme");
		copy.Base = baseId == ClassicId ? null : baseId;
		bool empty = copy.Base == null && copy.Sky == null && copy.Fog == null && copy.Light == null &&
			copy.Ground == null && copy.Ui == null && (copy.Materials == null || copy.Materials.Count == 0);
		return empty ? null : copy;
	}

	private static ThemeFile Merge(ThemeFile under, ThemeFile over)
	{
		var materials = new Dictionary<string, MaterialDef>(under.Materials ?? new());
		foreach (var (key, look) in over.Materials ?? new())
			materials[key] = look;
		return new ThemeFile
		{
			Id = over.Id ?? under.Id,
			Name = over.Name ?? under.Name,
			Description = over.Description ?? under.Description,
			Sky = over.Sky ?? under.Sky,
			Fog = over.Fog ?? under.Fog,
			Light = over.Light ?? under.Light,
			Ground = over.Ground ?? under.Ground,
			Ui = over.Ui ?? under.Ui,
			Materials = materials,
		};
	}

	/// <summary>
	/// The look a part with material <paramref name="key"/> gets: the
	/// level's own when it's marked <c>override</c>, else the theme's, else
	/// the level's, else null.
	/// </summary>
	public static MaterialDef Look(LevelFile level, ThemeFile theme, string key)
	{
		key ??= "";
		MaterialDef own = null;
		level?.Materials.TryGetValue(key, out own);
		if (own?.Override == true)
			return own;
		if (theme?.Materials != null && theme.Materials.TryGetValue(key, out var themed))
			return themed;
		return own;
	}

	/// <summary>Sets the scene's WorldEnvironment and Sun (as in Main.tscn) from the theme.</summary>
	public static void ApplyEnvironment(Node scene, ThemeFile theme, float killHeight = -12)
	{
		var environment = scene.GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment;
		if (environment != null)
		{
			var sky = theme.Sky ?? new SkyDef();
			var skyMaterial = new ShaderMaterial { Shader = SkyShader };
			skyMaterial.SetShaderParameter("top_color", ToColor(sky.Top));
			skyMaterial.SetShaderParameter("horizon_color", ToColor(sky.Horizon));
			skyMaterial.SetShaderParameter("ground_color", ToColor(sky.Ground ?? (Vec3)((Vector3)sky.Horizon * 0.3f)));
			skyMaterial.SetShaderParameter("stars", sky.Stars ?? 0.0f);
			environment.BackgroundMode = Godot.Environment.BGMode.Sky;
			environment.Sky = new Sky { SkyMaterial = skyMaterial };

			var light = theme.Light ?? new LightDef();
			if (light.Ambient is Vec3 ambient)
			{
				environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
				environment.AmbientLightColor = ToColor(ambient);
			}
			else
			{
				environment.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
			}
			environment.AmbientLightEnergy = light.AmbientEnergy ?? 0.6f;

			var fog = theme.Fog;
			environment.FogEnabled = fog != null;
			if (fog != null)
			{
				environment.FogLightColor = ToColor(fog.Color);
				environment.FogDensity = fog.Density ?? 0.004f;
				environment.FogSkyAffect = 0;
				environment.FogHeight = killHeight + (fog.Height ?? 6);
				environment.FogHeightDensity = fog.HeightDensity ?? 0;
			}
		}

		if (scene.GetNodeOrNull<DirectionalLight3D>("Sun") is DirectionalLight3D sun)
		{
			var light = theme.Light ?? new LightDef();
			sun.LightColor = ToColor(light.Color);
			sun.LightEnergy = light.Energy ?? 1.2f;
			sun.Rotation = new Vector3(Mathf.DegToRad(-(light.Pitch ?? 54.4f)), Mathf.DegToRad(light.Yaw ?? 31.5f), 0);
		}
	}

	/// <summary>The menu's accent and fill colours.</summary>
	public static (Color Accent, Color Panel) UiColors(ThemeFile theme)
	{
		var ui = theme?.Ui;
		return (ToColor(ui?.Accent ?? new Vec3(0.45f, 0.4f, 0.8f)), ToColor(ui?.Panel ?? new Vec3(0.12f, 0.1f, 0.28f)));
	}

	public static ThemeFile Parse(string json, string source)
	{
		try
		{
			return JsonSerializer.Deserialize(json, LevelJsonContext.Default.ThemeFile)
				?? throw new InvalidOperationException($"{source} is empty");
		}
		catch (JsonException e)
		{
			throw new InvalidOperationException($"{source} is not a valid theme: {e.Message}", e);
		}
	}

	public static string Serialize(ThemeFile theme) =>
		JsonSerializer.Serialize(theme, LevelJsonContext.Default.ThemeFile);

	private static List<ThemeFile> LoadBuiltIn()
	{
		var themes = new List<ThemeFile>();
		using var dir = DirAccess.Open(Root);
		var files = dir == null ? Array.Empty<string>() : dir.GetFiles();
		foreach (string file in files.Where(f => f.EndsWith(".json", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal))
		{
			string path = $"{Root}/{file}";
			try
			{
				var theme = Parse(FileAccess.GetFileAsString(path), path);
				theme.Id ??= file.GetBaseName();
				theme.Name ??= theme.Id;
				themes.Add(theme);
			}
			catch (Exception e)
			{
				GD.PushWarning($"Skipping theme {path}: {e.Message}");
			}
		}
		// Classic first: it's the default and keeps each level's own colours.
		return themes.OrderBy(t => t.Id == ClassicId ? 0 : 1).ToList();
	}

	private static string CommandLineTheme()
	{
		string[] args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--theme" && i + 1 < args.Length)
				return args[i + 1];
			if (args[i].StartsWith("--theme="))
				return args[i]["--theme=".Length..];
		}
		return null;
	}

	public static Color ToColor(Vec3 v) => new(v.X, v.Y, v.Z);
}
