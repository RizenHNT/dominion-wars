package com.dominionwars.ui.editor;

import com.dominionwars.data.CardLibrary;
import com.dominionwars.model.CardDef;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import javax.swing.table.DefaultTableModel;
import java.awt.*;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.stream.Collectors;

/**
 * 卡牌编辑器：左侧文件/卡牌列表，右侧分页表单。
 * 完全基于 JSON 数据驱动，未知字段保留在“自定义字段”页中，
 * 因此可以在不改引擎的前提下为将来的新机制预埋数据。
 */
public class EditorWindow extends JFrame {

    private final Path cardsDir;
    private CardLibrary lib;

    private final JComboBox<String> fileBox = new JComboBox<>();
    private final DefaultListModel<CardDef> listModel = new DefaultListModel<>();
    private final JList<CardDef> cardList = new JList<>(listModel);

    // ---- 基本字段 ----
    private final JTextField fId = new JTextField();
    private final JTextField fName = new JTextField();
    private final JTextField fFaction = new JTextField();
    private final JComboBox<CardDef.CardType> fType = new JComboBox<>(CardDef.CardType.values());
    private final JTextField fTags = new JTextField();
    private final JSpinner fPunish = new JSpinner(new SpinnerNumberModel(1, 0, 99, 1));
    private final JTextArea fText = new JTextArea(3, 20);
    private final JTextArea fFlavor = new JTextArea(2, 20);
    private final JSpinner fAttack = new JSpinner(new SpinnerNumberModel(0, 0, 99, 1));
    private final JSpinner fHealth = new JSpinner(new SpinnerNumberModel(0, 0, 99, 1));
    private final JSpinner fAtkPerTurn = new JSpinner(new SpinnerNumberModel(1, 0, 9, 1));
    private final JTextField fKeywords = new JTextField();
    private final JSpinner fChant = new JSpinner(new SpinnerNumberModel(0, 0, 20, 1));
    private final JComboBox<CardDef.AmbushKind> fAmbushKind = new JComboBox<>(CardDef.AmbushKind.values());
    private final JTextField fAmbushTrigger = new JTextField();
    private final JCheckBox fPunishAct = new JCheckBox("具有【惩罚】效果（被惩罚抽到可发动）");
    private final JSpinner fPunishCost = new JSpinner(new SpinnerNumberModel(0, 0, 99, 1));
    private final JTextField fPunishCond = new JTextField();
    private final JCheckBox fLeader = new JCheckBox("统领牌");
    private final JSpinner fGrantLife = new JSpinner(new SpinnerNumberModel(0, 0, 999, 1));
    private final JSpinner fDurability = new JSpinner(new SpinnerNumberModel(0, 0, 999, 1));
    private final JTextField fWinCondition = new JTextField();
    private final JSpinner fWinParam = new JSpinner(new SpinnerNumberModel(0, 0, 999, 1));
    private final JTextField fWinText = new JTextField();

    // ---- 效果页 ----
    private static final String[] EFFECT_GROUPS = {
            "onPlayEffects（打出时）", "chantEffects（吟唱完成）", "ambushEffects（伏击触发）",
            "onOpponentDiscardEffects（对方弃牌时·在场触发）",
            "punishEffects（惩罚发动）", "leaderDef.punishEffects（统领被惩罚抽到）",
            "leaderDef.enterEffects（统领登场）", "leaderDef.persistentEffects（统领永续）"};
    private final JComboBox<String> groupBox = new JComboBox<>(EFFECT_GROUPS);
    private final DefaultTableModel effectModel =
            new DefaultTableModel(new Object[]{"action", "target", "amount", "param"}, 0);
    private final JTable effectTable = new JTable(effectModel);

    // ---- 自定义字段页 ----
    private final DefaultTableModel customModel =
            new DefaultTableModel(new Object[]{"key", "value"}, 0);

    private CardDef editing = null;
    private JPanel previewHost;
    private boolean loadingForm = false;

