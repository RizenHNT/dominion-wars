package com.rizen.dominionwars.specific;

import java.util.Map;

import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.cards.*;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

public class TakLeader extends LeaderCard {
    public TakLeader() {
        super("0000", "領主", "", 3, Rarity.LEGENDARY, Faction.NEUTRAL);
        addKeyword(CardKeyword.LEADER);
    }

     @Override
    protected boolean executeLeaderEffect(GameContext context, Player owner, Player opponent) {
        System.out.println("👑 " + getName() + " のリーダー効果が発動！");
        return true;
    }
    @Override
    public Card clone() {
        TakLeader cloned = new TakLeader();
        cloned.setDescription(this.getDescription());
        cloned.setPunishmentValue(this.getPunishmentValue());

        for (Map.Entry<CardKeyword, Integer> effect : this.getKeywordEffects().entrySet()) {
            if (effect.getValue() == null) {
                cloned.addKeyword(effect.getKey());
            } else {
                cloned.addKeyword(effect.getKey(), effect.getValue());
            }
        }
        return cloned;
    }
}
