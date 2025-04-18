package com.rizen.dominionwars.cards;

import java.util.ArrayList;

import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

public abstract class SpellCard extends Card {
    private int chantTurns; // 吟唱ターン数
    private int remainingChantTurns; // 残りの吟唱ターン

    // ✅ コンストラクタ（Cardを継承するために必要）
    public SpellCard(String cardID, String name, String description, int punishmentValue, Rarity rarity, Faction faction, int chantTurns) {
        super(cardID, name, description, punishmentValue, rarity, faction, CardType.SPELL, new ArrayList<CardKeyword>());
        this.chantTurns = chantTurns;
        this.remainingChantTurns = chantTurns; // ✅ 初期化
    }

    // ✅ 吟唱中の呪文かどうかを判定
    public boolean isChanting() {
        return chantTurns > 0;
    }

    @Override
    public boolean use(GameContext context, Player owner, Player opponent) {
        // ✅ 吟唱ターンがある場合のみフィールドに追加
        if (remainingChantTurns > 0) {
            owner.getField().addChantingSpell(this);
            return true;
        } else {
            return executeSpellEffect(context, owner, opponent);
        }
    }

    // ✅ 吟唱ターン数を減少させる
    public void reduceChantTurns() {
        if (remainingChantTurns > 0) {
            remainingChantTurns--;
        }
    }

    // ✅ `Field` がこのメソッドを呼び出す（`reduceChantTurns()`ではなく）
    public boolean isChantComplete() {
        return remainingChantTurns == 0;
    }

    // ✅ 呪文を発動
    public boolean executeSpellEffect(GameContext context, Player owner, Player opponent) {
        System.out.println(getName() + " を発動した！");
        return true;
    }

    public int getChantTurns() {
        return chantTurns;
    }
    
    // ✅ 残りの吟唱ターンを取得
    public int getRemainingChantTurns() {
        return remainingChantTurns;
    }

    @Override
    public abstract Card clone();  
}
