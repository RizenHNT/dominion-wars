package com.dominionwars.ui.game;

import com.dominionwars.engine.CardInstance;
import com.dominionwars.model.CardDef;
import com.dominionwars.ui.i18n.I18n;

import javax.swing.JComponent;
import java.awt.AlphaComposite;
import java.awt.BasicStroke;
import java.awt.Color;
import java.awt.Dimension;
import java.awt.Font;
import java.awt.FontMetrics;
import java.awt.GradientPaint;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.geom.Arc2D;
import java.awt.geom.Ellipse2D;
import java.awt.geom.GeneralPath;
import java.awt.geom.Path2D;
import java.awt.geom.RoundRectangle2D;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * 完整图形卡牌组件：圆角卡框、阵营色、程序化纹章插画、
 * 惩罚徽章、攻/血宝石、吟唱沙漏、关键词条、状态光效与卡背。
 */
public class CardPanel extends JComponent {

    public enum Glow { NONE, USABLE, SELECTED, TARGET }

    // ---- 卡图缓存：data/art/<cardId>.png，缺失时回退程序化纹章 ----
    private static final Map<String, java.awt.Image> ART_CACHE = new HashMap<>();
    private static final java.awt.Image ART_MISSING = new java.awt.image.BufferedImage(1, 1, java.awt.image.BufferedImage.TYPE_INT_ARGB);

    private static java.awt.Image artOf(String cardId) {
        return ART_CACHE.computeIfAbsent(cardId, id -> {
            try {
                java.io.File f = new java.io.File("data/art/" + id + ".png");
                if (f.isFile()) return javax.imageio.ImageIO.read(f);
            } catch (Exception ignored) { }
            return ART_MISSING;
        });
    }

    /** 卡图变更后可调用以重新加载 */
    public static void clearArtCache() { ART_CACHE.clear(); }

    public final CardInstance inst;     // 可为 null（卡背）
    private final boolean faceDown;
    private int punishShown;
    private Glow glow = Glow.NONE;
    private boolean hitFlash = false;
    private boolean dimmed = false;
    private boolean hover = false;
    private String statusText = "";

    public CardPanel(CardInstance inst, int punishShown) {
        this.inst = inst;
        this.faceDown = inst == null;
        this.punishShown = punishShown;
        setOpaque(false);
        addMouseListener(new java.awt.event.MouseAdapter() {
            @Override public void mouseEntered(java.awt.event.MouseEvent e) { hover = true; repaint(); }
            @Override public void mouseExited(java.awt.event.MouseEvent e) { hover = false; repaint(); }
        });
    }

    /** 卡背 */
    public static CardPanel back() { return new CardPanel(null, 0); }

    public CardPanel glow(Glow g) { this.glow = g; return this; }
    public CardPanel dim(boolean d) { this.dimmed = d; return this; }
    public CardPanel hit(boolean h) { this.hitFlash = h; return this; }
    public CardPanel status(String s) { this.statusText = s == null ? "" : s; return this; }
    public CardPanel size(int w, int h) { setPreferredSize(new Dimension(w, h)); return this; }

