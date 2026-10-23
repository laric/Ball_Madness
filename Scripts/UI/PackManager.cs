using System;
using System.Collections.Generic;
using System.Linq;
using BallMadness.Editing;
using BallMadness.LevelFormat;
using Godot;

namespace BallMadness;

/// <summary>
/// The level editor's front page: the player's own packs (in user://packs/)
/// and their levels. Make, rename and delete packs, reorder and delete
/// levels, open a level in the <see cref="LevelEditor"/>, and share packs as
/// zips. Built-in packs aren't listed; they can't be changed.
/// </summary>
public partial class PackManager : Control
{
	/// <summary>Called when the player backs out (Back button, Esc or gamepad B).</summary>
	public Action Closed;

	/// <summary>The pack to show first, e.g. the one just edited.</summary>
	public string InitialPackId;

	private ItemList _packList;
	private ItemList _levelList;
	private LineEdit _nameEdit;
	private LineEdit _authorEdit;
	private Label _info;
	private Control _details;
	private Label _empty;
	private FileDialog _importDialog;
	private FileDialog _exportDialog;
	private ConfirmationDialog _confirm;
	private AcceptDialog _message;
	private Action _confirmed;
	private List<Pack> _packs = new();

	private Pack SelectedPack =>
		_packList.GetSelectedItems() is { Length: > 0 } items && items[0] < _packs.Count ? _packs[items[0]] : null;

	private int SelectedLevel =>
		_levelList.GetSelectedItems() is { Length: > 0 } items ? items[0] : -1;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		Theme = MenuUi.CompactTheme;

		var column = MenuUi.CenteredColumn(this, null, 1040);
		MenuUi.AddHeading(column, "LEVEL EDITOR", 40);

		var columns = new HBoxContainer();
		columns.AddThemeConstantOverride("separation", 24);
		column.AddChild(columns);

		// Packs
		var left = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		columns.AddChild(left);
		left.AddChild(new Label { Text = "Your packs" });
		_packList = new ItemList { CustomMinimumSize = new Vector2(0, 380) };
		_packList.ItemSelected += _ => ShowPack();
		left.AddChild(_packList);
		var packButtons = new HBoxContainer();
		left.AddChild(packButtons);
		Add(packButtons, "New pack", NewPack);
		Add(packButtons, "Import zip…", () => _importDialog.PopupCentered(new Vector2I(900, 560)));

