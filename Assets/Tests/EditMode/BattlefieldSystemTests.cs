using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P9 行为验证测试 — 战场牌效果全集
    /// 对照原版：engine.js doStart、addScore，combat.js postCombatTriggers、
    ///            moveUnit 中散落的 16 张战场牌触发逻辑。
    /// </summary>
    public class BattlefieldSystemTests
    {
        private GameState          _g;
        private TurnManager        _tm;
        private CardDeployer       _cd;
        private CombatResolver     _cr;
        private SpellSystem        _ss;
        private BattlefieldSystem  _bfs;

        [SetUp]
        public void SetUp()
        {
            CardInstance.ResetUidCounter();
            RuneInstance.ResetUidCounter();
            LegendInstance.ResetUidCounter();

            _g   = new GameState();
            _tm  = new TurnManager(_g);
            _cd  = new CardDeployer(_g, _tm);
            _cr  = new CombatResolver(_g, _tm, _cd);
            _ss  = new SpellSystem(_g, _tm, _cd, _cr);
            _bfs = new BattlefieldSystem(_g);

            // 注入 BF 系统
            _tm.SetBattlefieldSystem(_bfs);
            _cr.SetBattlefieldSystem(_bfs);
            _cd.SetBattlefieldSystem(_bfs);
            _ss.SetBattlefieldSystem(_bfs);

            _g.turn  = Owner.Player;
            _g.phase = GamePhase.Action;
        }

        // ─────────────────────────────────────────
        // 辅助工厂
        // ─────────────────────────────────────────

        private static CardInstance MakeUnit(string id, int atk, int cost = 1,
            List<string> keywords = null, CardRegion region = CardRegion.Void)
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
                region     = region,
                tb         = new TurnBuffs(),
                attachedEquipments = new List<CardInstance>()
            };
        }

        private static CardInstance MakeBFCard(string id)
            => new CardInstance { uid = CardInstance.AllocUid(), id = id, cardName = id, type = CardType.Battlefield, tb = new TurnBuffs() };

        private static RuneInstance MakeRune(RuneType t = RuneType.Blazing, bool tapped = false)
        {
            var r = RuneInstance.Create(t);
            r.tapped = tapped;
            return r;
        }

        // ─────────────────────────────────────────
        // 1. altar_unity — DoStart 据守：召唤1名新兵
        // ─────────────────────────────────────────

        [Test]
        public void AltarUnity_OnHold_SpawnsRecruit()
        {
            _g.bf[0].card = MakeBFCard("altar_unity");
            _g.bf[0].ctrl = Owner.Player;

            _tm.DoStart();

            Assert.AreEqual(1, _g.pBase.Count);
            Assert.AreEqual("recruit", _g.pBase[0].id);
            Assert.IsTrue(_g.pBase[0].exhausted);
        }

        [Test]
        public void AltarUnity_OnHold_NoSpawn_WhenBaseFull()
        {
            _g.bf[0].card = MakeBFCard("altar_unity");
            _g.bf[0].ctrl = Owner.Player;
            for (int i = 0; i < 5; i++) _g.pBase.Add(MakeUnit("u" + i, 1));

            _tm.DoStart();

            Assert.AreEqual(5, _g.pBase.Count); // no new recruit
        }

        // ─────────────────────────────────────────
        // 2. aspirant_climb — 支付1法力强化基地单位
        // ─────────────────────────────────────────

        [Test]
        public void AspirantClimb_OnHold_BuffsBaseUnit()
        {
            _g.bf[0].card = MakeBFCard("aspirant_climb");
            _g.bf[0].ctrl = Owner.Player;
            _g.pMana = 3;
            var unit = MakeUnit("a", 2);
            _g.pBase.Add(unit);

            _tm.DoStart();

            Assert.AreEqual(2, _g.pMana);        // 1 mana spent
            Assert.IsTrue(unit.buffToken);
            Assert.AreEqual(3, unit.currentAtk); // +1
        }

        [Test]
        public void AspirantClimb_OnHold_NoEffect_WhenNoMana()
        {
            _g.bf[0].card = MakeBFCard("aspirant_climb");
            _g.bf[0].ctrl = Owner.Player;
            _g.pMana = 0;
            var unit = MakeUnit("a", 2);
            _g.pBase.Add(unit);

            _tm.DoStart();

            Assert.AreEqual(0, _g.pMana);
            Assert.IsFalse(unit.buffToken);
        }

        // ─────────────────────────────────────────
        // 3. bandle_tree — ≥3种地域 → +1法力
        // ─────────────────────────────────────────

        [Test]
        public void BandleTree_OnHold_AddsMana_WhenThreeRegions()
        {
            _g.bf[0].card = MakeBFCard("bandle_tree");
            _g.bf[0].ctrl = Owner.Player;
            _g.pMana = 1;
            _g.pBase.Add(MakeUnit("a", 1, region: CardRegion.Void));
            _g.pBase.Add(MakeUnit("b", 1, region: CardRegion.Noxus));
            _g.pBase.Add(MakeUnit("c", 1, region: CardRegion.Ionia));

            _tm.DoStart();

            // DoStart calls AddScore (+1 score) then OnHold (+1 mana from bandle_tree)
            Assert.AreEqual(2, _g.pMana);
        }

        [Test]
        public void BandleTree_OnHold_NoMana_WhenTwoRegions()
        {
            _g.bf[0].card = MakeBFCard("bandle_tree");
            _g.bf[0].ctrl = Owner.Player;
            _g.pMana = 1;
            _g.pBase.Add(MakeUnit("a", 1, region: CardRegion.Void));
            _g.pBase.Add(MakeUnit("b", 1, region: CardRegion.Noxus));

            _tm.DoStart();

            Assert.AreEqual(1, _g.pMana); // no change
        }

        // ─────────────────────────────────────────
        // 4. strength_obelisk — 据守/征服各+1符文
        // ─────────────────────────────────────────

        [Test]
        public void StrengthObelisk_OnHold_AddsUntappedRune()
        {
            _g.bf[0].card = MakeBFCard("strength_obelisk");
            _g.bf[0].ctrl = Owner.Player;
            _g.pRuneDeck.Add(MakeRune(RuneType.Blazing));

            _bfs.OnHold(_g.bf[0], Owner.Player);

            Assert.AreEqual(1, _g.pRunes.Count);
            Assert.IsFalse(_g.pRunes[0].tapped);
            Assert.AreEqual(0, _g.pRuneDeck.Count);
        }

        [Test]
        public void StrengthObelisk_OnConquer_AddsUntappedRune()
        {
            _g.bf[0].card = MakeBFCard("strength_obelisk");
            _g.pRuneDeck.Add(MakeRune(RuneType.Radiant));

            _bfs.OnConquer(_g.bf[0], Owner.Player);

            Assert.AreEqual(1, _g.pRunes.Count);
            Assert.IsFalse(_g.pRunes[0].tapped);
        }

        // ─────────────────────────────────────────
        // 5. star_peak — 据守：召出1枚休眠符文（tapped）并+1法力
        // ─────────────────────────────────────────

        [Test]
        public void StarPeak_OnHold_AddsTappedRune_AndMana()
        {
            _g.bf[0].card = MakeBFCard("star_peak");
            _g.bf[0].ctrl = Owner.Player;
            _g.pMana = 0;
            _g.pRuneDeck.Add(MakeRune(RuneType.Verdant));
            _bfs.PromptConfirm = () => true;

            _bfs.OnHold(_g.bf[0], Owner.Player);

            Assert.AreEqual(1, _g.pRunes.Count);
            Assert.IsTrue(_g.pRunes[0].tapped);
            Assert.AreEqual(1, _g.pMana);
        }

        [Test]
        public void StarPeak_OnHold_NoEffect_WhenPlayerDeclines()
        {
            _g.bf[0].card = MakeBFCard("star_peak");
            _g.bf[0].ctrl = Owner.Player;
            _g.pRuneDeck.Add(MakeRune());
            _bfs.PromptConfirm = () => false;

            _bfs.OnHold(_g.bf[0], Owner.Player);

            Assert.AreEqual(0, _g.pRunes.Count);
        }

        // ─────────────────────────────────────────
        // 6. ascending_stairs — 据守/征服额外+1分
        // ─────────────────────────────────────────

        [Test]
        public void AscendingStairs_Hold_GivesExtraPoint()
        {
            _g.bf[0].card = MakeBFCard("ascending_stairs");
            _g.bf[0].ctrl = Owner.Player;

            _tm.DoStart(); // hold → +1 base + +1 extra

            Assert.AreEqual(2, _g.pScore);
        }

        [Test]
        public void AscendingStairs_Conquer_GivesExtraPoint()
        {
            _g.bf[0].card = MakeBFCard("ascending_stairs");

            int pts = 1;
            bool result = _bfs.ModifyAddScore(Owner.Player, ref pts, "conquer", 1);

            Assert.IsTrue(result);
            Assert.AreEqual(2, pts);
        }

        // ─────────────────────────────────────────
        // 7. forgotten_monument — 第3回合前阻断据守分
        // ─────────────────────────────────────────

        [Test]
        public void ForgottenMonument_BlocksHold_BeforeRound3()
        {
            _g.bf[0].card = MakeBFCard("forgotten_monument");
            _g.round = 1;

            int pts = 1;
            bool result = _bfs.ModifyAddScore(Owner.Player, ref pts, "hold", 1);

            Assert.IsFalse(result);
        }

        [Test]
        public void ForgottenMonument_AllowsHold_AtRound3()
        {
            _g.bf[0].card = MakeBFCard("forgotten_monument");
            _g.round = 3;

            int pts = 1;
            bool result = _bfs.ModifyAddScore(Owner.Player, ref pts, "hold", 1);

            Assert.IsTrue(result);
        }

        [Test]
        public void ForgottenMonument_AllowsConquer_BeforeRound3()
        {
            _g.bf[0].card = MakeBFCard("forgotten_monument");
            _g.round = 1;

            int pts = 1;
            bool result = _bfs.ModifyAddScore(Owner.Player, ref pts, "conquer", 1);

            Assert.IsTrue(result); // conquer is not blocked
        }

        // ─────────────────────────────────────────
        // 8. reckoner_arena — 战斗开始：atk≥5 获得强攻/坚守
        // ─────────────────────────────────────────

        [Test]
        public void ReckonerArena_OnCombatStart_StrongAttack_ForAttacker()
        {
            _g.bf[0].card = MakeBFCard("reckoner_arena");
            var strong = MakeUnit("s", 5);
            var weak   = MakeUnit("w", 3);
            _g.bf[0].pU.Add(strong);
            _g.bf[0].pU.Add(weak);

            _bfs.OnCombatStart(_g.bf[0], Owner.Player);

            Assert.Contains("强攻", strong.keywords);
            Assert.IsFalse(weak.keywords?.Contains("强攻") == true);
        }

        [Test]
        public void ReckonerArena_OnCombatStart_Guard_ForDefender()
        {
            _g.bf[0].card = MakeBFCard("reckoner_arena");
            var defender = MakeUnit("d", 6);
            _g.bf[0].eU.Add(defender);

            _bfs.OnCombatStart(_g.bf[0], Owner.Player); // player attacks, enemy defends

            Assert.Contains("坚守", defender.keywords);
        }

        // ─────────────────────────────────────────
        // 9. back_alley_bar — 离开此处 → +1 tb.atk 本回合
        // ─────────────────────────────────────────

        [Test]
        public void BackAlleyBar_OnUnitLeaveBF_GivesTempBuff()
        {
            _g.bf[0].card = MakeBFCard("back_alley_bar");
            var unit = MakeUnit("u", 3);
            _g.bf[0].pU.Add(unit);

            _cd.MoveUnit(unit, Owner.Player, "base");

            Assert.AreEqual(1, unit.tb.atk);
            Assert.IsTrue(_g.pBase.Contains(unit));
        }

        // ─────────────────────────────────────────
        // 10. trifarian_warcamp — 进入此处 → buffToken
        // ─────────────────────────────────────────

        [Test]
        public void TrifarianWarcamp_OnUnitEnterBF_GivesBuffToken()
        {
            _g.bf[0].card = MakeBFCard("trifarian_warcamp");
            var unit = MakeUnit("u", 2);
            _g.pBase.Add(unit);

            _cd.MoveUnit(unit, Owner.Player, "1");

            Assert.IsTrue(unit.buffToken);
            Assert.AreEqual(3, unit.currentAtk);
        }

        // ─────────────────────────────────────────
        // 11. hirana — 征服：消耗增益指示物 → 抽1张牌
        // ─────────────────────────────────────────

        [Test]
        public void Hirana_OnConquer_ConsumesBuffToken_DrawsCard()
        {
            _g.bf[0].card = MakeBFCard("hirana");
            var unit = MakeUnit("u", 3);
            unit.buffToken  = true;
            unit.currentAtk = 4; // +1 from buffToken
            _g.pBase.Add(unit);
            _g.pDeck.Add(MakeUnit("drawn", 1));

            _bfs.OnConquer(_g.bf[0], Owner.Player);

            Assert.IsFalse(unit.buffToken);
            Assert.AreEqual(3, unit.currentAtk); // -1
            Assert.AreEqual(1, _g.pHand.Count);
            Assert.AreEqual("drawn", _g.pHand[0].id);
        }

        // ─────────────────────────────────────────
        // 12. reaver_row — 征服：召回废牌堆费用≤2单位
        // ─────────────────────────────────────────

        [Test]
        public void ReaverRow_OnConquer_RecallsUnitFromDiscard()
        {
            _g.bf[0].card = MakeBFCard("reaver_row");
            var cheap = MakeUnit("cheap", 2, cost: 2);
            _g.pDiscard.Add(cheap);

            _bfs.OnConquer(_g.bf[0], Owner.Player);

            Assert.AreEqual(0, _g.pDiscard.Count);
            Assert.AreEqual(1, _g.pBase.Count);
            Assert.IsTrue(_g.pBase[0].exhausted);
        }

        [Test]
        public void ReaverRow_OnConquer_IgnoresExpensiveUnits()
        {
            _g.bf[0].card = MakeBFCard("reaver_row");
            var expensive = MakeUnit("exp", 3, cost: 3);
            _g.pDiscard.Add(expensive);
            // PromptDiscardUnit returns first of valid list; valid list is empty here
            _bfs.PromptDiscardUnit = list => list.Count > 0 ? list[0] : null;

            _bfs.OnConquer(_g.bf[0], Owner.Player);

            Assert.AreEqual(1, _g.pDiscard.Count); // still in discard
        }

        // ─────────────────────────────────────────
        // 13. zaun_undercity — 征服：弃1手牌 → 抽1张牌
        // ─────────────────────────────────────────

        [Test]
        public void ZaunUndercity_OnConquer_DiscardsAndDraws()
        {
            _g.bf[0].card = MakeBFCard("zaun_undercity");
            var handCard = MakeUnit("hand", 1);
            _g.pHand.Add(handCard);
            _g.pDeck.Add(MakeUnit("deck", 2));

            _bfs.OnConquer(_g.bf[0], Owner.Player);

            Assert.AreEqual(1, _g.pHand.Count);
            Assert.AreEqual("deck", _g.pHand[0].id);
            Assert.AreEqual(1, _g.pDiscard.Count);
            Assert.AreEqual("hand", _g.pDiscard[0].id);
        }

        // ─────────────────────────────────────────
        // 14. thunder_rune — 征服：回收1枚已点击符文
        // ─────────────────────────────────────────

        [Test]
        public void ThunderRune_OnConquer_RecyclesTappedRune()
        {
            _g.bf[0].card = MakeBFCard("thunder_rune");
            var tapped = MakeRune(RuneType.Blazing, tapped: true);
            _g.pRunes.Add(tapped);

            _bfs.OnConquer(_g.bf[0], Owner.Player);

            Assert.AreEqual(0, _g.pRunes.Count);
            Assert.AreEqual(1, _g.pRuneDeck.Count);
            Assert.IsFalse(_g.pRuneDeck[0].tapped); // reset
        }

        [Test]
        public void ThunderRune_OnConquer_NoEffect_WhenNoTappedRunes()
        {
            _g.bf[0].card = MakeBFCard("thunder_rune");
            var untapped = MakeRune(RuneType.Blazing, tapped: false);
            _g.pRunes.Add(untapped);

            _bfs.OnConquer(_g.bf[0], Owner.Player);

            Assert.AreEqual(1, _g.pRunes.Count); // no change
            Assert.AreEqual(0, _g.pRuneDeck.Count);
        }

        // ─────────────────────────────────────────
        // 15. sunken_temple — 防守失败：支付2法力 → 抽1张牌
        // ─────────────────────────────────────────

        [Test]
        public void SunkenTemple_OnDefenseFailure_PayMana_DrawCard()
        {
            _g.bf[0].card = MakeBFCard("sunken_temple");
            _g.pMana = 3;
            _g.pDeck.Add(MakeUnit("d", 1));
            _bfs.PromptConfirm = () => true;

            _bfs.OnDefenseFailure(_g.bf[0], Owner.Player);

            Assert.AreEqual(1, _g.pMana);  // 3 - 2
            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void SunkenTemple_OnDefenseFailure_NoEffect_WhenNotEnoughMana()
        {
            _g.bf[0].card = MakeBFCard("sunken_temple");
            _g.pMana = 1;
            _g.pDeck.Add(MakeUnit("d", 1));

            _bfs.OnDefenseFailure(_g.bf[0], Owner.Player);

            Assert.AreEqual(0, _g.pHand.Count);
        }

        // ─────────────────────────────────────────
        // 16. dreaming_tree — 法术目标为此处盟友 → 抽1张牌
        // ─────────────────────────────────────────

        [Test]
        public void DreamingTree_OnSpellTargetAlly_DrawsCard()
        {
            _g.bf[0].card = MakeBFCard("dreaming_tree");
            var ally = MakeUnit("ally", 3);
            _g.bf[0].pU.Add(ally);
            _g.pDeck.Add(MakeUnit("d", 1));

            _bfs.OnSpellTargetAlly(ally, Owner.Player);

            Assert.AreEqual(1, _g.pHand.Count);
        }

        [Test]
        public void DreamingTree_NoEffect_WhenTargetNotInBf()
        {
            _g.bf[0].card = MakeBFCard("dreaming_tree");
            var baseUnit = MakeUnit("base", 3);
            _g.pBase.Add(baseUnit);
            _g.pDeck.Add(MakeUnit("d", 1));

            _bfs.OnSpellTargetAlly(baseUnit, Owner.Player);

            Assert.AreEqual(0, _g.pHand.Count); // base units aren't in bf.pU
        }

        // ─────────────────────────────────────────
        // 17. void_gate — 法术/技能伤害+1
        // ─────────────────────────────────────────

        [Test]
        public void VoidGate_ModifySpellDamage_AddsPlusOne()
        {
            _g.bf[0].card = MakeBFCard("void_gate");
            var target = MakeUnit("t", 5);
            _g.bf[0].eU.Add(target);

            int result = _bfs.ModifySpellDamage(target, 3);

            Assert.AreEqual(4, result);
        }

        [Test]
        public void VoidGate_NoEffect_WhenTargetNotInBf()
        {
            _g.bf[0].card = MakeBFCard("void_gate");
            var baseUnit = MakeUnit("t", 5);
            _g.eBase.Add(baseUnit);

            int result = _bfs.ModifySpellDamage(baseUnit, 3);

            Assert.AreEqual(3, result);
        }

        [Test]
        public void VoidGate_Integration_SpellDealsExtraDamage()
        {
            _g.bf[0].card = MakeBFCard("void_gate");
            var target = MakeUnit("t", 6);
            _g.bf[0].eU.Add(target);
            _g.pMana = 3;

            // deal3 spell targeting the bf unit
            var spell = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "s", cardName = "deal3",
                type = CardType.Spell, cost = 3, effect = "deal3",
                keywords = new List<string>(), tb = new TurnBuffs(),
                attachedEquipments = new List<CardInstance>()
            };
            _g.pHand.Add(spell);
            _ss.PromptTarget = list => list.FirstOrDefault(u => u.id == "t");

            _ss.ApplySpell(spell, Owner.Player, target.uid);

            // 3 + 1 (void_gate) = 4 damage; target has 6 hp → survives with 2
            Assert.AreEqual(2, target.currentHp);
        }

        // ─────────────────────────────────────────
        // 18. rockfall_path — 禁止手牌直接出牌到此战场
        // ─────────────────────────────────────────

        [Test]
        public void RockfallPath_CanDeployToBF_ReturnsFalse()
        {
            _g.bf[0].card = MakeBFCard("rockfall_path");
            Assert.IsFalse(_bfs.CanDeployToBF(1, Owner.Player));
        }

        [Test]
        public void RockfallPath_CanDeployToBF_ReturnsTrue_ForOtherBF()
        {
            _g.bf[0].card = MakeBFCard("rockfall_path");
            _g.bf[1].card = null;
            Assert.IsTrue(_bfs.CanDeployToBF(2, Owner.Player));
        }

        [Test]
        public void RockfallPath_DeployToBF_Blocked_ReturnsNull()
        {
            _g.bf[0].card = MakeBFCard("rockfall_path");
            var unit = MakeUnit("u", 2, cost: 1);
            _g.pHand.Add(unit);
            _g.pMana = 3;

            var result = _cd.DeployToBF(unit, Owner.Player, 1);

            Assert.IsNull(result);
            Assert.AreEqual(1, _g.pHand.Count); // still in hand
        }

        // ─────────────────────────────────────────
        // 19. vile_throat_nest — 禁止单位移回基地
        // ─────────────────────────────────────────

        [Test]
        public void VileThroatNest_CanMoveToBase_ReturnsFalse()
        {
            _g.bf[0].card = MakeBFCard("vile_throat_nest");
            var unit = MakeUnit("u", 3);
            _g.bf[0].pU.Add(unit);

            Assert.IsFalse(_bfs.CanMoveToBase(unit, Owner.Player));
        }

        [Test]
        public void VileThroatNest_MoveUnit_Blocked()
        {
            _g.bf[0].card = MakeBFCard("vile_throat_nest");
            var unit = MakeUnit("u", 3);
            _g.bf[0].pU.Add(unit);

            _cd.MoveUnit(unit, Owner.Player, "base");

            Assert.AreEqual(0, _g.pBase.Count);   // not moved to base
            Assert.AreEqual(1, _g.bf[0].pU.Count); // still in bf
        }

        // ─────────────────────────────────────────
        // 20. 缇亚娜·冕卫 — 对手本回合不得获据守分
        // ─────────────────────────────────────────

        [Test]
        public void Tiyana_BlocksOpponentHoldScore()
        {
            // 缇亚娜在玩家场上 → 阻断 enemy 的据守分
            var tiyana = MakeUnit("tiyana", 3);
            tiyana.effect = "tiyana_enter";
            _g.pBase.Add(tiyana);

            bool blocked = _bfs.IsTiyanaBlockingHold(Owner.Enemy);
            Assert.IsTrue(blocked);
        }

        [Test]
        public void Tiyana_DoesNotBlock_OwnHoldScore()
        {
            var tiyana = MakeUnit("tiyana", 3);
            tiyana.effect = "tiyana_enter";
            _g.pBase.Add(tiyana);

            bool blocked = _bfs.IsTiyanaBlockingHold(Owner.Player);
            Assert.IsFalse(blocked); // tiyana blocks opponent, not self
        }

        [Test]
        public void Tiyana_Integration_EnemyHold_Blocked()
        {
            _g.bf[0].ctrl = Owner.Enemy;
            _g.turn       = Owner.Enemy;

            var tiyana = MakeUnit("tiyana", 3);
            tiyana.effect = "tiyana_enter";
            _g.pBase.Add(tiyana); // player side tiyana

            _tm.DoStart(); // enemy holds bf[0] but tiyana blocks it

            Assert.AreEqual(0, _g.eScore); // blocked
        }

        // ─────────────────────────────────────────
        // 21. 集成测试：TriggerCombat 征服触发 reaver_row
        // ─────────────────────────────────────────

        [Test]
        public void TriggerCombat_Conquer_TriggersReaverRow()
        {
            _g.bf[0].card = MakeBFCard("reaver_row");
            _g.bf[0].ctrl = Owner.Enemy;
            var attacker = MakeUnit("a", 4);
            var defender = MakeUnit("d", 2);
            _g.bf[0].pU.Add(attacker);
            _g.bf[0].eU.Add(defender);
            var cheap = MakeUnit("cheap", 1, cost: 2);
            _g.pDiscard.Add(cheap);

            _cr.TriggerCombat(1, Owner.Player);

            // defender dies (atk 2 < 4), player conquers → reaver_row triggers
            Assert.AreEqual(0, _g.pDiscard.Count);
            Assert.AreEqual(1, _g.pBase.Count(u => u.id == "cheap"));
        }

        // ─────────────────────────────────────────
        // 22. 集成测试：dreaming_tree via SpellSystem.ApplySpell
        // ─────────────────────────────────────────

        [Test]
        public void DreamingTree_Integration_SpellOnBfAlly_DrawsCard()
        {
            _g.bf[0].card = MakeBFCard("dreaming_tree");
            var ally = MakeUnit("ally", 2);
            _g.bf[0].pU.Add(ally);
            _g.pDeck.Add(MakeUnit("d", 1));
            _g.pMana = 3;

            var buffSpell = new CardInstance
            {
                uid = CardInstance.AllocUid(), id = "buff_ally_spell", cardName = "buff",
                type = CardType.Spell, cost = 0, effect = "buff_ally",
                keywords = new List<string>(), tb = new TurnBuffs(),
                attachedEquipments = new List<CardInstance>()
            };
            _ss.PromptTarget = list => list.FirstOrDefault(u => u.id == "ally");

            _ss.ApplySpell(buffSpell, Owner.Player, ally.uid);

            Assert.AreEqual(1, _g.pHand.Count); // drew a card
        }
    }
}
