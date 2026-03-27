using NUnit.Framework;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P1 行为验证测试 — CardInstance & GameState
    /// 对照原版：engine.js mk()、atk()、G 对象字段初始值
    /// </summary>
    public class CardInstanceTests
    {
        private CardData MakeCardData(string id, int atk, int hp = 0,
            CardType type = CardType.Follower, bool isChampion = false)
        {
            var d = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            d.id             = id;
            d.cardName       = id;
            d.atk            = atk;
            d.hp             = hp;
            d.type           = type;
            d.strongAtkBonus = 1;
            d.canMoveToBase  = true;
            d.keywords       = new System.Collections.Generic.List<string>();
            return d;
        }

        [SetUp]
        public void SetUp() => CardInstance.ResetUidCounter();

        // ── UID 自增 ──
        [Test]
        public void Mk_AssignsIncrementingUids()
        {
            var d = MakeCardData("a", 3);
            var u1 = CardInstance.From(d);
            var u2 = CardInstance.From(d);
            Assert.AreEqual(1, u1.uid);
            Assert.AreEqual(2, u2.uid);
        }

        // ── currentHp = atk（普通随从），不是 hp 字段 ──
        [Test]
        public void Mk_CurrentHp_EqualsAtk_ForFollower()
        {
            var d = MakeCardData("unit", atk: 5, hp: 99);  // hp 字段故意设高，验证不用它
            var u = CardInstance.From(d);
            Assert.AreEqual(5, u.currentHp, "currentHp 应等于 atk，不是 hp");
            Assert.AreEqual(5, u.currentAtk);
        }

        // ── EffectiveAtk = max(1, currentAtk + tb.atk) ──
        [Test]
        public void EffectiveAtk_IncludesTurnBuff()
        {
            var u = CardInstance.From(MakeCardData("u", 3));
            u.tb.atk = 2;
            Assert.AreEqual(5, u.EffectiveAtk);
        }

        [Test]
        public void EffectiveAtk_MinimumOne()
        {
            var u = CardInstance.From(MakeCardData("u", 1));
            u.currentAtk = 0;
            u.tb.atk     = -5;
            Assert.AreEqual(1, u.EffectiveAtk, "有效战力最低为1");
        }

        [Test]
        public void EffectiveAtk_NegativeTurnBuff_Clamps()
        {
            var u = CardInstance.From(MakeCardData("u", 4));
            u.tb.atk = -3;
            Assert.AreEqual(1, u.EffectiveAtk);
        }

        // ── 初始状态 ──
        [Test]
        public void Mk_InitialState_Correct()
        {
            var u = CardInstance.From(MakeCardData("u", 3));
            Assert.IsFalse(u.exhausted);
            Assert.IsFalse(u.stunned);
            Assert.AreEqual(0, u.tb.atk);
            Assert.IsFalse(u.buffToken);
            Assert.IsNotNull(u.attachedEquipments);
            Assert.AreEqual(0, u.attachedEquipments.Count);
        }

        // ── IsPowerful: atk >= 5 ──
        [Test]
        public void IsPowerful_TrueWhenEffectiveAtkGe5()
        {
            var u = CardInstance.From(MakeCardData("u", 5));
            Assert.IsTrue(u.IsPowerful);

            var u2 = CardInstance.From(MakeCardData("u2", 4));
            Assert.IsFalse(u2.IsPowerful);
        }

        [Test]
        public void IsPowerful_ConsidersTurnBuff()
        {
            var u = CardInstance.From(MakeCardData("u", 4));
            u.tb.atk = 1;
            Assert.IsTrue(u.IsPowerful);
        }

        // ── strongAtkBonus 默认值 ──
        [Test]
        public void Mk_StrongAtkBonus_DefaultOne()
        {
            var d = MakeCardData("u", 3);
            d.strongAtkBonus = 0;  // 原版：unit.strongAtkBonus || 1
            var u = CardInstance.From(d);
            Assert.AreEqual(1, u.strongAtkBonus);
        }
    }

    public class GameStateTests
    {
        [Test]
        public void GameState_InitialValues()
        {
            var g = new GameState();
            Assert.AreEqual(0, g.pScore);
            Assert.AreEqual(0, g.eScore);
            Assert.AreEqual(1, g.round);
            Assert.AreEqual(Owner.Player, g.turn);
            Assert.AreEqual(GamePhase.Init, g.phase);
            Assert.IsFalse(g.gameOver);
            Assert.AreEqual(2, g.bf.Length);
            Assert.AreEqual(1, g.bf[0].id);
            Assert.AreEqual(2, g.bf[1].id);
        }

        [Test]
        public void GameState_Constants()
        {
            Assert.AreEqual(8,  GameState.WIN_SCORE);
            Assert.AreEqual(7,  GameState.MAX_HAND);
            Assert.AreEqual(2,  GameState.MAX_BF_UNITS);
            Assert.AreEqual(30, GameState.TIMER_SECONDS);
        }

        [Test]
        public void SchematicCounts_AddAndSpend()
        {
            var sch = new SchematicCounts();
            sch.Add(RuneType.Blazing, 2);
            Assert.AreEqual(2, sch.Get(RuneType.Blazing));
            sch.Spend(RuneType.Blazing, 1);
            Assert.AreEqual(1, sch.Get(RuneType.Blazing));
        }

        [Test]
        public void SchematicCounts_Spend_NeverNegative()
        {
            var sch = new SchematicCounts();
            sch.Spend(RuneType.Blazing, 99);
            Assert.AreEqual(0, sch.Get(RuneType.Blazing));
        }

        [Test]
        public void SchematicCounts_Reset_ClearsAll()
        {
            var sch = new SchematicCounts();
            sch.Add(RuneType.Blazing, 3);
            sch.Add(RuneType.Verdant, 2);
            sch.Reset();
            Assert.AreEqual(0, sch.Total());
        }

        [Test]
        public void LegendInstance_From_UsesHpField_ForCurrentHp()
        {
            var d = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            d.atk = 5;
            d.hp  = 14;
            var leg = LegendInstance.From(d);
            Assert.AreEqual(14, leg.currentHp, "传奇 currentHp 初始化应来自 data.hp");
            Assert.AreEqual(5,  leg.currentAtk);
        }

        [Test]
        public void GameState_Opponent()
        {
            var g = new GameState();
            Assert.AreEqual(Owner.Enemy,  g.Opponent(Owner.Player));
            Assert.AreEqual(Owner.Player, g.Opponent(Owner.Enemy));
        }
    }
}
