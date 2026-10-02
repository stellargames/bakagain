namespace BetrayalAtKrondor.Tests.Character;

using System.Collections.Generic;
using GameData.Resources.Character;
using Xunit;

public class SkillImprovedNoticeTests {
    private static System.Func<int, bool> Set(params (int Actor, int Attr)[] flags) {
        var keys = new HashSet<int>();
        foreach ((int a, int s) in flags) keys.Add(CharacterSheetRow.ChangedFlagFor(a, s));
        return keys.Contains;
    }

    [Fact]
    public void BardingAloneIsTheWholePartysEvenForOneMember() {
        // EVTCOND.C:352-354: a lone improvement in slot 0xb counts as three members. Measured after a
        // failed barding at the Black Sheep: "The party's Barding ability has increased."
        SkillImprovedNotice.Notice? n = SkillImprovedNotice.For(new[] { 0, 1, 2 }, Set((0, 0xb)));
        Assert.Equal(new SkillImprovedNotice.Notice(0x200b30, 0, 0xb), n);
    }

    [Fact]
    public void OneMemberOneSkillNamesBoth() {
        SkillImprovedNotice.Notice? n = SkillImprovedNotice.For(new[] { 0, 1, 2 }, Set((1, 5)));
        Assert.Equal(new SkillImprovedNotice.Notice(0x200b32, 1, 5), n);
    }

    [Fact]
    public void SeveralSkillsOrMembersTakeThePluralRecords() {
        Assert.Equal(0x200b33, SkillImprovedNotice.For(new[] { 0, 1 }, Set((1, 5), (1, 6)))!.Value.DialogId);
        Assert.Equal(0x200b31, SkillImprovedNotice.For(new[] { 0, 1 }, Set((0, 5), (1, 6)))!.Value.DialogId);
        Assert.Equal(0x200b30, SkillImprovedNotice.For(new[] { 0, 1 }, Set((0, 5), (1, 5)))!.Value.DialogId);
    }

    [Fact]
    public void OnlySlotsTwoToSixteenCountAndNothingMeansNoNotice() {
        // `for (slot_idx = 2; slot_idx < 0x11; ...)`: health and stamina never announce.
        Assert.Null(SkillImprovedNotice.For(new[] { 0 }, Set((0, 0), (0, 1))));
        Assert.Null(SkillImprovedNotice.For(new[] { 0 }, Set()));
    }
}
