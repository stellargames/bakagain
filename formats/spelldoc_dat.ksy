meta:
  id: spelldoc
  file-extension: dat
  endian: le
doc: |
  SPELLDOC.DAT — indexed spell-description string table (name / cost / duration / effect
  lines shown in the spell UI). Reversed from Load_spells (ovr173 @ 0x66700).

  u16 count; u32 offset[count] (byte offsets into the blob); u16 declared_size; then the
  NUL-terminated string blob. NOTE: declared_size in the shipped file is the WHOLE file
  size (4003), not the blob length — the game over-allocates and the read stops at EOF, so
  the real blob runs from the end of the header to EOF. Many entries share an offset (e.g.
  the empty separator string).
seq:
  - id: count
    type: u2
  - id: offsets
    type: u4
    repeat: expr
    repeat-expr: count
  - id: declared_size
    type: u2
  - id: blob
    size-eos: true
instances:
  descriptions:
    io: _root._io
    pos: offsets[_index] + sizeof<u2> + count * sizeof<u4> + sizeof<u2>
    type: strz
    encoding: ascii
    repeat: expr
    repeat-expr: count
