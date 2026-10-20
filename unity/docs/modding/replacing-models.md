# Replacing world models with your own 3D models

> **`.glb` replacement is not wired in the current build; this page describes the intended layout
> (TASK-42).** The glTF converter exists (`GltfModelConverter`, `ModelConverterRegistry`), but
> nothing calls it: `WorldModelLoader` builds every model from the game's own data, and no code
> looks for a `Models/` folder. A `.glb` placed as described below is ignored today.

The aim is for BaK-Again to render a 3D model you made in Blender (or any glTF tool) in place of
any building, prop, or object in the world — the inn, a tree, a fence — **without touching the
original game files**: you drop a `.glb` into a folder and the engine loads it.

![A plain cube replacing the inn in zone Z01](images/inn-replaced-with-cube.png)

*From an earlier build, before the loader was reworked: a 1-unit `cube.glb` dropped in as the
inn. The current build does not do this (see the note above).*

## How it works

Every world model has a **zone** (the area it appears in, e.g. `Z01`) and a **name** (e.g.
`inn`). To replace one, you would put a model file at a matching path under your overrides folder:

```
<your Overrides folder>/Models/<zone>/<name>.glb
```

The intended behaviour: when the zone loads, the engine looks for that file first. If it is
there, your model is used; if not (or if the file is broken), the original game model is used.
Overrides are meant to be **per-zone**: the same name can mean different models in different
zones, so `Z01/inn.glb` would only affect Z01.

### Turning overrides on

Once wired, models are meant to come from the **dev overrides folder**: a mod zip cannot carry a
`.glb` (`bakmod validate` refuses it). The folder is set in **Preferences → Enhanced** (switch on
**Dev overrides**, point **Dev overrides folder** at the directory holding your `Models/` tree,
restart), but nothing reads `Models/` from it yet. (Under the hood: `OverrideEnabled` +
`OverridePath`. With the dev folder off and no mods, the game is the original.) It is the same
folder you point at while making a mod (see [making mods](README.md)), which holds the same folders
as inside a mod; models would go in one more, `Models/`, that a mod itself does not have.

### Finding the zone and name

Use the **Model Debug viewer** (an Editor-only tool: it is the "[dbg] Model Debug" entry of the
start-state picker in the Unity Editor, and is not in a built game): pick a zone from the dropdown
and step through its models. Each model shows its name and dimensions. The name you see is the name an
override file would use; the zone in the dropdown is the folder.

Zones are `Z01`–`Z12`, the alternate-state zones `Z10M`/`Z11M`/`Z12M`, and `COMBAT`.

## Authoring your model (intended)

The formats the unused converter reads are **`.glb`** and **`.gltf`**, through
[glTFast](https://github.com/Unity-Technologies/com.unity.cloud.gltfast) (the
`com.unity.cloud.gltfast` package). Export from Blender with the standard glTF exporter; glTFast
does the glTF→Unity axis conversion.

The alignment convention below was measured in an earlier build, when the loader still placed
`.glb` models. No placement path runs today, so treat it as the intended convention:

| Property | Convention |
|---|---|
| **Origin** | The model's origin `(0,0,0)` is placed at the object's ground point. Put your origin at the **base** of the model so it sits on the terrain (not floating or sunk). |
| **Up axis** | `+Y` is up (standard glTF). |
| **Scale** | **1 glTF unit = 1 game world unit.** The world is large — buildings are tens of units across, not 1. Check the original model's dimensions in the Model Debug viewer and match them, or your model will look tiny (like the 1-unit cube above) or huge. |
| **Materials** | Your model's own materials/textures are used as exported. (The original game's palette/fog shading is not applied to override models.) |

## Optional: keeping the original's metadata (`.json` sidecar) (intended)

No code reads a model sidecar today (`WorldModelMetadata` is built only from the game's own zone
table). The intended design: a pure look-swap is just the `.glb`, keeping all the original
object's properties (its type, flags, draw priority, vertex scale).

To change one of those, a `.json` sidecar **next to** the model with the same name (`<name>.json`)
would override **only the fields it lists**, everything else staying at the original's values:

```json
{
  "EntityType": 2,
  "EntityFlags": 64,
  "DrawPriority": 7,
  "VertexScale": 3
}
```

All four fields would be optional bytes (0–255), the object's name and id never overridable, and
a malformed sidecar ignored with a warning rather than breaking the model.

## Worked example: a cube for the inn (intended; does nothing in the current build)

1. Switch on Dev overrides and set the Dev overrides folder (Preferences → Enhanced), then
   restart.
2. Create `Models/Z01/` under it.
3. Put a `cube.glb` there named `inn.glb`: `Models/Z01/inn.glb`.
4. Enter the world in Z01. Once the loader is wired, a cube stands where the inn's buildings
   were, as in the earlier build's picture above; today the original inn is drawn.

Because the test cube is 1 unit, it would be tiny next to the world; a real replacement would be
scaled to the inn's footprint (see the dimensions in the Model Debug viewer).

## Safety net (intended)

Once wired: a missing override falls through to the original silently; a **corrupt or
unreadable** model logs a warning and falls back to the original game model. A broken `.glb`
should never crash the zone or leave a hole where the building should be.

## Coming later

The converter registry dispatches by file extension, so once it has a caller `.fbx` / `.obj`
support can be added without changing the override layout or your folder structure.