    public EditorWindow(Path cardsDir) {
        super("统御战纪 - 卡牌编辑器");
        this.cardsDir = cardsDir;
        setDefaultCloseOperation(DISPOSE_ON_CLOSE);
        setSize(1340, 800);
        setLocationRelativeTo(null);
        buildUi();
        reloadLibrary();
    }

    private void buildUi() {
        JPanel root = new JPanel(new BorderLayout(8, 8));
        root.setBorder(new EmptyBorder(8, 8, 8, 8));
        setContentPane(root);

        // ---- 左侧 ----
        JPanel left = new JPanel(new BorderLayout(4, 4));
        left.setPreferredSize(new Dimension(280, 100));
        left.add(fileBox, BorderLayout.NORTH);
        cardList.setCellRenderer((list, v, i, sel, foc) -> {
            JLabel l = new JLabel(v.name + "  (" + v.id + ")" + (v.leader ? " ★" : ""));
            l.setOpaque(true);
            l.setBorder(new EmptyBorder(3, 6, 3, 6));
            l.setBackground(sel ? new Color(0xcc, 0xe2, 0xff) : Color.WHITE);
            return l;
        });
        left.add(new JScrollPane(cardList), BorderLayout.CENTER);
        JPanel listBtns = new JPanel(new GridLayout(1, 3, 4, 0));
        JButton bNew = new JButton("新建");
        JButton bCopy = new JButton("复制");
        JButton bDel = new JButton("删除");
        listBtns.add(bNew); listBtns.add(bCopy); listBtns.add(bDel);
        left.add(listBtns, BorderLayout.SOUTH);
        root.add(left, BorderLayout.WEST);

        // ---- 右侧分页 ----
        JTabbedPane tabs = new JTabbedPane();
        tabs.addTab("基本字段", new JScrollPane(buildBasicForm()));
        tabs.addTab("效果", buildEffectsTab());
        tabs.addTab("自定义字段", buildCustomTab());
        root.add(tabs, BorderLayout.CENTER);

        // ---- 右侧：实时卡面预览 + 卡图管理 ----
        JPanel right = new JPanel(new BorderLayout(6, 6));
        right.setPreferredSize(new Dimension(220, 100));
        JPanel pvWrap = new JPanel(new GridBagLayout());
        pvWrap.setBackground(new Color(0x14, 0x17, 0x21));
        previewHost = pvWrap;
        right.add(pvWrap, BorderLayout.CENTER);
        JPanel artBtns = new JPanel(new GridLayout(3, 1, 4, 4));
        JButton bRefresh = new JButton("刷新预览");
        JButton bArt = new JButton("选择卡图…");
        JButton bArtDir = new JButton("打开卡图文件夹");
        artBtns.add(bRefresh); artBtns.add(bArt); artBtns.add(bArtDir);
        right.add(artBtns, BorderLayout.SOUTH);
        root.add(right, BorderLayout.EAST);
        bRefresh.addActionListener(e -> { applyForm(); refreshPreview(); });
        bArt.addActionListener(e -> chooseArt());
        bArtDir.addActionListener(e -> {
            try {
                java.nio.file.Path dir = cardsDir.getParent().resolve("art");
                java.nio.file.Files.createDirectories(dir);
                java.awt.Desktop.getDesktop().open(dir.toFile());
            } catch (Exception ex) { JOptionPane.showMessageDialog(this, "无法打开：" + ex.getMessage()); }
        });
        cardList.addListSelectionListener(e -> { if (!e.getValueIsAdjusting()) refreshPreview(); });

        // ---- 底部按钮 ----
        JPanel bottom = new JPanel(new FlowLayout(FlowLayout.RIGHT, 10, 4));
        JButton bApply = new JButton("应用修改（写入内存）");
        JButton bSave = new JButton("保存当前文件到磁盘");
        bottom.add(bApply); bottom.add(bSave);
        root.add(bottom, BorderLayout.SOUTH);

        // ---- 事件 ----
        fileBox.addActionListener(e -> fillCardList());
        cardList.addListSelectionListener(e -> {
            if (!e.getValueIsAdjusting()) loadForm(cardList.getSelectedValue());
        });
        groupBox.addActionListener(e -> { if (!loadingForm && editing != null) fillEffectTable(editing); });

        bNew.addActionListener(e -> {
            CardDef d = new CardDef();
            d.id = "new_card_" + System.currentTimeMillis() % 100000;
            d.name = "新卡牌";
            currentFileCards().add(d);
            fillCardList();
            cardList.setSelectedValue(d, true);
        });
        bCopy.addActionListener(e -> {
            CardDef src = cardList.getSelectedValue();
            if (src == null) return;
            CardDef d = CardDef.fromMap(src.toMap());
            d.id = src.id + "_copy";
            d.name = src.name + "（副本）";
            currentFileCards().add(d);
            fillCardList();
            cardList.setSelectedValue(d, true);
        });
        bDel.addActionListener(e -> {
            CardDef src = cardList.getSelectedValue();
            if (src == null) return;
            if (JOptionPane.showConfirmDialog(this, "删除卡牌【" + src.name + "】？",
                    "确认", JOptionPane.OK_CANCEL_OPTION) == JOptionPane.OK_OPTION) {
                currentFileCards().remove(src);
                editing = null;
                fillCardList();
            }
        });
        bApply.addActionListener(e -> applyForm());
        bSave.addActionListener(e -> saveCurrentFile());
    }

