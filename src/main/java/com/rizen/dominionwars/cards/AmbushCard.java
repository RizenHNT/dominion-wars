package com.rizen.dominionwars.cards;

import java.util.ArrayList;

import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

// アンブッシュカード（伏せカード）基底クラス
public abstract class AmbushCard extends Card {
    private String triggerCondition; // ✅ 一旦Stringでトリガー条件を管理

    // ✅ コンストラクタ（Cardを継承するために必要）
    public AmbushCard(String cardID, String name, String description, int punishmentValue, int cost,
                      Rarity rarity, Faction faction, String triggerCondition) {
        super(cardID, name, description, punishmentValue, rarity, faction, CardType.AMBUSH, new ArrayList<>());
        this.triggerCondition = triggerCondition;
    }

    @Override
    public boolean use(GameContext context, Player owner, Player opponent) {
        // ✅ 伏せカードは1ターンに1枚のみ設置可能
        if (owner.hasSetAmbushThisTurn()) {
            return false;
        }
        owner.setAmbush(this);
        return true;
    }

    // ✅ 伏せ効果を発動
    public boolean triggerAmbush(GameContext context, Player owner, Player opponent) {
        System.out.println(getName() + " の伏せ効果が発動！トリガー条件：" + triggerCondition);
        return executeAmbushEffect(context, owner, opponent);
    }

    // ✅ 具体的な伏せ効果（サブクラスで実装）
    protected boolean executeAmbushEffect(GameContext context, Player owner, Player opponent) {
        // ここで様々な伏せ効果を実装可能：
        // - 相手のミニオンを破壊
        // - 相手の手札を捨てさせる
        // - 相手カードの効果を無効化
        return true;
    }

    @Override
    public abstract Card clone();
}
