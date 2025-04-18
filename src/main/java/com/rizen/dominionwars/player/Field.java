package com.rizen.dominionwars.player;

import java.util.List;
import java.util.ArrayList;
import java.util.Optional;
import java.util.Iterator;
import java.util.Collections;
import java.util.Scanner;
import com.rizen.dominionwars.cards.MinionCard;
import com.rizen.dominionwars.cards.SpellCard;
import com.rizen.dominionwars.cards.AmbushCard;
import com.rizen.dominionwars.cards.Card;
import com.rizen.dominionwars.cards.LeaderCard;
import com.rizen.dominionwars.game.GameContext;

public class Field {
    private LeaderCard leader;          // リーダーゾーン
    private BattleZone frontRow;        // 前線戦域
    private BattleZone backRow;         // 後衛戦域
    private List<SpellCard> chantArea;  // 吟唱ゾーン
    private List<Card> banishZone;      // 除外ゾーン
    private AmbushCard setAmbush;       // 伏せゾーン
    private Player owner;               // フィールド所有者

    public Field(Player owner) {
        this.owner = owner;
        this.frontRow = new BattleZone(3, "前線戦域");    // 前列は3箇所まで配置可能
        this.backRow = new BattleZone(4, "後衛戦域");     // 後列は4箇所まで配置可能
        this.chantArea = new ArrayList<>();
        this.banishZone = new ArrayList<>();
    }

    // 内部クラス：戦闘区域
    private class BattleZone {
        private MinionCard[] slots;     // 配列を使用して固定位置を実現
        private final String zoneName;
        private final int maxSlots;

        public BattleZone(int maxSlots, String name) {
            this.slots = new MinionCard[maxSlots];
            this.maxSlots = maxSlots;
            this.zoneName = name;
        }

        // 指定位置にミニオンを配置する
        public boolean deployMinion(MinionCard minion, int position) {
            if (position < 0 || position >= maxSlots) {
                System.out.println("❌ 無効な配置位置です！");
                return false;
            }
            if (slots[position] != null) {
                System.out.println("❌ この位置はすでに使用されています！");
                return false;
            }
            slots[position] = minion;
            System.out.println("✅ " + minion.getName() + " を " + zoneName + " の位置 " + (position + 1) + " に配置しました！");
            return true;
        }

        // 指定位置のミニオンを除去
        public MinionCard removeMinion(int position) {
            if (position < 0 || position >= maxSlots || slots[position] == null) {
                return null;
            }
            MinionCard removed = slots[position];
            slots[position] = null;
            return removed;
        }

        // 空き位置の一覧を取得
        public List<Integer> getEmptySlots() {
            List<Integer> emptySlots = new ArrayList<>();
            for (int i = 0; i < slots.length; i++) {
                if (slots[i] == null) {
                    emptySlots.add(i + 1);
                }
            }
            return emptySlots;
        }

        // ゾーンの状態を表示
        public void showZone() {
            System.out.println("🎪 " + zoneName + "：");
            for (int i = 0; i < slots.length; i++) {
                if (slots[i] != null) {
                    MinionCard minion = slots[i];
                    System.out.printf("  位置%d: %s (%d/%d)%n", 
                        i + 1, minion.getName(), minion.getAttack(), minion.getHealth());
                } else {
                    System.out.printf("  位置%d: ➖%n", i + 1);
                }
            }
        }
    }

    // ミニオンの追加（前線戦域へ配置）
    public void addMinionToFrontRow(MinionCard minion, Scanner scanner) {
        List<Integer> emptySlots = frontRow.getEmptySlots();
        if (emptySlots.isEmpty()) {
            System.out.println("❌ 前線戦域が満杯です！");
            return;
        }
        
        System.out.println("🎯 配置する位置を選択してください（" + emptySlots.toString().replaceAll("[\\[\\]]", "") + "）：");
      
        try {
            int position = Integer.parseInt(scanner.nextLine().trim()) - 1;
            frontRow.deployMinion(minion, position);
        } catch (NumberFormatException e) {
            System.out.println("❌ 無効な位置です！最初の空き位置に配置します。");
            frontRow.deployMinion(minion, emptySlots.get(0) - 1);
        }
    }

    // ミニオンの追加（後衛戦域へ配置）
    public void addMinionToBackRow(MinionCard minion, Scanner scanner) {
        List<Integer> emptySlots = backRow.getEmptySlots();
        if (emptySlots.isEmpty()) {
            System.out.println("❌ 後衛戦域が満杯です！");
            return;
        }
        
        System.out.println("🎯 配置する位置を選択してください（" + emptySlots.toString().replaceAll("[\\[\\]]", "") + "）：");
      
        try {
            int position = Integer.parseInt(scanner.nextLine().trim()) - 1;
            backRow.deployMinion(minion, position);
        } catch (NumberFormatException e) {
            System.out.println("❌ 無効な位置です！最初の空き位置に配置します。");
            backRow.deployMinion(minion, emptySlots.get(0) - 1);
        }
    }

