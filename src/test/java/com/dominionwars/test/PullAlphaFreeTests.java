package com.dominionwars.test;

import com.dominionwars.data.Balance;
import com.dominionwars.ai.AiAgent;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Effects;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.server.GameSession;
import com.dominionwars.server.WebHumanAgent;

import java.util.ArrayList;
import java.util.Arrays;

/** Narrow parity coverage for the owner-approved machine Alpha PULL fee. */
public final class PullAlphaFreeTests {
    private PullAlphaFreeTests() {}

    static void run() {
        TestMain.test("机械：真实地标晋升 Alpha 后 PULL 免费，离场恢复泰坦费用", PullAlphaFreeTests::productionLifecycle);
        TestMain.test("机械：敌方 Alpha 或非统领同名卡不授予免费 PULL", PullAlphaFreeTests::alphaIdentityAndOwner);
        TestMain.test("机械：AI askPull 收到有效零值并按引擎规则继续处理", PullAlphaFreeTests::aiReceivesEffectiveCost);
        TestMain.test("机械：WebHumanAgent 的零费 PULL 不产生惩罚抽牌或待处理弹问", PullAlphaFreeTests::webHumanFreePull);
    }

    private static void productionLifecycle() {
        Game g = TestMain.freshGame(TestMain.bal(), new TestMain.TestAgent(), new TestMain.TestAgent());
        g.library = TestMain.realLibrary();
        TestMain.fillDeck(g, 0, 20);
        TestMain.fillDeck(g, 1, 40);
        PlayerState owner = g.players[0];
        PlayerState opponent = g.players[1];
        CardInstance landmark = TestMain.drawLeaderThroughProductionPath(g, 0, "machine_leader");
        CardInstance carrier = TestMain.toField(g, 0, g.library.get("machine_golem"));
        CardDef titanDef = g.library.get("machine_titan");
        CardDef normalCostDef = g.library.get("machine_drone");
        CardInstance firstDrone = TestMain.toCloud(g, 0, normalCostDef);

        TestMain.eq(1, g.effectiveDownloadCost(owner, firstDrone), "before Alpha, machine_drone uses its printed downloadCost");
        int firstDeckSize = opponent.deck.size();
        TestMain.check(g.pullWith(landmark), "production machine_leader can receive the first PULL");
        TestMain.eq(firstDeckSize - 1, opponent.deck.size(), "pre-Alpha PULL draws exactly one");

        CardInstance secondPull = TestMain.toCloud(g, 0, normalCostDef);
        int secondDeckSize = opponent.deck.size();
        TestMain.check(g.pullWith(landmark), "production landmark completes its second PULL");
        TestMain.eq(secondDeckSize - secondPull.def.downloadCost, opponent.deck.size(), "second pre-Alpha PULL uses the card's printed value");
        TestMain.eq("machine_alpha", landmark.pendingLandmarkSummonCardId, "tier two schedules the production Alpha");
        g.endTurn();

        CardInstance alpha = owner.leaderOnField;
        TestMain.check(alpha != null && "machine_alpha".equals(alpha.def.id)
                        && alpha.isLeaderEntity && owner.field.contains(alpha),
                "the existing production landmark path enters the real Alpha minion");
        g.currentIdx = 0;
        g.phase = Game.Phase.ACTION;
        CardInstance freeTitan = TestMain.toCloud(g, 0, titanDef);
        TestMain.eq(2, titanDef.downloadCost, "the high-cost free case is the production machine_titan");
        TestMain.eq(0, g.effectiveDownloadCost(owner, freeTitan), "active Alpha sets PULL cost to zero despite Titan cost two");
        int freeDeckSize = opponent.deck.size();
        int freeHandSize = opponent.hand.size();
        int pullCountBeforeFree = owner.pullCount;
        Effects.resolve(g, 0, carrier, Arrays.asList(TestMain.fx("PULL", "", 1, "")), new Effects.Ctx());
        TestMain.eq(pullCountBeforeFree + 1, owner.pullCount, "the production PULL effect still performs the action");
        TestMain.eq(freeDeckSize, opponent.deck.size(), "active Alpha PULL draws no punishment cards");
        TestMain.eq(freeHandSize, opponent.hand.size(), "free PULL does not change the opponent hand");
        TestMain.check(owner.graveyard.contains(freeTitan), "the free PULL still moves the cloud top to graveyard");

        owner.field.remove(alpha);
        owner.leaderOnField = null;
        owner.graveyard.add(alpha);
        CardInstance afterDepartureDrone = TestMain.toCloud(g, 0, normalCostDef);
        TestMain.eq(1, g.effectiveDownloadCost(owner, afterDepartureDrone), "leaving the active leader position restores the printed normal cost");
        int restoredDeckSize = opponent.deck.size();
        TestMain.check(g.pullWith(carrier), "the remaining real mechanical carrier can PULL after Alpha leaves");
        TestMain.eq(restoredDeckSize - 1, opponent.deck.size(), "post-Alpha-departure PULL draws exactly one");
        TestMain.eq(4, owner.pullCount, "all four production-path pulls count normally");
    }