    private JPanel buildBasicForm() {
        JPanel p = new JPanel(new GridBagLayout());
        GridBagConstraints g = new GridBagConstraints();
        g.insets = new Insets(3, 6, 3, 6);
        g.fill = GridBagConstraints.HORIZONTAL;
        g.weightx = 1;
        int row = 0;
        row = addRow(p, g, row, "卡牌ID（唯一）", fId);
        row = addRow(p, g, row, "名称", fName);
        row = addRow(p, g, row, "阵营", fFaction);
        row = addRow(p, g, row, "类型", fType);
        row = addRow(p, g, row, "词条（逗号分隔，同词条每回合段限用1次）", fTags);
        row = addRow(p, g, row, "惩罚值（打出时对方抽牌数）", fPunish);
        row = addRow(p, g, row, "效果描述文本", new JScrollPane(fText));
        row = addRow(p, g, row, "风味文本", new JScrollPane(fFlavor));
        row = addRow(p, g, row, "攻击力（随从）", fAttack);
        row = addRow(p, g, row, "生命值（随从）", fHealth);
        row = addRow(p, g, row, "每回合攻击次数", fAtkPerTurn);
        row = addRow(p, g, row, "关键词（逗号分隔：突袭/嘲讽/圣盾/扰魔/永续）", fKeywords);
        row = addRow(p, g, row, "吟唱回合数（咒文，0=立即）", fChant);
        row = addRow(p, g, row, "伏击类型", fAmbushKind);
        row = addRow(p, g, row, "伏击触发时机（OPPONENT_PLAYS_CARD/OPPONENT_ATTACKS/OPPONENT_SUMMONS）", fAmbushTrigger);
        g.gridx = 0; g.gridy = row++; g.gridwidth = 2; p.add(fPunishAct, g); g.gridwidth = 1;
        row = addRow(p, g, row, "惩罚发动时的惩罚值（降低后）", fPunishCost);
        row = addRow(p, g, row, "惩罚附加条件（ALWAYS/SELF_LEADER_ON_FIELD/ENEMY_MINIONS_GE_n/HAND_GE_n/SELF_FIELD_GE_n）", fPunishCond);
        g.gridx = 0; g.gridy = row++; g.gridwidth = 2; p.add(fLeader, g); g.gridwidth = 1;
        row = addRow(p, g, row, "统领：赋予玩家生命（>0 启用）", fGrantLife);
        row = addRow(p, g, row, "统领：自身耐久（非随从统领）", fDurability);
        row = addRow(p, g, row, "统领：特殊胜利条件（NONE/OPP_DISCARD_TOTAL_GE/NO_DAMAGE_TURNS_GE/OPP_PUNISH_DRAW_TURN_GE）", fWinCondition);
        row = addRow(p, g, row, "统领：胜利条件参数", fWinParam);
        row = addRow(p, g, row, "统领：胜利条件说明文字", fWinText);
        return p;
    }

