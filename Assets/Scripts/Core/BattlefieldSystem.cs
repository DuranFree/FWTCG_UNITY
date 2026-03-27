using System;
using System.Collections.Generic;
using System.Linq;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 战场牌效果系统（P9）。
    /// 集中实现全部 16 张战场牌的触发逻辑，等价原版 engine.js doStart、
    /// addScore、combat.js triggerCombat / postCombatTriggers 中散落的战场牌分发。
    ///
    /// 触发点由外部调用：
    ///   TurnManager.DoStart    → OnHold
    ///   TurnManager.AddScore   → ModifyAddScore
    ///   CombatResolver         → OnCombatStart / OnConquer / OnDefenseFailure
    ///   CardDeployer.MoveUnit  → OnUnitEnterBF / OnUnitLeaveBF
    ///   CardDeployer.DeployToBF → CanDeployToBF
    ///   CardDeployer.MoveUnit  → CanMoveToBase
    ///   SpellSystem.ApplySpell → OnSpellTargetAlly / ModifySpellDamage
    /// </summary>
    public class BattlefieldSystem
    {
        public readonly GameState G;

        // ── Prompt 委托（UI 层覆写；逻辑层默认自动选择）──
        /// <summary>从基地单位列表中选取目标（aspirant_climb 用）。默认选第一个。</summary>
        public Func<List<CardInstance>, CardInstance> PromptBaseUnit
            = list => list.FirstOrDefault();

        /// <summary>从废牌堆选取费用≤2的单位（reaver_row 用）。默认选第一个。</summary>
        public Func<List<CardInstance>, CardInstance> PromptDiscardUnit
            = list => list.FirstOrDefault();

        /// <summary>从手牌选取弃置目标（zaun_undercity 用）。默认选第一个。</summary>
        public Func<List<CardInstance>, CardInstance> PromptHandCard
            = list => list.FirstOrDefault();

        /// <summary>从已点击（tapped）符文选取回收目标（thunder_rune 用）。默认选第一个。</summary>
        public Func<List<RuneInstance>, RuneInstance> PromptTappedRune
            = list => list.FirstOrDefault();

        /// <summary>通用确认弹窗（star_peak / sunken_temple 用）。默认返回 true。</summary>
        public Func<bool> PromptConfirm = () => true;

        public BattlefieldSystem(GameState g) { G = g; }

        // ────────────────────────────────────────────
        // OnHold — DoStart 据守阶段（每个己方控制战场调用一次）
        // ────────────────────────────────────────────

        /// <summary>
        /// 处理据守时战场牌被动效果。
        /// 等价原版 engine.js doStart 中按 bf.card.id 分发的各 case。
        /// 注意：只对 owner（当前回合方）侧生效，与原版一致。
        /// </summary>
        public void OnHold(BattlefieldState bf, Owner owner)
        {
            if (bf?.card == null) return;

            var hand     = G.GetHand(owner);
            var deck     = G.GetDeck(owner);
            var baseZone = G.GetBase(owner);
            var runeDeck = G.GetRuneDeck(owner);
            var runes    = G.GetRunes(owner);

            switch (bf.card.id)
            {
                // 团结祭坛：召唤1名新兵（exhausted）到基地
                case "altar_unity":
                {
                    if (baseZone.Count < 5)
                    {
                        var recruit = new CardInstance
                        {
                            uid         = CardInstance.AllocUid(),
                            id          = "recruit",
                            cardName    = "新兵",
                            type        = CardType.Follower,
                            cost        = 1,
                            atk         = 1,
                            currentAtk  = 1,
                            currentHp   = 1,
                            exhausted   = true,
                            keywords    = new List<string>(),
                            tb          = new TurnBuffs(),
                            attachedEquipments = new List<CardInstance>()
                        };
                        baseZone.Add(recruit);
                    }
                    break;
                }

                // 试炼者之阶：支付1法力 → 基地一名单位获得 buffToken（+1战力）
                case "aspirant_climb":
                {
                    int mana = G.GetMana(owner);
                    var targets = baseZone.Where(u => u.type != CardType.Equipment).ToList();
                    if (mana >= 1 && targets.Count > 0)
                    {
                        var target = PromptBaseUnit(targets);
                        if (target != null)
                        {
                            G.SetMana(owner, mana - 1);
                            ApplyBuffToken(target);
                        }
                    }
                    break;
                }

                // 班德尔城神树：场上≥3种不同地域 → +1法力
                case "bandle_tree":
                {
                    var regions = new HashSet<CardRegion>();
                    var allAllies = GetAllUnitsForOwner(owner);
                    foreach (var u in allAllies)
                        regions.Add(u.region);
                    if (regions.Count >= 3)
                        G.SetMana(owner, G.GetMana(owner) + 1);
                    break;
                }

                // 力量方尖碑（据守）：从符文牌堆获得1张符文（未翻转）
                case "strength_obelisk":
                {
                    if (runeDeck.Count > 0)
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        // 据守获得的符文处于未点击状态（玩家可手动点击）
                        r.tapped = false;
                        runes.Add(r);
                    }
                    break;
                }

                // 星尖峰（据守）：PromptConfirm → 召出1枚休眠符文（已点击，即立即可用法力）
                case "star_peak":
                {
                    if (runeDeck.Count > 0 && PromptConfirm())
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        r.tapped = true;
                        runes.Add(r);
                        // tapped 符文立即提供法力（等价原版行为）
                        G.SetMana(owner, G.GetMana(owner) + 1);
                    }
                    break;
                }
            }
        }

        // ────────────────────────────────────────────
        // ModifyAddScore — TurnManager.AddScore 调用
        // ────────────────────────────────────────────

        /// <summary>
        /// 在得分发放前由 AddScore 调用。
        /// 返回 false 表示阻断本次得分；否则可修改 pts。
        /// 等价原版 addScore 函数中的前置守卫。
        /// </summary>
        public bool ModifyAddScore(Owner who, ref int pts, string type, int? bfId)
        {
            if (!bfId.HasValue) return true;
            int id = bfId.Value;
            if (id < 1 || id > G.bf.Length) return true;
            var bf = G.bf[id - 1];
            if (bf?.card == null) return true;

            switch (bf.card.id)
            {
                // 攀圣长阶：据守/征服时额外+1分
                case "ascending_stairs":
                    if (type == "hold" || type == "conquer")
                        pts += 1;
                    break;

                // 遗忘丰碑：第三回合前阻断据守得分
                case "forgotten_monument":
                    if (type == "hold" && G.round < 3)
                        return false;
                    break;

                // 缇亚娜·冕卫被动：对手本回合不得获得据守分
                // （通过扫描场上缇亚娜实现，不依赖战场牌 card.id）
            }

            return true;
        }

        // ────────────────────────────────────────────
        // 缇亚娜扫描（由 AddScore 在 ModifyAddScore 外调用）
        // ────────────────────────────────────────────

        /// <summary>
        /// 检查对方场上是否有缇亚娜·冕卫（tiyana_enter 效果），若有则阻断据守得分。
        /// 由 TurnManager.AddScore 在 ModifyAddScore 之后调用（type=="hold" 时）。
        /// </summary>
        public bool IsTiyanaBlockingHold(Owner who)
        {
            Owner opp = G.Opponent(who);
            var oppAllUnits = GetAllUnitsForOwner(opp);
            return oppAllUnits.Any(u => u.effect == "tiyana_enter");
        }

        // ────────────────────────────────────────────
        // OnCombatStart — CombatResolver.TriggerCombat 最前
        // ────────────────────────────────────────────

        /// <summary>
        /// 战斗对决开始时效果（等价原版 reckoner_arena 判定）。
        /// </summary>
        public void OnCombatStart(BattlefieldState bf, Owner attacker)
        {
            if (bf?.card == null) return;

            switch (bf.card.id)
            {
                // 清算人竞技场：战力≥5的单位获得强攻（进攻方）或坚守（防守方）
                case "reckoner_arena":
                {
                    Owner defender = G.Opponent(attacker);
                    var atkUs = attacker == Owner.Player ? bf.pU : bf.eU;
                    var defUs = attacker == Owner.Player ? bf.eU : bf.pU;

                    foreach (var u in atkUs)
                    {
                        if (CombatResolver.EffAtk(u) >= 5)
                        {
                            if (u.keywords == null) u.keywords = new List<string>();
                            if (!u.keywords.Contains("强攻"))
                                u.keywords.Add("强攻");
                        }
                    }
                    foreach (var u in defUs)
                    {
                        if (CombatResolver.EffAtk(u) >= 5)
                        {
                            if (u.keywords == null) u.keywords = new List<string>();
                            if (!u.keywords.Contains("坚守"))
                                u.keywords.Add("坚守");
                        }
                    }
                    break;
                }
            }
        }

        // ────────────────────────────────────────────
        // OnConquer — CombatResolver.TriggerCombat 征服后
        // ────────────────────────────────────────────

        /// <summary>
        /// 征服战场后触发（等价原版 postCombatTriggers conquer 分支）。
        /// winner = 征服方（进攻方）。
        /// </summary>
        public void OnConquer(BattlefieldState bf, Owner winner)
        {
            if (bf?.card == null) return;

            var hand     = G.GetHand(winner);
            var deck     = G.GetDeck(winner);
            var baseZone = G.GetBase(winner);
            var discard  = G.GetDiscard(winner);
            var runeDeck = G.GetRuneDeck(winner);
            var runes    = G.GetRunes(winner);

            switch (bf.card.id)
            {
                // 希拉娜修道院：消耗一名盟友的增益指示物 → 抽1张牌
                case "hirana":
                {
                    var buffed = GetAllUnitsForOwner(winner).Where(u => u.buffToken).ToList();
                    if (buffed.Count > 0)
                    {
                        var target = PromptBaseUnit(buffed);
                        if (target != null)
                        {
                            target.buffToken = false;
                            target.currentAtk = Math.Max(1, target.currentAtk - 1);
                            if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                            {
                                hand.Add(deck[deck.Count - 1]);
                                deck.RemoveAt(deck.Count - 1);
                            }
                        }
                    }
                    break;
                }

                // 掠夺者之街：从废牌堆召回1名费用≤2的非法术单位
                case "reaver_row":
                {
                    var valid = discard.Where(c => c.type != CardType.Spell && c.cost <= 2).ToList();
                    if (valid.Count > 0)
                    {
                        var target = PromptDiscardUnit(valid);
                        if (target != null)
                        {
                            discard.Remove(target);
                            target.exhausted = true;
                            baseZone.Add(target);
                        }
                    }
                    break;
                }

                // 祖安地沟：弃置1张手牌 → 抽1张牌
                case "zaun_undercity":
                {
                    if (hand.Count > 0)
                    {
                        var toDiscard = PromptHandCard(hand);
                        if (toDiscard != null)
                        {
                            hand.Remove(toDiscard);
                            discard.Add(toDiscard);
                            if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                            {
                                hand.Add(deck[deck.Count - 1]);
                                deck.RemoveAt(deck.Count - 1);
                            }
                        }
                    }
                    break;
                }

                // 力量方尖碑（征服）：获得1张符文（未翻转）
                case "strength_obelisk":
                {
                    if (runeDeck.Count > 0)
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        r.tapped = false;
                        runes.Add(r);
                    }
                    break;
                }

                // 雷霆之纹：回收1枚已点击符文到符文牌堆顶
                case "thunder_rune":
                {
                    var tapped = runes.Where(r => r.tapped).ToList();
                    if (tapped.Count > 0)
                    {
                        var rune = PromptTappedRune(tapped);
                        if (rune != null)
                        {
                            runes.Remove(rune);
                            rune.tapped = false;
                            runeDeck.Add(rune);
                        }
                    }
                    break;
                }
            }
        }

        // ────────────────────────────────────────────
        // OnDefenseFailure — 玩家防守失败（enemy 征服）
        // ────────────────────────────────────────────

        /// <summary>
        /// 防守方未能守住战场时触发（等价原版 postCombatTriggers enemy-conquer 分支）。
        /// defender = 防守失败的一方（即被征服方）。
        /// </summary>
        public void OnDefenseFailure(BattlefieldState bf, Owner defender)
        {
            if (bf?.card == null) return;

            var hand = G.GetHand(defender);
            var deck = G.GetDeck(defender);

            switch (bf.card.id)
            {
                // 沉没神庙：支付2法力 → 抽1张牌
                case "sunken_temple":
                {
                    int mana = G.GetMana(defender);
                    if (mana >= 2 && PromptConfirm())
                    {
                        G.SetMana(defender, mana - 2);
                        if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                        {
                            hand.Add(deck[deck.Count - 1]);
                            deck.RemoveAt(deck.Count - 1);
                        }
                    }
                    break;
                }
            }
        }

        // ────────────────────────────────────────────
        // OnUnitEnterBF — CardDeployer.MoveUnit 单位进入战场后
        // ────────────────────────────────────────────

        /// <summary>
        /// 单位移动进入战场时触发（等价原版 combat.js moveUnit 进入判定）。
        /// </summary>
        public void OnUnitEnterBF(BattlefieldState bf, CardInstance unit, Owner owner)
        {
            if (bf?.card == null) return;

            switch (bf.card.id)
            {
                // 崔法利战营：移动进入此处时获得 buffToken（+1战力）
                case "trifarian_warcamp":
                    ApplyBuffToken(unit);
                    break;
            }
        }

        // ────────────────────────────────────────────
        // OnUnitLeaveBF — CardDeployer.MoveUnit 单位离开战场前
        // ────────────────────────────────────────────

        /// <summary>
        /// 单位离开战场时触发（等价原版 combat.js moveUnit 离开判定）。
        /// </summary>
        public void OnUnitLeaveBF(BattlefieldState bf, CardInstance unit, Owner owner)
        {
            if (bf?.card == null) return;

            switch (bf.card.id)
            {
                // 暗巷酒吧：离开此处的己方单位本回合战力+1（临时）
                case "back_alley_bar":
                    if (unit.tb == null) unit.tb = new TurnBuffs();
                    unit.tb.atk += 1;
                    break;
            }
        }

        // ────────────────────────────────────────────
        // OnSpellTargetAlly — SpellSystem 对己方单位施放法术时
        // ────────────────────────────────────────────

        /// <summary>
        /// 法术以己方战场单位为目标时触发（等价原版 spell.js 梦幻树判定）。
        /// spellOwner = 施放法术的一方。
        /// </summary>
        public void OnSpellTargetAlly(CardInstance targetUnit, Owner spellOwner)
        {
            // 在所有战场中找目标所在位置
            foreach (var bf in G.bf)
            {
                if (bf?.card == null) continue;
                var allySlot = spellOwner == Owner.Player ? bf.pU : bf.eU;
                if (!allySlot.Contains(targetUnit)) continue;

                switch (bf.card.id)
                {
                    // 梦幻树：以此处友方单位为法术目标时抽1张牌
                    case "dreaming_tree":
                    {
                        var hand = G.GetHand(spellOwner);
                        var deck = G.GetDeck(spellOwner);
                        if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                        {
                            hand.Add(deck[deck.Count - 1]);
                            deck.RemoveAt(deck.Count - 1);
                        }
                        break;
                    }
                }
            }
        }

        // ────────────────────────────────────────────
        // ModifySpellDamage — SpellSystem 对单位造成法术/技能伤害前
        // ────────────────────────────────────────────

        /// <summary>
        /// 返回修改后的伤害值。
        /// 等价原版 void_gate 效果：此处单位受法术/技能伤害时额外+1。
        /// </summary>
        public int ModifySpellDamage(CardInstance target, int baseDmg)
        {
            foreach (var bf in G.bf)
            {
                if (bf?.card == null) continue;
                bool inBf = bf.pU.Contains(target) || bf.eU.Contains(target);
                if (!inBf) continue;

                if (bf.card.id == "void_gate")
                    return baseDmg + 1;
            }
            return baseDmg;
        }

        // ────────────────────────────────────────────
        // CanDeployToBF — CardDeployer.DeployToBF 前置检查
        // ────────────────────────────────────────────

        /// <summary>
        /// 返回 false 表示该战场禁止从手牌直接出牌（只能通过移动进入）。
        /// 等价原版 rockfall_path 限制。
        /// </summary>
        public bool CanDeployToBF(int bfId, Owner owner)
        {
            if (bfId < 1 || bfId > G.bf.Length) return true;
            var bf = G.bf[bfId - 1];
            if (bf?.card == null) return true;
            return bf.card.id != "rockfall_path";
        }

        // ────────────────────────────────────────────
        // CanMoveToBase — CardDeployer.MoveUnit 目标为 base 前置检查
        // ────────────────────────────────────────────

        /// <summary>
        /// 返回 false 表示该战场的单位无法移回基地。
        /// 等价原版 vile_throat_nest 限制。
        /// </summary>
        public bool CanMoveToBase(CardInstance unit, Owner owner)
        {
            foreach (var bf in G.bf)
            {
                if (bf?.card == null) continue;
                var slot = owner == Owner.Player ? bf.pU : bf.eU;
                if (!slot.Contains(unit)) continue;
                if (bf.card.id == "vile_throat_nest")
                    return false;
            }
            return true;
        }

        // ────────────────────────────────────────────
        // 内部辅助
        // ────────────────────────────────────────────

        private static void ApplyBuffToken(CardInstance unit)
        {
            unit.buffToken  = true;
            unit.currentAtk += 1;
            unit.currentHp  = unit.currentAtk;
            if (unit.tb == null) unit.tb = new TurnBuffs();
        }

        private List<CardInstance> GetAllUnitsForOwner(Owner o)
        {
            var result = new List<CardInstance>(G.GetBase(o));
            foreach (var b in G.bf)
                result.AddRange(o == Owner.Player ? b.pU : b.eU);
            return result;
        }
    }
}
