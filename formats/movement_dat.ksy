meta:
  id: movement
  file-extension: dat
  endian: le
doc: |
  MOVEMENT.DAT — the lookup table that turns the player's movement-speed and
  turn-speed preferences into the scalars used by the world-walk loop.

  Three arrays of three u16, one entry per speed preset (index 0/1/2 =
  small/medium/large). Reversed from Load_movement.dat (ovr184 @ 0x71790):
    - step_size[pref.step_size]  -> forward distance per step, in game units
                                    (1600 = one sub-tile).
    - turn_angle[pref.turn_size] -> heading rotation per discrete turn, in
                                    16-bit angle units (full circle = 0x10000;
                                    1024/2048/4096 => 5.625/11.25/22.5 deg).
    - step_time[pref.step_size]  -> NOT a turn. The loader multiplies by 30 and
                                    the main loop adds it to GameTimeIn2Seconds
                                    (sub_ovr131_8E7) -> game-time advanced per
                                    step. Raw 1/2/4 => 60/120/240 real seconds.
                                    Indexed by the STEP-size preference (time per
                                    step scales with distance per step), and
                                    quartered underground at runtime.
seq:
  - id: step_size
    type: u2
    repeat: expr
    repeat-expr: 3
    doc: Forward distance per step (game units), indexed by step-size preference.
  - id: turn_angle
    type: u2
    repeat: expr
    repeat-expr: 3
    doc: Heading rotation per turn (16-bit angle units), indexed by turn-size preference.
  - id: step_time
    type: u2
    repeat: expr
    repeat-expr: 3
    doc: Game-time per step (raw; engine x30 -> 2s ticks), indexed by step-size preference.
