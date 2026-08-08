package com.dominionwars.ui;

import com.dominionwars.ai.AiAgent;
import com.dominionwars.data.Balance;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerAgent;
import com.dominionwars.model.CardDef;
import com.dominionwars.ui.editor.EditorWindow;
import com.dominionwars.ui.game.GameWindow;
import com.dominionwars.ui.i18n.I18n;
import com.dominionwars.ui.settings.SettingsDialog;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import java.awt.*;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/** 启动器：选择卡组与模式，进入对战 / 编辑器 / 规则。 */
public class MainMenu extends JFrame {

    private final Path dataDir;
    private final Balance balance;
    private CardLibrary library;

    private final JComboBox<DeckItem> deckA = new JComboBox<>();
    private final JComboBox<DeckItem> deckB = new JComboBox<>();

    private record DeckItem(Path path, CardLibrary.DeckDef def) {
        @Override public String toString() { return def.name + "（" + def.faction + "）"; }
    }

    public MainMenu(Path dataDir) {
        super(I18n.t("app.title"));
        this.dataDir = dataDir;
        this.balance = Balance.load(dataDir.resolve("balance.json"));
        this.library = CardLibrary.load(dataDir.resolve("cards"));

        setDefaultCloseOperation(EXIT_ON_CLOSE);
        setSize(620, 470);
        setLocationRelativeTo(null);
        buildUi();
        reloadDecks();
    }

    private void buildUi() {
        JPanel root = new JPanel() {
            @Override protected void paintComponent(Graphics g) {
                super.paintComponent(g);
                com.dominionwars.ui.game.Theme.paintBoard((Graphics2D) g, getWidth(), getHeight());
            }
        };
        root.setLayout(new BoxLayout(root, BoxLayout.Y_AXIS));
        root.setBorder(new EmptyBorder(18, 28, 18, 28));
        setContentPane(root);

        JLabel title = new JLabel(I18n.t("app.title"), SwingConstants.CENTER);
        title.setFont(new Font(com.dominionwars.ui.game.Theme.cjk(), Font.BOLD, 32));
        title.setForeground(com.dominionwars.ui.game.Theme.GOLD);
        title.setAlignmentX(CENTER_ALIGNMENT);
        JLabel sub = new JLabel(I18n.t("app.subtitle"), SwingConstants.CENTER);
        sub.setAlignmentX(CENTER_ALIGNMENT);
        sub.setForeground(new Color(0x9a, 0x97, 0x8e));
        root.add(title);
        root.add(Box.createVerticalStrut(4));
        root.add(sub);
        root.add(Box.createVerticalStrut(18));

        root.add(rowOf(I18n.t("menu.playerADeck"), deckA));
        root.add(Box.createVerticalStrut(6));
        root.add(rowOf(I18n.t("menu.playerBDeck"), deckB));
        root.add(Box.createVerticalStrut(18));

        JButton bPvE = big(I18n.t("menu.pve"));
        JButton bPvP = big(I18n.t("menu.pvp"));
        JButton bAiAi = big(I18n.t("menu.aiai"));
        JButton bEditor = big(I18n.t("menu.editor"));
        JButton bRules = big(I18n.t("menu.rules"));
        JButton bSettings = big(I18n.t("menu.settings"));
        for (JButton b : List.of(bPvE, bPvP, bAiAi, bEditor, bRules, bSettings)) {
            root.add(b);
            root.add(Box.createVerticalStrut(8));
        }

        bPvE.addActionListener(e -> launchGame(true, false));
        bPvP.addActionListener(e -> launchGame(true, true));
        bAiAi.addActionListener(e -> launchGame(false, false));
        bEditor.addActionListener(e -> {
            EditorWindow w = new EditorWindow(dataDir.resolve("cards"));
            w.setVisible(true);
        });
        bRules.addActionListener(e -> showRules());
        bSettings.addActionListener(e -> showSettings());
    }

    private JPanel rowOf(String label, JComponent c) {
        JPanel p = new JPanel(new BorderLayout(8, 0));
        p.setOpaque(false);
        p.setMaximumSize(new Dimension(Integer.MAX_VALUE, 30));
        JLabel l = new JLabel(label);
        l.setForeground(new Color(0xc6, 0xc2, 0xb4));
        p.add(l, BorderLayout.WEST);
        p.add(c, BorderLayout.CENTER);
        return p;
    }

