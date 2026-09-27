meta:
  id: ring
  file-extension: dat
  endian: le
doc: |
  RING.DAT — the 30 positions of the spell-casting ring. The casting interface
  arranges its icons around a fixed elliptical ring of 30 slots; this file stores
  their pixel coordinates as two parallel arrays: all 30 X coords, then all 30 Y
  coords. Every 5th slot (indices 4, 9, 14, 19, 24, 29) is a spell-category anchor
  (6 categories x 5 = 30), one per SYMBOL<n>.DAT.

  Coordinates are in the original 320x200 VGA casting screen; the extractor scales
  them to the canonical 1600x1200 display space (x*5, y*6).

  Reversed from IDA ovr173: ReadSymbolsAndRingDat @0x6900c (parser),
  UI_drawCastRingIcons @0x6910e (renderer), UI_GetRingPositionAtMouse @0x69690
  (hit-test). See docs/FileFormats/RING.DAT.md.
seq:
  - id: x
    type: u2
    repeat: expr
    repeat-expr: 30
    doc: X coordinate of each of the 30 ring positions (320x200 VGA space).
  - id: y
    type: u2
    repeat: expr
    repeat-expr: 30
    doc: Y coordinate of each of the 30 ring positions (320x200 VGA space).
