using System;
using System.Collections.Generic;
using System.Linq;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 游戏初始化流程：牌组构建、翻硬币、战场选择、调整手牌。
    /// 等价原版 main.js: startGame / showCoinFlip / confirmBFSelect / confirmMulligan。
    /// 纯 C# 类，可在 EditMode 测试中直接实例化。
    /// </summary>
    public class GameInitializer
    {
        // ── 随机函数（注入可实现测试确定性）──
        // GetRandom 返回 [0, 1)，用于硬币翻转；< 0.5 → Player 先手
        public Func<float> GetRandom = () => (float)new System.Random().NextDouble();

        // 洗牌函数（注入可固定顺序）
        public Action<List<CardInstance>> ShuffleCards;
        public Action<List<RuneInstance>> ShuffleRunes;

        private readonly GameState _g;

        public GameInitializer(GameState g)
        {
            _g = g;
            ShuffleCards = DefaultShuffle;
            ShuffleRunes = DefaultShuffleRunes;
        }

        // ────────────────────────────────────────────
        // 牌组配置（分离自 ScriptableObject，EditMode 可直接注入）
        // ────────────────────────────────────────────

        /// <summary>
        /// 游戏初始化所需的牌组配置。
        /// 生产环境由 Unity 通过 ScriptableObject 填充；EditMode 测试直接 new 填写。
        /// </summary>
        public class DeckConfig
        {
            public List<CardData> playerCards;       // 玩家主牌堆（规则：40张）
            public List<CardData> enemyCards;        // AI 主牌堆（规则：40张）
            public CardData       playerLegendData;  // 玩家传奇卡 CardData
            public CardData       enemyLegendData;   // AI 传奇卡 CardData
            public List<CardData> playerBFCards;     // 玩家战场牌池（3张可选）
            public List<CardData> enemyBFCards;      // AI 战场牌池（3张可选）
            public List<RuneType> playerRuneTypes;   // 玩家符文牌堆类型列表（规则：12张）
            public List<RuneType> enemyRuneTypes;    // AI 符文牌堆类型列表
        }

        // ────────────────────────────────────────────
        // 公共方法（按流程顺序）
        // ────────────────────────────────────────────

        /// <summary>
        /// 掷硬币决定先后手。
        /// GetRandom() &lt; 0.5 → Player 先手；否则 Enemy 先手。
        /// 写入 G.first / G.turn，返回先手方 Owner。
        /// </summary>
        public Owner CoinFlip()
        {
            var result = GetRandom() < 0.5f ? Owner.Player : Owner.Enemy;
            _g.first = result;
            _g.turn  = result;
            return result;
        }

        /// <summary>
        /// 构建牌堆、符文牌堆、传奇实例、战场牌池，并各摸4张起始手牌。
        /// 等价原版 startGame() 核心逻辑（不含 UI / 硬币 / 调整手牌 步骤）。
        /// </summary>
        public void SetupDecks(DeckConfig config)
        {
            // 重置 UID 计数器，保证测试可重复
            CardInstance.ResetUidCounter();
            RuneInstance.ResetUidCounter();
            LegendInstance.ResetUidCounter();

            // ── 主牌堆 ──
            _g.pDeck = config.playerCards.Select(CardInstance.From).ToList();
            _g.eDeck = config.enemyCards.Select(CardInstance.From).ToList();
            ShuffleCards(_g.pDeck);
            ShuffleCards(_g.eDeck);

            // ── 符文牌堆 ──
            _g.pRuneDeck = config.playerRuneTypes.Select(RuneInstance.Create).ToList();
            _g.eRuneDeck = config.enemyRuneTypes.Select(RuneInstance.Create).ToList();
            ShuffleRunes(_g.pRuneDeck);
            ShuffleRunes(_g.eRuneDeck);

            // ── 传奇 ──
            _g.pLeg = LegendInstance.From(config.playerLegendData);
            _g.eLeg = LegendInstance.From(config.enemyLegendData);

            // ── 战场牌池 ──
            _g.pBFPool = config.playerBFCards.Select(CardInstance.From).ToList();
            _g.eBFPool = config.enemyBFCards.Select(CardInstance.From).ToList();

            // ── 清空区域与计数 ──
            _g.pHand.Clear();    _g.eHand.Clear();
            _g.pBase.Clear();    _g.eBase.Clear();
            _g.pDiscard.Clear(); _g.eDiscard.Clear();
            _g.pRunes.Clear();   _g.eRunes.Clear();
            _g.pSch.Reset();     _g.eSch.Reset();
            _g.pScore = 0;       _g.eScore = 0;
            _g.pMana  = 0;       _g.eMana  = 0;
            _g.round  = 1;
            _g.mulSel.Clear();
            foreach (var b in _g.bf)
            {
                b.pU.Clear(); b.eU.Clear();
                b.ctrl = null; b.conqDone = false; b.card = null;
            }

            // ── 初始摸牌（各4张）──
            DrawN(_g.pDeck, _g.pHand, 4);
            DrawN(_g.eDeck, _g.eHand, 4);
        }

        /// <summary>
        /// 从各自战场牌池随机选1张，放入 G.bf[0].card（玩家方）和 G.bf[1].card（AI方）。
        /// 等价原版 confirmBFSelect()。
        /// 需在 SetupDecks() 之后调用（pBFPool / eBFPool 须已填充）。
        /// </summary>
        public void SelectBattlefields()
        {
            var pPool = new List<CardInstance>(_g.pBFPool);
            var ePool = new List<CardInstance>(_g.eBFPool);
            ShuffleCards(pPool);
            ShuffleCards(ePool);
            _g.bf[0].card = pPool[0];
            _g.bf[1].card = ePool[0];
        }

        /// <summary>
        /// 玩家调整手牌：将选中位置的牌（最多2张）放回牌库底，摸等量新牌补充。
        /// 等价原版 confirmMulligan()。
        /// </summary>
        /// <param name="selectedIndices">手牌位置索引列表（0-based），最多2个，允许重复/越界（自动过滤）。</param>
        public void ConfirmMulligan(List<int> selectedIndices)
        {
            if (selectedIndices == null || selectedIndices.Count == 0)
            {
                _g.mulSel.Clear();
                return;
            }

            // 过滤无效索引，去重，降序排列（避免移除时索引位移），最多取2
            var indices = selectedIndices
                .Where(i => i >= 0 && i < _g.pHand.Count)
                .Distinct()
                .OrderByDescending(i => i)
                .Take(2)
                .ToList();

            var shelved = new List<CardInstance>();
            foreach (var i in indices)
            {
                shelved.Add(_g.pHand[i]);
                _g.pHand.RemoveAt(i);
            }

            // 摸补充牌（等量）
            DrawN(_g.pDeck, _g.pHand, shelved.Count);

            // 搁置的牌放入牌库底（index 0 = 底部）
            foreach (var c in shelved)
                _g.pDeck.Insert(0, c);

            _g.mulSel.Clear();
        }

        // ────────────────────────────────────────────
        // 辅助方法
        // ────────────────────────────────────────────

        /// <summary>
        /// 从 deck 顶部（List 末尾）抽 n 张到 hand，不超过 MAX_HAND。
        /// </summary>
        private static void DrawN(List<CardInstance> deck, List<CardInstance> hand, int n)
        {
            for (int i = 0; i < n && deck.Count > 0 && hand.Count < GameState.MAX_HAND; i++)
            {
                hand.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }
        }

        /// <summary>
        /// Fisher-Yates 洗牌（CardInstance 列表）。
        /// </summary>
        private static void DefaultShuffle(List<CardInstance> list)
        {
            var rng = new System.Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Fisher-Yates 洗牌（RuneInstance 列表）。
        /// </summary>
        private static void DefaultShuffleRunes(List<RuneInstance> list)
        {
            var rng = new System.Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ────────────────────────────────────────────
        // 卡莎 / 无极剑圣 符文牌堆工厂（便利方法）
        // ────────────────────────────────────────────

        /// <summary>卡莎（虚空）符文类型列表：炽烈×7 + 灵光×5</summary>
        public static List<RuneType> KaisaRuneTypes() =>
            Enumerable.Repeat(RuneType.Blazing, 7)
                      .Concat(Enumerable.Repeat(RuneType.Radiant, 5))
                      .ToList();

        /// <summary>无极剑圣（伊欧尼亚）符文类型列表：翠意×6 + 摧破×6</summary>
        public static List<RuneType> MasterYiRuneTypes() =>
            Enumerable.Repeat(RuneType.Verdant, 6)
                      .Concat(Enumerable.Repeat(RuneType.Crushing, 6))
                      .ToList();
    }
}
