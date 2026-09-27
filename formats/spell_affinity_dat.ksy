meta:
  id: spell_affinity
  file-extension: dat
  endian: le
doc: |
  Shared format of SPELLWEA.DAT (creature weaknesses) and SPELLRES.DAT (creature
  resistances): per creature type, a bitmask of the spells it is weak to / resists.

  Reversed from Load_spell_weakness_and_resistance (ovr177 @ 0x6b4dc) and the lookups
  check_spell_weakness (0x6b5db) / check_spell_resistance (0x6b595):
  test = mask[creatureType*3 + spell/16] & (1 << (spell % 16)).
  Every caller passes the creature type first (Cast_Evil_Seek @0x673bc), so a row is a
  creature type (64 = the mnames count) and its 48 bits are spells. See
  docs/FileFormats/SPELLWEA_SPELLRES.DAT.md.
seq:
  - id: creature_count
    type: u2
  - id: creatures
    type: creature_row
    repeat: expr
    repeat-expr: creature_count
types:
  creature_row:
    seq:
      - id: spell_mask
        type: u2
        repeat: expr
        repeat-expr: 3
        doc: 48-bit spell bitmask (word w, bit b -> spell w*16 + b)