		// The selected pack
		var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		columns.AddChild(right);
		_empty = new Label
		{
			Text = "Make a pack to start building levels, or import one someone shared with you.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		right.AddChild(_empty);

		var details = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		_details = details;
		right.AddChild(details);
		_nameEdit = AddEdit(details, "Name", name => UpdateInfo(info => info.Name = string.IsNullOrWhiteSpace(name) ? info.Id : name.Trim()));
		_authorEdit = AddEdit(details, "Author", author => UpdateInfo(info => info.Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim()));
		_info = new Label { Modulate = new Color(1, 1, 1, 0.7f) };
		details.AddChild(_info);
		details.AddChild(new Label { Text = "Levels, in play order" });
		_levelList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220) };
		_levelList.ItemActivated += _ => EditLevel();
		details.AddChild(_levelList);

		var levelButtons = new HBoxContainer();
		details.AddChild(levelButtons);
		Add(levelButtons, "New level", NewLevel);
		Add(levelButtons, "Edit", EditLevel);
		Add(levelButtons, "Move up", () => MoveLevel(-1));
		Add(levelButtons, "Move down", () => MoveLevel(1));
		Add(levelButtons, "Delete level", DeleteLevel);

		var packActions = new HBoxContainer();
		details.AddChild(packActions);
		Add(packActions, "Play pack ▶", PlayPack);
		Add(packActions, "Export zip…", ExportPack);
		Add(packActions, "Delete pack", DeletePack);

		MenuUi.AddButton(column, "Back", Close);

		_importDialog = new FileDialog
		{
			Title = "Import a level pack",
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			Filters = new[] { "*.zip ; Level pack zips" },
		};
		_importDialog.FileSelected += ImportPack;
		AddChild(_importDialog);

		_exportDialog = new FileDialog
		{
			Title = "Export the pack as a zip",
			FileMode = FileDialog.FileModeEnum.SaveFile,
			Access = FileDialog.AccessEnum.Filesystem,
			Filters = new[] { "*.zip ; Level pack zips" },
		};
		_exportDialog.FileSelected += path => Try(() =>
		{
			Packs.ExportZip(SelectedPack, path);
			Tell($"Saved {path.GetFile()}. Anyone can add it with Import zip.");
		});
		AddChild(_exportDialog);

		_confirm = new ConfirmationDialog { Title = "Are you sure?" };
		_confirm.Confirmed += () => _confirmed?.Invoke();
		AddChild(_confirm);
		_message = new AcceptDialog { Title = "Level editor" };
		AddChild(_message);

		RefreshPacks(InitialPackId);
		if (_packs.Count > 0)
			_packList.CallDeferred(Control.MethodName.GrabFocus);
		else
			MenuUi.FocusFirst(this);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Close();
		}
	}

	private void RefreshPacks(string selectId)
	{
		_packs = Packs.All.Where(p => !p.BuiltIn).ToList();
		_packList.Clear();
		foreach (var pack in _packs)
			_packList.AddItem($"{pack.Name}  ({LevelCount(pack.LevelPaths.Count)})");

		int index = _packs.FindIndex(p => p.Id == selectId);
		if (index < 0 && _packs.Count > 0)
			index = 0;
		if (index >= 0)
			_packList.Select(index);
		ShowPack();
	}

	private void ShowPack(int selectLevel = -1)
	{
		var pack = SelectedPack;
		_details.Visible = pack != null;
		_empty.Visible = pack == null;
		if (pack == null)
			return;

		_nameEdit.Text = pack.Name;
		_authorEdit.Text = pack.Info.Author ?? "";
		_info.Text = $"id {pack.Id}  ·  {pack.Folder}";
		_levelList.Clear();
		for (int i = 0; i < pack.LevelPaths.Count; i++)
		{
			string path = pack.LevelPaths[i];
			string name;
			try
			{
				name = Packs.LoadLevel(path).Name;
			}
			catch (Exception e)
			{
				name = $"(can't read: {e.Message})";
			}
			_levelList.AddItem($"{i + 1}. {name}    {path.GetFile()}");
		}
		if (selectLevel >= 0 && selectLevel < _levelList.ItemCount)
			_levelList.Select(selectLevel);
		else if (_levelList.ItemCount > 0)
			_levelList.Select(0);
	}

	private void NewPack()
	{
		string name = "My pack";
		for (int n = 2; _packs.Any(p => p.Name == name); n++)
			name = $"My pack {n}";
		Try(() =>
		{
			var pack = Packs.CreatePack(name);
			RefreshPacks(pack.Id);
			_nameEdit.CallDeferred(Control.MethodName.GrabFocus);
			_nameEdit.CallDeferred(LineEdit.MethodName.SelectAll);
		});
	}

	private void UpdateInfo(Action<PackFile> change)
	{
		var pack = SelectedPack;
		if (pack == null)
			return;
		var info = Packs.InfoCopy(pack);
		string before = Packs.Serialize(info);
		change(info);
		if (Packs.Serialize(info) == before)
			return;
		Try(() =>
		{
			int level = SelectedLevel;
			Packs.SaveInfo(pack, info);
			Levels.ReloadPack();
			RefreshPacks(info.Id);
			ShowPack(level);
		});
	}

	private void MoveLevel(int direction)
	{
		var pack = SelectedPack;
		int index = SelectedLevel;
		int target = index + direction;
		if (pack == null || index < 0 || target < 0 || target >= pack.LevelPaths.Count)
			return;
		var info = Packs.InfoCopy(pack);
		(info.Levels[index], info.Levels[target]) = (info.Levels[target], info.Levels[index]);
		Try(() =>
		{
			Packs.SaveInfo(pack, info);
			Levels.ReloadPack();
			RefreshPacks(pack.Id);
			ShowPack(target);
		});
	}

	private void DeleteLevel()
	{
		var pack = SelectedPack;
		int index = SelectedLevel;
		if (pack == null || index < 0)
			return;
		string file = pack.LevelPaths[index].GetFile();
		Confirm($"Delete level {index + 1} ({file}) from {pack.Name}? This can't be undone.", () => Try(() =>
		{
			Packs.DeleteLevel(pack, file);
			Levels.ReloadPack();
			RefreshPacks(pack.Id);
			ShowPack(Mathf.Max(index - 1, 0));
		}));
	}

	private void DeletePack()
	{
		var pack = SelectedPack;
		if (pack == null)
			return;
		Confirm($"Delete the pack \"{pack.Name}\" and its {LevelCount(pack.LevelPaths.Count)}? Export it first if you want to keep a copy.", () => Try(() =>
		{
			Packs.DeletePack(pack);
			Levels.ReloadPack();
			RefreshPacks(null);
		}));
	}

	private void NewLevel()
	{
		if (SelectedPack is Pack pack)
			OpenEditor(pack, null);
	}

	private void EditLevel()
	{
		var pack = SelectedPack;
		int index = SelectedLevel;
		if (pack != null && index >= 0)
			OpenEditor(pack, pack.LevelPaths[index].GetFile());
	}

	private void OpenEditor(Pack pack, string fileName)
	{
		EditorSession.Open(pack.Id, fileName);
		GetTree().ChangeSceneToFile(LevelEditor.ScenePath);
	}

	private void PlayPack()
	{
		var pack = SelectedPack;
		if (pack == null)
			return;
		if (pack.LevelPaths.Count == 0)
		{
			Tell("This pack has no levels yet. Make one with New level.");
			return;
		}
		Levels.SelectPack(pack.Id);
		Levels.StartAt(Mathf.Max(SelectedLevel, 0));
		GetTree().ChangeSceneToFile(Game.ScenePath);
	}

	private void ExportPack()
	{
		var pack = SelectedPack;
		if (pack == null)
			return;
		if (pack.LevelPaths.Count == 0)
		{
			Tell("This pack has no levels to share yet.");
			return;
		}
		_exportDialog.CurrentFile = $"{pack.Id}.zip";
		_exportDialog.PopupCentered(new Vector2I(900, 560));
	}

	private void ImportPack(string path) => Try(() =>
	{
		string id = PackIdIn(path);
		bool replacing = id != null && Packs.Find(id) is { BuiltIn: false };
		var pack = Packs.ImportZip(path);
		Levels.ReloadPack();
		RefreshPacks(pack.Id);
		Tell($"Imported \"{pack.Name}\" with {LevelCount(pack.LevelPaths.Count)}{(replacing ? ", replacing the older copy" : "")}.");
	});

	/// <summary>The pack id a zip will import as, or null if it can't be read (ImportZip reports why).</summary>
	private static string PackIdIn(string zipPath)
	{
		var zip = new ZipReader();
		if (zip.Open(zipPath) != Error.Ok)
			return null;
		try
		{
			string entry = zip.GetFiles().Where(f => f == "pack.json" || f.EndsWith("/pack.json")).OrderBy(f => f.Length).FirstOrDefault();
			return entry == null ? null : Packs.ParsePack(zip.ReadFile(entry).GetStringFromUtf8(), entry).Id;
		}
		catch (Exception)
		{
			return null;
		}
		finally
		{
			zip.Close();
		}
	}

	private void Try(Action action)
	{
		try
		{
			action();
		}
		catch (Exception e)
		{
			GD.PushWarning(e.Message);
			Tell(e.Message);
		}
	}

	private void Tell(string text)
	{
		_message.DialogText = text;
		_message.PopupCentered();
	}

	private void Confirm(string text, Action confirmed)
	{
		_confirmed = confirmed;
		_confirm.DialogText = text;
		_confirm.PopupCentered();
	}

	private static Button Add(Container parent, string text, Action pressed)
	{
		var button = MenuUi.AddButton(parent, text, pressed);
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		return button;
	}

	private static LineEdit AddEdit(Container parent, string label, Action<string> committed)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(80, 0) });
		var edit = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		edit.TextSubmitted += text => committed(text);
		edit.FocusExited += () => committed(edit.Text);
		row.AddChild(edit);
		parent.AddChild(row);
		return edit;
	}

	private static string LevelCount(int count) => count == 1 ? "1 level" : $"{count} levels";

	private void Close()
	{
		QueueFree();
		Closed?.Invoke();
	}
}
