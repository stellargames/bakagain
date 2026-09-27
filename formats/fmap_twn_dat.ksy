meta:
  id: fmap_twn
  file-extension: dat
  endian: le
doc: |
  FMAP_TWN.DAT — town labels drawn on the world-map screen (FULLMAP.SCX).
  Reversed from Load_fmap_twn.dat (ovr183 @ 0x713cd).

  The four header words are shared icon/label geometry constants (3,3,9,9 in the
  shipped file): (icon_width, icon_height) = (9,9) is the FMAP_ICN footprint used
  by the mouse hit-test (fmap_getTownIconAtMouse @ 0x715b2), and
  (icon_anchor_x, icon_anchor_y) = (3,3) is the centring anchor (UI_full_map
  draws each label centred at x + icon_anchor_x/2, one label-height above y).

  Each town's (x, y) is a position in FULLMAP.SCX pixel space (320x200).
seq:
  - id: icon_anchor_x
    type: u2
  - id: icon_anchor_y
    type: u2
  - id: icon_width
    type: u2
  - id: icon_height
    type: u2
  - id: num_town
    type: u2
  - id: town
    type: town
    repeat: expr
    repeat-expr: num_town
types:
  town:
    seq:
      - id: str_len
        type: u2
      - id: name
        type: str
        encoding: ascii
        size: str_len
        doc: NUL-terminated; the terminator is included in str_len.
      - id: x
        type: s2
        doc: Label X in FULLMAP.SCX pixels.
      - id: y
        type: s2
        doc: Label Y in FULLMAP.SCX pixels.
