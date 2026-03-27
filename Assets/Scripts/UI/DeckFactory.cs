using System.Collections.Generic;
using FWTCG.Core;
using FWTCG.Data;
using UnityEngine;

namespace FWTCG.UI
{
    /// <summary>
    /// 运行时构造卡莎 / 易大师牌组配置。
    /// 因 ScriptableObject .asset 文件尚未在 Unity Editor 中创建，
    /// 本类用 ScriptableObject.CreateInstance 在内存中生成等价数据，
    /// 供 GameInitializer.SetupDecks 直接消费。
    ///
    /// 卡牌数据来源：原版 cards.js，字段含义与 CardData.cs 完全对应。
    /// </summary>
    public static class DeckFactory
    {
        // ─────────────────────────────────────────────
        // 公共入口
        // ─────────────────────────────────────────────

        public static GameInitializer.DeckConfig MakeKaisaVsMasterYi()
        {
            return new GameInitializer.DeckConfig
            {
                playerCards      = KaisaMainDeck(),
                enemyCards       = MasterYiMainDeck(),
                playerLegendData = KaisaLegend(),
                enemyLegendData  = MasterYiLegend(),
                playerBFCards    = SharedBFPool(),
                enemyBFCards     = SharedBFPool(),
                playerRuneTypes  = KaisaRuneTypes(),
                enemyRuneTypes   = MasterYiRuneTypes(),
            };
        }

        // ─────────────────────────────────────────────
        // 符文牌堆
        // ─────────────────────────────────────────────

        public static List<RuneType> KaisaRuneTypes() => new List<RuneType>
        {
            RuneType.Blazing, RuneType.Blazing, RuneType.Blazing,
            RuneType.Blazing, RuneType.Blazing, RuneType.Blazing, RuneType.Blazing,
            RuneType.Radiant, RuneType.Radiant, RuneType.Radiant,
            RuneType.Radiant, RuneType.Radiant,
        };

        public static List<RuneType> MasterYiRuneTypes() => new List<RuneType>
        {
            RuneType.Verdant,  RuneType.Verdant,  RuneType.Verdant,
            RuneType.Verdant,  RuneType.Verdant,  RuneType.Verdant,
            RuneType.Crushing, RuneType.Crushing, RuneType.Crushing,
            RuneType.Crushing, RuneType.Crushing, RuneType.Crushing,
        };

        // ─────────────────────────────────────────────
        // 传奇牌
        // ─────────────────────────────────────────────

        static CardData KaisaLegend() => Make(new CardData
        {
            id       = "kaisa_legend",
            name     = "卡莎",
            type     = CardType.Champion,
            cost     = 5, atk = 5, hp = 14,
            keywords = new List<string> { "迅捷", "虚空感知" },
            text     = "被动：盟友拥有4种不同关键词时进化+3/+3。主动：消耗本体，给法术+1炽烈符能。",
            emoji    = "🌌",
            effect   = "kaisa_active",
        });

        static CardData MasterYiLegend() => Make(new CardData
        {
            id       = "masteryi_legend",
            name     = "易大师",
            type     = CardType.Champion,
            cost     = 5, atk = 5, hp = 12,
            keywords = new List<string> { "急速", "独影剑鸣" },
            text     = "被动：1名盟友独自防守时该单位本回合+2攻击力。",
            emoji    = "⚔️",
            effect   = "masteryi_defend_buff",
        });

        // ─────────────────────────────────────────────
        // 卡莎主牌堆（40 张）
        // ─────────────────────────────────────────────

