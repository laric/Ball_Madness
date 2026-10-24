using System;
using System.Collections.Generic;
using System.Linq;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness.Editing;

/// <summary>
/// The in-game level editor. Shows the level as the game builds it (with
/// hazards frozen) over a 0.5 m build grid, with a palette of everything in
/// <see cref="EditorCatalog"/> on the left and the <see cref="PropertyPanel"/>
/// on the right.
///
/// Mouse: left-click selects (or places the palette's part on the grid),
/// drag moves on the grid, the cone handle lifts and the ball handle turns
/// in 15° steps; Shift+click moves the grid to the top of what you click.
/// The camera works like the game's: right-drag orbits, the wheel zooms,
/// WASD or middle-drag pans.
///
/// Keys: arrows and PgUp/PgDn move by 0.5 m, R turns 90°, Q/E 15°, T/G
/// tilt 15°, Ctrl+D duplicates, Delete removes, Ctrl+Z/Ctrl+Y undo and redo,
/// Ctrl+S saves and F5 test plays. Esc backs out.
///
/// The level, undo history, selection and camera live in
/// <see cref="EditorSession"/>, so a test play comes back to the same spot.
/// </summary>
public partial class LevelEditor : Node3D
{
	public const string ScenePath = "res://Scenes/Editor.tscn";

	/// <summary>Grid step in metres for placing and moving.</summary>
	public const float Snap = 0.5f;

	private const float FineTurn = 15.0f;
	private const int MaxUndo = 200;

	private static readonly Color SelectionColor = new(1f, 0.85f, 0.25f);
	private static readonly Color TravelColor = new(0.4f, 0.9f, 1f);

	private enum DragKind { None, Move, Lift, Turn, Pan }

	private LevelFile _level;
	private EditorItem? _selected;
	private CatalogEntry _tool;
	private float _buildHeight;

	private Node3D _focus;
	private CameraRig _rig;
	private Camera3D _camera;
	private Node3D _preview;
	private MeshInstance3D _grid;
	private ShaderMaterial _gridMaterial;
	private ImmediateMesh _lines;
	private MeshInstance3D _liftHandle;
	private MeshInstance3D _turnHandle;
	private MeshInstance3D _cursor;
	private MeshInstance3D _spawnMarker;

	private PropertyPanel _panel;
	private Label _title;
	private Label _status;
	private Label _problems;
	private SpinBox _heightSpin;
	private Button _undoButton;
	private Button _redoButton;
	private readonly Dictionary<CatalogEntry, Button> _toolButtons = new();
	private ConfirmationDialog _leaveDialog;

	private DragKind _drag;
	private string _dragSnapshot;
	private bool _dragChanged;
	private Vector3 _dragStartPos;
	private Vector3 _dragStartRot;
	private Vector3 _dragStartHit;
	private Vector3 _dragCenter;
	private float _dragStartAngle;
	private Plane _dragPlane;
	private Vector2 _panLast;
	private float _statusTimer;

	public override void _Ready()
	{
		_focus = GetNode<Node3D>("Focus");
		_rig = GetNode<CameraRig>("CameraRig");
		_camera = GetNode<Camera3D>("CameraRig/Camera3D");

		BuildOverlay();
		BuildUi(GetNode<Control>("Ui/Root"));

		if (!EditorSession.IsOpen)
		{
			// Run on its own (from Godot or a test): edit a new level in the first user pack.
			var pack = Packs.All.FirstOrDefault(p => !p.BuiltIn) ?? Packs.CreatePack("My levels");
			EditorSession.Open(pack.Id, null);
		}
		LoadFromSession();
	}

	/// <summary>Starts editing a level of a user pack (a new one when <paramref name="fileName"/> is null).</summary>
	public void OpenLevel(string packId, string fileName)
	{
		EditorSession.Open(packId, string.IsNullOrEmpty(fileName) ? null : fileName);
		LoadFromSession();
	}

	private void LoadFromSession()
	{
		var pack = Packs.Find(EditorSession.PackId);
		try
		{
			if (EditorSession.WorkingCopy != null)
				_level = Packs.ParseLevel(EditorSession.WorkingCopy, "working copy");
			else if (EditorSession.FileName != null && pack != null)
				_level = Packs.LoadLevel($"{pack.Folder}/levels/{EditorSession.FileName}");
			else
				_level = NewLevel(pack);
		}
		catch (Exception e)
		{
			GD.PushError(e.Message);
			_level = NewLevel(pack);
			ShowStatus($"Couldn't open the level: {e.Message}", true);
		}
		bool returning = EditorSession.WorkingCopy != null;
		EditorSession.WorkingCopy = null;

		_selected = EditorSession.Selected is EditorItem s && EditorItems.Exists(_level, s) ? s : null;
		// Start the grid on the floor the ball starts on.
		SetBuildHeight(returning ? EditorSession.BuildHeight : EditorItems.GroundUnder(_level, _level.Spawn) ?? 0);

		if (EditorSession.View is { } view)
		{
			_focus.GlobalPosition = view.Focus;
			_rig.SetView(view.Yaw, view.Pitch, view.Zoom);
		}
		else
		{
			FrameLevel();
		}
		_rig.SnapToTarget();

		RefreshScene();
		RebuildPanel();
	}

