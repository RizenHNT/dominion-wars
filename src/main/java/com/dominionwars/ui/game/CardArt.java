package com.dominionwars.ui.game;

import javax.imageio.ImageIO;
import java.awt.image.BufferedImage;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * 卡图加载器：使用 data/art/&lt;卡牌id&gt;.png（或 .jpg），
 * 并兼容旧的 data/cardart/ 目录；不存在时返回 null，由 CardPanel
 * 退回程序化纹章。data/art 是当前统一入口，编辑器和 Web 端也使用它。
 */
public final class CardArt {
    private static final Map<String, BufferedImage> CACHE = new HashMap<>();
    private static final BufferedImage MISSING = new BufferedImage(1, 1, BufferedImage.TYPE_INT_ARGB);
    /** 当前统一卡图目录。保留旧目录回退，避免已有用户素材失效。 */
    public static Path artDir = Path.of("data", "art");
    public static Path legacyArtDir = Path.of("data", "cardart");

    private CardArt() { }

    public static synchronized BufferedImage of(String cardId) {
        BufferedImage img = CACHE.get(cardId);
        if (img != null) return img == MISSING ? null : img;
        BufferedImage loaded = null;
        for (Path dir : new Path[]{artDir, legacyArtDir}) {
            for (String ext : new String[]{".png", ".jpg", ".jpeg"}) {
                Path f = dir.resolve(cardId + ext);
                if (Files.exists(f)) {
                    try { loaded = ImageIO.read(f.toFile()); } catch (Exception ignored) { }
                    if (loaded != null) break;
                }
            }
            if (loaded != null) break;
        }
        CACHE.put(cardId, loaded == null ? MISSING : loaded);
        return loaded;
    }

    /** 编辑器替换卡图后调用，使新图立即生效 */
    public static synchronized void invalidate(String cardId) { CACHE.remove(cardId); }
}
