meta:
  id: encamp
  file-extension: dat
  endian: le
doc: |
  ENCAMP.DAT — geometry for the encampment / "rest" screen (ENCAMP.SCX).
  Reversed from Load_encamp (ovr182 @ 0x7087e). Structurally a twin of FMAP_TWN.DAT.

  Four icon-geometry words (anchorX, anchorY, width, height = 3,3,9,9) define the
  clickable hit-box around each clock entry; the hit-test (0x70b2f) builds
  [x-(width-anchorX)/2 .. +width] x [y-(height-anchorY)/2 .. +height].

  clock_entries (24): clickable hour positions on the rest dial.
  needle_entries (27): dial needle/hand vertex positions (0x70c9b draws the hand).

  Coordinates are 320x200 mode-13h pixels on disk (the extractor scales them to the
  canonical 1600x1200 space, x5/x6, to match ENCAMP.SCX).
seq:
  - id: icon_anchor_x
    type: u2
  - id: icon_anchor_y
    type: u2
  - id: icon_width
    type: u2
  - id: icon_height
    type: u2
  - id: num_clock_entries
    type: u2
  - id: clock_entries
    type: point
    repeat: expr
    repeat-expr: num_clock_entries
  - id: num_needle_entries
    type: u2
  - id: needle_entries
    type: point
    repeat: expr
    repeat-expr: num_needle_entries
types:
  point:
    seq:
      - id: x
        type: u2
      - id: y
        type: u2
