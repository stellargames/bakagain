# CutScene system

The cutscene system loads an AnimatorResource, which contains a reference to an AnimationResource.
That in turn contains a list of Frames, which consist of a list of FrameCommands.

For every Frame (not to be confused with the frames drawn in the Update loop) the commands within it are executed. These commands load resources like palettes, images, sounds, screens, dialogs.
The Commands also draw the images, play the sounds, and show the dialogs.

For the drawing, the cutscene state maintains 4 ScreenBuffers:
0. Buffer_A
1. Buffer_B - This is the "DrawTexture" buffer, which is where the FrameCommands draw their images.
2. Buffer_C - This is the "background" buffer, which is copied to the DrawTexture buffer at the start of each frame.
3. Buffer_X

Every ScreenBuffer has a RenderTexture and maintains an Area specification.

The CutSceneState has pointers to the Background and DrawTexture buffers, as well as a "Target" buffer, which can be selected by FrameCommands, and which the CopyToTargetBuffer command copies an area of the DrawTexture buffer to.

It also has ImageSlots, which contain image sets, from which images can be taken to be drawn to buffers.

The Commands that effect these buffers are:
- CopyAreaBetweenBuffers({X}, {Y}, {Width}, {Height}, {SourceBuffer}, {DestinationBuffer})
    - Copies an area from one buffer to another.
- CopyToTargetBuffer({X}, {Y}, {Width}, {Height})
    - Copies an area from the DrawTexture buffer to the Target buffer.
- DisposeTargetBuffer()
    - Disposes the current Target buffer.
- DisposeCurrentBitmap()
    - Disposes the current image.
- DrawAreaFromBuffer({BufferNumber})
    - Draws the area from a buffer to the DrawTexture buffer.
- DrawBorder({X}, {Y}, {Width}, {Height})
    - Draws a border around an area of the DrawTexture buffer.
- DrawImage({X}, {Y}, {ImageNumber}, {ImageSlot})
    - Draws an image to the DrawTexture buffer.
- DrawImageFlippedHorizontally({X}, {Y}, {ImageNumber}, {ImageSlot})
    - Draws an image to the DrawTexture buffer, flipped horizontally.
- DrawImageFlippedVertically({X}, {Y}, {ImageNumber}, {ImageSlot})
    - Draws an image to the DrawTexture buffer, flipped vertically.
- DrawImageRotated({X}, {Y}, {ImageNumber}, {ImageSlot}, {Width}, {Height}, {Angle})
    - Draws an image to the DrawTexture buffer, rotated by an angle.
- DrawImageRotated180({X}, {Y}, {ImageNumber}, {ImageSlot})
    - Draws an image to the DrawTexture buffer, rotated by 180 degrees.
- DrawImageScaled({X}, {Y}, {ImageNumber}, {ImageSlot}, {Width}, {Height})
    - Draws an image to the DrawTexture buffer, scaled to specified dimensions.
- DrawImageFlippedHorizontallyScaled({X}, {Y}, {ImageNumber}, {ImageSlot}, {Width}, {Height})
    - Draws an image to the DrawTexture buffer, flipped horizontally, scaled to specified dimensions.
- DrawImageFlippedVerticallyScaled({X}, {Y}, {ImageNumber}, {ImageSlot}, {Width}, {Height})
    - Draws an image to the DrawTexture buffer, flipped vertically, scaled to specified dimensions.
- DrawImageRotated180Scaled({X}, {Y}, {ImageNumber}, {ImageSlot}, {Width}, {Height})
    - Draws an image to the DrawTexture buffer, rotated by 180 degrees, scaled by a factor.
- FillArea({X}, {Y}, {Width}, {Height})
    - Fills an area of the DrawTexture buffer with a color.
- LoadScreenResource('{Filename}')
    - Loads a screen resource into the DrawTexture buffer.
- SetTargetBuffer({BufferNumber})
    - Makes the specified buffer the active buffer.
- StoreArea({X}, {Y}, {Width}, {Height})
    - Copy an area from the DrawTexture buffer to the Background buffer.
- StoreScreen()
    - Copy the DrawTexture buffer to the Background buffer.


At the start of every Frame, the Background buffer is copied to the DrawTexture buffer.
At the end of every Frame, the DrawTexture is output to the player's Canvas.

