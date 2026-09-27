meta:
  id: traps
  file-extension: dat
  endian: le
doc: |
  TRAPS.DAT — per-combat-encounter tactical-grid layouts (actors, cannons, exit,
  crystal/diamond traps). A flat array of 768 fixed 62-byte records, seeked directly
  as encounterNumber * 62.

  Reversed from Load_traps.dat (seg033 @ 0x2e2ce): reads u16 count, then `count`
  4-byte elements ({i16 type, u8 grid_x, u8 grid_y}); the record is padded to a fixed
  62 bytes (2 + 15*4), so only the first `count` of the 15 slots are active (the rest
  are stale editor data). count is compared SIGNED, so values <= 0 yield no elements.
seq:
  - id: encounters
    type: encounter
    size: 62
    repeat: expr
    repeat-expr: 768
types:
  encounter:
    seq:
      - id: count
        type: u2
      - id: elements
        type: trap_element
        repeat: expr
        repeat-expr: 'count > 0 and count <= 15 ? count : 0'
  trap_element:
    seq:
      - id: type
        type: s2
        enum: element_type
      - id: grid_x
        type: u1
      - id: grid_y
        type: u1
    enums:
      element_type:
        # actors/cannons/exit are stored negative; loader dispatches on |type|.
        # actor placement uses combat slot |type| - 15.
        -18: clear_combat_flag
        -17: actor_slot_2
        -16: actor_slot_1
        -15: actor_slot_0
        -13: cannon_south
        -12: cannon_north
        -11: cannon_east
        -10: cannon_west
        -6: exit
        0: empty
        7: red_crystal
        8: green_crystal
        9: diamond_solid
        10: diamond_passthrough