        static List<CardData> KaisaMainDeck()
        {
            var d = new List<CardData>();

            // ── 随从 ──
            Add(d, 3, new CardData { id = "voidling",          cardName = "虚空碎片",   type = CardType.Follower,   cost = 1, atk = 1, keywords = new List<string>{"绝念"}, emoji = "💠", effect = "voidling",          text = "绝念：在手牌中生成1张「碎片」。" });
            Add(d, 3, new CardData { id = "void_sentinel",     cardName = "虚空哨兵",   type = CardType.Follower,   cost = 2, atk = 2, keywords = new List<string>{"绝念"}, emoji = "👁️", effect = "void_sentinel",     text = "绝念：你的下一个盟友入场时获得+1/+1。" });
            Add(d, 3, new CardData { id = "alert_sentinel",    cardName = "警觉哨兵",   type = CardType.Follower,   cost = 2, atk = 2, keywords = new List<string>{"绝念"}, emoji = "🔔", effect = "alert_sentinel",    text = "绝念：摸1张牌。" });
            Add(d, 2, new CardData { id = "yordel_instructor", cardName = "约德尔教官", type = CardType.Follower,   cost = 3, atk = 3, emoji = "📚", effect = "yordel_instructor_enter", text = "入场：摸1张牌。" });
            Add(d, 2, new CardData { id = "darius",            cardName = "德莱厄斯",   type = CardType.Follower,   cost = 4, atk = 4, keywords = new List<string>{"坚守"}, emoji = "🪓", effect = "darius_second_card",  text = "入场：本回合已出牌≥2时获得+2/+2并重置疲惫。" });
            Add(d, 2, new CardData { id = "thousand_tail",     cardName = "千尾监视者", type = CardType.Follower,   cost = 5, atk = 5, keywords = new List<string>{"压制"}, emoji = "🐙", effect = "thousand_tail_enter", text = "入场：所有敌方单位本回合-3攻击力。" });
            Add(d, 2, new CardData { id = "tiyana",            cardName = "缇亚娜·冕卫",type = CardType.Follower,   cost = 3, atk = 3, emoji = "👑", effect = "tiyana_enter",           text = "入场：对手本回合不得获得据守分。" });
            Add(d, 1, new CardData { id = "wailing_poro",      cardName = "嚎叫波洛",   type = CardType.Follower,   cost = 1, atk = 1, keywords = new List<string>{"绝念"}, emoji = "🐾", effect = "wailing_poro",       text = "绝念：该区无其他盟友时摸1张牌。" });

            // ── 法术 ──
            Add(d, 3, new CardData { id = "buff_ally_card",    cardName = "虚空增幅",   type = CardType.Spell,      cost = 2, emoji = "⬆️", effect = "buff_ally",   text = "给1名盟友永久+1/+1。", keywords = new List<string>{"迅捷"} });
            Add(d, 3, new CardData { id = "stun_card",         cardName = "冰冻封印",   type = CardType.Spell,      cost = 3, emoji = "❄️", effect = "stun",        text = "眩晕1名敌方单位（本回合不能行动）。" });
            Add(d, 3, new CardData { id = "deal3_card",        cardName = "虚空轰击",   type = CardType.Spell,      cost = 3, emoji = "💥", effect = "deal3",       text = "对战场上1名敌方单位造成3点伤害。" });
            Add(d, 2, new CardData { id = "buff_draw_card",    cardName = "晋升仪式",   type = CardType.Spell,      cost = 4, emoji = "📖", effect = "buff_draw",   text = "给1名盟友永久+2/+2，然后摸1张牌。" });
            Add(d, 2, new CardData { id = "recall_draw_card",  cardName = "空间折叠",   type = CardType.Spell,      cost = 3, emoji = "🔮", effect = "recall_draw", text = "召回1名盟友，然后摸1张牌。" });
            Add(d, 2, new CardData { id = "deal3_twice_card",  cardName = "双重虚空波", type = CardType.Spell,      cost = 5, emoji = "💫", effect = "deal3_twice", text = "对战场上1名敌方单位造成3点伤害，再次对1名敌方单位造成3点伤害。" });
            Add(d, 1, new CardData { id = "draw1_card",        cardName = "虚空探测",   type = CardType.Spell,      cost = 1, emoji = "🔍", effect = "draw1",       text = "摸1张牌。" });
            Add(d, 1, new CardData { id = "counter_any_card",  cardName = "虚空反制",   type = CardType.Spell,      cost = 4, emoji = "🛡️", effect = "counter_any", text = "反制1个法术。", keywords = new List<string>{"反应"} });

            // ── 装备 ──
            Add(d, 2, new CardData { id = "trinity_equip",     cardName = "三相之力",   type = CardType.Equipment,  cost = 3, atkBonus = 2, emoji = "🔱", effect = "trinity_equip",  text = "装配于单位：+2攻击力，「三相之力」激活。", equipSchCost = 1, equipSchType = RuneType.Blazing });
            Add(d, 2, new CardData { id = "guardian_equip",    cardName = "守护天使",   type = CardType.Equipment,  cost = 3, atkBonus = 1, emoji = "💫", effect = "guardian_equip", text = "装配于单位：+1攻击力，死亡护盾。",             equipSchCost = 1, equipSchType = RuneType.Radiant });
            Add(d, 1, new CardData { id = "death_shield",      cardName = "中娅沙漏",   type = CardType.Equipment,  cost = 2, atkBonus = 0, emoji = "⏳", effect = "death_shield",   text = "装配于基地：一次性保护1名盟友免于死亡。",       equipSchCost = 0 });

            return d;
        }

