# Level and pack format

Levels are JSON files, grouped into packs. The in-game level editor reads and writes this format, so hand-written and editor-made levels mix freely. The game reads them with `Scripts/LevelFormat/Packs.cs` and builds the level's nodes with `Scripts/LevelFormat/LevelBuilder.cs`. The files hold numbers and names only; every type name maps to one of the game's own scripts, so a level can't run code.

Conventions used throughout:

- Positions and sizes are in metres. Y is up. A level usually starts high and works its way down.
- Vectors are `[x, y, z]` arrays.
- `rot` is a rotation in degrees, as Euler angles in Godot's YXZ order. Leave it out for no rotation. A ramp tilted down towards +Z is `"rot": [-20, 0, 0]`.
- Any field marked optional can be left out to get the default.
- Comments (`//`) and trailing commas are allowed.

## Packs

A pack is a folder:

```
my-pack/
  pack.json
  levels/
    01-warm-up.json
    02-the-drop.json
```

`pack.json`:

| Field | | |
|---|---|---|
| `format` | optional | File format version, currently `1`. |
| `id` | required | Lower case letters, digits and dashes. Unique; also the folder name for user packs. |
| `name` | optional | Shown to players. Defaults to the id. |
| `author`, `version`, `description` | optional | Shown to players. |
| `levels` | optional | Level file names inside `levels/`, in play order. Left out, every `levels/*.json` is played in file name order. |

Built-in packs live in `res://packs/`, players' packs in `user://packs/`. Save progress is kept per pack id.

A pack can be shared as a zip of its folder (or of its contents). Importing a zip (`Packs.ImportZip`) checks every file first and only copies `pack.json` and `levels/*.json`; level file names may only use letters, digits, `-`, `_`, `.` and spaces.

## Level file

```json
{
	"format": 1,
	"name": "First Descent",
	"timeLimit": 60,
	"killHeight": -4,
	"materials": { "grid": { "color": [0.55, 0.62, 0.75], "line": [0.18, 0.2, 0.3] } },
	"spawn": [0, 21.1, 0],
	"course": [ { "shape": "box", "pos": [0, 20, 0], "size": [8, 1, 8], "material": "grid" } ],
	"hazards": [],
	"checkpoints": [ { "pos": [0, 12, 30], "size": [8, 3, 2] } ],
	"goals": [ { "pos": [0, 2, 80] } ],
	"gates": [],
	"route": [ { "pos": [0, 21, 0] }, { "pos": [0, 2, 80] } ]
}
```

| Field | | |
|---|---|---|
| `format` | optional | `1`. A file with a newer format than the game knows is refused. |
| `name` | required | Shown on the HUD and the level select. |
| `author` | optional | |
| `difficulty` | optional | From 0 (gentlest) to 1 (hardest). Set by the level generator. |
| `generator` | optional | Generator version and seed of a generated level. |
| `timeLimit` | optional | Seconds on the clock. Default 60. |
| `killHeight` | optional | Falling below this height counts as a fall. Default -12. Put it a few metres under the lowest part of the course. |
| `materials` | optional | Named looks, see below. |
| `spawn` | required | Where the ball starts: put it about 1 m above the floor. |
| `course` | | Static parts, see below. |
| `hazards` | | Moving parts and enemies. |
| `checkpoints` | | Where the ball respawns after a fall. |
| `goals` | required | At least one finish pad. |
| `gates` | | Areas the autopilot watches, see `route`. |
| `route` | | Waypoints for the autopilot smoke test. Optional for user levels. |

### Materials

```json
"materials": {
	"grid":   { "color": [0.55, 0.62, 0.75], "line": [0.18, 0.2, 0.3] },
	"pusher": { "color": [0.85, 0.2, 0.45], "roughness": 0.5, "metallic": 0.1 }
}
```

With `line`, the part gets the course's grid shader (base colour plus 1 m grid lines). Without it, a plain surface with optional `roughness` (default 1) and `metallic` (default 0). Colours are RGB from 0 to 1. A part with no material, or an unknown one, is grey-blue grid.

### Parts

Used in `course` and inside hazards.

| Field | | |
|---|---|---|
| `name` | optional | Node name, handy when debugging. |
| `shape` | | `box` (default), `cylinder` or `sphere`. |
| `pos` | required | Centre of the part. |
| `rot` | optional | |
| `size` | box | `[width, height, depth]`. |
| `radius`, `height` | cylinder | Cylinders stand upright; `height` along Y. |
| `radius` | sphere | |
| `material` | optional | Key into `materials`. |
| `collide` | optional | `false` for decoration the ball passes through. |
| `visible` | optional | `false` for an invisible wall. |

A floor tile 1 m thick has its top surface at `pos.y + 0.5`.

### Hazards

All hazards have `name` (optional), `type`, `pos` and `rot` (optional). Movers and spinners are built from `parts`, placed relative to the hazard's `pos`.

**mover**: slides from where it's placed to `offset` away and back, easing at both ends. Moving platforms (the ball rides along), pistons and sliding bridges.

| Field | Default | |
|---|---|---|
| `offset` | `[0, 0, 4]` | How far it travels. |
| `period` | 4 | Seconds for a trip out and back. |
| `phase` | 0 | Where in the cycle it starts, 0 to 1 (0.5 starts at the far end). |

**spinner**: turns steadily. Sweeper bars and turntables; put the bar's part off-centre or make it long.

| Field | Default | |
|---|---|---|
| `speed` | 90 | Degrees per second; negative turns the other way. |
| `axis` | `[0, 1, 0]` | Axis in the hazard's own space. |

**chaser**: an enemy marble that rolls after the ball when it comes near and back home otherwise.

| Field | Default | |
|---|---|---|
| `radius` | 0.5 | |
| `chaseRadius` | 10 | How close the ball must come (from the chaser's home) before it chases. |
| `pushForce` | 6 | |
| `maxSpeed` | 5 | |
| `mass`, `friction`, `bounce` | 1.6, 0.9, 0.3 | |
| `material` | dark steel | |

### Checkpoints

```json
{ "pos": [0, 12, 30], "rot": [0, 90, 0], "size": [8, 3, 2] }
```

`pos` is on the floor; `size` is the trigger box (width, height, depth) standing on it, and a yellow stripe the same width and depth is drawn on the floor. Rolling into it makes it the respawn point (1 m above `pos`).

### Goals

```json
{ "pos": [0, 2, 80], "radius": 2 }
```

A green pad with a flag, on the floor at `pos`. `radius` defaults to 2.

### Route and gates

The autopilot (`-- --autopilot`) rolls the ball from waypoint to waypoint. Put waypoints along the middle of the course, at the ball's height, with the last one on the goal.

| Field | | |
|---|---|---|
| `pos` | required | |
| `speed` | optional | Caps the autopilot's speed on the way to this waypoint (default 6 m/s), for thin beams and tight corners. |
| `gate` | optional | Name of a gate. The autopilot waits at the previous waypoint until the gate is in the right state, then goes. |
| `waitFor` | optional | `clear` (default: nothing moving inside the gate) or `occupied` (a moving body has arrived, e.g. a sliding bridge). |

A gate is an invisible box: `{ "name": "Piston1Gate", "pos": [...], "size": [...] }`.
