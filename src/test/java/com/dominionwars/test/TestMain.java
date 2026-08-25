package com.dominionwars.test;

import com.dominionwars.data.Balance;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Effects;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerAgent;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.AmbushKind;
import com.dominionwars.model.CardDef.CardType;
import com.dominionwars.model.CardDef.EffectSpec;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * 《统御战纪》规则引擎测试（零依赖自写框架）。
 * 运行：java -cp build/classes:build/test-classes com.dominionwars.test.TestMain
 */
public class TestMain {

    // ===================== 微型测试框架 =====================
    static int passed = 0, failed = 0;
    static List<String> failures = new ArrayList<>();

    static void check(boolean cond, String msg) {
        if (!cond) throw new AssertionError(msg);
    }
    static void eq(Object expect, Object actual, String msg) {
        if (expect == null ? actual != null : !expect.equals(actual))
            throw new AssertionError(msg + "：期望 " + expect + "，实际 " + actual);
    }

    interface TestCase { void run() throws Exception; }

    static void test(String name, TestCase t) {
        try {
            t.run();
            passed++;
            System.out.println("  ✓ " + name);
        } catch (Throwable e) {
            failed++;
            failures.add(name + " —— " + e.getMessage());
            System.out.println("  ✗ " + name + " —— " + e.getMessage());
        }
    }

    // ===================== 测试用代理（可脚本化） =====================
    static class TestAgent implements PlayerAgent {
        boolean activatePunish = false;     // 被惩罚抽到可发动牌时是否发动
        boolean triggerAmbush = true;       // 是否触发伏击
        @Override public boolean askActivatePunish(Game g, int idx, CardInstance c, int cost) { return activatePunish; }
        @Override public CardInstance chooseTarget(Game g, int idx, List<CardInstance> opts, String prompt, boolean optional) {
            return opts.isEmpty() ? null : opts.get(0);
        }
        @Override public CardInstance chooseDiscard(Game g, int idx, List<CardInstance> hand) {
            return hand.isEmpty() ? null : hand.get(0);
        }
        @Override public CardInstance chooseAmbush(Game g, int idx, List<CardInstance> cands, String desc) {
            return (!triggerAmbush || cands.isEmpty()) ? null : cands.get(0);
        }
    }

    // ===================== 构造辅助 =====================
    static CardDef minion(String id, int atk, int hp, int punish, String... tags) {
        CardDef c = new CardDef();
        c.id = id; c.name = id; c.type = CardType.MINION;
        c.attack = atk; c.health = hp; c.punish = punish;
        c.tags.addAll(Arrays.asList(tags));
        return c;
    }
    static CardDef spell(String id, int punish, EffectSpec... fx) {
        CardDef c = new CardDef();
        c.id = id; c.name = id; c.type = CardType.SPELL; c.punish = punish;
        c.onPlayEffects.addAll(Arrays.asList(fx));
        return c;
    }
    static EffectSpec fx(String action, String target, int amount, String param) {
        EffectSpec e = new EffectSpec();
        e.action = action; e.target = target; e.amount = amount; e.param = param;
        return e;
    }
    static CardDef vanilla(String id) { return minion(id, 1, 1, 0); }

    /** 创建空局：双方空卡组、手动布置，phase=ACTION、P0 行动 */
    static Game freshGame(Balance b, TestAgent a0, TestAgent a1) {
        Game g = new Game(b, new ArrayList<>(), new ArrayList<>(), "甲", "乙", a0, a1, 42L, 0);
        g.phase = Game.Phase.ACTION;
        g.turnNumber = 1;
        return g;
    }
    static Balance bal() { return new Balance(); }

    /** 向卡组顶部(末尾)压入卡 */
    static CardInstance stackDeck(Game g, int p, CardDef def) {
        CardInstance c = new CardInstance(def, p);
        g.players[p].deck.add(c);
        return c;
    }
    static CardInstance toHand(Game g, int p, CardDef def) {
        CardInstance c = new CardInstance(def, p);
        g.players[p].hand.add(c);
        return c;
    }
    static CardInstance toField(Game g, int p, CardDef def) {
        CardInstance c = new CardInstance(def, p);
        c.summonedThisTurn = false;
        g.players[p].field.add(c);
        return c;
    }
    static void fillDeck(Game g, int p, int n) {
        for (int i = 0; i < n; i++) stackDeck(g, p, vanilla("填充" + p + "_" + i));
    }

    static CardDef leaderMinion(String id, int atk, int hp) {
        CardDef c = minion(id, atk, hp, 0);
        c.leader = true;
        c.leaderDef.vulnerabilities.add("DAMAGE");
        return c;
    }

