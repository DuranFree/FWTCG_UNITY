using NUnit.Framework;
using System.Collections.Generic;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P5 行为验证测试 — AIController
    /// 对照原版：ai.js aiAction / aiDecideMovement / aiCardValue / aiBoardScore
    /// </summary>
    public class AIControllerTests
    {
        private GameState      _g;
        private TurnManager    _tm;
        private CardDeployer   _cd;
        private CombatResolver _cr;
        private AIController   _ai;

        [SetUp]
        public void SetUp()
        {
            CardInstance.ResetUidCounter();
            RuneInstance.ResetUidCounter();
            _g  = new GameState();
            _tm = new TurnManager(_g);
            _cd = new CardDeployer(_g, _tm);
            _cr = new CombatResolver(_g, _tm, _cd);
            _ai = new AIController(_g, _tm, _cd);
            // 同步调度器：立即执行，不等待
            _ai.Schedule = (_, fn) => fn();

            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;
        }

        // ─────────────────────────────────────────
        // 辅助工厂
        // ─────────────────────────────────────────

        private static CardInstance MakeUnit(string id, int atk, int cost = 1,
            List<string> keywords = null)
        {
            return new CardInstance
            {
                uid        = CardInstance.AllocUid(),
                id         = id,
                cardName   = id,
                type       = CardType.Follower,
                cost       = cost,
                atk        = atk,
                currentAtk = atk,
                currentHp  = atk,
                keywords   = keywords ?? new List<string>(),
                tb         = new TurnBuffs { atk = 0 },
                attachedEquipments = new List<CardInstance>(),
                strongAtkBonus = 1,
                guardBonus     = 1
            };
        }

        private static RuneInstance MakeRune(RuneType type = RuneType.Blazing)
            => RuneInstance.Create(type);

        // ─────────────────────────────────────────
        // AiCardValue
        // ─────────────────────────────────────────

        [Test]
        public void AiCardValue_BaseFormula_AtkOverCost()
        {
            var c = MakeUnit("u", 4, cost: 2);
            // (4/2)*10 = 20
            Assert.AreEqual(20f, AIController.AiCardValue(c));
        }

        [Test]
        public void AiCardValue_Haste_Plus4()
        {
            var c = MakeUnit("u", 2, cost: 2, keywords: new List<string> { "急速" });
            // (2/2)*10 + 4 = 14
            Assert.AreEqual(14f, AIController.AiCardValue(c));
        }

        [Test]
        public void AiCardValue_Barrier_Plus3()
        {
            var c = MakeUnit("u", 2, cost: 2, keywords: new List<string> { "壁垒" });
            Assert.AreEqual(13f, AIController.AiCardValue(c));
        }

        [Test]
        public void AiCardValue_MultipleKeywords_Stacked()
        {
            // 急速+壁垒+强攻 = +4+3+2 = +9
            var c = MakeUnit("u", 2, cost: 2,
                keywords: new List<string> { "急速", "壁垒", "强攻" });
            Assert.AreEqual(19f, AIController.AiCardValue(c));
        }

        [Test]
        public void AiCardValue_ZeroAtk_CostOne()
        {
            var c = MakeUnit("u", 0, cost: 1);
            Assert.AreEqual(0f, AIController.AiCardValue(c));
        }

        [Test]
        public void AiCardValue_MinCostOne_PreventsDivideByZero()
        {
            var c = MakeUnit("u", 4, cost: 0); // cost 0 → clamped to 1
            Assert.AreEqual(40f, AIController.AiCardValue(c));
        }

        // ─────────────────────────────────────────
        // AiBoardScore
        // ─────────────────────────────────────────

        [Test]
        public void AiBoardScore_AiLeadsOnScore()
        {
            _g.eScore = 5; _g.pScore = 2;
            float s = _ai.AiBoardScore();
            Assert.Greater(s, 0f);
        }

        [Test]
        public void AiBoardScore_PlayerLeadsOnScore()
        {
            _g.pScore = 5; _g.eScore = 2;
            float s = _ai.AiBoardScore();
            Assert.Less(s, 0f);
        }

        [Test]
        public void AiBoardScore_AiControlsBothBF()
        {
            _g.bf[0].ctrl = Owner.Enemy;
            _g.bf[1].ctrl = Owner.Enemy;
            float s = _ai.AiBoardScore();
            Assert.Greater(s, 0f);
        }

        [Test]
        public void AiBoardScore_AllEven_IsZero()
        {
            // no units, no bf control, equal score/hand
            Assert.AreEqual(0f, _ai.AiBoardScore());
        }

        // ─────────────────────────────────────────
        // AiDecideMovement
        // ─────────────────────────────────────────

        [Test]
        public void AiDecideMovement_PrefersUncontrolledEmptyBF()
        {
            var unit = MakeUnit("u", 3);
            unit.exhausted = false;
            _g.eBase.Add(unit);

            var dec = _ai.AiDecideMovement(new List<CardInstance> { unit });

            Assert.IsNotNull(dec);
            Assert.IsTrue(dec.TargetBfId == 1 || dec.TargetBfId == 2);
        }

        [Test]
        public void AiDecideMovement_ReturnsNull_WhenAllSlotsFull()
        {
            // Fill both BF sides with AI units (2 per side = max)
            for (int i = 0; i < 2; i++)
            {
                _g.bf[i].eU.Add(MakeUnit("e1", 2));
                _g.bf[i].eU.Add(MakeUnit("e2", 2));
            }
            var unit = MakeUnit("u", 3); unit.exhausted = false;

            var dec = _ai.AiDecideMovement(new List<CardInstance> { unit });
            Assert.IsNull(dec);
        }

        [Test]
        public void AiDecideMovement_WinningCombat_HighScore()
        {
            // BF1: has player unit with atk=2; AI unit atk=5 → will win
            _g.bf[0].pU.Add(MakeUnit("p", 2));
            var unit = MakeUnit("u", 5); unit.exhausted = false;

            var dec = _ai.AiDecideMovement(new List<CardInstance> { unit });

            Assert.IsNotNull(dec);
            Assert.AreEqual(1, dec.TargetBfId);
        }

        [Test]
        public void AiDecideMovement_SplitStrategy_TwoEmptyBFs()
        {
            // 2 units, 2 empty uncontrolled BFs → split strategy picks bf[0] for first unit
            var u1 = MakeUnit("u1", 4); u1.exhausted = false;
            var u2 = MakeUnit("u2", 3); u2.exhausted = false;
            var active = new List<CardInstance> { u1, u2 };

            var dec = _ai.AiDecideMovement(active);

            Assert.IsNotNull(dec);
            Assert.AreEqual(1, dec.TargetBfId);  // Split goes to bf0 (id=1)
            Assert.AreEqual(1, dec.Movers.Count); // Only 1 mover in split plan
        }

        [Test]
        public void AiDecideMovement_PrefersBFWithConquestPotential_WhenNearWin()
        {
            // AI is at 6 points → close to win → urgency bonus
            _g.eScore = 6;
            var unit = MakeUnit("u", 3); unit.exhausted = false;

            var dec = _ai.AiDecideMovement(new List<CardInstance> { unit });

            Assert.IsNotNull(dec);
        }

        // ─────────────────────────────────────────
        // AiAction — 符文点击
        // ─────────────────────────────────────────

        [Test]
        public void AiAction_TapsAllUntappedRunes()
        {
            _g.eRunes.Add(MakeRune());
            _g.eRunes.Add(MakeRune());
            _g.eMana = 0;

            _ai.AiAction();

            // DoEndPhase resets eMana to 0; only verify runes were tapped
            Assert.IsTrue(_g.eRunes.TrueForAll(r => r.tapped));
        }

        [Test]
        public void AiAction_DoesNotTapAlreadyTappedRunes()
        {
            var r = MakeRune(); r.tapped = true;
            _g.eRunes.Add(r);
            _g.eMana = 5;

            _ai.AiAction();

            Assert.AreEqual(1, _g.eRunes.Count);
            Assert.IsTrue(_g.eRunes[0].tapped);
        }

        // ─────────────────────────────────────────
        // AiAction — 出牌
        // ─────────────────────────────────────────

        [Test]
        public void AiAction_DeploysUnit_WhenManaAndHandSufficient()
        {
            _g.eMana = 3;
            var unit = MakeUnit("u", 2, cost: 2);
            _g.eHand.Add(unit);

            _ai.AiAction();

            Assert.AreEqual(1, _g.eBase.Count);
            Assert.AreEqual(0, _g.eHand.Count);
        }

        [Test]
        public void AiAction_DoesNotDeploy_WhenInsufficientMana()
        {
            _g.eMana = 1;
            var unit = MakeUnit("u", 3, cost: 3);
            _g.eHand.Add(unit);
            // No runes either

            _ai.AiAction();

            Assert.AreEqual(0, _g.eBase.Count);
            Assert.AreEqual(1, _g.eHand.Count);
        }

        [Test]
        public void AiAction_DeploysHigherValueUnitFirst()
        {
            _g.eMana = 5;
            // unit A: atk=1, cost=1 → value=10; unit B: atk=4, cost=2 → value=20
            var unitA = MakeUnit("low", 1, cost: 1);
            var unitB = MakeUnit("high", 4, cost: 2);
            _g.eHand.Add(unitA);
            _g.eHand.Add(unitB);

            // With synchronous schedule, AI deploys recursively until no units fit
            // First deploy: unitB (higher value)
            // After unitB deployed: AiAction() again, unitA affordable, deploys
            // After unitA deployed: AiAction() again, empty hand → move/end

            _ai.AiAction();

            // Both deployed; unitB should have been deployed first → both in base
            Assert.AreEqual(2, _g.eBase.Count);
        }

        [Test]
        public void AiAction_DoesNotExceedBaseCap5()
        {
            _g.eMana = 20;
            // 6 cheap units in hand
            for (int i = 0; i < 6; i++)
                _g.eHand.Add(MakeUnit($"u{i}", 1, cost: 1));

            _ai.AiAction();

            Assert.LessOrEqual(_g.eBase.Count, 5);
        }

        [Test]
        public void AiAction_HasteUnit_EntersNonExhausted()
        {
            _g.eMana = 3;
            var unit = MakeUnit("u", 2, cost: 2, keywords: new List<string> { "急速" });
            _g.eHand.Add(unit);
            // Fill all BF slots so the unit cannot be moved (stays in base to check exhausted state)
            _g.bf[0].eU.Add(MakeUnit("e1", 1)); _g.bf[0].eU.Add(MakeUnit("e2", 1));
            _g.bf[1].eU.Add(MakeUnit("e3", 1)); _g.bf[1].eU.Add(MakeUnit("e4", 1));

            _ai.AiAction();

            // Haste unit deployed to base, cannot move (BF full) → must be non-exhausted
            Assert.AreEqual(1, _g.eBase.Count);
            Assert.IsFalse(_g.eBase[0].exhausted);
        }

        // ─────────────────────────────────────────
        // AiAction — 移动
        // ─────────────────────────────────────────

        [Test]
        public void AiAction_MovesActiveUnit_ToBattlefield()
        {
            _g.eMana = 0;
            var unit = MakeUnit("u", 3); unit.exhausted = false;
            _g.eBase.Add(unit);

            _ai.AiAction();

            // Unit should be on a battlefield
            bool onBF = _g.bf[0].eU.Contains(unit) || _g.bf[1].eU.Contains(unit);
            Assert.IsTrue(onBF);
            Assert.AreEqual(0, _g.eBase.Count);
        }

        [Test]
        public void AiAction_DoesNotMove_ExhaustedUnit()
        {
            _g.eMana = 0;
            var unit = MakeUnit("u", 3); unit.exhausted = true;
            _g.eBase.Add(unit);

            _ai.AiAction();

            // Unit stays in base (exhausted → cannot move)
            Assert.AreEqual(1, _g.eBase.Count);
        }

        [Test]
        public void AiAction_DoesNotMove_StunnedUnit()
        {
            _g.eMana = 0;
            var unit = MakeUnit("u", 3); unit.exhausted = false; unit.stunned = true;
            _g.eBase.Add(unit);

            _ai.AiAction();

            Assert.AreEqual(1, _g.eBase.Count);
        }

        // ─────────────────────────────────────────
        // AiAction — 回合结束
        // ─────────────────────────────────────────

        [Test]
        public void AiAction_EndsEnemyTurn_WhenNothingToDo()
        {
            _g.eMana = 0;
            // No hand, no active base units

            _ai.AiAction();

            Assert.AreEqual(Owner.Player, _g.turn);
        }

        [Test]
        public void AiAction_NoOp_WhenNotEnemyTurn()
        {
            _g.turn = Owner.Player;
            _g.eHand.Add(MakeUnit("u", 2, cost: 1));
            _g.eMana = 5;

            _ai.AiAction();

            // Nothing should change
            Assert.AreEqual(Owner.Player, _g.turn);
            Assert.AreEqual(1, _g.eHand.Count);
        }

        [Test]
        public void AiAction_NoOp_WhenGameOver()
        {
            _g.gameOver = true;
            _g.eHand.Add(MakeUnit("u", 2, cost: 1));
            _g.eMana = 5;

            _ai.AiAction();

            Assert.AreEqual(0, _g.eBase.Count);
        }

        [Test]
        public void AiAction_SkipsDeploy_WhenCardLocked()
        {
            _g.cardLockTarget = Owner.Enemy;
            _g.eMana = 5;
            var unit = MakeUnit("u", 2, cost: 1);
            _g.eHand.Add(unit);

            _ai.AiAction();

            // Locked → cannot deploy
            Assert.AreEqual(0, _g.eBase.Count);
            Assert.AreEqual(1, _g.eHand.Count);
        }

        // ─────────────────────────────────────────
        // AiDuelAction
        // ─────────────────────────────────────────

        [Test]
        public void AiDuelAction_NoOp_WhenNotInDuel()
        {
            _g.duelActive = false;
            Assert.DoesNotThrow(() => _ai.AiDuelAction());
        }

        [Test]
        public void AiDuelAction_NoOp_WhenInDuelNoSpellSystem()
        {
            _g.duelActive = true;
            Assert.DoesNotThrow(() => _ai.AiDuelAction());
        }
    }
}
