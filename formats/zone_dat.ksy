meta:
  id: zone
  file-extension: dat
  endian: le
doc: 3D viewport rectangle — defines the screen region where the 3D world is rendered,
  | leaving space for the HUD border. Loaded by Load_zone_dat (0x73070) into the
  | renderViewDescriptor.viewport area fields. Default 320x200 is overridden to this
  | inset (13, 11, 294, 101).
seq:
  - id: x_offset
    type: u2
  - id: y_offset
    type: u2
  - id: width
    type: u2
  - id: height
    type: u2
