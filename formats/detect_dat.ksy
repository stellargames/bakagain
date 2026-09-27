meta:
  id: detect
  file-extension: dat
  endian: le
doc: |
  DETECT.DAT — world-item interaction-detection range table (sibling of FILTER.DAT).

  Two 172-byte blocks: block 0 = aboveground, block 1 = underground. Each block is
  43 signed 32-bit detection ranges indexed by the entity's TableDatInfo.EntityType
  tag (TBL DAT section +0x01, range 0..42).

  Reversed from Load_detect.dat (ovr190 @ 0x76510), which fseeks to offset 172 when
  zoneLocation == underground and reads 43 dwords into data_detect_dat (0x3f1c8).
  The render/detect loop (sub_seg021_38D @ 0x21a9d) marks a visible item as
  interactable when distanceToPlayer <= range[EntityType]; HandleEnvironmentInteraction
  (ovr190 0x76573) consumes the same table.

  A range of 0 means the entity type is never interactable (decorative). Ranges are
  in game units (64000 per tile) and are shorter underground.
  See docs/FileFormats/DETECT.DAT.md for the EntityType -> category mapping
  (shared with FILTER.DAT).
seq:
  - id: locations
    type: location_block
    repeat: eos
types:
  location_block:
    seq:
      - id: detect_ranges
        type: s4
        repeat: expr
        repeat-expr: 43
        doc: Interaction-detection range per entity-type tag (index 0..42).
