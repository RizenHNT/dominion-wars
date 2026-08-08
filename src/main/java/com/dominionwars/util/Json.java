package com.dominionwars.util;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 零依赖的极简 JSON 解析与序列化工具。
 * 解析结果: Map&lt;String,Object&gt; / List&lt;Object&gt; / String / Long / Double / Boolean / null
 */
public final class Json {
    private Json() {}

    // ===================== 解析 =====================
    public static Object parse(String text) {
        P p = new P(text);
        p.ws();
        Object v = p.value();
        p.ws();
        if (!p.eof()) throw p.err("多余内容");
        return v;
    }

    @SuppressWarnings("unchecked")
    public static Map<String, Object> parseObject(String text) {
        Object o = parse(text);
        if (!(o instanceof Map)) throw new IllegalArgumentException("JSON 根节点不是对象");
        return (Map<String, Object>) o;
    }

    private static final class P {
        final String s; int i = 0;
        P(String s) { this.s = s; }
        boolean eof() { return i >= s.length(); }
        char peek() { return s.charAt(i); }
        char next() { return s.charAt(i++); }
        void ws() { while (!eof() && Character.isWhitespace(peek())) i++; }
        RuntimeException err(String m) { return new IllegalArgumentException("JSON 解析错误@" + i + ": " + m); }

        Object value() {
            if (eof()) throw err("意外结束");
            char c = peek();
            switch (c) {
                case '{': return obj();
                case '[': return arr();
                case '"': return str();
                case 't': expect("true"); return Boolean.TRUE;
                case 'f': expect("false"); return Boolean.FALSE;
                case 'n': expect("null"); return null;
                default: return num();
            }
        }
        void expect(String w) {
            if (!s.startsWith(w, i)) throw err("期望 " + w);
            i += w.length();
        }
        Map<String, Object> obj() {
            Map<String, Object> m = new LinkedHashMap<>();
            next(); ws();
            if (!eof() && peek() == '}') { next(); return m; }
            while (true) {
                ws();
                if (eof() || peek() != '"') throw err("期望键名");
                String k = str(); ws();
                if (eof() || next() != ':') throw err("期望 :");
                ws();
                m.put(k, value()); ws();
                if (eof()) throw err("对象未闭合");
                char c = next();
                if (c == '}') return m;
                if (c != ',') throw err("期望 , 或 }");
            }
        }
        List<Object> arr() {
            List<Object> l = new ArrayList<>();
            next(); ws();
            if (!eof() && peek() == ']') { next(); return l; }
            while (true) {
                ws();
                l.add(value()); ws();
                if (eof()) throw err("数组未闭合");
                char c = next();
                if (c == ']') return l;
                if (c != ',') throw err("期望 , 或 ]");
            }
        }
        String str() {
            next(); // opening quote
            StringBuilder b = new StringBuilder();
            while (true) {
                if (eof()) throw err("字符串未闭合");
                char c = next();
                if (c == '"') return b.toString();
                if (c == '\\') {
                    if (eof()) throw err("转义未闭合");
                    char e = next();
                    switch (e) {
                        case '"': b.append('"'); break;
                        case '\\': b.append('\\'); break;
                        case '/': b.append('/'); break;
                        case 'b': b.append('\b'); break;
                        case 'f': b.append('\f'); break;
                        case 'n': b.append('\n'); break;
                        case 'r': b.append('\r'); break;
                        case 't': b.append('\t'); break;
                        case 'u':
                            if (i + 4 > s.length()) throw err("\\u 不完整");
                            b.append((char) Integer.parseInt(s.substring(i, i + 4), 16));
                            i += 4; break;
                        default: throw err("非法转义 \\" + e);
                    }
                } else b.append(c);
            }
        }
        Object num() {
            int st = i;
            if (!eof() && (peek() == '-' || peek() == '+')) i++;
            boolean dot = false;
            while (!eof()) {
                char c = peek();
                if (Character.isDigit(c)) i++;
                else if (c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') { dot = true; i++; }
                else break;
            }
            String t = s.substring(st, i);
            if (t.isEmpty()) throw err("非法数字");
            try {
                if (dot) return Double.parseDouble(t);
                return Long.parseLong(t);
            } catch (NumberFormatException e) { throw err("非法数字 " + t); }
        }
    }

