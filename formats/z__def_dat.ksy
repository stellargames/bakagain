meta:
  id: z__def
  file-extension: dat
  endian: le
doc: |
  Zone definition file (Z##DEF.DAT) — 52 bytes. Defines physical properties
  of game zones including camera defaults, map zoom limits, and distance fog
  parameters for the RMP palette remap system.
seq:
  - id: zone_location
    type: s2
    doc: Zone type. 2 = underground/dungeon.
  - id: zone_pointer_struct_22
    type: s2
    doc: Struct pointer / zone ID.
  - id: default_camera_z
    type: u4
    doc: Default camera height (Z position) for the 3D world view.
  - id: default_camera_pitch
    type: u2
    doc: Default camera X rotation (pitch/tilt angle).
  - id: flags
    type: u2
    doc: >
      Bit 0: solid sky — SET = fill with sky_color, CLEAR = render Z##H.BMX horizon panorama.
      Bit 1: solid ground — SET = fill with ground_color, CLEAR = render textured ground from Z##L.SCX.
      Bit 2: track empty neighbors — SET = record positions of non-existent adjacent tiles during world tile loading.
      Bit 3: runtime only (not stored in file) — ground texture extends to screen bottom during camera transitions.
  - id: sky_color
    type: u1
    doc: VGA palette index for solid sky fill. Only used when flags bit 0 is set.
  - id: ground_color
    type: u1
    doc: VGA palette index for solid ground fill. Only used when flags bit 1 is set.
  - id: map_min_z
    type: u4
    doc: Minimum Z position for zone map view (maximum zoom-in limit).
  - id: camera_z_position
    type: u4
    doc: Saved camera Z position (restored on zone re-entry).
  - id: map_max_z
    type: u4
    doc: Maximum Z position for zone map view (maximum zoom-out limit). Also used as initial Z for locator spells.
  - id: map_zoom_step
    type: u4
    doc: Z change per single map zoom click (fast zoom = 5x this value).
  - id: num_rmp_resource_items
    type: s2
    doc: Count of RMP palette remap tables for distance fog.
  - id: sprite_fog_divisor
    type: s2
    doc: Distance divisor for sprite RMP remap index calculation. Signed; -1 disables fog.
  - id: sprite_fog_near_distance
    type: u4
    doc: Distance below which sprites render at full color (no fog).
  - id: unused_26
    type: u4
    doc: Loaded but never referenced in game code.
  - id: polygon_fog_divisor
    type: s2
    doc: Distance divisor for polygon/wall RMP remap index calculation. Signed; -1 disables fog.
  - id: polygon_fog_near_distance
    type: u4
    doc: Distance below which polygons render at full color (no fog).
  - id: far_clip_distance
    type: u4
    doc: Maximum render distance. Objects/polygons beyond this are not drawn.