        // ─────────────────────────────────────────────
        // 易大师主牌堆（40 张）
        // ─────────────────────────────────────────────

        static List<CardData> MasterYiMainDeck()
        {
            var d = new List<CardData>();

            // ── 随从 ──
            Add(d, 3, new CardData { id = "wailing_poro",      cardName = "嚎叫波洛",   type = CardType.Follower,   cost = 1, atk = 1, keywords = new List<string>{"绝念"}, emoji = "🐾", effect = "wailing_poro",       text = "绝念：该区无其他盟友时摸1张牌。" });
            Add(d, 3, new CardData { id = "alert_sentinel",    cardName = "警觉哨兵",   type = CardType.Follower,   cost = 2, atk = 2, keywords = new List<string>{"绝念"}, emoji = "🔔", effect = "alert_sentinel",    text = "绝念：摸1张牌。" });
            Add(d, 3, new CardData { id = "malphite",          cardName = "熔岩巨兽",   type = CardType.Follower,   cost = 4, atk = 4, keywords = new List<string>{"坚守"}, emoji = "⛰️", effect = "malph_enter",        text = "入场：场上每有1名坚守盟友获得+1攻击力。" });
            Add(d, 2, new CardData { id = "jax",               cardName = "贾克斯",     type = CardType.Follower,   cost = 4, atk = 4, keywords = new List<string>{"急速"}, emoji = "🏮", effect = "jax_enter",          text = "入场：手牌中所有装备获得【反应】。" });
            Add(d, 2, new CardData { id = "darius",            cardName = "德莱厄斯",   type = CardType.Follower,   cost = 4, atk = 4, keywords = new List<string>{"坚守"}, emoji = "🪓", effect = "darius_second_card",  text = "入场：本回合已出牌≥2时获得+2/+2并重置疲惫。" });
            Add(d, 2, new CardData { id = "yordel_instructor", cardName = "约德尔教官", type = CardType.Follower,   cost = 3, atk = 3, emoji = "📚", effect = "yordel_instructor_enter", text = "入场：摸1张牌。" });
            Add(d, 2, new CardData { id = "foresight_mech",    cardName = "先见机甲",   type = CardType.Follower,   cost = 3, atk = 3, emoji = "🤖", effect = "foresight_enter",         text = "入场：查看牌堆顶，可选择回收至库底。" });
            Add(d, 1, new CardData { id = "tiyana",            cardName = "缇亚娜·冕卫",type = CardType.Follower,   cost = 3, atk = 3, emoji = "👑", effect = "tiyana_enter",            text = "入场：对手本回合不得获得据守分。" });

            // ── 法术 ──
            Add(d, 3, new CardData { id = "weaken_card",       cardName = "削弱之击",   type = CardType.Spell,      cost = 2, emoji = "⬇️", effect = "weaken",      text = "使1名敌方单位本回合-4攻击力。" });
            Add(d, 3, new CardData { id = "ready_unit_card",   cardName = "唤醒战意",   type = CardType.Spell,      cost = 2, emoji = "⚡", effect = "ready_unit",  text = "移除1名单位的疲惫状态。",  keywords = new List<string>{"迅捷"} });
            Add(d, 3, new CardData { id = "force_move_card",   cardName = "强制移位",   type = CardType.Spell,      cost = 3, emoji = "🌀", effect = "force_move",  text = "将1名敌方单位强制移动到另一个战场。" });
            Add(d, 2, new CardData { id = "buff5_manual_card", cardName = "极限强化",   type = CardType.Spell,      cost = 5, emoji = "💪", effect = "buff5_manual", text = "给1名单位+5/+5（本回合）。", schCost = 2, schType = RuneType.Verdant });
            Add(d, 2, new CardData { id = "buff2_draw_card",   cardName = "修炼传承",   type = CardType.Spell,      cost = 4, emoji = "📜", effect = "buff2_draw",  text = "给1名单位本回合+2攻击力，然后摸1张牌。" });
            Add(d, 2, new CardData { id = "draw1_card",        cardName = "冥想洞察",   type = CardType.Spell,      cost = 1, emoji = "🔍", effect = "draw1",       text = "摸1张牌。" });
            Add(d, 1, new CardData { id = "recall_unit_rune_card", cardName = "召归炼化", type = CardType.Spell,    cost = 3, emoji = "♻️", effect = "recall_unit_rune", text = "召回1名盟友，获得1枚对应符文。", keywords = new List<string>{"迅捷"} });
            Add(d, 1, new CardData { id = "counter_cost4_card",cardName = "高段反击",   type = CardType.Spell,      cost = 3, emoji = "🛡️", effect = "counter_cost4", text = "反制费用≤4的法术。", keywords = new List<string>{"反应"} });

            // ── 装备 ──
            Add(d, 2, new CardData { id = "dorans_equip",      cardName = "多兰之刃",   type = CardType.Equipment,  cost = 2, atkBonus = 2, emoji = "🗡️", effect = "dorans_equip",   text = "装配于单位：+2攻击力。", equipSchCost = 1, equipSchType = RuneType.Crushing });
            Add(d, 2, new CardData { id = "trinity_equip",     cardName = "三相之力",   type = CardType.Equipment,  cost = 3, atkBonus = 2, emoji = "🔱", effect = "trinity_equip",  text = "装配于单位：+2攻击力，「三相之力」激活。", equipSchCost = 1, equipSchType = RuneType.Verdant });

            return d;
        }

