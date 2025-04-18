package com.rizen.dominionwars.specific;

import java.util.Map;
import java.util.Scanner;

import com.rizen.dominionwars.cards.*;
import com.rizen.dominionwars.player.Player;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.factions.Faction;

// フレイムインパクト（炎の衝撃）カードクラス
public class FlameImpact extends SpellCard {

    public FlameImpact() {
        super("F0001", "フレイムインパクト", "", 2, 
              Rarity.RARE, Faction.FLAME_EMPIRE, 0);  // コストは0
        addKeyword(CardKeyword.DESTROY, 5);
    }

    @Override
    public boolean executeSpellEffect(GameContext context, Player owner, Player opponent) {
        opponent.decreaseLife(5);
        System.out.println("🔥 " + owner.getName() + " は " + getName() + " を使い、相手に5ダメージを与えた！");
        return true;
    }

    @Override
    public Card clone() {
        FlameImpact cloned = new FlameImpact();
        cloned.setDescription(this.getDescription());
        cloned.setPunishmentValue(this.getPunishmentValue());
        
        // キーワード効果をコピー
        for (Map.Entry<CardKeyword, Integer> effect : this.getKeywordEffects().entrySet()) {
            if (effect.getValue() == null) {
                cloned.addKeyword(effect.getKey());
            } else {
                cloned.addKeyword(effect.getKey(), effect.getValue());
            }
        }
        return cloned;
    }

    @Override
    public boolean triggerPunishmentEffect(GameContext context, Player owner, Player opponent) {
        System.out.println("💥 " + owner.getName() + " は " + getName() + " の反動でカードを2枚捨てた！");
        owner.getHand().discardCards(2, context.getScanner());
        return true;
    }
}
