package com.rizen.dominionwars.player;

import java.util.List;
import java.util.ArrayList;
import java.util.Optional;
import java.util.Scanner;

import com.rizen.dominionwars.cards.AmbushCard;
import com.rizen.dominionwars.cards.Card;
import com.rizen.dominionwars.cards.CardKeyword;
import com.rizen.dominionwars.cards.LeaderCard;
import com.rizen.dominionwars.game.GameContext;

// プレイヤークラス
// プレイヤーの情報を管理するクラス
public class Player {
    private String userID;
    private String password;
    private String name;
    private Deck deck;
    public void setDeck(Deck deck) {
        this.deck = deck;
    }

    private Hand hand;
    private Field field;
    private List<Card> graveyard; // ✅ 墓地をリストとして保持
    private List<Card> banishZone; // 除外ゾーン（必要に応じて追加）
    private LeaderCard leader;
    private boolean hasLeaderOnField = false;
    private List<CardKeyword> usedKeywordsThisTurn = new ArrayList<>();
    private boolean hasSetAmbushThisTurn = false;


    
    public String getUserID() {
        return userID;
    }


    public void setUserID(String userID) {
        this.userID = userID;
    }


    public String getPassword() {
        return password;
    }


    public void setPassword(String password) {
        this.password = password;
    }


    public List<Card> getGraveyard() {
        return graveyard;
    }


    public void setGraveyard(List<Card> graveyard) {
        this.graveyard = graveyard;
    }


    public boolean isHasLeaderOnField() {
        return hasLeaderOnField;
    }


    public void setHasLeaderOnField(boolean hasLeaderOnField) {
        this.hasLeaderOnField = hasLeaderOnField;
    }


    public List<CardKeyword> getUsedKeywordsThisTurn() {
        return usedKeywordsThisTurn;
    }


    public void setUsedKeywordsThisTurn(List<CardKeyword> usedKeywordsThisTurn) {
        this.usedKeywordsThisTurn = usedKeywordsThisTurn;
    }


    public boolean isHasSetAmbushThisTurn() {
        return hasSetAmbushThisTurn;
    }


    public void setHasSetAmbushThisTurn(boolean hasSetAmbushThisTurn) {
        this.hasSetAmbushThisTurn = hasSetAmbushThisTurn;
    }


    public LeaderCard getLeader() {
        return leader;
    }
    
    
    // ✅ コンストラクタ
    // プレイヤーの名前、ユーザーID、パスワードを受け取るコンストラクタ
    public Player(String name,String userID,String password) {
        this.userID = userID;
        this.password = password;
        this.name = name;
        this.hand = new Hand(this);
        this.field = new Field(this);
        this.graveyard = new ArrayList<>(); // ✅ 墓地を初期化
        this.deck = new Deck(); // デッキは空の状態で初期化 
    }

    // ✅ カードを引くロジック
    // ✅ カードを引く処理（手札溢れも対応）
    public boolean drawCard(int count, Scanner scanner) { // Scanner を渡して再生成を防止
        if (deck.isEmpty()) {
            System.out.println("⚠️ デッキが空です。墓地を回収します...");
            recycleGraveyardToDeck(); // ✅ 墓地を回収
            if (deck.isEmpty()) {
                System.out.println(name + " のデッキは依然として空です。カードを引けません！");
                return false;
            }
        }

        for (int i = 0; i < count; i++) {
            Optional<Card> drawnCard = deck.drawCard();
            if (drawnCard.isPresent()) {
                Card card = drawnCard.get();
                if (card instanceof LeaderCard) {
                    leader = (LeaderCard) card;
                    hasLeaderOnField = true;
                    field.placeLeader(leader);
                    System.out.println("👑 リーダー " + leader.getName() + " がフィールドに登場しました！");
                } else {
                    boolean added = hand.addCard(card, scanner); // ✅ Scanner を渡す
                    if (!added) {
                        System.out.println("⚠️ 手札が満杯のため、" + card.getName() + " を引けません！");
                        return false;
                    }
                }
            } else {
                System.out.println(name + " のデッキは空です。カードを引けません！");
                return false;
            }
        }
        return true;
    }

    public void playCard(Card card, GameContext context) {
        if (card instanceof LeaderCard) {
            if (!hasLeaderOnField) {  // フィールド上にリーダーカードが存在しないことを確認
                field.placeLeader((LeaderCard)card);
                hand.removeCard(card);
                hasLeaderOnField = true;  // リーダーカードの状態を更新
                context.markLeaderDeployed(this);
            } else {
                System.out.println("⚠️ すでにフィールドにリーダーカードが存在しています！");
            }
        } else {
            // ... 他の種類のカードの処理 ...
            System.out.println("⚠️ 現在は未対応のカードタイプです");
        }
    }