    // ===================== 测试用例 =====================
    public static void main(String[] args) {
        System.out.println("== 统御战纪 引擎规则测试 ==");

        test("惩罚值：打出惩罚3的卡牌使对方抽3张", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 1, 10);
            CardInstance c = toHand(g, 0, spell("火舌", 3, fx("DAMAGE", "ENEMY_FACE", 1, "")));
            g.playFromHand(c);
            eq(3, g.players[1].hand.size(), "对方手牌");
            eq(7, g.players[1].deck.size(), "对方卡组");
        });

        test("负攻击 Buff：结算时 clamp 到0", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardInstance target = toField(g, 0, minion("攻击目标", 2, 5, 0));
            Effects.resolve(g, 0, null,
                    Arrays.asList(fx("BUFF", "FRIENDLY_MINION", -99, "atk")), new Effects.Ctx());
            eq(0, target.attack, "攻击力不能为负");
        });

        test("延迟死亡：同一效果链中负血后回血可以存活", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardInstance target = toField(g, 0, minion("濒死目标", 2, 5, 0));
            target.health = 1; target.maxHealth = 5;
            Effects.resolve(g, 0, null, Arrays.asList(
                    fx("BUFF", "FRIENDLY_MINION", -3, "hp"),
                    fx("HEAL", "FRIENDLY_MINION", 3, "")
            ), new Effects.Ctx());
            check(g.players[0].field.contains(target), "效果链结束前回血，随从留场");
            eq(1, target.health, "回血后的生命值");
            eq(2, target.maxHealth, "最大生命值同步 clamp");
        });

        test("延迟死亡：效果链结束仍为负血则进入墓地", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardInstance target = toField(g, 0, minion("死亡目标", 2, 5, 0));
            target.health = 1;
            Effects.resolve(g, 0, null,
                    Arrays.asList(fx("BUFF", "FRIENDLY_MINION", -3, "hp")), new Effects.Ctx());
            check(!g.players[0].field.contains(target), "负血随从离场");
            check(g.players[0].graveyard.contains(target), "负血随从进入墓地");
        });

        test("裁决③：惩罚值超出对方卡组余量→空发并强制结束回合", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 10); fillDeck(g, 1, 2);          // 对方仅2张
            CardInstance enemy = toField(g, 1, minion("靶子", 2, 5, 0));
            CardInstance c = toHand(g, 0, spell("豪火", 3, fx("DAMAGE", "ENEMY_MINION", 9, "")));
            g.playFromHand(c);
            eq(5, enemy.health, "空发不结算效果");
            check(g.players[0].graveyard.contains(c), "空发卡进墓地（计为使用）");
            eq(0, g.players[1].punishDrawnThisTurn, "对方未因惩罚抽牌");
            eq(1, g.currentIdx, "回合被强制结束，轮到对方");
            eq(2, g.players[1].hand.size(), "对方仅获得新回合的正常抽牌(1+后手1)");
        });

        test("裁决③：惩罚值恰好抽空对方卡组合法，之后仍可打0费卡", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 1, 2);
            CardInstance enemy = toField(g, 1, minion("靶子", 1, 6, 0));
            CardInstance c1 = toHand(g, 0, spell("双重灼烧", 2, fx("DAMAGE", "ENEMY_MINION", 2, "")));
            g.playFromHand(c1);
            eq(0, g.players[1].deck.size(), "对方卡组被恰好抽空");
            eq(4, enemy.health, "效果正常结算");
            eq(0, g.currentIdx, "回合不结束");
            CardInstance c0 = toHand(g, 0, spell("无声刺", 0, fx("DAMAGE", "ENEMY_MINION", 1, "")));
            c0.def.tags.add("无声");
            g.playFromHand(c0);
            eq(3, enemy.health, "0费卡可继续使用");
            eq(0, g.currentIdx, "0费卡不触发空发");
        });

        test("词条限制：同词条每回合段限用一次", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 1, 20);
            CardInstance e1 = toField(g, 1, minion("靶A", 1, 4, 0));
            CardInstance c1 = toHand(g, 0, spell("破坏一号", 1, fx("DAMAGE", "ENEMY_MINION", 1, "")));
            c1.def.tags.add("破坏");
            CardInstance c2 = toHand(g, 0, spell("破坏二号", 1, fx("DAMAGE", "ENEMY_MINION", 1, "")));
            c2.def.tags.add("破坏");
            check(g.playFromHand(c1), "第一张可用");
            check(!g.playFromHand(c2), "同词条第二张被拒绝");
            eq(3, e1.health, "第二张未结算");
        });

        test("词条计数在玩家交替时双方重置", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardInstance c1 = toHand(g, 0, spell("破坏甲", 1, fx("DAMAGE", "ENEMY_FACE", 1, "")));
            c1.def.tags.add("破坏");
            g.playFromHand(c1);
            check(g.players[0].usedTags.contains("破坏"), "词条已记录");
            g.endTurn();
            check(g.players[0].usedTags.isEmpty(), "交替后甲方词条重置");
            check(g.players[1].usedTags.isEmpty(), "交替后乙方词条重置");
        });

        test("惩罚牌：无法主动使用，仅靠惩罚抽取激活", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardDef pd = new CardDef();
            pd.id = "天罚"; pd.name = "天罚"; pd.type = CardType.PUNISH; pd.punish = 5;
            pd.punishCost = 0; pd.punishEffects.add(fx("DAMAGE", "ALL_ENEMY_MINIONS", 3, ""));
            CardInstance c = toHand(g, 0, pd);
            check(!g.playFromHand(c), "惩罚牌不可主动打出");
            check(g.players[0].hand.contains(c), "仍在手中");
        });

        test("惩罚牌：非惩罚途径抽到→当回合结束阶段自动销毁", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 5); fillDeck(g, 1, 5);
            CardDef pd = new CardDef();
            pd.id = "天罚2"; pd.name = "天罚2"; pd.type = CardType.PUNISH;
            CardInstance c = new CardInstance(pd, 0);
            g.players[0].deck.add(c);
            g.drawCards(g.players[0], 1, false);   // 普通抽取
            check(g.players[0].hand.contains(c) && !c.punishActivated, "抽到但未激活");
            g.endTurn();
            check(g.players[0].graveyard.contains(c), "结束阶段自动销毁");
        });

        test("惩罚连锁：被惩罚抽到的卡可立即发动反击（连锁）", () -> {
            TestAgent a0 = new TestAgent(); TestAgent a1 = new TestAgent();
            a1.activatePunish = true;
            Game g = freshGame(bal(), a0, a1);
            fillDeck(g, 0, 10);
            CardDef counter = spell("反震", 4, fx("DAMAGE", "ENEMY_FACE", 2, ""));
            counter.punishActivatable = true; counter.punishCost = 0;
            counter.punishEffects.add(fx("DAMAGE", "ALL_ENEMY_MINIONS", 2, ""));
            stackDeck(g, 1, counter);
            CardInstance myMinion = toField(g, 0, minion("前锋", 2, 3, 0));
            CardInstance c = toHand(g, 0, spell("挑衅", 1, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            g.playFromHand(c);
            eq(1, myMinion.health, "对方惩罚发动的反击已结算（3-2=1）");
            check(g.players[1].graveyard.stream().anyMatch(x -> x.def.id.equals("反震")), "反击卡用毕进墓地");
        });

        test("惩罚连锁：达到上限后不再询问", () -> {
            Balance b = bal(); b.chainLimit = 1;
            TestAgent a0 = new TestAgent(); TestAgent a1 = new TestAgent();
            a0.activatePunish = true; a1.activatePunish = true;
            Game g = freshGame(b, a0, a1);
            // 双方卡组全是可0费连锁、且发动时让对方抽1的卡
            CardDef loop = spell("回响", 1, fx("DAMAGE", "ENEMY_FACE", 0, ""));
            loop.punishActivatable = true; loop.punishCost = 1;
            loop.punishEffects.add(fx("DAMAGE", "ENEMY_FACE", 0, ""));
            for (int i = 0; i < 8; i++) { stackDeck(g, 0, loop); stackDeck(g, 1, loop); }
            CardInstance c = toHand(g, 0, spell("引信", 1, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            g.playFromHand(c);   // 不应无限连锁
            check(!g.over(), "对局未崩溃且未结束");
            check(g.logs.stream().anyMatch(s -> s.contains("连锁达到上限")), "连锁被上限截停");
        });

        test("对方回合的惩罚发动空发：卡牌作废但不结束对方回合", () -> {
            TestAgent a0 = new TestAgent(); TestAgent a1 = new TestAgent();
            a1.activatePunish = true;
            Game g = freshGame(bal(), a0, a1);
            // 甲卡组0张：乙的惩罚发动(punishCost=2)必然空发
            CardDef counter = spell("贪噬", 3, fx("DAMAGE", "ENEMY_FACE", 9, ""));
            counter.punishActivatable = true; counter.punishCost = 2;
            counter.punishEffects.add(fx("DAMAGE", "ALL_ENEMY_MINIONS", 9, ""));
            stackDeck(g, 1, counter);
            CardInstance myMinion = toField(g, 0, minion("哨兵", 1, 3, 0));
            CardInstance c = toHand(g, 0, spell("点火", 1, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            g.playFromHand(c);
            eq(3, myMinion.health, "空发不结算");
            check(g.players[1].graveyard.stream().anyMatch(x -> x.def.id.equals("贪噬")), "空发卡进墓地");
            eq(0, g.currentIdx, "甲的回合未被结束");
        });

        test("伏击：每回合限盖1张；封场伏击禁止再盖", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 1, 20);
            g.phase = Game.Phase.AMBUSH;
            CardDef amb = new CardDef();
            amb.id = "陷阱A"; amb.name = "陷阱A"; amb.type = CardType.AMBUSH; amb.punish = 0;
            amb.ambushEffects.add(fx("NEGATE", "NONE", 0, ""));
            CardInstance a1 = toHand(g, 0, amb), a2 = toHand(g, 0, amb);
            check(g.setAmbush(a1), "第一张可盖");
            check(!g.setAmbush(a2), "本回合第二张被拒绝");
            // 封场
            Game g2 = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g2, 1, 20);
            g2.phase = Game.Phase.AMBUSH;
            CardDef lock = new CardDef();
            lock.id = "封锁领域"; lock.name = "封锁领域"; lock.type = CardType.AMBUSH;
            lock.punish = 0; lock.ambushKind = AmbushKind.LOCKDOWN;
            lock.ambushEffects.add(fx("NEGATE", "NONE", 0, ""));
            g2.setAmbush(toHand(g2, 0, lock));
            g2.players[0].ambushSetThisTurn = false;   // 模拟下一回合
            check(!g2.setAmbush(toHand(g2, 0, amb)), "封场期间无法盖放其他伏击");
        });

        test("伏击触发：反制对方咒文；专注伏击压制本回合其他伏击", () -> {
            TestAgent a0 = new TestAgent(); TestAgent a1 = new TestAgent();
            Game g = freshGame(bal(), a0, a1);
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardDef focus = new CardDef();
            focus.id = "凝神反击"; focus.name = "凝神反击"; focus.type = CardType.AMBUSH;
            focus.ambushKind = AmbushKind.FOCUS; focus.ambushTrigger = "OPPONENT_PLAYS_SPELL";
            focus.ambushEffects.add(fx("NEGATE", "NONE", 0, ""));
            CardDef norm = new CardDef();
            norm.id = "暗刺"; norm.name = "暗刺"; norm.type = CardType.AMBUSH;
            norm.ambushTrigger = "OPPONENT_PLAYS_SPELL";
            norm.ambushEffects.add(fx("DAMAGE", "ALL_ENEMY_MINIONS", 2, ""));
            g.players[1].ambushes.add(new CardInstance(focus, 1));
            g.players[1].ambushes.add(new CardInstance(norm, 1));
            CardInstance mine = toField(g, 0, minion("步卒", 2, 3, 0));
            CardInstance s1 = toHand(g, 0, spell("烈风", 0, fx("DAMAGE", "ENEMY_FACE", 3, "")));
            s1.def.tags.add("风");
            g.playFromHand(s1);
            check(g.players[0].graveyard.contains(s1), "咒文被专注伏击反制");
            CardInstance s2 = toHand(g, 0, spell("烈风二式", 0, fx("DAMAGE", "ENEMY_FACE", 3, "")));
            s2.def.tags.add("风二");
            g.playFromHand(s2);
            eq(3, mine.health, "专注压制下，普通伏击【暗刺】本回合无法再触发");
        });

        test("统领：任何手段抽到即自动登场（随从统领上战场）", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardDef ld = leaderMinion("焰皇", 8, 10);
            stackDeck(g, 0, ld);
            g.drawCards(g.players[0], 1, false);
            check(g.players[0].leaderFielded(), "统领已登场");
            check(g.players[0].field.stream().anyMatch(c -> c.def.id.equals("焰皇")), "随从统领在战场");
            eq(0, g.players[0].hand.size(), "不进入手牌");
        });

        test("统领：被惩罚抽到→额外触发强力惩罚效果", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardDef ld = leaderMinion("雷帝", 6, 8);
            ld.leaderDef.punishEffects.add(fx("ADD_OPP_PUNISH_TURN", "NONE", 5, ""));
            stackDeck(g, 1, ld);
            CardInstance c = toHand(g, 0, spell("急令", 1, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            g.playFromHand(c);   // 乙被惩罚抽1 → 抽到统领
            check(g.players[1].leaderFielded(), "统领登场");
            eq(5, g.players[0].turnPunishDelta, "甲本回合惩罚值+5（强力惩罚效果生效）");
        });

        test("统领免疫：离场类效果对统领空发", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 1, 20);
            CardDef ld = leaderMinion("不动王", 4, 9);
            CardInstance l = new CardInstance(ld, 1);
            stackDeck(g, 1, vanilla("垫")); // 防止统领被本回合抽到
            g.players[1].deck.add(0, l);
            g.drawCards(g.players[1], 0, false);
            // 直接登场
            g.players[1].deck.remove(l);
            l.isLeaderEntity = true; g.players[1].leaderOnField = l; g.players[1].field.add(l);
            CardInstance c = toHand(g, 0, spell("湮灭", 1, fx("DESTROY", "ENEMY_MINION", 0, "")));
            g.playFromHand(c);
            check(g.players[1].field.contains(l), "统领免疫破坏离场，仍在场");
        });

        test("先驱威压：仅一方统领在场→对方卡牌惩罚值+1、统领方弃牌上限+2", () -> {
            Balance b = bal();
            Game g = freshGame(b, new TestAgent(), new TestAgent());
            fillDeck(g, 0, 30); fillDeck(g, 1, 30);
            CardDef ld = leaderMinion("先驱者", 5, 7);
            CardInstance l = new CardInstance(ld, 1);
            l.isLeaderEntity = true; g.players[1].leaderOnField = l; g.players[1].field.add(l);
            CardInstance c = toHand(g, 0, spell("试探", 2, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            eq(3, g.effectivePunish(0, c, false), "甲的卡惩罚2→3（威压+1）");
            CardInstance c2 = new CardInstance(spell("回应", 2), 1);
            eq(2, g.effectivePunish(1, c2, false), "统领方自身不受威压");
            // 弃牌上限：乙手牌10张，威压上限8+2=10 → 不弃
            for (int i = 0; i < 10; i++) toHand(g, 1, vanilla("乙手" + i));
            g.endTurn();   // 甲回合结束（甲手1张不弃）→ 进入乙回合
            // 现在轮到乙；让乙立刻结束回合验证弃牌
            int before = g.players[1].hand.size();
            g.phase = Game.Phase.ACTION;
            g.endTurn();
            check(g.players[1].hand.size() >= 10 && before >= 10, "统领方保留上限提高（8+2）");
        });

        test("门限保护：双方统领未齐时无法分出胜负（濒死钳为1）", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardDef ld = leaderMinion("孤王", 3, 2);
            CardInstance l = new CardInstance(ld, 1);
            l.isLeaderEntity = true; g.players[1].leaderOnField = l; g.players[1].field.add(l);
            CardInstance c = toHand(g, 0, spell("斩首", 0, fx("DAMAGE", "ENEMY_MINION", 99, "")));
            c.def.kingSlayer = true;
            c.def.tags.add("斩");
            g.playFromHand(c);
            check(!g.over(), "对局不能结束");
            eq(1, l.health, "统领被强行止于一线之间");
        });

        test("胜负：双方统领在场后击败统领获胜", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardDef l0 = leaderMinion("甲统领", 5, 9), l1 = leaderMinion("乙统领", 3, 2);
            CardInstance i0 = new CardInstance(l0, 0), i1 = new CardInstance(l1, 1);
            i0.isLeaderEntity = true; g.players[0].leaderOnField = i0; g.players[0].field.add(i0);
            i1.isLeaderEntity = true; g.players[1].leaderOnField = i1; g.players[1].field.add(i1);
            CardInstance c = toHand(g, 0, spell("终焉", 0, fx("DAMAGE", "ENEMY_MINION", 99, "")));
            c.def.kingSlayer = true;
            g.playFromHand(c);
            check(g.over(), "对局结束");
            eq(0, g.winner, "甲获胜");
        });



        test("护卫：有其他随从时不能攻击本统领，清场后才可攻击", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardDef leaderDef = leaderMinion("护卫王", 5, 10); leaderDef.guard = true;
            CardInstance leader = new CardInstance(leaderDef, 1);
            leader.isLeaderEntity = true; g.players[1].leaderOnField = leader; g.players[1].field.add(leader);
            CardInstance guard = toField(g, 1, minion("近卫", 1, 2, 0));
            CardInstance atk = toField(g, 0, minion("攻击者", 3, 3, 0));
            check(!g.legalAttackTargets(atk).contains(leader), "有近卫时统领不在可攻击目标中");
            g.players[1].field.remove(guard);
            check(g.legalAttackTargets(atk).contains(leader), "近卫消失后统领可被攻击");
        });

        test("弑君：普通单体效果不能伤害统领，弑君可以绕过统领抗性", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardDef leaderDef = leaderMinion("抗性王", 5, 8);
            CardInstance leader = new CardInstance(leaderDef, 1);
            leader.isLeaderEntity = true; g.players[1].leaderOnField = leader; g.players[1].field.add(leader);
            CardInstance normal = toHand(g, 0, spell("普通冲击", 0, fx("DAMAGE", "ENEMY_MINION", 4, "")));
            g.playFromHand(normal);
            eq(8, leader.health, "普通效果无法影响统领");
            EffectSpec slayerEffect = fx("DAMAGE", "ENEMY_MINION", 4, "");
            slayerEffect.kingSlayer = true;
            CardInstance slayer = toHand(g, 0, spell("弑君冲击", 0, slayerEffect));
            g.playFromHand(slayer);
            eq(4, leader.health, "弑君效果可以伤害统领");

            CardDef immuneDef = leaderMinion("免疫王", 5, 8);
            immuneDef.leaderDef.vulnerabilities.clear();
            CardInstance immune = new CardInstance(immuneDef, 1);
            immune.isLeaderEntity = true;
            g.players[1].field.clear();
            g.players[1].leaderOnField = immune;
            g.players[1].field.add(immune);
            EffectSpec blockedEffect = fx("DAMAGE", "ENEMY_MINION", 4, "");
            blockedEffect.kingSlayer = true;
            CardInstance blocked = toHand(g, 0, spell("被拦截的弑君", 0, blockedEffect));
            g.playFromHand(blocked);
            eq(8, immune.health, "弑君仍需匹配统领 vulnerabilities 白名单");
        });

        test("洗牌：无卡可抽时场外卡牌洗回；达阈值判负", () -> {
            Balance b = bal(); b.reshuffleLoseAt = 2;
            Game g = freshGame(b, new TestAgent(), new TestAgent());
            for (int i = 0; i < 3; i++) g.players[0].graveyard.add(new CardInstance(vanilla("亡" + i), 0));
            g.drawCards(g.players[0], 1, false);
            eq(1, g.players[0].reshuffleCount, "第一次洗牌记录");
            eq(1, g.players[1].cycleWinCount, "对手获得1点胜利计数");
            check(!g.over(), "未判负");
            // 抽光再触发第二次
            g.drawCards(g.players[0], 2, false);
            g.players[0].graveyard.add(new CardInstance(vanilla("亡x"), 0));
            g.drawCards(g.players[0], 1, false);
            eq(2, g.players[0].reshuffleCount, "第二次洗牌记录");
            eq(2, g.players[1].cycleWinCount, "对手胜利计数达到阈值");
            check(g.over() && g.winner == 1, "达到胜利计数阈值，对手获胜");
        });

        test("机械遗迹：洗牌免计数额度生效", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.players[0].skipReshuffleCredits = 1;
            g.players[0].graveyard.add(new CardInstance(vanilla("回收"), 0));
            g.drawCards(g.players[0], 1, false);
            eq(0, g.players[0].reshuffleCount, "本次洗牌不计数");
            eq(0, g.players[1].cycleWinCount, "对手不获得胜利计数");
            eq(0, g.players[0].skipReshuffleCredits, "额度消耗");
        });

        test("王城：击破时设置破城方胜利计数，而不是给受害者失败计数", () -> {
            Balance b = bal();
            b.royalCastleEnabled = true;
            b.royalCastleMaxHp = 1;
            b.royalCastleBreakVictoryCount = 9;
            Game g = freshGame(b, new TestAgent(), new TestAgent());
            g.royalCastleHp = 1;
            g.damageRoyalCastle(0, 1, "测试破城");
            eq(9, g.players[0].cycleWinCount, "破城方获得胜利计数");
            eq(0, g.players[1].cycleWinCount, "受害者不背失败计数");
            eq(0, g.players[1].reshuffleCount, "受害者自身循环记录不被强改");
        });

        test("弃牌阶段：超过上限强制弃至上限", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 5); fillDeck(g, 1, 5);
            for (int i = 0; i < 11; i++) toHand(g, 0, vanilla("手" + i));
            g.endTurn();
            eq(8, g.players[0].hand.size(), "弃至上限8");
            eq(0, g.players[0].totalDiscarded, "弃牌阶段强制弃牌不计入累计弃牌胜利条件");
        });

        test("吟唱：延迟X回合后于结束阶段结算", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardDef ch = new CardDef();
            ch.id = "蓄能爆破"; ch.name = "蓄能爆破"; ch.type = CardType.SPELL;
            ch.punish = 0; ch.chant = 2;
            ch.chantEffects.add(fx("DAMAGE", "ALL_ENEMY_MINIONS", 4, ""));
            CardInstance enemy = toField(g, 1, minion("城墙", 0, 4, 0));
            CardInstance c = toHand(g, 0, ch);
            g.playFromHand(c);
            check(g.players[0].field.contains(c), "吟唱中驻留场上");
            g.endTurn();              // 甲结束：吟唱2→1
            g.phase = Game.Phase.ACTION; g.endTurn();   // 乙结束
            g.phase = Game.Phase.ACTION; g.endTurn();   // 甲结束：吟唱1→0 触发
            check(g.players[0].graveyard.contains(c), "吟唱完成入墓地");
            check(g.players[1].field.isEmpty() || enemy.health <= 0, "吟唱效果已结算");
        });

        test("战斗：召唤当回合不可攻击，突袭可以；攻击次数按字段", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardInstance fresh = toField(g, 0, minion("新兵", 2, 2, 0));
            fresh.summonedThisTurn = true;
            check(!fresh.canAttackNow(), "召唤回合不可攻击");
            CardDef cgDef = minion("迅捷豹", 2, 2, 0);
            cgDef.keywords.add(CardDef.KW_CHARGE);
            CardInstance cg = toField(g, 0, cgDef);
            cg.summonedThisTurn = true;
            check(cg.canAttackNow(), "突袭随从召唤当回合可攻击");
            CardDef twin = minion("双击者", 1, 4, 0);
            twin.attacksPerTurn = 2;
            CardInstance tw = toField(g, 0, twin);
            CardInstance dummy = toField(g, 1, minion("木桩", 0, 9, 0));
            g.attack(tw, dummy); g.attack(tw, dummy);
            eq(7, dummy.health, "两段攻击均结算");
            check(!tw.canAttackNow(), "攻击次数耗尽");
        });

        test("战斗：嘲讽强制优先；无嘲讽可直击统领", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardInstance atk = toField(g, 0, minion("骑士", 3, 3, 0));
            CardDef tauntDef = minion("壁垒", 1, 5, 0);
            tauntDef.keywords.add(CardDef.KW_TAUNT);
            CardInstance wall = toField(g, 1, tauntDef);
            CardInstance other = toField(g, 1, minion("后排", 4, 2, 0));
            List<CardInstance> targets = g.legalAttackTargets(atk);
            check(targets.contains(wall) && !targets.contains(other), "只能打嘲讽");
            check(!g.canAttackFace(atk), "嘲讽在场不可直击");
        });

        test("关键词：圣盾抵消首次伤害；扰魔不可被指定", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 1, 20);
            CardDef sd = minion("圣盾兵", 2, 3, 0);
            sd.keywords.add(CardDef.KW_SHIELD);
            CardInstance s = toField(g, 1, sd);
            g.dealDamage(s, 5);
            eq(3, s.health, "圣盾抵消");
            g.dealDamage(s, 1);
            eq(2, s.health, "第二次伤害正常");
            CardDef wd = minion("迷雾行者", 2, 2, 0);
            wd.keywords.add(CardDef.KW_WARD);
            CardInstance w = toField(g, 1, wd);
            g.players[1].field.remove(s); g.players[1].graveyard.add(s);
            CardInstance c = toHand(g, 0, spell("点杀", 0, fx("DAMAGE", "ENEMY_MINION", 9, "")));
            g.playFromHand(c);
            eq(2, w.health, "扰魔随从无法被指定型效果选中");
        });

        test("深海统领：对方惩罚值转化为弃自己牌", () -> {
            TestAgent a0 = new TestAgent();
            Game g = freshGame(bal(), a0, new TestAgent());
            fillDeck(g, 1, 20);
            g.players[0].punishToSelfDiscardThisTurn = true;
            toHand(g, 0, vanilla("祭品1")); toHand(g, 0, vanilla("祭品2"));
            CardInstance c = toHand(g, 0, spell("强攻", 2, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            g.playFromHand(c);
            eq(20, g.players[1].deck.size(), "对方不抽牌");
            eq(2, g.players[0].totalDiscarded, "改为弃自己2张");
        });

        test("特殊胜利：条件达成但统领未齐→门不开；齐后达成→获胜", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardDef sea = new CardDef();
            sea.id = "涛冥"; sea.name = "涛冥"; sea.type = CardType.SPELL; sea.leader = true;
            sea.leaderDef.grantLife = 25;
            sea.leaderDef.winCondition = "OPP_DISCARD_TOTAL_GE";
            sea.leaderDef.winParam = 2;
            sea.leaderDef.winText = "深渊吞没了对手的一切";
            CardInstance l0 = new CardInstance(sea, 0);
            l0.isLeaderEntity = true; g.players[0].leaderOnField = l0; g.players[0].life = 25;
            g.players[1].totalDiscarded = 5;
            g.discardFromHand(g.players[1], toHand(g, 1, vanilla("再弃")), "测试");
            check(!g.over(), "对方统领未登场，胜利之门未开");
            CardDef l1d = leaderMinion("敌酋", 2, 2);
            CardInstance l1 = new CardInstance(l1d, 1);
            l1.isLeaderEntity = true; g.players[1].leaderOnField = l1; g.players[1].field.add(l1);
            g.discardFromHand(g.players[1], toHand(g, 1, vanilla("终弃")), "测试");
            check(g.over() && g.winner == 0, "统领齐后条件达成即获胜");
        });

        test("命运之影：删除压制光环后统领效果正常结算", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 20); fillDeck(g, 1, 20);
            CardDef shadow = leaderMinion("命运之影", 1, 3);
            CardInstance sh = new CardInstance(shadow, 0);
            sh.isLeaderEntity = true; g.players[0].leaderOnField = sh; g.players[0].field.add(sh);
            g.computeAuras();
            check(!g.players[1].leaderDisabled, "已删除的统领压制光环不再生效");
            CardDef ld = leaderMinion("雷帝", 6, 8);
            ld.leaderDef.punishEffects.add(fx("ADD_OPP_PUNISH_TURN", "NONE", 5, ""));
            stackDeck(g, 1, ld);
            CardInstance c = toHand(g, 0, spell("急令", 1, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            g.playFromHand(c);
            eq(5, g.players[0].turnPunishDelta, "统领惩罚效果正常生效");
        });

        test("伏击统领：触发后以自身效果洗回卡组（唯一离场例外）", () -> {
            TestAgent a0 = new TestAgent(); TestAgent a1 = new TestAgent();
            Game g = freshGame(bal(), a0, a1);
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardDef gate = new CardDef();
            gate.id = "命运之门"; gate.name = "命运之门"; gate.type = CardType.AMBUSH; gate.leader = true;
            gate.ambushTrigger = "OPPONENT_PLAYS_SPELL";
            gate.leaderDef.winCondition = "AMBUSH_TRIGGER_WIN";
            gate.ambushEffects.add(fx("WIN_GAME", "NONE", 0, "命运之门开启"));
            CardInstance gi = new CardInstance(gate, 1);
            gi.isLeaderEntity = true;
            g.players[1].leaderOnField = gi;
            g.players[1].ambushes.add(gi);
            CardInstance c = toHand(g, 0, spell("引诱", 0, fx("DAMAGE", "ENEMY_FACE", 0, "")));
            g.playFromHand(c);
            check(!g.over(), "甲方统领未登场，胜利之门未开");
            check(!g.players[1].leaderFielded(), "统领伏击失败后离场");
            check(g.players[1].deck.stream().anyMatch(x -> x.def.id.equals("命运之门")), "洗回卡组等待再临");
        });

        test("数据驱动：卡牌定义 JSON 双向转换无损", () -> {
            CardDef c = new CardDef();
            c.id = "fire_strike"; c.name = "火焰冲击"; c.faction = "烈焰帝国";
            c.type = CardType.SPELL; c.tags.add("破坏"); c.punish = 3;
            c.onPlayEffects.add(fx("DAMAGE", "ENEMY_MINION", 5, ""));
            c.custom.put("rarity", "稀有");
            java.util.Map<String, Object> m = c.toMap();
            CardDef back = CardDef.fromMap(com.dominionwars.util.Json.parseObject(
                    com.dominionwars.util.Json.write(m, true)));
            eq(c.id, back.id, "id");
            eq(c.punish, back.punish, "punish");
            eq("DAMAGE", back.onPlayEffects.get(0).action, "效果动作");
            eq("稀有", back.custom.get("rarity"), "自定义字段保留");
        });

        test("深海联动：对方因效果弃牌触发己方场上「对方弃牌时」效果", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardDef siren = minion("联动海妖", 2, 3, 0);
            siren.onOpponentDiscardEffects.add(fx("BUFF", "SELF", 1, "both"));
            CardInstance s = toField(g, 0, siren);
            toHand(g, 1, vanilla("将被弃"));
            CardInstance c = toHand(g, 0, spell("夺念", 0, fx("DISCARD_OPP_RANDOM", "NONE", 1, "")));
            g.playFromHand(c);
            eq(3, s.attack, "弃牌联动 +1 攻");
            eq(4, s.health, "弃牌联动 +1 血");
            // 弃牌阶段的强制弃牌不应触发联动
            for (int i = 0; i < 10; i++) toHand(g, 1, vanilla("超量" + i));
            g.endTurn();
            g.phase = Game.Phase.ACTION; g.endTurn();
            eq(3, s.attack, "弃牌阶段强制弃牌不触发联动");
        });

        test("王城：受敌方伤害打断对方无伤计数；DAMAGE_CASTLE 动作生效", () -> {
            Balance b = bal(); b.royalCastleEnabled = true; b.royalCastleMaxHp = 75;
            Game g = freshGame(b, new TestAgent(), new TestAgent());
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardDef wl = leaderMinion("树心", 0, 9);
            CardInstance wli = new CardInstance(wl, 1);
            wli.isLeaderEntity = true; g.players[1].leaderOnField = wli; g.players[1].field.add(wli);
            g.players[1].noDamageTurns = 3;
            int hpBefore = g.royalCastleHp;
            CardInstance c = toHand(g, 0, spell("攻城锤", 0, fx("DAMAGE_CASTLE", "NONE", 5, "")));
            g.playFromHand(c);
            eq(hpBefore - 5, g.royalCastleHp, "王城掉血");
            check(g.players[1].damagedThisCycle, "对方无伤计数本轮被打断");
            g.phase = Game.Phase.ACTION; g.endTurn();
            // 乙的回合结束时 noDamageTurns 应归零
            g.phase = Game.Phase.ACTION; g.endTurn();
            eq(0, g.players[1].noDamageTurns, "无伤计数清零");
        });

        // ===================== 汇总 =====================
        System.out.println();
        System.out.println("通过 " + passed + " / " + (passed + failed));
        if (failed > 0) {
            System.out.println("失败用例：");
            for (String f : failures) System.out.println("  ✗ " + f);
            System.exit(1);
        }
    }
}
