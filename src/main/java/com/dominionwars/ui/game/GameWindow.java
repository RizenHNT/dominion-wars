package com.dominionwars.ui.game;

import com.dominionwars.ai.AiAgent;
import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerState;
import com.dominionwars.model.CardDef;
import com.dominionwars.ui.i18n.I18n;
import com.dominionwars.ui.settings.SettingsDialog;
import com.dominionwars.ui.settings.UiSettings;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import java.awt.*;
import java.awt.event.MouseAdapter;
import java.awt.event.MouseEvent;
import java.util.List;

/**
 * 对战窗口（人机 / 双人同屏）—— 图形卡牌界面版。
 *
 * 视觉：暗色牌桌、全自绘卡牌（CardPanel）、共享王城 HUD、玩家信息板、状态光效。
 * 交互沿用 v7：手牌点击=盖放/打出（含空发确认）、己方单位点击=选攻击者、
 * 敌方单位点击=攻击、按钮攻击王城/统领、伏击阶段无操作时自动推进。
 * 引擎决策回调（惩罚发动/选目标/弃牌/伏击响应）经 SwingHumanAgent 模态弹窗完成。
 */
public class GameWindow extends JFrame {

    private final Game game;
    private final boolean[] human;
    private final boolean hotseat;
    private int shownFor = -1;

    private CardInstance selectedAttacker = null;
    private boolean gameOverShown = false;

    // ---- 行动播放：AI 分步计时器 / 中央横幅 / 受击闪烁 ----
    private javax.swing.Timer aiStepTimer;
    private boolean aiStepping = false;   // 防模态对话框事件泵重入
    private javax.swing.Timer bannerTimer;
    private javax.swing.Timer flashTimer;
    private final JLabel banner = new JLabel("", SwingConstants.CENTER);
    private final java.util.Set<String> recentlyHit = new java.util.HashSet<>();
    private int lastLogSize = 0;

    private final JLabel phaseLabel = new JLabel(" ");
    private final CastlePanel castlePanel;
    private final JTextArea logArea = new JTextArea();
    private final PlayerPlate oppPlate;
    private final PlayerPlate myPlate;
    private final JPanel oppField = cardRow();
    private final JPanel myField = cardRow();
    private final JPanel handPanel = cardRow();
    private final JButton btnSkipAmbush = boardButton(I18n.t("btn.skipAmbush"));
    private final JButton btnAttackFace = boardButton(I18n.t("btn.attackCore"));
    private final JButton btnEndTurn = boardButton(I18n.t("btn.endTurn"));
    private final JButton btnSettings = boardButton(I18n.t("btn.settings"));

    public GameWindow(Game game, boolean humanA, boolean humanB) {
        super(I18n.t("app.duelTitle"));
        this.game = game;
        this.human = new boolean[]{humanA, humanB};
        this.hotseat = humanA && humanB;
        this.castlePanel = new CastlePanel(game);
        this.oppPlate = new PlayerPlate(false);
        this.myPlate = new PlayerPlate(true);

        setDefaultCloseOperation(DISPOSE_ON_CLOSE);
        setSize(1480, 960);
        setMinimumSize(new Dimension(1220, 820));
        setLocationRelativeTo(null);
        buildUi();

        game.logListener = s -> {
            logArea.append(s + "\n");
            logArea.setCaretPosition(logArea.getDocument().getLength());
        };
        for (String s : game.logs) logArea.append(s + "\n");
    }

    // ================= 布局 =================

    private static JPanel cardRow() {
        JPanel p = new JPanel(new FlowLayout(FlowLayout.CENTER, 8, 6));
        p.setOpaque(false);
        return p;
    }

    private static JButton boardButton(String text) {
        JButton b = new JButton(text);
        b.setFocusPainted(false);
        b.setForeground(Theme.TEXT_MAIN);
        b.setBackground(new Color(0x2c, 0x33, 0x44));
        b.setBorder(BorderFactory.createCompoundBorder(
                BorderFactory.createLineBorder(new Color(0x4a, 0x54, 0x6b), 1, true),
                new EmptyBorder(7, 16, 7, 16)));
        b.setFont(Theme.font(Font.BOLD, 13f));
        return b;
    }

