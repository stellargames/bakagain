meta:
  id: filter
  file-extension: dat
  endian: le
doc: |
  FILTER.DAT — world-item draw-distance / culling table.

  One 172-byte block per graphics detail-level preference (config.levelOfDetail).
  Each block is 43 signed 32-bit draw-distance thresholds indexed by the entity's
  TableDatInfo.EntityType tag (TBL DAT section +0x01, range 0..42).

  Reversed from Load_filter_dat (ovr136 @ 0x43f60), which fseeks to
  levelOfDetail * 0xAC and reads 43 dwords into filterData_43dwords (0x3eae0).
  The four ovr136 consumers keep a world item when
  distanceToPlayer - (Extent << VertexScale) < threshold[EntityType].

  Threshold sentinels (units = game units, 64000 per tile):
    -1 = entity type never drawn
     1 = always drawn (distance test bypassed)
    >1 = drawn only when within the distance.
  See docs/FileFormats/FILTER.DAT.md for the EntityType -> category mapping.
seq:
  - id: detail_levels
    type: detail_level_block
    repeat: eos
types:
  detail_level_block:
    seq:
      - id: draw_distances
        type: s4
        repeat: expr
        repeat-expr: 43
        doc: Culling distance per entity-type tag (index 0..42).