    // ミニオンの追加：配置ゾーンを選択
    public void addMinion(MinionCard minion, Scanner scanner) {
        System.out.println("\n🎯 配置するゾーンを選択してください：");
        System.out.println("1. 前線戦域");
        System.out.println("2. 後衛戦域");
        
        String choice = scanner.nextLine().trim();
        
        switch (choice) {
            case "1":
                addMinionToFrontRow(minion, scanner);
                break;
            case "2":
                addMinionToBackRow(minion, scanner);
                break;
            default:
                System.out.println("❌ 無効な選択です！デフォルトで前線戦域に配置します。");
                addMinionToFrontRow(minion, scanner);
                break;
        }
    }

    // リーダーの配置
    public void placeLeader(LeaderCard leader) {
        if (this.leader == null) {
            this.leader = leader;
            System.out.println("⚔️ リーダー " + leader.getName() + " がフィールドに登場しました！");
        } else {
            System.out.println("⚠️ 既にリーダーがフィールドに存在するため、再配置できません！");
        }
    }

    public void showField(Player viewer) {
        System.out.println("\n" + (owner == viewer ? "⚔️ あなたのフィールド：" : "🗡 相手のフィールド："));
        
        // リーダーゾーンの表示
        System.out.println("👑 リーダーゾーン：" + (leader != null ? 
            formatLeaderInfo(leader) : "なし"));

        // 戦闘ゾーンの表示
        frontRow.showZone();
        backRow.showZone();

        // 吟唱ゾーンの表示
        if (!chantArea.isEmpty()) {
            System.out.println("🎭 吟唱ゾーン：");
            for (SpellCard spell : chantArea) {
                System.out.printf("  %s (残り%dターン)%n", 
                    spell.getName(), spell.getRemainingChantTurns());
            }
        }

        // 伏せゾーンは所有者のみ表示
        if (owner == viewer && setAmbush != null) {
            System.out.println("🕵 伏せゾーン：" + setAmbush.getName());
        }

        // 除外ゾーンの枚数を表示
        if (!banishZone.isEmpty()) {
            System.out.println("⚡ 除外ゾーン：" + banishZone.size() + " 枚");
        }
    }

    // リーダーの種類に応じた情報を整形して表示
    private String formatLeaderInfo(LeaderCard leader) {
        StringBuilder info = new StringBuilder(leader.getName());
        
        // リーダーの具体的なタイプによって異なる情報を表示
        switch (leader.getLeaderType()) {
            case MINION:
                info.append(" (戦闘リーダー)");
                break;
            case SPELL:
                info.append(" (呪文リーダー)");
                break;
            case LEADER:
                info.append(" (統御リーダー)");
                break;
            default:
                info.append(" (未知のタイプ)");
                break;
        }
        
        return info.toString();
    }

    // ✅ 吟唱呪文の追加
    public void addChantingSpell(SpellCard spell) {
        if (spell != null) {
            chantArea.add(spell);
            System.out.println("🔮 " + spell.getName() + " の吟唱を開始しました！");
        }
    }

    // ✅ 伏せカードの設定（手札に伏せカードがある場合のみ設定可能）
    public void setAmbush(AmbushCard ambush, Player owner) {
        if (!owner.hasAmbushCardInHand()) {
            System.out.println("⚠️ 手札に伏せカードがありません。伏せカードを設定できません！");
            return;
        }
        if (ambush == null) {
            System.out.println("⚠️ 伏せカードは null ではありません！");
            return;
        }
        if (this.setAmbush != null) {
            System.out.println("⚠️ フィールド上に既に伏せカードが存在するため、新しい伏せカードに置き換えます！");
        }
        this.setAmbush = ambush;
        System.out.println("🕵 伏せカード " + ambush.getName() + " が配置されました！");
    }

    // ✅ ターン終了時の効果を処理
    public void updateEndOfTurn(GameContext gameContext, Player currentPlayer, Player opponentPlayer) {
        System.out.println("🔄 フィールドのターン終了効果を処理中...");
    
        // 🎭 吟唱呪文の処理
        Iterator<SpellCard> iterator = chantArea.iterator();
        while (iterator.hasNext()) {
            SpellCard spell = iterator.next();
            spell.reduceChantTurns();
            if (spell.isChantComplete()) {
                spell.executeSpellEffect(gameContext, currentPlayer, opponentPlayer);
                iterator.remove(); // ✅ 安全に削除
                System.out.println("✨ " + spell.getName() + " が正常に発動しました！");
            }
        }
    }

    // ✅ 戦場上のミニオンを取得（変更不可のリスト）
    public List<MinionCard> getMinions() {
        List<MinionCard> allMinions = new ArrayList<>();
        Collections.addAll(allMinions, frontRow.slots);
        Collections.addAll(allMinions, backRow.slots);
        return Collections.unmodifiableList(allMinions);
    }

    // ✅ 伏せカードを取得（Optional を使用）
    public Optional<AmbushCard> getAmbush() {
        return Optional.ofNullable(setAmbush);
    }
}