    @Override
    protected void paintComponent(Graphics g0) {
        Graphics2D g = (Graphics2D) g0.create();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        int w = getWidth(), h = getHeight();
        int pad = 3;
        RoundRectangle2D card = new RoundRectangle2D.Float(pad, pad, w - pad * 2f, h - pad * 2f, 14, 14);

        // ---- 状态光晕 ----
        Color glowColor = glow == Glow.USABLE ? Theme.GLOW_USABLE
                : glow == Glow.SELECTED ? Theme.GLOW_SELECT
                : glow == Glow.TARGET ? Theme.GLOW_TARGET : null;
        if (glowColor != null) {
            for (int i = 3; i >= 1; i--) {
                g.setColor(Theme.alpha(glowColor, 36 * i));
                g.setStroke(new BasicStroke(i * 2.2f));
                g.draw(new RoundRectangle2D.Float(pad - i, pad - i, w - (pad - i) * 2f, h - (pad - i) * 2f, 16, 16));
            }
        }

        if (faceDown) { paintBack(g, card, w, h); g.dispose(); return; }

        CardDef d = inst.def;
        boolean leader = inst.isLeaderEntity || d.leader;
        Color deep = Theme.factionDeep(d.faction);
        Color main = Theme.factionMain(d.faction);
        Color light = Theme.factionLight(d.faction);

        // ---- 卡框 ----
        g.setPaint(new GradientPaint(0, pad, Theme.mix(deep, Color.BLACK, 0.15),
                0, h - pad, Theme.mix(deep, Color.BLACK, 0.5)));
        g.fill(card);
        // 内衬：按类型微调底色（随从米黄/咒文淡蓝/伏击淡紫/惩罚淡红）
        int in = 6;
        RoundRectangle2D inner = new RoundRectangle2D.Float(pad + in, pad + in,
                w - (pad + in) * 2f, h - (pad + in) * 2f, 10, 10);
        Color innerTop, innerBot;
        switch (d.type) {
            case SPELL  -> { innerTop = new Color(0xe2, 0xea, 0xf4); innerBot = new Color(0xc4, 0xd2, 0xe2); }
            case AMBUSH -> { innerTop = new Color(0xea, 0xe2, 0xf2); innerBot = new Color(0xd2, 0xc4, 0xe0); }
            case PUNISH -> { innerTop = new Color(0xf4, 0xe2, 0xe2); innerBot = new Color(0xe2, 0xc6, 0xc4); }
            default     -> { innerTop = new Color(0xf2, 0xea, 0xd6); innerBot = new Color(0xd9, 0xcd, 0xb0); }
        }
        g.setPaint(new GradientPaint(0, 0, innerTop, 0, h, innerBot));
        g.fill(inner);

        // ---- 插画区 ----
        int artY = pad + in + 22;
        int artH = Math.max(26, (int) (h * 0.34));
        java.awt.Shape clip = g.getClip();
        RoundRectangle2D artRect = new RoundRectangle2D.Float(pad + in + 3, artY, w - (pad + in + 3) * 2f, artH, 8, 8);
        g.setClip(artRect);
        java.awt.image.BufferedImage userArt = CardArt.of(d.id);
        if (userArt != null) {
            int aw0 = (int) artRect.getWidth(), ah0 = (int) artRect.getHeight();
            double sc = Math.max(aw0 / (double) userArt.getWidth(), ah0 / (double) userArt.getHeight());
            int dw = (int) Math.ceil(userArt.getWidth() * sc), dh = (int) Math.ceil(userArt.getHeight() * sc);
            g.drawImage(userArt, (int) artRect.getX() + (aw0 - dw) / 2, (int) artRect.getY() + (ah0 - dh) / 2, dw, dh, null);
        } else {
            paintArt(g, d, (int) artRect.getX(), (int) artRect.getY(), (int) artRect.getWidth(), (int) artRect.getHeight(), deep, main, light);
        }
        g.setClip(clip);
        g.setColor(Theme.alpha(deep, 170));
        g.setStroke(new BasicStroke(1.4f));
        g.draw(artRect);

        // ---- 名称栏 ----
        g.setColor(Theme.alpha(Color.BLACK, 26));
        g.fillRoundRect(pad + in + 2, pad + in + 2, w - (pad + in + 2) * 2, 19, 8, 8);
        g.setColor(leader ? Theme.GOLD_DARK : Theme.mix(deep, Color.BLACK, 0.2));
        String name = (leader ? "★ " : "") + d.name;
        float ns = 13f;
        g.setFont(Theme.font(Font.BOLD, ns));
        FontMetrics fm = g.getFontMetrics();
        int nameMax = w - (pad + in + 2) * 2 - 26;
        while (fm.stringWidth(name) > nameMax && ns > 9f) {
            ns -= 0.5f; g.setFont(Theme.font(Font.BOLD, ns)); fm = g.getFontMetrics();
        }
        g.drawString(name, pad + in + 6, pad + in + 16);

        // ---- 类型条 ----
        int typeY = artY + artH + 3;
        Color tc = Theme.typeColor(d.type);
        g.setFont(Theme.font(Font.BOLD, 10.5f));
        String typeText = Theme.typeGlyph(d.type) + " " + I18n.type(d.type);
        int chipW = g.getFontMetrics().stringWidth(typeText) + 12;
        g.setColor(Theme.alpha(tc, 200));
        g.fillRoundRect(pad + in + 3, typeY, chipW, 15, 8, 8);
        g.setColor(Color.WHITE);
        g.drawString(typeText, pad + in + 9, typeY + 12);
        // 伏击类型 / 吟唱
        g.setFont(Theme.font(Font.PLAIN, 10.5f));
        g.setColor(new Color(0x5a, 0x52, 0x3e));
        StringBuilder extra = new StringBuilder();
        if (d.type == CardDef.CardType.AMBUSH) extra.append(I18n.ambushKind(d.ambushKind));
        if (d.chant > 0) extra.append(extra.length() > 0 ? " " : "").append(I18n.t("info.chant"))
                .append(inst.chantRemaining > 0 ? inst.chantRemaining : d.chant);
        if (extra.length() > 0) g.drawString(extra.toString(), pad + in + chipW + 8, typeY + 12);

        // ---- 关键词 + 状态 ----
        int kwY = typeY + 18;
        List<String> chips = new ArrayList<>();
        if (h >= 130) {
            for (String k : inst.keywords.isEmpty() ? d.keywords : inst.keywords) chips.add(k);
            if (d.guard) chips.add("护卫");
            if (d.hasKingSlayer()) chips.add("弑君");
            if (inst.shield) chips.remove(CardDef.KW_SHIELD); // 用宝石呈现
        }
        int cx = pad + in + 3;
        g.setFont(Theme.font(Font.BOLD, 10f));
        for (String k : chips) {
            int cw = g.getFontMetrics().stringWidth(k) + 10;
            if (cx + cw > w - pad - in - 3) break;
            g.setColor(Theme.alpha(deep, 36));
            g.fillRoundRect(cx, kwY, cw, 14, 7, 7);
            g.setColor(Theme.mix(deep, Color.BLACK, 0.1));
            g.drawString(k, cx + 5, kwY + 11);
            cx += cw + 3;
        }

        // ---- 效果文本（最多两行省略） ----
        if (h >= 150 && !d.text.isEmpty()) {
            g.setFont(Theme.font(Font.PLAIN, 10f));
            g.setColor(new Color(0x4a, 0x44, 0x36));
            int maxLines = h >= 190 ? 3 : 2;
            drawWrapped(g, d.text, pad + in + 5, kwY + 26, w - (pad + in + 5) * 2, maxLines);
        }

        // ---- 底部宝石 ----
        int gy = h - pad - in - 13;
        if (d.isMinion()) {
            Color atkBase = inst.attack > d.attack ? new Color(0x2f, 0x8f, 0x3f) : new Color(0xd0, 0x7a, 0x1f);
            Color hpBase = inst.health < d.health ? new Color(0xd9, 0x1f, 0x1f)
                    : inst.health > d.health ? new Color(0x2f, 0x8f, 0x3f) : new Color(0xb8, 0x32, 0x32);
            gem(g, pad + in + 14, gy, atkBase, String.valueOf(inst.attack));
            gem(g, w - pad - in - 14, gy, hpBase, String.valueOf(inst.health));
            if (inst.shield) {
                g.setColor(Theme.alpha(new Color(0xe8, 0xd9, 0x8a), 235));
                g.setStroke(new BasicStroke(2.6f));
                g.draw(new Ellipse2D.Float(w - pad - in - 14 - 16, gy - 16, 32, 32));
            }
        } else if (inst.isLeaderEntity && inst.durability > 0) {
            gem(g, w / 2, gy, new Color(0x3b, 0x6e, 0xb0), String.valueOf(inst.durability));
        }

        // ---- 惩罚徽章（右上） ----
        paintPunishBadge(g, w - pad - 14, pad + 14, punishShown, d.punishActivatable || d.type == CardDef.CardType.PUNISH);

        // ---- 统领金框 ----
        if (leader) {
            g.setColor(Theme.GOLD);
            g.setStroke(new BasicStroke(2.2f));
            g.draw(card);
            g.setColor(Theme.alpha(Theme.GOLD, 90));
            g.draw(new RoundRectangle2D.Float(pad + 2.5f, pad + 2.5f, w - (pad + 2.5f) * 2, h - (pad + 2.5f) * 2, 11, 11));
        } else {
            g.setColor(Theme.alpha(Color.BLACK, 130));
            g.setStroke(new BasicStroke(1.2f));
            g.draw(card);
        }

        // ---- 状态文字 ----
        if (!statusText.isEmpty()) {
            g.setFont(Theme.font(Font.BOLD, 10f));
            int sw = g.getFontMetrics().stringWidth(statusText) + 10;
            int sx = (w - sw) / 2, sy = h - pad - 3;
            g.setColor(Theme.alpha(glowColor != null ? glowColor : new Color(0x44, 0x44, 0x44), 215));
            g.fillRoundRect(sx, sy - 12, sw, 14, 7, 7);
            g.setColor(Color.WHITE);
            g.drawString(statusText, sx + 5, sy - 1);
        }

        // ---- 受击红闪 ----
        if (hitFlash) {
            g.setColor(new Color(232, 60, 50, 90));
            g.fill(card);
            g.setColor(new Color(255, 90, 70, 200));
            g.setStroke(new BasicStroke(2.6f));
            g.draw(card);
        }

        // ---- 置灰 / 悬停 ----
        if (dimmed) {
            g.setColor(new Color(20, 22, 28, 130));
            g.fill(card);
        } else if (hover) {
            g.setColor(new Color(255, 255, 255, 26));
            g.fill(card);
        }
        g.dispose();
    }

