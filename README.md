# Ball Madness

A Marble Madness-style game built with **Godot 4.7.2 (.NET / C#)**. Roll the ball down a series of isometric obstacle courses, each before its clock runs out. Desktop targets: Windows and Linux.

![Prototype level](docs/screenshot.png)

## Requirements

- [Godot 4.7.2 .NET edition](https://godotengine.org/download) (the "Godot Engine - .NET" build, not the standard one)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Running

Open `project.godot` in the Godot .NET editor and press **F5**. Or from a terminal:

```sh
dotnet build
godot --path .
```

## Controls

| Action  | Keyboard        | Gamepad          |
|---------|-----------------|------------------|
| Roll    | WASD / arrows   | Left stick, D-pad |
| Rotate camera | Hold right mouse button and move the mouse | |
| Zoom    | Mouse wheel     |                  |
| Switch camera mode (fixed / follow) | C | |
| Pause menu | Esc          | Start            |
| Restart level | R         | Back             |
| Continue (next level / retry) | Enter / Space | A |
| Skip to next level (testing) | Click "Next level" at the top right (debug builds and the editor only) | |

Movement is relative to the camera: "up" always rolls the ball towards the top of the screen, however the camera is turned.

The camera has two modes, saved in `user://settings.cfg`. **Fixed** (the default) keeps its angle until you turn it. **Follow** slowly swings round to look the way the ball is rolling; it pauses while you right-drag and for two seconds after, and holds still while the ball falls.

Steering has two modes too, under Settings. **Screen direction** (the default) rolls the ball the way you push on screen. **Tank** turns the ball's heading with left and right (a yellow arrow beside the ball shows it) and pushes forward and back along it with up and down; each life starts facing along the course. With the Follow camera, tank steering keeps the camera behind the heading.

## Title screen and saves

The game opens on the title screen: **Continue** plays the level after the last one you completed (**Start** on a fresh save), **Level select** lists every level with your best time and fewest falls, **Pack** picks which level pack to play (shown once you have a pack of your own), **Level editor** opens the pack manager and editor (see below), and **Settings** has the camera and steering modes. Completed levels and the next one are playable from level select; later ones stay locked. Esc (or Start on a gamepad) pauses a level with Resume, Restart level, Settings and Title screen. Menus work with the mouse, the keyboard (arrows, Enter, Esc) and a gamepad (D-pad or stick, A, B).

Progress is saved to `user://save.json` (on Linux `~/.local/share/godot/app_userdata/Ball Madness/`, on Windows `%APPDATA%\Godot\app_userdata\Ball Madness\`) as soon as you reach a goal. It is stored per level pack id and per level index: the furthest level completed, and each level's best time and fewest falls. Delete the file to start over.

Starting with `-- --level N` or `-- --autopilot` skips the title screen. Autopilot runs don't write the save file unless you pass one with `-- --save PATH`.

## Levels

Levels come in **packs**. The game ships with the `main` pack in `packs/main/`; players' own packs go in the user data folder under `packs/` (`~/.local/share/godot/app_userdata/Ball Madness/packs/` on Linux, `%APPDATA%\Godot\app_userdata\Ball Madness\packs\` on Windows). Each pack is a folder with a `pack.json` and a `levels/` folder of level files:

```
packs/main/
  pack.json            id, name, author, and the level files in play order
  levels/
    01-first-descent.json
    02-switchbacks.json
    ...
```

A level file is plain JSON: the time limit, colours, the course's boxes, cylinders and ramps, moving hazards, checkpoints, goal and the autopilot's route. The game builds the level from it when it loads, and a level file can't contain code, so packs from other players are safe to play. [docs/level-format.md](docs/level-format.md) describes every field.

Reach the goal and the level-complete screen shows your time; press Enter for the next level. Run out of time and Enter or R retries the same level. After the last level the game-complete screen shows your total time and falls, and Enter starts again from level 1.

To make levels, use the in-game editor below, or copy one of the files in `packs/main/levels/`, edit it, and add its name to `pack.json`.

To jump straight to a level: `godot --path . -- --level 5`. To play another pack: `-- --pack ID`.

### Level 1: First Descent

Start high on the blue pad and work your way down: a wide ramp, a narrow ramp with no rails, a zig-zag bridge, then a slalom ramp with posts before the green goal pad. You have 60 seconds. Fall off and you respawn at the last yellow checkpoint, but the clock keeps running while you fall.

### Level 8: Piston Alley

A thin walkway with three pistons that shoot out across it from alternating sides. Wait for a piston to pull back, then go. After that, a narrow ramp with no rails and a zig-zag of beams only 1.4 m wide. 45 seconds.

### Level 9: Sweeper Works

A turntable with a long sweeper bar (follow it round, don't meet it head on), a bridge that slides in and out of a gap, a wiper bar that swings across the next bridge, a steep narrow ramp, and one more sliding bridge before the goal. 45 seconds.

### Level 10: The Gauntlet

The finale: faster pistons on a 2.5 m walkway, a sliding bridge, an arena guarded by two black steelies that chase you and try to knock you off, a fast turntable sweeper, and thin beams to the finish. 48 seconds.

## Level editor

**Level editor** on the title screen opens the pack manager. It lists your own packs (the built-in pack can't be changed): **New pack** makes one, the Name and Author boxes rename it, and the level list has **New level**, **Edit** (or double-click), **Move up**/**Move down** for the play order and **Delete level**. **Play pack** plays it from the selected level, **Export zip…** saves the pack as a zip to share, **Import zip…** adds one someone sent you (replacing an older copy of the same pack), and **Delete pack** removes it.

The editor shows the level as the game builds it, with hazards frozen where they start, over a build grid with 0.5 m steps. The camera works like the game's: right-drag to orbit, wheel to zoom, WASD or middle-drag to pan, F to frame the selection.

| Action | Mouse | Keys |
|---|---|---|
| Place a part, hazard or marker | Pick it in the palette (or press 1 to 9), then click a part or the grid; Shift+click places more | |
| Select | Click it | Esc deselects |
| Move | Drag it across the grid; drag the green cone up and down | Arrows (0.5 m, Shift for 2 m), PgUp/PgDn |
| Turn | Drag the orange ball round its ring (15° steps) | R / Shift+R 90°, Q/E 15°, T/G tilt 15° |
| Grid height | Shift+click an object to put the grid on its top | Shift+PgUp/PgDn, or the box in the toolbar |
| Duplicate, delete | | Ctrl+D, Delete |
| Undo, redo | Toolbar | Ctrl+Z, Ctrl+Y (or Ctrl+Shift+Z) |
| Save, test | Toolbar | Ctrl+S, F5 |

The panel on the right edits the selection's position, rotation, size, material (from a small preset palette) and hazard settings, or the level's name, time limit and kill height when nothing is selected. Underneath it, the editor checks the level as you go: it needs a name, a goal, ground under the spawn, and everything above the kill height before it will save. **Test** plays the level as it is now, saved or not; finish it or pick **Back to editor** in the pause menu to return with the camera, selection and undo history as you left them.

Palette entries come from one list in `Scripts/Editing/EditorCatalog.cs`; a new hazard type needs an entry there (with its property rows) and a case in `LevelBuilder`.

## Project layout

```
Scenes/Title.tscn   Title screen (the main scene): menus over a turning view of level 1
Scenes/Editor.tscn  Level editor: camera rig over the level being edited (the UI is built in code)
Scenes/Main.tscn    Game root: environment, light, ball, camera rig, HUD (the level is loaded at runtime)
Scenes/Ball.tscn    The player ball (RigidBody3D)
packs/main/          The built-in levels as JSON (see docs/level-format.md)
Scripts/Game.cs     Loads the level; countdown, timer, falling/respawn, checkpoints, goal, end screens
Scripts/Title.cs    Title screen: Continue, Level select, Pack, Level editor, Settings, Quit
Scripts/SaveGame.cs Progress in user://save.json: furthest level, best times, fewest falls
Scripts/UI/         Menu theme, level select, settings screen, pause menu, pack manager
Scripts/Editing/    Level editor: palette catalog, item helpers and checks, property panel, session kept across test plays
Scripts/Levels.cs   The pack and level being played and the run's total time
Scripts/LevelFormat/ Level and pack file formats (LevelFile), LevelBuilder (file to nodes), Packs (finding, importing, exporting)
Scripts/LevelInfo.cs Per-level settings on the built level's root: name, time limit, kill height
Scripts/Ball.cs     Camera-relative rolling controls
Scripts/CameraRig.cs Isometric orthographic follow camera, right-drag to orbit, wheel to zoom, optional follow-the-ball yaw
Scripts/Settings.cs Player preferences (camera and steering modes) in user://settings.cfg
Scripts/AutoPilot.cs Smoke test that drives the course by itself
Scripts/LevelGen/   Level generator: course sections chained into a level with an autopilot route
Scripts/Tools/      Command line tools for packs (Scenes/Tools.tscn); tools/*.sh drive them
Scripts/Hazards/    Moving obstacles: Mover (pistons, sliding bridges), Spinner (sweeper bars), Chaser (enemy steelies)
Shaders/            World-space grid for the course, the editor's build grid, two-tone ball
```

Tuning knobs: `timeLimit` and `killHeight` in each level file, and exported properties: `RollTorque`, `PushForce`, `AirControl` and `MaxSpeed` on the ball, and the camera's starting orthographic `size` on `CameraRig/Camera3D` (plus `MinZoom`, `MaxZoom`, `ZoomStep` and `MouseSensitivity` on `CameraRig`, and the `Follow*` knobs for follow mode).

## Smoke test

The autopilot drives the ball along the level's route and exits with code 0 when it reaches the goal (1 if time runs out). It runs headless, so it works in CI:

```sh
dotnet build
godot --headless --path . -- --autopilot             # level 1
godot --headless --path . -- --level 5 --autopilot   # one level
godot --headless --path . -- --autopilot --all       # every level in a row
```

For levels with timed hazards, a route waypoint can tell the autopilot to wait for a gate (an invisible box over the danger zone) to be `clear` of moving bodies (a piston pulls back) or `occupied` (a sliding bridge fills the gap), and a waypoint `speed` caps the pilot's speed for thin beams. See [docs/level-format.md](docs/level-format.md).

Add `--fixed-fps 60` to run the autopilot as fast as the machine allows; physics still steps 60 times per game second, so the results are the same. `tools/check-pack.sh [ID]` plays every level of a pack this way (the `main` pack takes under a minute). Run it before opening a pull request that changes levels or gameplay: to save CI minutes, the CI workflow only builds the game and loads every level of every built-in pack (`--validate`, see `Scripts/Tools/ToolRunner.cs`) without playing them.

## Level generator

`Scripts/LevelGen/` builds levels from a seed and a difficulty from 0 (gentlest) to 1 (hardest). It chains course sections together: runs, ramps, steps, a slalom, zig-zag beams, swaying planks, a sweeper bar, thin beams, a sliding bridge, pistons and enemy steelies. Each section appears from a set difficulty and gets narrower, steeper or faster as difficulty rises. The course always heads down and away from the camera, every level gets an autopilot route, and the same seed always gives the same level.

```sh
dotnet build
GODOT=godot-mono tools/build-pack.sh my-pack 20 1 0 1 "My pack"   # ID COUNT SEED FROM TO NAME
```

That writes `packs/my-pack/` with 20 levels whose difficulty rises from 0 to 1, then plays each one with the autopilot. A level the autopilot can't finish without falling is generated again from another seed, and every level's time limit is set from the autopilot's time on it: about 2.6 times as long on the easiest levels, down to 1.3 times on the hardest. The lower-level commands it uses are in `Scripts/Tools/ToolRunner.cs`.

## Linux release builds (AppImage)

`.github/workflows/release-linux.yml` exports a release build with Godot 4.7.2 .NET and packs it into a single-file AppImage that runs on most Linux desktops without installing anything:

- **Push a version tag** (`git tag v0.1.0 && git push origin v0.1.0`) and the AppImage is attached to a GitHub Release for that tag.
- **Run it by hand** from the Actions tab ("Linux AppImage" → "Run workflow") to build any branch. The AppImage is then a download on the run page under Artifacts (`chmod +x` it after downloading).

Either way the workflow plays the last level with the autopilot inside the built AppImage before publishing it. Release builds hide the "Next level" testing button.

To build one locally, install the Linux release export template for Godot 4.7.2 (Editor → Manage Export Templates), [appimagetool](https://github.com/AppImage/appimagetool/releases) and `desktop-file-utils`, then:

```sh
GODOT=godot-mono packaging/linux/build-appimage.sh dev   # writes build/Ball_Madness-dev-x86_64.AppImage
```

## Windows release builds

`.github/workflows/release-windows.yml` exports a single self-contained `Ball_Madness-<version>-windows-x86_64.exe` for 64-bit Windows 10/11. The game data, the C# assemblies and the .NET runtime are all embedded in the .exe, so there is nothing to install or unzip; on first launch the game unpacks its .NET files into `%APPDATA%\Godot\app_userdata\Ball Madness`.

- **Push a version tag** and the .exe is attached to the same GitHub Release as the AppImage.
- **Run it by hand** from the Actions tab ("Windows build" → "Run workflow") to build any branch. The .exe is then a download on the run page under Artifacts.

The export runs on Linux; a Windows runner then plays the last level with the autopilot in the built .exe before publishing it. The .exe is not code-signed, so Windows SmartScreen asks for confirmation the first time ("More info" → "Run anyway").

To build one locally, install the Windows export templates for Godot 4.7.2, then:

```sh
GODOT=godot-mono packaging/windows/build-windows.sh dev   # writes build/Ball_Madness-dev-windows-x86_64.exe
```
