package com.rizen.dominionwars.game;

import java.util.*;
import com.rizen.dominionwars.cards.*;
import com.rizen.dominionwars.player.Deck;
import com.rizen.dominionwars.specific.AbyssalDevour;
import com.rizen.dominionwars.specific.FlameImpact;

public class CardDatabase {
    private final Map<String, Card> cardsByID = new HashMap<>();
    private final Map<String, Card> cardsByName = new HashMap<>();

    // ✅ カードデータベースを初期化し、すべてのカードを登録
    public void initialize() {
        registerCard(new FlameImpact());
        registerCard(new AbyssalDevour());

        // サンプルカード
        // registerCard(new MinionCard("MIN001", "烈焰士兵", "...",  2, Rarity.COMMON, Faction.FLAME_EMPIRE, 2, 2));
        // registerCard(new SpellCard("SPL001", "深渊吞噬", "...",  3, Rarity.RARE, Faction.DEEP_SEA_ALLIANCE, 1));

        // 今後さらにカードを追加可能...
    }

    // ✅ カードを登録（IDが重複しないようにする）
    private void registerCard(Card card) {
        if (cardsByID.containsKey(card.getCardID())) {
            System.out.println("⚠️ 警告：カード ID `" + card.getCardID() + "` は既に存在します。登録できません！");
            return;
        }
        cardsByID.put(card.getCardID(), card);
        cardsByName.put(card.getName(), card);
    }

    // ✅ ID または Name でカードを検索（`Optional` を使用）
    public Optional<Card> findCardByID(String cardID) {
        return Optional.ofNullable(cardsByID.get(cardID));
    }

    public Optional<Card> findCardByName(String cardName) {
        return Optional.ofNullable(cardsByName.get(cardName));
    }

    // ✅ ID でカードを取得（エラーハンドリング付き）
    public Card getCardByID(String cardID) {
        Card card = cardsByID.get(cardID);
        if (card == null) {
            System.out.println("⚠️ 警告：カード ID が見つかりません：" + cardID);
        }
        return card; // ✅ null を返すことを許可するが、クラッシュはしない
    }

    public Card getCardByName(String name) {
        Card card = cardsByName.get(name);
        if (card == null) {
            System.out.println("⚠️ 警告：カード `" + name + "` が見つかりません！");
        }
        return card; // null を返すことを許可し、呼び出し側がチェックする
    }

    // ✅ デフォルトデッキを作成（IDを使用、多言語問題を回避）
    public Deck createDefaultDeck() {
        Deck deck = new Deck();
        for (int i = 0; i < 10; i++) {
            findCardByID("MIN001").ifPresent(card -> deck.addCard(card.clone()));
            findCardByID("SPL001").ifPresent(card -> deck.addCard(card.clone()));
        }
        deck.shuffle();
        return deck;
    }

    // ✅ ユーザー定義のデッキをサポート
    public Deck createCustomDeck(List<String> cardIDs) {
        Deck deck = new Deck();
        for (String id : cardIDs) {
            findCardByID(id).ifPresentOrElse(
                card -> deck.addCard(card.clone()),
                () -> System.out.println("⚠️ 無効なカード ID：" + id)
            );
        }
        deck.shuffle();
        return deck;
    }

    // ✅ ランダムなデモデッキを生成（NullPointerException を回避）
    public Deck createDemoDeck() {
        Deck deck = new Deck();
        if (cardsByID.isEmpty()) {
            System.out.println("⚠️ カードデータベースが空です。デモデッキを作成できません！");
            return deck;
        }

        for (int i = 0; i < 20; i++) {
            deck.addCard(getRandomCard());
        }
        deck.shuffle();
        return deck;
    }

    // ✅ ランダムなカードを取得し、空のデータベースでクラッシュを防ぐ
    private Card getRandomCard() {
        List<Card> allCards = new ArrayList<>(cardsByID.values());
        if (allCards.isEmpty()) {
            throw new IllegalStateException("❌ データベースが空です。ランダムなカードを取得できません！");
        }
        return allCards.get(new Random().nextInt(allCards.size())).clone();
    }

    public void addCard(Card card) {
        if (card == null) {
            System.out.println("⚠️ `null` カードを追加することはできません！");
            return;
        }
        if (cardsByID.containsKey(card.getCardID())) {
            System.out.println("⚠️ カード ID `" + card.getCardID() + "` は既に存在します。登録できません！");
            return;
        }
        cardsByID.put(card.getCardID(), card);
        cardsByName.put(card.getName(), card);
    }

    // ✅ ID を使用してカードを追加（データベースから取得）
    public void addCard(String cardID) {
        findCardByID(cardID).ifPresentOrElse(
            card -> addCard(card.clone()),
            () -> System.out.println("⚠️ カード ID `" + cardID + "` が見つかりません。追加できません！")
        );
    }

    // ✅ カードを一括追加
    public void addCards(List<Card> cardList) {
        for (Card card : cardList) {
            addCard(card);
        }
    }
}