    // ================= 细节绘制 =================

    private void paintPunishBadge(Graphics2D g, int cx, int cy, int v, boolean activatable) {
        int r = 12;
        GeneralPath hex = new GeneralPath();
        for (int i = 0; i < 6; i++) {
            double a = Math.PI / 6 + i * Math.PI / 3;
            double x = cx + r * Math.cos(a), y = cy + r * Math.sin(a);
            if (i == 0) hex.moveTo(x, y); else hex.lineTo(x, y);
        }
        hex.closePath();
        g.setPaint(new GradientPaint(cx - r, cy - r, new Color(0x6e, 0x2d, 0x73),
                cx + r, cy + r, new Color(0x3a, 0x16, 0x40)));
        g.fill(hex);
        g.setColor(activatable ? new Color(0xff, 0x77, 0xc8) : new Color(0xd9, 0xb2, 0xe8));
        g.setStroke(new BasicStroke(1.6f));
        g.draw(hex);
        g.setColor(Color.WHITE);
        g.setFont(Theme.font(Font.BOLD, v >= 10 ? 11f : 13f));
        FontMetrics fm = g.getFontMetrics();
        String s = String.valueOf(v);
        g.drawString(s, cx - fm.stringWidth(s) / 2f, cy + fm.getAscent() / 2f - 1.5f);
    }

