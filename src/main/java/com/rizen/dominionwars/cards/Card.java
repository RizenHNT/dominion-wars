package com.rizen.dominionwars.cards;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

public abstract class Card implements Cloneable {
    private final String cardID;
    private final String name;
    private String description;
    private final Rarity rarity;
    private final Faction faction;
    private final CardType type;
    private final Map<CardKeyword, Integer> keywordEffects; // キーワード効果を格納するマップ
    private int punishmentValue; // ペナルティ値（変更可能なのでfinalは付けない）

    public String getCardID() {
        return cardID;
    }

    public String getName() {
        return name;
    }

    public Rarity getRarity() {
        return rarity;
    }

    public Faction getFaction() {
        return faction;
    }

    public CardType getType() {
        return type;
    }

    public Map<CardKeyword, Integer> getKeywordEffects() {
        return keywordEffects;
    }

    public String getDescription() {
        return description;
    }

    public void setDescription(String description) {
        this.description = description;
    }

    public int getPunishmentValue() {
        return punishmentValue;
    }

    public void setPunishmentValue(int punishmentValue) {
        this.punishmentValue = punishmentValue;
    }

    // ✅ コンストラクタ
    public Card(String cardID, String name, String description, int punishmentValue, 
            Rarity rarity, Faction faction, CardType type, List<CardKeyword> keywords) {
        this.cardID = cardID;
        this.name = name;
        this.description = description;
        this.punishmentValue = punishmentValue;
        this.rarity = rarity;
        this.faction = faction;
        this.type = type;
        this.keywordEffects = new HashMap<>();
    }

    // コピーコンストラクタ
    protected Card(Card other) {
        this.cardID = other.cardID;
        this.name = other.name;
        this.description = other.description;
        this.rarity = other.rarity;
        this.faction = other.faction;
        this.type = other.type;
        this.punishmentValue = other.punishmentValue;
        this.keywordEffects = new HashMap<>(other.keywordEffects);
    }

    // 抽象メソッド（サブクラスで実装）
    public abstract boolean use(GameContext context, Player owner, Player opponent);

    @Override
    public abstract Card clone(); // cloneメソッドをオーバーライド

    // ペナルティ効果（必要に応じてサブクラスでオーバーライド）
    public boolean triggerPunishmentEffect(GameContext context, Player owner, Player opponent) {
        System.out.println(getName() + " にペナルティ効果が定義されていません！");
        return false;
    }

    // キーワードを追加（値なし）
    public void addKeyword(CardKeyword keyword) {
        keywordEffects.put(keyword, null);
        updateDescription();
    }

    // キーワードを追加（値あり）
    public void addKeyword(CardKeyword keyword, int value) {
        keywordEffects.put(keyword, value);
        updateDescription();
    }

    // キーワードリストを取得
    public List<CardKeyword> getKeywords() {
        return new ArrayList<>(keywordEffects.keySet());
    }

    // キーワードの値を取得
    public Integer getKeywordValue(CardKeyword keyword) {
        return keywordEffects.get(keyword);
    }

    // 説明文を更新
    private void updateDescription() {
        StringBuilder desc = new StringBuilder();
        for (Map.Entry<CardKeyword, Integer> effect : keywordEffects.entrySet()) {
            if (desc.length() > 0) {
                desc.append("、");
            }
            if (effect.getValue() == null) {
                desc.append(effect.getKey().getDescription());
            } else {
                desc.append(String.format("%s：%d", 
                    effect.getKey().getDescription(), 
                    effect.getValue()));
            }
        }
        this.description = desc.toString();
    }
}
