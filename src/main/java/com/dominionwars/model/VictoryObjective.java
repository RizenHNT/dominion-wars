package com.dominionwars.model;

import com.dominionwars.util.Json;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.BiFunction;

/**
 * 胜利目标的**声明**与其**读数登记表** —— 与 C# 的 {@code VictoryObjectiveDefinition} /
 * {@code VictoryConditions} 对齐的统一接口。
 *
 * <p>设计目标（owner 定稿）：**新增一个胜利条件 = 写一个进度计算，AI 与动作算法不必改动。**
 * 因此胜利条件不由阵营或敌人类型硬编码，而是<b>由数据声明</b>：
 *
 * <pre>
 *   "victory": { "metric": "PULL_COUNT", "direction": "INCREASE", "target": 6 }
 * </pre>
 *
 * <p>{@code metric} 命名一个登记在 {@link #register} 里的读数函数。<b>未知 metric 在加载期就抛错</b>，
 * 而不是被静默忽略 —— "声明了但没实现"的胜利条件必须立刻可见，否则它在对局里只会表现为
 * "这个统领永远赢不了"，而这种缺陷极难从对局本身看出来。
 *
 * <p>读数函数返回 {@code Integer}，{@code null} 表示**本局无法测量**（前置状态不存在）。
 * {@code null} 与 {@code 0} 是两件不同的事，调用方必须区分：不可测量时不得判定达成。
 *
 * <p>老字段 {@code winCondition}/{@code winParam} 仍可读，作为尚未迁移卡牌的兼容回退；
 * 两者同时存在时以 {@code victory} 为准（与 C# 的混合生产者同序）。
 */
public final class VictoryObjective {

    /** 方向：INCREASE —— 当前值达到或超过目标即达成。 */
    public static final String INCREASE = "INCREASE";
    /** 方向：DECREASE —— 当前值降到目标或以下即达成。 */
    public static final String DECREASE = "DECREASE";

    // ── 读数登记表 ──────────────────────────────────────────────────────
    // 键 = metric id，值 = (Game, seat) -> 读数。Game 以 Object 传入以避免
    // model 包反向依赖 engine 包；登记处做一次受检转换。
    private static final Map<String, BiFunction<Object, Integer, Integer>> READERS = new LinkedHashMap<>();

    /** 老 {@code winCondition} 字符串 → 新 metric id。迁移完成后可删。 */
    private static final Map<String, String> LEGACY = new LinkedHashMap<>();

    /**
     * 登记一个读数函数。**这是新增胜利条件的唯一入口。**
     *
     * @param metric 数据里 `victory.metric` 使用的 id
     * @param reader (Game, seat) -> 当前度量值；返回 null 表示本局无法测量
     */
    public static synchronized void register(String metric, BiFunction<Object, Integer, Integer> reader) {
        if (metric == null || metric.isEmpty()) throw new IllegalArgumentException("metric id 不能为空");
        if (reader == null) throw new IllegalArgumentException("读数函数不能为空");
        READERS.put(metric, reader);
    }

    /**
     * 保证胜利条件读数已登记。
     *
     * <p><b>为什么要有这个（2026-09-13 修复的阻断级缺陷）：</b>登记原先只在
     * {@code Game.start()} 里通过 {@code VictoryConditionRegistry.install()} 发生，
     * 但<b>卡库是在 Game 构造之前加载的</b>（{@code CardLibrary.load → CardDef.fromMap →
     * VictoryObjective.of}）。于是任何声明了 {@code leaderDef.victory} 的统领在解析时
     * 都会抛「未登记的胜利条件读数」，<b>整个阵营文件被静默跳过</b> ——
     * 现场症状是 {@code CardLibrary.byId} 只剩 neutral 的 6 张，
     * 四个阵营的 84 张卡一张都不存在，而格式化输出只留一行 mojibake 警告。
     *
     * <p>现在把登记做成"取用前前置条件"：任何读数/解析入口先调用本方法。
     * 幂等且线程安全。
     */
    public static void ensureInstalled() {
        com.dominionwars.engine.VictoryConditionRegistry.install();
    }

    /** 登记一个老名字到新 metric 的兼容映射。 */
    public static synchronized void registerLegacy(String legacyCondition, String metric) {
        LEGACY.put(legacyCondition, metric);
    }

    public static synchronized List<String> known() {
        return List.copyOf(READERS.keySet());
    }

    public static synchronized boolean isKnown(String metric) {
        return metric != null && READERS.containsKey(metric);
    }

    /** 老字符串对应的 metric id；无对应时返回 {@code null}。 */
    public static synchronized String metricForLegacyCondition(String legacy) {
        return legacy == null ? null : LEGACY.get(legacy);
    }