    private void gem(Graphics2D g, int cx, int cy, Color base, String v) {
        int r = 14;
        g.setPaint(new GradientPaint(cx, cy - r, Theme.mix(base, Color.WHITE, 0.25), cx, cy + r, Theme.mix(base, Color.BLACK, 0.3)));
        g.fill(new Ellipse2D.Float(cx - r, cy - r, r * 2, r * 2));
        g.setColor(Theme.mix(base, Color.BLACK, 0.45));
        g.setStroke(new BasicStroke(1.5f));
        g.draw(new Ellipse2D.Float(cx - r, cy - r, r * 2, r * 2));
        g.setColor(Color.WHITE);
        g.setFont(Theme.font(Font.BOLD, 15f));
        FontMetrics fm = g.getFontMetrics();
        g.drawString(v, cx - fm.stringWidth(v) / 2f, cy + fm.getAscent() / 2f - 2f);
    }

    private void drawWrapped(Graphics2D g, String text, int x, int y, int maxW, int maxLines) {
        FontMetrics fm = g.getFontMetrics();
        List<String> lines = new ArrayList<>();
        StringBuilder cur = new StringBuilder();
        for (char ch : text.toCharArray()) {
            if (ch == '\n') { lines.add(cur.toString()); cur.setLength(0); continue; }
            cur.append(ch);
            if (fm.stringWidth(cur.toString()) > maxW) {
                cur.setLength(cur.length() - 1);
                lines.add(cur.toString());
                cur.setLength(0);
                cur.append(ch);
            }
            if (lines.size() >= maxLines) break;
        }
        if (lines.size() < maxLines && cur.length() > 0) lines.add(cur.toString());
        if (lines.size() == maxLines && (cur.length() > 0 || text.contains("\n"))) {
            String last = lines.get(maxLines - 1);
            if (last.length() > 1) lines.set(maxLines - 1, last.substring(0, last.length() - 1) + "…");
        }
        for (int i = 0; i < lines.size(); i++) g.drawString(lines.get(i), x, y + i * (fm.getHeight() - 2));
    }

