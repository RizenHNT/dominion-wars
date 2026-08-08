package com.dominionwars.server;

import com.dominionwars.engine.CardInstance;
import com.dominionwars.engine.Game;
import com.dominionwars.engine.PlayerAgent;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.SynchronousQueue;

/**
 * 网页版人类代理：引擎在对局线程上回调本类时，把决策内容发布给 UI（pending），
 * 然后阻塞等待 HTTP 送来的答案。引擎与 UI 由此完全解耦——引擎只认 PlayerAgent 接口。
 */
public class WebHumanAgent implements PlayerAgent {

    /** 待 UI 应答的决策 */
    public static class Pending {
        public final long id;
        public final String kind;       // activatePunish / target / discard / ambush / singleDamage / coreTarget
        public final String prompt;
        public final boolean optional;
        public final List<Object> raw;  // 引擎原始选项（CardInstance / SingleDamageTarget / CoreTarget）
        Pending(long id, String kind, String prompt, boolean optional, List<Object> raw) {
            this.id = id; this.kind = kind; this.prompt = prompt; this.optional = optional; this.raw = raw;
        }
    }

    private final GameSession session;
    private long nextId = 1;
    private volatile Pending pending;
    private final SynchronousQueue<Integer> answers = new SynchronousQueue<>();

    public WebHumanAgent(GameSession session) { this.session = session; }

    public Pending pending() { return pending; }

    /** HTTP 线程送达答案：choice = 选项下标；-1 = 放弃（仅 optional） */
    public boolean answer(long id, int choice) {
        Pending p = pending;
        if (p == null || p.id != id) return false;
        return answers.offer(choice);
    }

    private int ask(String kind, String prompt, boolean optional, List<Object> raw) {
        Pending p = new Pending(nextId++, kind, prompt, optional, raw);
        pending = p;
        session.publish();                 // 让 UI 立刻看到待决策
        try {
            while (true) {
                Integer a = answers.take(); // 对局线程在此阻塞
                if (a == null) continue;
                if (a == -1 && !optional && !"activatePunish".equals(kind)) continue; // 非可选不接受放弃
                if (a >= raw.size()) continue;
                return a;
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return optional ? -1 : 0;
        } finally {
            pending = null;
            session.publish();
        }
    }

    @Override
    public boolean askActivatePunish(Game g, int playerIdx, CardInstance card, int cost) {
        List<Object> raw = new ArrayList<>();
        raw.add(card);
        // choice 0 = 发动；-1 = 不发动
        return ask("activatePunish", "是否发动【" + card.def.name + "】的惩罚效果？（惩罚 " + cost + "）", true, raw) == 0;
    }

    @Override
    public CardInstance chooseTarget(Game g, int playerIdx, List<CardInstance> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        int c = ask("target", prompt, optional, new ArrayList<>(options));
        return c < 0 ? null : options.get(c);
    }

    @Override
    public CardInstance chooseDiscard(Game g, int playerIdx, List<CardInstance> hand) {
        if (hand.isEmpty()) return null;
        int c = ask("discard", "请选择一张手牌弃置（超出弃牌上限）", false, new ArrayList<>(hand));
        return hand.get(Math.max(0, c));
    }

    @Override
    public CardInstance chooseAmbush(Game g, int playerIdx, List<CardInstance> candidates, String actionDesc) {
        if (candidates.isEmpty()) return null;
        int c = ask("ambush", "对方行动：" + actionDesc + "。是否触发伏击？", true, new ArrayList<>(candidates));
        return c < 0 ? null : candidates.get(c);
    }

    @Override
    public Game.SingleDamageTarget chooseSingleDamageTarget(Game g, int playerIdx, List<Game.SingleDamageTarget> options, String prompt, boolean optional) {
        if (options.isEmpty()) return null;
        int c = ask("singleDamage", prompt, optional, new ArrayList<>(options));
        return c < 0 ? null : options.get(c);
    }

    @Override
    public Game.CoreTarget chooseCoreTarget(Game g, int playerIdx, List<Game.CoreTarget> options, String prompt, int amount, boolean optional) {
        if (options.isEmpty()) return null;
        int c = ask("coreTarget", prompt + "（伤害 " + amount + "）", optional, new ArrayList<>(options));
        return c < 0 ? null : options.get(c);
    }
}