    private int addRow(JPanel p, GridBagConstraints g, int row, String label, Component comp) {
        g.gridx = 0; g.gridy = row; g.weightx = 0.4;
        p.add(new JLabel(label), g);
        g.gridx = 1; g.weightx = 0.6;
        p.add(comp, g);
        return row + 1;
    }

    private JPanel buildEffectsTab() {
        JPanel p = new JPanel(new BorderLayout(4, 4));
        JPanel top = new JPanel(new BorderLayout(4, 4));
        top.add(new JLabel("效果组："), BorderLayout.WEST);
        top.add(groupBox, BorderLayout.CENTER);
        p.add(top, BorderLayout.NORTH);
        p.add(new JScrollPane(effectTable), BorderLayout.CENTER);
        JPanel btns = new JPanel(new FlowLayout(FlowLayout.LEFT));
        JButton add = new JButton("增加效果行");
        JButton del = new JButton("删除选中行");
        btns.add(add); btns.add(del);
        JLabel hint = new JLabel("动作表见 docs/DESIGN.md：DAMAGE/HEAL/DRAW/OPP_DRAW/DESTROY/BUFF/SUMMON/END_TURN/…");
        btns.add(hint);
        p.add(btns, BorderLayout.SOUTH);
        add.addActionListener(e -> effectModel.addRow(new Object[]{"DAMAGE", "ENEMY_MINION", 1, ""}));
        del.addActionListener(e -> {
            int r = effectTable.getSelectedRow();
            if (r >= 0) effectModel.removeRow(r);
        });
        return p;
    }

    private JPanel buildCustomTab() {
        JPanel p = new JPanel(new BorderLayout(4, 4));
        p.add(new JLabel("  自定义键值（引擎不识别的字段会原样保留，用于扩展新机制）"), BorderLayout.NORTH);
        p.add(new JScrollPane(new JTable(customModel)), BorderLayout.CENTER);
        JPanel btns = new JPanel(new FlowLayout(FlowLayout.LEFT));
        JButton add = new JButton("增加字段");
        JButton del = new JButton("删除选中字段");
        btns.add(add); btns.add(del);
        p.add(btns, BorderLayout.SOUTH);
        add.addActionListener(e -> customModel.addRow(new Object[]{"key", "value"}));
        del.addActionListener(e -> {
            JTable t = (JTable) ((JScrollPane) p.getComponent(1)).getViewport().getView();
            int r = t.getSelectedRow();
            if (r >= 0) customModel.removeRow(r);
        });
        return p;
    }

    // ================= 数据装载 =================

    private void reloadLibrary() {
        lib = CardLibrary.load(cardsDir);
        fileBox.removeAllItems();
        for (String f : lib.byFile.keySet()) fileBox.addItem(f);
        if (fileBox.getItemCount() > 0) fileBox.setSelectedIndex(0);
        fillCardList();
    }

    private List<CardDef> currentFileCards() {
        String f = (String) fileBox.getSelectedItem();
        return f == null ? new ArrayList<>() : lib.byFile.computeIfAbsent(f, k -> new ArrayList<>());
    }

    private void fillCardList() {
        listModel.clear();
        for (CardDef d : currentFileCards()) listModel.addElement(d);
        if (!listModel.isEmpty()) cardList.setSelectedIndex(0);
        else loadForm(null);
    }

