using System.Collections.Generic;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Randomness;

namespace DominionWars.Engine.Tests
{

internal sealed class EffectTestFixture
{
    public EffectTestFixture()
    {
        SourceDefinition = new CardDefinition(
            "source",
            "Source",
            1,
            4,
            isMinion: true,
            faction: "机械遗迹",
            tags: new[] { "机械" });
        SoldierDefinition = new CardDefinition("soldier", "Soldier", 2, 5, isMinion: true);
        WardDefinition = new CardDefinition(
            "ward", "Ward", 1, 5, isMinion: true, keywords: new[] { "扰魔" });
        TokenDefinition = new CardDefinition("token", "Token", 1, 1, isMinion: true);
        LeaderDefinition = new CardDefinition(
            "leader", "Leader", 3, 8, isMinion: true, isLeader: true, grantLife: 15,
            vulnerabilities: new[] { EffectNames.Damage });

        State = new GameState(
            new PlayerState(0, 20),
            new PlayerState(1, 20),
            new Xoshiro256StarStar(1234),
            new[]
            {
                SourceDefinition, SoldierDefinition, WardDefinition, TokenDefinition, LeaderDefinition,
            });

        Source = new CardInstance(1, 0, SourceDefinition);
        Friendly = new CardInstance(2, 0, SoldierDefinition);
        Enemy = new CardInstance(3, 1, SoldierDefinition);
        WardEnemy = new CardInstance(4, 1, WardDefinition);
        State.Players[0].Field.Add(Source);
        State.Players[0].Field.Add(Friendly);
        State.Players[1].Field.Add(Enemy);
        State.Players[1].Field.Add(WardEnemy);

        Runtime = new EffectRuntime(State);
        Dispatcher = EffectDispatcher.CreateDefault(Runtime);
    }

    public GameState State { get; }
    public EffectRuntime Runtime { get; }
    public EffectDispatcher Dispatcher { get; }
    public CardDefinition SourceDefinition { get; }
    public CardDefinition SoldierDefinition { get; }
    public CardDefinition WardDefinition { get; }
    public CardDefinition TokenDefinition { get; }
    public CardDefinition LeaderDefinition { get; }
    public CardInstance Source { get; }
    public CardInstance Friendly { get; }
    public CardInstance Enemy { get; }
    public CardInstance WardEnemy { get; }

    public EffectContext Context(
        long? selectedTargetId = null,
        IReadOnlyList<CardInstance>? drawnCards = null,
        CoreTarget? selectedCoreTarget = null)
    {
        var root = State.Events.Append("CARD_PLAYED");
        return new EffectContext(
            0,
            root.EventId,
            Source,
            playedCard: Source,
            drawnCards: drawnCards,
            selectedTargetId: selectedTargetId,
            selectedCoreTarget: selectedCoreTarget);
    }

    public void Apply(
        string action,
        string? target = null,
        int amount = 0,
        string? param = null,
        long? selectedTargetId = null,
        IReadOnlyList<CardInstance>? drawnCards = null,
        CoreTarget? selectedCoreTarget = null)
    {
        Dispatcher.Apply(
            new EffectSpec(action, target, amount, param),
            Context(selectedTargetId, drawnCards, selectedCoreTarget));
    }
}
}