	/// <summary>A start pad with the spawn on it: the smallest level that loads.</summary>
	private static LevelFile NewLevel(Pack pack)
	{
		var level = new LevelFile
		{
			Name = pack == null ? "New level" : $"Level {pack.LevelPaths.Count + 1}",
			TimeLimit = 60,
			KillHeight = -8,
			Spawn = new Vec3(0, 1.5f, 0),
		};
		level.Course.Add(new Part { Name = "StartPad", Shape = "box", Pos = new Vec3(0, 0, 0), Size = new Vec3(8, 1, 8), Material = "start" });
		EditorCatalog.EnsureMaterial(level, "start");
		EditorCatalog.EnsureMaterial(level, "grid");
		return level;
	}

	// ---------------------------------------------------------------- scene

	private void BuildOverlay()
	{
		_gridMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/editor_grid.gdshader") };
		_grid = new MeshInstance3D
		{
			Name = "Grid",
			Mesh = new PlaneMesh { Size = new Vector2(240, 240) },
			MaterialOverride = _gridMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_grid);

		_lines = new ImmediateMesh();
		AddChild(new MeshInstance3D
		{
			Name = "Lines",
			Mesh = _lines,
			MaterialOverride = OverlayMaterial(Colors.White, vertexColor: true),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		_liftHandle = Handle("LiftHandle", new CylinderMesh { TopRadius = 0, BottomRadius = 0.45f, Height = 0.9f, RadialSegments = 12 }, new Color(0.3f, 1f, 0.6f));
		_turnHandle = Handle("TurnHandle", new SphereMesh { Radius = 0.4f, Height = 0.8f }, new Color(1f, 0.55f, 0.15f));
		_cursor = Handle("Cursor", new SphereMesh { Radius = 0.25f, Height = 0.5f }, TravelColor);

		_spawnMarker = new MeshInstance3D
		{
			Name = "SpawnMarker",
			Mesh = new SphereMesh { Radius = 0.5f, Height = 1 },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 1f, 0.7f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.3f },
		};
		AddChild(_spawnMarker);
	}

	private MeshInstance3D Handle(string name, Mesh mesh, Color color)
	{
		var handle = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = OverlayMaterial(color), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(handle);
		return handle;
	}

	/// <summary>Unshaded and drawn over everything, so handles and outlines show through the course.</summary>
	private static StandardMaterial3D OverlayMaterial(Color color, bool vertexColor = false) => new()
	{
		AlbedoColor = color,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		NoDepthTest = true,
		VertexColorUseAsAlbedo = vertexColor,
		RenderPriority = 10,
	};

	/// <summary>Rebuilds the level preview from the file and refreshes the checks.</summary>
	private void RefreshScene()
	{
		if (_preview != null)
		{
			RemoveChild(_preview);
			_preview.QueueFree();
			_preview = null;
		}
		try
		{
			var built = LevelBuilder.Build(_level);
			// Frozen: hazards are shown where they start.
			built.ProcessMode = ProcessModeEnum.Disabled;
			AddChild(built);
			_preview = built;
		}
		catch (Exception e)
		{
			ShowStatus(e.Message, true);
		}

		var problems = EditorItems.Problems(_level);
		_problems.Text = problems.Count == 0 ? "✓ Ready to save" : string.Join("\n", problems.Select(p => "• " + p));
		_problems.AddThemeColorOverride("font_color", problems.Count == 0 ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f));
		UpdateTitle();
	}

	private void RebuildPanel() =>
		_panel.Show(_level, PropertyPanel.FieldsFor(_level, _selected, SetKillHeightUnderCourse));

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		PanWithKeys(dt);

		// A release over a panel never reaches _UnhandledInput.
		if (_drag is DragKind.Move or DragKind.Lift or DragKind.Turn && !Input.IsMouseButtonPressed(MouseButton.Left))
			EndDrag();
		if (_drag == DragKind.Pan && !Input.IsMouseButtonPressed(MouseButton.Middle))
			_drag = DragKind.None;

