meta:
  id: chapsong
  file-extension: dat
  endian: le
doc: |
  Chapter-book → song mapping. Loaded by `open_book?` (seg020:0x20d21)
  which reads a single i16 at offset `((chapter - 1) * 2 + (book - 1)) * 2`
  before playing the song and displaying `C{chapter}{book}.BOK`.

  Layout: 9 chapters × 2 books = 18 i16 song-id slots. A value of -999
  (0xFC19 little-endian) means "leave current song playing" — confirmed
  via `audio_song_sub_1505A` at 0x15072 which short-circuits on that
  sentinel and returns the currently-playing song unchanged. The shipping
  file has -999 for the Book-2 slot of chapters 2, 4, 6, 7 (no mid-chapter
  book interlude in those chapters).
seq:
  - id: entries
    type: chapter_song
    repeat: expr
    repeat-expr: 9
types:
  chapter_song:
    seq:
      - id: book1_song
        type: s2
        doc: Song id for C{n}1.BOK (chapter intro book). -999 = no change.
      - id: book2_song
        type: s2
        doc: Song id for C{n}2.BOK (mid-chapter book). -999 = no change.