        // ─────────────────────────────────────────────
        // 战场牌池（两侧共用，各从3张中选1）
        // ─────────────────────────────────────────────

        static List<CardData> SharedBFPool() => new List<CardData>
        {
            Make(new CardData { id = "reckoner_arena",  cardName = "清算人竞技场", type = CardType.Battlefield, emoji = "🏟️", effect = "reckoner_arena",  text = "战斗开始时攻击力≥5的单位获得【强攻】/【坚守】。" }),
            Make(new CardData { id = "void_gate",       cardName = "虚空之门",     type = CardType.Battlefield, emoji = "🌌", effect = "void_gate",       text = "法术/技能伤害+1。" }),
            Make(new CardData { id = "altar_unity",     cardName = "统御祭坛",     type = CardType.Battlefield, emoji = "⛩️", effect = "altar_unity",     text = "据守：召唤1名2/2新兵。" }),
            Make(new CardData { id = "strength_obelisk",cardName = "力量方尖碑",   type = CardType.Battlefield, emoji = "🗿", effect = "strength_obelisk", text = "据守/征服：各获得1枚翠意符文。" }),
            Make(new CardData { id = "dreaming_tree",   cardName = "梦境之树",     type = CardType.Battlefield, emoji = "🌳", effect = "dreaming_tree",   text = "法术目标为此处盟友时：摸1张牌。" }),
            Make(new CardData { id = "reaver_row",      cardName = "掠夺者走廊",   type = CardType.Battlefield, emoji = "⚔️", effect = "reaver_row",      text = "征服：召回废牌堆中费用≤2的1个单位。" }),
        };

        // ─────────────────────────────────────────────
        // 辅助
        // ─────────────────────────────────────────────

        static void Add(List<CardData> deck, int count, CardData data)
        {
            Make(data);
            for (int i = 0; i < count; i++) deck.Add(data);
        }

        /// <summary>
        /// 用 CreateInstance 把一个已填好字段的 CardData 转为合法的 ScriptableObject。
        /// 直接 new CardData() 在 Unity 中不会触发 ScriptableObject 初始化路径。
        /// </summary>
        static CardData Make(CardData template)
        {
            var so = ScriptableObject.CreateInstance<CardData>();
            so.id            = template.id;
            so.cardName      = template.cardName;
            so.name          = template.cardName;   // Unity editor display name
            so.type          = template.type;
            so.cost          = template.cost;
            so.atk           = template.atk;
            so.hp            = template.hp;
            so.atkBonus      = template.atkBonus;
            so.keywords      = template.keywords ?? new List<string>();
            so.text          = template.text;
            so.emoji         = template.emoji;
            so.effect        = template.effect;
            so.schCost       = template.schCost;
            so.schType       = template.schType;
            so.equipSchCost  = template.equipSchCost;
            so.equipSchType  = template.equipSchType;
            return so;
        }
    }
}
