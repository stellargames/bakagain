namespace BetrayalAtKrondor.Tests.Combat;

using GameData;
using GameData.Resources.Combat;
using Xunit;

public class EnemyKnowledgeTests {
    [Fact]
    public void ARevealedRowIsRemembered_AFailedRowIsNot() {
        var k = new EnemyKnowledge();
        k.Record(0, ActorAttribute.Defense, 42);
        Assert.True(k.TryGet(0, ActorAttribute.Defense, out int v));
        Assert.Equal(42, v);
        Assert.False(k.TryGet(0, ActorAttribute.Strength, out _));
        Assert.True(k.Inspected(0));
    }

    [Fact]
    public void KnowledgeIsPerEnemy() {
        var k = new EnemyKnowledge();
        k.Record(0, ActorAttribute.Defense, 42);
        Assert.False(k.Inspected(1));
        Assert.False(k.TryGet(1, ActorAttribute.Defense, out _));
    }

    [Fact]
    public void SeenAddsUp_MissesAddNothing() {
        var k = new EnemyKnowledge();
        k.AddSeen(2, 5);
        k.AddSeen(2, 0);
        k.AddSeen(2, -3);
        k.AddSeen(2, 4);
        Assert.Equal(9, k.Seen(2));
        Assert.Equal(0, k.Seen(1));
    }

    [Fact]
    public void ClearForgetsEverything() {
        var k = new EnemyKnowledge();
        k.Record(0, ActorAttribute.Defense, 42);
        k.AddSeen(0, 5);
        k.Clear();
        Assert.False(k.Inspected(0));
        Assert.Equal(0, k.Seen(0));
    }
}
