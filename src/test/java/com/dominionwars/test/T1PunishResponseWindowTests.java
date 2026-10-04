package com.dominionwars.test;

import com.dominionwars.data.Balance;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.util.Json;

import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/** T1 response allowance: one shared budget per punish root, independent of chain depth. */
final class T1PunishResponseWindowTests {
    private T1PunishResponseWindowTests() {}

    static void run() {
        TestMain.test("T1：Balance 默认、显式 1/0 配置和 toMap 往返", () -> {
            Balance defaults = new Balance();
            TestMain.eq(0, defaults.maxPunishResponsesPerRound, "缺省回退与 C# MatchRules 一致且不限制响应");
            TestMain.eq(0L, defaults.toMap().get("maxPunishResponsesPerRound"), "内置缺省序列化为 0");
            Balance oldConfig = new Balance();
            oldConfig.apply(Json.parseObject("{}"));
            TestMain.eq(0, oldConfig.maxPunishResponsesPerRound, "旧版配置缺少字段时兼容为 0");
            Balance loaded = Balance.load(Path.of("data/balance.json"));
            TestMain.eq(1, loaded.maxPunishResponsesPerRound, "正式 data/balance.json 显式配置值为 1");
            TestMain.eq(1L, loaded.toMap().get("maxPunishResponsesPerRound"), "toMap 写出配置值");

            Balance one = new Balance();
            one.apply(Json.parseObject("{\"maxPunishResponsesPerRound\":1}"));
            TestMain.eq(1, one.maxPunishResponsesPerRound, "显式 1 被读取");
            TestMain.eq(1L, one.toMap().get("maxPunishResponsesPerRound"), "显式 1 往返");

            Balance unlimited = new Balance();
            unlimited.apply(Json.parseObject("{\"maxPunishResponsesPerRound\":0}"));
            TestMain.eq(0, unlimited.maxPunishResponsesPerRound, "显式 0 表示不限制");
            TestMain.eq(0L, unlimited.toMap().get("maxPunishResponsesPerRound"), "显式 0 往返后仍为 0");
        });

        TestMain.test("T1：额度 0 时同一惩罚根可接受全部符合条件的响应", () -> {
            TestMain.TestAgent p0 = new TestMain.TestAgent();
            TestMain.TestAgent p1 = new TestMain.TestAgent(); p1.activatePunish = true;
            Balance b = new Balance(); b.maxPunishResponsesPerRound = 0; b.royalCastleEnabled = false;
            Game g = TestMain.freshGame(b, p0, p1);
            for (int i = 0; i < 3; i++) TestMain.stackDeck(g, 1, response("unlimited-" + i, 0));

            CardInstance root = TestMain.toHand(g, 0,
                    TestMain.spell("unlimited-root", 3, TestMain.fx("DAMAGE", "ENEMY_FACE", 0, "")));
            TestMain.check(g.playFromHand(root), "根惩罚牌正常打出");
            TestMain.eq(3, p1.activateCount, "0 不限制同一窗口中的合法响应数");
            TestMain.eq(3, g.players[1].graveyard.size(), "三张接受的响应均已结算");
            TestMain.check(g.players[1].hand.isEmpty(), "没有响应被额度抑制留在手中");
            TestMain.eq(0, g.chainDepth, "响应链返回后深度复位");
        });

        TestMain.test("T1：同一回合的两个独立惩罚根分别获得额度，未选牌保留状态", () -> {
            TestMain.TestAgent p0 = new TestMain.TestAgent();
            TestMain.TestAgent p1 = new TestMain.TestAgent(); p1.activatePunish = true;
            Balance b = new Balance(); b.maxPunishResponsesPerRound = 1; b.royalCastleEnabled = false;
            Game g = TestMain.freshGame(b, p0, p1);
            List<CardInstance> responses = new ArrayList<>();
            for (int i = 0; i < 6; i++) {
                CardInstance c = TestMain.stackDeck(g, 1, response("independent-" + i, 0));
                c.def.tags.add("T1-tag-" + i);
                responses.add(c);
            }

            CardInstance first = TestMain.toHand(g, 0,
                    TestMain.spell("independent-root-1", 3, TestMain.fx("DAMAGE", "ENEMY_FACE", 0, "")));
            CardInstance second = TestMain.toHand(g, 0,
                    TestMain.spell("independent-root-2", 3, TestMain.fx("DAMAGE", "ENEMY_FACE", 0, "")));
            TestMain.check(g.playFromHand(first), "第一根惩罚正常结算");
            TestMain.check(g.playFromHand(second), "第二根惩罚正常结算");

            TestMain.eq(1, g.turnNumber, "两根事件仍在同一回合");
            TestMain.eq(2, p1.activateCount, "每个独立根分别允许一次响应");
            List<CardInstance> retained = new ArrayList<>();
            for (CardInstance c : responses) if (g.players[1].hand.contains(c)) retained.add(c);
            TestMain.eq(4, retained.size(), "两根事件各压制两张候选响应");
            for (CardInstance c : retained) {
                TestMain.check(c.punishActivated, "额度压制不清除惩罚激活状态：" + c.def.id);
                TestMain.check(!g.players[1].usedTags.contains("T1-tag-" + c.def.id.substring("independent-".length())),
                        "未接受响应不消耗词条：" + c.def.id);
            }
        });

        TestMain.test("T1：嵌套反制共享根额度，chainLimit 仍独立限制深度", () -> {
            TestMain.TestAgent p0 = new TestMain.TestAgent(); p0.activatePunish = true;
            TestMain.TestAgent p1 = new TestMain.TestAgent(); p1.activatePunish = true;
            Balance b = new Balance(); b.maxPunishResponsesPerRound = 1; b.chainLimit = 20;
            b.royalCastleEnabled = false;
            Game g = TestMain.freshGame(b, p0, p1);
            TestMain.stackDeck(g, 1, response("nested-p1-response", 1));
            CardInstance suppressed = TestMain.stackDeck(g, 0, response("nested-p0-response", 0));
            CardInstance root = TestMain.toHand(g, 0,
                    TestMain.spell("nested-root", 1, TestMain.fx("DAMAGE", "ENEMY_FACE", 0, "")));

            TestMain.check(g.playFromHand(root), "嵌套响应根牌正常打出");
            TestMain.eq(1, p1.activateCount, "第一层反制被接受");
            TestMain.eq(0, p0.activateCount, "第二层嵌套反制被同一根额度压制，未再询问");
            TestMain.check(g.players[0].hand.contains(suppressed), "被压制的嵌套响应仍留在手牌");
            TestMain.check(suppressed.punishActivated, "被压制的嵌套响应保持已激活");
            TestMain.eq(0, g.chainDepth, "嵌套完成后 chainDepth 复位");
        });
    }

    private static CardDef response(String id, int punishCost) {
        CardDef c = TestMain.spell(id, 0, TestMain.fx("DAMAGE", "ENEMY_FACE", 0, ""));
        c.punishActivatable = true;
        c.punishCost = punishCost;
        c.punishCondition = "ALWAYS";
        c.punishEffects.add(TestMain.fx("DAMAGE", "ENEMY_FACE", 0, ""));
        return c;
    }
}
