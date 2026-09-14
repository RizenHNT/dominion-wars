package com.dominionwars.ui.game;

import com.dominionwars.model.CardDef;

import java.awt.Color;
import java.awt.Font;
import java.awt.GradientPaint;
import java.awt.Graphics2D;
import java.awt.GraphicsEnvironment;

/** 全局视觉主题：暗色牌桌 + 阵营色系 + 字体 */
public final class Theme {

    // ---- 牌桌 ----
    public static final Color BOARD_TOP    = new Color(0x18, 0x1d, 0x2a);
    public static final Color BOARD_BOTTOM = new Color(0x0d, 0x10, 0x18);
    public static final Color ZONE_BG      = new Color(255, 255, 255, 14);
    public static final Color ZONE_LINE    = new Color(255, 255, 255, 36);
    public static final Color TEXT_MAIN    = new Color(0xe8, 0xe4, 0xd8);
    public static final Color TEXT_DIM     = new Color(0x9a, 0x97, 0x8e);
    public static final Color GOLD         = new Color(0xd9, 0xb2, 0x5f);
    public static final Color GOLD_DARK    = new Color(0x8a, 0x6a, 0x2a);

    // ---- 状态光 ----
    public static final Color GLOW_USABLE  = new Color(0x57, 0xd9, 0x7a);
    public static final Color GLOW_SELECT  = new Color(0xff, 0x9e, 0x2e);
    public static final Color GLOW_TARGET  = new Color(0xe8, 0x4d, 0x4d);

    // ---- 类型色 ----
    public static final Color TYPE_MINION  = new Color(0xc7, 0x8a, 0x3b);
    public static final Color TYPE_SPELL   = new Color(0x5a, 0x8f, 0xd6);
    public static final Color TYPE_AMBUSH  = new Color(0x8a, 0x5f, 0xc2);
    public static final Color TYPE_PUNISH  = new Color(0xc8, 0x4a, 0x6b);

    private Theme() { }

    /** 阵营主色（卡框/插画基调） */
    public static Color[] faction(String f) {
        if (f == null) f = "";
        switch (f) {
            case "赫萨廷": return new Color[]{new Color(0x7a, 0x1f, 0x12), new Color(0xe2, 0x5a, 0x1f), new Color(0xff, 0xb3, 0x47)};
            case "纳维恩诸邑": return new Color[]{new Color(0x0c, 0x2b, 0x4e), new Color(0x1f, 0x6e, 0x9e), new Color(0x6f, 0xd6, 0xe8)};
            case "依兰维索": return new Color[]{new Color(0x1d, 0x40, 0x22), new Color(0x3f, 0x7d, 0x37), new Color(0xa7, 0xd9, 0x6b)};
            case "克莱恩书院": return new Color[]{new Color(0x2e, 0x33, 0x3c), new Color(0x5f, 0x6e, 0x7e), new Color(0x9f, 0xc4, 0xd6)};
            default:        return new Color[]{new Color(0x33, 0x27, 0x46), new Color(0x6b, 0x52, 0x96), new Color(0xc7, 0xa9, 0xe8)};
        }
    }

    public static Color factionDeep(String f)  { return faction(f)[0]; }
    public static Color factionMain(String f)  { return faction(f)[1]; }
    public static Color factionLight(String f) { return faction(f)[2]; }

    public static Color typeColor(CardDef.CardType t) {
        switch (t) {
            case MINION: return TYPE_MINION;
            case SPELL: return TYPE_SPELL;
            case AMBUSH: return TYPE_AMBUSH;
            default: return TYPE_PUNISH;
        }
    }

    public static String typeGlyph(CardDef.CardType t) {
        switch (t) {
            case MINION: return "▲";
            case SPELL: return "◆";
            case AMBUSH: return "◇";
            default: return "●";
        }
    }

    private static String cjkFont;

    /** 找一款覆盖中文的字体 */
    public static synchronized String cjk() {
        if (cjkFont != null) return cjkFont;
        String[] prefer = {"Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC",
                "WenQuanYi Micro Hei", "WenQuanYi Zen Hei", "SimSun", Font.SANS_SERIF};
        String[] have = GraphicsEnvironment.getLocalGraphicsEnvironment().getAvailableFontFamilyNames();
        for (String w : prefer)
            for (String h : have)
                if (h.equalsIgnoreCase(w)) { cjkFont = h; return h; }
        cjkFont = Font.SANS_SERIF;
        return cjkFont;
    }

    public static Font font(int style, float size) { return new Font(cjk(), style, Math.round(size)); }

    public static void paintBoard(Graphics2D g, int w, int h) {
        g.setPaint(new GradientPaint(0, 0, BOARD_TOP, 0, h, BOARD_BOTTOM));
        g.fillRect(0, 0, w, h);
        // 中央桌布光晕
        g.setPaint(new java.awt.RadialGradientPaint(new java.awt.geom.Point2D.Float(w / 2f, h / 2f),
                Math.max(w, h) / 1.4f, new float[]{0f, 1f},
                new Color[]{new Color(255, 244, 214, 14), new Color(0, 0, 0, 0)}));
        g.fillRect(0, 0, w, h);
    }

    public static Color mix(Color a, Color b, double t) {
        return new Color(
                (int) (a.getRed() + (b.getRed() - a.getRed()) * t),
                (int) (a.getGreen() + (b.getGreen() - a.getGreen()) * t),
                (int) (a.getBlue() + (b.getBlue() - a.getBlue()) * t));
    }

    public static Color alpha(Color c, int a) {
        return new Color(c.getRed(), c.getGreen(), c.getBlue(), a);
    }
}