    private JScrollPane zone(JComponent inner, String title, int height) {
        JScrollPane sp = new JScrollPane(inner,
                ScrollPaneConstants.VERTICAL_SCROLLBAR_NEVER,
                ScrollPaneConstants.HORIZONTAL_SCROLLBAR_AS_NEEDED);
        sp.setOpaque(false);
        sp.getViewport().setOpaque(false);
        sp.setBorder(BorderFactory.createTitledBorder(
                BorderFactory.createLineBorder(Theme.ZONE_LINE, 1, true),
                title, 0, 0, Theme.font(Font.PLAIN, 12f), Theme.TEXT_DIM));
        sp.setPreferredSize(new Dimension(100, height));
        sp.getHorizontalScrollBar().setUnitIncrement(24);
        sp.getHorizontalScrollBar().setPreferredSize(new Dimension(0, 8));
        return sp;
    }

    private void buildUi() {
        JPanel root = new JPanel(new BorderLayout(10, 6)) {
            @Override protected void paintComponent(Graphics g) {
                super.paintComponent(g);
                Theme.paintBoard((Graphics2D) g, getWidth(), getHeight());
            }
        };
        root.setBorder(new EmptyBorder(8, 12, 8, 12));
        setContentPane(root);

        // ---- 顶栏：阶段 + 王城 + 设置 ----
        JPanel top = new JPanel(new BorderLayout(10, 0));
        top.setOpaque(false);
        phaseLabel.setFont(Theme.font(Font.BOLD, 14f));
        phaseLabel.setForeground(Theme.TEXT_MAIN);
        phaseLabel.setBorder(BorderFactory.createCompoundBorder(
                BorderFactory.createLineBorder(Theme.ZONE_LINE, 1, true),
                new EmptyBorder(6, 14, 6, 14)));
        phaseLabel.setOpaque(true);
        phaseLabel.setBackground(new Color(255, 255, 255, 16));
        top.add(phaseLabel, BorderLayout.WEST);
        JPanel castleWrap = new JPanel(new GridBagLayout());
        castleWrap.setOpaque(false);
        castlePanel.setPreferredSize(new Dimension(420, 50));
        castleWrap.add(castlePanel);
        top.add(castleWrap, BorderLayout.CENTER);
        top.add(btnSettings, BorderLayout.EAST);
        root.add(top, BorderLayout.NORTH);

        // ---- 右侧：日志 ----
        logArea.setEditable(false);
        logArea.setLineWrap(true);
        logArea.setWrapStyleWord(true);
        logArea.setFont(Theme.font(Font.PLAIN, 12f));
        logArea.setForeground(new Color(0xc6, 0xc2, 0xb4));
        logArea.setBackground(new Color(0x14, 0x17, 0x21));
        logArea.setBorder(new EmptyBorder(6, 8, 6, 8));
        JScrollPane logScroll = new JScrollPane(logArea);
        logScroll.setBorder(BorderFactory.createTitledBorder(
                BorderFactory.createLineBorder(Theme.ZONE_LINE, 1, true),
                I18n.t("zone.log"), 0, 0, Theme.font(Font.PLAIN, 12f), Theme.TEXT_DIM));
        logScroll.setOpaque(false);
        logScroll.getViewport().setOpaque(true);
        logScroll.getViewport().setBackground(new Color(0x14, 0x17, 0x21));
        logScroll.setPreferredSize(new Dimension(280, 100));
        root.add(logScroll, BorderLayout.EAST);

        // ---- 中央牌桌 ----
        JPanel board = new JPanel();
        board.setOpaque(false);
        board.setLayout(new BoxLayout(board, BoxLayout.Y_AXIS));

        board.add(oppPlate);
        board.add(zone(oppField, I18n.t("zone.opponent"), UiSettings.fieldCardHeight() + 62));
        board.add(Box.createVerticalStrut(2));
        board.add(zone(myField, I18n.t("zone.self"), UiSettings.fieldCardHeight() + 62));
        board.add(myPlate);
        board.add(zone(handPanel, I18n.t("zone.hand"), UiSettings.cardHeight() + 72));

        JPanel buttons = new JPanel(new FlowLayout(FlowLayout.CENTER, 12, 4));
        buttons.setOpaque(false);
        buttons.add(btnSkipAmbush);
        buttons.add(btnAttackFace);
        buttons.add(btnEndTurn);
        board.add(buttons);

        root.add(board, BorderLayout.CENTER);

        // ---- 按钮事件（沿用 v7 语义） ----
        btnSkipAmbush.addActionListener(e -> {
            if (!humanTurn()) return;
            game.endAmbushPhase();
            afterAction();
        });
        btnEndTurn.addActionListener(e -> {
            if (!humanTurn()) return;
            if (game.phase == Game.Phase.AMBUSH) game.endAmbushPhase();
            game.endTurn();
            afterAction();
        });
        btnSettings.addActionListener(e -> showSettings());
        // 行动横幅：盖在窗口上层中央，短暂显示"谁做了什么"
        banner.setFont(Theme.font(Font.BOLD, 17f));
        banner.setForeground(new Color(0xff, 0xe9, 0xc2));
        banner.setOpaque(true);
        banner.setBackground(new Color(20, 16, 10, 215));
        banner.setBorder(BorderFactory.createCompoundBorder(
                BorderFactory.createLineBorder(Theme.alpha(Theme.GOLD, 200), 2, true),
                new EmptyBorder(10, 26, 10, 26)));
        banner.setVisible(false);
        JPanel glass = new JPanel(null) {
            @Override public boolean contains(int x, int y) { return false; } // 不挡鼠标
        };
        glass.setOpaque(false);
        glass.add(banner);
        setGlassPane(glass);
        glass.setVisible(true);

        btnAttackFace.addActionListener(e -> {
            if (!humanTurn()) return;
            if (selectedAttacker == null) { toast(I18n.t("msg.needAttacker")); return; }
            if (!game.canAttackFace(selectedAttacker)) { toast(I18n.t("msg.cannotAttackCore")); return; }
            game.attack(selectedAttacker, null);
            selectedAttacker = null;
            afterAction();
        });
    }

