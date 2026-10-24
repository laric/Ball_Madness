# Level and pack format

Levels are JSON files, grouped into packs. The in-game level editor reads and writes this format, so hand-written and editor-made levels mix freely. The game reads them with `Scripts/LevelFormat/Packs.cs` and builds the level's nodes with `Scripts/LevelFormat/LevelBuilder.cs`. The files hold numbers and names only; every type name maps to one of the game's own scripts, so a level can't run code.

Conventions used throughout:

- Positions and sizes are in metres. Y is up. A level usually starts high and works its way down.
- Vectors are `[x, y, z]` arrays.
- `rot` is a rotation in degrees, as Euler angles in Godot's YXZ order. Leave it out for no rotation. A ramp tilted down towards +Z is `"rot": [20, 0, 0]`.
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
| `hazards` | | Moving parts, enemies and mechanics (conveyors, ice, bumpers, holes, one-way gates, teleporters). |
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

### Mechanics

Mechanics are entries in `hazards` too, with their own `type`. Conveyors, ice and bumpers are built from `parts` like movers are; holes, one-way gates and teleporters have no parts and stand on the floor at `pos`. Every mechanic can be turned with `rot`.

**conveyor**: a belt that carries the ball along the hazard's +Z (turn it with `rot` to aim it). The belt pulls the ball's speed along it towards the belt's speed, gently enough that the player can still steer across or against it. Its parts always get the moving-chevron belt look; their `material` is ignored.

| Field | Default | |
|---|---|---|
| `speed` | 4 | Belt speed in m/s; negative runs it backwards. |

**ice**: a frictionless surface. On ice the ball slides instead of rolling and the player has under a third of the usual push, so turning and stopping take a long time. Tilt it for an icy ramp. Parts without a `material` get an icy look.

**bumper**: a post that kicks the ball away from its centre line at `force` m/s (or faster, if the ball came in faster) and pulses. A cylinder part standing on the floor is the usual shape. Parts without a `material` get a cyan look.

| Field | Default | |
|---|---|---|
| `force` | 10 | Speed the ball leaves at, in m/s. |

**hole**: a round hole cut out of every solid course part (box or cylinder) under `pos`, which is the hole's centre on the floor's top surface. The ball falls through. With a `period` it's a trapdoor: a lid covers the hole, flashes red, then shrinks away for part of each cycle. Holes don't cut parts of other hazards or mechanics.

| Field | Default | |
|---|---|---|
| `radius` | 1.2 | The ball is 0.5. |
| `depth` | 3 | How far down the cut goes. |
| `period` | none | Seconds for one open-and-close cycle. Leave it out for a hole that's always open. |
| `open` | 0.5 | Fraction of the cycle the trapdoor is open, from 0 to 1. |
| `phase` | 0 | Where in the cycle it starts, 0 to 1. At 0 the trapdoor starts opening. |

For the autopilot, put a gate on the lid and wait for it with `"waitFor": "occupied"`: the lid counts as a moving body while it's closed, so the pilot sets off the moment the trapdoor shuts.

**oneway**: a swing door across the course that the ball can only pass along the hazard's +Z (the way the floor arrows point). From the other side it's a wall. The posts at its sides are always solid.

| Field | Default | |
|---|---|---|
| `width` | 4 | Width of the door. Posts stand just outside it. |
| `height` | 1.2 | |

**teleporter**: a glowing pad that sends the ball to `target`. The ball comes out 1 m above that point, stopped, unless `keepSpeed` is set. A faint ring marks the exit. The autopilot carries on from the nearest waypoint it hasn't passed yet, so put a waypoint on the pad and the next one beyond the exit.

| Field | Default | |
|---|---|---|
| `target` | 8 m along +Z from `pos` | Exit point on the floor, in level space. |
| `radius` | 1 | The ball has to roll well onto the pad. |
| `keepSpeed` | false | Keep the ball's speed and spin through the jump. |

```json
{ "type": "conveyor", "pos": [0, -0.5, 10], "rot": [0, 90, 0], "speed": 3, "parts": [ { "shape": "box", "size": [4, 1, 8] } ] },
{ "type": "ice", "pos": [0, -0.5, 20], "parts": [ { "shape": "box", "size": [6, 1, 10] } ] },
{ "type": "bumper", "pos": [1.5, 0.5, 30], "force": 9, "parts": [ { "shape": "cylinder", "radius": 0.8, "height": 1 } ] },
{ "type": "hole", "pos": [0, 0, 40], "radius": 1.3, "period": 4, "open": 0.4 },
{ "type": "oneway", "pos": [0, 0, 50], "width": 4 },
{ "type": "teleporter", "pos": [0, 0, 60], "target": [0, -3, 75] }
```

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
