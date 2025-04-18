package com.rizen.dominionwars;

import com.rizen.dominionwars.cards.*;
import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.*;
import com.rizen.dominionwars.player.*;
import com.rizen.dominionwars.specific.FlameImpact;
import com.rizen.dominionwars.specific.TakLeader;

import java.nio.charset.StandardCharsets;
import java.util.Scanner;

public class Main {
    public static void main(String[] args) throws Exception {
        // WindowsコンソールをUTF-8エンコーディングに変更（1回だけでOK）
        try {
            new ProcessBuilder("cmd", "/c", "chcp 65001").inheritIO().start().waitFor();
        } catch (Exception e) {
            // 例外は無視
        }

        System.setOut(new java.io.PrintStream(System.out, true, "UTF-8"));
        Scanner scanner = new Scanner(System.in, "UTF-8");

        System.out.println("🎮 ようこそ！");

        // 🛠 カードデータベースの初期化
        CardDatabase cardDB = new CardDatabase();
        cardInitialization(cardDB);

        // 🎭 プレイヤーの作成
        Player player1 = new Player("USER1", "0000", "0000");
        Player player2 = new Player("USER2", "0001", "0001");

        // 🎴 プレイヤー情報に基づいてデッキを作成
        Deck deckPlayer1 = createDemoDeck(cardDB, player1.getUserID()); // USER1のデッキ
        Deck deckPlayer2 = createDemoDeck(cardDB, player2.getUserID()); // USER2のデッキ
        player1.setDeck(deckPlayer1);
        player2.setDeck(deckPlayer2);

        // 🎲 ゲームコンテキストの初期化
        GameContext context = new GameContext(player1, player2, scanner);
        deckPlayer1.shuffle();
        deckPlayer2.shuffle();
        // 🔄 初期手札を5枚ドロー
        player1.drawStartingHand(5);
        player2.drawStartingHand(5);

        // 🕹 メインゲームループ
        while (!context.isGameOver()) {
            System.out.println(
                    "\n🎮 ターン: " + context.getTurnNumber() + " | 現在のプレイヤー: " + context.getCurrentPlayer().getName());
            System.out.println("【Enter】で続行、【exit】で終了...");

            String input = scanner.nextLine().trim().toLowerCase();
            if (input.equals("exit")) {
                System.out.println("👋 ゲームを終了します。プレイしていただき、ありがとうございました！");
                break;
            }

            context.nextPhase(scanner);
        }

        // 🏆 通常終了時に勝者を表示
        if (context.isGameOver()) {
            System.out.println("🏆 ゲーム終了！勝者: " + context.getWinner().getName());
        }

        scanner.close();
        // Scanner とScheduledExecutorServiceを閉じる
        player1.getHand().shutdownExecutor();
        player2.getHand().shutdownExecutor();
    }

    // 🛠 カードデータベースの初期化
    private static void cardInitialization(CardDatabase cardDB) {
        System.out.println("📚 カードデータベースを初期化中...");
        cardDB.addCard(new TakLeader());
        cardDB.addCard(new FlameImpact());
        System.out.println("✅ カードデータベースの初期化が完了しました！");
    }

    // 🎴 デモデッキの作成
    private static Deck createDemoDeck(CardDatabase cardDB, String leaderID) {
        Deck deck = new Deck();
        System.out.println("🛠 デッキを作成中...");

        // 👑 リーダーカード（最低1枚必要）
        if (leaderID != null) {
            deck.addCard(cardDB.getCardByID(leaderID));
        }
        // 🎲 その他のカード（デッキが20枚になるまで追加）
        for (int i = 0; i < 5; i++) {
            deck.addCard(cardDB.getCardByID("F0001"));
        }

        deck.shuffle();

        // ✅ デッキが空でない場合のみ表示
        if (!deck.getCards().isEmpty()) {
            System.out.println("✅ デッキの作成が完了しました。現在のデッキ内容：");
            deck.showDeck();
        } else {
            System.out.println("❌ デッキの作成に失敗しました。カードDBが初期化されていない可能性があります！");
        }

        return deck;
    }
}