    // ================= 驱动（与 v7 一致） =================

    public void begin() {
        game.start();
        lastLogSize = game.logs.size();
        if (hotseat) shownFor = game.currentIdx;
        pump();
    }

    private void pump() {
        if (!game.over() && !human[game.currentIdx]) {
            startAiPlayback();
            return;
        }
        afterPump();
    }

    /** AI 分步播放：每 650ms 执行一个最小动作，配合横幅与闪烁让玩家看清过程 */
    private void startAiPlayback() {
        if (aiStepTimer != null && aiStepTimer.isRunning()) return;
        lastLogSize = game.logs.size();
        aiStepTimer = new javax.swing.Timer(650, e -> {
            if (aiStepping) return;
            if (game.over() || human[game.currentIdx]) {
                aiStepTimer.stop();
                afterPump();
                return;
            }
            Object agent = game.agentOf(game.currentIdx);
            boolean more;
            aiStepping = true;
            try {
                if (agent instanceof AiAgent ai) more = ai.playOneStep(game);
                else { game.endTurn(); more = false; }
            } finally {
                aiStepping = false;
            }
            announceNewLogs();
            refresh();
            if (game.over() || (!more && human[game.currentIdx])) {
                aiStepTimer.stop();
                afterPump();
            }
        });
        aiStepTimer.setInitialDelay(350);
        aiStepTimer.start();
    }

    /** 从新增日志中提取播报与受击信息 */
    private void announceNewLogs() {
        java.util.List<String> logs = game.logs;
        String announce = null;
        for (int i = lastLogSize; i < logs.size(); i++) {
            String ln = logs.get(i);
            // 受击闪烁：【名字】受到 X 点伤害
            int a = ln.indexOf('【'), b = ln.indexOf('】');
            if (a >= 0 && b > a && ln.substring(b).contains("受到") && ln.contains("点伤害")) {
                recentlyHit.add(ln.substring(a + 1, b));
            }
            // 选最有信息量的一条做横幅：使用/召唤/发动/攻击/弃置/破城/触发
            if (announce == null || ln.contains("使用") || ln.contains("发动") || ln.contains("召唤")
                    || ln.contains("攻击") || ln.contains("王城") || ln.contains("触发")) {
                if (!ln.startsWith("——")) announce = ln;
            }
        }
        lastLogSize = logs.size();
        if (announce != null) showBanner(announce);
        if (!recentlyHit.isEmpty()) {
            if (flashTimer != null) flashTimer.stop();
            flashTimer = new javax.swing.Timer(900, ev -> { recentlyHit.clear(); refresh(); });
            flashTimer.setRepeats(false);
            flashTimer.start();
        }
    }

