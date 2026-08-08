package com.dominionwars.ui.settings;

import com.dominionwars.ui.i18n.I18n;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import java.awt.*;

/** UI 设置窗口：语言、显示密度、提示信息等。 */
public class SettingsDialog extends JDialog {
    private boolean changed = false;

    public SettingsDialog(Window owner) {
        super(owner, I18n.t("settings.title"), ModalityType.APPLICATION_MODAL);
        buildUi();
        pack();
        setMinimumSize(new Dimension(420, 240));
        setLocationRelativeTo(owner);
    }

    private void buildUi() {
        JPanel root = new JPanel(new BorderLayout(10, 10));
        root.setBorder(new EmptyBorder(16, 18, 16, 18));
        setContentPane(root);

        JPanel form = new JPanel(new GridBagLayout());
        GridBagConstraints g = new GridBagConstraints();
        g.insets = new Insets(6, 4, 6, 4);
        g.anchor = GridBagConstraints.WEST;
        g.fill = GridBagConstraints.HORIZONTAL;
        g.weightx = 0;

        JComboBox<UiSettings.Language> lang = new JComboBox<>(UiSettings.Language.values());
        lang.setSelectedItem(UiSettings.language);
        JComboBox<UiSettings.Density> density = new JComboBox<>(UiSettings.Density.values());
        density.setSelectedItem(UiSettings.density);
        JCheckBox tips = new JCheckBox(I18n.t("settings.tooltip"), UiSettings.showTooltips);
        JCheckBox ambushCount = new JCheckBox(I18n.t("settings.ambushCount"), UiSettings.showOpponentAmbushCount);

        addRow(form, g, 0, I18n.t("settings.language"), lang);
        addRow(form, g, 1, I18n.t("settings.density"), density);
        g.gridx = 0; g.gridy = 2; g.gridwidth = 2; g.weightx = 1;
        form.add(tips, g);
        g.gridy = 3;
        form.add(ambushCount, g);

        JTextArea note = new JTextArea(I18n.t("settings.note"));
        note.setEditable(false);
        note.setLineWrap(true);
        note.setWrapStyleWord(true);
        note.setOpaque(false);
        note.setForeground(new Color(0x66, 0x66, 0x66));

        JPanel buttons = new JPanel(new FlowLayout(FlowLayout.RIGHT));
        JButton apply = new JButton(I18n.t("settings.apply"));
        JButton close = new JButton(I18n.t("settings.close"));
        buttons.add(apply);
        buttons.add(close);

        apply.addActionListener(e -> {
            UiSettings.language = (UiSettings.Language) lang.getSelectedItem();
            UiSettings.density = (UiSettings.Density) density.getSelectedItem();
            UiSettings.showTooltips = tips.isSelected();
            UiSettings.showOpponentAmbushCount = ambushCount.isSelected();
            UiSettings.save();
            changed = true;
            dispose();
        });
        close.addActionListener(e -> dispose());

        root.add(form, BorderLayout.CENTER);
        root.add(note, BorderLayout.NORTH);
        root.add(buttons, BorderLayout.SOUTH);
    }

    private void addRow(JPanel p, GridBagConstraints g, int row, String label, JComponent c) {
        g.gridy = row;
        g.gridx = 0;
        g.gridwidth = 1;
        g.weightx = 0;
        p.add(new JLabel(label), g);
        g.gridx = 1;
        g.weightx = 1;
        p.add(c, g);
    }

    public boolean changed() { return changed; }
}