    // ===================== 序列化 =====================
    public static String write(Object o, boolean pretty) {
        StringBuilder b = new StringBuilder();
        w(o, b, pretty, 0);
        return b.toString();
    }

    private static void indent(StringBuilder b, int d) { for (int i = 0; i < d; i++) b.append("  "); }

    @SuppressWarnings("unchecked")
    private static void w(Object o, StringBuilder b, boolean pretty, int d) {
        if (o == null) { b.append("null"); return; }
        if (o instanceof String) { wStr((String) o, b); return; }
        if (o instanceof Boolean || o instanceof Long || o instanceof Integer) { b.append(o); return; }
        if (o instanceof Number) {
            double v = ((Number) o).doubleValue();
            if (v == Math.floor(v) && !Double.isInfinite(v)) b.append((long) v);
            else b.append(v);
            return;
        }
        if (o instanceof Map) {
            Map<String, Object> m = (Map<String, Object>) o;
            if (m.isEmpty()) { b.append("{}"); return; }
            b.append('{');
            boolean first = true;
            for (Map.Entry<String, Object> e : m.entrySet()) {
                if (!first) b.append(',');
                first = false;
                if (pretty) { b.append('\n'); indent(b, d + 1); }
                wStr(e.getKey(), b);
                b.append(pretty ? ": " : ":");
                w(e.getValue(), b, pretty, d + 1);
            }
            if (pretty) { b.append('\n'); indent(b, d); }
            b.append('}');
            return;
        }
        if (o instanceof List) {
            List<Object> l = (List<Object>) o;
            if (l.isEmpty()) { b.append("[]"); return; }
            b.append('[');
            boolean first = true;
            for (Object e : l) {
                if (!first) b.append(',');
                first = false;
                if (pretty) { b.append('\n'); indent(b, d + 1); }
                w(e, b, pretty, d + 1);
            }
            if (pretty) { b.append('\n'); indent(b, d); }
            b.append(']');
            return;
        }
        wStr(String.valueOf(o), b);
    }

    private static void wStr(String s, StringBuilder b) {
        b.append('"');
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"': b.append("\\\""); break;
                case '\\': b.append("\\\\"); break;
                case '\n': b.append("\\n"); break;
                case '\r': b.append("\\r"); break;
                case '\t': b.append("\\t"); break;
                default:
                    if (c < 0x20) b.append(String.format("\\u%04x", (int) c));
                    else b.append(c);
            }
        }
        b.append('"');
    }

    // ===================== 便捷读取 =====================
    public static String str(Map<String, Object> m, String k, String def) {
        Object v = m.get(k);
        return v == null ? def : String.valueOf(v);
    }
    public static int integer(Map<String, Object> m, String k, int def) {
        Object v = m.get(k);
        if (v instanceof Number) return ((Number) v).intValue();
        if (v instanceof String) try { return Integer.parseInt((String) v); } catch (Exception ignored) {}
        return def;
    }
    public static boolean bool(Map<String, Object> m, String k, boolean def) {
        Object v = m.get(k);
        if (v instanceof Boolean) return (Boolean) v;
        if (v instanceof String) return Boolean.parseBoolean((String) v);
        return def;
    }
    @SuppressWarnings("unchecked")
    public static List<Object> list(Map<String, Object> m, String k) {
        Object v = m.get(k);
        return v instanceof List ? (List<Object>) v : new ArrayList<>();
    }
    @SuppressWarnings("unchecked")
    public static Map<String, Object> map(Map<String, Object> m, String k) {
        Object v = m.get(k);
        return v instanceof Map ? (Map<String, Object>) v : null;
    }
    public static List<String> strList(Map<String, Object> m, String k) {
        List<String> r = new ArrayList<>();
        for (Object o : list(m, k)) r.add(String.valueOf(o));
        return r;
    }
}
