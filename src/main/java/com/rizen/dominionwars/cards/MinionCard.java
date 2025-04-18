package com.rizen.dominionwars.cards;
import java.util.ArrayList;

import com.rizen.dominionwars.factions.Faction;
import com.rizen.dominionwars.game.GameContext;
import com.rizen.dominionwars.player.Player;

public abstract class MinionCard extends Card {
    private int attack;
    private int health;
    private int maxHealth; 
    private boolean isOnField; // フィールド上にいるかどうか

    // ✅ コンストラクタ（Cardを継承するために必要）
    public MinionCard(String cardID, String name, String description, int punishmentValue, Rarity rarity, Faction faction, int attack, int health) {
        super(cardID, name, description, punishmentValue, rarity, faction, CardType.MINION, new ArrayList<>());
        this.attack = attack;
        this.health = health;
        this.maxHealth = health;  
        this.isOnField = false;
    }

    public int getMaxHealth() {
        return maxHealth;
    }

    public void resetHealth() {
        this.health = this.maxHealth;
    }
    
    // ✅ 攻撃ロジック
    public boolean attack(MinionCard target) {
        if (!isOnField || !target.isOnField) {
            return false; // フィールド上にいないミニオンは攻撃できない
        }

        // ダメージ計算
        target.takeDamage(this.attack);
        this.takeDamage(target.attack);
        
        return true;
    }

    // ✅ ミニオンにダメージを与える
    public void takeDamage(int damage) {
        this.health -= damage;
        if (this.health <= 0) {
            this.die();
        }
    }

    public boolean isOnField() {
        return isOnField;
    }

    // 回復
    public void healToMax() {
        this.health = maxHealth;
    }
    
    // ✅ ミニオンを死亡させる
    public void die() {
        System.out.println(getName() + " が破壊された！");
        isOnField = false; // フィールドから取り除かれる
    }

    // ✅ 基底クラスのメソッドをオーバーライド
    @Override
    public boolean use(GameContext context, Player owner, Player opponent) {
        if (isOnField) {
            return false; // 既にフィールド上にいるミニオンは召喚できない
        }
        owner.getField().addMinion(this, context.getScanner());
        isOnField = true;
        return true;
    }

    public int getAttack() {
        return attack;
    }
    
    public int getHealth() {
        return health;
    }
    
    public void setAttack(int attack) {
        this.attack = attack;
    }
    
    public void setHealth(int health) {
        this.health = health;
    }

    @Override
    public abstract Card clone();  
}

