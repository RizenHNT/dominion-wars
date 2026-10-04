import java.nio.file.*;
import java.util.*;
import java.util.regex.*;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.model.CardDef;

/**
 * 卡池契约校验器：入库前逐张强制校验，任何一项不过就拒绝入库。
 *
 * <p><b>为什么要有这个（2026-09-13 的四个真实事故）：</b>今天出现了四次
 * 「设计稿说 A、数据/引擎实际是 B」且都<b>静默通过</b>的错误：
 * <ol>
 *   <li>转换器正则吞掉效果文本 ⇒ <code>amount</code> 丢失变 0 ⇒ 卡不再加扎根，实测结论全废；</li>
 *   <li><code>text</code> 被写成阵营名（"烈焰"）⇒ 卡面文案整个丢失；</li>
 *   <li><code>param</code> 被写成 <code>"param=嘲讽"</code> ⇒ 关键词授予以空参结算；</li>
 *   <li>读取单元格时列索引错位 ⇒ 把 <code>targetFaction</code> 当成卡面文本。</li>
 * </ol>
 * 四次都不是"引擎坏了"，而是<b>没有一道机器闸门</b>。本类就是那道闸门：
 * 它不依赖人眼，也不依赖子代理自检。
 *
 * <p>用法：java -cp build/classes;build:test-classes PoolContractValidator [json路径...]
 */
public class PoolContractValidator {

    private static final Set<String> OK_ACTIONS = new HashSet<>(Arrays.asList(
        "DAMAGE","HEAL","DRAW","OPP_DRAW","DISCARD_OPP_RANDOM","DISCARD_DRAWN","DESTROY","BUFF",
        "ADD_ROOT","ADD_RAMPANT","GRANT_KEYWORD","SUMMON","SUMMON_LEADER","END_TURN",
        "ADD_OPP_PUNISH_TURN","ADD_SELF_PUNISH_TURN","CONVERT_PUNISH_TO_DISCARD","PROTECT_TURN",
        "NEGATE","NEGATE_ENEMY_EFFECTS_TURN","SKIP_RESHUFFLE","RESTORE_ATTACKS","GAIN_LIFE",
        "LOSE_LIFE","DAMAGE_CASTLE","WIN_GAME","ROLLBACK","COMMIT","PUSH","PULL"));

    private static final Set<String> OK_TARGETS = new HashSet<>(Arrays.asList(
        "ENEMY_TARGET","ENEMY_MINION","FRIENDLY_MINION","ALL_ENEMY_MINIONS","ALL_FRIENDLY_MINIONS",
        "ALL_MINIONS","ENEMY_FACE","SELF","ANY_MINION","ENEMY_SINGLE","SINGLE_ENEMY"));

    private static final List<String> COND_PREFIX = Arrays.asList(
        "ENEMY_MINIONS_GE_","SELF_MINIONS_GE_","HAND_GE_","OPP_HAND_GE_","OPP_HAND_LE_",
        "SELF_LIFE_LE_","SELF_SEALED_GE_","SELF_SEALED_HEALTH_GE_","SELF_ROOT_GE_",
        "SELF_RAMPANT_GE_","SELF_COMMIT_GE_","SELF_CLOUD_GE_","SELF_PULL_GE_",
        "OPP_DISCARD_GE_","SELF_AMBUSH_GE_","CASTLE_HP_LE_");

    private static final Set<String> COND_EXACT = new HashSet<>(Arrays.asList(
        "ALWAYS","SELF_LEADER_ON_FIELD","OPP_LEADER_ON_FIELD"));

    /** 需要 amount 的动作；缺了就一定是转换丢了数据。 */
    private static final Set<String> NEEDS_AMOUNT = new HashSet<>(Arrays.asList(
        "DAMAGE","HEAL","DRAW","OPP_DRAW","BUFF","ADD_ROOT","ADD_RAMPANT","GAIN_LIFE",
        "SUMMON","ADD_OPP_PUNISH_TURN","ADD_SELF_PUNISH_TURN","DISCARD_OPP_RANDOM","DISCARD_DRAWN"));

