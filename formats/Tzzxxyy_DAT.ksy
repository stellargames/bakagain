meta:
  id: tile_events
  file-extension: dat
  endian: le
doc: |
  Per-tile event/encounter triggers for one world tile, sliced by chapter.

  Filename: T<zz><xx><yy>.DAT (decimal zone, x, y).
  Total file size: 1920 bytes = 10 chapter blocks × 192 bytes.

  Per chapter block: u16 trigger count, then count × 19-byte trigger record.
  The DOS loader reads only `2 + count * 19` bytes per chapter — the
  remaining bytes of each 192-byte block are unloaded leftovers and may
  contain stale data; do not parse them.

  See docs/FileFormats/Tzzxxyy.DAT.md for full semantics (gating, type
  handlers, fieldA/field11 behavior).
seq:
  - id: chapters
    type: chapter_block
    size: 192
    repeat: eos
types:
  chapter_block:
    seq:
      - id: trigger_count
        type: u2
      - id: triggers
        type: trigger
        repeat: expr
        repeat-expr: trigger_count
  trigger:
    doc: |
      Fires when the player's sub-tile (x, y) is inside the rectangle and
      the gating keys allow it (see docs/FileFormats/Tzzxxyy.DAT.md).
    seq:
      - id: type
        type: u2
        enum: event_type
      - id: start_x
        type: u1
        doc: Sub-tile rectangle bounds (40×40 sub-tiles per world tile).
      - id: end_y
        type: u1
      - id: end_x
        type: u1
      - id: start_y
        type: u1
      - id: entry_number
        type: u4
        doc: Index into the def_<type>.dat file named by `type`.
      - id: field_a
        type: u1
        doc: |
          Boolean (only ever 0 or 1 in shipping data). Verified for Dial
          handler: when set, runs an extra cleanup step before
          SetGlobalFlag5200_5209. Effect on other types unverified.
      - id: required_key
        type: u2
        doc: |
          Save-state flag that must be set for trigger to fire (0 = no
          requirement). Checked via GetGlobalValue.
      - id: forbidden_key
        type: u2
        doc: |
          Save-state flag that must be unset for trigger to fire (0 = no
          suppression). Checked via GetGlobalValue.
      - id: set_on_fire_key
        type: u2
        doc: |
          Save-state flag set to 1 after the trigger fires (0 = no flag is
          set). Marks the event as completed.
      - id: field_11
        type: u2
        doc: |
          Boolean (only ever 0 or 1 in shipping data, only on Dial records).
          Verified for Dial handler: when set, returns early — skipping both
          the field_a cleanup and SetGlobalFlag5200_5209.
enums:
  event_type:
    # Each value names a def_<name>.dat file that entry_number indexes into.
    # 2 (comm), 4 (heal), 11 (bloc) are vestigial — the dispatcher in ovr187
    # never reaches their handlers and shipping data has zero records of
    # types 2 and 4. Type 5 (soun) has a handler but no shipping data.
    0: bkgr
    1: comb
    2: comm
    3: dial
    4: heal
    5: soun
    6: town
    7: trap
    8: zone
    9: disa
    10: enab
    11: bloc