    // ✅ デッキのみをシャッフルし、墓地はそのまま保持
    public void shuffleDeck() {
        deck.shuffle();
        System.out.println(name + " のデッキをシャッフルしました！");
    }

    public void setLeader(LeaderCard leader) {
        this.leader = leader;
        System.out.println("👑 リーダー " + leader.getName() + " がフィールドに登場しました！");
    }

    // ✅ 墓地を回収してデッキに加え、シャッフルする
    public void recycleGraveyardToDeck() {
        if (graveyard.isEmpty()) {
            System.out.println("⚠️ 墓地にカードがありません。回収できません！");
            return;
        }

        System.out.println("♻️ " + graveyard.size() + " 枚のカードを墓地からデッキに回収します！");
        deck.addCards(graveyard); // ✅ 墓地のカードをデッキに追加
        graveyard.clear(); // ✅ 墓地をクリア
        deck.shuffle(); // ✅ 再シャッフル
    }

    // ✅ キーワード利用の可否チェック
    public boolean canUseKeyword(CardKeyword keyword) {
        return !usedKeywordsThisTurn.contains(keyword);
    }

    // ✅ キーワード使用
    public void useKeyword(CardKeyword keyword) {
        if (!canUseKeyword(keyword)) {
            System.out.println("⚠️ このターン、キーワード " + keyword + " は既に使用済みです！");
            return;
        }
        usedKeywordsThisTurn.add(keyword);
    }

    // ✅ ターンリセット
    public void resetTurn() {
        usedKeywordsThisTurn.clear();
        hasSetAmbushThisTurn = false;
    }

    // ✅ 手札に伏せカードが存在するかチェック
    public boolean hasAmbushCardInHand() {
        for (Card card : hand.getCards()) {
            if (card instanceof AmbushCard) {
                return true;
            }
        }
        return false;
    }

    // ✅ 伏せカードの設定（手札に伏せカードがある場合のみ実行）
    public void setAmbush(AmbushCard ambush) {
        if (!hasAmbushCardInHand()) {
            System.out.println("⚠️ 手札に伏せカードがありません。伏せカードを設定できません！");
            return;
        }
        if (ambush != null) {
            field.setAmbush(ambush, this);
            hasSetAmbushThisTurn = true;
            System.out.println(name + " は伏せカードを設定しました！");
        }
    }
    
    // ✅ カードを墓地に送る
    public void discardToGraveyard(Card card) {
        if (card != null) {
            graveyard.add(card);
            System.out.println("🗑 " + name + " は " + card.getName() + " を墓地に捨てました！");
        }
    }

    // ✅ 旧呼び出しに対応するための drawStartingHand()（Scanner 未指定版）
    public void drawStartingHand(int count) {
        drawStartingHand(count, new Scanner(System.in));  // デフォルトで Scanner を生成
    }

    // ✅ Scanner 指定版の drawStartingHand()
    public void drawStartingHand(int count, Scanner scanner) {
        System.out.println("🎴 " + name + " は起始手札 " + count + " 枚を引いています...");
        for (int i = 0; i < count; i++) {
            if (!drawCard(1, scanner)) {
                System.out.println("⚠️ " + name + " はカードを引けません。デッキが空かもしれません！");
                break;
            }
        }
    }
    
    // Player.java（簡易サンプル）
    private int life = 20; // デフォルトのライフは20

    public void decreaseLife(int amount) {
        life -= amount;
        if (life <= 0) {
            life = 0;
            System.out.println(getName() + " のライフがゼロになり、ゲームオーバーです！");
        } else {
            System.out.println(getName() + " は " + amount + " ダメージを受けました。残りライフ：" + life);
        }
    }
    
    public void discardCards(int count, Scanner scanner) {
        for (int i = 0; i < count; i++) {
            hand.discardOneCard(scanner); // ✅ 手札のカード捨てメソッドを呼び出す
        }
    }
    
    public void showHand() {
        System.out.println("🃏 あなたの手札：");
        List<Card> cards = hand.getCards();
        for (int i = 0; i < cards.size(); i++) {
            System.out.println("  " + (i + 1) + ". " + cards.get(i).getName());
        }
    }
    
    public int getLife() {
        return life;
    }

    // ✅ Getter メソッド
    public String getName() {
        return name;
    }

    public Deck getDeck() {
        return deck;
    }

    public Hand getHand() {
        return hand;
    }

    public Field getField() {
        return field;
    }

    public boolean hasLeaderOnField() {
        return hasLeaderOnField;
    }

    public boolean hasSetAmbushThisTurn() {
        return hasSetAmbushThisTurn;
    }

    
}
