meta:
  id: monst
  file-extension: dat
  endian: le
doc: |
  Monster statistics file (MONST##.DAT). The number in the filename corresponds
  to the creature type ID. Each stat is stored as (min, max) — a random value
  between min and max is rolled when combat begins.

  The last 4 fields control combat AI behaviour. They are NOT an
  attack/defense/movement split — that was a guess, and the code contradicts it.
  monstat_load (MONSTAT.C) reads them straight into pad_e[1], pad_e[2], pad_e[3]
  and pad_e[0], in that order, and monster_combatTurn (ovr169 0x64501) picks
  between the first three by the creature's CAPABILITY, in this cascade:

  - spellcast_pattern: pad_e[1], used when the creature can cast — indexes
    AI_TURN_INDEX in the spell-attempt loop (CBTAI.C:363)
  - crossbow_pattern:  pad_e[2], the next tier down — indexes the encounter AI
    action table in combataiturn_take_actor_turn (CBTAITRN.C:351)
  - melee_move_pattern: pad_e[3], the fallback — indexes AI_ACTION_INDEX
    (CMBTAI.C:501)
  - flee_threshold: pad_e[0], morale (CBENC.C: morale_adj = 8 - pad_e[0],
    0xff = never flees)

  MonsterStatsExtractor.cs has always used these names; this spec was the one
  that drifted.
seq:
  - id: health
    type: min_max
  - id: stamina
    type: min_max
  - id: speed
    type: min_max
  - id: strength
    type: min_max
  - id: accuracy_crossbow
    type: min_max
  - id: accuracy_melee
    type: min_max
  - id: accuracy_casting
    type: min_max
  - id: defense
    type: min_max
  - id: spellcast_pattern
    type: min_max
    doc: pad_e[1]. Caster AI pattern, used only when the creature can cast spells.
  - id: crossbow_pattern
    type: min_max
    doc: pad_e[2]. Ranged AI pattern, the second tier of the capability cascade.
  - id: melee_move_pattern
    type: min_max
    doc: pad_e[3]. Default melee/movement AI pattern — what a creature with neither
      spells nor a crossbow uses.
  - id: flee_threshold
    type: min_max
    doc: pad_e[0]. Morale — CBENC.C computes morale_adj = 8 - value, and 0xff means the
      creature never flees. Only rolled at load time when the field is already non-zero,
      so a creature shipped with 0 here stays fearless.
types:
  min_max:
    doc: "File stores min (lower bound) first, then max (upper bound). Verified against randomizeStatInRange (0x6a360): result = first + rand % (second - first + 1), so first=min, second=max. Matches MonsterStatsExtractor.cs and the raw data (field0 <= field1)."
    seq:
      - id: min
        type: u2
      - id: max
        type: u2
