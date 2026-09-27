meta:
  id: z__shp
  file-extension: dat
  imports:
    - creature_name
  endian: le
doc: Zone monster roster — 4 creature spawn slots per chapter (9 chapters).
  | Despite the SHP filename, this is NOT geometry/shape data. It defines which
  | monster types can appear in random encounters for each zone per chapter.
  | CreatureType -1 (0xFFFF) = empty slot (no monsters for that chapter).
  | Loaded by Load_ZxxSHP_DAT (0x7369e). 80 bytes total = 10 × 4 × 2 bytes
  | (one block per chapter slot; chapters 1-9 are used, the 10th block is empty.
  | ZoneShapeExtractor reads only the first 9).
seq:
  - id: chapters
    type: creatures
    repeat: eos
types:
  creatures:
    seq:
      - id: slot1
        type: creature_name
      - id: slot2
        type: creature_name
      - id: slot3
        type: creature_name
      - id: slot4
        type: creature_name