    private void showBanner(String text) {
        banner.setText(text);
        Dimension d = banner.getPreferredSize();
        int bw = Math.min(d.width, getWidth() - 80);
        banner.setBounds((getWidth() - bw) / 2, (int) (getHeight() * 0.36), bw, d.height);
        banner.setVisible(true);
        if (bannerTimer != null) bannerTimer.stop();
        bannerTimer = new javax.swing.Timer(1400, ev -> banner.setVisible(false));
        bannerTimer.setRepeats(false);
        bannerTimer.start();
    }

    private void afterPump() {
        if (hotseat && !game.over() && game.currentIdx != shownFor) {
            shownFor = game.currentIdx;
            refresh();
            JOptionPane.showMessageDialog(this,
                    I18n.f("msg.passDevice", game.players[game.currentIdx].name),
                    I18n.t("msg.playerSwitch"), JOptionPane.INFORMATION_MESSAGE);
        }
        autoAdvanceHumanAmbushIfNoMoves();
        refresh();
        maybeShowGameOver();
    }

    private void afterAction() {
        selectedAttacker = null;
        announceNewLogs();
        autoAdvanceHumanAmbushIfNoMoves();
        refresh();
        if (!game.over() && !human[game.currentIdx]) pump();
        else maybeShowGameOver();
    }

    private boolean autoAdvanceHumanAmbushIfNoMoves() {
        if (game.over() || !human[game.currentIdx] || game.phase != Game.Phase.AMBUSH) return false;
        PlayerState p = game.current();
        for (CardInstance c : p.hand) {
            if (game.whyCannotSetAmbush(c) == null) return false;
        }
        game.log(I18n.f("msg.autoAmbush", p.name));
        game.endAmbushPhase();
        return true;
    }

    private boolean humanTurn() {
        return !game.over() && human[game.currentIdx];
    }

    private void maybeShowGameOver() {
        if (game.over() && !gameOverShown) {
            gameOverShown = true;
            refresh();
            JOptionPane.showMessageDialog(this,
                    I18n.f("msg.gameOver", game.players[game.winner].name, game.winReason),
                    I18n.t("msg.gameOverTitle"), JOptionPane.INFORMATION_MESSAGE);
        }
    }

    private void toast(String msg) {
        JOptionPane.showMessageDialog(this, msg, I18n.t("msg.notice"), JOptionPane.WARNING_MESSAGE);
    }

    // ================= 渲染 =================

    private void refresh() {
        int me = viewIdx();
        PlayerState my = game.players[me];
        PlayerState op = game.players[1 - me];

        phaseLabel.setText(I18n.f("phase.turn", game.turnNumber)
                + " ｜ " + I18n.t("phase.current") + game.players[game.currentIdx].name
                + " ｜ " + I18n.t("phase.phase") + I18n.phase(game.phase)
                + (game.over() ? (" ｜ " + I18n.t("phase.winner") + game.players[game.winner].name) : ""));

        oppPlate.update(op);
        myPlate.update(my);
        fillField(oppField, op, false);
        fillField(myField, my, true);
        fillHand(my);

        boolean myTurn = humanTurn() && game.currentIdx == me;
        btnSkipAmbush.setEnabled(myTurn && game.phase == Game.Phase.AMBUSH);
        btnAttackFace.setEnabled(myTurn && game.phase == Game.Phase.ACTION);
        btnEndTurn.setEnabled(myTurn && (game.phase == Game.Phase.AMBUSH || game.phase == Game.Phase.ACTION));

        castlePanel.repaint();
        revalidate();
        repaint();
    }

