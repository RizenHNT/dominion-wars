/* ================================================================
 * 统御战纪 · 界面参数总配置（P3）
 * ----------------------------------------------------------------
 * 改这个文件就能调整界面，无需读懂其余代码。保存后刷新浏览器生效。
 * 服务端节奏（AI 出招速度）在 data/ui.json 里调。
 * ================================================================ */
window.DW_CONFIG = {

  /* ---------- 节奏（毫秒） ---------- */
  pollMs: 320,          // 客户端拉取状态的间隔（越小越跟手，越大越省电）
  bannerMs: 1500,       // 行动播报横幅停留时长
  turnBannerMs: 1600,   // "你的回合"过场横幅时长
  dmgPopMs: 1000,       // 伤害飘字时长
  flipMs: 350,          // 卡牌移动动画时长
  hitFlashMs: 500,      // 受击红闪时长
  toastMs: 2200,        // 错误提示停留时长

  /* ---------- 默认语言：zh / ja / en ---------- */
  defaultLang: "zh",

  /* ---------- 教程 ---------- */
  tutorialAutoShow: true,   // 首次对局自动弹出教程

  /* ---------- 尺寸与排版（CSS 变量） ---------- */
  cssVars: {
    "--card-w": "128px",        // 卡牌宽
    "--card-h": "178px",        // 卡牌高
    "--card-w-s": "90px",       // 小卡（伏击区）宽
    "--card-h-s": "124px",      // 小卡高
    "--hand-h": "210px",        // 手牌区高度
    "--hand-lift": "-30px",     // 手牌悬停抬升量
    "--hand-overlap": "-12px",  // 手牌重叠（负值越大越挤）
    "--log-w": "270px",         // 日志栏宽度
    "--castle-w": "min(420px, 38vw)", // 王城血条宽

    /* ---------- 颜色 ---------- */
    "--gold": "#d9b25f",            // 主题金
    "--text-main": "#e8e4d8",
    "--text-dim": "#9a978e",
    "--glow-usable": "rgba(87,217,122,.7)",   // 可用绿光
    "--glow-select": "rgba(255,158,46,.85)",  // 选中橙光
    "--glow-target": "rgba(232,77,77,.85)",   // 目标红光
    "--arrow": "#e84d4d",                     // 攻击箭头
    "--f-flame": "#c4502a",     // 阵营色条：烈焰
    "--f-sea": "#2a7fb5",       // 深海
    "--f-wood": "#4f9143",      // 古木
    "--f-machine": "#7c8da0",   // 机械
    "--f-neutral": "#8a67c0",   // 中立
    "--t-minion": "#3d6ea5",    // 类型条：随从
    "--t-spell": "#3f8ad6",     // 咒文
    "--t-ambush": "#7a4fc0",    // 伏击
    "--t-punish": "#c0452a",    // 惩罚
    "--bg-board": "radial-gradient(ellipse at 50% 30%, #232a3d 0%, #11141d 60%, #0a0c12 100%)",

    /* ---------- 图片皮肤插槽 ----------
     * 把素材放进 web/skin/ 后取消注释即可逐张替换（全部插槽见 web/skin/说明.txt）：
     * "--img-page-bg": "url('skin/page-bg.jpg')",
     * "--img-battle-bg": "url('skin/battle-bg.jpg')",
     * "--img-card-frame": "url('skin/card-frame.png')",
     * "--img-card-frame-leader": "url('skin/card-frame-leader.png')",
     * "--img-card-back": "url('skin/card-back.png')",
     * "--img-endturn": "url('skin/end-turn.png')",
     * "--img-punish-badge": "url('skin/punish.png')",
     * "--img-gem-atk": "url('skin/gem-atk.png')",
     * "--img-gem-hp": "url('skin/gem-hp.png')",
     */
  },
};