    private JButton big(String text) {
        JButton b = new JButton(text);
        b.setAlignmentX(CENTER_ALIGNMENT);
        b.setMaximumSize(new Dimension(Integer.MAX_VALUE, 38));
        b.setFocusPainted(false);
        b.setForeground(new Color(0xe8, 0xe4, 0xd8));
        b.setBackground(new Color(0x2c, 0x33, 0x44));
        b.setFont(new Font(com.dominionwars.ui.game.Theme.cjk(), Font.BOLD, 14));
        b.setBorder(BorderFactory.createCompoundBorder(
                BorderFactory.createLineBorder(new Color(0x4a, 0x54, 0x6b), 1, true),
                new EmptyBorder(8, 16, 8, 16)));
        return b;
    }

    private void reloadDecks() {
        deckA.removeAllItems();
        deckB.removeAllItems();
        for (Path p : CardLibrary.listDecks(dataDir.resolve("decks"))) {
            try {
                CardLibrary.DeckDef d = CardLibrary.DeckDef.load(p);
                deckA.addItem(new DeckItem(p, d));
                deckB.addItem(new DeckItem(p, d));
            } catch (Exception ex) {
                System.err.println("卡组读取失败 " + p + ": " + ex.getMessage());
            }
        }
        if (deckB.getItemCount() > 1) deckB.setSelectedIndex(1);
    }

    private void launchGame(boolean humanA, boolean humanB) {
        DeckItem a = (DeckItem) deckA.getSelectedItem();
        DeckItem b = (DeckItem) deckB.getSelectedItem();
        if (a == null || b == null) {
            JOptionPane.showMessageDialog(this, I18n.t("menu.deckMissing"));
            return;
        }
        // 每次开局重新加载卡库，编辑器改动即时生效
        library = CardLibrary.load(dataDir.resolve("cards"));
        List<String> problems = new ArrayList<>();
        List<CardDef> da = a.def.build(library, balance, problems);
        List<CardDef> db = b.def.build(library, balance, problems);
        if (!problems.isEmpty()) {
            JOptionPane.showMessageDialog(this, I18n.t("menu.deckProblem") + "\n" + String.join("\n", problems),
                    I18n.t("menu.deckErrorTitle"), JOptionPane.ERROR_MESSAGE);
            return;
        }

        PlayerAgent pa = humanA ? new SwingHumanAgent(null) : new AiAgent();
        PlayerAgent pb = humanB ? new SwingHumanAgent(null) : new AiAgent();

        long seed = System.nanoTime();
        int first = new java.util.Random(seed).nextInt(2);
        Game g = new Game(balance, da, db,
                a.def.name + " (A)", b.def.name + " (B)", pa, pb, seed, first);
        g.library = library;
        GameWindow w = new GameWindow(g, humanA, humanB);
        w.setVisible(true);
        SwingUtilities.invokeLater(w::begin);
    }

    private void showRules() {
        String text;
        Path p = dataDir.getParent() == null ? Path.of("docs/RULES.md") : dataDir.getParent().resolve("docs/RULES.md");
        if (!Files.exists(p)) p = Path.of("docs/RULES.md");
        try {
            text = Files.readString(p);
        } catch (Exception ex) {
            text = I18n.t("rules.missing");
        }
        JTextArea ta = new JTextArea(text, 30, 80);
        ta.setEditable(false);
        ta.setLineWrap(true);
        JScrollPane sp = new JScrollPane(ta);
        sp.setPreferredSize(new Dimension(860, 600));
        JDialog d = new JDialog(this, I18n.t("rules.title"), false);
        d.add(sp);
        d.pack();
        d.setLocationRelativeTo(this);
        d.setVisible(true);
    }
    private void showSettings() {
        SettingsDialog d = new SettingsDialog(this);
        d.setVisible(true);
        if (d.changed()) {
            dispose();
            SwingUtilities.invokeLater(() -> new MainMenu(dataDir).setVisible(true));
        }
    }

}
