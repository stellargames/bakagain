meta:
  id: party_dat
  title: Betrayal at Krondor PARTY.DAT (initial party templates)
  file-extension: dat
  endian: le
  encoding: ASCII
doc: |
  PARTY.DAT defines the six initial playable characters the engine copies into
  TEMP.GAM when a new game starts: Locklear, Gorath, Owyn, Pug, James, Patrus.

  Layout: six fixed 95-byte actor records (the SAME record stored inside save
  files / TEMP.GAM), a u2 trailer equal to the file size, then the display names
  as NUL-terminated strings.

  Names are resolved per record via `name_pointer` (an offset into the trailing
  name block), NOT by position — the record order (Locklear, Gorath, Owyn, …)
  differs from the string order (Locklear, Owyn, Gorath, …).

  Each attribute is {maximum, current, current_effective, experience, modifier};
  at game start maximum == current and the other three are 0. The 16 attributes
  are in fixed ActorAttribute order: Health, Stamina, Speed, Strength, Defense,
  AccuracyCrossbow, AccuracyMelee, AccuracyCasting, Assessment, Armorcraft,
  Weaponcraft, Barding, Haggling, Lockpick, Scouting, Stealth.
seq:
  - id: actors
    type: actor_record
    repeat: expr
    repeat-expr: 6
  - id: file_size
    type: u2
    doc: Equals the total file size (610); a trailer between records and names.
  - id: names
    type: strz
    repeat: eos
    doc: Display names; indexed by each actor's name_pointer (offset into this block).
types:
  actor_record:
    seq:
      - id: name_pointer
        type: u2
        doc: Offset into the trailing name block for this character's display name.
      - id: known_spells
        type: s2
        repeat: expr
        repeat-expr: 3
        doc: Spell bitmasks (0 for non-casters).
      - id: attributes
        type: attribute_values
        repeat: expr
        repeat-expr: 16
      - id: actor_number
        type: u1
      - id: inventory_pointer
        type: u4
      - id: combat_data_pointer
        type: u2
  attribute_values:
    seq:
      - id: maximum
        type: u1
      - id: current
        type: u1
      - id: current_effective
        type: u1
      - id: experience
        type: u1
      - id: modifier
        type: u1
