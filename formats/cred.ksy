meta:
  id: cred
  file-extension: dat
  title: Betrayal at Krondor — intro credits text (CRED.DAT)
  endian: le
  encoding: ASCII
doc: |
  Text for the scrolling intro credits, loaded by LoadCRED.DAT (KRONDOR.EXE
  0x40520) and scrolled by ShowCredits/scrollCredits (0x40934 / 0x405f1) over
  the BLANK.SCX parchment.

  A header gives a count and one offset per string, followed by a blob of
  NUL-terminated strings; each offset is the byte position of a string within
  that blob. Entry [0] is the centered title ("CREDITS"); the remaining entries
  are consumed two at a time as (role, name) line-pairs — e.g.
  ("PROGRAMMING:", "Steve Cordon"). A blank role is a continuation name under
  the previous role; an empty pair is a spacer.
seq:
  - id: count
    type: u2
    doc: Number of strings (427 in the shipped file).
  - id: offsets
    type: u2
    repeat: expr
    repeat-expr: count
    doc: Byte offset of each string within `text`.
  - id: blob_size
    type: u2
  - id: text
    size: blob_size
    doc: |
      `count` NUL-terminated strings packed back to back. Index into this with
      `offsets[i]` to read string i.