    private int viewIdx() {
        if (hotseat) return game.currentIdx;
        if (human[0]) return 0;
        if (human[1]) return 1;
        return 0;
    }

    private void fillField(JPanel panel, PlayerState p, boolean self) {
        panel.removeAll();
        if (p.leaderOnField != null && !p.leaderOnField.def.isMinion()
                && !p.ambushes.contains(p.leaderOnField)) {
            panel.add(fieldCard(p.leaderOnField, self, true));
        }
        for (CardInstance c : p.field) panel.add(fieldCard(c, self, false));
        if (panel.getComponentCount() == 0) panel.add(emptyLabel(I18n.t("zone.empty")));
        appendAmbushes(panel, p, self);
    }

    /** 伏击牌内联显示在战场行尾（小尺寸；对手视角为卡背） */
    private void appendAmbushes(JPanel panel, PlayerState p, boolean self) {
        if (p.ambushes.isEmpty()) return;
        boolean reveal = self || (!human[0] && !human[1]);
        if (!reveal && !UiSettings.showOpponentAmbushCount) return;
        JLabel sep = new JLabel("◇ " + I18n.t("info.ambushZone"));
        sep.setForeground(Theme.TEXT_DIM);
        sep.setFont(Theme.font(Font.PLAIN, 11f));
        sep.setBorder(new EmptyBorder(UiSettings.fieldCardHeight() / 2 - 10, 14, 0, 2));
        panel.add(sep);
        int aw = (int) (UiSettings.fieldCardWidth() * 0.62);
        int ah = (int) (UiSettings.fieldCardHeight() * 0.62);
        for (CardInstance c : p.ambushes) {
            if (reveal) {
                CardPanel cp = new CardPanel(c, game.effectivePunish(c.ownerIdx, c, false)).size(aw, ah);
                if (UiSettings.showTooltips) cp.setToolTipText(tooltip(c.def, c));
                panel.add(cp);
            } else {
                CardPanel back = CardPanel.back().size(aw, ah);
                if (UiSettings.showTooltips) back.setToolTipText(I18n.t("zone.hiddenAmbushTip"));
                panel.add(back);
            }
        }
    }

    private JLabel emptyLabel(String s) {
        JLabel l = new JLabel(s);
        l.setForeground(Theme.TEXT_DIM);
        l.setFont(Theme.font(Font.PLAIN, 12f));
        l.setBorder(new EmptyBorder(UiSettings.fieldCardHeight() / 2 - 10, 16, 0, 16));
        return l;
    }

    private CardPanel fieldCard(CardInstance c, boolean self, boolean leaderZone) {
        boolean myTurn = humanTurn() && game.phase == Game.Phase.ACTION;
        boolean canAttack = myTurn && self && c.canAttackNow();
        boolean canBeTarget = myTurn && !self && selectedAttacker != null
                && game.legalAttackTargets(selectedAttacker).contains(c);

        CardPanel cp = new CardPanel(c, game.effectivePunish(c.ownerIdx, c, false))
                .size(UiSettings.fieldCardWidth(), UiSettings.fieldCardHeight())
                .hit(recentlyHit.contains(c.def.name));
        if (c == selectedAttacker) cp.glow(CardPanel.Glow.SELECTED).status(I18n.t("status.selected"));
        else if (canBeTarget) cp.glow(CardPanel.Glow.TARGET).status(I18n.t("status.target"));
        else if (canAttack) cp.glow(CardPanel.Glow.USABLE).status(I18n.t("status.canAttack"));
        if (UiSettings.showTooltips) cp.setToolTipText(tooltip(c.def, c));

        if (!leaderZone) {
            cp.addMouseListener(new MouseAdapter() {
                @Override public void mouseClicked(MouseEvent e) {
                    if (!humanTurn() || game.phase != Game.Phase.ACTION) return;
                    if (self) {
                        if (!c.def.isMinion() && !c.isLeaderEntity) return;
                        if (!c.canAttackNow()) { toast(I18n.t("msg.cannotUnitAttack")); return; }
                        selectedAttacker = (selectedAttacker == c) ? null : c;
                        refresh();
                    } else {
                        if (selectedAttacker == null) { toast(I18n.t("msg.chooseAttacker")); return; }
                        List<CardInstance> legal = game.legalAttackTargets(selectedAttacker);
                        if (!legal.contains(c)) { toast(I18n.t("msg.illegalTarget")); return; }
                        game.attack(selectedAttacker, c);
                        selectedAttacker = null;
                        afterAction();
                    }
                }
            });
            cp.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        }
        return cp;
    }