    private static final Set<String> FACTION_NAMES = new HashSet<>(Arrays.asList(
        "古木圣地","烈焰帝国","机械遗迹","深海联盟","无阵营"));

    private static final Set<String> RESERVED_IDS = new HashSet<>(Arrays.asList(
        "wood_leader","wood_sapling","wood_guard","wood_spring","wood_druid","wood_bear",
        "wood_thorn","wood_growth","wood_bark","wood_root","wood_veil","wood_punish_wrath",
        "wood_treant","wood_wisp","wood_moon","wood_warden","wood_seed","wood_stag",
        "wood_circle","wood_renew","wood_owl"));

    private static final List<String> errors = new ArrayList<>();
    private static final List<String> warnings = new ArrayList<>();

    public static void main(String[] args) throws Exception {
        List<Path> files = new ArrayList<>();
        if (args.length > 0) {
            for (String a : args) files.add(Path.of(a));
        } else {
            System.err.println("用法: PoolContractValidator <json路径...>");
            return;
        }

        Map<String, String> seenIds = new LinkedHashMap<>();
        Map<String, String> seenNames = new LinkedHashMap<>();
        int total = 0;

        for (Path f : files) {
            if (!Files.exists(f)) { errors.add("文件不存在: " + f); continue; }
            System.out.println("--- 校验 " + f + " ---");
            // 用引擎自己的加载器解析单个 JSON 文件，避免"我写的解析器"再成为错误源
            CardLibrary lib = CardLibrary.load(
                    f.getParent() == null ? Path.of(".") : f.getParent());
            java.util.List<CardDef> parsed = lib.byFile.get(f.getFileName().toString());
            if (parsed == null) { errors.add("加载器没有读到文件: " + f); continue; }
            CardDef[] cards = parsed.toArray(new CardDef[0]);
            int n = 0;
            for (CardDef c : cards) {
                n++; total++;
                checkCard(c, f.toString());
                if (c.id == null || c.id.isEmpty()) { errors.add("空 id in " + f); continue; }
                if (seenIds.containsKey(c.id)) {
                    errors.add("重复 id: " + c.id + " （同时出现在 " + seenIds.get(c.id) + " 与 " + f + "）");
                } else seenIds.put(c.id, f.toString());
                if (c.name != null && !c.name.isEmpty()) {
                    if (seenNames.containsKey(c.name)) errors.add("重复卡名: " + c.name + " (" + c.id + ")");
                    else seenNames.put(c.name, c.id);
                }
            }
            System.out.println("  解析 " + n + " 张");
        }

        System.out.println("\n=== 汇总 ===");
        System.out.println("总卡数 " + total);
        System.out.println("错误 " + errors.size() + " 项；警告 " + warnings.size() + " 项");
        if (!errors.isEmpty()) {
            System.out.println("\n[错误] 必须修复，拒绝入库：");
            for (String e : errors) System.out.println("  ✗ " + e);
        }
        if (!warnings.isEmpty()) {
            System.out.println("\n[警告] 需要人看：");
            for (String w : warnings) System.out.println("  ! " + w);
        }
        if (errors.isEmpty()) System.out.println("\nVERDICT: PASS —— 可以入库");
        else { System.out.println("\nVERDICT: REJECT —— " + errors.size() + " 项错误"); System.exit(1); }
    }

    private interface Fn { CardDef[] apply(CardDef[] c); }

