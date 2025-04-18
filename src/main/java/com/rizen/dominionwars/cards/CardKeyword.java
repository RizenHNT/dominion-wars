// CardKeyword.java
package com.rizen.dominionwars.cards;

public enum CardKeyword {
    LEADER("領主"),
    DESTROY("破壊"),
    DISCARD("捨てる"),
    HEAL("回復"),
    ENHANCE("強化");

    private final String description;

    CardKeyword(String description) {
        this.description = description;
    }

    public String getDescription() {
        return description;
    }

    // 数値付きの完全な説明を取得
    public String getDescriptionWithValue(Integer value) {
        if (value == null) {
            return description;
        }
        return String.format("%s：%d", description, value);
    }
}
