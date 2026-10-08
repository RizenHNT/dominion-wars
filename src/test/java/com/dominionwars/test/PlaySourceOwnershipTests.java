package com.dominionwars.test;

import com.dominionwars.data.Balance;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import java.util.List;

/** A played source cannot remain a random-discard candidate during its payment response. */
final class PlaySourceOwnershipTests {
    private PlaySourceOwnershipTests() {}

    static void run() {
        for (int shape = 0; shape < 3; shape++) {
            final int currentShape = shape;
            TestMain.test("出牌源唯一归属：退潮反打不会复制牌形态 " + shape, () -> {
                TestMain.TestAgent a0 = new TestMain.TestAgent();
                TestMain.TestAgent a1 = new TestMain.TestAgent();
                a1.activatePunish = true;
                Balance b = new Balance();
                b.maxPunishResponsesPerRound = 1;
                b.royalCastleEnabled = false;
                Game g = TestMain.freshGame(b, a0, a1);
                CardDef def = currentShape == 0
                        ? TestMain.minion("ownership-minion", 2, 2, 1, "source")
                        : TestMain.spell("ownership-spell-" + currentShape, 1,
                                TestMain.fx("DRAW", "", 1, ""));
                if (currentShape == 2) def.chant = 2;
                CardInstance source = TestMain.toHand(g, 0, def);
                TestMain.stackDeck(g, 0, TestMain.vanilla("source-draw-filler"));
                CardInstance tide = TestMain.stackDeck(g, 1, TestMain.realLibrary().get("sea_tide"));
                TestMain.check(g.playFromHand(source), "源卡应按正常行动路径打出");
                TestMain.eq(1, a1.activateCount, "实际触发生产退潮的惩罚响应");
                TestMain.check(g.players[1].graveyard.contains(tide), "退潮实际完成结算");
                TestMain.eq(0, g.players[0].totalDiscarded, "正在结算的源不是手牌弃置候选");
                TestMain.eq(1, occurrences(g.players[0], source), "源卡恰好位于一个区域一次");
                TestMain.check(!g.players[0].hand.contains(source), "源已离开手牌");
                if (currentShape == 1) {
                    TestMain.check(g.players[0].graveyard.contains(source), "即时咒文仅入墓地一次");
                    TestMain.eq(1, g.players[0].hand.size(), "普通抽牌效果仍执行一次");
                } else {
                    TestMain.check(g.players[0].field.contains(source), "随从或吟唱牌只在战场");
                    TestMain.check(!g.players[0].graveyard.contains(source), "不得同时在墓地");
                    if (currentShape == 2) TestMain.eq(2, source.chantRemaining, "吟唱时点不提前");
                }
            });
        }
        TestMain.test("出牌源唯一归属：退潮仍弃掉其他手牌", () -> {
            TestMain.TestAgent a0 = new TestMain.TestAgent();
            TestMain.TestAgent a1 = new TestMain.TestAgent(); a1.activatePunish = true;
            Balance b = new Balance(); b.maxPunishResponsesPerRound = 1; b.royalCastleEnabled = false;
            Game g = TestMain.freshGame(b, a0, a1);
            CardInstance source = TestMain.toHand(g, 0, TestMain.minion("reserved", 2, 2, 1, "source"));
            CardInstance other = TestMain.toHand(g, 0, TestMain.vanilla("discard-other"));
            TestMain.stackDeck(g, 1, TestMain.realLibrary().get("sea_tide"));
            TestMain.check(g.playFromHand(source), "正常出牌");
            TestMain.eq(1, g.players[0].totalDiscarded, "只排除源，不屏蔽正常反打弃牌");
            TestMain.check(g.players[0].graveyard.contains(other), "其他手牌被弃");
            TestMain.check(g.players[0].field.contains(source), "源仍正常入场");
            TestMain.eq(1, occurrences(g.players[0], source), "源唯一");
        });
    }

    private static int occurrences(PlayerState player, CardInstance source) {
        int count = 0;
        for (List<CardInstance> zone : List.of(player.hand, player.deck, player.field,
                player.graveyard, player.ambushes, player.commitQueue, player.cloudStack)) {
            for (CardInstance card : zone) if (card == source) count++;
        }
        return count;
    }
}
