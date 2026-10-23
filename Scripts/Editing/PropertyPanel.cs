using System;
using System.Collections.Generic;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness.Editing;

/// <summary>
/// The editor's right-hand panel: the selected object's settings, or the
/// level's when nothing is selected. Rows come from <see cref="FieldsFor"/>
/// and every edit goes through <see cref="Changing"/> so it can be undone.
/// </summary>
public partial class PropertyPanel : VBoxContainer
{
	/// <summary>Runs an edit as one undo step; the flag asks for the panel to be rebuilt after.</summary>
	public Action<Action, bool> Changing;

	private const int LabelWidth = 110;

	public static IEnumerable<Field> FieldsFor(LevelFile level, EditorItem? selected, Action setKillHeight)
	{
		if (selected is not EditorItem item)
		{
			yield return new NoteField("LEVEL");
			yield return new TextField("Name", () => level.Name, v => level.Name = v.Trim());
			yield return new TextField("Author", () => level.Author ?? "", v => level.Author = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
			yield return new NumberField("Time limit (s)", () => level.TimeLimit, v => level.TimeLimit = v) { Min = 5, Max = 999, Arrow = 5 };
			yield return new NumberField("Kill height", () => level.KillHeight, v => level.KillHeight = v) { Min = -500, Max = 500, Arrow = 1 };
			yield return new ButtonField("Put kill height 4 m under the course", setKillHeight);
			yield return new NoteField("Click an object to edit it, or pick a part on the left and click the grid to place it.");
			yield break;
		}

		yield return new NoteField(EditorItems.Label(level, item).ToUpperInvariant());
		switch (item.Kind)
		{
			case ItemKind.Part:
				var part = level.Course[item.Index];
				yield return new TextField("Name", () => part.Name ?? "", v => part.Name = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
				foreach (var field in Placement(level, item))
					yield return field;
				var shapes = new[] { "box", "cylinder", "sphere" };
				yield return new ChoiceField("Shape", shapes, () => Math.Max(Array.IndexOf(shapes, part.Shape), 0), i => SetShape(part, shapes[i])) { Rebuilds = true };
				foreach (var field in ShapeFields(part, ""))
					yield return field;
				yield return new MaterialField("Material", () => part.Material, v => part.Material = v);
				yield return new ToggleField("Solid", () => part.Collide ?? true, v => part.Collide = v ? null : false);
				yield return new ToggleField("Visible", () => part.Visible ?? true, v => part.Visible = v ? null : false);
				break;

			case ItemKind.Hazard:
				var hazard = level.Hazards[item.Index];
				yield return new TextField("Name", () => hazard.Name ?? "", v => hazard.Name = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
				foreach (var field in Placement(level, item))
					yield return field;
				var entry = EditorCatalog.ForHazard(hazard.Type);
				if (entry?.HazardFields != null)
				{
					foreach (var field in entry.HazardFields(hazard))
						yield return field;
				}
				if (hazard.Parts is { Count: > 0 } parts)
				{
					var body = parts[0];
					foreach (var field in ShapeFields(body, "Body "))
						yield return field;
					yield return new MaterialField("Body material", () => body.Material, v => body.Material = v);
					if (parts.Count > 1)
						yield return new NoteField($"This hazard has {parts.Count} parts; only the first is edited here.");
				}
				break;

			case ItemKind.Checkpoint:
				var checkpoint = level.Checkpoints[item.Index];
				foreach (var field in Placement(level, item))
					yield return field;
				yield return new VectorField("Size", () => checkpoint.Size, v => checkpoint.Size = v.Max(Vector3.One * 0.5f)) { Min = 0.5f };
				break;

			case ItemKind.Goal:
				var goal = level.Goals[item.Index];
				foreach (var field in Placement(level, item))
					yield return field;
				yield return new NumberField("Radius", () => goal.Radius ?? LevelBuilder.DefaultGoalRadius, v => goal.Radius = v) { Min = 0.5f, Max = 20 };
				break;

			default:
				foreach (var field in Placement(level, item))
					yield return field;
				yield return new NoteField("The ball starts here. Put it about 1 m above the floor.");
				break;
		}
	}

	private static IEnumerable<Field> Placement(LevelFile level, EditorItem item)
	{
		yield return new VectorField("Position", () => EditorItems.GetPos(level, item), v => EditorItems.SetPos(level, item, v));
		if (EditorItems.CanRotate(item))
			yield return new VectorField("Rotation (°)", () => EditorItems.GetRot(level, item), v => EditorItems.SetRot(level, item, v)) { Min = -360, Max = 360, Arrow = 15 };
	}

	private static IEnumerable<Field> ShapeFields(Part part, string prefix)
	{
		switch (part.Shape)
		{
			case "cylinder":
				yield return new NumberField(prefix + "Radius", () => part.Radius ?? 0.5f, v => part.Radius = v) { Min = 0.1f, Max = 100, Arrow = 0.5f };
				yield return new NumberField(prefix + "Height", () => part.Height ?? 1, v => part.Height = v) { Min = 0.1f, Max = 100, Arrow = 0.5f };
				break;
			case "sphere":
				yield return new NumberField(prefix + "Radius", () => part.Radius ?? 0.5f, v => part.Radius = v) { Min = 0.1f, Max = 100, Arrow = 0.5f };
				break;
			default:
				yield return new VectorField(prefix + "Size", () => part.Size ?? Vector3.One, v => part.Size = v.Max(Vector3.One * 0.1f)) { Min = 0.1f };
				break;
		}
	}

	/// <summary>Switches a part's shape, keeping roughly the same footprint.</summary>
	private static void SetShape(Part part, string shape)
	{
		if (shape == part.Shape)
			return;
		Vector3 half = EditorItems.PartHalf(part);
		part.Shape = shape;
		part.Size = shape == "box" ? half * 2 : null;
		part.Radius = shape == "box" ? null : Mathf.Max(half.X, half.Z);
		part.Height = shape == "cylinder" ? half.Y * 2 : null;
	}

	/// <summary>Replaces the panel's rows.</summary>
	public void Show(LevelFile level, IEnumerable<Field> fields)
	{
		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.QueueFree();
		}
		foreach (var field in fields)
			AddChild(MakeRow(level, field));
	}

	private Control MakeRow(LevelFile level, Field field)
	{
		switch (field)
		{
			case NoteField note:
				var noteLabel = new Label { Text = note.Label, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0) };
				if (note.Label == note.Label.ToUpperInvariant())
					noteLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
				return noteLabel;

			case ButtonField button:
				var b = new Button { Text = button.Label, FocusMode = FocusModeEnum.None, AutowrapMode = TextServer.AutowrapMode.WordSmart };
				b.Pressed += button.Pressed;
				return b;

			case TextField text:
				var edit = new LineEdit { Text = text.Get(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
				void CommitText()
				{
					if (edit.Text != text.Get())
						Changing?.Invoke(() => text.Set(edit.Text), false);
				}
				edit.TextSubmitted += _ => { CommitText(); edit.ReleaseFocus(); };
				edit.FocusExited += CommitText;
				return Row(text.Label, edit);

			case NumberField number:
				var spin = Spin(number.Min, number.Max, number.Arrow, number.Get());
				spin.ValueChanged += v => Changing?.Invoke(() => number.Set((float)v), number.Rebuilds);
				return Row(number.Label, spin);

			case VectorField vector:
				var column = new VBoxContainer();
				column.AddChild(new Label { Text = vector.Label });
				var boxes = new HBoxContainer();
				column.AddChild(boxes);
				Vector3 current = vector.Get();
				for (int axis = 0; axis < 3; axis++)
				{
					int a = axis;
					var s = Spin(vector.Min, vector.Max, vector.Arrow, current[axis]);
					s.Prefix = "xyz"[axis].ToString();
					s.ValueChanged += v =>
					{
						Vector3 value = vector.Get();
						value[a] = (float)v;
						Changing?.Invoke(() => vector.Set(value), vector.Rebuilds);
					};
					boxes.AddChild(s);
				}
				return column;

			case ChoiceField choice:
				var options = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
				foreach (string option in choice.Options)
					options.AddItem(option);
				options.Selected = choice.Get();
				options.ItemSelected += i => Changing?.Invoke(() => choice.Set((int)i), choice.Rebuilds);
				return Row(choice.Label, options);

			case ToggleField toggle:
				var check = new CheckBox { Text = toggle.Label, ButtonPressed = toggle.Get(), FocusMode = FocusModeEnum.None };
				check.Toggled += on => Changing?.Invoke(() => toggle.Set(on), toggle.Rebuilds);
				return check;

			case MaterialField material:
				var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
				var keys = EditorCatalog.MaterialKeys(level);
				picker.AddItem("(default)");
				foreach (string key in keys)
					picker.AddIconItem(Swatch(EditorCatalog.Look(level, key)), key);
				picker.Selected = Math.Max(keys.IndexOf(material.Get() ?? "") + 1, 0);
				picker.ItemSelected += i =>
				{
					string key = i == 0 ? null : keys[(int)i - 1];
					Changing?.Invoke(() =>
					{
						EditorCatalog.EnsureMaterial(level, key);
						material.Set(key);
					}, false);
				};
				return Row(material.Label, picker);

			default:
				throw new ArgumentException($"No control for {field.GetType().Name}");
		}
	}

	private static Control Row(string label, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(LabelWidth, 0) });
		control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(control);
		return row;
	}

	private static SpinBox Spin(float min, float max, float arrow, float value)
	{
		var spin = new SpinBox
		{
			MinValue = min,
			MaxValue = max,
			Step = 0.001,
			CustomArrowStep = arrow,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(60, 0),
			SelectAllOnFocus = true,
		};
		spin.SetValueNoSignal(value);
		return spin;
	}

	private static Texture2D Swatch(MaterialDef look)
	{
		var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgb8);
		var color = look == null ? new Color(0.55f, 0.62f, 0.75f) : new Color(look.Color.X, look.Color.Y, look.Color.Z);
		image.Fill(color);
		if (look?.Line is Vec3 line)
		{
			var lineColor = new Color(line.X, line.Y, line.Z);
			for (int i = 0; i < 16; i++)
			{
				image.SetPixel(i, 0, lineColor);
				image.SetPixel(0, i, lineColor);
				image.SetPixel(i, 8, lineColor);
				image.SetPixel(8, i, lineColor);
			}
		}
		return ImageTexture.CreateFromImage(image);
	}
}
