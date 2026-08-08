package com.dominionwars.ui.game;

import com.dominionwars.engine.Game;
import com.dominionwars.ui.i18n.I18n;

import javax.swing.JComponent;
import java.awt.BasicStroke;
import java.awt.Color;
import java.awt.Dimension;
import java.awt.Font;
import java.awt.FontMetrics;
import java.awt.GradientPaint;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.geom.GeneralPath;
import java.awt.geom.RoundRectangle2D;

/** 共享王城 HUD：城堡剪影 + 公共血条 + 破城状态 */
public class CastlePanel extends JComponent {

    private final Game game;

    public CastlePanel(Game game) {
        this.game = game;
        setPreferredSize(new Dimension(360, 54));
        setOpaque(false);
    }

    @Override
    protected void paintComponent(Graphics g0) {
        Graphics2D g = (Graphics2D) g0.create();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        int w = getWidth(), h = getHeight();

        if (!game.balance.royalCastleEnabled) { g.dispose(); return; }

        boolean broken = game.royalCastleHp <= 0;
        int barH = 18, castleW = 44;
        int barX = castleW + 10, barY = h / 2 - barH / 2;
        int barW = w - barX - 8;

        // 城堡剪影
        Color stone = broken ? new Color(0x5a, 0x4a, 0x4a) : new Color(0xb9, 0xb2, 0xa2);
        GeneralPath castle = new GeneralPath();
        int cx = 2, cy = h - 10, cw = castleW - 6, ch = h - 18;
        castle.moveTo(cx, cy);
        castle.lineTo(cx, cy - ch * 0.6);
        // 三座塔与垛口
        double tw = cw / 3.0;
        for (int t = 0; t < 3; t++) {
            double tx = cx + t * tw;
            double th = cy - ch * (t == 1 ? 1.0 : 0.75);
            castle.lineTo(tx, th);
            castle.lineTo(tx + tw * 0.25, th);
            castle.lineTo(tx + tw * 0.25, th + 4);
            castle.lineTo(tx + tw * 0.55, th + 4);
            castle.lineTo(tx + tw * 0.55, th);
            castle.lineTo(tx + tw, th);
            castle.lineTo(tx + tw, cy - ch * 0.6);
        }
        castle.lineTo(cx + cw, cy);
        castle.closePath();
        g.setColor(stone);
        g.fill(castle);
        if (broken) { // 裂纹
            g.setColor(new Color(0x20, 0x16, 0x16));
            g.setStroke(new BasicStroke(2f));
            g.drawLine(cx + cw / 2, (int) (cy - ch), cx + cw / 2 - 6, cy - ch / 2);
            g.drawLine(cx + cw / 2 - 6, cy - ch / 2, cx + cw / 2 + 4, cy - 4);
        }

        // 血条
        RoundRectangle2D track = new RoundRectangle2D.Float(barX, barY, barW, barH, barH, barH);
        g.setColor(new Color(0, 0, 0, 130));
        g.fill(track);
        double ratio = Math.max(0, Math.min(1, game.royalCastleHp / (double) game.royalCastleMaxHp));
        if (ratio > 0) {
            Color hpc = ratio > 0.5 ? new Color(0xcf, 0xa9, 0x4e)
                    : ratio > 0.25 ? new Color(0xd9, 0x7c, 0x33) : new Color(0xc4, 0x44, 0x3a);
            g.setPaint(new GradientPaint(barX, barY, Theme.mix(hpc, Color.WHITE, 0.2),
                    barX, barY + barH, Theme.mix(hpc, Color.BLACK, 0.25)));
            g.fill(new RoundRectangle2D.Float(barX, barY, (float) (barW * ratio), barH, barH, barH));
        }
        g.setColor(Theme.alpha(Theme.GOLD, 200));
        g.setStroke(new BasicStroke(1.4f));
        g.draw(track);

        // 文本
        g.setFont(Theme.font(Font.BOLD, 12f));
        String label;
        if (broken) {
            label = I18n.t("castle.broken") + " — " + game.players[game.royalCastleBreaker].name;
        } else {
            label = I18n.t("castle.name") + "  " + game.royalCastleHp + " / " + game.royalCastleMaxHp;
        }
        FontMetrics fm = g.getFontMetrics();
        g.setColor(Color.BLACK);
        g.drawString(label, barX + (barW - fm.stringWidth(label)) / 2f + 1, barY + barH / 2f + fm.getAscent() / 2f - 1);
        g.setColor(Color.WHITE);
        g.drawString(label, barX + (barW - fm.stringWidth(label)) / 2f, barY + barH / 2f + fm.getAscent() / 2f - 2);
        g.dispose();
    }
}
