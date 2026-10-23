# Ball Madness

A Marble Madness-style game built with **Godot 4.7 (.NET / C#)**. Roll the ball down an isometric obstacle course before the clock runs out. Desktop targets: Windows and Linux.

![Prototype level](docs/screenshot.png)

## Requirements

- [Godot 4.7 .NET edition](https://godotengine.org/download) (the "Godot Engine - .NET" build, not the standard one)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

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
| Restart | R               | Start            |

Movement is relative to the camera: "up" always rolls the ball towards the top of the screen, however the camera is turned.

## The prototype level

Start high on the blue pad and work your way down: a wide ramp, a narrow ramp with no rails, a zig-zag bridge, then a slalom ramp with posts before the green goal pad. You have 60 seconds. Fall off and you respawn at the last yellow checkpoint, but the clock keeps running while you fall.

## Project layout

```
Scenes/Main.tscn    Game root: environment, light, ball, camera rig, HUD
Scenes/Level.tscn   The course (CSG boxes with collision), checkpoints, goal, autopilot route
Scenes/Ball.tscn    The player ball (RigidBody3D)
Scripts/Game.cs     Countdown, timer, falling/respawn, checkpoints, goal
Scripts/Ball.cs     Camera-relative rolling controls
Scripts/CameraRig.cs Isometric orthographic follow camera, right-drag to orbit
Scripts/AutoPilot.cs Smoke test that drives the course by itself
Shaders/            World-space grid for the course, two-tone ball
```

Levels need a `Spawn` marker, `Area3D` nodes in the `checkpoint` and `goal` groups, and a `Route` node of `Marker3D` waypoints for the autopilot. Everything else is plain geometry, so the course can be edited directly in the Godot editor.

Tuning knobs are exported properties: `TimeLimit` and `KillHeight` on `Main`, `RollTorque`, `PushForce`, `AirControl` and `MaxSpeed` on the ball, and the camera's orthographic `size` on `CameraRig/Camera3D`.

## Smoke test

The autopilot drives the ball along the level's route and exits with code 0 when it reaches the goal (1 if time runs out). It runs headless, so it works in CI:

```sh
dotnet build
godot --headless --path . -- --autopilot
```
