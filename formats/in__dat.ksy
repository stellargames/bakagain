meta:
  id: in_
  file-extension: dat
  endian: le
doc: |
  IN_*.DAT — "input form" layouts: a list of labelled input/list-box widgets used by
  the original game's EDITOR tools. Reversed from Load_in_file (ovr140 @ 0x469f0).

  Of the ten shipped files only IN_SAVE.DAT is loaded by the shipped game (its
  "Directories"/"Games" list columns in dialog_SaveGame); the others have no loader
  and are build-time editor sources.

  Each field: a list/input box (item_count rows; box at x,y,width; height computed from
  the font at runtime) plus a caption string drawn at (label_x, label_y). Coordinates are
  320x200 mode-13h pixels.
seq:
  - id: string_pool_len
    type: u2
  - id: string_pool
    size: string_pool_len
  - id: field_count
    type: u2
  - id: fields
    type: field
    repeat: expr
    repeat-expr: field_count
types:
  field:
    seq:
      - id: item_count
        type: u2
      - id: x
        type: u2
      - id: y
        type: u2
      - id: width
        type: u2
      - id: style
        type: u1
        repeat: expr
        repeat-expr: 5
      - id: label_offset
        type: u2
        doc: byte offset into string_pool (0xFFFF = no caption)
      - id: label_x
        type: u2
      - id: label_y
        type: u2
      - id: alloc_flag
        type: u2
    instances:
      label:
        pos: label_offset + 2
        type: strz
        encoding: ascii
        if: label_offset != 0xffff
