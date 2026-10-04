package com.dominionwars.test;

import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Effects;
import com.dominionwars.engine.Game;
import com.dominionwars.model.CardDef;
import com.dominionwars.model.CardDef.CardType;

import static com.dominionwars.test.TestMain.*;

/**
 * 条件语法（卡级 {@code punishCondition} 与每效果 {@code condition}）的回归测试。
 *
 * <p>背景（2026-09-13）：卡面文案是手写的、不由效果列表反推，所以"卡面写「若……则……」"
 * 只有在引擎真能求值那句话时才是诚实的。这批测试钉住两件事：
 * <ol>
 *   <li><b>每个词条真的能求值</b>，且语义与 C# {@code PunishConditionEvaluator} 一致；</li>
 *   <li><b>未知词条 fail-closed</b> —— 修正前 Java 对未知词条返回 true，与 C# 的 false 相反；
 *       一个笔误就能把受条件保护的高收益卡变成无门槛卡，且在 Java 侧看不出来。</li>
 * </ol>
 */
final class ConditionGrammarTests {

    private ConditionGrammarTests() {}

    /** 造一局空白对局：双方空卡组、ACTION 阶段、P0 行动。 */
    private static Game g() {
        TestAgent a0 = new TestAgent();
        TestAgent a1 = new TestAgent();
        return freshGame(bal(), a0, a1);
    }

