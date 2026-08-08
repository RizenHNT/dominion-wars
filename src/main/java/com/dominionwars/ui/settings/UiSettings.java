package com.dominionwars.ui.settings;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Locale;
import java.util.Properties;

/**
 * 轻量 UI 设置。当前先做全局静态设置，后续可由 Claude 重构为可注入配置。
 */
public final class UiSettings {
    private UiSettings() { }

    public enum Language {
        ZH("中文"), JA("日本語"), EN("English");
        public final String label;
        Language(String label) { this.label = label; }
        @Override public String toString() { return label; }
    }

    public enum Density {
        COMPACT("紧凑 / Compact"), NORMAL("标准 / Normal"), LARGE("宽松 / Large");
        public final String label;
        Density(String label) { this.label = label; }
        @Override public String toString() { return label; }
    }

    public static Language language = detectLanguage();
    public static Density density = Density.NORMAL;
    public static boolean showTooltips = true;
    public static boolean showOpponentAmbushCount = true;

    private static final Path SETTINGS_PATH = Path.of(System.getProperty("user.home", "."), ".dominion-wars-ui.properties");

    public static void load() {
        if (!Files.exists(SETTINGS_PATH)) return;
        Properties p = new Properties();
        try (InputStream in = Files.newInputStream(SETTINGS_PATH)) {
            p.load(in);
            language = parseEnum(Language.class, p.getProperty("language"), language);
            density = parseEnum(Density.class, p.getProperty("density"), density);
            showTooltips = Boolean.parseBoolean(p.getProperty("showTooltips", Boolean.toString(showTooltips)));
            showOpponentAmbushCount = Boolean.parseBoolean(p.getProperty("showOpponentAmbushCount", Boolean.toString(showOpponentAmbushCount)));
        } catch (Exception ignored) { }
    }

    public static void save() {
        Properties p = new Properties();
        p.setProperty("language", language.name());
        p.setProperty("density", density.name());
        p.setProperty("showTooltips", Boolean.toString(showTooltips));
        p.setProperty("showOpponentAmbushCount", Boolean.toString(showOpponentAmbushCount));
        try (OutputStream out = Files.newOutputStream(SETTINGS_PATH)) {
            p.store(out, "Dominion Wars UI settings");
        } catch (IOException ignored) { }
    }

    private static Language detectLanguage() {
        String lang = Locale.getDefault().getLanguage();
        if ("ja".equalsIgnoreCase(lang)) return Language.JA;
        if ("en".equalsIgnoreCase(lang)) return Language.EN;
        return Language.ZH;
    }

    private static <T extends Enum<T>> T parseEnum(Class<T> type, String raw, T fallback) {
        if (raw == null) return fallback;
        try { return Enum.valueOf(type, raw); } catch (Exception e) { return fallback; }
    }

    public static int cardWidth() {
        return switch (density) {
            case COMPACT -> 122;
            case NORMAL -> 146;
            case LARGE -> 170;
        };
    }

    public static int cardHeight() {
        return switch (density) {
            case COMPACT -> 168;
            case NORMAL -> 200;
            case LARGE -> 232;
        };
    }

    public static int fieldCardWidth() {
        return switch (density) {
            case COMPACT -> 108;
            case NORMAL -> 126;
            case LARGE -> 148;
        };
    }

    public static int fieldCardHeight() {
        return switch (density) {
            case COMPACT -> 148;
            case NORMAL -> 172;
            case LARGE -> 200;
        };
    }

    public static int columns() {
        return switch (density) {
            case COMPACT -> 10;
            case NORMAL -> 8;
            case LARGE -> 6;
        };
    }

    public static float uiFontSize() {
        return switch (density) {
            case COMPACT -> 12f;
            case NORMAL -> 13f;
            case LARGE -> 15f;
        };
    }
}