    /** 程序化阵营插画 */
    private void paintArt(Graphics2D g, CardDef d, int x, int y, int w, int h, Color deep, Color main, Color light) {
        java.awt.Image art = artOf(d.id);
        if (art != ART_MISSING) {
            int iw = art.getWidth(null), ih = art.getHeight(null);
            if (iw > 0 && ih > 0) {
                double sc = Math.max(w / (double) iw, h / (double) ih);
                int dw = (int) Math.ceil(iw * sc), dh = (int) Math.ceil(ih * sc);
                g.drawImage(art, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh, null);
                if (d.leader) {
                    g.setColor(Theme.alpha(Theme.GOLD, 160));
                    g.setStroke(new BasicStroke(1.8f));
                    int cw2 = Math.min(w, h) / 2 + 8;
                    g.draw(new Ellipse2D.Float(x + w / 2f - cw2 / 2f, y + h / 2f + 2 - cw2 / 2f, cw2, cw2));
                }
                return;
            }
        }
        long seed = d.id.hashCode() * 2654435761L;
        g.setPaint(new GradientPaint(x, y, Theme.mix(deep, Color.BLACK, 0.25), x, y + h, deep));
        g.fillRect(x, y, w, h);
        String f = d.faction == null ? "" : d.faction;
        switch (f) {
            case "烈焰帝国": paintFlame(g, x, y, w, h, main, light, seed); break;
            case "深海联盟": paintSea(g, x, y, w, h, main, light, seed); break;
            case "古木圣地": paintWood(g, x, y, w, h, main, light, seed); break;
            case "机械遗迹": paintMachine(g, x, y, w, h, main, light, seed); break;
            default: paintNeutral(g, x, y, w, h, main, light, seed); break;
        }
        // 统领额外王冠光环
        if (d.leader) {
            g.setColor(Theme.alpha(Theme.GOLD, 200));
            g.setStroke(new BasicStroke(1.8f));
            int cw = Math.min(w, h) / 2 + 8, cx = x + w / 2, cy = y + h / 2 + 2;
            g.draw(new Ellipse2D.Float(cx - cw / 2f, cy - cw / 2f, cw, cw));
            g.setFont(Theme.font(Font.PLAIN, cw * 0.46f));
            FontMetrics fm = g.getFontMetrics();
            g.drawString("★", cx - fm.stringWidth("★") / 2f, cy + fm.getAscent() / 2f - 3);
        }
    }

    private double rnd(long seed, int i) { // 稳定伪随机
        long v = seed + i * 0x9E3779B97F4A7C15L;
        v ^= v >>> 33; v *= 0xFF51AFD7ED558CCDL; v ^= v >>> 33;
        return (v >>> 11) / (double) (1L << 53);
    }