    static void run() {

        // ── A. 未知词条必须 fail-closed（这是本次修正的核心） ──
        test("条件：未知词条 fail-closed，不得无条件结算", () -> {
            Game g = g();
            check(!Effects.checkCondition(g, 0, "TOTALLY_MADE_UP_TOKEN"),
                    "未知词条必须为假（fail-closed），否则写错条件 = 无条件生效");
            check(!Effects.checkCondition(g, 0, "EMEMY_MINIONS_GE_1"),
                    "拼错前缀（EMEMY）也必须 fail-closed");
            check(Effects.checkCondition(g, 0, null)
                            && Effects.checkCondition(g, 0, "")
                            && Effects.checkCondition(g, 0, "ALWAYS"),
                    "空/null/ALWAYS 应视为无条件成立");

            String[] malformedThresholds = {
                    "SELF_PULL_GE_",
                    "SELF_PULL_GE_-1",
                    "SELF_PULL_GE_+1",
                    "SELF_PULL_GE_1x",
                    "SELF_PULL_GE_2147483648",
                    "SELF_PULL_GE_999999999999999999999"
            };
            for (String condition : malformedThresholds) {
                check(!Effects.checkCondition(g, 0, condition),
                        condition + " 的坏后缀/负数/溢出必须 fail-closed");
            }
            check(Effects.checkCondition(g, 0, "SELF_PULL_GE_0"),
                    "非负十进制 0 是合法阈值");
        });

        // ── B. 古木：封印身份与封印生命（胜利轴 = 最大封印随从生命）──
        test("条件：古木封印身份与封印生命（含受伤后读当前生命）", () -> {
            Game g = g();
            check(!Effects.checkCondition(g, 0, "SELF_SEALED_GE_1"), "空场时 SELF_SEALED_GE_1 应为假");

            CardInstance plain = toField(g, 0, minion("plain", 2, 3, 0));
            check(!Effects.checkCondition(g, 0, "SELF_SEALED_GE_1"), "未封印随从不应计入封印数");

            CardInstance sealedA = toField(g, 0, minion("sealed_a", 0, 40, 0));
            sealedA.sealed = true;
            check(Effects.checkCondition(g, 0, "SELF_SEALED_GE_1"), "有封印随从后应为真");
            check(!Effects.checkCondition(g, 0, "SELF_SEALED_GE_2"), "只有 1 个封印时应为假");

            CardInstance sealedB = toField(g, 0, minion("sealed_b", 0, 200, 0));
            sealedB.sealed = true;
            check(Effects.checkCondition(g, 0, "SELF_SEALED_GE_2"), "两个封印后应为真");

            // 封印生命取全场最大者（与 C# SealedMinionMaxHealthCondition 同源）
            check(Effects.checkCondition(g, 0, "SELF_SEALED_HEALTH_GE_200"),
                    "最大封印生命为 200 时阈 200 应为真");
            check(!Effects.checkCondition(g, 0, "SELF_SEALED_HEALTH_GE_201"),
                    "阈 201 超过最大封印生命应为假");

            // 读的是「当前生命」而非「生命上限」：受伤掉到 30 后阈 200 必须变假
            sealedB.health = 30;
            check(!Effects.checkCondition(g, 0, "SELF_SEALED_HEALTH_GE_200"),
                    "封印随从受伤后应读当前生命，阈 200 变假");
        });

        // ── C. 古木：扎根 / 疯长 ──
        test("条件：扎根与疯长（含疯长硬上限 3）", () -> {
            Game g = g();
            check(!Effects.checkCondition(g, 0, "SELF_ROOT_GE_1"), "初始扎根为 0");
            g.players[0].rootStacks = 5;
            check(Effects.checkCondition(g, 0, "SELF_ROOT_GE_5"), "扎根 5 应满足阈 5");
            check(!Effects.checkCondition(g, 0, "SELF_ROOT_GE_6"), "扎根 5 不应满足阈 6");

            check(!Effects.checkCondition(g, 0, "SELF_RAMPANT_GE_1"), "初始疯长为 0");
            g.players[0].rampantStacks = 3;
            check(Effects.checkCondition(g, 0, "SELF_RAMPANT_GE_3"), "疯长 3 应满足阈 3");
            check(!Effects.checkCondition(g, 0, "SELF_RAMPANT_GE_4"),
                    "疯长硬上限 3，阈 4 永远不可能成立");
        });

        // ── D. 机械：提交队列 / 云端栈 / 累计下载 ──
        test("条件：提交队列、云端栈与累计下载", () -> {
            Game g = g();
            check(!Effects.checkCondition(g, 0, "SELF_COMMIT_GE_1"), "空提交队列应为假");
            g.players[0].commitQueue.add(new CardInstance(minion("q1", 1, 1, 0), 0));
            check(Effects.checkCondition(g, 0, "SELF_COMMIT_GE_1"), "队列 1 张应满足阈 1");
            check(!Effects.checkCondition(g, 0, "SELF_COMMIT_GE_2"), "队列 1 张不应满足阈 2");

            check(!Effects.checkCondition(g, 0, "SELF_CLOUD_GE_1"), "空云端栈应为假");
            g.players[0].cloudStack.add(new CardInstance(minion("c1", 1, 1, 0), 0));
            check(Effects.checkCondition(g, 0, "SELF_CLOUD_GE_1"), "云端栈 1 张应满足阈 1");

            check(!Effects.checkCondition(g, 0, "SELF_PULL_GE_1"), "累计下载 0 应为假");
            g.players[0].pullCount = 3;
            check(Effects.checkCondition(g, 0, "SELF_PULL_GE_3"), "累计下载 3 应满足阈 3");
        });

        // ── E. 深海：对手弃牌必须读「对手」的计数器（与胜利条件同源）──
        test("条件：OPP_DISCARD 读对手而非自己（与弃牌胜利条件同源）", () -> {
            Game g = g();
            check(!Effects.checkCondition(g, 0, "OPP_DISCARD_GE_1"), "初始应为假");
            g.players[0].totalDiscarded = 99;   // 焦点方自己弃 99 张
            check(!Effects.checkCondition(g, 0, "OPP_DISCARD_GE_1"),
                    "OPP_DISCARD 必须读对手的 totalDiscarded；读自己会让深海卡在错误时点发动");
            g.players[1].totalDiscarded = 12;
            check(Effects.checkCondition(g, 0, "OPP_DISCARD_GE_12"), "对手弃 12 应满足阈 12");
            check(!Effects.checkCondition(g, 0, "OPP_DISCARD_GE_13"), "不应满足阈 13");
        });

        // ── F. 手牌上下界 / 场面 / 伏击 ──
        test("条件：手牌上下界、场面与伏击", () -> {
            Game g = g();
            check(!Effects.checkCondition(g, 0, "HAND_GE_1"), "空手应为假");
            toHand(g, 0, vanilla("h1"));
            check(Effects.checkCondition(g, 0, "HAND_GE_1"), "手牌 1 张应满足阈 1");

            // OPP_HAND_LE_ 是 OPP_HAND_GE_ 的反面：深海要「饿死对手手牌」作为收益门槛
            check(Effects.checkCondition(g, 0, "OPP_HAND_LE_0"), "对手空手应满足阈 ≤0");
            toHand(g, 1, vanilla("o1"));
            check(!Effects.checkCondition(g, 0, "OPP_HAND_LE_0"), "对手有手牌后不应满足阈 ≤0");
            check(Effects.checkCondition(g, 0, "OPP_HAND_GE_1"), "对手 1 张手牌应满足阈 1");

            check(!Effects.checkCondition(g, 0, "SELF_MINIONS_GE_1"), "手牌不应算作随从");
            toField(g, 0, minion("m1", 2, 2, 0));
            check(Effects.checkCondition(g, 0, "SELF_MINIONS_GE_1"), "场上有随从后应为真");

            check(!Effects.checkCondition(g, 0, "ENEMY_MINIONS_GE_1"), "对方空场应为假");

            check(!Effects.checkCondition(g, 0, "SELF_AMBUSH_GE_1"), "无伏击应为假");
            CardDef amb = new CardDef();
            amb.id = "amb1"; amb.name = "amb1"; amb.type = CardType.AMBUSH;
            g.players[0].ambushes.add(new CardInstance(amb, 0));
            check(Effects.checkCondition(g, 0, "SELF_AMBUSH_GE_1"), "盖放 1 张伏击后应为真");
        });

        // ── G. 每效果 condition 真的在 Java 生效（修正前 Java 从不求值）──
        test("条件：每效果 condition 在 Java 生效（不满足则不结算）", () -> {
            Game g = g();
            // 一个「若对方场上有随从，则抽1张」的效果：对方空场时不得抽牌
            CardDef d = spell("条件测试", 0, fx("DRAW", "SELF", 1, ""));
            d.onPlayEffects.get(0).condition = "ENEMY_MINIONS_GE_1";
            CardInstance src = toField(g, 0, d);
            // 必须有牌可抽：空牌库时引擎会重建洗牌、无牌可抽就放弃，
            // 那会让"条件满足后是否结算"这件事测不出来（抽 0 张 ≠ 没结算）。
            fillDeck(g, 0, 3);

            int before = g.players[0].hand.size();
            Effects.resolve(g, 0, src, d.onPlayEffects, new Effects.Ctx());
            check(g.players[0].hand.size() == before,
                    "条件不满足时每效果 condition 必须阻止结算（Java 修正前会照常结算）");

            // 对方登场后条件满足 → 效果结算
            toField(g, 1, minion("foe", 1, 1, 0));
            Effects.resolve(g, 0, src, d.onPlayEffects, new Effects.Ctx());
            check(g.players[0].hand.size() == before + 1,
                    "条件满足后效果应正常结算（对照：不满足时抽 0 张）");
        });

        // ── H. 卡面用语：词条必须能翻成人话（卡面与效果统一的前提）──
        test("条件文案：词条可翻成卡面用语", () -> {
            eq("己方封印随从≥2", Effects.describeCondition("SELF_SEALED_GE_2"), "封印随从文案");
            eq("对方累计弃牌≥9", Effects.describeCondition("OPP_DISCARD_GE_9"), "对手弃牌文案");
            eq("己方云端栈≥1", Effects.describeCondition("SELF_CLOUD_GE_1"), "云端栈文案");
            eq("无条件", Effects.describeCondition("ALWAYS"), "无条件文案");
            eq("无条件", Effects.describeCondition(null), "null 条件文案");
        });
    }
}
