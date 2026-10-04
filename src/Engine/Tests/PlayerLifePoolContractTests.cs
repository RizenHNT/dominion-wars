using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DominionWars.Data;
using DominionWars.Engine.Effects;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// THE LIFE POOL: OFF BY DEFAULT, NOT A VICTORY CONDITION.
///
/// docs/RULES.md §1 lists exactly two victory paths — a leader's declared `winCondition`, and
/// the deck-cycle counter — and §7 states the player life pool constitutes neither a win nor a
/// loss. §0 defines the four distinct things the rule book used to call "life".
///
/// These tests pin that contract, because it was NOT true before 2026-09-12:
/// `MatchSetupOptions.PlayerLife` defaulted to 20 (every match started with a life pool nobody
/// had granted), and `EffectRuntime.CheckAll` declared `win.enemy_life_zero` when a player's
/// life reached 0. Both are gone.
///
/// WHY THIS MATTERS BEYOND TIDINESS: the stale default and the extra defeat branch together
/// produced a game state the rules do not allow, and a test built on it wrongly concluded the
/// AI was failing to defend against lethal damage. Analysis in
/// `docs/PL_RULES_ERRATA_2026-09-12.md`; this fixture is the regression guard for it.
/// </summary>
public sealed class PlayerLifePoolContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "cards")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    private static GameState Create(DeckDefinition first, DeckDefinition second, MatchSetupOptions options)
    {
        var catalog = Catalog();
        return MatchSetup.Create(Spec(first), Spec(second), catalog.Cards, options);
    }

    [Test]
    public void TheDefaultOptionValueIsNotAStartingPool()
    {
        // Asserted on the TYPE, not just on one match: a regression of this default back to 20
        // is precisely the defect this fixture exists to catch.
        Assert.That(
            new MatchSetupOptions().PlayerLife,
            Is.Null,
            "MatchSetupOptions.PlayerLife 默认必须为 null —— 回到 20 就是本次缺陷复发");
    }

    [Test]
    public void AMatchStartsWithNoLifePoolAtAll()
    {
        // No `PlayerLife` supplied, which is the shipped path through MatchFactory.
        var state = Create(Deck("flame"), Deck("wood"), new MatchSetupOptions
        {
            Seed = 5,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            CastleEnabled = true,
            CastleHealth = 75,
        });

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(0).Life, Is.Null, "生命池默认不存在：没有统领赋予就没有生命");
            Assert.That(state.GetPlayer(1).Life, Is.Null);
            Assert.That(state.WinnerPlayerIndex, Is.Null);
        });
    }

    [Test]
    public void AGrantingLeaderCanCreateAPoolFromNothing()
    {
        // The life pool is created by an ASSIGNMENT, not by a delta: the leader-manifest path
        // does `player.Life = definition.GrantLife` (EffectRuntime.Cards.cs), which is what makes
        // a null pool become a real one. This test pins that shape, because the change from a
        // default of 20 to a default of null is only safe while a grant can start from nothing.
        var seaLeaderDefinition = Catalog().Cards["sea_leader"];
        Assert.That(
            seaLeaderDefinition.GrantLife,
            Is.GreaterThan(0),
            "this test needs a leader that grants life; if the sea leader lost grantLife, update this fixture");

        var state = Create(Deck("sea"), Deck("wood"), new MatchSetupOptions
        {
            Seed = 3,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            CastleEnabled = true,
            CastleHealth = 75,
        });

        Assert.That(state.GetPlayer(0).Life, Is.Null, "统领降临之前没有生命池");

        // The grant path's own semantics: replace, not add.
        state.GetPlayer(0).Life = seaLeaderDefinition.GrantLife;

        Assert.Multiple(() =>
        {
            Assert.That(
                state.GetPlayer(0).Life,
                Is.EqualTo(seaLeaderDefinition.GrantLife),
                "统领降临必须能为该玩家从「无」开启一个生命池");
            Assert.That(state.WinnerPlayerIndex, Is.Null, "开启生命池本身不判胜负");
        });
    }

    [Test]
    public void DrainingThePoolToZeroDeclaresNoWinner()
    {
        // Build the exact state the deleted branch called a win: an explicit pool, drained to 0.
        // An explicit pool is legitimate here — it is what `grantLife` produces — and the point
        // is that reaching 0 changes nothing.
        var state = Create(Deck("flame"), Deck("wood"), new MatchSetupOptions
        {
            Seed = 9,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            PlayerLife = 20,
            CastleEnabled = true,
            CastleHealth = 75,
        });

        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        var source = state.GetPlayer(0).Hand.Count > 0
            ? state.GetPlayer(0).Hand[0]
            : new CardInstance(state.AllocateEntityId(), 0, Catalog().Cards["flame_leader"]);

        var root = state.Events.Append("CARD_PLAYED");
        dispatcher.Apply(
            new EffectSpec(EffectNames.LoseLife, "ENEMY_PLAYER", 25),
            new EffectContext(0, root.EventId, source, playedCard: source));

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(1).Life, Is.EqualTo(0), "生命值实际被扣到 0");
            Assert.That(
                state.WinnerPlayerIndex,
                Is.Null,
                "生命归零不得判定胜负，即使双方统领都在场（docs/RULES.md §1/§7）");
            Assert.That(
                state.WinReason,
                Is.Not.EqualTo("win.enemy_life_zero"),
                "win.enemy_life_zero 已从引擎删除，不得再出现");
        });
    }

    [Test]
    public void AttackingADurabilitylessNonMinionLeaderIsInert()
    {
        // THE REMOVED REDIRECT, pinned so it cannot come back.
        //
        // `DamageNonMinionLeader` used to end with `else { DamagePlayer(owner, ...) }`, so
        // attacking a non-minion leader that has NO durability track spent the attack on its
        // OWNER'S PLAYER LIFE POOL. `sea_leader` is the only such leader (type SPELL, no
        // `durability`, `grantLife` 25), which made the life pool its single point of
        // vulnerability — the "attacking a minion drains a health bar" behaviour.
        //
        // That is wrong under the current rules: the life pool is neither a victory nor a defeat
        // condition, so the attack accomplished nothing. A durability-less non-minion leader now
        // has NO defeat track from attacking at all: it wins, or loses, through its own declared
        // objective or its own printed negative effects (owner ruling 2026-09-12).
        var seaLeader = Catalog().Cards["sea_leader"];
        Assert.Multiple(() =>
        {
            Assert.That(seaLeader.IsMinion, Is.False, "sea_leader must stay a non-minion for this test to mean anything");
            Assert.That(
                seaLeader.LeaderDurability,
                Is.Zero,
                "sea_leader must have no durability track; if it gains one, update this fixture");
        });

        var state = Create(Deck("flame"), Deck("sea"), new MatchSetupOptions
        {
            Seed = 6,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            CastleEnabled = true,
            CastleHealth = 75,
        });

        // The opponent manifests the durability-less leader and has a life pool because it granted
        // one — the exact state in which the removed branch would have drained that pool.
        //
        // It goes in the LEADER ZONE ONLY. Adding the same instance to `Field` as well would make
        // `ActiveLeaderCount` 2, and `PlayerState.Leader` deliberately returns null when more than
        // one active leader exists (fail closed) — which makes every leader target illegal. That
        // is a trap worth naming, because it looks like "the leader is on the field".
        var target = new CardInstance(state.AllocateEntityId(), 1, seaLeader) { IsLeaderEntity = true };
        state.GetPlayer(1).LeaderZone.Add(target);
        state.GetPlayer(1).Life = seaLeader.GrantLife;

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow);
        flow.Advance(state, 0);
        router.Execute(state, new GameActionRequest(0, TurnAction.SkipAmbush));

        var attacker = new CardInstance(
            state.AllocateEntityId(), 0, new CardDefinition("attacker", "Attacker", 5, 5, isMinion: true));
        state.GetPlayer(0).Field.Add(attacker);

        var lifecycleBefore = state.GetPlayer(1).Life;
        var result = router.Execute(state, new GameActionRequest(
            0,
            LegalActionGenerator.Attack,
            sourceEntityId: attacker.InstanceId,
            targetId: "entity_" + target.InstanceId.ToString("D12", CultureInfo.InvariantCulture)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, "the attack itself must be a legal action; reason=" + result.ReasonKey);
            Assert.That(
                state.GetPlayer(1).Life,
                Is.EqualTo(lifecycleBefore),
                "attacking the leader must NOT touch the owner's life pool");
            Assert.That(target.Durability, Is.Zero, "a durability-less leader has nothing to reduce");
            Assert.That(state.WinnerPlayerIndex, Is.Null, "and nothing about this ends the game");
            Assert.That(
                state.Events.Items.Any(item =>
                    string.Equals(item.EventType, "EFFECT_SKIPPED", StringComparison.Ordinal)
                    && item.Data.TryGetValue("reasonKey", out var reason)
                    && string.Equals(reason?.ToString(), "target.leader_has_no_durability", StringComparison.Ordinal)),
                Is.True,
                "the inert attack must be reported as skipped with its own reason, not silently swallowed");
        });
    }

    [Test]
    public void ALifeLossOnAPlayerWithNoPoolIsSkippedNotClamped()
    {
        var state = Create(Deck("flame"), Deck("wood"), new MatchSetupOptions
        {
            Seed = 11,
            FirstPlayerIndex = 0,
            OpeningHandSize = 5,
            CastleEnabled = true,
            CastleHealth = 75,
        });

        Assert.That(state.GetPlayer(1).Life, Is.Null, "前置条件：没有生命池");

        var runtime = new EffectRuntime(state);
        var dispatcher = EffectDispatcher.CreateDefault(runtime);
        var source = state.GetPlayer(0).Hand.Count > 0
            ? state.GetPlayer(0).Hand[0]
            : new CardInstance(state.AllocateEntityId(), 0, Catalog().Cards["flame_leader"]);

        var root = state.Events.Append("CARD_PLAYED");
        dispatcher.Apply(
            new EffectSpec(EffectNames.LoseLife, "ENEMY_PLAYER", 5),
            new EffectContext(0, root.EventId, source, playedCard: source));

        Assert.Multiple(() =>
        {
            Assert.That(state.GetPlayer(1).Life, Is.Null, "没有生命池就没有可扣的东西");
            Assert.That(state.WinnerPlayerIndex, Is.Null);
            Assert.That(
                state.Events.Items[^1].EventType,
                Is.EqualTo("EFFECT_SKIPPED"),
                "缺生命池时应报 EFFECT_SKIPPED，而不是静默或钳制");
        });
    }
}
}