    private void paintFlame(Graphics2D g, int x, int y, int w, int h, Color main, Color light, long s) {
        for (int i = 0; i < 5; i++) {
            double bx = x + w * (0.15 + 0.7 * rnd(s, i));
            double bw = w * (0.10 + 0.12 * rnd(s, i + 9));
            double bh = h * (0.5 + 0.45 * rnd(s, i + 17));
            GeneralPath p = new GeneralPath();
            p.moveTo(bx - bw / 2, y + h);
            p.curveTo(bx - bw, y + h - bh * 0.5, bx + bw * 0.4, y + h - bh * 0.55, bx, y + h - bh);
            p.curveTo(bx - bw * 0.4, y + h - bh * 0.5, bx + bw, y + h - bh * 0.45, bx + bw / 2, y + h);
            p.closePath();
            g.setColor(Theme.alpha(i % 2 == 0 ? main : light, 165));
            g.fill(p);
        }
        g.setColor(Theme.alpha(light, 220));
        int r = (int) (h * 0.16);
        g.fill(new Ellipse2D.Float(x + w * 0.5f - r, y + h * 0.62f - r, r * 2, r * 2));
    }

    private void paintSea(Graphics2D g, int x, int y, int w, int h, Color main, Color light, long s) {
        for (int i = 0; i < 4; i++) {
            float wy = y + h * (0.30f + 0.18f * i);
            GeneralPath p = new GeneralPath();
            p.moveTo(x, wy);
            double amp = h * (0.06 + 0.05 * rnd(s, i));
            for (int k = 0; k <= 4; k++) {
                double px = x + w * k / 4.0;
                double py = wy + (k % 2 == 0 ? -amp : amp);
                p.quadTo(px - w / 8.0, k % 2 == 0 ? wy + amp : wy - amp, px, py);
            }
            p.lineTo(x + w, y + h); p.lineTo(x, y + h); p.closePath();
            g.setColor(Theme.alpha(i % 2 == 0 ? main : light, 90 + i * 22));
            g.fill(p);
        }
        // 气泡
        for (int i = 0; i < 5; i++) {
            int r = (int) (2 + 3 * rnd(s, i + 31));
            g.setColor(Theme.alpha(Color.WHITE, 120));
            g.draw(new Ellipse2D.Float((float) (x + w * rnd(s, i + 41)), (float) (y + h * 0.55 * rnd(s, i + 51)), r * 2, r * 2));
        }
    }

