package com.dominionwars.ui.i18n;

import com.dominionwars.engine.Game;
import com.dominionwars.model.CardDef;
import com.dominionwars.ui.settings.UiSettings;

import java.util.HashMap;
import java.util.Map;

/**
 * UI 文案的轻量多语言表。卡牌名称/描述仍来自 JSON 数据，后续可扩展 card localization。
 */
public final class I18n {
    private I18n() { }

    private static final Map<String, String[]> TEXT = new HashMap<>();
    static {
        put("app.title", "统御战纪", "統御戦記", "Dominion Wars");
        put("app.duelTitle", "统御战纪 - 对战", "統御戦記 - 対戦", "Dominion Wars - Duel");
        put("app.subtitle", "以惩罚为代价的对决 —— 击败对方统领者胜", "ペナルティを代価に戦う対決 —— 相手の統領を倒せ", "A duel paid by punishment — defeat the enemy leader");
        put("menu.playerADeck", "玩家A 卡组：", "プレイヤーA デッキ：", "Player A Deck:");
        put("menu.playerBDeck", "玩家B 卡组：", "プレイヤーB デッキ：", "Player B Deck:");
        put("menu.pve", "人机对战（玩家A = 你）", "CPU対戦（プレイヤーA = あなた）", "VS CPU (Player A = You)");
        put("menu.pvp", "双人同屏对战", "2人同画面対戦", "Local Hotseat Duel");
        put("menu.aiai", "AI 演示对局（观战）", "AIデモ対戦（観戦）", "AI Demo Duel");
        put("menu.editor", "卡牌编辑器", "カードエディター", "Card Editor");
        put("menu.rules", "查看规则", "ルールを見る", "View Rules");
        put("menu.settings", "设置", "設定", "Settings");
        put("menu.deckMissing", "未找到可用卡组，请检查 data/decks 目录。", "使用可能なデッキが見つかりません。data/decks を確認してください。", "No available decks found. Please check the data/decks directory.");
        put("menu.deckErrorTitle", "卡组错误", "デッキエラー", "Deck Error");
        put("menu.deckProblem", "卡组校验问题：", "デッキ検証の問題：", "Deck validation issues:");
        put("rules.title", "统御战纪 规则书", "統御戦記 ルールブック", "Dominion Wars Rulebook");
        put("rules.missing", "未找到 docs/RULES.md（请确认从项目根目录运行）。", "docs/RULES.md が見つかりません（プロジェクトルートから起動してください）。", "docs/RULES.md was not found. Please run from the project root.");

        put("settings.title", "设置", "設定", "Settings");
        put("settings.language", "语言", "言語", "Language");
        put("settings.density", "页面显示", "画面表示", "Display");
        put("settings.tooltip", "显示卡牌提示", "カード詳細ツールチップを表示", "Show card tooltips");
        put("settings.ambushCount", "显示对方伏击数量", "相手の伏撃枚数を表示", "Show opponent ambush count");
        put("settings.apply", "应用", "適用", "Apply");
        put("settings.close", "关闭", "閉じる", "Close");
        put("settings.note", "语言和显示设置会立即应用到新打开的窗口；当前对局部分区域会在刷新后更新。", "言語と表示設定は新しく開く画面にすぐ反映されます。現在の対戦画面は更新後に一部反映されます。", "Language and display settings apply immediately to newly opened windows; parts of the current duel update after refresh.");

        put("side.opponent", "对方", "相手", "Opponent");
        put("side.self", "己方", "自分", "You");
        put("zone.opponent", "对方：", "相手：", "Opponent: ");
        put("zone.self", "己方：", "自分：", "You: ");
        put("zone.oppField", "对方战场（点击随从选择攻击目标）", "相手の戦場（ミニオンをクリックして攻撃対象を選択）", "Opponent Field (click a minion to choose attack target)");
        put("zone.myField", "己方战场（点击随从选择攻击者）", "自分の戦場（ミニオンをクリックして攻撃者を選択）", "Your Field (click a minion to select attacker)");
        put("zone.oppAmbush", "对方伏击区", "相手の伏撃ゾーン", "Opponent Ambush Zone");
        put("zone.myAmbush", "己方伏击区", "自分の伏撃ゾーン", "Your Ambush Zone");
        put("zone.hand", "手牌", "手札", "Hand");
        put("zone.log", "对局日志", "対戦ログ", "Duel Log");
        put("zone.empty", "（空）", "（なし）", "(empty)");
        put("zone.noAmbush", "（无伏击）", "（伏撃なし）", "(no ambushes)");
        put("zone.noHand", "（无手牌）", "（手札なし）", "(no cards in hand)");
        put("zone.hiddenAmbush", "已盖放", "セット済み", "Set");
        put("zone.ambush", "伏击", "伏撃", "Ambush");
        put("zone.hiddenAmbushTip", "对方盖放的伏击，内容隐藏", "相手がセットした伏撃。内容は非公開です。", "Opponent's set ambush. Contents are hidden.");
        put("zone.hiddenInfo", "（隐藏）", "（非表示）", "(hidden)");

        put("btn.skipAmbush", "跳过伏击阶段", "伏撃フェイズをスキップ", "Skip Ambush Phase");
        put("btn.attackCore", "攻击王城/对方统领", "王城/相手統領を攻撃", "Attack Castle / Enemy Leader");
        put("btn.endTurn", "结束回合", "ターン終了", "End Turn");
        put("btn.settings", "设置", "設定", "Settings");

        put("phase.turn", "第 %d 回合", "第%dターン", "Turn %d");
        put("phase.current", "当前玩家：", "現在のプレイヤー：", "Current Player: ");
        put("phase.phase", "阶段：", "フェイズ：", "Phase: ");
        put("phase.castle", "王城", "王城", "Royal Castle");
        put("phase.winner", "胜者：", "勝者：", "Winner: ");
        put("phase.start", "开始阶段", "開始フェイズ", "Start Phase");
        put("phase.ambush", "伏击阶段", "伏撃フェイズ", "Ambush Phase");
        put("phase.action", "行动阶段", "行動フェイズ", "Action Phase");
        put("phase.discard", "弃牌阶段", "捨て札フェイズ", "Discard Phase");
        put("phase.end", "结束阶段", "終了フェイズ", "End Phase");
        put("phase.gameover", "对局结束", "対戦終了", "Game Over");

        put("info.deck", "卡组", "デッキ", "Deck");
        put("info.hand", "手牌", "手札", "Hand");
        put("info.grave", "墓地", "墓地", "Graveyard");
        put("info.ambushZone", "伏击区", "伏撃ゾーン", "Ambushes");
        put("info.cardUnit", "张", "枚", "cards");
        put("info.life", "生命", "ライフ", "Life");
        put("info.leader", "统领", "統領", "Leader");
        put("info.leaderAbsent", "统领未登场", "統領未登場", "Leader not deployed");
        put("info.disabled", "被压制", "無効化", "Disabled");
        put("info.victoryCount", "胜利计数", "勝利カウント", "Victory Count");
        put("info.cycle", "循环", "循環", "Cycles");
        put("info.durability", "耐久", "耐久", "Durability");
        put("info.chant", "吟", "詠", "Chant");

        put("status.selected", "已选", "選択中", "Selected");
        put("status.canAttack", "可攻", "攻撃可", "Can Attack");
        put("status.target", "可选目标", "対象可", "Targetable");
        put("status.usable", "可用", "使用可", "Usable");
        put("status.unusable", "不可用", "使用不可", "Unusable");
        put("status.currentUsable", "当前阶段可用", "現在のフェイズで使用可能", "Usable in the current phase");
        put("status.unusableBecause", "不可用：", "使用不可：", "Unusable: ");

        put("type.minion", "随从", "ミニオン", "Minion");
        put("type.spell", "咒文", "呪文", "Spell");
        put("type.ambush", "伏击", "伏撃", "Ambush");
        put("type.punish", "惩罚", "ペナルティ", "Punish");
        put("ambush.normal", "普通", "通常", "Normal");
        put("ambush.focus", "专注", "集中", "Focus");
        put("ambush.lockdown", "封场", "封鎖", "Lockdown");
        put("kw.guard", "护卫", "護衛", "Guard");
        put("kw.kingSlayer", "弑君", "王殺し", "King Slayer");
        put("kw.leader", "统领", "統領", "Leader");
        put("kw.punish", "惩罚", "ペナルティ", "Punish");
        put("kw.tags", "词条", "タグ", "Tags");
        put("kw.atkHp", "攻/血", "攻/体力", "ATK/HP");
        put("kw.ambushKind", "伏击类型", "伏撃種別", "Ambush Type");
        put("castle.name", "共享王城", "共有王城", "Royal Castle");
        put("castle.broken", "王城已被击破", "王城は陥落した", "Castle Broken");
        put("hud.victory", "胜利计数", "勝利カウント", "Victory");
        put("hud.goal", "目标", "目標", "Goal");
        put("hud.goalTurn", "本回合", "このターン", "this turn");
        put("hud.deck", "卡组", "デッキ", "Deck");
        put("hud.grave", "墓地", "墓地", "Grave");
        put("hud.handCount", "手牌", "手札", "Hand");
        put("hud.leaderAbsent", "统领未登场", "統領未登場", "Leader not fielded");
        put("hud.life", "生命", "ライフ", "Life");
        put("hud.durability", "耐久", "耐久", "Durability");
        put("hud.suppressed", "被压制", "抑制中", "Suppressed");

        put("msg.needAttacker", "请先在己方战场点击一个随从作为攻击者", "先に自分の戦場で攻撃者を選択してください。", "Select an attacker on your field first.");
        put("msg.cannotAttackCore", "当前无法攻击王城/对方统领（存在嘲讽或攻击者不可行动）", "現在、王城/相手統領を攻撃できません（挑発または攻撃不可）。", "Cannot attack the Castle / enemy leader now (taunt or attacker cannot act).");
        put("msg.passDevice", "请将设备交给【%s】，点击确定后开始该玩家回合。", "端末を【%s】に渡してください。OKでそのプレイヤーのターンを開始します。", "Pass the device to [%s]. Press OK to start that player's turn.");
        put("msg.playerSwitch", "玩家交替", "プレイヤー交替", "Player Switch");
        put("msg.autoAmbush", "%s 没有可盖放的伏击，自动进入行动阶段", "%s はセットできる伏撃がないため、自動で行動フェイズに進みます。", "%s has no ambushes to set and automatically proceeds to Action Phase.");
        put("msg.gameOver", "对局结束！胜者：%s\n原因：%s", "対戦終了！勝者：%s\n理由：%s", "Game over! Winner: %s\nReason: %s");
        put("msg.gameOverTitle", "对局结束", "対戦終了", "Game Over");
        put("msg.notice", "提示", "注意", "Notice");
        put("msg.cannotUnitAttack", "该单位当前无法攻击（召唤回合/攻击次数已用尽/吟唱中）", "このユニットは現在攻撃できません（召喚ターン/攻撃回数使用済み/詠唱中）。", "This unit cannot attack now (summoning turn / attacks used / chanting).");
        put("msg.chooseAttacker", "请先在己方战场选择攻击者", "先に自分の戦場で攻撃者を選択してください。", "Select an attacker on your field first.");
        put("msg.illegalTarget", "该目标不可被攻击（嘲讽限制或目标非法）", "その対象は攻撃できません（挑発制限または不正な対象）。", "That target cannot be attacked (taunt restriction or illegal target).");
        put("msg.cannotSet", "无法盖放：", "セット不可：", "Cannot set: ");
        put("msg.cannotPlay", "无法打出：", "使用不可：", "Cannot play: ");
        put("msg.fizzleTitle", "空发确认", "不発の確認", "Fizzle Confirmation");
        put("msg.fizzleConfirm", "惩罚值 %d 超过对方卡组余量 %d：\n此牌将【空发】——计为使用、消耗词条、效果不结算、对方不抽牌，\n并强制进入弃牌阶段结束你的回合。确定打出？", "ペナルティ値 %d が相手の残りデッキ %d を超えています。\nこのカードは【不発】になります——使用済み扱い、タグを消費、効果は解決せず、相手はドローしません。\nその後、捨て札フェイズに入りターンを終了します。使用しますか？", "Punish value %d exceeds the opponent's remaining deck size %d.\nThis card will FIZZLE: it counts as used, consumes tags, resolves no effect, and the opponent draws no cards.\nYour turn then moves to Discard Phase. Play it anyway?");

        put("doc.i18n", "多语言与显示设置", "多言語と表示設定", "Localization and Display Settings");
    }

    private static void put(String key, String zh, String ja, String en) { TEXT.put(key, new String[]{zh, ja, en}); }

    public static String t(String key) {
        String[] arr = TEXT.get(key);
        if (arr == null) return key;
        return switch (UiSettings.language) {
            case ZH -> arr[0];
            case JA -> arr[1];
            case EN -> arr[2];
        };
    }

    public static String f(String key, Object... args) { return String.format(t(key), args); }

    public static String phase(Game.Phase ph) {
        return switch (ph) {
            case START -> t("phase.start");
            case AMBUSH -> t("phase.ambush");
            case ACTION -> t("phase.action");
            case DISCARD -> t("phase.discard");
            case END -> t("phase.end");
            default -> t("phase.gameover");
        };
    }

    public static String type(CardDef.CardType t) {
        return switch (t) {
            case MINION -> t("type.minion");
            case SPELL -> t("type.spell");
            case AMBUSH -> t("type.ambush");
            case PUNISH -> t("type.punish");
        };
    }

    public static String ambushKind(CardDef.AmbushKind k) {
        return switch (k) {
            case NORMAL -> t("ambush.normal");
            case FOCUS -> t("ambush.focus");
            case LOCKDOWN -> t("ambush.lockdown");
        };
    }
}
