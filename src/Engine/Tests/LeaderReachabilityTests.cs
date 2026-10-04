using System;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// IS A GUARDED LEADER ACTUALLY UNREACHABLE, AND HOW DID SEVEN MATCHES END BY DEFEATING ONE?
///
/// WHY THIS EXISTS. A match-shape sweep found `win.enemy_leader_defeated` as the MOST COMMON ending
/// (7 of 16), which contradicts an earlier conclusion that no reliable way to defeat a leader exists.
/// That contradiction matters more than it looks: if the common ending is "kill the leader", then the
/// per-faction victory axes (discard, download, sealed growth) are decoration and faction identity
/// collapses — which would make designing decks AROUND those axes pointless.
///
/// The suspected mechanism is narrow and testable: `AttackTargetPolicy.IsGuarded` returns true only
/// when the leader's owner has ANOTHER minion on the field. So a leader standing alone is not guarded,
/// and a plain attack would reach it. This fixture checks that directly instead of reasoning about it:
/// build a field with the leader alone, ask the ENGINE for legal targets, then attack and see whether
/// the leader's health drops.
///
/// It asserts only what the engine actually does, and prints the legality so the finding is readable
/// from the run log rather than inferred.
/// </summary>
public sealed class LeaderReachabilityTests
{
    private static string RepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(directory.FullName, "data", "cards")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new System.IO.DirectoryNotFoundException("repo root not found");
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(System.IO.Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(System.IO.Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    private static GameState FreshMatch()
    {
        return MatchSetup.Create(
            Spec(Deck("flame")),
            Spec(Deck("machine")),
            Catalog().Cards,
            new MatchSetupOptions
            {
                Seed = 77,
                FirstPlayerIndex = 0,
                OpeningHandSize = 5,
                CastleEnabled = true,
                CastleHealth = 75,
            });
    }

    private static (TurnFlow Flow, TurnActionRouter Router) Routed(GameState state)
    {
        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));
        return (flow, router);
    }

    [Test]
    public void ALeaderStandingAloneIsReachableByAPlainAttack()
    {
        var state = FreshMatch();
        var (flow, router) = Routed(state);
        var catalog = Catalog();

        // Player 1's leader, manifested, ALONE on its field.
        var leaderDefinition = catalog.Cards["machine_leader"];
        var leader = new CardInstance(state.AllocateEntityId(), 1, leaderDefinition) { IsLeaderEntity = true };
        state.GetPlayer(1).LeaderZone.Add(leader);

        // Player 0 gets one ready attacker with enough attack to be visible in the outcome.
        var attacker = new CardInstance(
            state.AllocateEntityId(),
            0,
            new CardDefinition("probe_attacker", "Probe Attacker", attack: 3, health: 5, isMinion: true));
        state.GetPlayer(0).Field.Add(attacker);

        var legalTargets = flow.GetLegalActions(state, 0)
            .Where(a => string.Equals(a.Type, LegalGenerator.Attack, StringComparison.Ordinal))
            .Select(a => a.TargetReferenceId)
            .ToList();

        var targetReference = "entity_" + leader.InstanceId.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);
        var leaderIsTargetable = legalTargets.Contains(targetReference, StringComparer.Ordinal);

