meta:
  id: fmap_xy
  file-extension: dat
  endian: le
doc: |
  FMAP_XY.DAT — per-tile "you are here" marker positions on the world-map screen.
  Reversed from Load_fmap_xy.dat (ovr183 @ 0x71634).

  Exactly 12 zones (zone numbers 1..12). For the player's current zone and world
  tile, the loader returns positions[currentZone-1].position[currentTile] as the
  fullmap marker location. A value of (-1, -1) means the tile has no marker and
  the position indicator is suppressed. All coordinates are FULLMAP.SCX pixels.
seq:
  - id: zones
    type: zone
    repeat: expr
    repeat-expr: 12
types:
  zone:
    seq:
      - id: num_tiles
        type: u2
      - id: positions
        type: tile_position
        repeat: expr
        repeat-expr: num_tiles
  tile_position:
    seq:
      - id: x
        type: s2
      - id: y
        type: s2