    private void fillHand(PlayerState p) {
        handPanel.removeAll();
        boolean myTurn = humanTurn() && game.currentIdx == p.idx;
        for (CardInstance c : p.hand) {
            String why = handUnavailableReason(c, p, myTurn);
            boolean usable = why == null;
            int punish = game.effectivePunish(p.idx, c, c.punishActivated);
            CardPanel cp = new CardPanel(c, punish)
                    .size(UiSettings.cardWidth(), UiSettings.cardHeight())
                    .dim(!usable);
            if (usable) cp.glow(CardPanel.Glow.USABLE);
            if (UiSettings.showTooltips) {
                cp.setToolTipText("<html>" + (usable ? I18n.t("status.currentUsable")
                        : I18n.t("status.unusableBecause") + esc(why))
                        + "<br><hr>" + tooltipBody(c.def, c) + "</html>");
            }
            cp.addMouseListener(new MouseAdapter() {
                @Override public void mouseClicked(MouseEvent e) {
                    if (!humanTurn()) return;
                    if (game.phase == Game.Phase.AMBUSH) {
                        String w = game.whyCannotSetAmbush(c);
                        if (w != null) { toast(I18n.t("msg.cannotSet") + w); return; }
                        game.setAmbush(c);
                        afterAction();
                    } else if (game.phase == Game.Phase.ACTION) {
                        String w = game.whyCannotPlay(c);
                        if (w != null) { toast(I18n.t("msg.cannotPlay") + w); return; }
                        int ep = game.effectivePunish(p.idx, c, false);
                        PlayerState opp = game.players[1 - p.idx];
                        if (ep > opp.deck.size()) {
                            int r = JOptionPane.showConfirmDialog(GameWindow.this,
                                    I18n.f("msg.fizzleConfirm", ep, opp.deck.size()),
                                    I18n.t("msg.fizzleTitle"), JOptionPane.OK_CANCEL_OPTION, JOptionPane.WARNING_MESSAGE);
                            if (r != JOptionPane.OK_OPTION) return;
                        }
                        game.playFromHand(c);
                        afterAction();
                    }
                }
            });
            cp.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
            handPanel.add(cp);
        }
        if (p.hand.isEmpty()) handPanel.add(emptyLabel(I18n.t("zone.noHand")));
    }

    private String handUnavailableReason(CardInstance c, PlayerState p, boolean myTurn) {
        if (!myTurn) return I18n.t("status.unusable");
        if (game.phase == Game.Phase.AMBUSH) return game.whyCannotSetAmbush(c);
        if (game.phase == Game.Phase.ACTION) return game.whyCannotPlay(c);
        return I18n.t("status.unusable");
    }

    // ================= 玩家信息板 =================

    private class PlayerPlate extends JComponent {
        private final boolean self;
        private PlayerState p;

        PlayerPlate(boolean self) {
            this.self = self;
            setPreferredSize(new Dimension(100, 46));
            setMaximumSize(new Dimension(Integer.MAX_VALUE, 46));
        }

        void update(PlayerState p) { this.p = p; repaint(); }