    private static void alphaIdentityAndOwner() {
        Game g = TestMain.freshGame(TestMain.bal(), new TestMain.TestAgent(), new TestMain.TestAgent());
        g.library = TestMain.realLibrary();
        PlayerState owner = g.players[0];
        PlayerState enemy = g.players[1];
        CardInstance titan = new CardInstance(g.library.get("machine_titan"), 0);
        CardInstance carrier = TestMain.toField(g, 0, g.library.get("machine_golem"));
        CardInstance lookalike = TestMain.toField(g, 0, g.library.get("machine_alpha"));
        CardInstance enemyAlpha = new CardInstance(g.library.get("machine_alpha"), 1);
        enemyAlpha.isLeaderEntity = true;
        enemy.leaderOnField = enemyAlpha;
        enemy.field.add(enemyAlpha);

        TestMain.eq(2, g.effectiveDownloadCost(owner, titan),
                "a same-ID card that is not an entered leader and the enemy's real Alpha do not grant free PULL");
        TestMain.check(g.isDownloadCarrier(owner, lookalike),
                "a same-ID faction minion may still be a normal mechanical carrier");
        TestMain.eq(2, g.effectiveDownloadCost(owner, titan),
                "normal carrier eligibility does not make a non-leader Alpha lookalike free");
        TestMain.check(g.isDownloadCarrier(enemy, enemyAlpha), "the enemy Alpha remains a valid carrier only for its own player");
        TestMain.check(g.isDownloadCarrier(owner, carrier), "owner retains the independent mechanical carrier");
    }

    private static void webHumanFreePull() {
        GameSession session = new GameSession(
                new Balance(),
                new ArrayList<>(),
                new ArrayList<>(),
                "Human",
                "Machine",
                true);
        Game g = session.game;
        g.library = TestMain.realLibrary();
        PlayerState human = g.players[0];
        PlayerState machine = g.players[1];
        g.currentIdx = 1;
        g.phase = Game.Phase.ACTION;
        g.turnNumber = 1;
        for (int i = 0; i < 8; i++) human.deck.add(new CardInstance(TestMain.vanilla("web-no-punish-" + i), 0));
        human.deck.add(new CardInstance(g.library.get("wood_punish_wrath"), 0));
        CardInstance carrier = TestMain.toField(g, 1, g.library.get("machine_golem"));
        CardInstance normalCost = TestMain.toCloud(g, 1, g.library.get("machine_drone"));
        int deckBefore = human.deck.size();
        int handBefore = human.hand.size();

        final boolean[] firstPullResult = {false};
        Thread pullThread = new Thread(() -> firstPullResult[0] = g.pullWith(carrier), "b2b-web-positive-pull");
        pullThread.setDaemon(true);
        pullThread.start();
        WebHumanAgent.Pending pending = waitForPending(session, pullThread, 2000);
        if (pending != null) {
            TestMain.eq("activatePunish", pending.kind, "positive normal fee opens the existing Web activation prompt");
            TestMain.check(answerPending(session, pending, pullThread, 2000),
                    "the positive-fee Web response can safely decline");
        } else if (pullThread.isAlive()) {
            pullThread.interrupt();
        }
        try {
            pullThread.join(2000);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new AssertionError(e);
        }
        TestMain.check(!pullThread.isAlive(), "the positive-fee Web PULL completes within the bounded response wait");
        TestMain.check(pending != null && firstPullResult[0], "positive-fee PULL actually reached and completed its response window");
        TestMain.check(machine.graveyard.contains(normalCost), "positive-fee comparison PULL reached the graveyard");

        CardInstance alpha = new CardInstance(g.library.get("machine_alpha"), 1);
        alpha.isLeaderEntity = true;
        machine.leaderOnField = alpha;
        machine.field.add(alpha);
        CardInstance titan = TestMain.toCloud(g, 1, g.library.get("machine_titan"));
        int freeDeckBefore = human.deck.size();
        int freeHandBefore = human.hand.size();
        int freePunishBefore = human.punishDrawnThisTurn;
        TestMain.check(g.pullWith(carrier), "the same Web session completes a high-cost Alpha-free PULL");

        TestMain.check(session.human.pending() == null, "WebHumanAgent has no pending activation prompt for zero fee");
        TestMain.eq(freeDeckBefore, human.deck.size(), "zero fee does not draw from the human deck");
        TestMain.eq(freeHandBefore, human.hand.size(), "zero fee does not change the human hand");
        TestMain.eq(freePunishBefore, human.punishDrawnThisTurn, "free PULL does not add a punishment draw");
        TestMain.check(machine.graveyard.contains(titan) && !machine.cloudStack.contains(titan),
                "the assertion observes an actual PULL, not a skipped Web prompt");
        TestMain.eq(2, machine.pullCount, "both positive and free Web PULLs increment the count normally");
        TestMain.check(deckBefore - human.deck.size() >= 1, "the positive comparison actually drew from the Web player's deck");
        session.close();
    }