        var durabilityBefore = leader.Durability;
        var healthBefore = leader.Health;

        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalGenerator.Attack,
            sourceEntityId: attacker.InstanceId,
            targetId: targetReference));

        TestContext.Out.WriteLine("leader card        : {0} (isMinion={1}, durability={2}, health={3})",
            leaderDefinition.Id, leaderDefinition.IsMinion, leaderDefinition.LeaderDurability, leaderDefinition.Health);
        TestContext.Out.WriteLine("legal ATTACK refs  : [{0}]", string.Join(", ", legalTargets));
        TestContext.Out.WriteLine("leader targetable  : {0}", leaderIsTargetable);
        TestContext.Out.WriteLine("attack accepted    : {0} ({1})", result.Accepted, result.ReasonKey);
        TestContext.Out.WriteLine("durability {0} -> {1} ; health {2} -> {3}",
            durabilityBefore, leader.Durability, healthBefore, leader.Health);

        Assert.That(
            leaderIsTargetable,
            Is.True,
            "a leader standing ALONE must be reachable by a plain attack; if this fails the guard "
            + "holds even with no bodyguard and the sweep's `enemy_leader_defeated` endings came from somewhere else");
        Assert.That(result.Accepted, Is.True, "the advertised leader attack must be accepted: " + result.ReasonKey);
        Assert.That(
            leader.Durability < durabilityBefore,
            "the attack must actually reduce the leader's durability track");
    }

    /// <summary>
    /// THE FINDING, pinned directly: A GUARD IS DECLARED ONLY ON MINION LEADERS, so every NON-MINION
    /// leader is reachable by a plain attack whether or not it has a bodyguard beside it.
    ///
    /// This is the explanation for `win.enemy_leader_defeated` being the MOST COMMON match ending
    /// (7 of 16 in the match-shape sweep) despite an earlier conclusion that leaders are hard to
    /// defeat. The first version of this test asserted the bodyguard protects the leader and FAILED —
    /// correctly, because `AttackTargetPolicy.IsGuarded` begins `if (!leader.Definition.Guard) return
    /// false;` and `machine_leader`, `wood_leader` and `sea_leader` declare no guard at all while
    /// `flame_leader`, `machine_alpha` and `shadow_of_fate` do.
    ///
    /// WHY IT MATTERS FOR DECK DESIGN: the sweep's most common ending is "attack the leader", not any
    /// faction's declared axis, which flattens faction identity (strategy criterion S5). A non-minion
    /// leader with a durability track of 8 or 20 that can be attacked from turn one by any body also
    /// has a durability track that is really just a countdown.
    /// </summary>
    [Test]
    public void EveryNonMinionLeaderIsReachableByAPlainAttack()
    {
        var catalog = Catalog();
        var leaders = catalog.Cards.Values.Where(c => c.IsLeader).ToList();
        Assert.That(leaders, Is.Not.Empty, "the catalog must contain leaders");

        var minionLeaders = leaders.Where(l => l.IsMinion).Select(l => l.Id).OrderBy(id => id).ToList();
        var nonMinionLeaders = leaders.Where(l => !l.IsMinion).Select(l => l.Id).OrderBy(id => id).ToList();

        TestContext.Out.WriteLine("minion leaders     (guard declared): [{0}]", string.Join(", ", minionLeaders));
        TestContext.Out.WriteLine("non-minion leaders (no guard)      : [{0}]", string.Join(", ", nonMinionLeaders));

        foreach (var declared in leaders)
        {
            TestContext.Out.WriteLine("  {0,-14} isMinion={1,-6} guard={2,-6} durability={3}",
                declared.Id, declared.IsMinion, declared.Guard, declared.LeaderDurability);
        }

        // The rule, stated as an assertion on the DATA: a non-minion leader declares no guard, and a
        // minion leader does. If this changes, the reachability conclusion changes with it.
        foreach (var declared in leaders.Where(l => !l.IsMinion))
        {
            Assert.That(
                declared.Guard,
                Is.False,
                "non-minion leader '" + declared.Id + "' now declares a guard; the reachability finding and the"
                + " match-shape conclusion both need revisiting");
        }

        Assert.That(
            leaders.Count(l => l.IsMinion && l.Guard),
            Is.GreaterThan(0),
            "at least one minion leader must declare a guard, or guard is unused in the data");

        // And the consequence, measured on a real position: the non-minion leader is a legal target
        // even with a bodyguard standing beside it.
        var state = FreshMatch();
        var (flow, _) = Routed(state);

        var leaderDefinition = catalog.Cards["machine_leader"];
        var leader = new CardInstance(state.AllocateEntityId(), 1, leaderDefinition) { IsLeaderEntity = true };
        state.GetPlayer(1).LeaderZone.Add(leader);

        var bodyguard = new CardInstance(
            state.AllocateEntityId(),
            1,
            new CardDefinition("probe_bodyguard", "Probe Bodyguard", attack: 1, health: 4, isMinion: true));
        state.GetPlayer(1).Field.Add(bodyguard);

        var attacker = new CardInstance(
            state.AllocateEntityId(),
            0,
            new CardDefinition("probe_attacker", "Probe Attacker", attack: 3, health: 5, isMinion: true));
        state.GetPlayer(0).Field.Add(attacker);

        var legalTargets = flow.GetLegalActions(state, 0)
            .Where(a => string.Equals(a.Type, LegalGenerator.Attack, StringComparison.Ordinal))
            .Select(a => a.TargetReferenceId)
            .ToList();

        var leaderReference = "entity_" + leader.InstanceId.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);
        var bodyguardReference = "entity_" + bodyguard.InstanceId.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);

        TestContext.Out.WriteLine("with a bodyguard beside machine_leader:");
        TestContext.Out.WriteLine("  legal ATTACK refs   : [{0}]", string.Join(", ", legalTargets));
        TestContext.Out.WriteLine("  leader targetable   : {0}", legalTargets.Contains(leaderReference, StringComparer.Ordinal));
        TestContext.Out.WriteLine("  bodyguard targetable: {0}", legalTargets.Contains(bodyguardReference, StringComparer.Ordinal));

        Assert.That(
            legalTargets.Contains(leaderReference, StringComparer.Ordinal),
            Is.True,
            "a non-minion leader must be reachable even with a bodyguard, because it declares no guard");
    }
}

/// <summary>Alias so the action-name constant is read from one place, matching the engine's source.</summary>
internal static class LegalGenerator
{
    public const string Attack = "ATTACK";
}
}
