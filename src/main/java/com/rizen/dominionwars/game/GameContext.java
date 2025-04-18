package com.rizen.dominionwars.game;

import java.util.Scanner;

import com.rizen.dominionwars.cards.AmbushCard;
import com.rizen.dominionwars.player.Player;
import com.rizen.dominionwars.cards.Card;
import com.rizen.dominionwars.cards.LeaderCard;
import com.rizen.dominionwars.cards.SpellCard;
import com.rizen.dominionwars.cards.MinionCard;

public class GameContext {
    private Player currentPlayer;
    private Player opponentPlayer;
    private int turnNumber;
    private GamePhase currentPhase;
    private boolean gameStarted = false; // ✅ ゲームが正式に開始されたかどうかを追跡
    private final Scanner scanner; // ✅ Scanner オブジェクトを保存

    public Player getCurrentPlayer() {
        return currentPlayer;
    }

    public void setCurrentPlayer(Player currentPlayer) {
        this.currentPlayer = currentPlayer;
    }

    public Player getOpponentPlayer() {
        return opponentPlayer;
    }

    public Scanner getScanner() {
        return scanner;
    }

    public void setOpponentPlayer(Player opponentPlayer) {
        this.opponentPlayer = opponentPlayer;
    }

    public int getTurnNumber() {
        return turnNumber;
    }

    public void setTurnNumber(int turnNumber) {
        this.turnNumber = turnNumber;
    }

    public GamePhase getCurrentPhase() {
        return currentPhase;
    }

    public void setCurrentPhase(GamePhase currentPhase) {
        this.currentPhase = currentPhase;
    }

    public void nextPhase() {
        nextPhase(new Scanner(System.in)); // デフォルトで Scanner を作成
    }

    public void nextPhase(Scanner scanner) {
        if (!gameStarted) {
            gameStarted = true; // ✅ ゲームが正式に開始され、勝敗のチェックが許可される
        }
        if (isGameOver()) {
            System.out.println("🏆 ゲーム終了！勝者：" + getWinner().getName());
            return;
        }

        // 📌 フェーズ名を表示
        System.out.println("📢 フェーズに入る：" + currentPhase);

        switch (currentPhase) {
            case SHUFFLE:
                System.out.println("🔄【シャッフルフェーズ】");

                // デバッグ：デッキ内容を表示
                System.out.println("🎴 " + currentPlayer.getName() + " のデッキ：");
                currentPlayer.getDeck().showDeck();

                if (turnNumber == 0) {
                    System.out.println("🎴 ゲーム開始！各プレイヤーはカードを5枚引く...");
                    currentPlayer.drawCard(5, scanner);
                    opponentPlayer.drawCard(5, scanner);
                } else {
                    if (currentPlayer.getDeck().isEmpty()) {
                        System.out.println("⚠️ デッキが空いています。墓地をデッキに戻します...");
                        currentPlayer.recycleGraveyardToDeck();
                    }

                    if (currentPlayer.getDeck().isEmpty()) {
                        System.out.println("⚠️ " + currentPlayer.getName() + " はカードを引けません。ゲームを続行します！");
                    } else {
                        System.out.println("🎴 " + currentPlayer.getName() + " はカードを1枚引きました！");
                        currentPlayer.drawCard(1, scanner);
                    }
                }
                currentPhase = GamePhase.AMBUSH;
                break;

            case AMBUSH:
                System.out.println("🕵【伏せフェーズ】");
                System.out.println("戦場に伏せカードを配置できます！");
                currentPhase = GamePhase.ACTION;
                break;

            case ACTION:
                System.out.println("🎭【アクションフェーズ】");
                System.out.println("\n現在の戦場の状態：");
                currentPlayer.getField().showField(currentPlayer); // 自分の場を表示
                opponentPlayer.getField().showField(currentPlayer); // 敵の場を表示

                System.out.println("📜 手札をプレイするか、他のアクションを選択してください...");

                boolean actionPhaseEnded = false;
                while (!actionPhaseEnded) {
                    currentPlayer.showHand();
                    System.out.println("\n📜 アクションを選択してください：");
                    System.out.println("1️⃣ 手札を使用");
                    System.out.println("2️⃣ 戦場を確認");
                    System.out.println("3️⃣ ターン終了");

                    String choice = scanner.nextLine().trim();
                    switch (choice) {
                        case "1":
                            playCardFromHand(scanner);
                            break;
                        case "2":
                            showBattlefield();
                            break;
                        case "3":
                            actionPhaseEnded = true;
                            break;
                        default:
                            System.out.println("❌ 無効な選択です！");
                            break;
                    }
                }
                currentPhase = GamePhase.END;
                break;

            case END:
                System.out.println("⏳【ターン終了フェーズ】");
                System.out.println("場の状態を処理し、相手のターンに切り替えます！");
                currentPlayer.getField().updateEndOfTurn(this, currentPlayer, opponentPlayer);
                swapPlayersAndResetTurn();
                currentPhase = GamePhase.SHUFFLE;
                turnNumber++;
                break;
        }
    }
    private void playSpellCard(SpellCard spell, int cardIndex, Scanner scanner) {
        if (spell.isChanting()) {
            System.out.println("🎭 放置中の吟唱呪文：" + spell.getName() + 
                "（吟唱必要ターン：" + spell.getChantTurns() + " ターン）");
            currentPlayer.getField().addChantingSpell(spell);
            currentPlayer.getHand().removeCard(cardIndex);
        } else {
            System.out.println("⚡ 即時呪文を発動：" + spell.getName());
            if (spell.use(this, currentPlayer, opponentPlayer)) {
                currentPlayer.getHand().removeCard(cardIndex);
            }
        }
    }

