package com.rizen.dominionwars.player;

import java.util.List;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Optional;
import com.rizen.dominionwars.cards.Card;

public class Deck {
    private List<Card> cards;

    // ✅ コンストラクタ：空のデッキを初期化
    public Deck() {
        this.cards = new ArrayList<>();
    }

    // ✅ カードをドロー（NullPointerException を回避）
    public Optional<Card> drawCard() {
        if (cards.isEmpty()) {
            return Optional.empty();
        }
        return Optional.of(cards.remove(0));
    }

    // ✅ カードを追加
    public void addCard(Card card) {
        if (card != null) {
            cards.add(card);
        }
    }

    // ✅ デッキをシャッフル
    public void shuffle() {
        Collections.shuffle(cards);
    }

    // ✅ デッキが空かどうかを判断
    public boolean isEmpty() {
        return cards.isEmpty();
    }

    // ✅ 複数のカードを追加
    public void addCards(List<Card> cardsToAdd) {
        if (cardsToAdd != null) {
            cards.addAll(cardsToAdd);
        }
    }
    
    // ✅ デッキのサイズを取得
    public int size() {
        return cards.size();
    }

    public List<Card> getCards() {
        return Collections.unmodifiableList(cards); // ✅ 外部からの変更を防止
    }

    public void showDeck() {
        if (cards.isEmpty()) {
            System.out.println("⚠️ デッキが空です！");
            return;
        }
    
        System.out.println("🃏 現在のデッキ：");
        for (Card card : cards) {
            System.out.println("  🔹 [" + card.getCardID() + "] " + card.getName());
        }
    }
}
