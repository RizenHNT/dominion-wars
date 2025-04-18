package com.rizen.dominionwars.cards;

import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

public abstract class PunishmentCard extends Card {
    private boolean validInHand; // 手札で条件を満たしているかどうか

    // ✅ コンストラクタ（Cardを継承するために必要）
    public PunishmentCard(String cardID, String name, String description, int punishmentValue, int cost,
                          Rarity rarity, Faction faction, boolean validInHand) {
        super(cardID, name, description, punishmentValue, rarity, faction, CardType.PUNISHMENT, null);
        this.validInHand = validInHand;
    }

    @Override
    public boolean use(GameContext context, Player owner, Player opponent) {
        // ✅ ペナルティカードは条件を満たしている場合のみ使用可能
        if (!validInHand) {
            return false;
        }
        return executePunishmentEffect(context, owner, opponent);
    }

    // ✅ ペナルティ効果を実行（サブクラスで具体的に実装可能）
    protected boolean executePunishmentEffect(GameContext context, Player owner, Player opponent) {
        System.out.println(getName() + " のペナルティ効果が発動！");
        return true;
    }

    @Override
    public abstract Card clone();  
}
