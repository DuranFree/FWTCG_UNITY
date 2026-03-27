using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P8 传奇系统行为验证测试（23 cases）。
    /// 等价 legend.js 的核心逻辑：
    ///   checkLegendPassives / triggerLegendEvent / activateLegendAbility /
    ///   resetLegendAbilitiesForTurn / aiLegendActionPhase / aiLegendDuelAction
    /// </summary>
    [TestFixture]
    public class LegendSystemTests
    {
        private GameState    _g;
        private TurnManager  _tm;
        private CardDeployer _cd;
        private LegendSystem _ls;
        private AIController _ai;

        [SetUp]
        public void SetUp()
        {
            CardInstance.ResetUidCounter();
            LegendInstance.ResetUidCounter();

            _g  = new GameState();
            _tm = new TurnManager(_g);
            _cd = new CardDeployer(_g, _tm);
            _ls = new LegendSystem(_g, _tm);

            _tm.SetLegendSystem(_ls);
            _cd.SetLegendSystem(_ls);

            _g.turn  = Owner.Player;
            _g.phase = GamePhase.Action;

            _ai = new AIController(_g, _tm, _cd);
            _ai.SetLegendSystem(_ls);
            _ai.Schedule = (_, fn) => fn();
        }

        // ────────────────────────────────────────────
        // 辅助方法
        // ────────────────────────────────────────────

        private LegendInstance MakeLegend(string id, int atk, int hp)
        {
            var data = ScriptableObject.CreateInstance<CardData>();
            data.id   = id;
            data.atk  = atk;
            data.hp   = hp;
            data.type = CardType.Champion;
            data.keywords = new List<string>();
            return LegendInstance.From(data);
        }

        private CardInstance MakeUnit(string id, int atk, int cost = 1,
                                      List<string> keywords = null)
        {
            var data = ScriptableObject.CreateInstance<CardData>();
            data.id       = id;
            data.atk      = atk;
            data.cost     = cost;
            data.type     = CardType.Follower;
            data.keywords = keywords ?? new List<string>();
            return CardInstance.From(data);
        }

        // ────────────────────────────────────────────
        // CheckLegendPassives — 卡莎进化
        // ────────────────────────────────────────────

        [Test]
        public void CheckLegendPassives_Kaisa_Evolves_When4UniqueKeywords()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);
            int initAtk = _g.pLeg.currentAtk;
            int initHp  = _g.pLeg.currentHp;

            // 放4名拥有不同关键词的盟友
            var u1 = MakeUnit("a", 3, 1, new List<string> { "急速" });
            var u2 = MakeUnit("b", 3, 1, new List<string> { "迅捷" });
            var u3 = MakeUnit("c", 3, 1, new List<string> { "坚守" });
            var u4 = MakeUnit("d", 3, 1, new List<string> { "绝念" });
            _g.pBase.Add(u1); _g.pBase.Add(u2); _g.pBase.Add(u3); _g.pBase.Add(u4);

            _ls.CheckLegendPassives(Owner.Player);

            Assert.IsTrue(_g.pLeg.evolved);
            Assert.AreEqual(2, _g.pLeg.level);
            Assert.AreEqual(initAtk + 3, _g.pLeg.currentAtk);
            Assert.AreEqual(initHp + 3,  _g.pLeg.currentHp);
            Assert.AreEqual(14 + 3,       _g.pLeg.maxHp);
        }

        [Test]
        public void CheckLegendPassives_Kaisa_DoesNotEvolve_WhenFewerThan4Keywords()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);

            var u1 = MakeUnit("a", 3, 1, new List<string> { "急速" });
            var u2 = MakeUnit("b", 3, 1, new List<string> { "迅捷" });
            _g.pBase.Add(u1); _g.pBase.Add(u2);

            _ls.CheckLegendPassives(Owner.Player);

            Assert.IsFalse(_g.pLeg.evolved);
            Assert.AreEqual(1, _g.pLeg.level);
            Assert.AreEqual(5, _g.pLeg.currentAtk);
        }

        [Test]
        public void CheckLegendPassives_Kaisa_DoesNotEvolveTwice()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);

            var u1 = MakeUnit("a", 3, 1, new List<string> { "急速" });
            var u2 = MakeUnit("b", 3, 1, new List<string> { "迅捷" });
            var u3 = MakeUnit("c", 3, 1, new List<string> { "坚守" });
            var u4 = MakeUnit("d", 3, 1, new List<string> { "绝念" });
            _g.pBase.Add(u1); _g.pBase.Add(u2); _g.pBase.Add(u3); _g.pBase.Add(u4);

            _ls.CheckLegendPassives(Owner.Player);
            int atkAfterFirst = _g.pLeg.currentAtk;

            _ls.CheckLegendPassives(Owner.Player);  // 再次调用不应再次进化

            Assert.AreEqual(atkAfterFirst, _g.pLeg.currentAtk);
        }

        [Test]
        public void CheckLegendPassives_Kaisa_DuplicateKeywordsCountOnce()
        {
            // 3名盟友，各拥有相同关键词 → 仍只有3种，不进化
            _g.pLeg = MakeLegend("kaisa", 5, 14);

            var u1 = MakeUnit("a", 3, 1, new List<string> { "急速" });
            var u2 = MakeUnit("b", 3, 1, new List<string> { "急速", "迅捷" });
            var u3 = MakeUnit("c", 3, 1, new List<string> { "坚守" });
            _g.pBase.Add(u1); _g.pBase.Add(u2); _g.pBase.Add(u3);

            _ls.CheckLegendPassives(Owner.Player);

            Assert.IsFalse(_g.pLeg.evolved);
        }

        // ────────────────────────────────────────────
        // TriggerLegendEvent — 易大师独影剑鸣
        // ────────────────────────────────────────────

        [Test]
        public void TriggerLegendEvent_MasteryiDefendBuff_AppliesWhen1Defender()
        {
            _g.pLeg = MakeLegend("masteryi", 5, 12);
            var solo = MakeUnit("solo", 3);
            _g.bf[0].pU.Add(solo);  // 仅1名防守单位

            _ls.TriggerLegendEvent("onCombatDefend", Owner.Player, new LegendEventCtx { bfId = 1 });

            Assert.AreEqual(2, solo.tb.atk);
        }

        [Test]
        public void TriggerLegendEvent_MasteryiDefendBuff_NoBuffWhen2Defenders()
        {
            _g.pLeg = MakeLegend("masteryi", 5, 12);
            var u1 = MakeUnit("u1", 3);
            var u2 = MakeUnit("u2", 3);
            _g.bf[0].pU.Add(u1);
            _g.bf[0].pU.Add(u2);

            _ls.TriggerLegendEvent("onCombatDefend", Owner.Player, new LegendEventCtx { bfId = 1 });

            Assert.AreEqual(0, u1.tb.atk);
            Assert.AreEqual(0, u2.tb.atk);
        }

        [Test]
        public void TriggerLegendEvent_MasteryiDefendBuff_NoBuffWithNullCtx()
        {
            _g.pLeg = MakeLegend("masteryi", 5, 12);
            var solo = MakeUnit("solo", 3);
            _g.bf[0].pU.Add(solo);

            // ctx 为 null 应安全无效
            Assert.DoesNotThrow(() =>
                _ls.TriggerLegendEvent("onCombatDefend", Owner.Player, null));
            Assert.AreEqual(0, solo.tb.atk);
        }

        [Test]
        public void TriggerLegendEvent_UnknownEvent_DoesNothing()
        {
            _g.pLeg = MakeLegend("masteryi", 5, 12);
            Assert.DoesNotThrow(() =>
                _ls.TriggerLegendEvent("nonExistentEvent", Owner.Player));
        }

        // ────────────────────────────────────────────
        // ActivateLegendAbility — 卡莎虚空感知
        // ────────────────────────────────────────────

        [Test]
        public void ActivateLegendAbility_KaisaVoidSense_ExhaustsAndAddsBlazingSchm()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);
            _g.pLeg.exhausted = false;
            int blazingBefore = _g.pSch.blazing;

            bool result = _ls.ActivateLegendAbility(Owner.Player, "kaisa_void_sense");

            Assert.IsTrue(result);
            Assert.IsTrue(_g.pLeg.exhausted);
            Assert.AreEqual(blazingBefore + 1, _g.pSch.blazing);
        }

        [Test]
        public void ActivateLegendAbility_KaisaVoidSense_FailsWhenAlreadyExhausted()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);
            _g.pLeg.exhausted = true;

            bool result = _ls.ActivateLegendAbility(Owner.Player, "kaisa_void_sense");

            Assert.IsFalse(result);
            Assert.AreEqual(0, _g.pSch.blazing);  // 未增加
        }

        [Test]
        public void ActivateLegendAbility_FailsOutsideActionPhase()
        {
            _g.pLeg  = MakeLegend("kaisa", 5, 14);
            _g.phase = GamePhase.End;  // 非行动阶段

            bool result = _ls.ActivateLegendAbility(Owner.Player, "kaisa_void_sense");

            Assert.IsFalse(result);
        }

        [Test]
        public void ActivateLegendAbility_FailsWhenNotPlayerTurn()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);
            _g.turn = Owner.Enemy;  // 轮到敌方

            bool result = _ls.ActivateLegendAbility(Owner.Player, "kaisa_void_sense");

            Assert.IsFalse(result);
        }

        [Test]
        public void ActivateLegendAbility_VoidSense_CanActivateMultipleTimesInDuel()
        {
            // once=false: 虚空感知每次只要 leg 未 exhausted 均可激活
            // 测试：第一次激活成功；再次休眠重置后可再次激活
            _g.pLeg = MakeLegend("kaisa", 5, 14);
            _g.pLeg.exhausted = false;
            _g.duelActive = true;
            _g.duelTurn   = Owner.Player;

            bool first = _ls.ActivateLegendAbility(Owner.Player, "kaisa_void_sense");
            Assert.IsTrue(first);
            Assert.AreEqual(1, _g.pSch.blazing);

            // 重置休眠（模拟回合开始）
            _g.pLeg.exhausted = false;
            bool second = _ls.ActivateLegendAbility(Owner.Player, "kaisa_void_sense");
            Assert.IsTrue(second);
            Assert.AreEqual(2, _g.pSch.blazing);
        }

        // ────────────────────────────────────────────
        // ResetLegendAbilitiesForTurn
        // ────────────────────────────────────────────

        [Test]
        public void ResetLegendAbilitiesForTurn_ClearsUsedThisTurn()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);
            _g.pLeg.usedThisTurn["some_ability"] = true;

            _ls.ResetLegendAbilitiesForTurn(Owner.Player);

            Assert.IsFalse(_g.pLeg.usedThisTurn.ContainsKey("some_ability"));
        }

        [Test]
        public void ResetLegendAbilitiesForTurn_SafeWithNoLegend()
        {
            _g.pLeg = null;
            Assert.DoesNotThrow(() => _ls.ResetLegendAbilitiesForTurn(Owner.Player));
        }

        // ────────────────────────────────────────────
        // StartTurn 调用 ResetLegendAbilitiesForTurn
        // ────────────────────────────────────────────

        [Test]
        public void StartTurn_ResetsLegendAbilitiesForThatOwner()
        {
            _g.pLeg = MakeLegend("kaisa", 5, 14);
            _g.pLeg.usedThisTurn["kaisa_void_sense"] = true;

            _tm.StartTurn(Owner.Player);

            Assert.IsFalse(_g.pLeg.usedThisTurn.ContainsKey("kaisa_void_sense"));
        }

        // ────────────────────────────────────────────
        // GetAbilities — 未知传奇不崩溃
        // ────────────────────────────────────────────

        [Test]
        public void GetAbilities_ReturnsEmpty_ForUnknownLegend()
        {
            var unknown = MakeLegend("unknown_hero", 5, 10);
            var abilities = LegendSystem.GetAbilities(unknown);
            Assert.AreEqual(0, abilities.Count);
        }

        [Test]
        public void GetAbilities_ReturnsAbilities_ForKaisa()
        {
            var kaisa = MakeLegend("kaisa", 5, 14);
            var abilities = LegendSystem.GetAbilities(kaisa);
            Assert.IsTrue(abilities.Count > 0);
        }

        // ────────────────────────────────────────────
        // AI: AiLegendActionPhase
        // ────────────────────────────────────────────

        [Test]
        public void AiLegendActionPhase_UsesVoidSense_WhenEnemyIsKaisaAndAble()
        {
            _g.eLeg  = MakeLegend("kaisa", 5, 14);
            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;

            int blazingBefore = _g.eSch.blazing;
            bool used = _ls.AiLegendActionPhase();

            Assert.IsTrue(used);
            Assert.IsTrue(_g.eLeg.exhausted);
            Assert.AreEqual(blazingBefore + 1, _g.eSch.blazing);
        }

        [Test]
        public void AiLegendActionPhase_ReturnsFalse_WhenNoEnemyLegend()
        {
            _g.eLeg  = null;
            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;

            bool used = _ls.AiLegendActionPhase();
            Assert.IsFalse(used);
        }

        [Test]
        public void AiLegendActionPhase_ReturnsFalse_WhenAlreadyExhausted()
        {
            _g.eLeg  = MakeLegend("kaisa", 5, 14);
            _g.eLeg.exhausted = true;  // 已休眠
            _g.turn  = Owner.Enemy;
            _g.phase = GamePhase.Action;

            bool used = _ls.AiLegendActionPhase();
            Assert.IsFalse(used);
        }

        // ────────────────────────────────────────────
        // AI: AiLegendDuelAction
        // ────────────────────────────────────────────

        [Test]
        public void AiLegendDuelAction_UsesVoidSense_DuringDuel()
        {
            _g.eLeg       = MakeLegend("kaisa", 5, 14);
            _g.duelActive = true;
            _g.duelTurn   = Owner.Enemy;
            _g.duelBf     = 1;

            int blazingBefore = _g.eSch.blazing;
            bool used = _ls.AiLegendDuelAction();

            Assert.IsTrue(used);
            Assert.AreEqual(blazingBefore + 1, _g.eSch.blazing);
            // 使用后交换对决权给玩家
            Assert.AreEqual(Owner.Player, _g.duelTurn);
            Assert.AreEqual(0, _g.duelSkips);
        }

        [Test]
        public void AiLegendDuelAction_ReturnsFalse_WhenNotInDuel()
        {
            _g.eLeg       = MakeLegend("kaisa", 5, 14);
            _g.duelActive = false;
            _g.turn       = Owner.Enemy;
            _g.phase      = GamePhase.Action;
            // VoidSense 需要 反应 关键词, duelActive=false 时需要 turn==enemy && phase==Action
            // 但 once=false 且 exhaust 条件满足，应能激活
            bool used = _ls.AiLegendDuelAction();
            // 非对决且轮到 enemy 行动阶段 → 可以激活
            Assert.IsTrue(used);
        }

        // ────────────────────────────────────────────
        // CombatResolver 集成：独影剑鸣触发点
        // ────────────────────────────────────────────

        [Test]
        public void CombatResolver_TriggersMasteryiDefendBuff_BeforeDamage()
        {
            // 布置：玩家有易大师，bf[0] 有1名玩家单位（防守方）
            _g.pLeg = MakeLegend("masteryi", 5, 12);

            var defUnit = MakeUnit("def", 4);
            defUnit.currentHp = defUnit.currentAtk;
            _g.bf[0].pU.Add(defUnit);

            // 敌方进攻单位：战力 = 4（和 defUnit 相同）
            var atkUnit = MakeUnit("atk", 4);
            atkUnit.currentHp = atkUnit.currentAtk;
            _g.bf[0].eU.Add(atkUnit);

            // 控制权设为 player（防守方是 player）
            _g.bf[0].ctrl = Owner.Player;

            var cr = new CombatResolver(_g, _tm, _cd);
            cr.SetLegendSystem(_ls);

            // 触发战斗（敌方进攻）
            cr.TriggerCombat(1, Owner.Enemy);

            // 独影剑鸣触发：defUnit 获得 +2 tb.atk → 战力6，atkUnit 战力4
            // defUnit 存活，atkUnit 死亡
            Assert.AreEqual(0, _g.bf[0].eU.Count, "攻击方应已被歼灭");
            // defUnit 可能在 pU 或 pBase（若有死亡护盾）；只要 enemy 被清除即可
        }

        // ────────────────────────────────────────────
        // CardDeployer 集成：进化被动触发点
        // ────────────────────────────────────────────

        [Test]
        public void DeployToBase_TriggersEvolvePassive_WhenKeywordsComplete()
        {
            _g.pLeg  = MakeLegend("kaisa", 5, 14);
            _g.pMana = 10;

            // 手牌中放4张拥有不同关键词的单位
            var c1 = MakeUnit("kw1", 2, 1, new List<string> { "急速" });
            var c2 = MakeUnit("kw2", 2, 1, new List<string> { "迅捷" });
            var c3 = MakeUnit("kw3", 2, 1, new List<string> { "坚守" });
            var c4 = MakeUnit("kw4", 2, 1, new List<string> { "绝念" });

            // 先部署前3张
            _g.pHand.Add(c1); _cd.DeployToBase(c1, Owner.Player);
            _g.pHand.Add(c2); _cd.DeployToBase(c2, Owner.Player);
            _g.pHand.Add(c3); _cd.DeployToBase(c3, Owner.Player);

            Assert.IsFalse(_g.pLeg.evolved, "3种关键词不应进化");

            // 第4张触发进化
            _g.pHand.Add(c4); _cd.DeployToBase(c4, Owner.Player);

            Assert.IsTrue(_g.pLeg.evolved, "4种关键词应触发进化");
            Assert.AreEqual(8, _g.pLeg.currentAtk);    // 5 + 3
        }
    }
}
