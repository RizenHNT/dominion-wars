package com.dominionwars.data;

import com.dominionwars.model.CardDef;
import com.dominionwars.util.Json;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;

/** 卡库：加载 data/cards/*.json（每个文件为一个卡牌数组，通常按阵营分文件） */
public class CardLibrary {
    public final Map<String, CardDef> byId = new LinkedHashMap<>();
    public final Map<String, List<CardDef>> byFile = new LinkedHashMap<>();

    public static CardLibrary load(Path cardsDir) {
        CardLibrary lib = new CardLibrary();
        if (!Files.isDirectory(cardsDir)) return lib;
        try (Stream<Path> s = Files.list(cardsDir)) {
            s.filter(p -> p.toString().endsWith(".json")).sorted().forEach(p -> {
                try { lib.loadFile(p); } catch (Exception e) {
                    System.err.println("卡牌文件加载失败 " + p + ": " + e.getMessage());
                }
            });
        } catch (IOException e) {
            System.err.println("卡库目录读取失败: " + e.getMessage());
        }
        return lib;
    }

    @SuppressWarnings("unchecked")
    public void loadFile(Path p) throws IOException {
        Object root = Json.parse(Files.readString(p));
        List<Object> arr;
        if (root instanceof List) arr = (List<Object>) root;
        else arr = Json.list((Map<String, Object>) root, "cards");
        List<CardDef> cards = new ArrayList<>();
        for (Object o : arr) {
            CardDef c = CardDef.fromMap((Map<String, Object>) o);
            if (c.id.isEmpty()) continue;
            cards.add(c);
            byId.put(c.id, c);
        }
        byFile.put(p.getFileName().toString(), cards);
    }

    public CardDef get(String id) { return byId.get(id); }

    public static void saveFile(Path p, List<CardDef> cards) throws IOException {
        List<Object> arr = new ArrayList<>();
        for (CardDef c : cards) arr.add(c.toMap());
        Files.createDirectories(p.getParent());
        Files.writeString(p, Json.write(arr, true));
    }

    /** 卡组定义：data/decks/*.json  {"name":..,"faction":..,"leader":"卡id","cards":{"卡id":数量,...}} */
    public static class DeckDef {
        public String name = "未命名卡组";
        public String faction = "无阵营";
        public String leaderId = "";
        public Map<String, Integer> counts = new LinkedHashMap<>();

        public static DeckDef load(Path p) throws IOException {
            Map<String, Object> m = Json.parseObject(Files.readString(p));
            DeckDef d = new DeckDef();
            d.name = Json.str(m, "name", p.getFileName().toString());
            d.faction = Json.str(m, "faction", "无阵营");
            d.leaderId = Json.str(m, "leader", "");
            Map<String, Object> c = Json.map(m, "cards");
            if (c != null) for (Map.Entry<String, Object> e : c.entrySet())
                d.counts.put(e.getKey(), ((Number) e.getValue()).intValue());
            return d;
        }

        /** 展开为卡牌定义列表（含统领1张），并做基础校验 */
        public List<CardDef> build(CardLibrary lib, Balance bal, List<String> problems) {
            List<CardDef> deck = new ArrayList<>();
            CardDef leader = lib.get(leaderId);
            if (leader == null) problems.add("统领牌不存在: " + leaderId);
            else if (!leader.leader) problems.add(leaderId + " 不是统领牌");
            else deck.add(leader);
            for (Map.Entry<String, Integer> e : counts.entrySet()) {
                CardDef c = lib.get(e.getKey());
                if (c == null) { problems.add("卡牌不存在: " + e.getKey()); continue; }
                if (c.leader) { problems.add("统领牌不能作为普通卡加入: " + e.getKey()); continue; }
                for (int i = 0; i < e.getValue(); i++) deck.add(c);
            }
            if (deck.size() < bal.deckMin || deck.size() > bal.deckMax)
                problems.add("卡组张数 " + deck.size() + " 不在 [" + bal.deckMin + "," + bal.deckMax + "] 内");
            return deck;
        }
    }

    public static List<Path> listDecks(Path decksDir) {
        List<Path> r = new ArrayList<>();
        if (!Files.isDirectory(decksDir)) return r;
        try (Stream<Path> s = Files.list(decksDir)) {
            s.filter(p -> p.toString().endsWith(".json")).sorted().forEach(r::add);
        } catch (IOException ignored) {}
        return r;
    }
}
