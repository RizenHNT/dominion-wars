package com.rizen.dominionwars.specific;

import com.rizen.dominionwars.cards.MinionCard;
import com.rizen.dominionwars.cards.Rarity;
import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

public class AbyssalDevour extends MinionCard {
    public AbyssalDevour() {
        super("D0001", "Abyssal Devour", "敵のミニオンを喰らい、その攻撃力と体力を得る", 6,
              Rarity.LEGENDARY, Faction.DEEP_SEA_ALLIANCE, 3, 5);
    }

    // ✅ 喰らう効果
    public boolean devour(GameContext context, Player owner, Player opponent, MinionCard target) {
        if (target == null || !target.isOnField()) {
            return false;
        }
        System.out.println("🕳 " + getName() + " が " + target.getName() + " を喰らった！");
        this.boostStats(target.getAttack(), target.getHealth());
        target.die();
        return true;
    }

    private void boostStats(int attackBoost, int healthBoost) {
        this.setAttack(this.getAttack() + attackBoost);
        this.setHealth(this.getHealth() + healthBoost);
    }

    @Override
    public MinionCard clone() {
        return new AbyssalDevour();
    }
}
