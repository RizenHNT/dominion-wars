package com.dominionwars.test;

import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Effects;
import com.dominionwars.engine.Game;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.EffectSpec;

import java.util.Arrays;

/**
 * Java parity fixtures for the explicitly frozen ENFEEBLE/BANISH/CONTROL
 * semantics in RULES §13.2 and effects.contract.md §11.1.
 *
 * <p>These cases intentionally pass an explicit target id when the fixture
 * has more than one possible target.  The same initial state and expected
 * state transitions are handed to the C# action_localization_slice; this
 * file only exercises the Java engine.</p>
 */
final class EffectParityTests {
    private EffectParityTests() {}

    private static EffectSpec effect(String action, String target, int amount, String param) {
        return TestMain.fx(action, target, amount, param);
    }

    static void run() {
        TestMain.test("效果对齐：ENFEEBLE 负值同时压低攻击与生命", () -> {
            TestMain.TestAgent a0 = new TestMain.TestAgent();
            TestMain.TestAgent a1 = new TestMain.TestAgent();
            Game g = TestMain.freshGame(TestMain.bal(), a0, a1);
            CardInstance source = TestMain.toField(g, 0, TestMain.minion("无力来源", 1, 4, 0));
            CardInstance target = TestMain.toField(g, 1, TestMain.minion("无力目标", 2, 5, 0));

            checkKnownActions();
            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("ENFEEBLE", "ENEMY_MINION", -1, "both")),
                    new Effects.Ctx(), target.uid);

            TestMain.eq(1, target.attack, "ENFEEBLE both：攻击 2-1");
            TestMain.eq(4, target.health, "ENFEEBLE both：生命 5-1");
            TestMain.eq(4, target.maxHealth, "ENFEEBLE both：最大生命 5-1");
            TestMain.check(g.players[1].field.contains(target), "非致死无力目标仍在原控制者场上");
            TestMain.eq(1, target.controllerIdx(), "无 CONTROL 时控制者仍是拥有者");
        });

        TestMain.test("效果对齐：ENFEEBLE 非法 amount/param 与失效目标均 fail-closed", () -> {
            Game g = TestMain.freshGame(TestMain.bal(),
                    new TestMain.TestAgent(), new TestMain.TestAgent());
            CardInstance source = TestMain.toField(g, 0, TestMain.minion("无力来源边界", 1, 4, 0));
            CardInstance first = TestMain.toField(g, 1, TestMain.minion("边界目标甲", 5, 6, 0));
            CardInstance second = TestMain.toField(g, 1, TestMain.minion("边界目标乙", 7, 8, 0));

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("ENFEEBLE", "ENEMY_MINION", 1, "both")),
                    new Effects.Ctx(), first.uid);
            TestMain.eq(5, first.attack, "ENFEEBLE 正 amount 不改攻击");
            TestMain.eq(6, first.health, "ENFEEBLE 正 amount 不改生命");

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("ENFEEBLE", "ENEMY_MINION", -1, "invalid")),
                    new Effects.Ctx(), first.uid);
            TestMain.eq(5, first.attack, "ENFEEBLE 非法 param 不改攻击");
            TestMain.eq(6, first.health, "ENFEEBLE 非法 param 不改生命");

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("ENFEEBLE", "ENEMY_MINION", -1, "atk")),
                    new Effects.Ctx(), first.uid + second.uid + 100000);
            TestMain.eq(5, first.attack, "显式失效目标不回退到第一张候选");
            TestMain.eq(7, second.attack, "显式失效目标不改第二张候选");
        });

        TestMain.test("效果对齐：ENFEEBLE 致死走拥有者墓地的普通死亡清理", () -> {
            Game g = TestMain.freshGame(TestMain.bal(),
                    new TestMain.TestAgent(), new TestMain.TestAgent());
            CardInstance source = TestMain.toField(g, 0, TestMain.minion("致死来源", 1, 4, 0));
            CardInstance target = TestMain.toField(g, 1, TestMain.minion("濒死目标", 2, 1, 0));
            target.health = 1;
            target.maxHealth = 1;

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("ENFEEBLE", "ENEMY_MINION", -2, "hp")),
                    new Effects.Ctx(), target.uid);

            TestMain.check(!g.players[1].field.contains(target), "致死目标离开当前控制者场上");
            TestMain.check(g.players[1].graveyard.contains(target), "致死目标进入原拥有者墓地");
            TestMain.check(!g.players[0].graveyard.contains(target), "致死目标不进入效果来源墓地");
        });

        TestMain.test("效果对齐：BANISH 受控制目标回原拥有者牌库且不进墓地", () -> {
            Game g = TestMain.freshGame(TestMain.bal(),
                    new TestMain.TestAgent(), new TestMain.TestAgent());
            CardInstance source = TestMain.toField(g, 0, TestMain.minion("驱逐来源", 1, 4, 0));
            CardInstance target = TestMain.toField(g, 1, TestMain.minion("被驱逐目标", 2, 5, 0));

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("BANISH", "ENEMY_MINION", 0, "")),
                    new Effects.Ctx(), target.uid);

            TestMain.check(!g.players[1].field.contains(target), "BANISH 从当前控制者场上移除");
            TestMain.check(g.players[1].deck.contains(target), "BANISH 放回原拥有者牌库");
            TestMain.check(!g.players[0].graveyard.contains(target), "BANISH 不进控制者墓地");
            TestMain.check(!g.players[1].graveyard.contains(target), "BANISH 不进拥有者墓地");
            TestMain.eq(1, target.ownerIdx, "BANISH 不改变拥有者");
            TestMain.eq(1, target.controllerIdx(), "BANISH 重置控制权");
            TestMain.eq(2, target.attack, "BANISH 保持定义攻击状态");
            TestMain.eq(5, target.health, "BANISH 保持定义生命状态");

            // 同一动作再覆盖控制者与拥有者不同的场景：P1 仍收牌，P0 场上只负责移除。
            CardInstance controlled = new CardInstance(TestMain.minion("被操纵后驱逐", 9, 7, 0), 1);
            g.players[0].field.add(controlled);
            controlled.controlledByIdx = 0;
            controlled.controlTurnsRemaining = 2;
            controlled.attack = 1;
            controlled.health = 2;
            controlled.maxHealth = 2;
            CardInstance p1Source = TestMain.toField(g, 1, TestMain.minion("驱逐来源乙", 1, 4, 0));

            Effects.resolve(g, 1, p1Source,
                    Arrays.asList(effect("BANISH", "ENEMY_MINION", 0, "")),
                    new Effects.Ctx(), controlled.uid);

            TestMain.check(!g.players[0].field.contains(controlled), "BANISH 从临时控制者场上移除");
            TestMain.check(g.players[1].deck.contains(controlled), "BANISH 仍回原拥有者牌库");
            TestMain.check(!g.players[0].graveyard.contains(controlled), "受控制目标不进控制者墓地");
            TestMain.eq(1, controlled.controllerIdx(), "BANISH 清除临时控制者");
            TestMain.eq(9, controlled.attack, "BANISH 清除受控制目标的运行时攻击变化");
            TestMain.eq(7, controlled.health, "BANISH 清除受控制目标的运行时生命变化");
        });

        TestMain.test("效果对齐：CONTROL 移动场上归属、使用控制者攻击并在回合末归还", () -> {
            Game g = TestMain.freshGame(TestMain.bal(),
                    new TestMain.TestAgent(), new TestMain.TestAgent());
            CardInstance source = TestMain.toField(g, 0, TestMain.minion("操纵来源", 1, 4, 0));
            CardInstance target = TestMain.toField(g, 1, TestMain.minion("操纵目标", 2, 5, 0));

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("CONTROL", "ENEMY_MINION", 1, "")),
                    new Effects.Ctx(), target.uid);

            TestMain.check(g.players[0].field.contains(target), "CONTROL 移至来源方场上");
            TestMain.check(!g.players[1].field.contains(target), "CONTROL 离开原控制者场上");
            TestMain.eq(1, target.ownerIdx, "CONTROL 不改变原拥有者");
            TestMain.eq(0, target.controllerIdx(), "CONTROL 当前控制者为来源方");
            TestMain.eq(1, target.controlTurnsRemaining, "CONTROL 保存时限");

            TestMain.check(g.attack(target, null), "当前控制者可以用被操纵随从攻击");
            TestMain.eq(1, target.attacksUsed, "被操纵随从记录控制者回合的攻击");
            g.endTurn();

            TestMain.check(g.players[1].field.contains(target), "控制者回合结束后归还拥有者场上");
            TestMain.check(!g.players[0].field.contains(target), "归还时移出临时控制者场上");
            TestMain.eq(1, target.ownerIdx, "归还不改变拥有者");
            TestMain.eq(1, target.controllerIdx(), "归还后控制者恢复拥有者");
            TestMain.eq(null, target.controlledByIdx, "归还清除临时控制者");
            TestMain.eq(0, target.controlTurnsRemaining, "归还清除控制时限");
        });

        TestMain.test("效果对齐：CONTROL 非正时限与统领目标均不改变状态", () -> {
            Game g = TestMain.freshGame(TestMain.bal(),
                    new TestMain.TestAgent(), new TestMain.TestAgent());
            CardInstance source = TestMain.toField(g, 0, TestMain.minion("操纵边界来源", 1, 4, 0));
            CardDef leaderDef = TestMain.leaderMinion("不可操纵统领", 2, 5);
            CardInstance leader = TestMain.toField(g, 1, leaderDef);
            leader.isLeaderEntity = true;
            g.players[1].leaderOnField = leader;

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("CONTROL", "ENEMY_MINION", 0, "")),
                    new Effects.Ctx(), leader.uid);
            TestMain.check(g.players[1].field.contains(leader), "CONTROL 非正时限不移动统领");
            TestMain.eq(1, leader.controllerIdx(), "CONTROL 非正时限不改统领控制权");

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("CONTROL", "ENEMY_MINION", 1, "")),
                    new Effects.Ctx(), leader.uid);
            TestMain.check(g.players[1].field.contains(leader), "统领目标保持原场上");
            TestMain.eq(1, leader.controllerIdx(), "统领免疫 CONTROL");
        });

        TestMain.test("效果对齐：效果来源控制权不匹配时不结算", () -> {
            Game g = TestMain.freshGame(TestMain.bal(),
                    new TestMain.TestAgent(), new TestMain.TestAgent());
            CardInstance source = TestMain.toField(g, 0, TestMain.minion("失效来源", 1, 4, 0));
            CardInstance target = TestMain.toField(g, 1, TestMain.minion("失效来源目标", 5, 6, 0));
            g.players[0].field.remove(source);
            g.players[1].field.add(source);
            source.controlledByIdx = 1;
            source.controlTurnsRemaining = 1;

            Effects.resolve(g, 0, source,
                    Arrays.asList(effect("ENFEEBLE", "ENEMY_MINION", -1, "both")),
                    new Effects.Ctx(), target.uid);

            TestMain.eq(5, target.attack, "错误来源控制权不改变目标攻击");
            TestMain.eq(6, target.health, "错误来源控制权不改变目标生命");
            TestMain.check(g.players[1].field.contains(source), "来源仍在当前控制者场上");
        });
    }

    private static void checkKnownActions() {
        TestMain.check(Effects.isKnownAction("ENFEEBLE"), "ENFEEBLE 已登记");
        TestMain.check(Effects.isKnownAction("BANISH"), "BANISH 已登记");
        TestMain.check(Effects.isKnownAction("CONTROL"), "CONTROL 已登记");
    }
}