    // ✅ プレイヤーの交換 + ターンのリセットを統合
    private void swapPlayersAndResetTurn() {
        Player temp = currentPlayer;
        currentPlayer = opponentPlayer;
        opponentPlayer = temp;
        currentPlayer.resetTurn();
    }

    private boolean player1LeaderDeployed = false;
    private boolean player2LeaderDeployed = false;

    // ✅ 新規追加：リーダーカードが配置されたことをマークするメソッド
    public void markLeaderDeployed(Player player) {
        if (player == currentPlayer) {
            player1LeaderDeployed = true;
            System.out.println("👑 " + player.getName() + " のリーダーカードが登場しました！");
        } else if (player == opponentPlayer) {
            player2LeaderDeployed = true;
            System.out.println("👑 " + player.getName() + " のリーダーカードが登場しました！");
        }
    }

    // ✅ ゲーム終了判定ロジックの修正
    public boolean isGameOver() {
        // どちらかのリーダーカードがまだ登場していない場合、ゲーム終了をチェックしない
        if (!player1LeaderDeployed || !player2LeaderDeployed) {
            return false;
        }
        // 両方のリーダーカードが登場した場合のみ、場の状態をチェックする
        return !currentPlayer.hasLeaderOnField() || !opponentPlayer.hasLeaderOnField();
    }

    // ✅ 勝者を取得するロジックの修正

    // ✅ これで getWinner() はゲームが終了していない場合に null を返さず、情報を表示する
    public Player getWinner() {
        if (!opponentPlayer.hasLeaderOnField()) {
            return currentPlayer;
        }
        if (!currentPlayer.hasLeaderOnField()) {
            return opponentPlayer;
        }
        System.out.println("⚠️ ゲームはまだ終了していないため、勝者を確定できません！");
        return null;
    }

    // ✅ プレイヤーが伏せカードを設定するメソッドを追加し、null チェックを追加して NullPointerException を防止
    public void playerSetAmbush(AmbushCard ambush) {
        if (ambush == null) {
            System.out.println("⚠️ 伏せカードは null ではいけません！");
            return;
        }
        currentPlayer.getField().setAmbush(ambush, currentPlayer);
    }

    public GameContext(Player player1, Player player2, Scanner scanner) {
        this.scanner = scanner; // ✅ Scanner オブジェクトを保存
        this.currentPlayer = player1;
        this.opponentPlayer = player2;
        this.currentPhase = GamePhase.SHUFFLE;
        this.turnNumber = 1;
    }

    private void playCardFromHand(Scanner scanner) {
        System.out.println("🎴 使用するカードの番号を選択してください（1-" + currentPlayer.getHand().getCards().size() + 
                          "、キャンセルするには q を入力）：");
        currentPlayer.showHand();

        String input = scanner.nextLine().trim().toLowerCase();
        if (input.equals("q")) {
            System.out.println("⚪ 出牌をキャンセル");
            return;
        }

        try {
            int cardIndex = Integer.parseInt(input) - 1; // ユーザー入力1-Nを0-(N-1)に変換
            if (cardIndex >= 0 && cardIndex < currentPlayer.getHand().getCards().size()) {
                Card selectedCard = currentPlayer.getHand().getCard(cardIndex);
                if (selectedCard != null) {
                    playSelectedCard(selectedCard, cardIndex, scanner);
                }
            } else {
                System.out.println("❌ 無効なカード番号です！1-" + 
                                 currentPlayer.getHand().getCards().size() + " の間の数字を入力してください。");
            }
        } catch (NumberFormatException e) {
            System.out.println("❌ 有効な数字を入力するか、q を入力してキャンセルしてください！");
        }
    }

    // 新規追加：選択されたカードを処理するロジック
    private void playSelectedCard(Card selectedCard, int cardIndex, Scanner scanner) {
        if (selectedCard instanceof LeaderCard) {
            currentPlayer.playCard(selectedCard, this);
        } else if (selectedCard instanceof MinionCard) {
            playMinionCard((MinionCard)selectedCard, cardIndex, scanner);
        } else if (selectedCard instanceof SpellCard) {
            playSpellCard((SpellCard)selectedCard, cardIndex, scanner);
        }
    }

    // 新規追加：ミニオンカードの配置を処理するメソッド
    private void playMinionCard(MinionCard minion, int cardIndex, Scanner scanner) {
        while (true) {
            System.out.println("配置位置を選択してください：");
            System.out.println("1. 前列");
            System.out.println("2. 後列");
            System.out.println("q. 配置をキャンセル");
            
            String position = scanner.nextLine().trim().toLowerCase();
            if (position.equals("q")) {
                System.out.println("⚪ 配置をキャンセル");
                return;
            }
            
            if (position.equals("1")) {
                currentPlayer.getField().addMinionToFrontRow(minion, scanner);
                currentPlayer.getHand().removeCard(cardIndex);
                System.out.println("🛡 ミニオン " + minion.getName() + " を前列に配置しました！");
                break;
            } else if (position.equals("2")) {
                currentPlayer.getField().addMinionToBackRow(minion, scanner);
                currentPlayer.getHand().removeCard(cardIndex);
                System.out.println("🏹 ミニオン " + minion.getName() + " を後列に配置しました！");
                break;
            } else {
                System.out.println("❌ 無効な位置選択です！1、2 または q を選択してください");
            }
        }
    }

    private void showBattlefield() {
        System.out.println("\n🏰 現在の戦場の状態：");
        System.out.println("\n自分の場：");
        currentPlayer.getField().showField(currentPlayer);
        System.out.println("\n敵の場：");
        opponentPlayer.getField().showField(currentPlayer);
    }
}