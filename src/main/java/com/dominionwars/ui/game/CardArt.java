package com.dominionwars.ui.game;

import javax.imageio.ImageIO;
import java.awt.image.BufferedImage;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * 卡图加载器：优先使用 data/cardart/&lt;卡牌id&gt;.png（或 .jpg），
 * 不存在时返回 null，由 CardPanel 退回程序化纹章。
 * 用户可直接把图片丢进 cardart 文件夹，或在卡牌编辑器中选择图片。
 */
public final class CardArt {
    private static final Map<String, BufferedImage> CACHE = new HashMap<>();
    private static final BufferedImage MISSING = new BufferedImage(1, 1, BufferedImage.TYPE_INT_ARGB);
    public static Path artDir = Path.of("data", "cardart");

    private CardArt() { }

    public static synchronized BufferedImage of(String cardId) {
        BufferedImage img = CACHE.get(cardId);
        if (img != null) return img == MISSING ? null : img;
        BufferedImage loaded = null;
        for (String ext : new String[]{".png", ".jpg", ".jpeg"}) {
            Path f = artDir.resolve(cardId + ext);
            if (Files.exists(f)) {
                try { loaded = ImageIO.read(f.toFile()); } catch (Exception ignored) { }
                if (loaded != null) break;
            }
        }
        CACHE.put(cardId, loaded == null ? MISSING : loaded);
        return loaded;
    }

    /** 编辑器替换卡图后调用，使新图立即生效 */
    public static synchronized void invalidate(String cardId) { CACHE.remove(cardId); }
}
