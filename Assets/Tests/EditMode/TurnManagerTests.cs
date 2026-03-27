using NUnit.Framework;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P2 行为验证测试 — TurnManager
    /// 对照原版：engine.js doAwaken / doSummon / doDraw / doEndPhase / addScore / checkWin
    /// </summary>
    public class TurnManagerTests
    {
        private GameState _g;
        private TurnManager _tm;

        [SetUp]
        public void SetUp()
        {
            CardInstance.ResetUidCounter();
            _g = new GameState();
            _tm = new TurnManager(_g);
        }

        // ─────────────────────────────────────────
        // DoAwaken
        // ─────────────────────────────────────────

        [Test]
        public void DoAwaken_ResetsExhaustedUnits()
        {
            var u = MakeUnit("u1", 3);
            u.exhausted = true;
            _g.pBase.Add(u);

            _g.turn = Owner.Player;
            _tm.DoAwaken();

            Assert.IsFalse(u.exhausted);
        }

        [Test]
        public void DoAwaken_UntapsRunes()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            r.tapped = true;
            _g.pRunes.Add(r);

            _g.turn = Owner.Player;
            _tm.DoAwaken();

            Assert.IsFalse(r.tapped);
        }

        [Test]
        public void DoAwaken_ResetsSch()
        {
            _g.pSch.Add(RuneType.Blazing, 3);
            _g.turn = Owner.Player;
            _tm.DoAwaken();

            Assert.AreEqual(0, _g.pSch.Total());
        }

        [Test]
        public void DoAwaken_ClearsCardsPlayedThisTurn()
        {
            _g.cardsPlayedThisTurn = 5;
            _g.turn = Owner.Player;
            _tm.DoAwaken();

            Assert.AreEqual(0, _g.cardsPlayedThisTurn);
        }

        [Test]
        public void DoAwaken_ClearsPendingRunes_ForPlayer()
        {
            _g.pendingRunes.Add(new PendingRune { idx = 0, action = PendingRuneAction.Tap });
            _g.turn = Owner.Player;
            _tm.DoAwaken();

            Assert.AreEqual(0, _g.pendingRunes.Count);
        }

        [Test]
        public void DoAwaken_ResetsLegendExhausted()
        {
            var legData = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            legData.atk = 5; legData.hp = 14;
            _g.pLeg = LegendInstance.From(legData);
            _g.pLeg.exhausted = true;

            _g.turn = Owner.Player;
            _tm.DoAwaken();

            Assert.IsFalse(_g.pLeg.exhausted);
        }

        // ─────────────────────────────────────────
        // DoSummon
        // ─────────────────────────────────────────

        [Test]
        public void DoSummon_DrawsTwoRunes_NormalTurn()
        {
            // 先手方非首回合，应抽2张
            _g.turn  = Owner.Player;
            _g.first = Owner.Player;
            _g.pFirstTurnDone = true; // 非首回合
            AddRunes(_g.pRuneDeck, 5);

            _tm.DoSummon();

            Assert.AreEqual(2, _g.pRunes.Count);
            Assert.AreEqual(3, _g.pRuneDeck.Count);
        }

        [Test]
        public void DoSummon_DrawsThreeRunes_BackPlayerFirstTurn()
        {
            // 后手方 + 首回合 = 3张
            _g.turn  = Owner.Player;
            _g.first = Owner.Enemy;   // 玩家是后手
            _g.pFirstTurnDone = false;
            AddRunes(_g.pRuneDeck, 5);

            _tm.DoSummon();

            Assert.AreEqual(3, _g.pRunes.Count);
        }

        [Test]
        public void DoSummon_DoesNotExceedRuneDeck()
        {
            _g.turn  = Owner.Player;
            _g.first = Owner.Player;
            _g.pFirstTurnDone = true;
            AddRunes(_g.pRuneDeck, 1); // 只有1张

            _tm.DoSummon();

            Assert.AreEqual(1, _g.pRunes.Count);  // 只拿到1张
            Assert.AreEqual(0, _g.pRuneDeck.Count);
        }

        // ─────────────────────────────────────────
        // DoDraw
        // ─────────────────────────────────────────

        [Test]
        public void DoDraw_DrawsCardFromDeck()
        {
            _g.turn = Owner.Player;
            _g.pDeck.Add(MakeUnit("card1", 2));
            _g.pDeck.Add(MakeUnit("card2", 3));

            _tm.DoDraw();

            Assert.AreEqual(1, _g.pHand.Count);
            Assert.AreEqual(1, _g.pDeck.Count);
        }

        [Test]
        public void DoDraw_ResetsManaAndSch()
        {
            _g.turn  = Owner.Player;
            _g.pMana = 5;
            _g.eMana = 3;
            _g.pSch.Add(RuneType.Blazing, 2);
            _g.eSch.Add(RuneType.Verdant, 1);
            _g.pDeck.Add(MakeUnit("c", 1));

            _tm.DoDraw();

            Assert.AreEqual(0, _g.pMana);
            Assert.AreEqual(0, _g.eMana);
            Assert.AreEqual(0, _g.pSch.Total());
            Assert.AreEqual(0, _g.eSch.Total());
        }

        [Test]
        public void DoDraw_EmptyDeck_ReshufflesDiscard_AndBurnoutOpponent()
        {
            _g.turn = Owner.Player;
            // 牌堆空，废牌堆有3张
            _g.pDiscard.Add(MakeUnit("d1", 1));
            _g.pDiscard.Add(MakeUnit("d2", 1));
            _g.pDiscard.Add(MakeUnit("d3", 1));

            _tm.DoDraw();

            // 废牌堆已洗入主牌堆，抽1张到手牌
            Assert.AreEqual(1, _g.pHand.Count);
            Assert.AreEqual(2, _g.pDeck.Count);
            Assert.AreEqual(0, _g.pDiscard.Count);
            // 燃尽惩罚：对手（enemy）+1分
            Assert.AreEqual(1, _g.eScore);
        }

        [Test]
        public void DoDraw_EmptyDeckAndDiscard_NoDrawButStillBurnout()
        {
            _g.turn = Owner.Player;
            // 两者皆空

            _tm.DoDraw();

            Assert.AreEqual(0, _g.pHand.Count);
            Assert.AreEqual(1, _g.eScore); // 燃尽惩罚仍触发
        }

        // ─────────────────────────────────────────
        // DoEndPhase
        // ─────────────────────────────────────────

        [Test]
        public void DoEndPhase_ClearsStunned()
        {
            var u = MakeUnit("u", 3);
            u.stunned = true;
            _g.pBase.Add(u);
            _g.turn = Owner.Player;

            _tm.DoEndPhase();

            Assert.IsFalse(u.stunned);
        }

        [Test]
        public void DoEndPhase_ResetsHpToAtk()
        {
            var u = MakeUnit("u", 5);
            u.currentHp = 2; // 受伤后
            _g.pBase.Add(u);
            _g.turn = Owner.Player;

            _tm.DoEndPhase();

            Assert.AreEqual(u.currentAtk, u.currentHp);
        }

        [Test]
        public void DoEndPhase_ClearsTurnBuff()
        {
            var u = MakeUnit("u", 3);
            u.tb = new TurnBuffs { atk = 5 };
            _g.pBase.Add(u);
            _g.turn = Owner.Player;

            _tm.DoEndPhase();

            Assert.AreEqual(0, u.tb.atk);
        }

        [Test]
        public void DoEndPhase_IncreasesRound()
        {
            _g.round = 1;
            _g.turn  = Owner.Player;

            _tm.DoEndPhase();

            Assert.AreEqual(2, _g.round);
        }

        [Test]
        public void DoEndPhase_SwitchesTurn_PlayerToEnemy()
        {
            _g.turn = Owner.Player;
            _tm.DoEndPhase();
            Assert.AreEqual(Owner.Enemy, _g.turn);
        }

        [Test]
        public void DoEndPhase_SwitchesTurn_EnemyToPlayer()
        {
            _g.turn = Owner.Enemy;
            _tm.DoEndPhase();
            Assert.AreEqual(Owner.Player, _g.turn);
        }

        [Test]
        public void DoEndPhase_ExtraTurn_DoesNotIncreaseRound()
        {
            _g.turn = Owner.Player;
            _g.extraTurnPending = true;

            _tm.DoEndPhase();

            Assert.AreEqual(1, _g.round, "额外回合不推进 round");
            Assert.AreEqual(Owner.Player, _g.turn, "额外回合仍是玩家回合");
            Assert.IsFalse(_g.extraTurnPending);
        }

        [Test]
        public void DoEndPhase_SetsFirstTurnDone()
        {
            _g.turn  = Owner.Player;
            _g.pFirstTurnDone = false;
            _tm.DoEndPhase();
            Assert.IsTrue(_g.pFirstTurnDone);
        }

        // ─────────────────────────────────────────
        // AddScore / CheckWin
        // ─────────────────────────────────────────

        [Test]
        public void AddScore_IncreasesPlayerScore()
        {
            _tm.AddScore(Owner.Player, 2, "hold", 1);
            Assert.AreEqual(2, _g.pScore);
        }

        [Test]
        public void AddScore_IncreasesEnemyScore()
        {
            _tm.AddScore(Owner.Enemy, 3, "hold", null);
            Assert.AreEqual(3, _g.eScore);
        }

        [Test]
        public void AddScore_Conquer_AtWinMinus1_BlockedIfNotAllBfConquered()
        {
            _g.pScore = GameState.WIN_SCORE - 1; // 7分
            _g.pDeck.Add(MakeUnit("c", 1));

            // 仅征服了战场1，未征服战场2 → 应被阻止，改为抽牌
            _g.bfConqueredThisTurn.Add(1); // 只有1个
            bool scored = _tm.AddScore(Owner.Player, 1, "conquer", 1);

            Assert.IsFalse(scored);
            Assert.AreEqual(7, _g.pScore, "分数不应改变");
            Assert.AreEqual(1, _g.pHand.Count, "应改为抽1张牌");
        }

        [Test]
        public void AddScore_Conquer_AtWinMinus1_AllowedIfAllBfConquered()
        {
            _g.pScore = GameState.WIN_SCORE - 1;
            // 两个战场都已征服
            _g.bfConqueredThisTurn.Add(1);
            _g.bfConqueredThisTurn.Add(2);

            bool scored = _tm.AddScore(Owner.Player, 1, "conquer", 2);

            Assert.IsTrue(scored);
            Assert.AreEqual(8, _g.pScore);
            Assert.IsTrue(_g.gameOver);
        }

        [Test]
        public void CheckWin_SetsGameOver_WhenPlayerReaches8()
        {
            _g.pScore = 8;
            _tm.CheckWin();
            Assert.IsTrue(_g.gameOver);
        }

        [Test]
        public void CheckWin_SetsGameOver_WhenLegendDies()
        {
            var legData = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            legData.atk = 5; legData.hp = 14;
            _g.pLeg = LegendInstance.From(legData);
            _g.pLeg.currentHp = 0;

            _tm.CheckWin();

            Assert.IsTrue(_g.gameOver);
        }

        // ─────────────────────────────────────────
        // PlayerEndTurn
        // ─────────────────────────────────────────

        [Test]
        public void PlayerEndTurn_OnlyWorksInActionPhase()
        {
            _g.turn  = Owner.Player;
            _g.phase = GamePhase.Summon; // 非行动阶段
            _tm.PlayerEndTurn();
            Assert.AreEqual(1, _g.round, "非行动阶段不应推进 round");
        }

        [Test]
        public void PlayerEndTurn_IgnoredForEnemy()
        {
            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;
            _tm.PlayerEndTurn();
            Assert.AreEqual(1, _g.round);
        }

        // ─────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────

        private static CardInstance MakeUnit(string id, int atk)
        {
            var d = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            d.id = id; d.cardName = id; d.atk = atk;
            d.type = CardType.Follower;
            d.keywords = new System.Collections.Generic.List<string>();
            return CardInstance.From(d);
        }

        private static void AddRunes(System.Collections.Generic.List<RuneInstance> list, int count)
        {
            for (int i = 0; i < count; i++)
                list.Add(RuneInstance.Create(RuneType.Blazing));
        }
    }
}