    private void loadForm(CardDef d) {
        loadingForm = true;
        editing = d;
        try {
            if (d == null) { clearForm(); return; }
            fId.setText(d.id);
            fName.setText(d.name);
            fFaction.setText(d.faction);
            fType.setSelectedItem(d.type);
            fTags.setText(String.join(",", d.tags));
            fPunish.setValue(d.punish);
            fText.setText(d.text);
            fFlavor.setText(d.flavor);
            fAttack.setValue(d.attack);
            fHealth.setValue(d.health);
            fAtkPerTurn.setValue(d.attacksPerTurn);
            fKeywords.setText(String.join(",", d.keywords));
            fChant.setValue(d.chant);
            fAmbushKind.setSelectedItem(d.ambushKind);
            fAmbushTrigger.setText(d.ambushTrigger);
            fPunishAct.setSelected(d.punishActivatable);
            fPunishCost.setValue(d.punishCost);
            fPunishCond.setText(d.punishCondition);
            fLeader.setSelected(d.leader);
            fGrantLife.setValue(d.leaderDef.grantLife);
            fDurability.setValue(d.leaderDef.durability);
            fWinCondition.setText(d.leaderDef.winCondition);
            fWinParam.setValue(d.leaderDef.winParam);
            fWinText.setText(d.leaderDef.winText);
            fillEffectTable(d);
            customModel.setRowCount(0);
            d.custom.forEach((k, v) -> customModel.addRow(new Object[]{k, String.valueOf(v)}));
        } finally {
            loadingForm = false;
        }
    }

    private void clearForm() {
        for (JTextField f : List.of(fId, fName, fFaction, fTags, fKeywords, fAmbushTrigger,
                fPunishCond, fWinCondition, fWinText)) f.setText("");
        fText.setText(""); fFlavor.setText("");
        effectModel.setRowCount(0);
        customModel.setRowCount(0);
    }

    private List<CardDef.EffectSpec> groupOf(CardDef d, int idx) {
        switch (idx) {
            case 0: return d.onPlayEffects;
            case 1: return d.chantEffects;
            case 2: return d.ambushEffects;
            case 3: return d.onOpponentDiscardEffects;
            case 4: return d.punishEffects;
            case 5: return d.leaderDef.punishEffects;
            case 6: return d.leaderDef.enterEffects;
            default: return d.leaderDef.persistentEffects;
        }
    }

    private void fillEffectTable(CardDef d) {
        effectModel.setRowCount(0);
        for (CardDef.EffectSpec s : groupOf(d, groupBox.getSelectedIndex()))
            effectModel.addRow(new Object[]{s.action, s.target, s.amount, s.param});
    }

    /** 将效果表写回当前效果组 */
    private void commitEffectTable(CardDef d) {
        if (effectTable.isEditing()) effectTable.getCellEditor().stopCellEditing();
        List<CardDef.EffectSpec> list = groupOf(d, groupBox.getSelectedIndex());
        list.clear();
        for (int i = 0; i < effectModel.getRowCount(); i++) {
            CardDef.EffectSpec s = new CardDef.EffectSpec();
            s.action = String.valueOf(effectModel.getValueAt(i, 0)).trim();
            s.target = String.valueOf(effectModel.getValueAt(i, 1)).trim();
            try { s.amount = Integer.parseInt(String.valueOf(effectModel.getValueAt(i, 2)).trim()); }
            catch (NumberFormatException ex) { s.amount = 0; }
            s.param = String.valueOf(effectModel.getValueAt(i, 3)).trim();
            list.add(s);
        }
    }