    private static void checkCard(CardDef c, String src) {
        String tag = src + "#" + (c.id == null ? "?" : c.id);

        // 1. text 不得是阵营名（真实事故 2）
        if (c.text != null && FACTION_NAMES.contains(c.text.trim())) {
            errors.add(tag + ": text 是阵营名「" + c.text + "」而不是卡面文案（真实事故2）");
        }

        // 2. id 前缀与保留
        if (c.id != null && !c.id.startsWith("wood_")) errors.add(tag + ": id 未以 wood_ 开头");
        if (c.id != null && RESERVED_IDS.contains(c.id)) {
            errors.add(tag + ": id 与现有 21 张基线卡冲突");
        }

        // 3. 型别
        if (c.type == null) errors.add(tag + ": 缺 type");
        if (c.isMinion()) {
            if (c.attack < 0 || c.health <= 0) errors.add(tag + ": 随从身材异常 " + c.attack + "/" + c.health);
        }

        // 4. 逐效果校验
        checkEffects(c, c.onPlayEffects, tag, "onPlayEffects");
        checkEffects(c, c.chantEffects, tag, "chantEffects");
        checkEffects(c, c.ambushEffects, tag, "ambushEffects");
        checkEffects(c, c.punishEffects, tag, "punishEffects");
        checkEffects(c, c.commitEffects, tag, "commitEffects");
        checkEffects(c, c.pushEffects, tag, "pushEffects");
        checkEffects(c, c.pullEffects, tag, "pullEffects");

        // 5. 卡级条件
        if (c.punishCondition != null && !isLegalCondition(c.punishCondition)) {
            errors.add(tag + ": 非法 punishCondition 「" + c.punishCondition + "」");
        }

        // 6. 惩罚牌必须有可发动标记与代价（真实缺口 4）
        if ("PUNISH".equals(c.type) && !c.punishActivatable) {
            warnings.add(tag + ": PUNISH 卡但 punishActivatable=false，玩家无法发动");
        }

        // 7. 卡面必须写出它自己的条件（卡面与字段一致）
        String t = c.text == null ? "" : c.text;
        if (t.contains("若") && c.onPlayEffects != null) {
            boolean anyCond = false;
            for (com.dominionwars.model.CardDef.EffectSpec e : c.onPlayEffects)
                if (e.condition != null && !e.condition.isEmpty()) anyCond = true;
            if (!anyCond) {
                warnings.add(tag + ": 卡面写了「若…」但没有任何效果带 condition（可能条件只挂在卡级）");
            }
        }
        // 8. 一张卡不得「卡面承诺了东西却零效果结算」。
        //
        // 这是真实事故 5：设计稿用 `effects` 作为键，而引擎读的是 `onPlayEffects`，
        // 于是加载器把所有效果当未知字段丢掉，每张卡都变成空效果 —— 而当时校验器
        // 只报 PASS，因为「空效果」不触发任何既有规则。
        // 后果比前四个更严重：卡能入库、能加载、能在对局里被打出，但什么都不做。
        int totalEffects = size(c.onPlayEffects) + size(c.chantEffects) + size(c.ambushEffects)
                + size(c.punishEffects) + size(c.commitEffects) + size(c.pushEffects) + size(c.pullEffects);
        if (totalEffects == 0) {
            if (c.isMinion()) {
                // 纯身材随从合法，但必须在卡面上看不出有额外承诺
                if (t.contains("登场") || t.contains("若") || t.contains("：")) {
                    errors.add(tag + ": 卡面承诺了效果但零效果结算（疑似键名错误：引擎读 onPlayEffects，"
                            + "若数据用了 effects 则整个效果列表被丢弃）");
                }
            } else {
                errors.add(tag + ": " + c.type + " 卡零效果结算（真实事故5：效果键名不被引擎识别）");
            }
        }

        // 9. 关键词授予必须走 effects 或 keywords 之一，不能只在卡面上
        if (t.contains("嘲讽") && !hasKeywordOrGrant(c, "嘲讽")) {
            warnings.add(tag + ": 卡面写「嘲讽」但既无 keywords 也无 GRANT_KEYWORD ⇒ 该嘲讽不会生效");
        }
        if (t.contains("圣盾") && !hasKeywordOrGrant(c, "圣盾")) {
            warnings.add(tag + ": 卡面写「圣盾」但既无 keywords 也无 GRANT_KEYWORD ⇒ 该圣盾不会生效");
        }
        if (t.contains("突袭") && !hasKeywordOrGrant(c, "突袭")) {
            warnings.add(tag + ": 卡面写「突袭」但既无 keywords 也无 GRANT_KEYWORD ⇒ 该突袭不会生效");
        }
        if (t.contains("扰魔") && !hasKeywordOrGrant(c, "扰魔")) {
            warnings.add(tag + ": 卡面写「扰魔」但既无 keywords 也无 GRANT_KEYWORD ⇒ 该扰魔不会生效");
        }
    }

