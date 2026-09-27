meta:
  id: symbol
  file-extension: dat
  endian: le
doc: |
  SYMBOL1.DAT .. SYMBOL6.DAT — the spell-selection layout for each of the six
  spell categories shown in the casting interface. Each file lists the castable
  spells in that category as positioned, clickable nodes: every node is one spell,
  drawn as a magic-symbol glyph (from SPELL.FNT) at a fixed screen position.

  Reversed from IDA ovr173: ReadSymbolsAndRingDat @0x6900c (parser),
  UI_drawSpellSymbols @0x69252 (renderer), UI_GetSymbolAtMouse @0x69192 (hit-test).
  See docs/FileFormats/SYMBOLx.DAT.md for full notes.
seq:
  - id: num_symbols
    type: u2
  - id: symbols
    type: symbol_node
    repeat: expr
    repeat-expr: num_symbols
types:
  symbol_node:
    doc: One castable spell node (7 bytes; IDA struct `spellSymbol`).
    seq:
      - id: spell_number
        type: u2
        doc: |
          Index into the spell table (SPELLS.DAT / pArrayOfSpellData). Passed to
          CanSpellBeCast; the node is only drawn/clickable when the spell is castable.
      - id: x_position
        type: u2
        doc: |
          X of the glyph centre, in 320x200 VGA space (node is a 10x10 px hit box centred here).
          The extractor scales this to the canonical 1600x1200 display space (x5).
      - id: y_position
        type: u2
        doc: |
          Y of the glyph centre, in 320x200 VGA space.
          The extractor scales this to the canonical 1600x1200 display space (x6).
      - id: font_glyph_minus_1
        type: u1
        doc: |
          SPELL.FNT glyph index minus 1. The engine increments this by 1 at load
          time (ReadSymbolsAndRingDat), so the glyph actually drawn = this + 1.
