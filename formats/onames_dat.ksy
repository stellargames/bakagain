meta:
  id: onames_dat
  title: Betrayal at Krondor ONAMES.DAT (object name table)
  file-extension: dat
  endian: le
  encoding: ASCII
doc: |
  ONAMES.DAT is an indexed object-name table, shipped in KRONDOR.001 like every
  other game resource.

  It is a DISTINCT list from the names embedded in OBJINFO.DAT: it uses the game's
  all-caps-keyword presentation (e.g. "CRYSTAL Staff", "Scroll MAD GODS RAGE") and
  includes quest items / containers / trap states OBJINFO has no entry for. Of the
  73 names, only 4 are byte-identical to an OBJINFO name, 35 differ only in casing,
  and 34 are absent from OBJINFO entirely (verified 2026-06-19).

  Format: u2 count; (count+1) u2 offsets, each relative to the string base
  (= 2 + 2*(count+1)); then the NUL-terminated strings. The final (count+1)th
  offset is an end sentinel, not a name.
seq:
  - id: count
    type: u2
  - id: offsets
    type: u2
    repeat: expr
    repeat-expr: count + 1
    doc: Offsets relative to the string base; the last entry is an end sentinel.
  - id: strings
    type: strz
    repeat: eos
instances:
  string_base:
    value: 2 + 2 * (count + 1)
    doc: File offset of the string block; name i = string_base + offsets[i].
