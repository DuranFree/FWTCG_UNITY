using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P10 行为验证测试 — 游戏初始化流程
    /// 覆盖：GameInitializer（CoinFlip / SetupDecks / SelectBattlefields / ConfirmMulligan）
    ///        TurnTimerSystem（Reset / Start / Stop / Tick / OnTimeout）
    ///        LocalizationTable（Get / Format）
    ///        CardDeployer.foresight_mech_enter（PromptForesight delegate）
    /// </summary>
    public class GameInitializerTests
    {
        private GameState        _g;
        private GameInitializer  _gi;
        private TurnManager      _tm;
        private CardDeployer     _cd;

        [SetUp]
        public void SetUp()
        {
            CardInstance.ResetUidCounter();
            RuneInstance.ResetUidCounter();
            LegendInstance.ResetUidCounter();

            _g  = new GameState();
            _tm = new TurnManager(_g);
            _cd = new CardDeployer(_g, _tm);
            _gi = new GameInitializer(_g);
        }

        // ─────────────────────────────────────────
        // 辅助工厂
        // ─────────────────────────────────────────

        private static CardData MakeCardData(string id, CardType type = CardType.Follower,
            int cost = 1, int atk = 2, int hp = 0, string effect = "")
        {
            var d = ScriptableObject.CreateInstance<CardData>();
            d.id       = id;
            d.cardName = id;
            d.type     = type;
            d.cost     = cost;
            d.atk      = atk;
            d.hp       = hp;
            d.effect   = effect;
            d.keywords = new List<string>();
            return d;
        }

        private static CardData MakeLegend(string id, int hp = 14, int atk = 5)
        {
            var d = ScriptableObject.CreateInstance<CardData>();
            d.id       = id;
            d.cardName = id;
            d.type     = CardType.Champion;
            d.hp       = hp;
            d.atk      = atk;
            d.keywords = new List<string>();
            return d;
        }

        /// <summary>构造含 n 张卡的牌堆配置</summary>
        private static List<CardData> MakeDeck(int n, string prefix = "card")
            => Enumerable.Range(0, n).Select(i => MakeCardData($"{prefix}_{i}")).ToList();

        /// <summary>构造含 3 张战场牌的列表</summary>
        private static List<CardData> MakeBFPool(string prefix = "bf")
            => Enumerable.Range(0, 3).Select(i => MakeCardData($"{prefix}_{i}", CardType.Battlefield)).ToList();

        private GameInitializer.DeckConfig MakeConfig(int deckSize = 40) => new()
        {
            playerCards      = MakeDeck(deckSize, "p"),
            enemyCards       = MakeDeck(deckSize, "e"),
            playerLegendData = MakeLegend("kaisa", 14),
            enemyLegendData  = MakeLegend("yi",    12),
            playerBFCards    = MakeBFPool("pbf"),
            enemyBFCards     = MakeBFPool("ebf"),
            playerRuneTypes  = GameInitializer.KaisaRuneTypes(),
            enemyRuneTypes   = GameInitializer.MasterYiRuneTypes(),
        };

        // ─────────────────────────────────────────
        // CoinFlip 测试
        // ─────────────────────────────────────────

        [Test]
        public void CoinFlip_LowRandom_ReturnsPlayer()
        {
            _gi.GetRandom = () => 0.3f;
            var result = _gi.CoinFlip();
            Assert.AreEqual(Owner.Player, result);
        }

        [Test]
        public void CoinFlip_HighRandom_ReturnsEnemy()
        {
            _gi.GetRandom = () => 0.7f;
            var result = _gi.CoinFlip();
            Assert.AreEqual(Owner.Enemy, result);
        }

        [Test]
        public void CoinFlip_SetsGFirst_Player()
        {
            _gi.GetRandom = () => 0.1f;
            _gi.CoinFlip();
            Assert.AreEqual(Owner.Player, _g.first);
            Assert.AreEqual(Owner.Player, _g.turn);
        }

        [Test]
        public void CoinFlip_SetsGFirst_Enemy()
        {
            _gi.GetRandom = () => 0.9f;
            _gi.CoinFlip();
            Assert.AreEqual(Owner.Enemy, _g.first);
            Assert.AreEqual(Owner.Enemy, _g.turn);
        }

        [Test]
        public void CoinFlip_Boundary_ExactlyHalf_IsEnemy()
        {
            // 0.5f >= 0.5f → enemy
            _gi.GetRandom = () => 0.5f;
            var result = _gi.CoinFlip();
            Assert.AreEqual(Owner.Enemy, result);
        }

        // ─────────────────────────────────────────
        // SetupDecks 测试
        // ─────────────────────────────────────────

        [Test]
        public void SetupDecks_BothDecksHave36After4Drawn()
        {
            _gi.SetupDecks(MakeConfig(40));
            Assert.AreEqual(36, _g.pDeck.Count, "player deck: 40 - 4 drawn = 36");
            Assert.AreEqual(36, _g.eDeck.Count, "enemy deck: 40 - 4 drawn = 36");
        }

        [Test]
        public void SetupDecks_BothHandsHave4Cards()
        {
            _gi.SetupDecks(MakeConfig(40));
            Assert.AreEqual(4, _g.pHand.Count);
            Assert.AreEqual(4, _g.eHand.Count);
        }

        [Test]
        public void SetupDecks_PlayerRuneDeck_Kaisa_7Blazing_5Radiant()
        {
            _gi.SetupDecks(MakeConfig());
            Assert.AreEqual(7, _g.pRuneDeck.Count(r => r.runeType == RuneType.Blazing));
            Assert.AreEqual(5, _g.pRuneDeck.Count(r => r.runeType == RuneType.Radiant));
        }

        [Test]
        public void SetupDecks_EnemyRuneDeck_Yi_6Verdant_6Crushing()
        {
            _gi.SetupDecks(MakeConfig());
            Assert.AreEqual(6, _g.eRuneDeck.Count(r => r.runeType == RuneType.Verdant));
            Assert.AreEqual(6, _g.eRuneDeck.Count(r => r.runeType == RuneType.Crushing));
        }

        [Test]
        public void SetupDecks_PlayerLegend_Kaisa_Hp14()
        {
            _gi.SetupDecks(MakeConfig());
            Assert.IsNotNull(_g.pLeg);
            Assert.AreEqual(14, _g.pLeg.currentHp);
            Assert.AreEqual(14, _g.pLeg.maxHp);
        }

        [Test]
        public void SetupDecks_EnemyLegend_Yi_Hp12()
        {
            _gi.SetupDecks(MakeConfig());
            Assert.IsNotNull(_g.eLeg);
            Assert.AreEqual(12, _g.eLeg.currentHp);
        }

        [Test]
        public void SetupDecks_BFPools_Have3CardsEach()
        {
            _gi.SetupDecks(MakeConfig());
            Assert.AreEqual(3, _g.pBFPool.Count);
            Assert.AreEqual(3, _g.eBFPool.Count);
        }

        [Test]
        public void SetupDecks_ClearsAllPreviousGameState()
        {
            _g.pScore = 5; _g.eScore = 3;
            _g.pMana = 4; _g.eMana = 2;
            _g.pDiscard.Add(new CardInstance());
            _gi.SetupDecks(MakeConfig());
            Assert.AreEqual(0, _g.pScore, "pScore reset");
            Assert.AreEqual(0, _g.eScore, "eScore reset");
            Assert.AreEqual(0, _g.pMana,  "pMana reset");
            Assert.AreEqual(0, _g.eMana,  "eMana reset");
            Assert.AreEqual(0, _g.pDiscard.Count, "pDiscard cleared");
        }

        [Test]
        public void SetupDecks_SmallDeck_LimitedDraw()
        {
            // 只有2张牌，摸牌不超过牌堆数量
            _gi.SetupDecks(MakeConfig(2));
            Assert.AreEqual(0, _g.pDeck.Count);   // 2张全摸走
            Assert.AreEqual(2, _g.pHand.Count);
        }

        // ─────────────────────────────────────────
        // SelectBattlefields 测试
        // ─────────────────────────────────────────

        [Test]
        public void SelectBattlefields_AssignsBFCards_NotNull()
        {
            _gi.SetupDecks(MakeConfig());
            _gi.SelectBattlefields();
            Assert.IsNotNull(_g.bf[0].card);
            Assert.IsNotNull(_g.bf[1].card);
        }

        [Test]
        public void SelectBattlefields_Bf0_ComesFromPlayerPool()
        {
            _gi.SetupDecks(MakeConfig());
            // 不洗牌→取第一张
            _gi.SelectBattlefields();
            Assert.IsTrue(_g.bf[0].card.id.StartsWith("pbf"));
        }

        [Test]
        public void SelectBattlefields_Bf1_ComesFromEnemyPool()
        {
            _gi.SetupDecks(MakeConfig());
            _gi.SelectBattlefields();
            Assert.IsTrue(_g.bf[1].card.id.StartsWith("ebf"));
        }

        // ─────────────────────────────────────────
        // ConfirmMulligan 测试
        // ─────────────────────────────────────────

        [Test]
        public void ConfirmMulligan_Empty_HandUnchanged()
        {
            _gi.SetupDecks(MakeConfig());
            var originalHand = new List<CardInstance>(_g.pHand);
            _gi.ConfirmMulligan(new List<int>());
            CollectionAssert.AreEqual(
                originalHand.Select(c => c.id),
                _g.pHand.Select(c => c.id));
        }

        [Test]
        public void ConfirmMulligan_Null_HandUnchanged()
        {
            _gi.SetupDecks(MakeConfig());
            var originalHand = _g.pHand.Select(c => c.id).ToList();
            _gi.ConfirmMulligan(null);
            CollectionAssert.AreEqual(originalHand, _g.pHand.Select(c => c.id));
        }

        [Test]
        public void ConfirmMulligan_OneCard_HandStillHas4()
        {
            _gi.SetupDecks(MakeConfig());
            _gi.ConfirmMulligan(new List<int> { 0 });
            Assert.AreEqual(4, _g.pHand.Count);
        }

        [Test]
        public void ConfirmMulligan_TwoCards_HandStillHas4()
        {
            _gi.SetupDecks(MakeConfig());
            _gi.ConfirmMulligan(new List<int> { 0, 2 });
            Assert.AreEqual(4, _g.pHand.Count);
        }

        [Test]
        public void ConfirmMulligan_MaxEnforced_ThreeIndices_OnlySwapsTwo()
        {
            _gi.SetupDecks(MakeConfig());
            // 提交 3 个索引，超出 2 的限制
            _gi.ConfirmMulligan(new List<int> { 0, 1, 2 });
            Assert.AreEqual(4, _g.pHand.Count);
        }

        [Test]
        public void ConfirmMulligan_ShelvedCard_AtDeckBottom()
        {
            // 不洗牌：牌堆顺序为 p_0..p_39，库顶 = p_39
            // 手牌应为 p_39, p_38, p_37, p_36
            _gi.SetupDecks(MakeConfig());

            var shelvedCard = _g.pHand[0]; // 第0张手牌 = p_39

            _gi.ConfirmMulligan(new List<int> { 0 });

            // shelved card 应在牌库底（index 0）
            Assert.AreEqual(shelvedCard.id, _g.pDeck[0].id);
        }

        [Test]
        public void ConfirmMulligan_DeckShrinksByReplaceDraw()
        {
            _gi.SetupDecks(MakeConfig());
            int deckBefore = _g.pDeck.Count; // 36
            _gi.ConfirmMulligan(new List<int> { 0 });
            // 摸1张新牌（-1），放入库底1张（+1）→ 净不变
            Assert.AreEqual(deckBefore, _g.pDeck.Count);
        }

        [Test]
        public void ConfirmMulligan_ClearsMulSel()
        {
            _gi.SetupDecks(MakeConfig());
            _g.mulSel.Add(_g.pHand[0]);
            _gi.ConfirmMulligan(new List<int> { 0 });
            Assert.AreEqual(0, _g.mulSel.Count);
        }

        // ─────────────────────────────────────────
        // TurnTimerSystem 测试
        // ─────────────────────────────────────────

        [Test]
        public void Timer_Reset_SetsToTimerSeconds()
        {
            var timer = new TurnTimerSystem();
            timer.Reset();
            Assert.AreEqual(GameState.TIMER_SECONDS, timer.TimeRemaining);
        }

        [Test]
        public void Timer_NotRunning_AfterReset()
        {
            var timer = new TurnTimerSystem();
            timer.Reset();
            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void Timer_Start_IsRunning()
        {
            var timer = new TurnTimerSystem();
            timer.Reset();
            timer.Start();
            Assert.IsTrue(timer.IsRunning);
        }

        [Test]
        public void Timer_Stop_NotRunning()
        {
            var timer = new TurnTimerSystem();
            timer.Reset();
            timer.Start();
            timer.Stop();
            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void Timer_Tick_Decrements()
        {
            var timer = new TurnTimerSystem();
            timer.Reset();
            timer.Start();
            timer.Tick(5f);
            Assert.AreEqual(GameState.TIMER_SECONDS - 5, timer.TimeRemaining);
        }

        [Test]
        public void Timer_Tick_NotRunning_DoesNotDecrement()
        {
            var timer = new TurnTimerSystem();
            timer.Reset();
            // 不调用 Start()
            timer.Tick(10f);
            Assert.AreEqual(GameState.TIMER_SECONDS, timer.TimeRemaining);
        }

        [Test]
        public void Timer_Tick_AtZero_FiresOnTimeout()
        {
            var timer = new TurnTimerSystem();
            bool fired = false;
            timer.OnTimeout = () => fired = true;
            timer.Reset();
            timer.Start();
            timer.Tick(30f);
            Assert.IsTrue(fired);
        }

        [Test]
        public void Timer_Tick_AtZero_StopsTimer()
        {
            var timer = new TurnTimerSystem();
            timer.OnTimeout = () => { };
            timer.Reset();
            timer.Start();
            timer.Tick(30f);
            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void Timer_Tick_BelowZero_ClampedToZero()
        {
            var timer = new TurnTimerSystem();
            timer.OnTimeout = () => { };
            timer.Reset();
            timer.Start();
            timer.Tick(100f);
            Assert.AreEqual(0, timer.TimeRemaining);
        }

        [Test]
        public void Timer_TimeRemaining_IsZeroAfterFullTick()
        {
            var timer = new TurnTimerSystem();
            timer.OnTimeout = () => { };
            timer.Reset();
            timer.Start();
            timer.Tick(GameState.TIMER_SECONDS);
            Assert.AreEqual(0, timer.TimeRemaining);
        }

        // ─────────────────────────────────────────
        // LocalizationTable 测试
        // ─────────────────────────────────────────

        [Test]
        public void Localization_Get_KnownKey_ReturnsChineseString()
        {
            var result = LocalizationTable.Get("phase.awaken");
            Assert.AreEqual("觉醒阶段", result);
        }

        [Test]
        public void Localization_Get_UnknownKey_ReturnsKeyItself()
        {
            var result = LocalizationTable.Get("nonexistent.key");
            Assert.AreEqual("nonexistent.key", result);
        }

        [Test]
        public void Localization_Format_ReplacesPlaceholder()
        {
            var result = LocalizationTable.Format("round", 3);
            Assert.AreEqual("第 3 回合", result);
        }

        [Test]
        public void Localization_Format_MultiplePlaceholders()
        {
            var result = LocalizationTable.Format("mulligan.selected", 2);
            Assert.AreEqual("已选 2/2 张搁置（再次点击取消）", result);
        }

        [Test]
        public void Localization_Get_AllPhases_Defined()
        {
            // 确保所有 GamePhase 都有对应字符串
            foreach (var key in new[] { "phase.init", "phase.awaken", "phase.start",
                                         "phase.summon", "phase.draw", "phase.action", "phase.end" })
            {
                Assert.AreNotEqual(key, LocalizationTable.Get(key),
                    $"Missing localization key: {key}");
            }
        }

        // ─────────────────────────────────────────
        // ForesightMech (CardDeployer) 测试
        // ─────────────────────────────────────────

        private CardDeployer MakeDeployer()
        {
            var g  = new GameState();
            var tm = new TurnManager(g);
            return new CardDeployer(g, tm);
        }

        [Test]
        public void ForesightMech_DoNotRecycle_DeckUnchanged()
        {
            var cd = MakeDeployer();
            cd.PromptForesight = _ => false;  // 不回收

            var topCard = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "topcard", cardName = "TopCard",
                type = CardType.Follower, atk = 2, currentAtk = 2, currentHp = 2,
                tb = new TurnBuffs(), keywords = new List<string>()
            };
            var bottomCard = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "bottomcard", cardName = "BottomCard",
                type = CardType.Follower, atk = 1, currentAtk = 1, currentHp = 1,
                tb = new TurnBuffs(), keywords = new List<string>()
            };

            cd.G.pDeck.Add(bottomCard);
            cd.G.pDeck.Add(topCard);    // 库顶

            var foresight = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "foresight_mech", cardName = "先见机甲",
                type = CardType.Follower, atk = 2, currentAtk = 2, currentHp = 2,
                effect = "foresight_mech_enter", tb = new TurnBuffs(), keywords = new List<string>()
            };

            cd.OnSummon(foresight, Owner.Player);

            // 不回收 → 库顶保持 topCard（最后一张）
            Assert.AreEqual("topcard", cd.G.pDeck[cd.G.pDeck.Count - 1].id);
            Assert.AreEqual(2, cd.G.pDeck.Count);
        }

        [Test]
        public void ForesightMech_Recycle_MovesTopCardToBottom()
        {
            var cd = MakeDeployer();
            cd.PromptForesight = _ => true;  // 回收

            var topCard = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "topcard", cardName = "TopCard",
                type = CardType.Follower, atk = 2, currentAtk = 2, currentHp = 2,
                tb = new TurnBuffs(), keywords = new List<string>()
            };
            var bottomCard = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "bottomcard", cardName = "BottomCard",
                type = CardType.Follower, atk = 1, currentAtk = 1, currentHp = 1,
                tb = new TurnBuffs(), keywords = new List<string>()
            };

            cd.G.pDeck.Add(bottomCard);
            cd.G.pDeck.Add(topCard);    // 库顶

            var foresight = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "foresight_mech", cardName = "先见机甲",
                type = CardType.Follower, atk = 2, currentAtk = 2, currentHp = 2,
                effect = "foresight_mech_enter", tb = new TurnBuffs(), keywords = new List<string>()
            };

            cd.OnSummon(foresight, Owner.Player);

            // 回收 → topCard 移到库底（index 0），bottomCard 上升为新库顶
            Assert.AreEqual("bottomcard", cd.G.pDeck[cd.G.pDeck.Count - 1].id,
                "新库顶应为 bottomCard");
            Assert.AreEqual("topcard", cd.G.pDeck[0].id,
                "topCard 应在库底（index 0）");
            Assert.AreEqual(2, cd.G.pDeck.Count, "牌库总数不变");
        }

        [Test]
        public void ForesightMech_EmptyDeck_NoError()
        {
            var cd = MakeDeployer();
            cd.PromptForesight = _ => true;

            // 空牌库 → 不应抛异常
            var foresight = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "foresight_mech", cardName = "先见机甲",
                type = CardType.Follower, atk = 2, currentAtk = 2, currentHp = 2,
                effect = "foresight_mech_enter", tb = new TurnBuffs(), keywords = new List<string>()
            };
            Assert.DoesNotThrow(() => cd.OnSummon(foresight, Owner.Player));
        }

        [Test]
        public void ForesightMech_PromptReceives_TopCardInstance()
        {
            var cd = MakeDeployer();
            CardInstance receivedCard = null;
            cd.PromptForesight = card => { receivedCard = card; return false; };

            var topCard = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "topcard", cardName = "TopCard",
                type = CardType.Follower, atk = 2, currentAtk = 2, currentHp = 2,
                tb = new TurnBuffs(), keywords = new List<string>()
            };
            cd.G.pDeck.Add(topCard);

            var foresight = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "foresight_mech", cardName = "先见机甲",
                type = CardType.Follower, atk = 2, currentAtk = 2, currentHp = 2,
                effect = "foresight_mech_enter", tb = new TurnBuffs(), keywords = new List<string>()
            };
            cd.OnSummon(foresight, Owner.Player);

            Assert.AreSame(topCard, receivedCard, "PromptForesight 应收到库顶 CardInstance");
        }

        // ─────────────────────────────────────────
        // KaisaRuneTypes / MasterYiRuneTypes 工厂测试
        // ─────────────────────────────────────────

        [Test]
        public void KaisaRuneTypes_Returns12Total()
        {
            var types = GameInitializer.KaisaRuneTypes();
            Assert.AreEqual(12, types.Count);
        }

        [Test]
        public void KaisaRuneTypes_7Blazing_5Radiant()
        {
            var types = GameInitializer.KaisaRuneTypes();
            Assert.AreEqual(7, types.Count(t => t == RuneType.Blazing));
            Assert.AreEqual(5, types.Count(t => t == RuneType.Radiant));
        }

        [Test]
        public void MasterYiRuneTypes_Returns12Total()
        {
            var types = GameInitializer.MasterYiRuneTypes();
            Assert.AreEqual(12, types.Count);
        }

        [Test]
        public void MasterYiRuneTypes_6Verdant_6Crushing()
        {
            var types = GameInitializer.MasterYiRuneTypes();
            Assert.AreEqual(6, types.Count(t => t == RuneType.Verdant));
            Assert.AreEqual(6, types.Count(t => t == RuneType.Crushing));
        }
    }
}
