namespace GameData.Resources.Character;

using System;
using System.Collections.Generic;

/// <summary>
/// "The party's Barding ability has increased." — which of the four notices a raised skill earns,
/// and whom and what it names: <c>evtcond_pty_dirty_flags_process</c>'s first half (EVTCOND.C:333-361).
/// </summary>
/// <remarks>
/// The world loop runs it whenever the party-dirty bit is set (WORLDLP.C:96-99). It reads the
/// sheet's changed flags for attributes 2..16 of every active member (it does not clear them — the
/// sheet does) and picks a record: several members → 0x200b30/31, one → 0x200b32/33, plural when
/// more than one attribute moved. A lone Barding improvement counts as the whole party's, because
/// barding is the party's performance.
/// </remarks>
public static class SkillImprovedNotice {
    /// <summary>The record to play, the member it names (<c>nEvtArgActor0</c>) and the attribute (<c>lEvtArgAuxValue</c>).</summary>
    public readonly record struct Notice(int DialogId, int Actor, int Attribute);

    private const int FirstAttribute = 2;
    private const int Barding = 0xb;

    /// <param name="party">Active members' character ids, in party order.</param>
    /// <param name="changed">Reads one changed flag (<see cref="CharacterSheetRow.ChangedFlagFor"/>).</param>
    public static Notice? For(IReadOnlyList<int> party, Func<int, bool> changed) {
        int members = 0, attributes = 0, actor = -1, attribute = -1;
        int memberMask = 0, attributeMask = 0;
        for (int m = 0; m < party.Count; m++) {
            for (int a = FirstAttribute; a < CharacterSheetRow.AttributesPerActor; a++) {
                if (!changed(CharacterSheetRow.ChangedFlagFor(party[m], a))) {
                    continue;
                }
                if ((memberMask & (1 << m)) == 0) {
                    memberMask |= 1 << m;
                    members++;
                    actor = party[m];
                }
                if ((attributeMask & (1 << a)) == 0) {
                    attributeMask |= 1 << a;
                    attributes++;
                    attribute = a;
                }
            }
        }
        if (attributes == 1 && attribute == Barding) {
            members = 3;
        }
        if (members > 1) {
            return new Notice(attributes > 1 ? 0x200b31 : 0x200b30, actor, attribute);
        }
        if (attributes != 0) {
            return new Notice(attributes > 1 ? 0x200b33 : 0x200b32, actor, attribute);
        }
        return null;
    }
}