		if (_statusTimer > 0)
		{
			_statusTimer -= dt;
			if (_statusTimer <= 0)
				_status.Text = "";
		}
		UpdateOverlay();
	}

	private float HandleScale => _camera.Size / 25.0f;

	private void UpdateOverlay()
	{
		_spawnMarker.Position = _level.Spawn;
		_grid.Position = new Vector3(Mathf.Snapped(_focus.Position.X, 2), _buildHeight, Mathf.Snapped(_focus.Position.Z, 2));
		_gridMaterial.SetShaderParameter("fade_center", _focus.Position);
		_gridMaterial.SetShaderParameter("fade_radius", Mathf.Max(_camera.Size * 1.5f, 30));

		_lines.ClearSurfaces();
		_liftHandle.Visible = _turnHandle.Visible = false;
		if (_selected is not EditorItem item)
			return;

		var bounds = EditorItems.Bounds(_level, item);
		_lines.SurfaceBegin(Mesh.PrimitiveType.Lines);
		DrawBox(bounds, SelectionColor);

		if (item.Kind == ItemKind.Hazard)
		{
			var hazard = _level.Hazards[item.Index];
			if (hazard.Type == "mover")
			{
				// Where the mover's trip ends.
				Vector3 offset = bounds.Transform.Basis * (Vector3)(hazard.Offset ?? new Vector3(0, 0, 4));
				var end = bounds with { Transform = bounds.Transform.Translated(offset) };
				DrawBox(end, TravelColor);
				Line(bounds.WorldCenter, end.WorldCenter, TravelColor);
			}
			else if (hazard.Type == "chaser")
			{
				Circle(hazard.Pos, hazard.ChaseRadius ?? 10, TravelColor);
			}
			else if (hazard.Type == "teleporter")
			{
				// Where the ball comes out.
				Vector3 exit = hazard.Target ?? (Vector3)hazard.Pos + new Vector3(0, 0, 8);
				Circle(exit, 0.85f, TravelColor);
				Line(bounds.WorldCenter, exit, TravelColor);
			}
			else if (hazard.Type is "oneway" or "conveyor")
			{
				// The way the ball is let through, or carried.
				Vector3 forward = bounds.Transform.Basis.Z.Normalized() * (hazard.Type == "conveyor" && (hazard.Speed ?? 4) < 0 ? -1 : 1);
				Vector3 from = bounds.WorldCenter - forward * 1.5f, to = bounds.WorldCenter + forward * 1.5f;
				Line(from, to, TravelColor);
				Vector3 side = forward.Cross(Vector3.Up).Normalized() * 0.5f;
				Line(to, to - forward * 0.6f + side, TravelColor);
				Line(to, to - forward * 0.6f - side, TravelColor);
			}
		}

		float s = HandleScale;
		Vector3 center = bounds.WorldCenter;
		float top = bounds.MaxY;
		_liftHandle.Visible = true;
		_liftHandle.Scale = Vector3.One * s;
		_liftHandle.Position = new Vector3(center.X, top + 1.4f * s, center.Z);
		Line(new Vector3(center.X, top, center.Z), _liftHandle.Position, new Color(0.3f, 1f, 0.6f));

		if (EditorItems.CanRotate(item))
		{
			float radius = TurnRadius(bounds);
			float yaw = Mathf.DegToRad(EditorItems.GetRot(_level, item).Y);
			var ringCenter = new Vector3(center.X, top, center.Z);
			_turnHandle.Visible = true;
			_turnHandle.Scale = Vector3.One * s;
			_turnHandle.Position = ringCenter + new Basis(Vector3.Up, yaw) * Vector3.Right * radius;
			Circle(ringCenter, radius, new Color(1f, 0.55f, 0.15f));
		}
		_lines.SurfaceEnd();
	}

	private float TurnRadius(Obb bounds)
	{
		Vector3 center = bounds.WorldCenter;
		float reach = bounds.Corners().Max(c => new Vector2(c.X - center.X, c.Z - center.Z).Length());
		return reach + 0.8f * HandleScale;
	}

	private void DrawBox(Obb box, Color color)
	{
		var c = box.Corners().ToArray();
		// Corner i has bit 0 = +x, bit 1 = +y, bit 2 = +z; join corners one bit apart.
		for (int i = 0; i < 8; i++)
		{
			for (int bit = 1; bit < 8; bit <<= 1)
			{
				if ((i & bit) == 0)
					Line(c[i], c[i | bit], color);
			}
		}
	}

	private void Circle(Vector3 center, float radius, Color color)
	{
		const int segments = 48;
		for (int i = 0; i < segments; i++)
		{
			float a = Mathf.Tau * i / segments, b = Mathf.Tau * (i + 1) / segments;
			Line(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius, center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius, color);
		}
	}

	private void Line(Vector3 a, Vector3 b, Color color)
	{
		_lines.SurfaceSetColor(color);
		_lines.SurfaceAddVertex(a);
		_lines.SurfaceSetColor(color);
		_lines.SurfaceAddVertex(b);
	}

	/// <summary>Points the camera at the middle of the level and zooms to fit it.</summary>
	private void FrameLevel()
	{
		var centers = EditorItems.All(_level).Select(i => EditorItems.Bounds(_level, i).WorldCenter).ToList();
		Vector3 min = centers.Aggregate((a, b) => a.Min(b)), max = centers.Aggregate((a, b) => a.Max(b));
		_focus.GlobalPosition = (min + max) / 2;
		float extent = Mathf.Max(max.X - min.X, max.Z - min.Z);
		_rig.SetView(_rig.Yaw, _rig.Pitch, Mathf.Clamp(extent * 1.1f + 10, 16, _rig.MaxZoom));
	}

	private void FrameSelection()
	{
		if (_selected is EditorItem item)
			_focus.GlobalPosition = EditorItems.Bounds(_level, item).WorldCenter;
		else
			FrameLevel();
	}

	// ---------------------------------------------------------------- editing

	/// <summary>Runs an edit as one undo step and refreshes the view.</summary>
	private void Change(Action edit, bool rebuildPanel = true)
	{
		PushUndo(Packs.Serialize(_level));
		edit();
		MarkDirty();
		RefreshScene();
		if (rebuildPanel)
			RebuildPanel();
	}

	private void PushUndo(string snapshot)
	{
		EditorSession.Undo.Add(snapshot);
		if (EditorSession.Undo.Count > MaxUndo)
			EditorSession.Undo.RemoveAt(0);
		EditorSession.Redo.Clear();
	}

	private void Undo() => Restore(EditorSession.Undo, EditorSession.Redo, "Undone");

	private void Redo() => Restore(EditorSession.Redo, EditorSession.Undo, "Redone");

	private void Restore(List<string> from, List<string> to, string message)
	{
		if (from.Count == 0)
			return;
		to.Add(Packs.Serialize(_level));
		_level = Packs.ParseLevel(from[^1], "undo");
		from.RemoveAt(from.Count - 1);
		if (_selected is EditorItem item && !EditorItems.Exists(_level, item))
			_selected = null;
		MarkDirty();
		RefreshScene();
		RebuildPanel();
		ShowStatus(message);
	}

	private void MarkDirty() => EditorSession.Dirty = true;

	private void Select(EditorItem? item)
	{
		if (_selected == item)
			return;
		_selected = item;
		RebuildPanel();
	}

	private void SetTool(CatalogEntry entry)
	{
		_tool = entry;
		foreach (var (e, button) in _toolButtons)
			button.SetPressedNoSignal(e == entry);
		_cursor.Visible = false;
		if (entry != null)
			ShowStatus($"Click a part or the grid to place a {entry.Label.ToLowerInvariant()} (Shift+click to place more)");
	}

	private void Place(CatalogEntry entry, Vector3 floor) =>
		Change(() => _selected = entry.Add(_level, floor));

	private void MoveSelected(Vector3 delta)
	{
		if (_selected is EditorItem item)
			Change(() => EditorItems.SetPos(_level, item, EditorItems.GetPos(_level, item) + delta));
	}

	private void RotateSelected(Vector3 degrees)
	{
		if (_selected is EditorItem item && EditorItems.CanRotate(item))
			Change(() => EditorItems.SetRot(_level, item, EditorItems.GetRot(_level, item) + degrees));
	}

	private void DeleteSelected()
	{
		if (_selected is not EditorItem item)
			return;
		if (item.Kind == ItemKind.Spawn)
		{
			ShowStatus("A level always has a spawn; move it instead.", true);
			return;
		}
		Change(() =>
		{
			EditorItems.Remove(_level, item);
			_selected = null;
		});
	}

	private void DuplicateSelected()
	{
		if (_selected is EditorItem item && item.Kind != ItemKind.Spawn)
			Change(() => _selected = EditorItems.Duplicate(_level, item));
	}

	private void SetKillHeightUnderCourse() =>
		Change(() => _level.KillHeight = Mathf.Floor(EditorItems.LowestPoint(_level) - 4));

	private void SetBuildHeight(float height)
	{
		_buildHeight = Mathf.Snapped(height, Snap);
		_heightSpin.SetValueNoSignal(_buildHeight);
	}

	// ---------------------------------------------------------------- input

	public override void _Input(InputEvent @event)
	{
		// Drags keep going while the pointer is over a panel.
		if (@event is InputEventMouseMotion motion && _drag != DragKind.None)
			DragTo(motion.Position);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
				if (button.Pressed)
					LeftPress(button.Position, button.ShiftPressed);
				else
					EndDrag();
				if (IsInsideTree())
					GetViewport().SetInputAsHandled();
				break;

			case InputEventMouseButton { ButtonIndex: MouseButton.Middle } button:
				_drag = button.Pressed ? DragKind.Pan : DragKind.None;
				_panLast = button.Position;
				break;

			case InputEventMouseMotion motion when _drag == DragKind.None:
				UpdateCursor(motion.Position);
				break;

			case InputEventKey { Pressed: true } key:
				// Leaving or testing changes scene; this node may be out of the tree by now.
				if (HandleKey(key) && IsInsideTree())
					GetViewport().SetInputAsHandled();
				break;
		}
	}

	private bool HandleKey(InputEventKey key)
	{
		bool ctrl = key.CtrlPressed || key.MetaPressed;
		bool shift = key.ShiftPressed;
		Key code = key.Keycode;
		bool repeatable = code is Key.Up or Key.Down or Key.Left or Key.Right or Key.Pageup or Key.Pagedown;
		if (key.Echo && !repeatable)
			return false;

		if (ctrl)
		{
			switch (code)
			{
				case Key.Z when shift: Redo(); return true;
				case Key.Z: Undo(); return true;
				case Key.Y: Redo(); return true;
				case Key.S: Save(); return true;
				case Key.D: DuplicateSelected(); return true;
			}
			return false;
		}

		float step = shift ? Snap * 4 : Snap;
		switch (code)
		{
			case Key.Up: MoveSelected(ScreenAxis(forward: true) * step); return true;
			case Key.Down: MoveSelected(-ScreenAxis(forward: true) * step); return true;
			case Key.Right: MoveSelected(ScreenAxis(forward: false) * step); return true;
			case Key.Left: MoveSelected(-ScreenAxis(forward: false) * step); return true;
			case Key.Pageup or Key.Pagedown:
				float dy = code == Key.Pageup ? Snap : -Snap;
				if (_selected != null && !shift)
					MoveSelected(Vector3.Up * dy);
				else
					SetBuildHeight(_buildHeight + dy);
				return true;
			case Key.R: RotateSelected(new Vector3(0, shift ? -90 : 90, 0)); return true;
			case Key.Q: RotateSelected(new Vector3(0, FineTurn, 0)); return true;
			case Key.E: RotateSelected(new Vector3(0, -FineTurn, 0)); return true;
			case Key.T: RotateSelected(new Vector3(-FineTurn, 0, 0)); return true;
			case Key.G: RotateSelected(new Vector3(FineTurn, 0, 0)); return true;
			case Key.Delete or Key.Backspace: DeleteSelected(); return true;
			case Key.F: FrameSelection(); return true;
			case Key.F5: Test(); return true;
			case Key.Escape:
				if (_tool != null)
					SetTool(null);
				else if (_selected != null)
					Select(null);
				else
					Back();
				return true;
			case >= Key.Key1 and <= Key.Key9:
				int index = (int)(code - Key.Key1);
				if (index < EditorCatalog.Entries.Count)
					SetTool(EditorCatalog.Entries[index]);
				return true;
		}
		return false;
	}

	/// <summary>The world axis closest to the screen's up (or right) direction, so arrow keys move things the way they look.</summary>
	private Vector3 ScreenAxis(bool forward)
	{
		Vector3 dir = forward ? -_rig.GlobalBasis.Z : _rig.GlobalBasis.X;
		dir.Y = 0;
		return Mathf.Abs(dir.X) > Mathf.Abs(dir.Z)
			? new Vector3(Mathf.Sign(dir.X), 0, 0)
			: new Vector3(0, 0, Mathf.Sign(dir.Z));
	}

	private void PanWithKeys(float dt)
	{
		if (GetViewport().GuiGetFocusOwner() is LineEdit || Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta))
			return;
		var input = new Vector2(
			(Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
			(Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0));
		if (input == Vector2.Zero)
			return;
		Vector3 right = _rig.GlobalBasis.X with { Y = 0 };
		Vector3 forward = -_rig.GlobalBasis.Z with { Y = 0 };
		_focus.GlobalPosition += (right.Normalized() * input.X + forward.Normalized() * input.Y) * _camera.Size * 0.8f * dt;
	}

	private (Vector3 Origin, Vector3 Dir) Ray(Vector2 screen) =>
		(_camera.ProjectRayOrigin(screen), _camera.ProjectRayNormal(screen));

	private static Vector3? HitHeight(Vector3 origin, Vector3 dir, float height) =>
		new Plane(Vector3.Up, height).IntersectsRay(origin, dir);

	private static Vector3 SnapXZ(Vector3 p) => new(Mathf.Snapped(p.X, Snap), p.Y, Mathf.Snapped(p.Z, Snap));

	/// <summary>
	/// Where a new object would stand: on the part or hazard under the
	/// pointer if there is one above the grid, otherwise on the grid.
	/// X and Z are snapped to the grid.
	/// </summary>
	private Vector3? PlacementPoint(Vector3 origin, Vector3 dir)
	{
		Vector3? onGrid = HitHeight(origin, dir, _buildHeight);
		float gridT = onGrid is Vector3 g ? (g - origin).Dot(dir) : float.MaxValue;
		float surfaceT = float.MaxValue;
		foreach (var item in EditorItems.All(_level))
		{
			if (item.Kind is ItemKind.Part or ItemKind.Hazard && EditorItems.Bounds(_level, item).Intersect(origin, dir) is float t)
				surfaceT = Mathf.Min(surfaceT, t);
		}
		if (surfaceT >= gridT)
			return onGrid is Vector3 p ? SnapXZ(p) with { Y = _buildHeight } : null;

		Vector3 hit = SnapXZ(origin + dir * surfaceT);
		float ground = EditorItems.GroundUnder(_level, hit + Vector3.Up * 0.05f) ?? _buildHeight;
		return hit with { Y = Mathf.Max(ground, _buildHeight) };
	}

	private Vector3? PlacementPoint(Vector2 screen)
	{
		var (origin, dir) = Ray(screen);
		return PlacementPoint(origin, dir);
	}

	private EditorItem? Pick(Vector3 origin, Vector3 dir)
	{
		EditorItem? best = null;
		float bestT = float.MaxValue;
		foreach (var item in EditorItems.All(_level))
		{
			float? t = EditorItems.Bounds(_level, item).Intersect(origin, dir);
			if (t is float hit && hit < bestT)
			{
				bestT = hit;
				best = item;
			}
		}
		return best;
	}

	private bool HitsHandle(MeshInstance3D handle, Vector3 origin, Vector3 dir)
	{
		if (!handle.Visible)
			return false;
		Vector3 toHandle = handle.GlobalPosition - origin;
		float along = toHandle.Dot(dir);
		return (toHandle - dir * along).Length() < 0.6f * HandleScale;
	}

	private void UpdateCursor(Vector2 screen)
	{
		if (_tool == null)
		{
			_cursor.Visible = false;
			return;
		}
		var hit = PlacementPoint(screen);
		_cursor.Visible = hit != null;
		if (hit is Vector3 point)
		{
			_cursor.Position = point;
			_cursor.Scale = Vector3.One * HandleScale;
		}
	}

	private void LeftPress(Vector2 screen, bool shift)
	{
		GetViewport().GuiReleaseFocus();
		var (origin, dir) = Ray(screen);

		if (_tool != null)
		{
			if (PlacementPoint(screen) is Vector3 floor)
				Place(_tool, floor);
			if (!shift)
				SetTool(null);
			return;
		}

		if (_selected is EditorItem selected)
		{
			if (HitsHandle(_liftHandle, origin, dir))
			{
				StartDrag(DragKind.Lift, selected, origin, dir);
				return;
			}
			if (HitsHandle(_turnHandle, origin, dir))
			{
				StartDrag(DragKind.Turn, selected, origin, dir);
				return;
			}
		}

		var picked = Pick(origin, dir);
		Select(picked);
		if (picked is not EditorItem item)
			return;
		if (shift)
		{
			SetBuildHeight(EditorItems.Bounds(_level, item).MaxY);
			ShowStatus($"Grid moved to {_buildHeight:0.#} m");
		}
		else
		{
			StartDrag(DragKind.Move, item, origin, dir);
		}
	}

	private void StartDrag(DragKind kind, EditorItem item, Vector3 origin, Vector3 dir)
	{
		_dragStartPos = EditorItems.GetPos(_level, item);
		_dragStartRot = EditorItems.GetRot(_level, item);
		var bounds = EditorItems.Bounds(_level, item);
		_dragCenter = bounds.WorldCenter;

		switch (kind)
		{
			case DragKind.Move:
				_dragPlane = new Plane(Vector3.Up, _dragStartPos.Y);
				break;
			case DragKind.Lift:
				// A vertical plane facing the camera through the handle.
				Vector3 facing = -_camera.GlobalBasis.Z with { Y = 0 };
				_dragPlane = new Plane(facing.LengthSquared() > 1e-4f ? facing.Normalized() : Vector3.Forward, _liftHandle.GlobalPosition);
				break;
			case DragKind.Turn:
				_dragPlane = new Plane(Vector3.Up, _turnHandle.GlobalPosition.Y);
				break;
		}
		if (_dragPlane.IntersectsRay(origin, dir) is not Vector3 hit)
			return;

		_dragStartHit = hit;
		_dragStartAngle = Mathf.Atan2(-(hit.Z - _dragCenter.Z), hit.X - _dragCenter.X);
		_dragSnapshot = Packs.Serialize(_level);
		_dragChanged = false;
		_drag = kind;
	}

	private void DragTo(Vector2 screen)
	{
		if (_drag == DragKind.Pan)
		{
			Vector2 delta = screen - _panLast;
			_panLast = screen;
			float metresPerPixel = _camera.Size / GetViewport().GetVisibleRect().Size.Y;
			Vector3 right = _rig.GlobalBasis.X with { Y = 0 };
			Vector3 forward = -_rig.GlobalBasis.Z with { Y = 0 };
			float stretch = 1.0f / Mathf.Max(Mathf.Sin(-_rig.Pitch), 0.3f);
			_focus.GlobalPosition += (-right.Normalized() * delta.X + forward.Normalized() * delta.Y * stretch) * metresPerPixel;
			return;
		}

		if (_selected is not EditorItem item || !EditorItems.Exists(_level, item))
			return;
		var (origin, dir) = Ray(screen);
		if (_dragPlane.IntersectsRay(origin, dir) is not Vector3 hit)
			return;

		switch (_drag)
		{
			case DragKind.Move:
				Vector3 moved = _dragStartPos + new Vector3(Mathf.Snapped(hit.X - _dragStartHit.X, Snap), 0, Mathf.Snapped(hit.Z - _dragStartHit.Z, Snap));
				ApplyDrag(() => EditorItems.GetPos(_level, item), v => EditorItems.SetPos(_level, item, v), moved);
				break;
			case DragKind.Lift:
				Vector3 lifted = _dragStartPos + Vector3.Up * Mathf.Snapped(hit.Y - _dragStartHit.Y, Snap);
				ApplyDrag(() => EditorItems.GetPos(_level, item), v => EditorItems.SetPos(_level, item, v), lifted);
				break;
			case DragKind.Turn:
				float angle = Mathf.Atan2(-(hit.Z - _dragCenter.Z), hit.X - _dragCenter.X);
				float turned = Mathf.Snapped(Mathf.RadToDeg(Mathf.AngleDifference(_dragStartAngle, angle)), FineTurn);
				ApplyDrag(() => EditorItems.GetRot(_level, item), v => EditorItems.SetRot(_level, item, v), _dragStartRot + new Vector3(0, turned, 0));
				break;
		}
	}

	/// <summary>Sets a dragged value if the snapped value changed; the undo step is taken when the drag ends.</summary>
	private void ApplyDrag(Func<Vector3> get, Action<Vector3> set, Vector3 value)
	{
		if (get().IsEqualApprox(value))
			return;
		set(value);
		_dragChanged = true;
		RefreshScene();
	}

	private void EndDrag()
	{
		if (_drag is DragKind.Move or DragKind.Lift or DragKind.Turn && _dragChanged)
		{
			PushUndo(_dragSnapshot);
			MarkDirty();
			RebuildPanel();
			UpdateTitle();
		}
		_drag = DragKind.None;
		_dragChanged = false;
	}

	// ---------------------------------------------------------------- save, test, leave

	/// <summary>Checks the level and writes it into its pack. Returns false if it couldn't.</summary>
	public bool Save()
	{
		var problems = EditorItems.Problems(_level);
		if (problems.Count > 0)
		{
			ShowStatus($"Can't save yet: {problems[0]}", true);
			return false;
		}
		var pack = Packs.Find(EditorSession.PackId);
		if (pack == null || pack.BuiltIn)
		{
			ShowStatus("The pack this level belongs to is gone.", true);
			return false;
		}
		string fileName = EditorSession.FileName ?? Packs.NewLevelFileName(pack, _level.Name);
		try
		{
			Packs.SaveLevel(pack, fileName, _level);
		}
		catch (Exception e)
		{
			ShowStatus($"Couldn't save: {e.Message}", true);
			return false;
		}
		EditorSession.FileName = fileName;
		EditorSession.Dirty = false;
		Levels.ReloadPack();
		ShowStatus($"Saved {fileName}");
		UpdateTitle();
		return true;
	}

	/// <summary>Plays the level as it is now, then comes back here.</summary>
	public void Test()
	{
		if (_level.Goals.Count == 0)
			ShowStatus("No goal yet: you can roll around, but there's no finish.", true);
		EditorSession.WorkingCopy = Packs.Serialize(_level);
		EditorSession.Selected = _selected;
		EditorSession.BuildHeight = _buildHeight;
		EditorSession.View = (_focus.GlobalPosition, _rig.Yaw, _rig.Pitch, _rig.Zoom);
		Levels.StartTest(Packs.ParseLevel(EditorSession.WorkingCopy, "test"));
		GetTree().ChangeSceneToFile(Game.ScenePath);
	}

	private void Back()
	{
		if (!EditorSession.Dirty)
		{
			Leave();
			return;
		}
		_leaveDialog.PopupCentered();
	}

	private void Leave()
	{
		EditorSession.Close();
		Levels.ReloadPack();
		GetTree().ChangeSceneToFile(Title.ScenePath);
	}

	// ---------------------------------------------------------------- ui

	private void BuildUi(Control root)
	{
		root.Theme = MenuUi.CompactTheme;
		root.MouseFilter = Control.MouseFilterEnum.Ignore;

		// Top bar
		var top = new PanelContainer();
		top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		root.AddChild(top);
		var bar = new HBoxContainer();
		bar.AddThemeConstantOverride("separation", 8);
		top.AddChild(bar);
		ToolbarButton(bar, "◀ Back", "Back to the pack manager (Esc)", Back);
		ToolbarButton(bar, "Save", "Check the level and save it to the pack (Ctrl+S)", () => Save());
		ToolbarButton(bar, "Test ▶", "Play the level, then come back here (F5)", Test);
		bar.AddChild(new VSeparator());
		_undoButton = ToolbarButton(bar, "Undo", "Ctrl+Z", Undo);
		_redoButton = ToolbarButton(bar, "Redo", "Ctrl+Y", Redo);
		bar.AddChild(new VSeparator());
		bar.AddChild(new Label { Text = "Grid height" });
		_heightSpin = new SpinBox { MinValue = -500, MaxValue = 500, Step = Snap, Suffix = "m", CustomMinimumSize = new Vector2(110, 0), TooltipText = "Where new parts are placed (Shift+PgUp/PgDn, or Shift+click an object to use its top)" };
		_heightSpin.ValueChanged += v => _buildHeight = (float)v;
		bar.AddChild(_heightSpin);
		bar.AddChild(new VSeparator());
		_title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
		bar.AddChild(_title);
		_status = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_status.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		_status.OffsetTop = 58;
		_status.GrowHorizontal = Control.GrowDirection.Both;
		root.AddChild(_status);

		// Palette, built from the catalog
		var left = new PanelContainer();
		left.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
		left.OffsetTop = 52;
		left.OffsetBottom = -44;
		left.CustomMinimumSize = new Vector2(150, 0);
		root.AddChild(left);
		// Scrolls when the window is too short for every entry.
		var paletteScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		left.AddChild(paletteScroll);
		var palette = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		palette.AddThemeConstantOverride("separation", 4);
		paletteScroll.AddChild(palette);
		string category = null;
		int number = 1;
		foreach (var entry in EditorCatalog.Entries)
		{
			if (entry.Category != category)
			{
				category = entry.Category;
				var heading = new Label { Text = category.ToUpperInvariant() };
				heading.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
				palette.AddChild(heading);
			}
			var e = entry;
			var button = new Button
			{
				Text = number <= 9 ? $"{number}  {entry.Label}" : entry.Label,
				ToggleMode = true,
				FocusMode = Control.FocusModeEnum.None,
				Alignment = HorizontalAlignment.Left,
				TooltipText = entry.Tooltip,
			};
			button.Toggled += on => SetTool(on ? e : null);
			palette.AddChild(button);
			_toolButtons[entry] = button;
			number++;
		}

		// Properties and checks
		var right = new PanelContainer();
		right.SetAnchorsPreset(Control.LayoutPreset.RightWide);
		right.OffsetTop = 52;
		right.OffsetBottom = -44;
		right.OffsetLeft = -340;
		root.AddChild(right);
		var column = new VBoxContainer();
		right.AddChild(column);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		column.AddChild(scroll);
		_panel = new PropertyPanel { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Changing = (edit, rebuild) => Change(edit, rebuild) };
		_panel.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(_panel);
		column.AddChild(new HSeparator());
		_problems = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
		column.AddChild(_problems);

		var help = new Label
		{
			Text = "Click select · drag move · cone lifts, ball turns · Shift+click: grid to its top · right-drag orbit · wheel zoom · WASD / middle-drag pan\n" +
				"Arrows, PgUp/PgDn move 0.5 m (Shift ×4) · R 90° · Q/E 15° · T/G tilt · Ctrl+D copy · Del delete · Ctrl+Z/Y undo/redo · Ctrl+S save · F5 test · F frame",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		help.AddThemeFontSizeOverride("font_size", 13);
		help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		help.OffsetTop = -40;
		help.OffsetLeft = 10;
		root.AddChild(help);

		_leaveDialog = new ConfirmationDialog
		{
			Title = "Unsaved changes",
			DialogText = "This level has changes that aren't saved.",
			OkButtonText = "Leave without saving",
		};
		_leaveDialog.AddButton("Save and leave", true, "save");
		_leaveDialog.Confirmed += Leave;
		_leaveDialog.CustomAction += action =>
		{
			_leaveDialog.Hide();
			if (action == "save" && Save())
				Leave();
		};
		root.AddChild(_leaveDialog);
	}

	private static Button ToolbarButton(Container parent, string text, string tooltip, Action pressed)
	{
		var button = new Button { Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None };
		button.Pressed += pressed;
		parent.AddChild(button);
		return button;
	}

	private void UpdateTitle()
	{
		if (_title == null)
			return;
		string pack = Packs.Find(EditorSession.PackId)?.Name ?? EditorSession.PackId;
		string file = EditorSession.FileName ?? "new level";
		_title.Text = $"{pack}  ·  {file}{(EditorSession.Dirty ? "  (unsaved)" : "")}";
		_undoButton.Disabled = EditorSession.Undo.Count == 0;
		_redoButton.Disabled = EditorSession.Redo.Count == 0;
	}

	private void ShowStatus(string text, bool error = false)
	{
		_status.Text = text;
		_status.AddThemeColorOverride("font_color", error ? new Color(1f, 0.55f, 0.45f) : new Color(0.8f, 0.95f, 1f));
		_statusTimer = error ? 6 : 3;
		if (error)
			GD.Print($"[editor] {text}");
	}
}
