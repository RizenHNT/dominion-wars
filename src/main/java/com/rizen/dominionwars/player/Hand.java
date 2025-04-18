package com.rizen.dominionwars.player;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Scanner;
import java.util.Iterator;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import com.rizen.dominionwars.cards.Card;
import com.rizen.dominionwars.cards.LeaderCard;

public class Hand {
    private List<Card> cards;
    private final Player owner;
    private final ScheduledExecutorService executor = Executors.newSingleThreadScheduledExecutor();
    private static final int MAX_HAND_SIZE = 10;

    // ✅ コンストラクタ：手札を初期化
    public Hand(Player owner) {
        this.cards = new ArrayList<>();
        this.owner = owner;
        startLeaderCheck(); // ✅ リーダーカード検出スレッドを起動
    }

    // 🔥 **リーダーカードを常時検出**
    private void startLeaderCheck() {
        executor.scheduleAtFixedRate(() -> {
            synchronized (cards) { // スレッドセーフ
                checkLeaderInHand();
            }
        }, 0, 500, TimeUnit.MILLISECONDS); // 500msごとに検出
    }

    // **🎯 リーダーカードを即座に登場させる**
    public void checkLeaderInHand() {
        Iterator<Card> iterator = cards.iterator();
        while (iterator.hasNext()) {
            Card card = iterator.next();
            if (card instanceof LeaderCard) {
                System.out.println("👑 リーダー " + card.getName() + " が即座に登場！");
                owner.setLeader((LeaderCard) card);
                owner.getField().placeLeader((LeaderCard) card);
                iterator.remove(); // ✅ 手札から削除
            }
        }
    }

    // ✅ カードを追加
    public boolean addCard(Card card, Scanner scanner) {
        if (card == null) {
            return false;
        }
        if (cards.size() >= MAX_HAND_SIZE) {
            System.out.println("⚠️ 手札が上限に達しました！先にカードを1枚捨ててください。");
            discardOneCard(scanner); // ✅ Scannerを渡す
        }
        cards.add(card);
        System.out.println("✅ カード追加：" + card.getName());
        return true;
    }

    public void discardCards(int number, Scanner scanner) {
        for (int i = 0; i < number; i++) {
            while (true) {
                System.out.println("捨てるカードを選んでください（インデックスを入力）：");
                for (int j = 0; j < cards.size(); j++) {
                    System.out.println(j + " - " + cards.get(j).getName());
                }

                String input = scanner.nextLine().trim();
                try {
                    int index = Integer.parseInt(input);
                    if (index >= 0 && index < cards.size()) {
                        Card discarded = cards.remove(index);
                        System.out.println("🗑 捨てたカード：" + discarded.getName());
                        break;
                    } else {
                        System.out.println("❌ 無効な選択です。もう一度入力してください。");
                    }
                } catch (NumberFormatException e) {
                    System.out.println("❌ 数字を入力してください。");
                }
            }
        }
    }

    public void discardOneCard(Scanner scanner) {
        while (true) {
            System.out.println("⚠️ 手札が上限です！先に1枚捨ててください。");
            System.out.println("🃏 あなたの手札：");

            for (int i = 0; i < cards.size(); i++) {
                System.out.println(i + " - " + cards.get(i).getName());
            }

            System.out.print("捨てるカードの番号を入力してください：");

            String input = scanner.nextLine().trim();
            try {
                int index = Integer.parseInt(input);
                if (index >= 0 && index < cards.size()) {
                    Card discarded = cards.remove(index);
                    System.out.println("🗑 捨てたカード：" + discarded.getName());
                    break;
                } else {
                    System.out.println("❌ 無効な選択です。もう一度入力してください。");
                }
            } catch (NumberFormatException e) {
                System.out.println("❌ 数字を入力してください。");
            }
        }
    }

    // Hand.java 中新增的方法

    // 展示手牌列表
    public void showCards() {
        System.out.println("🃏 你的手牌：");
        for (int i = 0; i < cards.size(); i++) {
            System.out.println("[" + i + "] " + cards.get(i).getName());
        }
    }

    // 手札からカードを削除する（インデックス指定）
    public Card removeCard(int index) {
        if (index >= 0 && index < cards.size()) {
            Card removed = cards.remove(index);
            System.out.println("✨ 手札から削除：" + removed.getName());
            return removed;
        } else {
            System.out.println("❌ 無効なインデックスです。削除に失敗しました！");
            return null;
        }
    }

    // カードオブジェクトで手札から削除
    public boolean removeCard(Card card) {
        boolean removed = cards.remove(card);
        if (removed) {
            System.out.println("✨ 手札から削除：" + card.getName());
        }
        return removed;
    }

    // 手札内でカードのインデックスを検索
    public int findCardIndex(Card card) {
        return cards.indexOf(card);
    }

    public List<Card> getCards() {
        return Collections.unmodifiableList(cards);
    }

    public Card getCard(int index) {
        return cards.get(index);
    }
    // ✅ 清空手牌
    public void clear() {
        cards.clear();
    }

    public void shutdownExecutor() {
        executor.shutdown();
        try {
            if (!executor.awaitTermination(1, TimeUnit.SECONDS)) {
                executor.shutdownNow();
            }
        } catch (InterruptedException e) {
            executor.shutdownNow();
        }
    }
}
