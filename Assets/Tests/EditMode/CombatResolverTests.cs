using NUnit.Framework;
using System.Collections.Generic;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P4 行为验证测试 — CombatResolver
    /// 对照原版：combat.js triggerCombat / cleanDead / roleAtk / assignDmg
    /// </summary>
    public class CombatResolverTests
    {
        private GameState       _g;
        private TurnManager     _tm;
        private CardDeployer    _cd;
        private CombatResolver  _cr;

        [SetUp]
        public void SetUp()
        {
            CardInstance.ResetUidCounter();
            RuneInstance.ResetUidCounter();
            _g  = new GameState();
            _tm = new TurnManager(_g);
            _cd = new CardDeployer(_g, _tm);
            _cr = new CombatResolver(_g, _tm, _cd);
            _g.turn  = Owner.Player;
            _g.phase = GamePhase.Action;
        }

        // ─────────────────────────────────────────
        // 辅助工厂
        // ─────────────────────────────────────────

        private static CardInstance MakeUnit(string id, int atk,
            List<string> keywords = null)
        {
            return new CardInstance
            {
                uid        = CardInstance.AllocUid(),
                id         = id,
                cardName   = id,
                type       = CardType.Follower,
                cost       = 1,
                atk        = atk,
                currentAtk = atk,
                currentHp  = atk,
                keywords   = keywords ?? new List<string>(),
                tb         = new TurnBuffs { atk = 0 },
                attachedEquipments = new List<CardInstance>(),
                strongAtkBonus = 1,
                guardBonus     = 1,
            };
        }

        private static CardInstance MakeEquipment(string id)
        {
            return new CardInstance
            {
                uid        = CardInstance.AllocUid(),
                id         = id,
                cardName   = id,
                type       = CardType.Equipment,
                cost       = 0,
                atk        = 0,
                currentAtk = 0,
                currentHp  = 0,
                keywords   = new List<string>(),
                tb         = new TurnBuffs(),
                attachedEquipments = new List<CardInstance>()
            };
        }

        // ─────────────────────────────────────────
        // RoleAtk
        // ─────────────────────────────────────────

        [Test]
        public void RoleAtk_Base_NormalUnit()
        {
            var u = MakeUnit("u", 3);
            Assert.AreEqual(3, CombatResolver.RoleAtk(u, "attacker"));
            Assert.AreEqual(3, CombatResolver.RoleAtk(u, "defender"));
        }

        [Test]
        public void RoleAtk_StrongAtk_AppliesAsAttacker()
        {
            var u = MakeUnit("u", 3, new List<string> { "强攻" });
            u.strongAtkBonus = 2;
            Assert.AreEqual(5, CombatResolver.RoleAtk(u, "attacker"));
        }

        [Test]
        public void RoleAtk_StrongAtk_NoBonus_AsDefender()
        {
            var u = MakeUnit("u", 3, new List<string> { "强攻" });
            u.strongAtkBonus = 2;
            Assert.AreEqual(3, CombatResolver.RoleAtk(u, "defender"));
        }

        [Test]
        public void RoleAtk_Guard_AppliesAsDefender()
        {
            var u = MakeUnit("u", 3, new List<string> { "坚守" });
            u.guardBonus = 1;
            Assert.AreEqual(4, CombatResolver.RoleAtk(u, "defender"));
        }

        [Test]
        public void RoleAtk_Guard_NoBonus_AsAttacker()
        {
            var u = MakeUnit("u", 3, new List<string> { "坚守" });
            u.guardBonus = 1;
            Assert.AreEqual(3, CombatResolver.RoleAtk(u, "attacker"));
        }

        [Test]
        public void RoleAtk_TurnBuff_Included()
        {
            var u = MakeUnit("u", 3);
            u.tb.atk = 2;
            Assert.AreEqual(5, CombatResolver.RoleAtk(u, "attacker"));
        }

        [Test]
        public void RoleAtk_MinimumOne()
        {
            var u = MakeUnit("u", 1);
            u.currentAtk = 0;  // debuffed to 0
            Assert.AreEqual(1, CombatResolver.RoleAtk(u, "attacker"));
        }

        // ─────────────────────────────────────────
        // 基本战斗结局
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_Conquer_AtkKillsDef()
        {
            // 进攻3 vs 防守2 → 进攻方胜，征服
            var atk = MakeUnit("atk", 3);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            var result = _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(CombatResult.Conquer, result);
            Assert.AreEqual(Owner.Player, _g.bf[0].ctrl);
            Assert.AreEqual(1, _g.pScore);
        }

        [Test]
        public void TriggerCombat_Defend_DefKillsAtk()
        {
            // 进攻2 vs 防守3 → 防守方胜
            var atk = MakeUnit("atk", 2);
            var def = MakeUnit("def", 3);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            var result = _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(CombatResult.Defend, result);
            Assert.AreEqual(Owner.Enemy, _g.bf[0].ctrl);
            Assert.AreEqual(0, _g.pScore);
        }

        [Test]
        public void TriggerCombat_Draw_BothAlive_AtkRetreats()
        {
            // 进攻3 vs 防守3 → 双方存活（同atk互伤，但都扛得住？）
            // atk=3 means currentHp=3; dealing 3 damage kills.
            // Need atk to survive: give atk 4 HP, def 4 HP but atk=3
            // atk(3 HP) vs def(3 HP): atk deals 3 to def → def HP=0 dead
            // → not a draw. Let's use 2v2 with barriers:
            // Better: use multi-unit scenario or units that survive
            // Simplest: atk=2, def=2, both have atk=2=HP so both die → BothDead
            // For Draw: atk needs to survive defender's dmg; def needs to survive attacker's dmg
            // atk unit: currentHp=4, currentAtk=2 so it deals 2, absorbs 2 → survives (4-2=2>0)
            // def unit: currentHp=4, currentAtk=2 so it deals 2, absorbs 2 → survives (4-2=2>0)
            var atk = MakeUnit("atk", 2); atk.currentHp = 4;
            var def = MakeUnit("def", 2); def.currentHp = 4;
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            var result = _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(CombatResult.Draw, result);
            Assert.AreEqual(Owner.Enemy, _g.bf[0].ctrl);
            // 进攻方撤回基地
            Assert.AreEqual(0, _g.bf[0].pU.Count);
            Assert.IsTrue(_g.pBase.Count > 0);
        }

        [Test]
        public void TriggerCombat_BothDead()
        {
            // 进攻2 vs 防守2 → 双方阵亡
            var atk = MakeUnit("atk", 2);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            var result = _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(CombatResult.BothDead, result);
            Assert.IsNull(_g.bf[0].ctrl);
        }

        // ─────────────────────────────────────────
        // 眩晕 / 壁垒 / 压制
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_Stunned_DealsZero()
        {
            // 眩晕进攻方 → 输出0伤害 → 防守方不死 → Defend
            var atk = MakeUnit("atk", 5);
            atk.stunned = true;
            var def = MakeUnit("def", 1); // 正常输出1 → atk HP=5−1=4 alive
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            var result = _cr.TriggerCombat(1, Owner.Player);

            // atk deals 0 (stunned) → def survives; def deals 1 → atk HP=5−1=4 alive
            // Both alive → Draw (atk retreats)
            Assert.AreEqual(CombatResult.Draw, result);
            // 眩晕进攻单位撤回基地，依然存活
            Assert.IsTrue(_g.pBase.Contains(atk));
        }

        [Test]
        public void TriggerCombat_Barrier_AbsorbsFirst()
        {
            // 防守方：壁垒单位HP=2 + 普通单位HP=1（进攻方输出4）
            // 壁垒吸收2 → 普通吸收剩余2 → 普通死 → 壁垒剩HP=0死 → Conquer
            var atk  = MakeUnit("atk", 4);
            var defB = MakeUnit("def_barrier", 2, new List<string> { "壁垒" });
            var defN = MakeUnit("def_normal",  1);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(defB);
            _g.bf[0].eU.Add(defN);
            // def total output: 0 (no stunned) = 2+1 = 3, atk HP=4 → atk survives

            var result = _cr.TriggerCombat(1, Owner.Player);

            // 壁垒先吸收 min(4,2)=2 → pool=2; 普通吸收 min(2,1)=1 → pool=1; overflow=1
            // All def dead (defB HP=0, defN HP=0) → Conquer
            Assert.AreEqual(CombatResult.Conquer, result);
        }

        [Test]
        public void TriggerCombat_Trample_OverflowToLegend()
        {
            // 压制：溢出伤害打传奇
            _g.eLeg = new LegendInstance { currentHp = 10, currentAtk = 5,
                                           tb = new TurnBuffs() };
            var atk = MakeUnit("atk", 5, new List<string> { "压制" });
            var def = MakeUnit("def", 2);  // HP=2, atk deals 5 → overflow = 3
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(7, _g.eLeg.currentHp); // 10 − 3 = 7
        }

        [Test]
        public void TriggerCombat_Trample_DefenderSide_OverflowToPlayerLeg()
        {
            // 防守方压制：溢出打玩家传奇
            _g.pLeg = new LegendInstance { currentHp = 8, currentAtk = 5,
                                           tb = new TurnBuffs() };
            var atk = MakeUnit("atk", 1);  // HP=1, def deals 5 → overflow = 4
            var def = MakeUnit("def", 5, new List<string> { "压制" });
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(4, _g.pLeg.currentHp); // 8 − 4 = 4
        }

        // ─────────────────────────────────────────
        // 征服逻辑
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_Conquer_NoDoubleScore_IfAlreadyConqDone()
        {
            _g.bf[0].conqDone = true;   // 本回合已征服
            _g.bf[0].ctrl     = null;   // 控制权为空

            var atk = MakeUnit("atk", 3);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(0, _g.pScore); // conqDone=true，不再加分
        }

        [Test]
        public void TriggerCombat_Conquer_NoScoreIfAlreadyControlled()
        {
            _g.bf[0].ctrl     = Owner.Player; // 已控制
            _g.bf[0].conqDone = false;

            var atk = MakeUnit("atk", 3);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(0, _g.pScore); // 控制权不变，不加分
        }

        [Test]
        public void TriggerCombat_EnemyConquers_EnemyScoreIncrement()
        {
            var atk = MakeUnit("atk", 3);
            var def = MakeUnit("def", 2);
            _g.bf[0].eU.Add(atk);
            _g.bf[0].pU.Add(def);

            _cr.TriggerCombat(1, Owner.Enemy);

            Assert.AreEqual(Owner.Enemy, _g.bf[0].ctrl);
            Assert.AreEqual(1, _g.eScore);
        }

        // ─────────────────────────────────────────
        // HP 重置 / 死亡清理
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_SurvivorHpReset()
        {
            // 进攻方atk=5 vs 防守atk=2; 进攻方受伤(currentHp=5-2=3)
            // 战斗结束后所有存活单位HP重置为currentAtk
            var atk = MakeUnit("atk", 5);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            // atk survived with temp damage → after reset HP = currentAtk = 5
            Assert.AreEqual(5, atk.currentHp);
        }

        [Test]
        public void TriggerCombat_DeadUnitGoesToDiscard()
        {
            var atk = MakeUnit("atk", 3);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.IsTrue(_g.eDiscard.Contains(def));
            Assert.AreEqual(0, _g.bf[0].eU.Count);
        }

        [Test]
        public void TriggerCombat_BothDead_BothInDiscard()
        {
            var atk = MakeUnit("atk", 2);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.IsTrue(_g.pDiscard.Contains(atk));
            Assert.IsTrue(_g.eDiscard.Contains(def));
        }

        [Test]
        public void CleanDead_Equipment_RecalledToBase()
        {
            // 装备误入战场 → 召回基地
            var eq = MakeEquipment("eq");
            _g.bf[0].pU.Add(eq);

            _cr.CleanDead(1);

            Assert.AreEqual(0, _g.bf[0].pU.Count);
            Assert.IsTrue(_g.pBase.Contains(eq));
        }

        [Test]
        public void CleanDead_DeathShield_SavesUnit()
        {
            var shield = MakeUnit("death_shield", 1);
            shield.type   = CardType.Equipment;
            shield.effect = "death_shield";
            var unit = MakeUnit("hero", 3);
            unit.currentHp = 0; // about to die

            _g.pBase.Add(shield);
            _g.bf[0].pU.Add(unit);

            _cr.CleanDead(1);

            // Unit saved to base, shield consumed to discard
            Assert.IsTrue(_g.pBase.Contains(unit));
            Assert.IsTrue(_g.pDiscard.Contains(shield));
            Assert.AreEqual(0, _g.bf[0].pU.Count);
        }

        // ─────────────────────────────────────────
        // 伤害追踪
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_TracksPlayerDmgDealt()
        {
            var atk = MakeUnit("atk", 3);
            var def = MakeUnit("def", 2);
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.AreEqual(3, _g.pAllyDmgDealt);
        }

        [Test]
        public void TriggerCombat_TracksEnemyDmgDealt()
        {
            var atk = MakeUnit("atk", 3);
            var def = MakeUnit("def", 2);
            _g.bf[0].eU.Add(atk);
            _g.bf[0].pU.Add(def);

            _cr.TriggerCombat(1, Owner.Enemy);

            Assert.AreEqual(3, _g.eAllyDmgDealt);
        }

        // ─────────────────────────────────────────
        // 多单位战斗
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_MultiUnit_TotalPower()
        {
            // 进攻方 2+2=4 vs 防守方 3 → 防守死，进攻方HP: atk1=2-3=0死, atk2=2-0=2活
            // Wait: def deals 3 total to atk side. assignDmg distributes:
            // sorted atk by HP ascending: both HP=2, so either first
            // atk1 absorbs min(3,2)=2 → pool=1; atk2 absorbs min(1,2)=1 → pool=0
            // atk1 dies (HP=0), atk2 survives (HP=1) → atkAlive=true
            var atk1 = MakeUnit("atk1", 2);
            var atk2 = MakeUnit("atk2", 2);
            var def  = MakeUnit("def",  3);
            _g.bf[0].pU.Add(atk1);
            _g.bf[0].pU.Add(atk2);
            _g.bf[0].eU.Add(def);

            var result = _cr.TriggerCombat(1, Owner.Player);

            // Both def dead (HP=0), some atk alive → Conquer
            Assert.AreEqual(CombatResult.Conquer, result);
            Assert.AreEqual(1, _g.bf[0].pU.Count); // one survivor on BF
        }

        [Test]
        public void TriggerCombat_InvalidBfId_ReturnsBothDead()
        {
            var result = _cr.TriggerCombat(99, Owner.Player);
            Assert.AreEqual(CombatResult.BothDead, result);
        }

        // ─────────────────────────────────────────
        // 退回基地时携带exhausted标记
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_DrawRetreater_IsExhausted()
        {
            var atk = MakeUnit("atk", 2); atk.currentHp = 4;
            var def = MakeUnit("def", 2); def.currentHp = 4;
            _g.bf[0].pU.Add(atk);
            _g.bf[0].eU.Add(def);

            _cr.TriggerCombat(1, Owner.Player);

            Assert.IsTrue(atk.exhausted);
        }
    }
}