    /** 读取某 metric 在当前对局中的值；{@code null} 表示无法测量。 */
    public static synchronized Integer read(String metric, Object game, int seat) {
        BiFunction<Object, Integer, Integer> reader = READERS.get(metric);
        if (reader == null) {
            throw new IllegalArgumentException(
                    "未登记的胜利条件读数 '" + metric + "'；已知：" + known());
        }
        return reader.apply(game, seat);
    }

    /** 供编辑器/调试列出全部已登记 id。 */
    public static synchronized List<String> knownSorted() {
        List<String> ids = new ArrayList<>(READERS.keySet());
        ids.sort(String::compareTo);
        return ids;
    }

    // ── 实例 ────────────────────────────────────────────────────────────
    public final String metric;
    public final String direction;
    public final int target;

    private VictoryObjective(String metric, String direction, int target) {
        if (!isKnown(metric)) {
            throw new IllegalArgumentException(
                    "未知的胜利条件 '" + metric + "'。请在 VictoryObjective.register 中登记一个读数函数，"
                            + "并在 data/cards 的 leaderDef.victory.metric 里使用它。已知 id："
                            + String.join(", ", known()));
        }
        if (!INCREASE.equals(direction) && !DECREASE.equals(direction)) {
            throw new IllegalArgumentException(
                    "胜利目标方向必须是 " + INCREASE + " 或 " + DECREASE + "，收到 '" + direction + "'");
        }

        this.metric = metric;
        this.direction = direction;
        this.target = target;
    }

    /**
     * 从 {@code leaderDef} 映射读出胜利目标；未声明则返回 {@code null}。
     *
     * <p>这是**加载期**入口：{@code victory} NOT 声明时回退到老字段
     * {@code winCondition}/{@code winParam}，并在那时校验 metric 是否已登记 ——
     * 未知 metric 在加载期就抛错，而不是等到某局对局里表现为"这个统领永远赢不了"。
     */
    public static VictoryObjective fromLeaderDef(Map<String, Object> leaderDef) {
        if (leaderDef == null) return null;

        // 加载期入口必须先保证读数已登记，否则声明了 victory 的统领会在解析时抛错，
        // 导致整个阵营卡牌文件被跳过（见 ensureInstalled 的说明）。
        ensureInstalled();

        Map<String, Object> v = Json.map(leaderDef, "victory");
        if (v != null) {
            String metric = Json.str(v, "metric", "");
            String direction = Json.str(v, "direction", INCREASE).toUpperCase();
            int target = Json.integer(v, "target", 0);
            if (!metric.isEmpty()) {
                return new VictoryObjective(metric, direction, target);
            }
        }

        return resolveLegacy(
                Json.str(leaderDef, "winCondition", "NONE"),
                Json.integer(leaderDef, "winParam", 0));
    }

    /**
     * 按 {@code LeaderDef} 对象解析胜利目标 —— <b>使用期入口</b>。
     *
     * <p>为什么两个入口都需要：卡牌从 JSON 加载时 {@code victory} 会被预先解析并缓存，
     * 但测试与编辑器会**手工构造** {@code LeaderDef} 并只写老字段。若只在加载期回退，
     * 那种对象就永远没有读数，表现为"这个统领赢不了"，而根因完全不可见。
     * 因此使用期必须能自己回退解析。
     */
    public static VictoryObjective of(com.dominionwars.model.CardDef.LeaderDef leaderDef) {
        if (leaderDef == null) return null;
        ensureInstalled();   // 使用期入口同样必须先登记（手工构造的 LeaderDef 会走这条）
        if (leaderDef.victory != null) return leaderDef.victory;
        VictoryObjective resolved = resolveLegacy(leaderDef.winCondition, leaderDef.winParam);
        if (resolved != null) leaderDef.victory = resolved;   // 缓存，避免每回合重建
        return resolved;
    }

    private static VictoryObjective resolveLegacy(String legacyCondition, int winParam) {
        if (legacyCondition == null || legacyCondition.isEmpty() || "NONE".equals(legacyCondition)) {
            return null;
        }

        String mapped = metricForLegacyCondition(legacyCondition);
        if (mapped == null) return null;

        return new VictoryObjective(mapped, INCREASE, winParam);
    }

    public Map<String, Object> toMap() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("metric", metric);
        m.put("direction", direction);
        m.put("target", (long) target);
        return m;
    }

    /** 当前度量值；{@code null} 表示本局无法测量。 */
    public Integer read(Object game, int seat) {
        return read(metric, game, seat);
    }

    /** 距离达成还差多少（已达成时为 0）。 */
    public int remaining(int current) {
        int delta = DECREASE.equals(direction) ? current - target : target - current;
        return Math.max(0, delta);
    }

    public boolean met(int current) {
        return DECREASE.equals(direction) ? current <= target : current >= target;
    }

    /** 可直接用于日志/回放的可读形式。 */
    public String describe() {
        return metric + " " + direction + " " + target;
    }

    @Override
    public String toString() {
        return describe();
    }
}
