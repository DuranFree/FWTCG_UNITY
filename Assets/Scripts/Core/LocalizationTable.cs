using System.Collections.Generic;

namespace FWTCG.Core
{
    /// <summary>
    /// 本地化字符串表（当前阶段：仅中文）。
    /// 静态字典；未来可替换为 ScriptableObject / JSON 驱动的多语言方案。
    /// </summary>
    public static class LocalizationTable
    {
        private static readonly Dictionary<string, string> _zh = new()
        {
            // ── 游戏阶段 ──
            ["phase.init"]    = "初始化",
            ["phase.awaken"]  = "觉醒阶段",
            ["phase.start"]   = "开始阶段",
            ["phase.summon"]  = "召唤阶段",
            ["phase.draw"]    = "抽牌阶段",
            ["phase.action"]  = "行动阶段",
            ["phase.end"]     = "结束阶段",

            // ── 回合 ──
            ["turn.player"]   = "玩家回合",
            ["turn.enemy"]    = "AI 回合",
            ["turn.first"]    = "先手",
            ["turn.second"]   = "后手",

            // ── 翻硬币 ──
            ["coin.flipping"]         = "正在抛掷硬币...",
            ["coin.player_first"]     = "硬币落地！【你是先手】",
            ["coin.enemy_first"]      = "硬币落地！【AI 是先手】",
            ["coin.first_strike"]     = "FIRST STRIKE",
            ["coin.reactive_stance"]  = "REACTIVE STANCE",

            // ── 调整手牌 ──
            ["mulligan.title"]        = "调整手牌",
            ["mulligan.info"]         = "你的传奇",
            ["mulligan.none"]         = "当前选择：不换任何牌",
            ["mulligan.selected"]     = "已选 {0}/2 张搁置（再次点击取消）",
            ["mulligan.over_limit"]   = "最多只能搁置 2 张牌！",
            ["mulligan.confirm"]      = "确认",

            // ── 区域 ──
            ["zone.hand"]     = "手牌",
            ["zone.deck"]     = "牌库",
            ["zone.base"]     = "基地",
            ["zone.discard"]  = "废牌堆",
            ["zone.exile"]    = "放逐堆",
            ["zone.bf"]       = "战场",

            // ── 战斗 ──
            ["combat.start"]     = "战斗开始",
            ["combat.conquer"]   = "征服！",
            ["combat.hold"]      = "据守！",
            ["combat.retreat"]   = "撤退",

            // ── 法术对决 ──
            ["duel.start"]    = "法术对决开始",
            ["duel.skip"]     = "跳过",
            ["duel.end"]      = "对决结束",

            // ── 积分 ──
            ["score.player"]  = "玩家得分",
            ["score.enemy"]   = "AI 得分",
            ["score.win"]     = "胜利！",
            ["score.lose"]    = "失败！",
            ["score.draw"]    = "平局",

            // ── 符文 ──
            ["rune.tap"]       = "点击符文",
            ["rune.recycle"]   = "回收符文",
            ["rune.tap_all"]   = "全点符文",
            ["rune.blazing"]   = "炽烈符文",
            ["rune.radiant"]   = "灵光符文",
            ["rune.verdant"]   = "翠意符文",
            ["rune.crushing"]  = "摧破符文",
            ["rune.chaos"]     = "混沌符文",
            ["rune.order"]     = "序理符文",

            // ── 关键词 ──
            ["kw.rush"]        = "急速",
            ["kw.swift"]       = "迅捷",
            ["kw.reaction"]    = "反应",
            ["kw.barrier"]     = "壁垒",
            ["kw.overwhelm"]   = "强攻",
            ["kw.guard"]       = "坚守",
            ["kw.deathwish"]   = "绝念",
            ["kw.echo"]        = "回响",
            ["kw.conquer"]     = "征服",
            ["kw.rally"]       = "鼓舞",
            ["kw.foresight"]   = "预知",

            // ── 计时器 ──
            ["timer.remaining"]   = "剩余时间",
            ["timer.timeout"]     = "时间到，自动结束回合",

            // ── 先见机甲 ──
            ["foresight.peek"]    = "查看牌库顶",
            ["foresight.recycle"] = "回收至牌库底",
            ["foresight.keep"]    = "保留",

            // ── 通用 ──
            ["btn.confirm"]  = "确认",
            ["btn.cancel"]   = "取消",
            ["btn.end_turn"] = "结束回合",
            ["btn.pass"]     = "跳过",
            ["game.start"]   = "游戏开始！",
            ["game.over"]    = "游戏结束",
            ["round"]        = "第 {0} 回合",
        };

        /// <summary>
        /// 根据 key 获取中文字符串。未知 key 直接返回 key 本身（方便调试）。
        /// </summary>
        public static string Get(string key)
            => _zh.TryGetValue(key, out var v) ? v : key;

        /// <summary>
        /// 格式化：先 Get(key)，再 string.Format(args)。
        /// 用于含 {0} 占位符的字符串。
        /// </summary>
        public static string Format(string key, params object[] args)
            => string.Format(Get(key), args);
    }
}
