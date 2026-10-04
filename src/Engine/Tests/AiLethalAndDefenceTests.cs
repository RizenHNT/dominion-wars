using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DominionWars.Adapters;
using DominionWars.Adapters.Ai;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Engine.Setup;
using DominionWars.Engine.Turns;
using NUnit.Framework;

namespace DominionWars.Engine.Tests
{

/// <summary>
/// LETHAL AND DEFENCE: the two blunders that would disqualify the instrument outright.
///
/// An instrument that misses its own winning line reports a deck as weaker than it is, and
/// an instrument that ignores a lethal threat reports a deck as stronger. Both biases are
/// systematic and both would corrupt every balance number downstream, so these are the
/// first things worth pinning — more than any question about optimal play.
///
/// The tests are deliberately written as "did it take the advertised kill" rather than
/// "did it play the move I would". Lethal is a property of the POSITION, computable from
/// the engine's own numbers, so it can be asserted without opinion.
/// </summary>
public sealed class AiLethalAndDefenceTests
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

        throw new DirectoryNotFoundException("Could not locate the repository root from " + TestContext.CurrentContext.TestDirectory);
    }

    private static CardCatalog Catalog()
        => CardCatalog.LoadDirectory(Path.Combine(RepositoryRoot(), "data", "cards"));

    private static DeckDefinition Deck(string faction)
        => DeckLoader.LoadFile(Path.Combine(RepositoryRoot(), "data", "decks", faction + "_deck.json"));

    private static MatchDeckSpec Spec(DeckDefinition deck)
        => new MatchDeckSpec(deck.Leader, deck.Cards, deck.Name, deck.Faction);

    /// <summary>
    /// A position where the AI has enough attack on board to kill, driven to a real
    /// decision point. The damage is real: the attackers are genuine summoned minions and
    /// the engine computes the lethal arithmetic, not this test.
    /// </summary>
    private sealed class Position
    {
        public GameState State { get; init; } = null!;
        public TurnFlow Flow { get; init; } = null!;
        public RuntimeMatchGateway Gateway { get; init; } = null!;
        public AdvertisedActionPolicy Policy { get; init; } = null!;
    }

    /// <summary>
    /// PHASE-INDEPENDENT DELIVERY, and the reason this helper exists: the engine routes
    /// phases, so dropping a minion onto the field and hoping an ACTION phase follows does
    /// not work. Instead the position is driven with the normal policy until the engine
    /// offers an ACTION phase to the seat under test, and only THEN is the board rigged —
    /// so the rig cannot disturb phase progression.
    /// </summary>
    private static Position DriveToAction(int seed, string actorFaction, string foeFaction, int maxSteps = 240)
    {
        var catalog = Catalog();
        var state = MatchSetup.Create(
            Spec(Deck(actorFaction)),
            Spec(Deck(foeFaction)),
            catalog.Cards,
            new MatchSetupOptions
            {
                Seed = (ulong)seed,
                FirstPlayerIndex = 0,
                OpeningHandSize = 5,
                PlayerLife = 20,
                CastleEnabled = true,
                CastleHealth = 75,
            });

        var flow = TurnFlow.CreateDefault();
        var router = TurnActionRouter.CreateDefault(flow, null, PunishResponseStances.PolicyFor(PlaystyleRegistry.Default));
        var gateway = new RuntimeMatchGateway("match_lethal", state, flow, router, null);
        Assert.That(gateway.Initialize(0).Accepted, Is.True);

        var policy = new AdvertisedActionPolicy();
        var steps = 0;

        while (steps++ < maxSteps)
        {
            if (state.WinnerPlayerIndex.HasValue) break;

            var viewer = state.CurrentPlayerIndex;
            var snapshot = gateway.GetSnapshot(viewer);
            if (snapshot.WinnerPlayerIndex.HasValue) break;

            if (string.Equals(snapshot.Phase, "ACTION", StringComparison.Ordinal)
                && snapshot.LegalActions.Count > 0)
            {
                return new Position { State = state, Flow = flow, Gateway = gateway, Policy = policy };
            }

            var pick = snapshot.LegalActions.FirstOrDefault();
            if (pick is null) break;
            var submission = gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, pick));
            if (!submission.Result.Accepted)
            {
                var alternative = snapshot.LegalActions
                    .FirstOrDefault(a => !string.Equals(a.ActionId, pick.ActionId, StringComparison.Ordinal));
                if (alternative is null
                    || !gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, alternative)).Result.Accepted)
                {
                    break;
                }
            }
        }

        Assert.Fail("could not reach an ACTION phase with legal actions inside the step budget");
        return null!;
    }

    /// <summary>
    /// RECORDED PREFERENCE, NOT A MISSED KILL — and the correction is the point of this test.
    ///
    /// WHAT WAS MEASURED: in 12/12 probed positions, with the opponent on ONE life and core
    /// ATTACKs advertised, the policy chose a PLAY_CARD instead of attacking.
    ///
    ///   policy took an advertised core attack: 0/12
    ///   [control: accepted=True damaged=False won=False reason=action.accepted]
    ///
    /// WHAT THE CONTROL ESTABLISHED: submitting the advertised core attack IS accepted by the
    /// engine, and it deals NO LIFE DAMAGE — not even against a 1-life opponent. No
    /// <c>rule.leader_gate</c> fires either, so the gate is not the explanation.
    ///
    /// THEREFORE THERE IS NO MISSED KILL HERE. I accused the policy three times and was wrong
    /// three times:
    ///   1. at 20 life, assuming the attack wins — the control would not confirm it;
    ///   2. at 1 life, still assuming the attack wins — still unconfirmed;
    ///   3. only after reading <c>TryDeclareWinner</c> and measuring "damage dealt" did it become
    ///      clear the attack lands nothing, so nothing was missed.
    ///
    /// WHAT REMAINS WORTH RECORDING: the policy prefers a card play over an advertised attack it
    /// never even evaluates as weaker. That matches the weight structure measured in V4 —
    /// <c>board</c> is worth 500 to a PLAY_CARD while <c>damage</c> is worth 120 and <c>face</c>
    /// is worth 0 — so the preference is predicted by the model rather than surprising. It is a
    /// statement about what the instrument values, and it belongs in the limitations, not in a
    /// claim that the AI throws games.
    /// </summary>
    [Test]
    public void AdvertisedCoreAttacksArePassedOverInFavourOfCardPlays()
    {
        var checkedPositions = 0;
        var failures = new List<string>();
        var diagnostics = new List<string>();
        var unconfirmed = new List<string>();
        var tookTheAttack = 0;

        foreach (var actorFaction in new[] { "flame", "machine", "sea", "wood" })
        {
            for (var seed = 1; seed <= 3; seed++)
            {
                var position = DriveToAction(seed, actorFaction, "wood");
                var state = position.State;
                var actor = state.CurrentPlayerIndex;
                var foe = state.GetOpponent(actor);

                // Rig a lethal board: enough attack to kill regardless of what the opponent
                // can block with, by exceeding life plus any plausible blocker.
                //
                // NO TEMPLATE GUARD. An earlier version bailed out when the AI's field was
                // empty — which it usually is at the first ACTION phase — so the sweep found
                // ZERO positions and reported that as a failure to be non-vacuous. The rig
                // does not need an existing minion to copy from.
                var attacker = new CardInstance(
                    90_000 + seed,
                    actor,
                    new CardDefinition(
                        "rigged_lethal",
                        "Rigged Lethal",
                        attack: 40,
                        health: 40,
                        isMinion: true));
                attacker.IsLeaderEntity = false;
                attacker.SummonedThisTurn = false;
                attacker.AttacksUsed = 0;
                state.GetPlayer(actor).Field.Add(attacker);

                if (foe.Life <= 0)
                {
                    state.GetPlayer(actor).Field.Remove(attacker);
                    continue;
                }

                // Put the opponent on ONE life. This is what makes the check incontrovertible:
                // a 40-attack minion against 1 life cannot be survived, and the engine's own
                // verdict on the replay below either confirms the win or the case is dropped.
                // An earlier version left life at 20, could not confirm a kill at all, and would
                // have reported 12 "missed kills" that were nothing of the sort.
                foe.Life = 1;
                var lifeBefore = foe.Life;

                // The engine must advertise attacks on the core now.
                //
                // The target is on TargetId, NOT in the payload — reading `payload["target"]`
                // found nothing and made an earlier version of this sweep report zero lethal
                // positions while the engine was in fact advertising the attacks. The
                // classification below mirrors ActionFeatures.AttackTarget: a target that is
                // not an entity on the opponent's side is the core.
                var snapshot = position.Gateway.GetSnapshot(actor);
                var opponentSide = snapshot.Players[1 - actor];
                var coreAttacks = snapshot.LegalActions
                    .Where(a => string.Equals(a.Type, "ATTACK", StringComparison.Ordinal))
                    .Where(a =>
                    {
                        var target = a.TargetId?.ToString();
                        if (string.IsNullOrEmpty(target)) return true;
                        var onBoard = (opponentSide.Field ?? Array.Empty<RuntimeCardSnapshot>())
                                .Any(c => string.Equals(c.EntityId.ToString(), target, StringComparison.Ordinal))
                            || (opponentSide.LeaderZone ?? Array.Empty<RuntimeCardSnapshot>())
                                .Any(c => string.Equals(c.EntityId.ToString(), target, StringComparison.Ordinal));
                        return !onBoard;
                    })
                    .ToList();

                if (coreAttacks.Count == 0)
                {
                    // Diagnose rather than silently skip. The first version of this test found
                    // ZERO lethal positions; printing why is what keeps a vacuous sweep from
                    // looking like a passing one.
                    diagnostics.Add(
                        actorFaction + " seed " + seed + ": no core ATTACK advertised after adding a 40-attack "
                        + "minion. ATTACK actions advertised: ["
                        + string.Join(", ", snapshot.LegalActions
                            .Where(a => string.Equals(a.Type, "ATTACK", StringComparison.Ordinal))
                            .Select(a => "src=" + a.SourceId + " targetKey="
                                + (a.TargetId?.ToString() ?? "<null>")
                                + " keys=" + (a.Payload is null ? "" : string.Join("|", a.Payload.Keys))))
                        + "] fieldCount=" + state.GetPlayer(actor).Field.Count
                        + " foeLife=" + foe.Life);
                    state.GetPlayer(actor).Field.Remove(attacker);
                    continue;
                }

                checkedPositions++;

                // THE CRITERION, stated so it can be judged: when the engine advertises a core
                // ATTACK, does the policy pick one?
                //
                // I first tried to confirm lethality by REPLAYING the attack and asking the
                // engine for a verdict. It would not confirm — at 20 life or at 1 life — which
                // means my control's attack was not resolving and every "missed kill" it
                // produced would have been a FALSE ACCUSATION. So the claim is narrowed to what
                // is directly observable: a core ATTACK was advertised, and the policy did not
                // choose any of them. That is the honest form of the check, and it needs no
                // prediction of the engine's damage arithmetic.
                var chosen = ChooseOrFail(position, snapshot, actorFaction, seed);

                var choseACoreAttack = coreAttacks.Any(a =>
                    string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal));

                var submission = position.Gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, chosen));
                var damaged = foe.Life < lifeBefore;

                if (choseACoreAttack)
                {
                    tookTheAttack++;
                }
                else
                {
                    // Ask the engine what taking the attack actually does. Damage landing is the
                    // criterion that matters, because `rule.leader_gate` means a kill cannot win
                    // this early in the game no matter what the policy does.
                    var control = ReplayTakingTheKill(actorFaction, seed);

                    var line = actorFaction + " seed " + seed + ": " + coreAttacks.Count
                        + " core ATTACK(s) advertised against a " + lifeBefore
                        + "-life opponent, but the policy chose " + chosen.Type + "/" + chosen.CardId
                        + " (damage dealt: " + (damaged ? "yes" : "no") + ")"
                        + "  [control: accepted=" + control.Accepted + " damaged=" + control.Damaged
                        + " won=" + control.Won + " reason=" + control.Reason + "]";

                    // A blunder only if the attack would have LANDED and the policy chose neither
                    // an attack nor anything that damages. If the control could not even land the
                    // attack, the case proves nothing and is recorded as unconfirmed.
                    if (control.Damaged) failures.Add(line);
                    else unconfirmed.Add(line);
                }

                state.GetPlayer(actor).Field.Remove(attacker);
                if (state.WinnerPlayerIndex.HasValue) break;
            }
        }

        TestContext.Out.WriteLine("lethal positions probed: {0}", checkedPositions);
        foreach (var line in diagnostics.Take(4)) TestContext.Out.WriteLine("  " + line);
        TestContext.Out.WriteLine("  policy took an advertised core attack: {0}/{1}", tookTheAttack, checkedPositions);
        foreach (var line in unconfirmed.Take(4)) TestContext.Out.WriteLine("  NOT TAKEN: " + line);

        Assert.That(checkedPositions, Is.GreaterThan(0), "the sweep must be non-vacuous");

        // The assertion is the CONFIRMED failures only. The measured PREFERENCE is printed above
        // and recorded in the report as a limitation, not asserted as a blunder — the control
        // established that taking the attack deals no life damage, so nothing was missed.
        Assert.That(
            failures,
            Is.Empty,
            "where the control CONFIRMED that taking the attack deals damage, the policy must take it ("
            + failures.Count + "):\n  " + string.Join("\n  ", failures.Take(6)));

        // Record the ratio so a regression either way is visible in the report rather than moving
        // silently: if the policy starts taking these, that is news too.
        TestContext.Out.WriteLine(
            "  => passed over {0} of {1} advertised core attacks (a value preference, not a missed kill:"
            + " the control takes the attack and deals no life damage)",
            checkedPositions - tookTheAttack, checkedPositions);
    }

    /// ============================================================ 必防（已撤回）
    ///
    /// A TEST NAMED `AnIncomingLethalIsNotIgnored` USED TO LIVE HERE AND HAS BEEN DELETED.
    /// It is recorded rather than silently removed, because it produced a WRONG CONCLUSION
    /// that reached the acceptance report and an implementation work order.
    ///
    /// WHAT IT CLAIMED: the AI ignores a lethal threat. It rigged a 40-attack minion onto the
    /// opponent's board with the viewer on 20 life, then required the policy to spend its turn
    /// lowering that attack. It failed 4/4, and the report recorded "the AI cannot see life".
    ///
    /// WHY THE PREMISE WAS INVALID — three separate errors, all mine:
    ///
    ///   1. IT ASSUMED THE PLAYER HAS LIFE AT ALL. The life pool is OFF by default
    ///      (`MatchSetupOptions.PlayerLife` is null) and exists only when a leader that
    ///      declares `grantLife` opens it. The test fabricated a pool that the rules do not
    ///      give a match.
    ///   2. IT ASSUMED LIFE-LOSS LOSES THE GAME. It does not. docs/RULES.md §1 lists exactly
    ///      two victory paths — a leader's declared winCondition and the deck-cycle counter —
    ///      and §7 states the life pool constitutes neither a win nor a loss. The engine's
    ///      `win.enemy_life_zero` branch that this test leaned on has since been deleted
    ///      (EffectRuntime.cs) precisely because the rule book does not contain it.
    ///   3. IT RIGGED A THREAT THAT COULD NOT REACH ANYTHING. `AttackTargetPolicy` only offers
    ///      a player's life as an attack target while that player's leader is UNMANIFESTED;
    ///      with a leader in play the rigged minion had no legal path to the life total. On top
    ///      of that, the test's own "lethal is advertised" predicate accepted an ATTACK aimed
    ///      at the ROYAL CASTLE, so "lethal confirmed" did not even mean what it said.
    ///
    /// So the policy was not blundering: it declined to defend against a death that the rules
    /// do not permit in that position. A test that demands otherwise is asserting a rule the
    /// game does not have.
    ///
    /// WHAT REPLACES IT: nothing in this file. If life ever becomes a real victory axis
    /// (docs/RULES.md §12.5 registers it as a pre-planned but unenabled idea), the correct
    /// test is not "does it defend" but "does it recognise a reachable lethal", built from
    /// positions where the engine actually advertises that kill. Full analysis:
    /// docs/PL_RULES_ERRATA_2026-09-12.md §2 and §4.
    /// ============================================================

    /// <summary>
    /// Rebuilds the same position and asks the ENGINE what taking the core attack actually
    /// does: does it deal damage, and does it end the game.
    ///
    /// WHY THE EARLIER CONTROL WAS WRONG, recorded because I got it wrong three times: it
    /// only asked "did a winner appear", and answered no. The engine explains why.
    /// <c>TryDeclareWinner</c> refuses to declare a win unless BOTH leaders are manifested
    /// (<c>rule.leader_gate</c>), and in an early ACTION phase the opponent's leader is still
    /// sitting in its deck. So at this point in the game a killing blow CANNOT win the match —
    /// which means "the policy passed up a kill" was not merely unproven, it was NOT A KILL.
    ///
    /// The control now returns both facts, because only the pair can characterise the
    /// behaviour honestly: damage landing is a real effect even when the win is gated.
    /// </summary>
    private static (bool Accepted, bool Damaged, bool Won, string Reason) ReplayTakingTheKill(
        string actorFaction, int seed)
    {
        Position position;
        try
        {
            position = DriveToAction(seed, actorFaction, "wood");
        }
        catch (AssertionException)
        {
            return (false, false, false, "could not reach an ACTION phase");
        }

        var state = position.State;
        var actor = state.CurrentPlayerIndex;
        var foe = state.GetOpponent(actor);
        foe.Life = 1;
        var lifeBefore = foe.Life;

        var attacker = new CardInstance(
            95_000 + seed,
            actor,
            new CardDefinition("rigged_lethal_control", "Rigged Lethal Control", attack: 40, health: 40, isMinion: true));
        attacker.IsLeaderEntity = false;
        attacker.SummonedThisTurn = false;
        attacker.AttacksUsed = 0;
        state.GetPlayer(actor).Field.Add(attacker);

        var snapshot = position.Gateway.GetSnapshot(actor);
        var opponentSide = snapshot.Players[1 - actor];
        var coreAttack = snapshot.LegalActions
            .Where(a => string.Equals(a.Type, "ATTACK", StringComparison.Ordinal))
            .FirstOrDefault(a =>
            {
                var target = a.TargetId?.ToString();
                if (string.IsNullOrEmpty(target)) return true;
                var onBoard = (opponentSide.Field ?? Array.Empty<RuntimeCardSnapshot>())
                        .Any(c => string.Equals(c.EntityId.ToString(), target, StringComparison.Ordinal))
                    || (opponentSide.LeaderZone ?? Array.Empty<RuntimeCardSnapshot>())
                        .Any(c => string.Equals(c.EntityId.ToString(), target, StringComparison.Ordinal));
                return !onBoard;
            });

        if (coreAttack is null) return (false, false, false, "no core attack advertised");

        var before = state.Events.Items.Count;
        var submission = position.Gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, coreAttack));

        // Which engine-side reason blocked anything, if one did — `rule.leader_gate` is the
        // expected one and naming it is the whole point of this control.
        var blocked = string.Empty;
        for (var index = before; index < state.Events.Items.Count; index++)
        {
            var item = state.Events.Items[index];
            if (item.Data is null) continue;
            if (item.Data.TryGetValue("reasonKey", out var reason) && reason is not null)
            {
                blocked = reason.ToString() ?? string.Empty;
            }
        }

        return (
            submission.Result.Accepted,
            foe.Life < lifeBefore,
            state.WinnerPlayerIndex == actor,
            string.IsNullOrEmpty(blocked) ? submission.Result.ReasonKey : blocked);
    }

    // ------------------------------------------------- ③ rule-determinable blunder

    /// <summary>
    /// A BLUNDER DEFINED BY THE RULES, not by preference.
    ///
    /// The owner asked for "obvious bad moves" coverage, and every other case in this report
    /// turned out to be a VALUE PREFERENCE (V4: preferring a play over ending the turn; V7:
    /// preferring a play over an attack that deals nothing). A preference can be argued about.
    /// This one cannot: attacking a minion that SURVIVES the blow while the ATTACKER dies is a
    /// pure loss of material — the attacker is destroyed and the defender is untouched. No weight
    /// table makes that a good move, and the engine's own numbers define it.
    /// </summary>
    [Test]
    public void ASelfDefeatingTradeIsNotPreferred()
    {
        var probed = 0;
        var choseTheBadTrade = new List<string>();
        var choseSomethingElse = 0;

        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            Position position;
            try
            {
                position = DriveToAction(seed, "wood", "machine");
            }
            catch (AssertionException)
            {
                continue;
            }

            var state = position.State;
            var actor = state.CurrentPlayerIndex;
            var foe = state.GetOpponent(actor);

            // A 1/1 of mine against a 10/10 of theirs: the attack cannot kill the wall, so it
            // trades my only body for nothing.
            var weak = new CardInstance(
                97_000 + seed,
                actor,
                new CardDefinition("rigged_weak", "Rigged Weak", attack: 1, health: 1, isMinion: true));
            weak.IsLeaderEntity = false;
            weak.SummonedThisTurn = false;
            weak.AttacksUsed = 0;
            state.GetPlayer(actor).Field.Add(weak);

            var wall = new CardInstance(
                98_000 + seed,
                1 - actor,
                new CardDefinition("rigged_wall", "Rigged Wall", attack: 10, health: 10, isMinion: true));
            wall.IsLeaderEntity = false;
            wall.SummonedThisTurn = false;
            wall.AttacksUsed = 0;
            foe.Field.Add(wall);

            var snapshot = position.Gateway.GetSnapshot(actor);
            var badTrades = snapshot.LegalActions
                .Where(a => string.Equals(a.Type, "ATTACK", StringComparison.Ordinal))
                .Where(a => a.SourceId is not null
                    && string.Equals(a.SourceId.ToString(), weak.InstanceId.ToString(), StringComparison.Ordinal)
                    && a.TargetId is not null
                    && string.Equals(a.TargetId.ToString(), wall.InstanceId.ToString(), StringComparison.Ordinal))
                .ToList();

            if (badTrades.Count == 0 || snapshot.WinnerPlayerIndex.HasValue)
            {
                foe.Field.Remove(wall);
                state.GetPlayer(actor).Field.Remove(weak);
                continue;
            }

            probed++;
            var chosen = ChooseOrFail(position, snapshot, "wood", seed);

            if (badTrades.Any(a => string.Equals(a.ActionId, chosen.ActionId, StringComparison.Ordinal)))
            {
                choseTheBadTrade.Add(
                    "seed " + seed + ": attacked a 10/10 with a 1/1 — the attacker dies and the defender "
                    + "survives, so material was lost for nothing");
            }
            else
            {
                choseSomethingElse++;
                TestContext.Out.WriteLine(
                    "seed {0}: chose {1}/{2} instead of the self-defeating trade",
                    seed, chosen.Type, chosen.CardId ?? chosen.ActionId);
            }

            foe.Field.Remove(wall);
            state.GetPlayer(actor).Field.Remove(weak);
        }

        TestContext.Out.WriteLine("self-defeating-trade positions probed: {0}", probed);
        TestContext.Out.WriteLine("  kept the material instead: {0}", choseSomethingElse);
        Assert.That(probed, Is.GreaterThan(0), "the sweep must be non-vacuous");
        Assert.That(
            choseTheBadTrade,
            Is.Empty,
            "attacking a minion that survives while the attacker dies is a pure material loss ("
            + choseTheBadTrade.Count + "):\n  " + string.Join("\n  ", choseTheBadTrade.Take(6)));
    }

    private static RuntimeLegalAction ChooseOrFail(
        Position position, RuntimeSnapshotEnvelope snapshot, string faction, int seed)
    {
        if (position.Policy.TryChoose(snapshot, snapshot.CurrentPlayer, false, out var chosen) && chosen is not null)
        {
            return chosen;
        }

        Assert.Fail(
            "the policy returned no action for " + faction + " seed " + seed
            + " in phase " + snapshot.Phase + " with " + snapshot.LegalActions.Count + " advertised actions");
        return null!;
    }

    // ------------------------------------------------------------ ④ 1-ply vs rollout

    /// <summary>
    /// ④ ONE-PLY AGREEMENT WITH A REAL ENGINE ROLLOUT.
    ///
    /// A 1-ply claim is "if I do this, the state becomes that". The engine is the only
    /// authority on what actually happens, so the check is executed rather than reasoned:
    /// submit the action, then compare the AI's own published view of the card flow against
    /// what the engine reports having done.
    ///
    /// The specific quantity compared is the punish count, because it is the one the AI's
    /// cost model is built on: the advertisement publishes it, the engine emits
    /// <c>PUNISH_DRAW count</c> when it pays it, and the two must not disagree in the
    /// direction that matters (the engine may pay LESS when a play fizzles or the deck is
    /// short, never more). This is the same law as the exact-match sweep, re-stated at the
    /// 1-ply granularity the owner asked for.
    /// </summary>
    [Test]
    public void OnePlyCardFlowAgreesWithTheEngineRollout()
    {
        var positions = 0;
        var shortfalls = 0;
        var overdraws = new List<string>();

        foreach (var actorFaction in new[] { "flame", "sea" })
        {
            var position = DriveToAction(7, actorFaction, "wood");
            var state = position.State;
            var actor = state.CurrentPlayerIndex;
            var snapshot = position.Gateway.GetSnapshot(actor);
            if (snapshot.WinnerPlayerIndex.HasValue) continue;

            var chosen = ChooseOrFail(position, snapshot, actorFaction, 7);
            var predicted = chosen.Payload is not null
                && chosen.Payload.TryGetValue("punish", out var raw)
                ? Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture)
                : 0;

            var opponent = state.GetOpponent(actor);
            var capacity = opponent.Deck.Count
                + (opponent.SkipReshuffleCredits > 0 ? 0 : opponent.Graveyard.Count);

            var before = state.Events.Items.Count;
            var submission = position.Gateway.Submit(AdvertisedActionPolicy.ToGameAction(snapshot, chosen));
            if (!submission.Result.Accepted) continue;

            positions++;

            var drawn = 0;
            for (var index = before; index < state.Events.Items.Count; index++)
            {
                var item = state.Events.Items[index];
                if (!string.Equals(item.EventType, "PUNISH_DRAW", StringComparison.Ordinal)) continue;
                if (item.Data is null) continue;
                if (!item.Data.TryGetValue("player", out var p)) continue;
                if (Convert.ToInt32(p, System.Globalization.CultureInfo.InvariantCulture) != 1 - actor) continue;
                if (item.Data.TryGetValue("count", out var c))
                {
                    drawn += Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (drawn <= predicted)
            {
                if (drawn < predicted) shortfalls++;
                continue;
            }

            overdraws.Add(
                actorFaction + ": 1-ply predicted punish=" + predicted + " but the rollout drew "
                + drawn + " (capacity=" + capacity + ")");
        }

        TestContext.Out.WriteLine("1-ply rollouts compared: {0}", positions);
        TestContext.Out.WriteLine("  engine drew fewer than the 1-ply reading (fizzle/capacity): {0}", shortfalls);
        Assert.That(positions, Is.GreaterThan(0), "the sweep must be non-vacuous");
        Assert.That(
            overdraws,
            Is.Empty,
            "a 1-ply reading must never be smaller than the rollout it predicts ("
            + overdraws.Count + "):\n  " + string.Join("\n  ", overdraws.Take(6)));
    }
}

}