        @Override protected void paintComponent(Graphics g0) {
            if (p == null) return;
            Graphics2D g = (Graphics2D) g0.create();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
            int w = getWidth(), h = getHeight();

            boolean active = !game.over() && game.currentIdx == p.idx;
            Color base = active ? new Color(0x2c, 0x36, 0x4c) : new Color(0x1d, 0x23, 0x32);
            g.setColor(base);
            g.fillRoundRect(2, 2, w - 4, h - 4, 14, 14);
            g.setColor(active ? Theme.alpha(Theme.GOLD, 190) : Theme.ZONE_LINE);
            g.setStroke(new BasicStroke(active ? 1.8f : 1f));
            g.drawRoundRect(2, 2, w - 4, h - 4, 14, 14);

            int x = 14, cy = h / 2;
            // 名字
            g.setFont(Theme.font(Font.BOLD, 14f));
            g.setColor(active ? Theme.GOLD : Theme.TEXT_MAIN);
            String name = p.name;
            g.drawString(name, x, cy + 5);
            x += g.getFontMetrics().stringWidth(name) + 18;

            g.setFont(Theme.font(Font.PLAIN, 12.5f));
            x = stat(g, x, cy, I18n.t("hud.deck"), String.valueOf(p.deck.size()), Theme.TEXT_MAIN);
            x = stat(g, x, cy, I18n.t("hud.handCount"), String.valueOf(p.hand.size()), Theme.TEXT_MAIN);
            x = stat(g, x, cy, I18n.t("hud.grave"), String.valueOf(p.graveyard.size()), Theme.TEXT_DIM);
            if (p.life != null) x = stat(g, x, cy, "♥" + I18n.t("hud.life"), String.valueOf(p.life), new Color(0xe2, 0x6b, 0x6b));

            // 统领
            CardInstance ld = p.leaderOnField;
            String ls;
            Color lc;
            if (ld != null) {
                ls = "★ " + ld.def.name;
                if (ld.def.isMinion()) ls += " " + ld.attack + "/" + ld.health;
                else if (ld.durability > 0) ls += " " + I18n.t("hud.durability") + ld.durability;
                if (p.leaderDisabled) ls += "（" + I18n.t("hud.suppressed") + "）";
                lc = Theme.GOLD;
            } else {
                ls = "★ " + I18n.t("hud.leaderAbsent");
                lc = Theme.TEXT_DIM;
            }
            g.setColor(lc);
            g.drawString(ls, x, cy + 5);
            x += g.getFontMetrics().stringWidth(ls) + 18;

            // 统领胜利条件进度（公开信息：双方都可见，临近达成时红色提醒）
            String wp = winProgress(p);
            if (wp != null) {
                boolean close = wp.endsWith("!");
                if (close) wp = wp.substring(0, wp.length() - 1);
                g.setColor(close ? new Color(0xff, 0x7d, 0x6b) : new Color(0x9f, 0xd0, 0x9a));
                g.drawString(wp, x, cy + 5);
                x += g.getFontMetrics().stringWidth(wp) + 18;
            }

            // 胜利计数：10 颗刻度
            int total = game.balance.reshuffleLoseAt;
            int filled = Math.min(total, p.cycleWinCount);
            String vc = I18n.t("hud.victory");
            g.setColor(Theme.TEXT_DIM);
            g.drawString(vc, x, cy + 5);
            x += g.getFontMetrics().stringWidth(vc) + 8;
            int r = 5;
            for (int i = 0; i < total; i++) {
                if (x + r * 2 > w - 10) break;
                if (i < filled) {
                    g.setColor(new Color(0x6f, 0xb7, 0xff));
                    g.fillOval(x, cy - r + 1, r * 2, r * 2);
                } else {
                    g.setColor(Theme.alpha(Color.WHITE, 50));
                    g.drawOval(x, cy - r + 1, r * 2, r * 2);
                }
                x += r * 2 + 3;
            }
            g.dispose();
        }

        private int stat(Graphics2D g, int x, int cy, String label, String value, Color valueColor) {
            g.setColor(Theme.TEXT_DIM);
            g.drawString(label, x, cy + 5);
            x += g.getFontMetrics().stringWidth(label) + 5;
            g.setColor(valueColor);
            Font old = g.getFont();
            g.setFont(Theme.font(Font.BOLD, 13f));
            g.drawString(value, x, cy + 5);
            x += g.getFontMetrics().stringWidth(value) + 16;
            g.setFont(old);
            return x;
        }
    }

