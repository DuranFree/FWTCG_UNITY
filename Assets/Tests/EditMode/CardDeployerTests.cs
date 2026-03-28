using NUnit.Framework;
using System.Collections.Generic;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P3 行为验证测试 — CardDeployer
    /// 对照原版：spell.js canPlay / deployToBase / deployToBF /
    ///           moveUnit / dealDamage / cleanDeadAll / onSummon /
    ///           triggerDeathwish / tryDeathShield
    /// </summary>
    public class CardDeployerTests
    {
        private GameState _g;
        private TurnManager _tm;
        private CardDeployer _cd;

        [SetUp]
        public void SetUp()
        {
            CardInstance.ResetUidCounter();
            RuneInstance.ResetUidCounter();
            _g  = new GameState();
            _tm = new TurnManager(_g);
            _cd = new CardDeployer(_g, _tm);
            _g.turn  = Owner.Player;
            _g.phase = GamePhase.Action;
            _g.pMana = 10; // 充足法力
        }

        // ─────────────────────────────────────────
        // CanPlay
        // ─────────────────────────────────────────

        [Test]
        public void CanPlay_False_WhenGameOver()
        {
            _g.gameOver = true;
            var c = MakeCard("c", 2, CardType.Follower);
            Assert.IsFalse(_cd.CanPlay(c, Owner.Player));
        }

        [Test]
        public void CanPlay_False_WhenNotActionPhase()
        {
            _g.phase = GamePhase.Summon;
            var c = MakeCard("c", 0, CardType.Follower);
            Assert.IsFalse(_cd.CanPlay(c, Owner.Player));
        }

        [Test]
        public void CanPlay_False_WhenInsufficientMana()
        {
            _g.pMana = 1;
            var c = MakeCard("c", 5, CardType.Follower);
            Assert.IsFalse(_cd.CanPlay(c, Owner.Player));
        }

        [Test]
        public void CanPlay_False_WhenInsufficientSch()
        {
            var c = MakeCard("c", 0, CardType.Spell);
            c.schCost = 2; c.schType = RuneType.Blazing;
            _g.pSch.Add(RuneType.Blazing, 1); // only 1, need 2
            Assert.IsFalse(_cd.CanPlay(c, Owner.Player));
        }

        [Test]
        public void CanPlay_True_NormalConditions()
        {
            var c = MakeCard("c", 2, CardType.Follower);
            Assert.IsTrue(_cd.CanPlay(c, Owner.Player));
        }

        [Test]
        public void CanPlay_False_WhenCardLocked()
        {
            _g.cardLockTarget = Owner.Player;
            var c = MakeCard("c", 0, CardType.Follower);
            Assert.IsFalse(_cd.CanPlay(c, Owner.Player));
        }

        [Test]
        public void CanPlay_False_InDuel_NonFastCard()
        {
            _g.duelActive = true;
            _g.duelTurn   = Owner.Player;
            var c = MakeCard("c", 0, CardType.Follower); // no keywords
            Assert.IsFalse(_cd.CanPlay(c, Owner.Player));
        }

        [Test]
        public void CanPlay_True_InDuel_FastCard()
        {
            _g.duelActive = true;
            _g.duelTurn   = Owner.Player;
            var c = MakeCard("c", 0, CardType.Spell);
            c.keywords.Add("迅捷");
            Assert.IsTrue(_cd.CanPlay(c, Owner.Player));
        }

        // ─────────────────────────────────────────
        // GetEffectiveCost
        // ─────────────────────────────────────────

        [Test]
        public void GetEffectiveCost_NormalCard_ReturnsCost()
        {
            var c = MakeCard("c", 3, CardType.Spell);
            Assert.AreEqual(3, _cd.GetEffectiveCost(c, Owner.Player));
        }

        [Test]
        public void GetEffectiveCost_BalanceResolve_DiscountWhenOppNear8()
        {
            _g.eScore = 6; // 对手距胜利≤3（8-6=2）
            var c = MakeCard("balance_resolve", 4, CardType.Spell);
            c.effect = "balance_resolve";
            Assert.AreEqual(2, _cd.GetEffectiveCost(c, Owner.Player));
        }

        [Test]
        public void GetEffectiveCost_BalanceResolve_NoDiscountWhenOppFar()
        {
            _g.eScore = 2;
            var c = MakeCard("balance_resolve", 4, CardType.Spell);
            c.effect = "balance_resolve";
            Assert.AreEqual(4, _cd.GetEffectiveCost(c, Owner.Player));
        }

        // ─────────────────────────────────────────
        // CardInstance.Mk()
        // ─────────────────────────────────────────

        [Test]
        public void Mk_CreatesNewUid()
        {
            var c = MakeCard("u", 3, CardType.Follower);
            var deployed = c.Mk();
            Assert.AreNotEqual(c.uid, deployed.uid);
        }

        [Test]
        public void Mk_SetsCurrentHpToAtk()
        {
            var c = MakeUnit("u", 5); // atk=5
            c.currentHp = 2; // simulate damage on hand card (shouldn't happen but be safe)
            var deployed = c.Mk();
            Assert.AreEqual(5, deployed.currentHp);
            Assert.AreEqual(5, deployed.currentAtk);
        }

        [Test]
        public void Mk_ResetsRuntimeState()
        {
            var c = MakeCard("u", 3, CardType.Follower);
            c.exhausted = true; c.stunned = true; c.buffToken = true;
            var deployed = c.Mk();
            Assert.IsFalse(deployed.exhausted);
            Assert.IsFalse(deployed.stunned);
            Assert.IsFalse(deployed.buffToken);
            Assert.AreEqual(0, deployed.tb.atk);
        }

        // ─────────────────────────────────────────
        // DeployToBase
        // ─────────────────────────────────────────

        [Test]
        public void DeployToBase_AddsUnitToBase()
        {
            var card = AddToHand(MakeCard("u", 3, CardType.Follower));
            _cd.DeployToBase(card, Owner.Player);
            Assert.AreEqual(1, _g.pBase.Count);
        }

        [Test]
        public void DeployToBase_DeductsMana()
        {
            _g.pMana = 5;
            var card = AddToHand(MakeCard("u", 3, CardType.Follower));
            _cd.DeployToBase(card, Owner.Player);
            Assert.AreEqual(2, _g.pMana);
        }

        [Test]
        public void DeployToBase_DeductsSch()
        {
            _g.pSch.Add(RuneType.Blazing, 3);
            var card = AddToHand(MakeCard("u", 0, CardType.Follower));
            card.schCost = 2; card.schType = RuneType.Blazing;
            _cd.DeployToBase(card, Owner.Player);
            Assert.AreEqual(1, _g.pSch.Get(RuneType.Blazing));
        }

        [Test]
        public void DeployToBase_RemovesCardFromHand()
        {
            var card = AddToHand(MakeCard("u", 1, CardType.Follower));
            _cd.DeployToBase(card, Owner.Player);
            Assert.AreEqual(0, _g.pHand.Count);
        }

        [Test]
        public void DeployToBase_IncreasesCardsPlayedThisTurn()
        {
            var card = AddToHand(MakeCard("u", 0, CardType.Follower));
            _cd.DeployToBase(card, Owner.Player);
            Assert.AreEqual(1, _g.cardsPlayedThisTurn);
        }

        [Test]
        public void DeployToBase_UnitIsExhausted_ByDefault()
        {
            var card = AddToHand(MakeCard("u", 2, CardType.Follower));
            var unit = _cd.DeployToBase(card, Owner.Player);
            Assert.IsTrue(unit.exhausted);
        }

        [Test]
        public void DeployToBase_UnitIsActive_WhenEnterActiveTrue()
        {
            var card = AddToHand(MakeCard("u", 2, CardType.Follower));
            var unit = _cd.DeployToBase(card, Owner.Player, enterActive: true);
            Assert.IsFalse(unit.exhausted);
        }

        [Test]
        public void DeployToBase_UnitIsActive_WhenRallyActive()
        {
            _g.pRallyActive = true;
            var card = AddToHand(MakeCard("u", 2, CardType.Follower));
            var unit = _cd.DeployToBase(card, Owner.Player);
            Assert.IsFalse(unit.exhausted);
        }

        [Test]
        public void DeployToBase_AppliesNextAllyBuff()
        {
            _g.pNextAllyBuff = 2;
            var card = AddToHand(MakeUnit("u", 3)); // atk=3, 3+2=5
            var unit = _cd.DeployToBase(card, Owner.Player);
            Assert.AreEqual(5, unit.currentAtk);
            Assert.AreEqual(5, unit.currentHp);
            Assert.AreEqual(0, _g.pNextAllyBuff);
        }

        [Test]
        public void DeployToBase_CreatesNewUid_NotHandCardUid()
        {
            var card = AddToHand(MakeCard("u", 2, CardType.Follower));
            int handUid = card.uid;
            var unit = _cd.DeployToBase(card, Owner.Player);
            Assert.AreNotEqual(handUid, unit.uid);
        }

        // ─────────────────────────────────────────
        // DeployToBF
        // ─────────────────────────────────────────

        [Test]
        public void DeployToBF_AddsUnitToSlot()
        {
            var card = AddToHand(MakeCard("u", 3, CardType.Follower));
            _cd.DeployToBF(card, Owner.Player, 1);
            Assert.AreEqual(1, _g.bf[0].pU.Count);
        }

        [Test]
        public void DeployToBF_UnitIsExhausted_ByDefault()
        {
            var card = AddToHand(MakeCard("u", 2, CardType.Follower));
            var unit = _cd.DeployToBF(card, Owner.Player, 1);
            Assert.IsTrue(unit.exhausted);
        }

        [Test]
        public void DeployToBF_UnitIsActive_WhenRallyActive()
        {
            _g.pRallyActive = true;
            var card = AddToHand(MakeCard("u", 2, CardType.Follower));
            var unit = _cd.DeployToBF(card, Owner.Player, 2);
            Assert.IsFalse(unit.exhausted);
        }

        [Test]
        public void DeployToBF_InvalidBfId_ReturnsNull()
        {
            var card = AddToHand(MakeCard("u", 0, CardType.Follower));
            var unit = _cd.DeployToBF(card, Owner.Player, 99);
            Assert.IsNull(unit);
        }

        // ─────────────────────────────────────────
        // MoveUnit
        // ─────────────────────────────────────────

        [Test]
        public void MoveUnit_FromBaseToBF()
        {
            var unit = MakeUnit("u", 3);
            _g.pBase.Add(unit);

            _cd.MoveUnit(unit, Owner.Player, "1");

            Assert.AreEqual(0, _g.pBase.Count);
            Assert.AreEqual(1, _g.bf[0].pU.Count);
        }

        [Test]
        public void MoveUnit_FromBFToBase()
        {
            var unit = MakeUnit("u", 3);
            _g.bf[0].pU.Add(unit);

            _cd.MoveUnit(unit, Owner.Player, "base");

            Assert.AreEqual(0, _g.bf[0].pU.Count);
            Assert.AreEqual(1, _g.pBase.Count);
        }

        [Test]
        public void MoveUnit_Equipment_ForcedToBase()
        {
            var equip = MakeUnit("eq", 0);
            equip.type = CardType.Equipment;
            _g.pBase.Add(equip);

            _cd.MoveUnit(equip, Owner.Player, "1"); // tries to go to BF

            Assert.AreEqual(1, _g.pBase.Count, "装备应被强制留在基地");
            Assert.AreEqual(0, _g.bf[0].pU.Count);
        }

        [Test]
        public void MoveUnit_NoUnitDuplication()
        {
            var unit = MakeUnit("u", 3);
            _g.pBase.Add(unit);
            _cd.MoveUnit(unit, Owner.Player, "1");

            int total = _g.pBase.Count + _g.bf[0].pU.Count + _g.bf[1].pU.Count;
            Assert.AreEqual(1, total, "单位不应重复存在");
        }

        // ─────────────────────────────────────────
        // DealDamage
        // ─────────────────────────────────────────

        [Test]
        public void DealDamage_ReducesUnitHp()
        {
            var unit = MakeUnit("u", 5);
            _g.pBase.Add(unit);
            _cd.DealDamage(unit, 2, Owner.Player);
            Assert.AreEqual(3, unit.currentHp);
        }

        [Test]
        public void DealDamage_ClampsToZero_AndUnitDies()
        {
            // 超量伤害：单位应阵亡（进废牌堆），而非停在 currentHp=0
            // CleanDeadAll 在 DealDamage 内调用后会 reset HP，故验证结果而非中间值
            var unit = MakeUnit("u", 3);
            _g.pBase.Add(unit);
            _cd.DealDamage(unit, 10, Owner.Player);
            Assert.AreEqual(0, _g.pBase.Count, "单位应从基地移除");
            Assert.AreEqual(1, _g.pDiscard.Count, "单位应进废牌堆");
        }

        [Test]
        public void DealDamage_KilledUnit_RemovedFromBase()
        {
            var unit = MakeUnit("u", 3);
            _g.pBase.Add(unit);
            _cd.DealDamage(unit, 3, Owner.Player);
            Assert.AreEqual(0, _g.pBase.Count);
            Assert.AreEqual(1, _g.pDiscard.Count);
        }

        [Test]
        public void DealDamage_Legend_ReducesLegendHp()
        {
            var legData = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            legData.atk = 5; legData.hp = 14;
            _g.eLeg = LegendInstance.From(legData);

            _cd.DealDamage(null, 4, Owner.Enemy, isLegend: true);

            Assert.AreEqual(10, _g.eLeg.currentHp);
        }

        /// <summary>
        /// 传奇受伤/死亡链集成测试：
        /// DealDamage(isLegend=true) → pLeg.currentHp=0 → CheckWin → gameOver=true
        /// 验证法术/技能致命伤害能正确触发游戏结束，修复前 CheckWin 未被调用的 Bug。
        /// </summary>
        [Test]
        public void DealDamage_LegendLethalDamage_TriggersGameOver()
        {
            var legData = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            legData.atk = 5; legData.hp = 14;
            _g.pLeg = LegendInstance.From(legData);   // 玩家传奇 HP=14

            _cd.DealDamage(null, 14, Owner.Player, isLegend: true); // 致命伤害

            Assert.AreEqual(0, _g.pLeg.currentHp, "传奇 HP 应降至 0");
            Assert.IsTrue(_g.gameOver,             "游戏应在传奇死亡后立即结束");
        }

        [Test]
        public void DealDamage_LegendNonLethal_DoesNotEndGame()
        {
            var legData = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            legData.atk = 5; legData.hp = 14;
            _g.eLeg = LegendInstance.From(legData);   // 敌方传奇 HP=14

            _cd.DealDamage(null, 5, Owner.Enemy, isLegend: true);   // 非致命

            Assert.AreEqual(9, _g.eLeg.currentHp, "传奇 HP 应为 14-5=9");
            Assert.IsFalse(_g.gameOver,            "非致命伤害不应结束游戏");
        }

        // ─────────────────────────────────────────
        // CleanDeadAll
        // ─────────────────────────────────────────

        [Test]
        public void CleanDeadAll_RemovesDeadFromBF()
        {
            var dead = MakeUnit("d", 2);
            dead.currentHp = 0;
            _g.bf[0].pU.Add(dead);

            _cd.CleanDeadAll();

            Assert.AreEqual(0, _g.bf[0].pU.Count);
        }

        [Test]
        public void CleanDeadAll_MovesDeadToDiscard()
        {
            var dead = MakeUnit("d", 2);
            dead.currentHp = 0;
            _g.pBase.Add(dead);

            _cd.CleanDeadAll();

            Assert.AreEqual(1, _g.pDiscard.Count);
        }

        [Test]
        public void CleanDeadAll_AliveUnits_NotRemoved()
        {
            var alive = MakeUnit("a", 3);
            _g.pBase.Add(alive);

            _cd.CleanDeadAll();

            Assert.AreEqual(1, _g.pBase.Count);
        }

        [Test]
        public void CleanDeadAll_AttachedEquipGoesToDiscard()
        {
            var dead  = MakeUnit("d", 2);
            dead.currentHp = 0;
            var equip = MakeUnit("eq", 0);
            equip.type = CardType.Equipment;
            dead.attachedEquipments.Add(equip);
            _g.pBase.Add(dead);

            _cd.CleanDeadAll();

            // dead unit + attached equip both go to discard = 2 entries
            Assert.AreEqual(2, _g.pDiscard.Count, "dead unit + attached equip in discard");
            Assert.IsTrue(_g.pDiscard.Contains(equip), "attached equip should be in discard");
        }

        [Test]
        public void CleanDeadAll_Equipment_NotRemovedFromBase()
        {
            // 装备 currentHp 可能为 0，不应被当作死亡单位清除
            var equip = MakeUnit("eq", 0);
            equip.type = CardType.Equipment;
            equip.currentHp = 0;
            _g.pBase.Add(equip);

            _cd.CleanDeadAll();

            Assert.AreEqual(1, _g.pBase.Count, "装备不应被死亡清理移除");
        }

        [Test]
        public void CleanDeadAll_MisplacedEquipInBF_RecalledToBase()
        {
            var equip = MakeUnit("eq", 0);
            equip.type = CardType.Equipment;
            _g.bf[0].pU.Add(equip); // 误入战场

            _cd.CleanDeadAll();

            Assert.AreEqual(0, _g.bf[0].pU.Count);
            Assert.AreEqual(1, _g.pBase.Count, "装备应召回基地");
        }

        // ─────────────────────────────────────────
        // TryDeathShield
        // ─────────────────────────────────────────

        [Test]
        public void TryDeathShield_ReturnsFalse_WhenNoShield()
        {
            var dying = MakeUnit("d", 3);
            bool result = _cd.TryDeathShield(dying, _g.pBase, _g.pDiscard);
            Assert.IsFalse(result);
        }

        [Test]
        public void TryDeathShield_SavesUnit_AndDestroysShield()
        {
            var shield = MakeUnit("zhonya", 0);
            shield.effect = "death_shield";
            shield.type   = CardType.Equipment;
            _g.pBase.Add(shield);

            var dying = MakeUnit("d", 3);
            dying.currentHp = 0;

            bool result = _cd.TryDeathShield(dying, _g.pBase, _g.pDiscard);

            Assert.IsTrue(result);
            Assert.IsTrue(_g.pBase.Contains(dying), "濒死单位应被救回基地");
            Assert.IsTrue(dying.exhausted, "救回后应为疲惫状态");
            Assert.IsTrue(_g.pDiscard.Contains(shield), "盾牌应进废牌堆");
        }

        // ─────────────────────────────────────────
        // TriggerDeathwish
        // ─────────────────────────────────────────

        [Test]
        public void TriggerDeathwish_AlertSentinel_DrawsCard()
        {
            _g.pDeck.Add(MakeUnit("card", 1));
            var sentinel = MakeUnit("alert_sentinel", 2);

            _cd.TriggerDeathwish(sentinel, Owner.Player);

            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void TriggerDeathwish_VoidSentinel_IncreasesNextAllyBuff()
        {
            var sentinel = MakeUnit("void_sentinel", 2);
            _cd.TriggerDeathwish(sentinel, Owner.Player);
            Assert.AreEqual(1, _g.pNextAllyBuff);
        }

        [Test]
        public void TriggerDeathwish_Voidling_CreatesFragmentInHand()
        {
            var voidling = MakeUnit("voidling", 1);
            _cd.TriggerDeathwish(voidling, Owner.Player);
            Assert.AreEqual(1, _g.pHand.Count);
            Assert.AreEqual("fragment", _g.pHand[0].id);
        }

        [Test]
        public void TriggerDeathwish_Voidling_NoFragment_WhenHandFull()
        {
            for (int i = 0; i < GameState.MAX_HAND; i++)
                _g.pHand.Add(MakeUnit("fill", 1));
            var voidling = MakeUnit("voidling", 1);
            _cd.TriggerDeathwish(voidling, Owner.Player);
            Assert.AreEqual(GameState.MAX_HAND, _g.pHand.Count);
        }

        // ─────────────────────────────────────────
        // OnSummon effects
        // ─────────────────────────────────────────

        [Test]
        public void OnSummon_NextAllyBuff_Applied()
        {
            _g.pNextAllyBuff = 1;
            var unit = MakeUnit("u", 3);
            _cd.OnSummon(unit, Owner.Player);
            Assert.AreEqual(4, unit.currentAtk);
            Assert.AreEqual(0, _g.pNextAllyBuff);
        }

        [Test]
        public void OnSummon_YordelInstructor_DrawsCard()
        {
            _g.pDeck.Add(MakeUnit("card", 1));
            var instructor = MakeUnit("instructor", 2);
            instructor.effect = "yordel_instructor_enter";
            _cd.OnSummon(instructor, Owner.Player);
            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void OnSummon_DariusSecondCard_Bonus_WhenSecondCard()
        {
            _g.cardsPlayedThisTurn = 2; // already played 1 card before this
            var darius = MakeUnit("darius", 4);
            darius.effect = "darius_second_card";
            _cd.OnSummon(darius, Owner.Player);
            Assert.AreEqual(2, darius.tb.atk);
            Assert.IsFalse(darius.exhausted);
        }

        [Test]
        public void OnSummon_DariusSecondCard_NoBonus_WhenFirstCard()
        {
            _g.cardsPlayedThisTurn = 1; // only 1 played (this is the first)
            var darius = MakeUnit("darius", 4);
            darius.effect = "darius_second_card";
            _cd.OnSummon(darius, Owner.Player);
            Assert.AreEqual(0, darius.tb.atk);
        }

        [Test]
        public void OnSummon_ThousandTail_DebuffsAllEnemies()
        {
            var enemy1 = MakeUnit("e1", 4);
            var enemy2 = MakeUnit("e2", 3);
            _g.eBase.Add(enemy1);
            _g.bf[0].eU.Add(enemy2);

            var overseer = MakeUnit("overseer", 2);
            overseer.effect = "thousand_tail_enter";
            _cd.OnSummon(overseer, Owner.Player);

            Assert.AreEqual(-3, enemy1.tb.atk);
            Assert.AreEqual(-3, enemy2.tb.atk);
        }

        [Test]
        public void OnSummon_MalphEnter_BonusPerHoldAlly()
        {
            // 2 "坚守" units in base
            var hold1 = MakeUnit("h1", 2);
            hold1.keywords.Add("坚守");
            var hold2 = MakeUnit("h2", 2);
            hold2.keywords.Add("坚守");
            _g.pBase.Add(hold1);
            _g.pBase.Add(hold2);

            var malph = MakeUnit("malph", 3);
            malph.effect = "malph_enter";
            _cd.OnSummon(malph, Owner.Player);

            Assert.AreEqual(2, malph.tb.atk);
        }

        // ─────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────

        private CardInstance MakeCard(string id, int cost, CardType type)
        {
            var d = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            d.id = id; d.cardName = id; d.cost = cost; d.atk = 2;
            d.type = type;
            d.keywords = new List<string>();
            return CardInstance.From(d);
        }

        private CardInstance MakeUnit(string id, int atk)
        {
            var d = UnityEngine.ScriptableObject.CreateInstance<CardData>();
            d.id = id; d.cardName = id; d.atk = atk; d.cost = 0;
            d.type = CardType.Follower;
            d.keywords = new List<string>();
            return CardInstance.From(d);
        }

        private CardInstance AddToHand(CardInstance card)
        {
            _g.pHand.Add(card);
            return card;
        }
    }
}
