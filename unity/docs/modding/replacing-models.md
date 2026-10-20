# Replacing world models with your own 3D models

> **`.glb` replacement is not wired in the current build; this page describes the intended layout
> (TASK-42).** The glTF converter exists (`GltfModelConverter`, `ModelConverterRegistry`), but
> nothing calls it: `WorldModelLoader` builds every model from the game's own data, and no code
> looks for a `Models/` folder. A `.glb` placed as described below is ignored today.

BaK-Again can render a 3D model you made in Blender (or any glTF tool) in place of any
building, prop, or object in the world — the inn, a tree, a fence — **without touching the
original game files**. The intent: you drop a `.glb` into a folder and the engine loads it.

![A plain cube replacing the inn in zone Z01](images/inn-replaced-with-cube.png)

*From an earlier build, before the loader was reworked: a 1-unit `cube.glb` dropped in as the
inn. The current build does not do this (see the note above).*

## How it works

Every world model has a **zone** (the area it appears in, e.g. `Z01`) and a **name** (e.g.
`inn`). To replace one, you put a model file at a matching path under your overrides folder:

```
<your Overrides folder>/Models/<zone>/<name>.glb
```

The intended behaviour: when the zone loads, the engine looks for that file first. If it is
there, your model is used; if not (or if the file is broken), the original game model is used. Overrides are **per-zone**:
the same name can mean different models in different zones, so `Z01/inn.glb` only affects Z01.

### Turning overrides on

Once wired, models are meant to come from the **dev overrides folder**: a mod zip cannot carry a
`.glb` (`bakmod validate` refuses it). The folder is set in **Preferences → Enhanced** (switch on
**Dev overrides**, point **Dev overrides folder** at the directory holding your `Models/` tree,
restart), but nothing reads `Models/` from it yet. (Under the hood: `OverrideEnabled` + `OverridePath`. With the dev folder off and no mods,
the game is the original.) It is the same folder, with the same layout, as a mod under development
(see [making mods](README.md)); models just live under `Models/`.

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

## Worked example: a cube for the inn (intended; does nothing in the current build)

1. Switch on Dev overrides and set the Dev overrides folder (Preferences → Enhanced), then restart.
2. Create `Models/Z01/` under it.
3. Put a `cube.glb` there named `inn.glb`: `Models/Z01/inn.glb`.
4. Enter the world in Z01. Once the loader is wired, a cube stands where the inn's buildings
   were, as in the earlier build's picture above; today the original inn is drawn.

Because the test cube is 1 unit, it's tiny next to the world; a real replacement would be
scaled to the inn's footprint (see the dimensions in the Model Debug viewer).

## Safety net (intended)

Once wired: a missing override falls through to the original silently; a **corrupt or unreadable** model
logs a warning and falls back to the original game model. A broken `.glb` never crashes the
zone or leaves a hole where the building should be.

## Coming later

The converter registry dispatches by file extension, so once it has a caller `.fbx` / `.obj`
support can be added without changing the override layout or your folder structure.
