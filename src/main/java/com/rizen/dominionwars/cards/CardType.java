package com.rizen.dominionwars.cards;

public enum CardType {
    MINION("ミニオンカード"), 
    SPELL("呪文カード"), 
    AMBUSH("伏せカード"), 
    PUNISHMENT("ペナルティカード"), 
    LEADER("リーダーカード");

    private final String description;

    CardType(String description) {
        this.description = description;
    }

    public String getDescription() {
        return description;
    }

    public static CardType fromString(String type) {
        for (CardType cardType : CardType.values()) {
            if (cardType.name().equalsIgnoreCase(type)) {
                return cardType;
            }
        }
        throw new IllegalArgumentException("未知のカードタイプ: " + type);
    }

    @Override
    public String toString() {
        return name() + " (" + description + ")";
    }
}


