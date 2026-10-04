package com.dominionwars.test;

import com.dominionwars.data.Balance;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Effects;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerAgent;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.AmbushKind;
import com.dominionwars.model.CardDef.CardType;
import com.dominionwars.model.CardDef.EffectSpec;
import com.dominionwars.util.Json;

import java.nio.file.Files;
import java.nio.file.Path;
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
        boolean enterAction = true;         // 是否进入行动阶段（false 时留在伏击阶段）
        CardInstance selectedRollback = null;   // ROLLBACK 指定目标（多目标时 null=拒绝）
        CardInstance selectedTarget = null;     // PULL/普通单体目标的显式选择；不在候选中时用于负例
        boolean rejectTargetSelection = false;  // 模拟 Web/人类没有提交多目标选择
        boolean vetoPush = false;               // 仅用于证明结束阶段 PUSH 不再读取代理否决
        boolean wantPull = true;            // 是否发起下载
        int activateCount = 0;              // 【惩罚】实际发动次数（连锁深度用例使用）
        @Override public boolean askActivatePunish(Game g, int idx, CardInstance c, int cost) {
            if (activatePunish) activateCount++;
            return activatePunish;
        }
        @Override public CardInstance chooseTarget(Game g, int idx, List<CardInstance> opts, String prompt, boolean optional) {
            if (rejectTargetSelection) return null;
            if (selectedTarget != null) return selectedTarget;
            return opts.isEmpty() ? null : opts.get(0);
        }
        @Override public CardInstance chooseDiscard(Game g, int idx, List<CardInstance> hand) {
            return hand.isEmpty() ? null : hand.get(0);
        }
        @Override public CardInstance chooseAmbush(Game g, int idx, List<CardInstance> cands, String desc) {
            return (!triggerAmbush || cands.isEmpty()) ? null : cands.get(0);
        }
        @Override public CardInstance chooseRollbackTarget(Game g, int idx, List<CardInstance> queued) {
            return selectedRollback;
        }
        @Override public boolean askPush(Game g, int idx, List<CardInstance> queued) {
            return !vetoPush;
        }
        @Override public boolean askPull(Game g, int idx, CardInstance top, CardInstance carrier, int cost) {
            return wantPull;
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

    // ---- 机械 B 模式 / 古木增幅用的真实卡库 ----
    static final List<String> machineLibIds = new ArrayList<>();

    /**
     * 每局重建的真实卡库：从 data/cards/*.json 读入全部真实卡定义，
     * 供 SUMMON / SUMMON_LEADER（含地标晋升 machine_alpha）解析。
     * 生产代码 CardLibrary.load 的语义不变，这里只是为了在测试里按 id 取单卡。
     */
    static CardLibrary realLibrary() {
        CardLibrary lib = CardLibrary.load(Path.of("data/cards"));
        check(!lib.byId.isEmpty(), "真实卡库应当能读到 data/cards/*.json");
        return lib;
    }

    /** 从 data/cards 的分阵营 JSON 中找到指定 id 的原始 map（不修改数据文件）。 */
    @SuppressWarnings("unchecked")
    static java.util.Map<String, Object> loadCardMap(String file, String id) throws Exception {
        Object root = Json.parse(Files.readString(Path.of(file)));
        if (root instanceof List) {
            for (Object o : (List<Object>) root) {
                java.util.Map<String, Object> m = (java.util.Map<String, Object>) o;
                if (id.equals(Json.str(m, "id", ""))) return m;
            }
        }
        throw new IllegalStateException(file + " 中找不到卡牌 " + id);
    }

    static CardDef machineMinion(String id, int atk, int hp) {
        CardDef c = new CardDef();
        c.id = id; c.name = id; c.faction = "机械遗迹"; c.type = CardType.MINION;
        c.attack = atk; c.health = hp; c.punish = 0;
        c.tags.add("机械");
        machineLibIds.add(id);
        return c;
    }

    static CardInstance toCloud(Game g, int p, CardDef def) {
        CardInstance c = new CardInstance(def, p);
        g.players[p].cloudStack.add(c);
        return c;
    }

    static CardInstance realLeader(Game g, int p, String id) {
        CardInstance c = new CardInstance(g.library.get(id), p);
        // Lightweight state fixture: preserve the engine's real zone shape
        // without executing leader enter effects in tests that only need an
        // active leader for victory/target checks.
        c.isLeaderEntity = true;
        PlayerState owner = g.players[p];
        owner.leaderOnField = c;
        switch (c.def.type) {
            case MINION -> owner.field.add(c);
            case AMBUSH -> owner.ambushes.add(c);
            default -> { /* spell/landmark leaders remain in the leader zone */ }
        }
        return c;
    }

    static CardInstance drawLeaderThroughProductionPath(Game g, int p, String id) {
        CardInstance c = new CardInstance(g.library.get(id), p);
        g.players[p].deck.add(c);
        g.drawCards(g.players[p], 1, false);
        if (g.players[p].leaderOnField != c) {
            throw new IllegalStateException("Leader did not enter through the production draw path: " + id);
        }
        return c;
    }

    /**
     * 增幅 gating 夹具（对照 C# EffectRuntime.Combat.Buff）。
     * wood=true 时来源为古木圣地阵营（woodSource 生效，两种层数都会吃）；
     * wood=false 时来源为无阵营（woodSource 不生效，只有 param 决定吃哪种层数）。
     * rootStacks=5 / rampantStacks=2 可区分两种层数。
     */
    static class GrowthCase {
        final Game g;
        final CardInstance src;
        final CardInstance friendly;
        final CardInstance enemyCard;
        GrowthCase() { this(true); }
        GrowthCase(boolean wood) {
            g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardDef source = minion(wood ? "古木来源X" : "无阵营来源X", 1, 1, 0);
            if (wood) source.faction = "古木圣地";
            src = new CardInstance(source, 0);
            friendly = toField(g, 0, minion("己方目标", 1, 1, 0));
            enemyCard = toField(g, 1, minion("敌方目标", 1, 1, 0));
            friendly.attack = 1; friendly.health = 1; friendly.maxHealth = 1;
            enemyCard.attack = 1; enemyCard.health = 1; enemyCard.maxHealth = 1;
            g.players[0].rootStacks = 5;
            g.players[0].rampantStacks = 2;
            // 来源不入场：避免它自己成为 FRIENDLY_MINION 的目标
            g.players[0].field.remove(src);
        }
    }

    static void buff(GrowthCase c, String target, int amount, String param) {
        Effects.resolve(c.g, 0, c.src,
                Arrays.asList(fx("BUFF", target, amount, param)), new Effects.Ctx());
    }

    /** 创建空局：双方空卡组、手动布置，phase=ACTION、P0 行动 */
    static Game freshGame(Balance b, TestAgent a0, TestAgent a1) {
        Game g = new Game(b, new ArrayList<>(), new ArrayList<>(), "甲", "乙", a0, a1, 42L, 0);
        g.phase = Game.Phase.ACTION;
        g.turnNumber = 1;
        return g;
    }
    static Balance bal() { return new Balance(); }

    /**
     * 惩罚连锁实测夹具：P0 打出惩罚1的引擎咒文，双方卡组全是「惩罚发动时再让对方抽1张」的惩罚牌。
     * 返回 {P0 被询问次数, P1 被询问次数}。
     * 深度上限由 chainLimit 决定；深度「等于」上限这一层是否结算正是本用例要钉住的语义。
     */
    static int[] punishChainAsks(int chainLimit) {
        TestAgent a0 = new TestAgent(); a0.activatePunish = true;
        TestAgent a1 = new TestAgent(); a1.activatePunish = true;
        Balance b = bal();
        b.chainLimit = chainLimit;
        b.maxPunishResponsesPerRound = 0;  // 隔离 T1 响应额度，单独验证 chainLimit 边界
        b.royalCastleEnabled = false;          // 隔离王城规则，避免额外分支
        Game g = freshGame(b, a0, a1);
        for (int i = 0; i < 8; i++) {
            CardDef d = new CardDef();
            d.id = "连锁惩罚牌" + i; d.name = d.id; d.type = CardType.PUNISH;
            d.punish = 0; d.punishActivatable = true; d.punishCost = 1; d.punishCondition = "ALWAYS";
            stackDeck(g, 1, d);
        }
        for (int i = 0; i < 8; i++) {
            CardDef d = new CardDef();
            d.id = "乙方连锁牌" + i; d.name = d.id; d.type = CardType.PUNISH;
            d.punish = 0; d.punishActivatable = true; d.punishCost = 1; d.punishCondition = "ALWAYS";
            stackDeck(g, 0, d);
        }
        CardInstance engine = toHand(g, 0, spell("连锁引擎", 1, fx("DAMAGE", "ENEMY_FACE", 0, "")));
        g.playFromHand(engine);
        return new int[] { a0.activateCount, a1.activateCount };
    }

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
            Balance b = bal(); b.chainLimit = 1; b.maxPunishResponsesPerRound = 0;
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
            // 上限语义与 C# 一致：只有「超过」上限才停（等于上限那一层仍结算），
            // 因此截停时日志里的深度必然严格大于 chainLimit。
            check(g.logs.stream().anyMatch(s -> s.contains("已超过上限")), "连锁被上限截停");
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
            eq(1, g.players[0].cycleWinCount, "发生循环的玩家获得1点胜利计数");
            eq(0, g.players[1].cycleWinCount, "对手不获得循环胜利计数");
            check(!g.over(), "未判负");
            // 抽光再触发第二次
            g.drawCards(g.players[0], 2, false);
            g.players[0].graveyard.add(new CardInstance(vanilla("亡x"), 0));
            g.drawCards(g.players[0], 1, false);
            eq(2, g.players[0].reshuffleCount, "第二次洗牌记录");
            eq(2, g.players[0].cycleWinCount, "发生循环的玩家胜利计数达到阈值");
            eq(0, g.players[1].cycleWinCount, "对手仍不获得胜利计数");
            check(g.over() && g.winner == 0, "达到胜利计数阈值，发生循环的玩家获胜");
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

        test("王城：双方随从统领时主动破城方优先", () -> {
            Balance b = bal();
            b.royalCastleEnabled = true;
            b.royalCastleMaxHp = 1;
            Game g = freshGame(b, new TestAgent(), new TestAgent());
            CardDef left = leaderMinion("烈焰镜像甲", 4, 8);
            left.leaderDef.winCondition = "ROYAL_CASTLE_BREAK";
            CardDef right = leaderMinion("烈焰镜像乙", 4, 8);
            right.leaderDef.winCondition = "ROYAL_CASTLE_BREAK";
            CardInstance l0 = new CardInstance(left, 0), l1 = new CardInstance(right, 1);
            l0.isLeaderEntity = true; l1.isLeaderEntity = true;
            g.players[0].leaderOnField = l0; g.players[0].field.add(l0);
            g.players[1].leaderOnField = l1; g.players[1].field.add(l1);
            g.royalCastleHp = 1;
            g.damageRoyalCastle(0, 1, "镜像测试");
            check(g.over() && g.winner == 0, "双方随从统领时主动破城方获胜");
        });

        test("王城：非随从活动持有者按被动条件获胜", () -> {
            Balance b = bal();
            b.royalCastleEnabled = true;
            b.royalCastleMaxHp = 1;
            Game g = freshGame(b, new TestAgent(), new TestAgent());
            CardDef breaker = leaderMinion("普通破城者", 4, 8);
            CardDef passive = new CardDef();
            passive.id = "非随从破城统领"; passive.name = passive.id; passive.type = CardType.SPELL;
            passive.leader = true; passive.leaderDef.winCondition = "ROYAL_CASTLE_BREAK";
            CardInstance l0 = new CardInstance(breaker, 0), l1 = new CardInstance(passive, 1);
            l0.isLeaderEntity = true; l1.isLeaderEntity = true;
            g.players[0].leaderOnField = l0; g.players[0].field.add(l0);
            g.players[1].leaderOnField = l1;
            g.royalCastleHp = 1;
            g.damageRoyalCastle(0, 1, "被动持有者测试");
            check(g.over() && g.winner == 1, "当前活动的 ROYAL_CASTLE_BREAK 持有者获胜");
        });

        test("王城：非随从双方同持有时不按座位顺序猜胜者", () -> {
            Balance b = bal();
            b.royalCastleEnabled = true;
            b.royalCastleMaxHp = 1;
            Game g = freshGame(b, new TestAgent(), new TestAgent());
            CardDef left = new CardDef();
            left.id = "非随从镜像甲"; left.name = left.id; left.type = CardType.SPELL;
            left.leader = true; left.leaderDef.winCondition = "ROYAL_CASTLE_BREAK";
            CardDef right = new CardDef();
            right.id = "非随从镜像乙"; right.name = right.id; right.type = CardType.SPELL;
            right.leader = true; right.leaderDef.winCondition = "ROYAL_CASTLE_BREAK";
            CardInstance l0 = new CardInstance(left, 0), l1 = new CardInstance(right, 1);
            l0.isLeaderEntity = true; l1.isLeaderEntity = true;
            g.players[0].leaderOnField = l0;
            g.players[1].leaderOnField = l1;
            g.royalCastleHp = 1;
            g.damageRoyalCastle(0, 1, "双持有未决测试");
            check(!g.over() && g.winner == -1, "未冻结的非随从双持有不得按座位顺序裁决");
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

        // ===================== 古木：扎根 / 疯长 / 封印（RULES §12.2）=====================

        test("古木：wood_seed 吟唱完成后扎根2（真实数据）", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            fillDeck(g, 0, 5); fillDeck(g, 1, 5);
            CardInstance seed = toHand(g, 0, g.library.get("wood_seed"));
            check(g.playFromHand(seed), "wood_seed 应当可以打出");
            eq(2, seed.chantRemaining, "wood_seed 进入吟唱 2");
            check(g.players[0].field.contains(seed), "吟唱中的咒文留在场上");
            eq(0, g.players[0].rootStacks, "吟唱未完成时不加扎根");
            g.endTurn();                                  // P0 结束：吟唱 →1
            g.phase = Game.Phase.ACTION; g.endTurn();     // P1 结束
            g.phase = Game.Phase.ACTION; g.endTurn();     // P0 结束：吟唱完成，结算 chantEffects
            eq(2, g.players[0].rootStacks, "chantEffects 的 ADD_ROOT 2 生效");
            check(g.players[0].graveyard.contains(seed), "吟唱完成的咒文进入墓地");
            check(g.players[0].field.stream().anyMatch(c -> c.def.id.equals("wood_treant")), "吟唱召唤古树行者");
        });

        test("古木：扎根/疯长层数进入增幅公式，增幅目标被封印", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardDef woodSource = minion("古木来源", 1, 1, 0);
            woodSource.faction = "古木圣地";
            CardInstance src = toField(g, 0, woodSource);
            g.players[0].rootStacks = 2;
            g.players[0].rampantStacks = 3;
            g.players[0].field.remove(src);      // 来源可以不在场：只提供阵营与效果来源
            CardInstance target = toField(g, 0, minion("增幅目标", 2, 3, 0));
            g.players[0].field.remove(src);      // 避免来源自己成为 FRIENDLY_MINION 的首选目标
            Effects.resolve(g, 0, src, Arrays.asList(fx("BUFF", "FRIENDLY_MINION", 1, "both")), new Effects.Ctx());
            eq(24, target.health - 3, "effective = (1+2)×2^3 = 24");
            eq(27, target.health, "生命 = 3 + 24");
            eq(27, target.maxHealth, "最大生命同步");
            eq(0, target.attack, "封印后攻击归 0");
            check(target.sealed, "增幅发生时目标被封印");
        });

        test("古木：疯长 x2^3 倍率与 param 解析（root/rampant/both/atk/hp）", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            CardDef woodSource = minion("古木来源2", 1, 1, 0);
            woodSource.faction = "古木圣地";
            CardInstance src = new CardInstance(woodSource, 0);
            // 逐档断言倍率：rampant 上限 3 → 8 倍
            int[][] cases = { {0, 0, 2}, {2, 0, 4}, {2, 1, 8}, {2, 2, 16}, {2, 3, 32}, {2, 5, 32} };
            for (int[] c : cases) {
                g.players[0].rootStacks = c[0];
                g.players[0].rampantStacks = c[1];
                CardInstance t = toField(g, 0, minion("倍率目标" + c[1], 0, 0, 0));
                g.players[0].field.remove(src);
                Effects.resolve(g, 0, src, Arrays.asList(fx("BUFF", "FRIENDLY_MINION", 2, "root")), new Effects.Ctx());
                eq(c[2], t.health, "root=" + c[0] + " rampant=" + c[1] + " → (2+" + c[0] + ")×2^min(3," + c[1] + ")");
                g.players[0].field.remove(t);
            }
            // param=atk 只加攻击；非法 param 整体跳过
            g.players[0].rootStacks = 0; g.players[0].rampantStacks = 0;
            CardInstance atkTarget = toField(g, 0, minion("攻击目标", 3, 3, 0));
            g.players[0].field.remove(src);
            Effects.resolve(g, 0, src, Arrays.asList(fx("BUFF", "FRIENDLY_MINION", 2, "atk")), new Effects.Ctx());
            eq(5, atkTarget.attack, "param=atk 只提升攻击");
            eq(3, atkTarget.health, "param=atk 不动生命");
            CardInstance badTarget = toField(g, 0, minion("非法参数目标", 1, 1, 0));
            g.players[0].field.remove(src); g.players[0].field.remove(atkTarget);
            Effects.resolve(g, 0, src, Arrays.asList(fx("BUFF", "FRIENDLY_MINION", 5, "health")), new Effects.Ctx());
            eq(1, badTarget.health, "非法 param 不产生任何强化");
        });

        test("古木：封印随从达到 GIANT_HEALTH_GE 阈值即获胜", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardInstance wl = realLeader(g, 0, "wood_leader");
            CardDef attacker = leaderMinion("敌酋", 1, 20);
            CardInstance l1 = new CardInstance(attacker, 1);
            l1.isLeaderEntity = true; g.players[1].leaderOnField = l1; g.players[1].field.add(l1);
            eq(512, wl.def.leaderDef.winParam, "wood_leader 的阈值来自数据");
            CardInstance giant = toField(g, 0, minion("巨树", 0, 511, 0));
            giant.sealed = true; giant.attack = 0;
            // 用一次普通弃牌触发引擎的胜负复查（与既有用例同样的公开入口）
            g.discardFromHand(g.players[0], toHand(g, 0, vanilla("触发1")), "测试");
            check(!g.over(), "511 < 512 不触发胜利");
            giant.health = 512; giant.maxHealth = 512;
            g.discardFromHand(g.players[0], toHand(g, 0, vanilla("触发2")), "测试");
            check(g.over() && g.winner == 0, "封印随从生命 ≥ 512 触发 GIANT_HEALTH_GE");
        });

        test("古木：511 与 512 通过增幅公式相加后跨过阈值（不靠硬编码生命）", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            realLeader(g, 0, "wood_leader");
            CardInstance l1 = new CardInstance(leaderMinion("敌酋2", 1, 20), 1);
            l1.isLeaderEntity = true; g.players[1].leaderOnField = l1; g.players[1].field.add(l1);
            CardDef woodSource = minion("古木来源3", 1, 1, 0);
            woodSource.faction = "古木圣地";
            CardInstance src = new CardInstance(woodSource, 0);
            CardInstance giant = toField(g, 0, minion("临界巨树", 0, 511, 0));
            g.players[0].rootStacks = 0; g.players[0].rampantStacks = 0;
            g.players[0].field.remove(src);
            Effects.resolve(g, 0, src, Arrays.asList(fx("BUFF", "FRIENDLY_MINION", 1, "both")), new Effects.Ctx());
            eq(512, giant.health, "511 + 1 = 512（无层数时不触发封印）");
            check(!giant.sealed, "未发生增幅则不封印");
            g.discardFromHand(g.players[0], toHand(g, 0, vanilla("触发3")), "测试");
            check(!g.over(), "未封印的 512 不满足 GIANT_HEALTH_GE（要求 sealed）");
            // 再补一次带扎根的增幅：封印成立且阈值达成
            g.players[0].rootStacks = 1;
            g.players[0].field.remove(src);
            Effects.resolve(g, 0, src, Arrays.asList(fx("BUFF", "FRIENDLY_MINION", 1, "root")), new Effects.Ctx());
            check(giant.sealed, "增幅触发封印");
            check(g.over() && g.winner == 0, "封印 + 生命 ≥ 512 即获胜");
        });

        test("古木：增幅层数按 param 分别门控（root 只吃扎根、rampant 只吃疯长）", () -> {
            // C# EffectRuntime.Combat.Buff 的两个条件是独立的：
            //   rootLayers    = growthTarget && (rawMode == "root"    || woodSource) ? RootStacks    : 0
            //   rampantLayers = growthTarget && (rawMode == "rampant" || woodSource) ? RampantStacks : 0
            // 用「非古木来源」隔离 woodSource，才能看到两种层数的独立门控。
            // 夹具：rootStacks=5, rampantStacks=2；目标 1/1，封印会把攻击压到 0。
            GrowthCase a = new GrowthCase(false);
            buff(a, "FRIENDLY_MINION", 2, "root");
            eq(8, a.friendly.health, "param=root：只吃扎根 5 → (2+5)×2^0=7，生命 1+7");
            eq(0, a.friendly.attack, "param=root：增幅发生 → 封印把攻击压到 0");
            check(a.friendly.sealed, "param=root 且有效果增幅 → 封印");
            eq(2, a.g.players[0].rampantStacks, "疯长层数未被读取（只读扎根）");

            GrowthCase b = new GrowthCase(false);
            buff(b, "FRIENDLY_MINION", 2, "rampant");
            eq(9, b.friendly.health, "param=rampant：只吃疯长 2 → (2+0)×2^2=8，生命 1+8");
            eq(0, b.friendly.attack, "param=rampant：增幅发生 → 封印把攻击压到 0");
            check(b.friendly.sealed, "param=rampant 且有效果增幅 → 封印");
            eq(5, b.g.players[0].rootStacks, "扎根层数未被读取（只读疯长）");
            check(b.friendly.health != a.friendly.health,
                    "root 与 rampant 必须给出不同结果（证明两者是独立条件，不是同一个开关）");

            GrowthCase c = new GrowthCase(false);
            buff(c, "FRIENDLY_MINION", 2, "both");
            eq(3, c.friendly.health, "param=both 且非古木来源：layer 都是 0 → 生命 1+2");
            eq(3, c.friendly.attack, "param=both 且非古木来源：攻击 1+2");
            check(!c.friendly.sealed, "无增幅发生（effective==amount）→ 不封印");

            GrowthCase d = new GrowthCase(false);
            buff(d, "FRIENDLY_MINION", 2, "atk");
            eq(3, d.friendly.attack, "param=atk：mode=atk 门控掉层数，攻击 1+2");
            eq(1, d.friendly.health, "param=atk：不动生命");
            check(!d.friendly.sealed, "param=atk 且非古木来源 → 不增幅不封印");

            // 古木来源（woodSource=true）：两种层数都吃，与 param 是否为 root/rampant/both 无关
            for (String param : new String[] { "root", "rampant", "both" }) {
                GrowthCase w = new GrowthCase(true);
                buff(w, "FRIENDLY_MINION", 2, param);
                eq(29, w.friendly.health,
                        "古木来源 param=" + param + "：两种层数都吃 → (2+5)×2^2=28，生命 1+28");
                eq(0, w.friendly.attack, "古木来源 param=" + param + "：封印把攻击压到 0");
                check(w.friendly.sealed, "古木来源 param=" + param + " → 封印");
            }
            // 古木来源 + param=atk：mode=atk 不做数值增幅，但 woodSource 仍让层数进入公式，
            // 因此 effective(28) != amount(2) → 依旧封印。这是 C# 的真实语义。
            GrowthCase woodAtk = new GrowthCase(true);
            buff(woodAtk, "FRIENDLY_MINION", 2, "atk");
            eq(1, woodAtk.friendly.health, "古木来源 param=atk：mode=atk 不动生命");
            eq(0, woodAtk.friendly.attack, "古木来源 param=atk：woodSource 仍启用层数 → growthApplied → 攻击归 0");
            check(woodAtk.friendly.sealed, "古木来源 param=atk 仍封印（C# woodSource 条件）");
        });

        test("古木：非增幅目标不使用任何层数", () -> {
            // ALL_ENEMY_MINIONS / ENEMY_MINION 都不在 IsGrowthTarget 白名单内，
            // 即使来源是古木圣地也必须按 0 层数计算（忽略 growthTarget 的实现会吃满层数）。
            for (String target : new String[] { "ALL_ENEMY_MINIONS", "ENEMY_MINION" }) {
                GrowthCase c = new GrowthCase(true);
                buff(c, target, 2, "rampant");
                eq(3, c.enemyCard.health, target + "：敌方目标按 0 层数计算，生命 1+2");
                eq(3, c.enemyCard.attack, target + "：攻击 1+2");
                check(!c.enemyCard.sealed, target + "：未发生增幅则不封印");
                eq(1, c.friendly.health, target + "：己方随从不是目标，保持原样");
            }
        });

        test("古木：增幅只由 growthApplied 触发密封，关键词由 sealed 门控读取", () -> {
            // growthApplied = (effective != amount)。层数为 0 时不吃层数：即使来源是古木也不封印。
            GrowthCase none = new GrowthCase();
            none.g.players[0].rootStacks = 0;
            none.g.players[0].rampantStacks = 0;
            none.friendly.keywords.add(CardDef.KW_TAUNT);
            buff(none, "FRIENDLY_MINION", 2, "both");
            eq(3, none.friendly.health, "无层数：生命 1+2");
            check(!none.friendly.sealed, "无层数 → 不封印");
            check(none.friendly.has(CardDef.KW_TAUNT), "未封印时关键词仍然生效");

            // 有层数 → 封印：与 C# 一致，关键词集合本身保留，
            // 但 HasKeyword 因 Sealed 返回 false（Java has() 同义），攻击归 0、护盾失效。
            GrowthCase sealed = new GrowthCase();
            sealed.friendly.keywords.add(CardDef.KW_TAUNT);
            sealed.friendly.keywords.add(CardDef.KW_CHARGE);
            sealed.friendly.shield = true;
            buff(sealed, "FRIENDLY_MINION", 2, "root");
            check(sealed.friendly.sealed, "增幅发生 → 封印");
            eq(0, sealed.friendly.attack, "封印后攻击固定为 0");
            eq(false, sealed.friendly.shield, "封印清除护盾");
            check(!sealed.friendly.has(CardDef.KW_TAUNT), "封印后嘲讽读取为 false（C# HasKeyword 同义）");
            check(!sealed.friendly.has(CardDef.KW_CHARGE), "封印后突袭读取为 false");
            check(sealed.friendly.keywords.contains(CardDef.KW_TAUNT),
                    "关键词集合保留（C# 不清空 Keywords，只在 HasKeyword 处门控）");
            check(!sealed.friendly.canAttackNow(), "封印且攻击为 0 时不能攻击");
        });

        test("古木：ADD_ROOT/ADD_RAMPANT 拒绝非正数 amount，且分别饱和/封顶", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            // amount = 0 必须整条跳过（C# TryPositiveAmount）
            Effects.resolve(g, 0, null, Arrays.asList(fx("ADD_ROOT", "NONE", 0, "")), new Effects.Ctx());
            eq(0, g.players[0].rootStacks, "ADD_ROOT amount=0 不加层");
            Effects.resolve(g, 0, null, Arrays.asList(fx("ADD_RAMPANT", "NONE", 0, "")), new Effects.Ctx());
            eq(0, g.players[0].rampantStacks, "ADD_RAMPANT amount=0 不加层");
            // 负数同样整条跳过（不是从 0 夹到 0，也不是倒扣）
            g.players[0].rootStacks = 4;
            g.players[0].rampantStacks = 2;
            Effects.resolve(g, 0, null, Arrays.asList(fx("ADD_ROOT", "NONE", -5, "")), new Effects.Ctx());
            eq(4, g.players[0].rootStacks, "ADD_ROOT 负数被跳过，扎根不变");
            Effects.resolve(g, 0, null, Arrays.asList(fx("ADD_RAMPANT", "NONE", -5, "")), new Effects.Ctx());
            eq(2, g.players[0].rampantStacks, "ADD_RAMPANT 负数被跳过，疯长不变");
            // 正数：扎根可累积，疯长封顶 3
            Effects.resolve(g, 0, null, Arrays.asList(fx("ADD_ROOT", "NONE", 2, "")), new Effects.Ctx());
            eq(6, g.players[0].rootStacks, "ADD_ROOT 2 累积");
            Effects.resolve(g, 0, null, Arrays.asList(fx("ADD_RAMPANT", "NONE", 2, "")), new Effects.Ctx());
            eq(3, g.players[0].rampantStacks, "ADD_RAMPANT 2 封顶到 3（before=2）");
            // 非法 param：整条跳过，不产生任何数值变化
            g.players[0].rootStacks = 1; g.players[0].rampantStacks = 1;
            CardInstance safe = toField(g, 0, minion("非法param目标", 4, 4, 0));
            Effects.resolve(g, 0, null, Arrays.asList(fx("BUFF", "FRIENDLY_MINION", 7, "health")), new Effects.Ctx());
            eq(4, safe.attack, "非法 param 不改攻击");
            eq(4, safe.health, "非法 param 不改生命（旧实现会静默什么都不做，现在明确跳过）");
            check(!safe.sealed, "非法 param 不封印");
        });

        test("古木：wood_growth 与「古木来源 param=rampant」给出 C# 等价数值（真实数据）", () -> {
            // wood_growth = ADD_RAMPANT 1 + BUFF 2 param=rampant（来源为古木圣地）。
            CardDef growth = CardDef.fromMap(loadCardMap("data/cards/wood.json", "wood_growth"));
            eq("古木圣地", growth.faction, "wood_growth 属于古木圣地");
            eq(2, growth.onPlayEffects.size(), "wood_growth 有两条登场效果");
            eq("ADD_RAMPANT", growth.onPlayEffects.get(0).action, "第一条是 ADD_RAMPANT");
            eq(1, growth.onPlayEffects.get(0).amount, "ADD_RAMPANT 1");
            eq("BUFF", growth.onPlayEffects.get(1).action, "第二条是 BUFF");
            eq(2, growth.onPlayEffects.get(1).amount, "BUFF 2");
            eq("rampant", growth.onPlayEffects.get(1).param, "param=rampant");

            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            CardInstance target = toField(g, 0, minion("疯长目标", 2, 3, 0));
            CardInstance src = new CardInstance(growth, 0);      // 不入场，只作效果来源
            for (CardDef.EffectSpec e : growth.onPlayEffects) {
                Effects.resolve(g, 0, src, Arrays.asList(e), new Effects.Ctx());
            }
            eq(1, g.players[0].rampantStacks, "ADD_RAMPANT 1 生效（第一条效果结算后，第二条才读到 1 层）");
            // BUFF 2 param=rampant + woodSource：rootLayers=0, rampantLayers=1
            //   effective = (2+0)×2^1 = 4；增幅发生 → 封印 → 攻击归 0，生命 3+4=7
            eq(7, target.health, "effective=(2+0)×2^1=4 → 生命 3+4");
            eq(7, target.maxHealth, "最大生命同步 +4");
            eq(0, target.attack, "增幅发生 → 封印把攻击压到 0");
            check(target.sealed, "wood_growth 的 BUFF 会封印目标");
        });

        test("古木：wood.json 里 10 个 ADD_ROOT（含 wood_seed 的吟唱）都是 +2（真实数据）", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            int rootEntries = 0;
            int rootTotal = 0;
            int rampantTotal = 0;
            for (CardDef card : g.library.byFile.getOrDefault("wood.json", new ArrayList<>())) {
                List<CardDef.EffectSpec> all = new ArrayList<>(card.onPlayEffects);
                all.addAll(card.chantEffects);
                for (CardDef.EffectSpec e : all) {
                    if (!"ADD_ROOT".equals(e.action) && !"ADD_RAMPANT".equals(e.action)) continue;
                    if ("ADD_ROOT".equals(e.action)) {
                        eq(2, e.amount, card.id + " 的 ADD_ROOT 固定为 2");
                    } else {
                        check(e.amount >= 1 && e.amount <= 3, card.id + " 的 ADD_RAMPANT 在 1..3");
                    }
                    eq("NONE", e.target, card.id + " 的 " + e.action + " 不带目标类型");
                    // 每次结算前把疯长归零，避免后一条 ADD_RAMPANT 被 3 层上限吞掉，
                    // 这样「数据声明值之和」与「引擎实际层数」才能直接比对。
                    g.players[0].rampantStacks = 0;
                    Effects.resolve(g, 0, null, Arrays.asList(e), new Effects.Ctx());
                    if ("ADD_ROOT".equals(e.action)) { rootEntries++; rootTotal += e.amount; }
                    else rampantTotal += e.amount;
                }
            }
            eq(10, rootEntries, "wood.json 共 10 个 ADD_ROOT（9 张卡的登场效果 + wood_seed 的吟唱效果）");
            eq(20, rootTotal, "每个都是 +2 → 合计 +20");
            eq(20, g.players[0].rootStacks, "逐条结算后扎根层数 = 20（扎根不封顶、可累积）");
            check(rampantTotal >= 1, "wood.json 至少有一个 ADD_RAMPANT");
            check(g.players[0].rampantStacks <= 3, "疯长封顶在 3");
            check(g.players[0].usedTags.isEmpty(), "效果直接结算不消耗词条（词条只在出牌时消耗）");
        });

        // ===================== 机械 B 模式（RULES §12.4）=====================

        test("机械：COMMIT 先按 commitCost 惩罚抽牌，卡进提交队列并结算提交效果", () -> {
            TestAgent a0 = new TestAgent(), a1 = new TestAgent();
            Game g = freshGame(bal(), a0, a1);
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardDef spark = CardDef.fromMap(loadCardMap("data/cards/machine.json", "machine_spark"));
            eq(1, spark.commitCost, "machine_spark 的 commitCost 来自数据");
            eq(1, spark.commitEffects.size(), "machine_spark 有提交效果");
            CardInstance card = toField(g, 0, spark);
            int before = g.players[1].hand.size();
            int ownBefore = g.players[0].hand.size();
            check(g.commitCard(card), "提交应当成功");
            check(!g.players[0].field.contains(card), "提交后离开场上");
            check(g.players[0].commitQueue.contains(card), "进入提交队列");
            eq(before + 1, g.players[1].hand.size(), "对方按 commitCost 抽 1 张");
            eq(1, g.players[1].punishDrawnThisTurn, "惩罚抽牌计数 +1");
            eq(ownBefore + 1, g.players[0].hand.size(), "提交效果 DRAW 1 结算");
            eq(1, g.players[0].commitQueue.size(), "队列只保留这一张");
        });

        test("机械：非机械卡不可提交，PUSH 时点不能主动发起", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            CardInstance ordinary = toField(g, 0, minion("普通随从", 2, 2, 0));
            check(g.whyCannotCommit(ordinary) != null, "普通随从不可提交");
            check(!g.commitCard(ordinary), "提交被拒绝");
            check(g.players[0].field.contains(ordinary), "拒绝后状态不变");
            check(g.whyCannotPush() != null, "上传不是玩家主动动作");
        });

        test("机械：上传按 FIFO 进云端栈，显式 uploadCost 在该时点惩罚，上传效果结算", () -> {
            TestAgent a0 = new TestAgent(), a1 = new TestAgent();
            a0.vetoPush = true; // 旧代理会否决；结束阶段引擎仍必须自动上传
            Game g = freshGame(bal(), a0, a1);
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardInstance carrier = toField(g, 0, machineMinion("载体A", 1, 3));
            CardDef wall = CardDef.fromMap(loadCardMap("data/cards/machine.json", "machine_wall"));
            CardDef assembler = CardDef.fromMap(loadCardMap("data/cards/machine.json", "machine_assembler"));
            eq(0, wall.uploadCost, "machine_wall 的 uploadCost 默认 0（不双算）");
            eq(1, assembler.pushEffects.size(), "machine_assembler 有上传效果");
            CardInstance a = toField(g, 0, wall);
            CardInstance b = toField(g, 0, assembler);
            check(g.commitCard(a), "提交 A");
            check(g.commitCard(b), "提交 B");
            eq(2, g.players[0].commitQueue.size(), "队列中有 2 张");
            int handBefore = g.players[0].hand.size();
            g.endTurn();      // 结束阶段：自动上传（FIFO）
            check(g.players[0].commitQueue.isEmpty(), "上传后队列清空");
            eq(2, g.players[0].cloudStack.size(), "两张卡进入云端栈");
            eq(a.uid, g.players[0].cloudStack.get(0).uid, "先提交的 A 在栈底");
            eq(b.uid, g.players[0].cloudStack.get(1).uid, "后提交的 B 在栈顶");
            eq(handBefore + 1, g.players[0].hand.size(), "machine_assembler 的上传效果 DRAW 1 结算");
        });

        test("机械：PULL 下载栈顶 → 惩罚/效果/墓地/计数/地标层数", () -> {
            TestAgent a0 = new TestAgent(), a1 = new TestAgent();
            Game g = freshGame(bal(), a0, a1);
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardInstance landmark = realLeader(g, 0, "machine_leader");
            check(landmark.def.leaderDef.isLandmark, "machine_leader 是地标");
            eq(2, landmark.def.leaderDef.landmarkTiers.size(), "数据里有 2 层地标层级");
            // 机械统领是 SPELL 型地标（非随从、无攻血）：它是合法下载载体，但不是 FRIENDLY_MINION
            check(!landmark.def.isMinion(), "地标不是随从，不参与 FRIENDLY_MINION 目标");
            CardInstance carrier = toField(g, 0, machineMinion("下载载体", 1, 3));
            CardInstance drone = toCloud(g, 0, g.library.get("machine_drone"));
            CardInstance lower = new CardInstance(machineMinion("下层卡", 1, 1), 0);
            g.players[0].cloudStack.add(0, lower);
            eq(1, drone.def.downloadCost, "machine_drone 的 downloadCost 来自数据");
            eq(1, drone.def.pullEffects.size(), "machine_drone 有下载效果");
            int handBefore = g.players[1].hand.size();
            eq(2, g.players[0].cloudStack.size(), "云端有下层卡 + 栈顶 drone");
            check(g.players[0].cloudStack.get(1) == drone, "drone 在栈顶");
            check(g.pullWith(landmark), "地标可以作为下载载体");
            eq(1, g.players[0].cloudStack.size(), "只掉栈顶一张，下层卡留在云端");
            check(g.players[0].cloudStack.contains(lower), "下层卡没有被跳过");
            check(g.players[0].graveyard.contains(drone), "被下载的卡进墓地");
            eq(1, g.players[0].pullCount, "累计下载 +1");
            eq(handBefore + 1, g.players[1].hand.size(), "对方按 downloadCost 抽 1 张");
            eq(4, carrier.health, "下载效果 BUFF 1/1 落在唯一的己方随从（载体）上：3+1");
            eq(1, landmark.landmarkPullCount, "地标层数推进到 1");
            check(landmark.chantRemaining == 0 && landmark.pendingLandmarkSummonCardId == null,
                    "第 1 层不启动吟唱晋升");
        });

        test("机械：生产路径地标自承 PULL、云端 LIFO 与 Alpha 载体接替", () -> {
            TestAgent p0 = new TestAgent();
            Game g = freshGame(bal(), p0, new TestAgent());
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardInstance landmark = drawLeaderThroughProductionPath(g, 0, "machine_leader");
            check(!g.players[0].field.contains(landmark), "SPELL 地标经 enterLeader 进入统领区，不伪装成战场随从");

            CardInstance lower = toCloud(g, 0, machineMinion("地标PULL-下层", 1, 1));
            CardInstance top = toCloud(g, 0, machineMinion("地标PULL-顶层", 1, 1));
            check(g.isDownloadCarrier(g.players[0], landmark), "当前活动的己方机械地标是下载载体");
            check(g.pull(), "仅有地标载体时可以发起 PULL");
            eq(1, g.players[0].cloudStack.size(), "第一次 PULL 仅移除云端栈顶");
            check(g.players[0].cloudStack.get(0) == lower, "较早入栈的下层牌仍留在栈中");
            eq(top.uid, g.players[0].graveyard.get(0).uid, "后入栈的顶牌先下载并进入墓地");
            check(!g.players[0].graveyard.contains(lower), "未跳过栈顶下载下层牌");

            check(g.pull(), "地标继续接收第二次 PULL");
            check(g.players[0].cloudStack.isEmpty(), "第二次 PULL 后两张云端牌均已移除");
            eq(lower.uid, g.players[0].graveyard.get(1).uid, "第二次按 LIFO 下载剩余下层牌");
            eq(2, g.players[0].pullCount, "两次成功下载分别推进全局计数");
            eq(2, landmark.landmarkPullCount, "两次成功下载分别推进地标层数");

            g.endTurn(); // 生产结束阶段推进吟唱，地标被替换为 machine_alpha
            CardInstance alpha = g.players[0].leaderOnField;
            check(alpha != null && "machine_alpha".equals(alpha.def.id), "地标完成吟唱并晋升为 Alpha");
            check(g.players[0].graveyard.contains(landmark), "晋升后的旧地标在墓地");
            check(!g.isDownloadCarrier(g.players[0], landmark), "失去活动统领身份的旧地标不再是载体");
            check(alpha != null && g.isDownloadCarrier(g.players[0], alpha), "战场上的机械 Alpha 接替成为载体");

            // The next PULL is checked against the promoted production entity.
            g.currentIdx = 0;
            g.phase = Game.Phase.ACTION;
            CardInstance alphaTop = toCloud(g, 0, machineMinion("AlphaPULL-顶层", 1, 1));
            p0.rejectTargetSelection = true;
            check(g.pull(), "Alpha 同时是 field 随从和活动统领指针时只计一个载体，不要求重复选择");
            p0.rejectTargetSelection = false;
            check(g.players[0].graveyard.contains(alphaTop), "Alpha PULL 仍将成功下载卡移入墓地");
            eq(3, g.players[0].pullCount, "Alpha 接替后成功下载继续累计");

            CardInstance controlSource = toField(g, 0, minion("控制者", 1, 1, 0));
            CardInstance controlled = toField(g, 1, machineMinion("受控机械载体", 1, 1));
            Effects.resolve(g, 0, controlSource,
                    Arrays.asList(fx("CONTROL", "ENEMY_MINION", 1, "")), new Effects.Ctx());
            check(controlled.controllerIdx() == 0 && g.players[0].field.contains(controlled),
                    "CONTROL 通过生产效果将机械单位移入控制方战场");
            check(g.isDownloadCarrier(g.players[0], controlled), "己方控制的机械单位可作为下载载体");
            check(!g.isDownloadCarrier(g.players[1], controlled), "原拥有方不再按旧 ownerIdx 将其视为己方载体");
        });

        test("机械：PULL 多目标缺失/非法选择在惩罚与消费前拒绝", () -> {
            TestAgent a0 = new TestAgent(), a1 = new TestAgent();
            Game g = freshGame(bal(), a0, a1);
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardInstance carrierA = toField(g, 0, machineMinion("载体甲", 1, 3));
            CardInstance carrierB = toField(g, 0, machineMinion("载体乙", 2, 3));
            CardInstance drone = toCloud(g, 0, g.library.get("machine_drone"));
            int beforeHand = g.players[1].hand.size();
            int beforeCloud = g.players[0].cloudStack.size();
            a0.rejectTargetSelection = true;
            check(!g.pull(), "多载体缺失选择必须拒绝");
            eq(beforeHand, g.players[1].hand.size(), "载体缺失选择不支付下载惩罚");
            eq(beforeCloud, g.players[0].cloudStack.size(), "载体缺失选择不消费云端栈");
            eq(0, g.players[0].pullCount, "载体缺失选择不推进下载计数");

            a0.rejectTargetSelection = false;
            a0.selectedTarget = new CardInstance(machineMinion("非法目标", 9, 9), 0);
            check(!g.pullWith(carrierA), "多效果目标返回非法实体必须拒绝");
            eq(beforeHand, g.players[1].hand.size(), "非法效果目标不支付下载惩罚");
            eq(beforeCloud, g.players[0].cloudStack.size(), "非法效果目标不消费云端栈");
            check(g.players[0].cloudStack.contains(drone), "非法效果目标后栈顶仍为原卡");

            a0.selectedTarget = carrierB;
            check(g.pullWith(carrierA), "提交合法效果目标后下载成功");
            check(carrierB.health == 4 && carrierA.health == 3,
                    "合法选择只强化被选中的己方随从");
        });

        test("机械：地标第 2 层启动吟唱1，结束阶段召唤 machine_alpha 取代地标", () -> {
            TestAgent a0 = new TestAgent(), a1 = new TestAgent();
            Game g = freshGame(bal(), a0, a1);
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardInstance landmark = realLeader(g, 0, "machine_leader");
            toCloud(g, 0, machineMinion("云1", 1, 1));
            toCloud(g, 0, machineMinion("云2", 1, 1));
            eq(1, landmark.def.leaderDef.tierAt(2).chant, "第 2 层 chant=1");
            eq("machine_alpha", landmark.def.leaderDef.tierAt(2).summon, "第 2 层 summon=machine_alpha");
            check(g.pullWith(landmark), "第 1 次下载");
            check(g.pullWith(landmark), "第 2 次下载");
            eq(2, landmark.landmarkPullCount, "地标层数 2");
            eq(1, landmark.chantRemaining, "第 2 层启动吟唱1");
            eq("machine_alpha", landmark.pendingLandmarkSummonCardId, "待晋升目标记录在地标上");
            check(g.players[0].leaderOnField == landmark, "吟唱期间地标仍是统领");
            g.endTurn();     // 结束阶段：吟唱完成 → 摧毁地标 → 召唤 Alpha
            CardInstance newLeader = g.players[0].leaderOnField;
            check(newLeader != null && newLeader.def.id.equals("machine_alpha"), "Alpha 成为新统领");
            eq("PULL_TOTAL_GE", newLeader.def.leaderDef.winCondition, "Alpha 的胜利条件来自数据");
            eq(6, newLeader.def.leaderDef.winParam, "Alpha 的 winParam=6");
            check(g.players[0].graveyard.contains(landmark), "旧地标被摧毁进墓地");
            check(g.players[0].field.contains(newLeader), "Alpha 以随从身份在场");
        });

        test("机械：ROLLBACK 把提交队列中的卡回滚回手，且不触发上传/下载", () -> {
            TestAgent a0 = new TestAgent();
            Game g = freshGame(bal(), a0, new TestAgent());
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardDef recycler = CardDef.fromMap(loadCardMap("data/cards/machine.json", "machine_recycler"));
            eq(1, recycler.commitCost, "machine_recycler 的 commitCost 来自数据");
            eq(1, recycler.commitEffects.size(), "machine_recycler 有 ROLLBACK 提交效果");
            eq("ROLLBACK", recycler.commitEffects.get(0).action, "提交效果就是 ROLLBACK");
            CardInstance targetCard = toField(g, 0, machineMinion("被回滚的卡", 1, 1));
            CardInstance rec = toField(g, 0, recycler);
            a0.selectedRollback = targetCard;
            check(g.commitCard(targetCard), "先提交一张，队列里已有目标");
            check(g.commitCard(rec), "再提交回收单元，触发回滚");
            check(g.players[0].hand.contains(targetCard), "队列中的卡被回滚回手");
            check(!g.players[0].commitQueue.contains(targetCard), "回滚后离开提交队列");
            eq(1, g.players[0].commitQueue.size(), "队列里只剩回收单元自己");
            // 明确指定回滚目标（多于一张时由代理选择）
            a0.selectedRollback = rec;
            CardInstance extra = toField(g, 0, machineMinion("额外卡", 1, 1));
            check(g.commitCard(extra), "提交额外卡");
            eq(2, g.players[0].commitQueue.size(), "队列里有回收单元与额外卡");
            Effects.resolve(g, 0, rec, Arrays.asList(fx("ROLLBACK", "NONE", 1, "")), new Effects.Ctx());
            check(g.players[0].hand.contains(rec), "代理指定的目标被回滚");
            eq(1, g.players[0].commitQueue.size(), "额外卡仍留在队列");
            check(g.players[0].cloudStack.isEmpty(), "回滚不触发上传");
            eq(0, g.players[0].pullCount, "回滚不触发下载");
        });

        test("机械：ROLLBACK 多目标缺失/非法选择拒绝且队列不变", () -> {
            TestAgent a0 = new TestAgent();
            Game g = freshGame(bal(), a0, new TestAgent());
            CardInstance first = new CardInstance(machineMinion("队列一", 1, 1), 0);
            CardInstance second = new CardInstance(machineMinion("队列二", 1, 1), 0);
            g.players[0].commitQueue.add(first);
            g.players[0].commitQueue.add(second);
            Effects.resolve(g, 0, first, Arrays.asList(fx("ROLLBACK", "NONE", 1, "")), new Effects.Ctx());
            eq(2, g.players[0].commitQueue.size(), "缺失多目标选择时队列保持不变");
            check(g.players[0].hand.isEmpty(), "缺失多目标选择时没有卡回手");
            a0.selectedRollback = new CardInstance(machineMinion("队列外", 1, 1), 0);
            Effects.resolve(g, 0, first, Arrays.asList(fx("ROLLBACK", "NONE", 1, "")), new Effects.Ctx());
            eq(2, g.players[0].commitQueue.size(), "非法多目标选择时队列保持不变");
            check(g.players[0].hand.isEmpty(), "非法多目标选择时没有卡回手");
        });

        test("机械：下载载体不可用时下载被拒绝（状态不变）", () -> {
            TestAgent a0 = new TestAgent();
            Game g = freshGame(bal(), a0, new TestAgent());
            g.library = realLibrary();
            realLeader(g, 0, "machine_leader");
            CardInstance drone = toCloud(g, 0, g.library.get("machine_drone"));
            g.players[0].field.clear();
            g.players[0].leaderOnField = null;      // 无任何机械载体
            check(g.whyCannotPull() != null, "没有载体时不可下载");
            check(!g.pull(), "下载被拒绝");
            check(g.players[0].cloudStack.contains(drone), "云端栈不变");
            eq(0, g.players[0].pullCount, "下载计数不推进");
            eq(0, g.players[0].graveyard.size(), "没有卡进墓地");
        });

        test("机械：PULL_TOTAL_GE 在 winParam=6 时触发胜利", () -> {
            Game g = freshGame(bal(), new TestAgent(), new TestAgent());
            g.library = realLibrary();
            fillDeck(g, 0, 10); fillDeck(g, 1, 10);
            CardInstance landmark = realLeader(g, 0, "machine_leader");
            eq(6, landmark.def.leaderDef.winParam, "machine_leader 的 winParam 来自数据");
            CardInstance l1 = new CardInstance(leaderMinion("敌酋3", 1, 20), 1);
            l1.isLeaderEntity = true; g.players[1].leaderOnField = l1; g.players[1].field.add(l1);
            // 5 次下载不触发，第 6 次触发（每次下载都会走引擎的胜负复查）
            for (int i = 0; i < 5; i++) {
                toCloud(g, 0, machineMinion("云卡" + i, 1, 1));
                check(g.pullWith(landmark), "第 " + (i + 1) + " 次下载");
            }
            eq(5, g.players[0].pullCount, "累计下载 5");
            check(!g.over(), "5 < 6 不触发 PULL_TOTAL_GE");
            toCloud(g, 0, machineMinion("云卡5", 1, 1));
            check(g.pullWith(landmark), "第 6 次下载");
            eq(6, g.players[0].pullCount, "累计下载 6");
            check(g.over() && g.winner == 0, "累计下载 6 次触发 PULL_TOTAL_GE");
        });

        test("机械：数据解析与 toMap 往返保留 commitCost/commitEffects/landmarkTiers", () -> {
            java.util.Map<String, Object> machineMap = loadCardMap("data/cards/machine.json", "machine_leader");
            CardDef leaderCard = CardDef.fromMap(machineMap);
            check(leaderCard.leaderDef.isLandmark, "isLandmark 解析");
            eq(2, leaderCard.leaderDef.landmarkTiers.size(), "landmarkTiers 解析");
            eq(1, leaderCard.leaderDef.landmarkTiers.get(1).chant, "第 2 层 chant");
            eq("machine_alpha", leaderCard.leaderDef.landmarkTiers.get(1).summon, "第 2 层 summon");
            java.util.Map<String, Object> backLeader = CardDef.fromMap(
                    Json.parseObject(Json.write(leaderCard.toMap(), true))).toMap();
            eq(true, backLeader.containsKey("leaderDef"), "统领定义写出");
            check(Json.write(backLeader, true).contains("landmarkTiers"), "landmarkTiers 往返不丢");

            CardDef droneCard = CardDef.fromMap(loadCardMap("data/cards/machine.json", "machine_drone"));
            eq(1, droneCard.commitCost, "commitCost 解析");
            eq(0, droneCard.uploadCost, "显式 uploadCost=0 被保留");
            eq(1, droneCard.downloadCost, "downloadCost 解析");
            eq(1, droneCard.pullEffects.size(), "pullEffects 解析");
            CardDef backDrone = CardDef.fromMap(Json.parseObject(Json.write(droneCard.toMap(), true)));
            eq(1, backDrone.commitCost, "commitCost 往返");
            eq(0, backDrone.uploadCost, "显式 0 往返后仍是 0（不会变成默认值）");
            eq(1, backDrone.downloadCost, "downloadCost 往返");
            eq(1, backDrone.pullEffects.size(), "pullEffects 往返");
            eq("BUFF", backDrone.pullEffects.get(0).action, "下载效果动作往返");
            // 未显式声明的普通机械随从吃默认值 1/0/1
            CardDef implicit = machineMinion("默认值机偶", 1, 1);
            implicit.tags.add("机偶");
            implicit.applyMechanicalLifecycleDefaults();
            eq(1, implicit.commitCost, "默认 commitCost=1");
            eq(0, implicit.uploadCost, "默认 uploadCost=0");
            eq(1, implicit.downloadCost, "默认 downloadCost=1");
            eq(1, implicit.pullEffects.size(), "默认下载效果 +1/+1");
            // 未知字段仍然完整保留
            CardDef custom = CardDef.fromMap(loadCardMap("data/cards/machine.json", "machine_golem"));
            check(custom.custom.isEmpty(), "已识别字段不落入 custom");
        });

        test("惩罚连锁：深度正好等于 chainLimit 时仍要响应（对照 C# 的 > 而非 >=）", () -> {
            // C# PlayCardActionHandler.ResolvePunishResponses:306
            //   if (chainDepth > _chainLimit ...) return;
            // 「等于上限」这一层仍然结算，只有「超过上限」才停下。
            // 夹具：chainLimit=1，引擎惩罚1，双方卡组全是「惩罚发动时再让对方抽1张」的惩罚牌。
            // 实测（A/B，见报告）：
            //   chainLimit=1 → 新规则 1 次响应 / 旧规则(>=) 0 次响应   ← 边界就在这里
            //   chainLimit=3 → 新规则 2 次响应 / 旧规则(>=) 1 次响应
            int[] atLimit = punishChainAsks(1);
            eq(0, atLimit[0], "chainLimit=1 时 P0 不会被追问（深度已超过上限）");
            eq(1, atLimit[1], "chainLimit=1 时 P1 在 depth==limit 这一层必须仍被询问（旧规则会得到 0）");
            int[] aboveLimit = punishChainAsks(3);
            eq(1, aboveLimit[0], "chainLimit=3 时 P0 在第 2 层被询问 1 次（> limit 才停止）");
            eq(1, aboveLimit[1], "chainLimit=3 时 P1 被询问 1 次");
            check(atLimit[0] + atLimit[1] < aboveLimit[0] + aboveLimit[1],
                    "更小的上限必须产生更少的响应（证明边界比较真的在起作用）");
        });

        T1PunishResponseWindowTests.run();

        // ===================== 条件语法（卡面发动条件的引擎支持） =====================
        // 卡面文案是手写的，所以"卡面写「若……则……」"只有在引擎真能求值那句话时才诚实。
        ConditionGrammarTests.run();
        EffectParityTests.run();

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
