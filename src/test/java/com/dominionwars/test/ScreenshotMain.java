package com.dominionwars.test;

import com.dominionwars.ai.AiAgent;
import com.dominionwars.data.Balance;
import com.dominionwars.data.CardLibrary;
import com.dominionwars.engine.Game;
import com.dominionwars.model.CardDef;
import com.dominionwars.ui.game.GameWindow;

import javax.imageio.ImageIO;
import javax.swing.SwingUtilities;
import java.awt.image.BufferedImage;
import java.io.File;
import java.lang.reflect.Method;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/** UI 截图工具（Xvfb 下运行）：推进若干 AI 回合后渲染对战窗口保存 PNG */
public class ScreenshotMain {
    public static void main(String[] args) throws Exception {
        int halfTurns = args.length > 0 ? Integer.parseInt(args[0]) : 9;
        String out = args.length > 1 ? args[1] : "/tmp/ui.png";
        String deckA = args.length > 2 ? args[2] : "data/decks/flame_deck.json";
        String deckB = args.length > 3 ? args[3] : "data/decks/sea_deck.json";

        Balance bal = Balance.load(Path.of("data/balance.json"));
        CardLibrary lib = CardLibrary.load(Path.of("data/cards"));
        CardLibrary.DeckDef da = CardLibrary.DeckDef.load(Path.of(deckA));
        CardLibrary.DeckDef db = CardLibrary.DeckDef.load(Path.of(deckB));
        List<String> p = new ArrayList<>();
        List<CardDef> la = da.build(lib, bal, p), lb = db.build(lib, bal, p);
        AiAgent x = new AiAgent(), y = new AiAgent();
        Game g = new Game(bal, la, lb, "你（" + da.name + "）", "AI（" + db.name + "）", x, y, 20260612L, 0);
        g.library = lib;
        g.start();
        for (int i = 0; i < halfTurns && !g.over(); i++) (g.currentIdx == 0 ? x : y).playTurn(g);

        SwingUtilities.invokeAndWait(() -> {
            try {
                com.dominionwars.app.Main.class.getDeclaredMethod("setupLookAndFeel");
            } catch (Exception ignored) { }
            GameWindow w = new GameWindow(g, true, false);
            w.setVisible(true);
            try {
                Method refresh = GameWindow.class.getDeclaredMethod("refresh");
                refresh.setAccessible(true);
                refresh.invoke(w);
            } catch (Exception e) { e.printStackTrace(); }
        });
        Thread.sleep(1200);
        SwingUtilities.invokeAndWait(() -> {
            try {
                java.awt.Window w = java.awt.Window.getWindows()[0];
                BufferedImage img = new BufferedImage(w.getWidth(), w.getHeight(), BufferedImage.TYPE_INT_RGB);
                w.paint(img.getGraphics());
                ImageIO.write(img, "png", new File(out));
                System.out.println("已保存截图: " + out + " (" + w.getWidth() + "x" + w.getHeight() + ")");
            } catch (Exception e) { e.printStackTrace(); }
        });
        System.exit(0);
    }
}
