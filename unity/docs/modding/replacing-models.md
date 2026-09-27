# Replacing world models with your own 3D models

BaK-Again can render a 3D model you made in Blender (or any glTF tool) in place of any
building, prop, or object in the world — the inn, a tree, a fence — **without touching the
original game files**. You drop a `.glb` next to the game and the engine loads it.

![A plain cube replacing the inn in zone Z01](images/inn-replaced-with-cube.png)

*The worked example below: a 1-unit `cube.glb` dropped in as the inn — the engine loaded it
in place of the original building.*

## How it works

Every world model has a **zone** (the area it appears in, e.g. `Z01`) and a **name** (e.g.
`inn`). To replace one, you put a model file at a matching path under your overrides folder:

```
<your Overrides folder>/Models/<zone>/<name>.glb
```

When the zone loads, the engine looks for that file first. If it's there, your model is used;
if not (or if the file is broken), the original game model is used. Overrides are **per-zone**:
the same name can mean different models in different zones, so `Z01/inn.glb` only affects Z01.

### Turning overrides on

In **Preferences**, enable overrides and point the overrides folder at the directory holding
your `Models/` tree. (Under the hood: `OverrideEnabled` + `OverridePath`. With overrides off,
the game is byte-for-byte the original.) This is the same `Overrides/` folder used for SCX/BMX
art replacement — models just live under `Models/`.

### Finding the zone and name

Use the **Model Debug viewer** (a dev tool in the game): pick a zone from the dropdown and step
through its models. Each model shows its name and dimensions. The name you see is the name your
override file must use; the zone in the dropdown is the folder.

Zones are `Z01`–`Z12`, the alternate-state zones `Z10M`/`Z11M`/`Z12M`, and `COMBAT`.

## Authoring your model

Supported formats today: **`.glb`** and **`.gltf`** (loaded with [glTFast](https://github.com/Unity-Technologies/com.unity.cloud.gltfast), the `com.unity.cloud.gltfast` package).
Export from Blender with the standard glTF exporter — glTFast handles the glTF→Unity axis
conversion for you.

Alignment convention (verified against the engine's placement):

| Property | Convention |
|---|---|
| **Origin** | The model's origin `(0,0,0)` is placed at the object's ground point. Put your origin at the **base** of the model so it sits on the terrain (not floating or sunk). |
| **Up axis** | `+Y` is up (standard glTF). |
| **Scale** | **1 glTF unit = 1 game world unit.** The world is large — buildings are tens of units across, not 1. Check the original model's dimensions in the Model Debug viewer and match them, or your model will look tiny (like the 1-unit cube above) or huge. |
| **Materials** | Your model's own materials/textures are used as exported. (The original game's palette/fog shading is not applied to override models.) |

## Optional: keeping the original's metadata (`.json` sidecar)

A pure look-swap is just the `.glb` — the engine keeps all the original object's properties
(its type, flags, draw priority, vertex scale) by default.

If you need to change one of those, drop a `.json` sidecar **next to** the model with the same
name (`<name>.json`). It overrides **only the fields you list**; everything else stays at the
original's values:

```json
{
  "EntityType": 2,
  "EntityFlags": 64,
  "DrawPriority": 7,
  "VertexScale": 3
}
```

All four fields are optional bytes (0–255). The object's name and id are never overridable. A
malformed sidecar is ignored (with a warning) — it never breaks the model.

## Worked example: a cube for the inn

1. Enable overrides and set your overrides folder (Preferences).
2. Create `Models/Z01/` under it.
3. Put a `cube.glb` there named `inn.glb`: `Models/Z01/inn.glb`.
4. Enter the world in Z01 — a cube stands where the inn's buildings were (above).

Because the test cube is 1 unit, it's tiny next to the world; a real replacement would be
scaled to the inn's footprint (see the dimensions in the Model Debug viewer).

## Safety net

A missing override falls through to the original silently; a **corrupt or unreadable** model
logs a warning and falls back to the original game model. A broken `.glb` never crashes the
zone or leaves a hole where the building should be.

## Coming later

The loader dispatches by file extension through a small converter registry, so `.fbx` / `.obj`
support can be added without changing the override layout or your folder structure.