## Image drawing
indexed image = an IndexedTexture with a palette texture and an index texture. This allows image effects by altering the palette.
direct image = an IndexedTexture with a Texture2D. No palette based effects are possible.

At the start of a frame the Background buffer is copied to the DrawTexture buffer

The frame commands draw both indexed and direct images to the buffers inside the cutscene state
We need to blend them together and render the final output to the screen

The drawing methods need to know if they are drawing an indexed or a direct image
Direct images are drawn to the direct buffers and indexed images are drawn to the indexed buffers.
And to preserve the order in which images are drawn, we need to draw transparent pixels in the direct buffer for every pixel written to the indexed buffer, except where the indexed pixel is transparent (0)

We render the indexed buffer to the output buffer first, using the palette to render the colors.
Then we blend the direct buffer (with transparent holes where the later indexed images were drawn) on top of the output buffer.
The result is the final output buffer, which we render to the screen.

## Palette colour-cycling (TTM 0x2302/0x2312/0x2322 + 0x2402)

The original game produces shimmer/sparkle effects (e.g. the glowing symbols in
TEMPLE.TTM) by VGA palette colour-cycling: a window of consecutive palette
entries is rotated one step per timer tick while the rendered image stays put.

Resolved from IDA (KRONDOR.EXE) — the relevant routines were renamed in the IDB:
- `paletteCycle_registerRange` (0x1909c) — stores a [start..end] window.
- `paletteCycle_tick` (0x19117) — rotates the active window(s) by ±1 per tick.
- `anim_registerPaletteCycle` (0x53a58) — the TTM-command handler for 0x2402.
- globals: `anim_paletteCycle_rangeStarts`/`rangeEnds`, `paletteCycle_active_flag`,
  `anim_paletteCycle_stepMagnitude`, `paletteCycle_activeRangeCount`.

Commands:
- `SetRange1/2/3({Start}, {End})` (opcodes 0x2302/0x2312/0x2322) — define palette
  cycle window 1/2/3 as the inclusive palette-index range [Start..End].
- `StartPaletteCycle({Range}, {Step})` (opcode 0x2402) — begin cycling the windows
  named by the `Range` bitmask (Range1=1, Range2=2, Range3=4). The **sign** of
  `Step` selects rotation direction; the original advances one entry per tick.

Remake implementation: `CutsceneState.SetPaletteCycles` / `AdvancePaletteCycles`
rotate a private cloned working copy of the palette (never the cached slot array)
and re-upload it; `CutsceneFrameProcessor` ticks the cycle during the frame hold
(`HoldWithPaletteCyclesAsync`) so the shimmer continues independent of frame
advancement, matching the timer-driven original.

### Open uncertainties / remaining unknowns (pick up later)

- **abs(Step) magnitude**: `StartPaletteCycle.Step`'s magnitude is stored to
  `anim_paletteCycle_stepMagnitude` but no static reader was found in the binary;
  per-tick advance appears fixed at ±1. The remake uses only the sign. To confirm
  whether magnitude matters: run under Spice86, breakpoint `paletteCycle_tick` /
  the DAC upload, patch a sample's Step (10→1), and observe the cycle speed.
- **Palette-cycle tick rate**: `PaletteCycleStepSeconds` in CutsceneFrameProcessor
  is a guess (1/18.2s, DOS PIT default). The true reprogrammed timer rate is
  unverified — confirm via the Spice86 session above.
- **Still-unknown frame commands** (routing established, semantics not):
  - 0xA000 group → `anim_handle_drawing`: `UnknownCommand0400` (9 uses),
    `UnknownCommandA014` (7), `UnknownCommandA034` (1), `UnknownCommandA094` (1),
    `UnknownCommandA0B5` (2).
  - 0xC000 group → `anim_handle_audio`: `UnknownCommandC061` (1 use).
  - `ObsoleteCommand0500` / `ObsoleteCommand0510` (1 use each).
- **Opcode 0x2022 (SetRandomFrameDuration)**: the interpreter handles it
  (`anim_frameDuration = param1 + rand() % (param2 - param1)`) but the **extractor
  does not parse it** — it would throw if encountered, though it appears in no
  current data file.