    private static void aiReceivesEffectiveCost() {
        RecordingAiAgent ai = new RecordingAiAgent();
        Game g = new Game(
                TestMain.bal(),
                new ArrayList<>(),
                new ArrayList<>(),
                "甲",
                "乙",
                ai,
                new TestMain.TestAgent(),
                42L,
                0);
        g.phase = Game.Phase.ACTION;
        g.turnNumber = 1;
        g.library = TestMain.realLibrary();
        TestMain.fillDeck(g, 1, 20);
        PlayerState owner = g.players[0];
        CardInstance alpha = new CardInstance(g.library.get("machine_alpha"), 0);
        alpha.isLeaderEntity = true;
        owner.leaderOnField = alpha;
        owner.field.add(alpha);
        owner.field.add(new CardInstance(g.library.get("machine_golem"), 0));
        CardInstance titan = TestMain.toCloud(g, 0, g.library.get("machine_titan"));

        ai.playOneStep(g);

        TestMain.eq(0, ai.lastAskedPullCost, "AiAgent.askPull receives the helper's effective zero");
        TestMain.eq(1, owner.pullCount, "the AI policy's accepted high-cost card actually PULLs");
        TestMain.eq(0, g.players[1].punishDrawnThisTurn, "accepted free AI PULL spends no punishment draw");
        TestMain.check(owner.graveyard.contains(titan), "the AI assertion observes a completed PULL");
    }

    private static WebHumanAgent.Pending waitForPending(GameSession session, Thread thread, long timeoutMs) {
        long deadline = System.nanoTime() + timeoutMs * 1_000_000L;
        while (System.nanoTime() < deadline && thread.isAlive()) {
            WebHumanAgent.Pending pending = session.human.pending();
            if (pending != null) return pending;
            try {
                Thread.sleep(5);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new AssertionError(e);
            }
        }
        return session.human.pending();
    }

    private static boolean answerPending(GameSession session, WebHumanAgent.Pending pending, Thread thread, long timeoutMs) {
        long deadline = System.nanoTime() + timeoutMs * 1_000_000L;
        while (System.nanoTime() < deadline && thread.isAlive()) {
            if (session.human.answer(pending.id, -1)) return true;
            try {
                Thread.sleep(5);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new AssertionError(e);
            }
        }
        return false;
    }

    private static final class RecordingAiAgent extends AiAgent {
        private int lastAskedPullCost = -1;

        @Override
        public boolean askPull(Game g, int playerIdx, CardInstance cloudTop, CardInstance carrier, int downloadCost) {
            lastAskedPullCost = downloadCost;
            return super.askPull(g, playerIdx, cloudTop, carrier, downloadCost);
        }
    }
}
