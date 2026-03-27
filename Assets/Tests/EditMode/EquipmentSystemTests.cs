using NUnit.Framework;
using System.Collections.Generic;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P7 行为验证测试 — 装备系统
    /// 对照原版：deployEquipAttach / activateEquipAbility / TryDeathShield(guardian_equip) /
    ///           TryDeathShield(death_shield) / atkBonus 应用 / trinityEquipped 标记
    /// </summary>
    public class EquipmentSystemTests
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
            _ai.Schedule = (_, fn) => fn();

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
                exhausted  = false,
                stunned    = false,
                tb         = new TurnBuffs(),
                keywords   = keywords ?? new List<string>(),
                attachedEquipments = new List<CardInstance>()
            };
        }

        private static CardInstance MakeEquip(string effect, int atkBonus = 0,
            int equipSchCost = 0, RuneType equipSchType = RuneType.Crushing,
            int cost = 2, int schCost = 0, RuneType schType = RuneType.Crushing)
        {
            return new CardInstance
            {
                uid           = CardInstance.AllocUid(),
                id            = effect,
                cardName      = effect,
                type          = CardType.Equipment,
                cost          = cost,
                atk           = 0,
                currentAtk    = 0,
                currentHp     = 0,
                exhausted     = false,
                stunned       = false,
                tb            = new TurnBuffs(),
                keywords      = new List<string>(),
                effect        = effect,
                atkBonus      = atkBonus,
                equipSchCost  = equipSchCost,
                equipSchType  = equipSchType,
                schCost       = schCost,
                schType       = schType,
                attachedEquipments = new List<CardInstance>()
            };
        }

        private static RuneInstance MakeRune(RuneType t = RuneType.Crushing)
            => RuneInstance.Create(t);

        // ─────────────────────────────────────────
        // AttachEquipToUnit — 基础附着
        // ─────────────────────────────────────────

        [Test]
        public void AttachEquipToUnit_AppliesAtkBonus()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("dorans_equip", atkBonus: 2);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);

            _cd.AttachEquipToUnit(equip, unit, Owner.Player, paySchCost: false);

            Assert.AreEqual(5, unit.atk);
            Assert.AreEqual(5, unit.currentAtk);
            Assert.AreEqual(5, unit.currentHp);
            Assert.IsTrue(unit.attachedEquipments.Contains(equip));
            Assert.IsFalse(_g.pBase.Contains(equip));  // removed from base
        }

        [Test]
        public void AttachEquipToUnit_TrinitySetsTrinityFlag()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("trinity_equip", atkBonus: 2);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);

            _cd.AttachEquipToUnit(equip, unit, Owner.Player, paySchCost: false);

            Assert.IsTrue(unit.trinityEquipped);
            Assert.AreEqual(5, unit.currentAtk);
        }

        [Test]
        public void AttachEquipToUnit_GuardianNoTrinityFlag()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("guardian_equip", atkBonus: 1);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);

            _cd.AttachEquipToUnit(equip, unit, Owner.Player, paySchCost: false);

            Assert.IsFalse(unit.trinityEquipped);
            Assert.AreEqual(4, unit.currentAtk);
        }

        [Test]
        public void AttachEquipToUnit_PaysSchCost()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("trinity_equip", atkBonus: 2, equipSchCost: 1, equipSchType: RuneType.Crushing);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);
            _g.pSch.Add(RuneType.Crushing, 2);

            _cd.AttachEquipToUnit(equip, unit, Owner.Player, paySchCost: true);

            Assert.AreEqual(1, _g.pSch.Get(RuneType.Crushing));  // spent 1
            Assert.AreEqual(5, unit.currentAtk);
        }

        [Test]
        public void AttachEquipToUnit_ZeroAtkBonus_NoStatChange()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("death_shield", atkBonus: 0);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);

            _cd.AttachEquipToUnit(equip, unit, Owner.Player, paySchCost: false);

            Assert.AreEqual(3, unit.currentAtk);
            Assert.IsTrue(unit.attachedEquipments.Contains(equip));
        }

        // ─────────────────────────────────────────
        // TryDeathShield — 中娅沙漏（基地）
        // ─────────────────────────────────────────

        [Test]
        public void TryDeathShield_Zhonya_SavesDyingUnit()
        {
            var unit   = MakeUnit("u", 3);
            var zhonya = MakeEquip("death_shield");
            _g.pBase.Add(zhonya);
            unit.currentHp = -1;  // dying

            bool saved = _cd.TryDeathShield(unit, _g.pBase, _g.pDiscard);

            Assert.IsTrue(saved);
            Assert.AreEqual(3, unit.currentHp);  // reset to currentAtk(=atk=3)
            Assert.IsTrue(unit.exhausted);
            Assert.IsTrue(_g.pBase.Contains(unit));
            Assert.IsTrue(_g.pDiscard.Contains(zhonya));
            Assert.IsFalse(_g.pBase.Contains(zhonya));
        }

        [Test]
        public void TryDeathShield_NoZhonya_ReturnsFalse()
        {
            var unit = MakeUnit("u", 3);
            unit.currentHp = -1;

            bool saved = _cd.TryDeathShield(unit, _g.pBase, _g.pDiscard);

            Assert.IsFalse(saved);
        }

        // ─────────────────────────────────────────
        // TryDeathShield — 守护天使（附着）
        // ─────────────────────────────────────────

        [Test]
        public void TryDeathShield_GuardianEquip_SavesDyingUnit()
        {
            var unit    = MakeUnit("u", 4);
            var guardian = MakeEquip("guardian_equip", atkBonus: 1);
            // Attach guardian: unit atk boosted to 5
            unit.currentAtk = 5; unit.atk = 5; unit.currentHp = 5;
            unit.attachedEquipments.Add(guardian);
            unit.currentHp = -1;  // dying

            bool saved = _cd.TryDeathShield(unit, _g.pBase, _g.pDiscard);

            Assert.IsTrue(saved);
            Assert.AreEqual(5, unit.currentHp);   // reset to currentAtk(5, keeps +1 bonus)
            Assert.IsTrue(unit.exhausted);
            Assert.IsTrue(_g.pBase.Contains(unit));
            Assert.IsTrue(_g.pDiscard.Contains(guardian));
            Assert.AreEqual(0, unit.attachedEquipments.Count);
        }

        [Test]
        public void TryDeathShield_GuardianEquip_PrioritisedOverZhonya()
        {
            var unit     = MakeUnit("u", 3);
            var guardian = MakeEquip("guardian_equip", atkBonus: 1);
            var zhonya   = MakeEquip("death_shield");
            unit.currentAtk = 4; unit.atk = 4; unit.currentHp = -1;
            unit.attachedEquipments.Add(guardian);
            _g.pBase.Add(zhonya);  // zhonya also in base

            bool saved = _cd.TryDeathShield(unit, _g.pBase, _g.pDiscard);

            Assert.IsTrue(saved);
            // guardian used (not zhonya)
            Assert.IsTrue(_g.pBase.Contains(zhonya));   // zhonya still in base
            Assert.IsTrue(_g.pDiscard.Contains(guardian));
        }

        // ─────────────────────────────────────────
        // CleanDeadAll — 死亡时装备进废牌堆
        // ─────────────────────────────────────────

        [Test]
        public void CleanDeadAll_UnitWithEquip_EquipGoesToDiscard()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("dorans_equip", atkBonus: 2);
            unit.attachedEquipments.Add(equip);
            unit.currentHp = 0;
            _g.bf[0].pU.Add(unit);

            _cd.CleanDeadAll();

            Assert.AreEqual(0, _g.bf[0].pU.Count);
            Assert.IsTrue(_g.pDiscard.Contains(equip));
            Assert.IsTrue(_g.pDiscard.Contains(unit));   // dead unit goes to discard
        }

        [Test]
        public void CleanDeadAll_EquipInBase_NotCleanedUp()
        {
            // Equipment in base should never be cleaned by currentHp check
            var equip = MakeEquip("dorans_equip");
            equip.currentHp = 0;
            _g.pBase.Add(equip);

            _cd.CleanDeadAll();

            Assert.IsTrue(_g.pBase.Contains(equip));  // equipment stays
        }

        [Test]
        public void CleanDeadAll_ZhonyaSavesUnitFromBF()
        {
            var unit   = MakeUnit("u", 3);
            var zhonya = MakeEquip("death_shield");
            _g.pBase.Add(zhonya);
            unit.currentHp = 0;
            _g.bf[0].pU.Add(unit);

            _cd.CleanDeadAll();

            Assert.AreEqual(0, _g.bf[0].pU.Count);    // removed from BF
            Assert.IsTrue(_g.pBase.Contains(unit));     // rescued to base
            Assert.IsTrue(unit.exhausted);
            Assert.IsTrue(_g.pDiscard.Contains(zhonya)); // zhonya destroyed
        }

        [Test]
        public void CleanDeadAll_GuardianEquipSavesUnitFromBF()
        {
            var unit    = MakeUnit("u", 3);
            var guardian = MakeEquip("guardian_equip", atkBonus: 1);
            unit.currentAtk = 4; unit.atk = 4; unit.currentHp = 0;
            unit.attachedEquipments.Add(guardian);
            _g.bf[0].pU.Add(unit);

            _cd.CleanDeadAll();

            Assert.AreEqual(0, _g.bf[0].pU.Count);
            Assert.IsTrue(_g.pBase.Contains(unit));
            Assert.IsTrue(unit.exhausted);
            Assert.IsTrue(_g.pDiscard.Contains(guardian));
        }

        // ─────────────────────────────────────────
        // SpellSystem — 装备效果 (trinity/guardian/dorans)
        // ─────────────────────────────────────────

        [Test]
        public void ApplySpell_DoransEquip_AttachesToTarget()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("dorans_equip", atkBonus: 2);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);
            _g.LastDeployedUid = equip.uid;
            // PromptTarget returns unit
            _ss.PromptTarget = list => list.Find(u => u.uid == unit.uid);

            _ss.ApplySpell(equip, Owner.Player, null);

            Assert.AreEqual(5, unit.currentAtk);
            Assert.IsTrue(unit.attachedEquipments.Contains(equip));
            Assert.IsFalse(_g.pBase.Contains(equip));
        }

        [Test]
        public void ApplySpell_TrinityEquip_SetsTrinityFlag()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("trinity_equip", atkBonus: 2);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);
            _g.LastDeployedUid = equip.uid;
            _ss.PromptTarget = list => list[0];

            _ss.ApplySpell(equip, Owner.Player, null);

            Assert.IsTrue(unit.trinityEquipped);
        }

        [Test]
        public void ApplySpell_EquipNoUnitsInBase_StaysInBase()
        {
            // No non-equipment units in base → equipment stays
            var equip = MakeEquip("dorans_equip", atkBonus: 2);
            _g.pBase.Add(equip);
            _g.LastDeployedUid = equip.uid;

            _ss.ApplySpell(equip, Owner.Player, null);

            Assert.IsTrue(_g.pBase.Contains(equip));  // still in base
        }

        [Test]
        public void ApplySpell_EquipPromptReturnsNull_StaysInBase()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("dorans_equip", atkBonus: 2);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);
            _g.LastDeployedUid = equip.uid;
            _ss.PromptTarget = _ => null;  // player skips attachment

            _ss.ApplySpell(equip, Owner.Player, null);

            Assert.IsTrue(_g.pBase.Contains(equip));  // stays in base
            Assert.AreEqual(3, unit.currentAtk);       // no bonus applied
        }

        // ─────────────────────────────────────────
        // ActivateEquipAbility
        // ─────────────────────────────────────────

        [Test]
        public void ActivateEquipAbility_PaysSchAndAttaches()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("trinity_equip", atkBonus: 2, equipSchCost: 1, equipSchType: RuneType.Crushing);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);
            _g.pSch.Add(RuneType.Crushing, 1);
            _ss.PromptTarget = list => list[0];

            bool result = _ss.ActivateEquipAbility(equip, Owner.Player);

            Assert.IsTrue(result);
            Assert.AreEqual(5, unit.currentAtk);
            Assert.AreEqual(0, _g.pSch.Get(RuneType.Crushing));  // spent
            Assert.IsTrue(unit.trinityEquipped);
        }

        [Test]
        public void ActivateEquipAbility_InsufficientSch_ReturnsFalse()
        {
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("trinity_equip", atkBonus: 2, equipSchCost: 1, equipSchType: RuneType.Crushing);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);
            // No crushing sch

            bool result = _ss.ActivateEquipAbility(equip, Owner.Player);

            Assert.IsFalse(result);
            Assert.AreEqual(3, unit.currentAtk);
        }

        [Test]
        public void ActivateEquipAbility_WrongTurn_ReturnsFalse()
        {
            _g.turn = Owner.Enemy;
            var unit  = MakeUnit("u", 3);
            var equip = MakeEquip("trinity_equip", atkBonus: 2);
            _g.pBase.Add(unit);
            _g.pBase.Add(equip);
            _ss.PromptTarget = list => list[0];

            bool result = _ss.ActivateEquipAbility(equip, Owner.Player);

            Assert.IsFalse(result);
        }

        // ─────────────────────────────────────────
        // AI 装备部署
        // ─────────────────────────────────────────

        [Test]
        public void AiAction_DeploysEquip_AndAttachesToHighestAtkUnit()
        {
            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;
            _g.eMana = 5;

            var unit1 = MakeUnit("u1", 2, cost: 1);
            var unit2 = MakeUnit("u2", 4, cost: 1);
            var equip = MakeEquip("dorans_equip", atkBonus: 2, cost: 2);
            _g.eHand.Add(equip);
            // Pre-deploy units so they're in base
            _g.eBase.Add(unit1);
            _g.eBase.Add(unit2);

            _ai.AiAction();

            // equip should have been played (in eDiscard since it was attached to a unit)
            // unit2 (higher atk=4) should have the bonus
            Assert.AreEqual(6, unit2.currentAtk);
            Assert.IsTrue(unit2.attachedEquipments.Count > 0);
        }
    }
}
