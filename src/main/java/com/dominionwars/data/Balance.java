package com.dominionwars.data;

import com.dominionwars.util.Json;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.Map;

/** 平衡参数表（data/balance.json，全部可调） */
public class Balance {
    public int openingHand = 5;            // 起手手牌
    public int drawPerTurn = 1;            // 每回合开始抽牌数
    public int secondPlayerBonusDraw = 1;  // 后手首回合额外抽牌
    public int handLimit = 8;              // 弃牌阶段手牌保留上限
    public int reshuffleLoseAt = 10;       // 胜利计数阈值：对手每次有效牌库循环时 +1
    public boolean reshuffleIncludesHand = false; // 洗牌阶段是否将手牌一并洗回（默认仅墓地）
    public int chainLimit = 20;            // 惩罚连锁硬上限
    public int deckMin = 60, deckMax = 80; // 卡组张数
    // 先驱威压
    public int pioneerOpponentPunishBonus = 1; // 仅一方统领在场时，对方卡牌惩罚值+N
    public int pioneerSelfPunishDiscount = 0;  // 备选思路：统领方自己卡牌惩罚值-N
    public int pioneerHandLimitBonus = 2;      // 统领方弃牌上限+N

    // 共享王城：一个全局公共目标。所有“打敌方统领/玩家”的伤害在王城存在时优先打王城。
    public boolean royalCastleEnabled = false;
    public int royalCastleMaxHp = 60;
    public int royalCastleBreakVictoryCount = 9;   // 破城者自己的胜利计数至少设为该值

    public Map<String, Object> custom = new LinkedHashMap<>();

    public static Balance load(Path file) {
        Balance b = new Balance();
        try {
            if (Files.exists(file)) b.apply(Json.parseObject(Files.readString(file)));
        } catch (Exception e) {
            System.err.println("balance.json 读取失败，使用默认值: " + e.getMessage());
        }
        return b;
    }

    public void apply(Map<String, Object> m) {
        openingHand = Json.integer(m, "openingHand", openingHand);
        drawPerTurn = Json.integer(m, "drawPerTurn", drawPerTurn);
        secondPlayerBonusDraw = Json.integer(m, "secondPlayerBonusDraw", secondPlayerBonusDraw);
        handLimit = Json.integer(m, "handLimit", handLimit);
        reshuffleLoseAt = Json.integer(m, "reshuffleLoseAt", reshuffleLoseAt);
        reshuffleIncludesHand = Json.bool(m, "reshuffleIncludesHand", reshuffleIncludesHand);
        chainLimit = Json.integer(m, "chainLimit", chainLimit);
        deckMin = Json.integer(m, "deckMin", deckMin);
        deckMax = Json.integer(m, "deckMax", deckMax);
        pioneerOpponentPunishBonus = Json.integer(m, "pioneerOpponentPunishBonus", pioneerOpponentPunishBonus);
        pioneerSelfPunishDiscount = Json.integer(m, "pioneerSelfPunishDiscount", pioneerSelfPunishDiscount);
        pioneerHandLimitBonus = Json.integer(m, "pioneerHandLimitBonus", pioneerHandLimitBonus);
        royalCastleEnabled = Json.bool(m, "royalCastleEnabled", royalCastleEnabled);
        royalCastleMaxHp = Json.integer(m, "royalCastleMaxHp", royalCastleMaxHp);
        royalCastleBreakVictoryCount = Json.integer(m, "royalCastleBreakVictoryCount",
                Json.integer(m, "royalCastleBreakReshuffleCount", royalCastleBreakVictoryCount));
        custom = m;
    }

    public Map<String, Object> toMap() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("openingHand", (long) openingHand);
        m.put("drawPerTurn", (long) drawPerTurn);
        m.put("secondPlayerBonusDraw", (long) secondPlayerBonusDraw);
        m.put("handLimit", (long) handLimit);
        m.put("reshuffleLoseAt", (long) reshuffleLoseAt);
        m.put("reshuffleIncludesHand", reshuffleIncludesHand);
        m.put("chainLimit", (long) chainLimit);
        m.put("deckMin", (long) deckMin);
        m.put("deckMax", (long) deckMax);
        m.put("pioneerOpponentPunishBonus", (long) pioneerOpponentPunishBonus);
        m.put("pioneerSelfPunishDiscount", (long) pioneerSelfPunishDiscount);
        m.put("pioneerHandLimitBonus", (long) pioneerHandLimitBonus);
        m.put("royalCastleEnabled", royalCastleEnabled);
        m.put("royalCastleMaxHp", (long) royalCastleMaxHp);
        m.put("royalCastleBreakVictoryCount", (long) royalCastleBreakVictoryCount);
        return m;
    }

    public void save(Path file) throws IOException {
        Files.createDirectories(file.getParent());
        Files.writeString(file, Json.write(toMap(), true));
    }
}
