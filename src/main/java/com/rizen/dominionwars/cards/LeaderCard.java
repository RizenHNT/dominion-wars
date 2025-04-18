package com.rizen.dominionwars.cards;

import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

import java.util.ArrayList;
import java.util.List;

public abstract class LeaderCard extends Card {
    private final CardType leaderType; // リーダーカードの実際のタイプ
    private boolean isUsed; // ✅ 使用されたかどうかを記録（特定の効果が複数回発動するのを防ぐ）

    // ✅ コンストラクタを修正し、直接 `CardType.LEADER` を設定
    public LeaderCard(String cardID, String name, String description, int punishmentValue, Rarity rarity, Faction faction) {
        super(cardID, name, description, punishmentValue, rarity, faction, CardType.LEADER, new ArrayList<>());
        this.leaderType = CardType.LEADER; // ✅ ここで値を設定
        this.isUsed = false; // ✅ 初期状態は未使用
    }

    // 抽象メソッドの宣言
    @Override
    public abstract Card clone();

    // オプション：リーダー固有の効果を追加する抽象メソッド
    protected abstract boolean executeLeaderEffect(GameContext context, Player owner, Player opponent);

    @Override
    public boolean use(GameContext context, Player owner, Player opponent) {
        // ✅ リーダーカードは再利用可能
        if (!isUsed) {
            System.out.println(getName() + " がリーダーとして初登場！");
            isUsed = true; // ✅ 使用済みとして記録
        } else {
            System.out.println(getName() + " が再登場！");
        }
        owner.setLeader(this);
        return true;
    }

    // ✅ リーダーカードのタイプを取得
    public CardType getLeaderType() {
        return leaderType;
    }

    // ✅ 使用済みかどうかを取得
    public boolean hasBeenUsed() {
        return isUsed;
    }

    // ✅ 未使用状態にリセット（ゲームルールで特定のカードがリセットを許可する場合）
    public void resetUsage() {
        isUsed = false;
    }

    // ✅ 追加の検証メソッド：デッキに複数のリーダーカードが含まれないことを確認
    public static boolean validateDeck(List<Card> deck) {
        int leaderCount = 0;
        for (Card card : deck) {
            if (card instanceof LeaderCard) {
                leaderCount++;
                if (leaderCount > 1) {
                    return false; // ❌ デッキ構築が不正
                }
            }
        }
        return true; // ✅ デッキ構築が正しい
    }
}
