meta:
  id: grid
  file-extension: dat
  endian: le
doc: |
  GRID.DAT — per-zone combat-grid border colour. 12 u16 values, one pen index per
  zone, indexed by zoneNumber-1.

  Reversed from Load_grid (seg033 @ 0x2e7ee): seeks to (currentZoneNumber-1)*2 and
  reads a single u16 into grid_dat_word_3E840. drawCombatGridOutline (0x2e8a4) then
  outlines the playable tactical-grid region with that colour. The value is a palette
  pen index into the zone's combat palette (not RGB).
seq:
  - id: zone_border_pens
    type: u2
    repeat: expr
    repeat-expr: 12