    private void applyForm() {
        if (editing == null) { JOptionPane.showMessageDialog(this, "未选中卡牌"); return; }
        CardDef d = editing;
        d.id = fId.getText().trim();
        d.name = fName.getText().trim();
        d.faction = fFaction.getText().trim();
        d.type = (CardDef.CardType) fType.getSelectedItem();
        d.tags = splitCsv(fTags.getText());
        d.punish = (Integer) fPunish.getValue();
        d.text = fText.getText();
        d.flavor = fFlavor.getText();
        d.attack = (Integer) fAttack.getValue();
        d.health = (Integer) fHealth.getValue();
        d.attacksPerTurn = (Integer) fAtkPerTurn.getValue();
        d.keywords = new LinkedHashSet<>(splitCsv(fKeywords.getText()));
        d.chant = (Integer) fChant.getValue();
        d.ambushKind = (CardDef.AmbushKind) fAmbushKind.getSelectedItem();
        d.ambushTrigger = fAmbushTrigger.getText().trim();
        d.punishActivatable = fPunishAct.isSelected();
        d.punishCost = (Integer) fPunishCost.getValue();
        d.punishCondition = fPunishCond.getText().trim().isEmpty() ? "ALWAYS" : fPunishCond.getText().trim();
        d.leader = fLeader.isSelected();
        d.leaderDef.grantLife = (Integer) fGrantLife.getValue();
        d.leaderDef.durability = (Integer) fDurability.getValue();
        d.leaderDef.winCondition = fWinCondition.getText().trim().isEmpty() ? "NONE" : fWinCondition.getText().trim();
        d.leaderDef.winParam = (Integer) fWinParam.getValue();
        d.leaderDef.winText = fWinText.getText().trim();
        commitEffectTable(d);
        d.custom.clear();
        for (int i = 0; i < customModel.getRowCount(); i++) {
            String k = String.valueOf(customModel.getValueAt(i, 0)).trim();
            if (!k.isEmpty()) d.custom.put(k, String.valueOf(customModel.getValueAt(i, 1)));
        }
        cardList.repaint();
        JOptionPane.showMessageDialog(this, "已应用到内存。点击“保存当前文件到磁盘”后写入 JSON。");
    }

    private List<String> splitCsv(String s) {
        return Arrays.stream(s.split("[,，]"))
                .map(String::trim).filter(x -> !x.isEmpty()).collect(Collectors.toList());
    }

    private void saveCurrentFile() {
        String f = (String) fileBox.getSelectedItem();
        if (f == null) return;
        try {
            Path p = cardsDir.resolve(f);
            Files.createDirectories(cardsDir);
            CardLibrary.saveFile(p, currentFileCards());
            JOptionPane.showMessageDialog(this, "已保存到 " + p);
        } catch (IOException ex) {
            JOptionPane.showMessageDialog(this, "保存失败：" + ex.getMessage(),
                    "错误", JOptionPane.ERROR_MESSAGE);
        }
    }

    // ===================== 卡面预览与卡图 =====================

    /** 渲染当前选中卡（与对局内观感一致） */
    private void refreshPreview() {
        if (previewHost == null) return;
        previewHost.removeAll();
        CardDef d = editing != null ? editing : cardList.getSelectedValue();
        if (d != null) {
            try {
                com.dominionwars.engine.CardInstance ci = new com.dominionwars.engine.CardInstance(d, 0);
                previewHost.add(new com.dominionwars.ui.game.CardPanel(ci, d.punish).size(176, 240));
            } catch (Exception ex) {
                previewHost.add(new JLabel("预览失败：" + ex.getMessage()));
            }
        }
        previewHost.revalidate();
        previewHost.repaint();
    }

    /** 选择一张图片复制为 data/art/<id>.png */
    private void chooseArt() {
        String id = fId.getText().trim();
        if (id.isEmpty()) { JOptionPane.showMessageDialog(this, "请先填写卡牌ID"); return; }
        JFileChooser fc = new JFileChooser();
        fc.setFileFilter(new javax.swing.filechooser.FileNameExtensionFilter("图片", "png", "jpg", "jpeg"));
        if (fc.showOpenDialog(this) != JFileChooser.APPROVE_OPTION) return;
        try {
            java.nio.file.Path dir = cardsDir.getParent().resolve("art");
            java.nio.file.Files.createDirectories(dir);
            java.awt.image.BufferedImage img = javax.imageio.ImageIO.read(fc.getSelectedFile());
            if (img == null) throw new Exception("无法读取图片");
            javax.imageio.ImageIO.write(img, "png", dir.resolve(id + ".png").toFile());
            com.dominionwars.ui.game.CardPanel.clearArtCache();
            refreshPreview();
            JOptionPane.showMessageDialog(this, "卡图已保存为 data/art/" + id + ".png");
        } catch (Exception ex) {
            JOptionPane.showMessageDialog(this, "保存失败：" + ex.getMessage());
        }
    }
}