    private static int size(List<?> l) { return l == null ? 0 : l.size(); }

    /** 该关键词是否真能被引擎读到（printed keywords 字段，或一条 GRANT_KEYWORD 效果）。 */
    private static boolean hasKeywordOrGrant(CardDef c, String keyword) {
        if (c.keywords != null && c.keywords.contains(keyword)) return true;
        for (List<com.dominionwars.model.CardDef.EffectSpec> l : allEffectLists(c)) {
            if (l == null) continue;
            for (com.dominionwars.model.CardDef.EffectSpec e : l) {
                if ("GRANT_KEYWORD".equals(e.action) && keyword.equals(e.param)) return true;
            }
        }
        return false;
    }

    private static List<List<com.dominionwars.model.CardDef.EffectSpec>> allEffectLists(CardDef c) {
        List<List<com.dominionwars.model.CardDef.EffectSpec>> r = new ArrayList<>();
        r.add(c.onPlayEffects); r.add(c.chantEffects); r.add(c.ambushEffects);
        r.add(c.punishEffects); r.add(c.commitEffects); r.add(c.pushEffects); r.add(c.pullEffects);
        return r;
    }

    private static void checkEffects(CardDef c, List<com.dominionwars.model.CardDef.EffectSpec> fx,
                                     String tag, String hook) {
        if (fx == null) return;
        for (int i = 0; i < fx.size(); i++) {
            com.dominionwars.model.CardDef.EffectSpec e = fx.get(i);
            String at = tag + " " + hook + "[" + i + "]";
            if (e.action == null || !OK_ACTIONS.contains(e.action)) {
                errors.add(at + ": 非法 action 「" + e.action + "」");
            }
            // target: 允许缺省。引擎的 EffectSpec 默认就是 "NONE"（CardDef 里
            // `public String target = "NONE"`），所以"没有目标"在引擎内部表现为 NONE ——
            // 那是引擎自己的哨兵值，不是非法数据。校验器必须认它，否则会把 37 处正常效果
            // 误判为错误（我第一版就是这么误报的）。
            if (e.target != null && !e.target.isEmpty()
                    && !"NONE".equals(e.target) && !OK_TARGETS.contains(e.target)) {
                errors.add(at + ": 非法 target 「" + e.target + "」");
            }
            if (e.condition != null && !e.condition.isEmpty() && !isLegalCondition(e.condition)) {
                errors.add(at + ": 非法 condition 「" + e.condition + "」");
            }
            if (e.action != null && NEEDS_AMOUNT.contains(e.action)
                    && e.amount == 0) {
                errors.add(at + ": action " + e.action + " 需要 amount 但为 0（真实事故1：转换丢了数据）");
            }
            if (e.param != null) {
                if (e.param.contains("=")) {
                    errors.add(at + ": param 含 '=' 「" + e.param + "」（真实事故3：应为纯值如「嘲讽」）");
                }
                if (e.param.startsWith("-") || e.param.startsWith("—")) {
                    errors.add(at + ": param 是破折号占位「" + e.param + "」（真实事故1：应为 amount）");
                }
            }
        }
    }

    private static boolean isLegalCondition(String cond) {
        if (cond == null || cond.isEmpty()) return true;
        if (COND_EXACT.contains(cond)) return true;
        for (String p : COND_PREFIX) {
            if (cond.startsWith(p)) {
                String num = cond.substring(p.length());
                try { return Integer.parseInt(num) >= 0; } catch (NumberFormatException e) { return false; }
            }
        }
        return false;
    }
}