    private void paintWood(Graphics2D g, int x, int y, int w, int h, Color main, Color light, long s) {
        int cx = x + w / 2;
        g.setColor(Theme.mix(main, Color.BLACK, 0.35));
        g.setStroke(new BasicStroke(Math.max(3f, h * 0.09f), BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        g.drawLine(cx, y + h, cx, y + (int) (h * 0.35));
        for (int i = 0; i < 4; i++) {
            double a = -Math.PI / 2 + (rnd(s, i) - 0.5) * 1.8;
            int len = (int) (h * (0.25 + 0.2 * rnd(s, i + 7)));
            int ex = cx + (int) (Math.cos(a) * len), ey = y + (int) (h * (0.45 + 0.2 * rnd(s, i + 3))) ;
            g.setStroke(new BasicStroke(2.2f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
            g.drawLine(cx, ey, ex, ey - len / 2);
            g.setColor(Theme.alpha(i % 2 == 0 ? main : light, 200));
            int r = (int) (h * (0.13 + 0.08 * rnd(s, i + 13)));
            g.fill(new Ellipse2D.Float(ex - r, ey - len / 2f - r, r * 2, r * 2));
            g.setColor(Theme.mix(main, Color.BLACK, 0.35));
        }
        g.setColor(Theme.alpha(light, 210));
        int r = (int) (h * 0.20);
        g.fill(new Ellipse2D.Float(cx - r, y + (int) (h * 0.30) - r, r * 2, r * 2));
    }

    private void paintMachine(Graphics2D g, int x, int y, int w, int h, Color main, Color light, long s) {
        // 齿轮
        for (int i = 0; i < 2; i++) {
            int r = (int) (h * (0.26 + 0.1 * rnd(s, i)));
            int cx = x + (int) (w * (0.3 + 0.4 * i + 0.08 * rnd(s, i + 5)));
            int cy = y + (int) (h * (0.45 + 0.15 * rnd(s, i + 9)));
            g.setColor(Theme.alpha(i == 0 ? main : light, 200));
            int teeth = 8;
            for (int t = 0; t < teeth; t++) {
                double a = t * Math.PI * 2 / teeth + rnd(s, i + 20);
                g.setStroke(new BasicStroke(Math.max(2.5f, r * 0.26f)));
                g.drawLine(cx, cy, cx + (int) (Math.cos(a) * r), cy + (int) (Math.sin(a) * r));
            }
            g.setStroke(new BasicStroke(2.5f));
            g.draw(new Ellipse2D.Float(cx - r * 0.72f, cy - r * 0.72f, r * 1.44f, r * 1.44f));
            g.setColor(Theme.mix(main, Color.BLACK, 0.4));
            g.fill(new Ellipse2D.Float(cx - r * 0.28f, cy - r * 0.28f, r * 0.56f, r * 0.56f));
        }
        // 电路线
        g.setColor(Theme.alpha(light, 160));
        g.setStroke(new BasicStroke(1.4f));
        for (int i = 0; i < 3; i++) {
            int yy = y + (int) (h * rnd(s, i + 30));
            int x2 = x + (int) (w * (0.3 + 0.6 * rnd(s, i + 35)));
            g.drawLine(x, yy, x2, yy);
            g.drawLine(x2, yy, x2, yy + (int) (h * 0.2 * (rnd(s, i + 40) - 0.5)));
        }
    }

    private void paintNeutral(Graphics2D g, int x, int y, int w, int h, Color main, Color light, long s) {
        int cx = x + w / 2, cy = y + h / 2;
        for (int i = 0; i < 3; i++) {
            int r = (int) (h * (0.18 + 0.14 * i));
            g.setColor(Theme.alpha(i % 2 == 0 ? light : main, 130 - i * 28));
            g.setStroke(new BasicStroke(2f));
            g.draw(new Arc2D.Float(cx - r, cy - r, r * 2, r * 2, (float) (rnd(s, i) * 360), 250, Arc2D.OPEN));
        }
        // 星
        Path2D star = new Path2D.Double();
        int r1 = (int) (h * 0.18), r2 = (int) (h * 0.075);
        for (int i = 0; i < 10; i++) {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            double r = i % 2 == 0 ? r1 : r2;
            double px = cx + Math.cos(a) * r, py = cy + Math.sin(a) * r;
            if (i == 0) star.moveTo(px, py); else star.lineTo(px, py);
        }
        star.closePath();
        g.setColor(Theme.alpha(light, 230));
        g.fill(star);
    }

    private void paintBack(Graphics2D g, RoundRectangle2D card, int w, int h) {
        g.setPaint(new GradientPaint(0, 0, new Color(0x2a, 0x21, 0x40), 0, h, new Color(0x14, 0x0f, 0x22)));
        g.fill(card);
        g.setColor(Theme.alpha(Theme.GOLD, 170));
        g.setStroke(new BasicStroke(1.6f));
        g.draw(card);
        int cx = w / 2, cy = h / 2;
        for (int i = 0; i < 3; i++) {
            int r = Math.min(w, h) / 6 + i * 7;
            g.setColor(Theme.alpha(Theme.GOLD, 120 - i * 30));
            g.draw(new Ellipse2D.Float(cx - r, cy - r, r * 2, r * 2));
        }
        g.setFont(Theme.font(Font.BOLD, Math.min(w, h) * 0.30f));
        g.setColor(Theme.alpha(Theme.GOLD, 220));
        FontMetrics fm = g.getFontMetrics();
        g.drawString("御", cx - fm.stringWidth("御") / 2f, cy + fm.getAscent() / 2f - 3);
        AlphaComposite old = (AlphaComposite) g.getComposite();
        g.setComposite(old);
    }
}
