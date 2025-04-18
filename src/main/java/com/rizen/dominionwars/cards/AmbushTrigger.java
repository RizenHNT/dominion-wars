package com.rizen.dominionwars.cards;

// dominionwars/cards/ディレクトリに配置
public enum AmbushTrigger {
    DRAW_CARD,    // カードをドローした時
    PLAY_CARD,    // カードをプレイした時
    ATTACK,       // 攻撃時
    END_TURN      // ターン終了時
    // 必要に応じてさらに追加可能
}