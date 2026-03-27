using NUnit.Framework;
using System.Collections.Generic;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P6 行为验证测试 — SpellSystem
    /// 对照原版：spell.js canPlay / getSpellTargets / applySpell（33效果）/
    ///           startSpellDuel / runDuelTurn / skipDuel / endDuel /
    ///           hasPlayableReactionCards；ai.js aiDuelAction
    /// </summary>
    public class SpellSystemTests
    {
        private GameState      _g;
        private TurnManager    _tm;
        private CardDeployer   _cd;
        private CombatResolver _cr;
        private SpellSystem    _ss;
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
            _ss = new SpellSystem(_g, _tm, _cd, _cr);
            _ai = new AIController(_g, _tm, _cd);
            _ai.SetSpellSystem(_ss);
            _ai.Schedule = (_, fn) => fn();   // 同步

            _g.turn  = Owner.Player;
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

        private static CardInstance MakeSpell(string id, string effect, int cost = 1,
            List<string> keywords = null, int schCost = 0, RuneType schType = RuneType.Blazing,
            int echoSchCost = 0, RuneType echoSchType = RuneType.Blazing, int echoManaCost = 0)
        {
            return new CardInstance
            {
                uid      = CardInstance.AllocUid(),
                id       = id,
                cardName = id,
                type     = CardType.Spell,
                cost     = cost,
                effect   = effect,
                keywords = keywords ?? new List<string>(),
                tb       = new TurnBuffs(),
                attachedEquipments = new List<CardInstance>(),
                schCost     = schCost,
                schType     = schType,
                echoSchCost = echoSchCost,
                echoSchType = echoSchType,
                echoManaCost= echoManaCost
            };
        }

        private static RuneInstance MakeRune(RuneType t = RuneType.Blazing)
            => RuneInstance.Create(t);

        // ─────────────────────────────────────────
        // GetEffectiveCost
        // ─────────────────────────────────────────

        [Test]
        public void GetEffectiveCost_NormalSpell_ReturnsBaseCost()
        {
            var sp = MakeSpell("s", "draw1", cost: 3);
            Assert.AreEqual(3, _ss.GetEffectiveCost(sp, Owner.Player));
        }

        [Test]
        public void GetEffectiveCost_BalanceResolve_DiscountWhenOpponentClose()
        {
            _g.eScore = GameState.WIN_SCORE - 2;  // 对手（AI）6分 → pScore≥WIN_SCORE-3 时折扣，这里opponent=enemy
            var sp = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "balance_resolve", type = CardType.Spell,
                cost = 4, effect = "balance_resolve", keywords = new List<string>(),
                tb = new TurnBuffs(), attachedEquipments = new List<CardInstance>()
            };
            // player调用，opponent=enemy=eScore=6 ≥ WIN_SCORE-3=5 → 折扣
            Assert.AreEqual(2, _ss.GetEffectiveCost(sp, Owner.Player));
        }

        [Test]
        public void GetEffectiveCost_BalanceResolve_NoDiscountWhenOpponentFar()
        {
            _g.eScore = 3;  // < WIN_SCORE-3=5 → 不折扣
            var sp = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "balance_resolve", type = CardType.Spell,
                cost = 4, effect = "balance_resolve", keywords = new List<string>(),
                tb = new TurnBuffs(), attachedEquipments = new List<CardInstance>()
            };
            Assert.AreEqual(4, _ss.GetEffectiveCost(sp, Owner.Player));
        }

        // ─────────────────────────────────────────
        // CanPlay
        // ─────────────────────────────────────────

        [Test]
        public void CanPlay_ActionPhase_PlayerTurn_Affordable()
        {
            _g.pMana = 3;
            var sp = MakeSpell("s", "draw1", cost: 2);
            Assert.IsTrue(_ss.CanPlay(sp, Owner.Player));
        }

        [Test]
        public void CanPlay_EnemyTurn_ReturnsFalse()
        {
            _g.turn = Owner.Enemy;
            _g.pMana = 5;
            var sp = MakeSpell("s", "draw1", cost: 1);
            Assert.IsFalse(_ss.CanPlay(sp, Owner.Player));
        }

        [Test]
        public void CanPlay_InsufficientMana_ReturnsFalse()
        {
            _g.pMana = 0;
            var sp = MakeSpell("s", "draw1", cost: 2);
            Assert.IsFalse(_ss.CanPlay(sp, Owner.Player));
        }

        [Test]
        public void CanPlay_Locked_ReturnsFalse()
        {
            _g.cardLockTarget = Owner.Player;
            _g.pMana = 5;
            var sp = MakeSpell("s", "draw1", cost: 1);
            Assert.IsFalse(_ss.CanPlay(sp, Owner.Player));
        }

        [Test]
        public void CanPlay_DuelActive_NoKeyword_ReturnsFalse()
        {
            _g.duelActive = true;
            _g.duelTurn   = Owner.Player;
            _g.pMana = 5;
            var sp = MakeSpell("s", "draw1", cost: 1);  // no 迅捷/反应
            Assert.IsFalse(_ss.CanPlay(sp, Owner.Player));
        }

        [Test]
        public void CanPlay_DuelActive_WithQuick_ReturnsTrue()
        {
            _g.duelActive = true;
            _g.duelTurn   = Owner.Player;
            _g.pMana = 5;
            var sp = MakeSpell("s", "stun_manual", cost: 2, keywords: new List<string>{"迅捷"});
            var enemy = MakeUnit("e", 3);
            _g.bf[0].eU.Add(enemy);
            Assert.IsTrue(_ss.CanPlay(sp, Owner.Player));
        }

        [Test]
        public void CanPlay_NoValidTargets_ReturnsFalse()
        {
            _g.pMana = 5;
            // deal3 requires enemy BF unit; BF is empty
            var sp = MakeSpell("s", "deal3", cost: 1);
            Assert.IsFalse(_ss.CanPlay(sp, Owner.Player));
        }

        // ─────────────────────────────────────────
        // GetSpellTargets
        // ─────────────────────────────────────────

        [Test]
        public void GetSpellTargets_BuffAlly_ReturnsMyUnitsExcludingBuffToken()
        {
            var u1 = MakeUnit("u1", 2); u1.buffToken = false;
            var u2 = MakeUnit("u2", 3); u2.buffToken = true;
            _g.pBase.Add(u1); _g.pBase.Add(u2);
            var sp = MakeSpell("s", "buff_ally");
            var targets = _ss.GetSpellTargets(sp, Owner.Player);
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual(u1.uid, targets[0]);
        }

        [Test]
        public void GetSpellTargets_Deal3_ReturnsOnlyEnemyBFUnits()
        {
            var bf = MakeUnit("e1", 3);
            var baseUnit = MakeUnit("e2", 2);
            _g.bf[0].eU.Add(bf);
            _g.eBase.Add(baseUnit);
            var sp = MakeSpell("s", "deal3");
            var targets = _ss.GetSpellTargets(sp, Owner.Player);
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual(bf.uid, targets[0]);
        }

        [Test]
        public void GetSpellTargets_RallyCall_ReturnsNull()
        {
            var sp = MakeSpell("s", "rally_call");
            Assert.IsNull(_ss.GetSpellTargets(sp, Owner.Player));
        }

        [Test]
        public void GetSpellTargets_StunManual_ReturnsOnlyEnemyBFUnits()
        {
            var bf1 = MakeUnit("e1", 3);
            var base1 = MakeUnit("e2", 2);
            _g.bf[0].eU.Add(bf1);
            _g.eBase.Add(base1);
            var sp = MakeSpell("s", "stun_manual");
            var targets = _ss.GetSpellTargets(sp, Owner.Player);
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual(bf1.uid, targets[0]);
        }

        [Test]
        public void GetSpellTargets_Deal4Draw_ReturnsAllBFUnits()
        {
            var p1 = MakeUnit("p1", 2); var e1 = MakeUnit("e1", 3);
            _g.bf[0].pU.Add(p1); _g.bf[1].eU.Add(e1);
            var sp = MakeSpell("s", "deal4_draw");
            var targets = _ss.GetSpellTargets(sp, Owner.Player);
            Assert.AreEqual(2, targets.Count);
        }

        // ─────────────────────────────────────────
        // HasPlayableReactionCards
        // ─────────────────────────────────────────

        [Test]
        public void HasPlayableReactionCards_HasAffordableReaction_ReturnsTrue()
        {
            _g.turn  = Owner.Enemy;  // enemy's turn → player can react
            _g.pMana = 3;
            var sp = MakeSpell("s", "stun_manual", cost: 2, keywords: new List<string>{"反应"});
            _g.pHand.Add(sp);
            Assert.IsTrue(_ss.HasPlayableReactionCards(Owner.Player));
        }

        [Test]
        public void HasPlayableReactionCards_InsufficientMana_ReturnsFalse()
        {
            _g.turn  = Owner.Enemy;
            _g.pMana = 0;
            var sp = MakeSpell("s", "stun_manual", cost: 2, keywords: new List<string>{"反应"});
            _g.pHand.Add(sp);
            Assert.IsFalse(_ss.HasPlayableReactionCards(Owner.Player));
        }

        [Test]
        public void HasPlayableReactionCards_PlayerTurn_ReturnsFalse()
        {
            _g.turn  = Owner.Player;
            _g.pMana = 5;
            var sp = MakeSpell("s", "stun_manual", cost: 1, keywords: new List<string>{"反应"});
            _g.pHand.Add(sp);
            Assert.IsFalse(_ss.HasPlayableReactionCards(Owner.Player));
        }

        // ─────────────────────────────────────────
        // ApplySpell — 抽牌 / 符文
        // ─────────────────────────────────────────

        [Test]
        public void ApplySpell_Draw1_IncreasesHandByOne()
        {
            _g.pDeck.Add(MakeUnit("d", 2));
            var sp = MakeSpell("s", "draw1");
            int before = _g.pHand.Count;
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.AreEqual(before + 1, _g.pHand.Count);
        }

        [Test]
        public void ApplySpell_Draw4_DrawsUpToFour()
        {
            for (int i = 0; i < 3; i++) _g.pDeck.Add(MakeUnit($"d{i}", 1));
            var sp = MakeSpell("s", "draw4");
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.AreEqual(3, _g.pHand.Count);  // deck only had 3
        }

        [Test]
        public void ApplySpell_SummonRune1_AddsRuneToMyRunes()
        {
            _g.pRuneDeck.Add(MakeRune(RuneType.Blazing));
            var sp = MakeSpell("s", "summon_rune1");
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.AreEqual(1, _g.pRunes.Count);
            Assert.IsTrue(_g.pRunes[0].tapped);
        }

        [Test]
        public void ApplySpell_RuneDraw_AddsBothRuneAndCard()
        {
            _g.pRuneDeck.Add(MakeRune());
            _g.pDeck.Add(MakeUnit("d", 2));
            var sp = MakeSpell("s", "rune_draw");
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.AreEqual(1, _g.pRunes.Count);
            Assert.AreEqual(1, _g.pHand.Count);
        }

        // ─────────────────────────────────────────
        // ApplySpell — 增益/减弱
        // ─────────────────────────────────────────

        [Test]
        public void ApplySpell_BuffAlly_PermanentPlusOne()
        {
            var unit = MakeUnit("u", 3);
            _g.pBase.Add(unit);
            var sp = MakeSpell("s", "buff_ally");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(4, unit.currentAtk);
            Assert.AreEqual(4, unit.atk);
            Assert.AreEqual(4, unit.currentHp);
        }

        [Test]
        public void ApplySpell_Stun_SetStunnedTrue()
        {
            var unit = MakeUnit("e", 3);
            _g.eBase.Add(unit);
            var sp = MakeSpell("s", "stun");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.IsTrue(unit.stunned);
        }

        [Test]
        public void ApplySpell_Weaken_ReducesTbAtk()
        {
            var unit = MakeUnit("e", 4);
            _g.eBase.Add(unit);
            var sp = MakeSpell("s", "weaken");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(-2, unit.tb.atk);
        }

        [Test]
        public void ApplySpell_Debuff4_ReducesTbAtk()
        {
            var unit = MakeUnit("e", 5);
            _g.eBase.Add(unit);
            var sp = MakeSpell("s", "debuff4");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(-4, unit.tb.atk);
        }

        [Test]
        public void ApplySpell_Buff7Manual_IncreaseTbAtk()
        {
            var unit = MakeUnit("u", 2);
            _g.pBase.Add(unit);
            var sp = MakeSpell("s", "buff7_manual");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(7, unit.tb.atk);
        }

        [Test]
        public void ApplySpell_Buff5Manual_IncreaseTbAtk()
        {
            var unit = MakeUnit("u", 2);
            _g.pBase.Add(unit);
            var sp = MakeSpell("s", "buff5_manual");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(5, unit.tb.atk);
        }

        [Test]
        public void ApplySpell_Buff2Draw_IncreasesTbAndDraws()
        {
            var unit = MakeUnit("u", 2);
            _g.pBase.Add(unit);
            _g.pDeck.Add(MakeUnit("d", 1));
            var sp = MakeSpell("s", "buff2_draw");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(2, unit.tb.atk);
            Assert.AreEqual(1, _g.pHand.Count);
        }

        // ─────────────────────────────────────────
        // ApplySpell — 伤害
        // ─────────────────────────────────────────

        [Test]
        public void ApplySpell_Deal3_DealsDamageToBFUnit()
        {
            var enemy = MakeUnit("e", 5);
            _g.bf[0].eU.Add(enemy);
            var sp = MakeSpell("s", "deal3");
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            // 5-3=2 hp; unit still alive (5>3)
            Assert.AreEqual(2, enemy.currentHp);
        }

        [Test]
        public void ApplySpell_Deal3_KillsWeakUnit()
        {
            var enemy = MakeUnit("e", 3);
            _g.bf[0].eU.Add(enemy);
            var sp = MakeSpell("s", "deal3");
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            // hp=0 → cleaned up
            Assert.AreEqual(0, _g.bf[0].eU.Count);
        }

        [Test]
        public void ApplySpell_Deal4Draw_DamageAndDraw()
        {
            var enemy = MakeUnit("e", 6);
            _g.bf[0].eU.Add(enemy);
            _g.pDeck.Add(MakeUnit("d", 1));
            var sp = MakeSpell("s", "deal4_draw");
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            Assert.AreEqual(2, enemy.currentHp);
            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void ApplySpell_ThunderGalManual_DealsDmgEqualToTargetAtk()
        {
            var enemy = MakeUnit("e", 4);
            _g.eBase.Add(enemy);
            var sp = MakeSpell("s", "thunder_gal_manual");
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            // atk=4 damage → unit dies (hp was 4)
            Assert.AreEqual(0, _g.eBase.Count);
        }

        // ─────────────────────────────────────────
        // ApplySpell — 召回 / 移动
        // ─────────────────────────────────────────

        [Test]
        public void ApplySpell_RecallDraw_MovesUnitToHandAndDraws()
        {
            var unit = MakeUnit("u", 3);
            _g.pBase.Add(unit);
            _g.pDeck.Add(MakeUnit("d", 1));
            var sp = MakeSpell("s", "recall_draw");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(0, _g.pBase.Count);
            Assert.IsTrue(_g.pHand.Contains(unit));
            Assert.AreEqual(2, _g.pHand.Count);  // unit + drawn card
        }

        [Test]
        public void ApplySpell_RecallUnitRune_MovesUnitAndSummonsRune()
        {
            var unit = MakeUnit("u", 2);
            _g.pBase.Add(unit);
            _g.pRuneDeck.Add(MakeRune());
            var sp = MakeSpell("s", "recall_unit_rune");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(0, _g.pBase.Count);
            Assert.IsTrue(_g.pHand.Contains(unit));
            Assert.AreEqual(1, _g.pRunes.Count);
        }

        [Test]
        public void ApplySpell_ReadyUnit_UnsetsExhausted()
        {
            var unit = MakeUnit("u", 3);
            unit.exhausted = true;
            _g.pBase.Add(unit);
            var sp = MakeSpell("s", "ready_unit");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.IsFalse(unit.exhausted);
        }

        [Test]
        public void ApplySpell_ForceMove_MovesEnemyUnitToBF()
        {
            var enemy = MakeUnit("e", 3);
            _g.eBase.Add(enemy);
            var sp = MakeSpell("s", "force_move");
            // PromptBattlefield defaults to first option (idx 0)
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            Assert.AreEqual(0, _g.eBase.Count);
            bool onBF = _g.bf[0].eU.Contains(enemy) || _g.bf[1].eU.Contains(enemy);
            Assert.IsTrue(onBF);
        }

        // ─────────────────────────────────────────
        // ApplySpell — 特殊效果
        // ─────────────────────────────────────────

        [Test]
        public void ApplySpell_Buff1Solo_PlusTwoWhenAloneVsEnemy()
        {
            var unit  = MakeUnit("u", 3);
            var enemy = MakeUnit("e", 2);
            _g.bf[0].pU.Add(unit);
            _g.bf[0].eU.Add(enemy);
            var sp = MakeSpell("s", "buff1_solo");
            _ss.ApplySpell(sp, Owner.Player, unit.uid);
            Assert.AreEqual(2, unit.tb.atk);  // sole defender vs enemy → +2
        }

        [Test]
        public void ApplySpell_Buff1Solo_PlusOneWhenNotAlone()
        {
            var u1 = MakeUnit("u1", 3);
            var u2 = MakeUnit("u2", 2);
            var enemy = MakeUnit("e", 2);
            _g.bf[0].pU.Add(u1); _g.bf[0].pU.Add(u2);
            _g.bf[0].eU.Add(enemy);
            var sp = MakeSpell("s", "buff1_solo");
            _ss.ApplySpell(sp, Owner.Player, u1.uid);
            Assert.AreEqual(1, u1.tb.atk);
        }

        [Test]
        public void ApplySpell_Deal3Twice_TwoTargetsEachTake3()
        {
            var e1 = MakeUnit("e1", 8);  // 8-3-3=2 after two hits
            var e2 = MakeUnit("e2", 5);
            _g.eBase.Add(e1); _g.eBase.Add(e2);
            // PromptTarget always returns first
            _ss.PromptTarget = list => list[0];
            var sp = MakeSpell("s", "deal3_twice");
            _ss.ApplySpell(sp, Owner.Player, null);
            // e1 hit twice (first in list both times), e2 unhurt
            Assert.AreEqual(2, e1.currentHp);
            Assert.AreEqual(5, e2.currentHp);
        }

        [Test]
        public void ApplySpell_Deal1Repeat_Fires5Times()
        {
            var enemy = MakeUnit("e", 10);  // high hp so it survives
            _g.eBase.Add(enemy);
            _ss.PromptTarget = list => list[0];
            var sp = MakeSpell("s", "deal1_repeat");
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.AreEqual(5, enemy.currentHp);
        }

        [Test]
        public void ApplySpell_AkasiStorm_Fires6TimesTotal()
        {
            // Big unit, just check it took 12 total damage (6×2)
            var enemy = MakeUnit("e", 20);
            _g.eBase.Add(enemy);
            var sp = MakeSpell("s", "akasi_storm");
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.AreEqual(8, enemy.currentHp);  // 20 - 12 = 8
        }

        [Test]
        public void ApplySpell_DiscardDeal_DiscardsThenDealsCostDamage()
        {
            var hand1 = MakeUnit("h", 2, cost: 3);  // cost=3
            var target = MakeUnit("e", 5);
            _g.pHand.Add(hand1);
            _g.bf[0].eU.Add(target);
            _ss.PromptDiscard = list => list[0];
            var sp = MakeSpell("s", "discard_deal");
            _ss.ApplySpell(sp, Owner.Player, target.uid);
            Assert.AreEqual(0, _g.pHand.Count);  // discarded
            Assert.AreEqual(1, _g.pDiscard.Count);
            Assert.AreEqual(2, target.currentHp);  // 5-3=2
        }

        [Test]
        public void ApplySpell_RallyCall_SetsPRallyActiveAndDraws()
        {
            _g.pDeck.Add(MakeUnit("d", 1));
            var sp = MakeSpell("s", "rally_call");
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.IsTrue(_g.pRallyActive);
            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void ApplySpell_BalanceResolve_DrawsAndSummonsRune()
        {
            _g.pDeck.Add(MakeUnit("d", 1));
            _g.pRuneDeck.Add(MakeRune());
            var sp = MakeSpell("s", "balance_resolve");
            _ss.ApplySpell(sp, Owner.Player, null);
            Assert.AreEqual(1, _g.pHand.Count);
            Assert.AreEqual(1, _g.pRunes.Count);
        }

        [Test]
        public void ApplySpell_Debuff1Draw_ReducesTbAndDraws()
        {
            var enemy = MakeUnit("e", 4);
            _g.eBase.Add(enemy);
            _g.pDeck.Add(MakeUnit("d", 1));
            var sp = MakeSpell("s", "debuff1_draw");
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            Assert.AreEqual(-1, enemy.tb.atk);
            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void ApplySpell_Deal1SameZone_HitsZoneMates()
        {
            var e1 = MakeUnit("e1", 4);
            var e2 = MakeUnit("e2", 4);
            _g.bf[0].eU.Add(e1); _g.bf[0].eU.Add(e2);
            var sp = MakeSpell("s", "deal1_same_zone");
            _ss.ApplySpell(sp, Owner.Player, e1.uid);
            Assert.AreEqual(3, e1.currentHp);
            Assert.AreEqual(3, e2.currentHp);
        }

        // ─────────────────────────────────────────
        // 法盾检查
        // ─────────────────────────────────────────

        [Test]
        public void ApplySpell_LawShield_SpendsSch_AllowsTargeting()
        {
            var enemy = MakeUnit("e", 3, keywords: new List<string>{"法盾"});
            _g.eBase.Add(enemy);
            _g.pSch.blazing = 1;  // has sch
            var sp = MakeSpell("s", "stun");
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            Assert.AreEqual(0, _g.pSch.blazing);  // spent
            Assert.IsTrue(enemy.stunned);
        }

        [Test]
        public void ApplySpell_LawShield_NoSch_AbortSpell()
        {
            var enemy = MakeUnit("e", 3, keywords: new List<string>{"法盾"});
            _g.eBase.Add(enemy);
            // pSch total = 0 → cannot spend → abort
            var sp = MakeSpell("s", "stun");
            _ss.ApplySpell(sp, Owner.Player, enemy.uid);
            Assert.IsFalse(enemy.stunned);  // effect did not apply
        }

        // ─────────────────────────────────────────
        // 回响
        // ─────────────────────────────────────────

        [Test]
        public void Echo_FiredOnce_WhenPromptReturnsTrue()
        {
            _g.pDeck.Add(MakeUnit("d1", 1));
            _g.pDeck.Add(MakeUnit("d2", 1));
            // draw1 with echo (echoManaCost=0, echoSchCost=0 → always can echo)
            var sp = MakeSpell("s", "draw1", keywords: new List<string>{"回响"});
            _g.pMana = 5;
            _ss.PromptEcho = (_, __) => true;  // always echo
            _ss.ApplySpell(sp, Owner.Player, null, isEcho: false);
            Assert.AreEqual(2, _g.pHand.Count);  // drew twice
        }

        [Test]
        public void Echo_NotFired_WhenPromptReturnsFalse()
        {
            _g.pDeck.Add(MakeUnit("d1", 1));
            _g.pDeck.Add(MakeUnit("d2", 1));
            var sp = MakeSpell("s", "draw1", keywords: new List<string>{"回响"});
            _ss.PromptEcho = (_, __) => false;  // skip echo
            _ss.ApplySpell(sp, Owner.Player, null, isEcho: false);
            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void Echo_NotRecursive_WhenIsEchoTrue()
        {
            _g.pDeck.Add(MakeUnit("d1", 1));
            _g.pDeck.Add(MakeUnit("d2", 1));
            var sp = MakeSpell("s", "draw1", keywords: new List<string>{"回响"});
            _ss.PromptEcho = (_, __) => true;
            // call with isEcho=true → echo check skipped
            _ss.ApplySpell(sp, Owner.Player, null, isEcho: true);
            Assert.AreEqual(1, _g.pHand.Count);  // only drew once, no recursion
        }

        [Test]
        public void Echo_WithManaCost_DeductsMana()
        {
            _g.pDeck.Add(MakeUnit("d1", 1)); _g.pDeck.Add(MakeUnit("d2", 1));
            _g.pMana = 4;
            var sp = MakeSpell("s", "draw1", keywords: new List<string>{"回响"}, echoManaCost: 2);
            _ss.PromptEcho = (_, __) => true;
            _ss.ApplySpell(sp, Owner.Player, null, isEcho: false);
            Assert.AreEqual(2, _g.pMana);  // spent 2 for echo
            Assert.AreEqual(2, _g.pHand.Count);  // drew twice
        }

        // ─────────────────────────────────────────
        // 法术对决
        // ─────────────────────────────────────────

        [Test]
        public void StartSpellDuel_SetsDuelActive()
        {
            _ss.StartSpellDuel(1, Owner.Player);
            Assert.IsTrue(_g.duelActive);
            Assert.AreEqual(1, _g.duelBf);
            Assert.AreEqual(Owner.Player, _g.duelAttacker);
            Assert.AreEqual(Owner.Player, _g.duelTurn);
            Assert.AreEqual(0, _g.duelSkips);
        }

        [Test]
        public void SkipDuel_Once_FlipsToEnemy()
        {
            bool aiCalled = false;
            _ss.StartSpellDuel(1, Owner.Player);
            _ss.OnAiDuelTurn = () => aiCalled = true;
            _ss.SkipDuel();
            Assert.AreEqual(1, _g.duelSkips);
            Assert.AreEqual(Owner.Enemy, _g.duelTurn);
            Assert.IsTrue(aiCalled);
        }

        [Test]
        public void SkipDuel_TwiceByPlayer_EndsDuel()
        {
            _ss.StartSpellDuel(1, Owner.Player);
            _g.duelTurn = Owner.Player;
            _ss.OnAiDuelTurn = () =>
            {
                // AI skips immediately
                _ss.AiSkipDuel();
            };
            _ss.SkipDuel();  // player skip 1 → AI skip → duelSkips=2 → EndDuel
            Assert.IsFalse(_g.duelActive);
        }

        [Test]
        public void EndDuel_WithEnemyOnBF_TriggersCombat()
        {
            var enemy = MakeUnit("e", 3);
            _g.bf[0].eU.Add(enemy);
            _ss.StartSpellDuel(1, Owner.Player);
            _ss.EndDuel();
            Assert.IsFalse(_g.duelActive);
            // Combat triggered: player unit (none) vs enemy unit → enemy conquers
            // pBase is empty → enemy kills nothing → ctrl unchanged, but check bf state
            // enemy wins uncontested
            Assert.AreEqual(Owner.Enemy, _g.bf[0].ctrl);
        }

        [Test]
        public void EndDuel_EmptyBF_ConquestForAttacker()
        {
            _ss.StartSpellDuel(1, Owner.Player);
            bool callbackFired = false;
            _ss.OnDuelEnded = _ => callbackFired = true;
            _ss.EndDuel();
            Assert.IsFalse(_g.duelActive);
            Assert.AreEqual(Owner.Player, _g.bf[0].ctrl);
            Assert.IsTrue(callbackFired);
        }

        // ─────────────────────────────────────────
        // AiDuelAction
        // ─────────────────────────────────────────

        [Test]
        public void AiDuelAction_Skips_WhenNoFastCards()
        {
            _g.duelActive   = true;
            _g.duelBf       = 1;
            _g.duelAttacker = Owner.Player;
            _g.duelTurn     = Owner.Enemy;
            _g.duelSkips    = 1;  // one more skip → endDuel
            // no fast cards in hand
            _ai.AiDuelAction();
            Assert.IsFalse(_g.duelActive);  // endDuel called
        }

        [Test]
        public void AiDuelAction_CountersSpell_WhenHasCounterAndPlayerSpelledRecently()
        {
            _g.duelActive   = true;
            _g.duelBf       = 1;
            _g.duelAttacker = Owner.Player;
            _g.duelTurn     = Owner.Enemy;
            _g.duelSkips    = 0;
            _g.lastPlayerSpellCost = 3;
            _g.eMana = 5;
            var counter = MakeSpell("c", "counter_any", cost: 2, keywords: new List<string>{"反应"});
            _g.eHand.Add(counter);
            _ai.AiDuelAction();
            Assert.AreEqual(0, _g.eHand.Count);     // counter played
            Assert.AreEqual(0, _g.lastPlayerSpellCost);
            Assert.AreEqual(Owner.Player, _g.duelTurn);  // turn flipped to player
        }

        [Test]
        public void AiDuelAction_StunsBestEnemy_WhenLosingCombat()
        {
            _g.duelActive   = true;
            _g.duelBf       = 1;
            _g.duelAttacker = Owner.Player;
            _g.duelTurn     = Owner.Enemy;
            _g.duelSkips    = 0;
            _g.eMana = 3;
            // Enemy has weak unit, player has strong unit → powDiff < 0
            var eUnit = MakeUnit("e", 1); _g.bf[0].eU.Add(eUnit);
            var pUnit = MakeUnit("p", 5); _g.bf[0].pU.Add(pUnit);
            var stun = MakeSpell("stun", "stun_manual", cost: 2, keywords: new List<string>{"迅捷"});
            _g.eHand.Add(stun);
            _ai.AiDuelAction();
            Assert.IsTrue(pUnit.stunned);
            Assert.AreEqual(Owner.Player, _g.duelTurn);
        }

        // ─────────────────────────────────────────
        // AiAction spell playing
        // ─────────────────────────────────────────

        [Test]
        public void AiAction_PlaysRallyCall_BeforeUnits()
        {
            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;
            _g.eMana = 5;
            var rally = MakeSpell("r", "rally_call", cost: 2);
            // Add a unit in hand so AI has reason to play rally
            var unit = MakeUnit("u", 2, cost: 1);
            _g.eHand.Add(rally);
            _g.eHand.Add(unit);
            _ai.AiAction();
            // rally should have been played → in discard (eRallyActive resets at turn end)
            Assert.IsTrue(_g.eDiscard.Contains(rally));
            // unit enters active (not exhausted) because rally was played before deployment
            Assert.IsFalse(unit.exhausted);
        }

        [Test]
        public void AiAction_PlaysBalanceResolve_EarlyInTurn()
        {
            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;
            _g.eMana = 5;
            _g.eDeck.Add(MakeUnit("d", 1, cost: 4));  // too expensive to deploy after balance (costs 2, leaving 3 mana... cost 4 won't deploy)
            _g.eRuneDeck.Add(MakeRune());
            var balance = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "balance_resolve", type = CardType.Spell,
                cost = 2, effect = "balance_resolve", keywords = new List<string>(),
                tb = new TurnBuffs(), attachedEquipments = new List<CardInstance>()
            };
            _g.eHand.Add(balance);
            _ai.AiAction();
            Assert.AreEqual(1, _g.eHand.Count);   // drew 1 card
            Assert.AreEqual(1, _g.eRunes.Count);  // summoned 1 rune
        }
    }
}
