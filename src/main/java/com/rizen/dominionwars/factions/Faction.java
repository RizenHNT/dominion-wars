package com.rizen.dominionwars.factions;

public enum Faction {
    FLAME_EMPIRE,       // 炎の帝国
    DEEP_SEA_ALLIANCE,  // 深海同盟
    ANCIENT_WOOD,       // 古木の聖域
    MECHANICAL_RELIC,   // 機械遺跡
    ICE_REALM,          // 氷の領域
    NEUTRAL;            // 中立陣営

    // ✅ `toString()` メソッドを追加して、ログや UI での利用を簡単にする
    @Override
    public String toString() {
        return name().replace("_", " ").toLowerCase();
    }
}