    /** 统领特殊胜利进度文本；接近达成（≥2/3）时末尾加"!"标记 */
    private String winProgress(PlayerState p) {
        CardInstance leader = game.findLeaderAnywhere(p);
        if (leader == null || leader.def.leaderDef == null) return null;
        var ld = leader.def.leaderDef;
        PlayerState opp = game.players[1 - p.idx];
        int cur, max;
        String label = I18n.t("hud.goal");
        switch (ld.winCondition == null ? "" : ld.winCondition) {
            case "OPP_DISCARD_TOTAL_GE": cur = opp.totalDiscarded; max = ld.winParam; break;
            case "NO_DAMAGE_TURNS_GE": cur = p.noDamageTurns; max = ld.winParam; break;
            case "OPP_PUNISH_DRAW_TURN_GE":
                cur = opp.punishDrawnThisTurn; max = ld.winParam;
                label = label + "(" + I18n.t("hud.goalTurn") + ")";
                break;
            case "ROYAL_CASTLE_BREAK":
                if (!game.balance.royalCastleEnabled) return null;
                cur = game.royalCastleMaxHp - Math.max(0, game.royalCastleHp); max = game.royalCastleMaxHp;
                break;
            default: return null;
        }
        String txt = label + " " + cur + "/" + max;
        return cur * 3 >= max * 2 ? txt + "!" : txt;
    }

    // ================= Tooltip（沿用 v7 内容） =================

    private String tooltipBody(CardDef d, CardInstance inst) {
        String t = tooltip(d, inst);
        if (t.startsWith("<html>")) t = t.substring(6);
        if (t.endsWith("</html>")) t = t.substring(0, t.length() - 7);
        return t;
    }

    private String tooltip(CardDef d, CardInstance inst) {
        StringBuilder sb = new StringBuilder("<html><b>").append(esc(d.name)).append("</b>");
        sb.append("（").append(I18n.type(d.type)).append("，").append(esc(d.faction)).append("）<br>");
        sb.append(I18n.t("kw.punish")).append(" ").append(d.punish);
        if (!d.tags.isEmpty()) sb.append(" ｜ ").append(I18n.t("kw.tags")).append("：").append(esc(String.join("、", d.tags)));
        if (!d.keywords.isEmpty()) sb.append(" ｜ ").append(esc(String.join(" ", d.keywords)));
        if (d.guard) sb.append(" ｜ ").append(I18n.t("kw.guard"));
        if (d.kingSlayer) sb.append(" ｜ ").append(I18n.t("kw.kingSlayer"));
        sb.append("<br>");
        if (d.isMinion()) sb.append(I18n.t("kw.atkHp")).append(" ").append(d.attack).append("/").append(d.health).append("<br>");
        if (d.chant > 0) sb.append(I18n.t("info.chant")).append(" ").append(d.chant).append("<br>");
        if (d.type == CardDef.CardType.AMBUSH) sb.append(I18n.t("kw.ambushKind")).append("：").append(I18n.ambushKind(d.ambushKind)).append("<br>");
        if (d.punishActivatable) sb.append("【").append(I18n.t("kw.punish")).append("】").append(d.punishCost).append("<br>");
        if (d.leader) sb.append("【").append(I18n.t("kw.leader")).append("】").append(esc(d.leaderDef.winText)).append("<br>");
        if (!d.text.isEmpty()) sb.append(esc(d.text).replace("\n", "<br>")).append("<br>");
        if (!d.flavor.isEmpty()) sb.append("<i><font color='#888888'>").append(esc(d.flavor)).append("</font></i>");
        sb.append("</html>");
        return sb.toString();
    }

    private static String esc(String s) {
        return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
    }

    private void showSettings() {
        SettingsDialog d = new SettingsDialog(this);
        d.setVisible(true);
        if (d.changed()) {
            setTitle(I18n.t("app.duelTitle"));
            btnSkipAmbush.setText(I18n.t("btn.skipAmbush"));
            btnAttackFace.setText(I18n.t("btn.attackCore"));
            btnEndTurn.setText(I18n.t("btn.endTurn"));
            btnSettings.setText(I18n.t("btn.settings"));
            refresh();
        }
    }

    /** AI 回合执行的小工具，与 UI 解耦便于测试 */
    static class PlayerAgentRunner {
        static void runAiTurn(Game g) {
            Object agent = g.agentOf(g.currentIdx);
            if (agent instanceof AiAgent ai) ai.playTurn(g);
            else g.endTurn();
        }
    }
}
