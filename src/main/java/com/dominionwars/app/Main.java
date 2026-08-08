package com.dominionwars.app;

import com.dominionwars.ui.MainMenu;
import com.dominionwars.ui.settings.UiSettings;

import javax.swing.*;
import java.awt.*;
import java.nio.file.Files;
import java.nio.file.Path;

/** 程序入口：设置外观与中文字体后打开主菜单。数据从工作目录 data/ 读取。 */
public class Main {

    public static void main(String[] args) throws Exception {
        boolean web = false;
        int port = 8137;
        String dataArg = null;
        for (String a : args) {
            if (a.equals("--web")) web = true;
            else if (a.startsWith("--port=")) port = Integer.parseInt(a.substring(7));
            else dataArg = a;
        }
        Path dataDir = Path.of(dataArg != null ? dataArg : "data");
        if (!Files.isDirectory(dataDir)) {
            System.err.println("警告：未找到数据目录 " + dataDir.toAbsolutePath()
                    + "，请从项目根目录运行（或将 data 目录路径作为第一个参数传入）。");
        }
        if (web) {
            // 网页版：引擎做本地服务，浏览器渲染界面（引擎与 UI 解耦）
            com.dominionwars.server.WebServer.launch(dataDir, Path.of("web"), port);
            Thread.currentThread().join();   // 常驻
            return;
        }
        UiSettings.load();
        SwingUtilities.invokeLater(() -> {
            setupLookAndFeel();
            new MainMenu(dataDir).setVisible(true);
        });
    }

    private static void setupLookAndFeel() {
        try {
            for (UIManager.LookAndFeelInfo info : UIManager.getInstalledLookAndFeels()) {
                if ("Nimbus".equals(info.getName())) {
                    UIManager.setLookAndFeel(info.getClassName());
                    break;
                }
            }
        } catch (Exception ignored) { }
        // 选择一款覆盖中文的字体，避免缺字
        String[] preferred = {"Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC",
                "WenQuanYi Micro Hei", "SimSun", "Dialog"};
        String chosen = "Dialog";
        var available = GraphicsEnvironment.getLocalGraphicsEnvironment().getAvailableFontFamilyNames();
        outer:
        for (String want : preferred) {
            for (String have : available) {
                if (have.equalsIgnoreCase(want)) { chosen = have; break outer; }
            }
        }
        Font f = new Font(chosen, Font.PLAIN, Math.round(UiSettings.uiFontSize()));
        for (var key : java.util.Collections.list(UIManager.getDefaults().keys())) {
            Object v = UIManager.get(key);
            if (v instanceof javax.swing.plaf.FontUIResource) {
                UIManager.put(key, new javax.swing.plaf.FontUIResource(f));
            }
        }
    }
}
