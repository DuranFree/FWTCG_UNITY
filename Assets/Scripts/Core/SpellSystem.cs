using System;
using System.Collections.Generic;
using System.Linq;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 法术系统，等价原版 spell.js 的：
    ///   canPlay / getSpellTargets / applySpell（33 效果）/
    ///   startSpellDuel / runDuelTurn / skipDuel / endDuel /
    ///   hasPlayableReactionCards
    ///
    /// 纯 C# 类，不依赖 MonoBehaviour。
    /// 交互提示通过委托注入：
    ///   - 测试：简单确定性选择（首选 / 固定选项）
    ///   - Unity UI：弹窗/异步选择
    ///
    /// P7 存根：装备效果（trinity_equip / guardian_equip / dorans_equip）
    /// P8 存根：传奇对决技能（AiLegendDuelAction）
    /// </summary>
    public class SpellSystem
    {
        public readonly GameState G;
        private readonly TurnManager    _tm;
        private readonly CardDeployer   _cd;
        private readonly CombatResolver _cr;

        // ── 提示委托（测试注入确定性版本，UI 注入弹窗版本）──

        /// <summary>从列表中选择一张牌作为目标（法术目标、多段目标选择）。</summary>
        public Func<List<CardInstance>, CardInstance> PromptTarget    = list => list.Count > 0 ? list[0] : null;
        /// <summary>从手牌中选择一张牌弃置。</summary>
        public Func<List<CardInstance>, CardInstance> PromptDiscard   = list => list.Count > 0 ? list[0] : null;
        /// <summary>从可用战场 id 列表中选择一个战场索引（0-based）。</summary>
        public Func<List<int>, int?> PromptBattlefield                = opts => opts.Count > 0 ? (int?)opts[0] : null;
        /// <summary>是否施放回响（返回 true = 施放，false = 跳过）。</summary>
        public Func<CardInstance, Owner, bool> PromptEcho             = (_, __) => false;
        /// <summary>AI 对决回合回调（测试注入 no-op 或 AiDuelAction；Unity 注入完整 AI 逻辑）。</summary>
        public Action OnAiDuelTurn = () => { };
        /// <summary>对决结束后继续 AI 行动的回调（空战场征服后）。</summary>
        public Action<Owner> OnDuelEnded = _ => { };

        public SpellSystem(GameState g, TurnManager tm, CardDeployer cd, CombatResolver cr)
        {
            G   = g;
            _tm = tm;
            _cd = cd;
            _cr = cr;
        }

        // ─────────────────────────────────────────
        // 工具方法
        // ─────────────────────────────────────────

        /// <summary>
        /// 等价原版 getAllUnits(owner)：基地（排除装备）+ 全部战场单位。
        /// </summary>
        public List<CardInstance> GetAllUnits(Owner owner)
        {
            var base_ = G.GetBase(owner).Where(u => u.type != CardType.Equipment).ToList();
            var bfUs  = G.bf.SelectMany(b => owner == Owner.Player ? b.pU : b.eU).ToList();
            return base_.Concat(bfUs).ToList();
        }

        /// <summary>
        /// 仅战场单位（不含基地），排除法术免疫。
        /// </summary>
        private List<CardInstance> GetBfUnits(Owner owner, bool excludeUntargetable = false)
        {
            var units = G.bf.SelectMany(b => owner == Owner.Player ? b.pU : b.eU).ToList();
            if (excludeUntargetable)
                units = units.Where(u => u.effect != "untargetable").ToList();
            return units;
        }

        /// <summary>
        /// 法力费用计算（等价原版 getEffectiveCost）。
        /// balance_resolve：对手距胜利 ≤3 时 -2（最低0）。
        /// </summary>
        public int GetEffectiveCost(CardInstance c, Owner owner)
        {
            int cost = c.cost;
            if (c.id == "balance_resolve")
            {
                Owner opp       = G.Opponent(owner);
                int   oppScore  = opp == Owner.Player ? G.pScore : G.eScore;
                if (oppScore >= GameState.WIN_SCORE - 3)
                    cost = Math.Max(0, cost - 2);
            }
            return cost;
        }

        /// <summary>
        /// 出牌合法性校验，等价原版 canPlay。
        /// 仅校验时机与资源，不校验 UI 状态（selUnits 等由 UI 层处理）。
        /// </summary>
        public bool CanPlay(CardInstance c, Owner owner)
        {
            if (G.cardLockTarget == owner) return false;

            int  effCost     = GetEffectiveCost(c, owner);
            bool hasMana     = G.GetMana(owner) >= effCost;
            var  sch         = G.GetSch(owner);
            bool hasSch      = sch.Get(c.schType) >= c.schCost
                            && sch.Get(c.schType2) >= c.schCost2;

            // 反应窗口：只能出反应/迅捷牌
            if (G.reactionWindowOpen && G.reactionWindowFor == owner)
            {
                bool isFast = c.keywords != null &&
                              (c.keywords.Contains("反应") || c.keywords.Contains("迅捷"));
                if (!isFast || !hasMana || !hasSch) return false;
                if (c.type == CardType.Spell)
                {
                    var t = GetSpellTargets(c, owner);
                    if (t != null && t.Count == 0) return false;
                }
                return true;
            }

            // 对决阶段：只能出迅捷/反应牌
            if (G.duelActive)
            {
                if (G.duelTurn != owner) return false;
                bool isFast = c.keywords != null &&
                              (c.keywords.Contains("迅捷") || c.keywords.Contains("反应"));
                if (!isFast || !hasMana || !hasSch) return false;
                if (c.type == CardType.Spell)
                {
                    var t = GetSpellTargets(c, owner);
                    if (t != null && t.Count == 0) return false;
                }
                return true;
            }

            // 行动阶段
            if (G.turn != owner || G.phase != GamePhase.Action) return false;
            if (!hasMana || !hasSch) return false;
            if (c.type == CardType.Spell)
            {
                var t = GetSpellTargets(c, owner);
                if (t != null && t.Count == 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 获取法术目标池，等价原版 getSpellTargets。
        /// 返回 null = 无需目标；返回空列表 = 没有合法目标（阻止出牌）；非空 = 合法目标 uid 列表。
        /// </summary>
        public List<int> GetSpellTargets(CardInstance c, Owner owner)
        {
            Owner opp     = G.Opponent(owner);
            var myUnits   = GetAllUnits(owner);
            var enUnits   = GetAllUnits(opp).Where(u => u.effect != "untargetable").ToList();
            var allUnits  = GetAllUnits(Owner.Player).Concat(GetAllUnits(Owner.Enemy)).ToList();
            var enBFUnits = GetBfUnits(opp, excludeUntargetable: true);
            var allBFUnits= G.bf.SelectMany(b => b.pU.Concat(b.eU)).ToList();

            switch (c.effect)
            {
                case "buff_ally":        return myUnits.Where(u => !u.buffToken).Select(u => u.uid).ToList();
                case "stun":             return enUnits.Select(u => u.uid).ToList();
                case "weaken":           return enUnits.Select(u => u.uid).ToList();
                case "deal3":            return enBFUnits.Select(u => u.uid).ToList();
                case "debuff4":          return enUnits.Select(u => u.uid).ToList();
                case "debuff1_draw":     return enUnits.Select(u => u.uid).ToList();
                case "recall_draw":      return myUnits.Select(u => u.uid).ToList();
                case "buff_draw":        return myUnits.Select(u => u.uid).ToList();
                case "recall_unit_rune": return myUnits.Select(u => u.uid).ToList();
                case "deal3_twice":      return enUnits.Select(u => u.uid).ToList();
                case "deal6_two":        return enUnits.Select(u => u.uid).ToList();
                case "deal1_repeat":     return enUnits.Select(u => u.uid).ToList();
                case "deal4_draw":       return allBFUnits.Select(u => u.uid).ToList();
                case "thunder_gal_manual": return allUnits.Select(u => u.uid).ToList();
                case "buff7_manual":     return allUnits.Select(u => u.uid).ToList();
                case "buff5_manual":     return allUnits.Select(u => u.uid).ToList();
                case "stun_manual":
                {
                    var bfUnits = GetBfUnits(opp)
                        .Where(u => u.keywords == null || !u.keywords.Contains("法术免疫"))
                        .Select(u => u.uid).ToList();
                    return bfUnits;
                }
                case "buff2_draw":       return allUnits.Select(u => u.uid).ToList();
                case "buff1_solo":       return myUnits.Select(u => u.uid).ToList();
                case "force_move":       return GetAllUnits(opp).Where(u => u.keywords == null || !u.keywords.Contains("法术免疫")).Select(u => u.uid).ToList();
                case "ready_unit":       return allUnits.Select(u => u.uid).ToList();
                case "discard_deal":     return enBFUnits.Select(u => u.uid).ToList();
                case "deal2_two":        return enBFUnits.Select(u => u.uid).ToList();
                case "deal1_same_zone":  return enUnits.Select(u => u.uid).ToList();
                case "akasi_storm":      return null;
                case "counter_cost4":    return null;
                case "counter_any":      return null;
                case "negate_spell":     return null;
                case "rally_call":       return null;
                case "balance_resolve":  return null;
                case "trinity_equip":    return null;
                case "guardian_equip":   return null;
                case "dorans_equip":     return null;
                case "death_shield":     return null;
                default:                 return null;
            }
        }

        /// <summary>
        /// 是否有可打出的反应/迅捷牌（等价原版 hasPlayableReactionCards）。
        /// </summary>
        public bool HasPlayableReactionCards(Owner owner)
        {
            if (G.turn == owner || G.duelActive || G.gameOver) return false;
            var hand = G.GetHand(owner);
            var sch  = G.GetSch(owner);
            int mana = G.GetMana(owner);
            return hand.Any(c =>
                (c.keywords?.Contains("反应") == true || c.keywords?.Contains("迅捷") == true) &&
                c.type != CardType.Equipment &&
                c.cost <= mana &&
                sch.Get(c.schType) >= c.schCost &&
                (c.schCost2 == 0 || sch.Get(c.schType2) >= c.schCost2));
        }

        // ─────────────────────────────────────────
        // 法术施放主入口
        // ─────────────────────────────────────────

        /// <summary>
        /// 法术结算主入口，等价原版 applySpell。
        /// 法盾检查 → 效果派发 → 回响检查。
        /// isEcho = true 跳过施法通告 + 回响递归（防止无限循环）。
        /// </summary>
        public void ApplySpell(CardInstance spell, Owner owner, int? targetUid, bool isEcho = false)
        {
            Owner opp     = G.Opponent(owner);
            var   myLeg   = G.GetLeg(owner);
            var   opLeg   = G.GetLeg(opp);

            // ── 法盾：对手须额外支付1点符能 ──
            if (spell.type == CardType.Spell && targetUid.HasValue)
            {
                var allUnits = GetAllUnits(Owner.Player).Concat(GetAllUnits(Owner.Enemy)).ToList();
                var target   = allUnits.FirstOrDefault(u => u.uid == targetUid.Value);
                if (target != null && target.keywords?.Contains("法盾") == true)
                {
                    var ownerSch = G.GetSch(owner);
                    if (ownerSch.Total() <= 0) return;  // 符能不足，法术中止
                    // 消耗第一个有存量的符能类型
                    SpendAnyOneSch(owner);
                }
            }

            // ── 效果派发 ──
            DispatchEffect(spell, owner, targetUid);

            // ── 回响检查 ──
            if (!isEcho && spell.type == CardType.Spell && spell.keywords?.Contains("回响") == true)
            {
                int  echoMana   = spell.echoManaCost;
                int  echoSch    = spell.echoSchCost;
                var  echoType   = spell.echoSchType;
                var  mySch      = G.GetSch(owner);
                bool canEcho    = G.GetMana(owner) >= echoMana
                               && (echoSch == 0 || mySch.Get(echoType) >= echoSch);

                if (canEcho && PromptEcho(spell, owner))
                {
                    if (echoMana > 0) G.SetMana(owner, G.GetMana(owner) - echoMana);
                    if (echoSch > 0) mySch.Spend(echoType, echoSch);
                    ApplySpell(spell, owner, targetUid, isEcho: true);
                }
            }
        }

        private void SpendAnyOneSch(Owner owner)
        {
            var sch = G.GetSch(owner);
            if (sch.blazing  > 0) { sch.Spend(RuneType.Blazing,  1); return; }
            if (sch.radiant  > 0) { sch.Spend(RuneType.Radiant,  1); return; }
            if (sch.verdant  > 0) { sch.Spend(RuneType.Verdant,  1); return; }
            if (sch.crushing > 0) { sch.Spend(RuneType.Crushing, 1); return; }
            if (sch.chaos    > 0) { sch.Spend(RuneType.Chaos,    1); return; }
            if (sch.order    > 0) { sch.Spend(RuneType.Order,    1); return; }
        }

        // ─────────────────────────────────────────
        // 效果派发
        // ─────────────────────────────────────────

        private void DispatchEffect(CardInstance spell, Owner owner, int? targetUid)
        {
            Owner opp   = G.Opponent(owner);
            var hand    = G.GetHand(owner);
            var deck    = G.GetDeck(owner);
            var discard = G.GetDiscard(owner);
            var myRunes = G.GetRunes(owner);
            var runeDeck= G.GetRuneDeck(owner);
            var opLeg   = G.GetLeg(opp);

            switch (spell.effect)
            {
                // ── 抽牌 / 符文 ──
                case "draw1":
                    if (deck.Count > 0) hand.Add(deck[deck.Count - 1]);
                    if (deck.Count > 0) deck.RemoveAt(deck.Count - 1);
                    break;

                case "draw4":
                    for (int i = 0; i < 4; i++)
                    {
                        if (deck.Count == 0) break;
                        hand.Add(deck[deck.Count - 1]);
                        deck.RemoveAt(deck.Count - 1);
                    }
                    break;

                case "summon_rune1":
                {
                    if (runeDeck.Count > 0)
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        r.tapped = true;
                        myRunes.Add(r);
                    }
                    break;
                }

                case "rune_draw":
                {
                    if (runeDeck.Count > 0)
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        r.tapped = true;
                        myRunes.Add(r);
                    }
                    if (deck.Count > 0)
                    {
                        hand.Add(deck[deck.Count - 1]);
                        deck.RemoveAt(deck.Count - 1);
                    }
                    break;
                }

                // ── 增益盟友（永久）──
                case "buff_ally":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(owner).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    target.currentAtk++;
                    target.atk++;
                    target.currentHp = target.currentAtk;
                    break;
                }

                // ── 眩晕 ──
                case "stun":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(opp).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null) target.stunned = true;
                    break;
                }

                // ── 减弱（-2回合攻击 or 对传奇造成2伤）──
                case "weaken":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(opp).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    if (opLeg != null && target.uid == opLeg.uid)
                        _cd.DealDamage(target, 2, opp, isLegend: true);
                    else
                        target.tb.atk -= 2;
                    break;
                }

                // ── 对战场单位造成3伤 ──
                case "deal3":
                {
                    if (!targetUid.HasValue) break;
                    var bfUnits = GetBfUnits(opp);
                    var target  = bfUnits.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null)
                        _cd.DealDamage(target, 3, opp, opLeg != null && target.uid == opLeg.uid);
                    break;
                }

                // ── 减弱4 or 对传奇造成4伤 ──
                case "debuff4":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(opp).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    if (opLeg != null && target.uid == opLeg.uid)
                        _cd.DealDamage(target, 4, opp, isLegend: true);
                    else
                        target.tb.atk -= 4;
                    break;
                }

                // ── 减弱1+抽1 ──
                case "debuff1_draw":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(opp).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    if (opLeg != null && target.uid == opLeg.uid)
                        _cd.DealDamage(target, 1, opp, isLegend: true);
                    else
                        target.tb.atk -= 1;
                    if (deck.Count > 0) { hand.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1); }
                    break;
                }

                // ── 召回+抽1 ──
                case "recall_draw":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(owner).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    _cd.RemoveUnitFromField(target, owner);
                    hand.Add(target);
                    if (deck.Count > 0) { hand.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1); }
                    break;
                }

                // ── 临时+1攻击+抽1 ──
                case "buff_draw":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(owner).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null) target.tb.atk++;
                    if (deck.Count > 0) { hand.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1); }
                    break;
                }

                // ── 召回+召出符文 ──
                case "recall_unit_rune":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(owner).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    if (G.GetLeg(owner) != null && target.uid == G.GetLeg(owner).uid) break;  // 传奇不可召回
                    _cd.RemoveUnitFromField(target, owner);
                    hand.Add(target);
                    if (runeDeck.Count > 0)
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        r.tapped = true;
                        myRunes.Add(r);
                    }
                    break;
                }

                // ── 两段各3伤 ──
                case "deal3_twice":
                {
                    var enemies = GetAllUnits(opp);
                    if (enemies.Count == 0) break;
                    var t1 = PromptTarget(enemies);
                    if (t1 != null) _cd.DealDamage(t1, 3, opp, opLeg != null && t1.uid == opLeg.uid);
                    var remaining = GetAllUnits(opp);
                    if (remaining.Count == 0) break;
                    var t2 = PromptTarget(remaining);
                    if (t2 != null) _cd.DealDamage(t2, 3, opp, opLeg != null && t2.uid == opLeg.uid);
                    break;
                }

                // ── 两段各6伤 ──
                case "deal6_two":
                {
                    var enemies = GetAllUnits(opp);
                    if (enemies.Count == 0) break;
                    var t1 = PromptTarget(enemies);
                    if (t1 != null) _cd.DealDamage(t1, 6, opp, opLeg != null && t1.uid == opLeg.uid);
                    var remaining = GetAllUnits(opp);
                    if (remaining.Count > 0)
                    {
                        var t2 = PromptTarget(remaining);
                        if (t2 != null) _cd.DealDamage(t2, 6, opp, opLeg != null && t2.uid == opLeg.uid);
                    }
                    break;
                }

                // ── 5次各1伤 ──
                case "deal1_repeat":
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var remaining = GetAllUnits(opp);
                        if (remaining.Count == 0) break;
                        var t = PromptTarget(remaining);
                        if (t != null) _cd.DealDamage(t, 1, opp, opLeg != null && t.uid == opLeg.uid);
                    }
                    break;
                }

                // ── 对战场单位造成4伤+抽1 ──
                case "deal4_draw":
                {
                    if (!targetUid.HasValue) break;
                    var allBF = G.bf.SelectMany(b => b.pU.Concat(b.eU)).ToList();
                    var target = allBF.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    bool isLeg = (G.pLeg != null && target.uid == G.pLeg.uid) ||
                                 (G.eLeg != null && target.uid == G.eLeg.uid);
                    Owner targetOwner = GetAllUnits(Owner.Player).Any(u => u.uid == target.uid)
                        ? Owner.Player : Owner.Enemy;
                    _cd.DealDamage(target, 4, targetOwner, isLeg);
                    if (deck.Count > 0) { hand.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1); }
                    break;
                }

                // ── 造成等同目标攻击力的伤害 ──
                case "thunder_gal_manual":
                {
                    if (!targetUid.HasValue) break;
                    var allPool = GetAllUnits(Owner.Player).Concat(GetAllUnits(Owner.Enemy)).ToList();
                    var target  = allPool.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    int dmg = CombatResolver.EffAtk(target);
                    bool isLeg = (G.pLeg != null && target.uid == G.pLeg.uid) ||
                                 (G.eLeg != null && target.uid == G.eLeg.uid);
                    Owner targetOwner = GetAllUnits(Owner.Player).Any(u => u.uid == target.uid)
                        ? Owner.Player : Owner.Enemy;
                    _cd.DealDamage(target, dmg, targetOwner, isLeg);
                    break;
                }

                // ── 临时+7 ──
                case "buff7_manual":
                {
                    if (!targetUid.HasValue) break;
                    var allPool = GetAllUnits(Owner.Player).Concat(GetAllUnits(Owner.Enemy)).ToList();
                    var target  = allPool.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null) target.tb.atk += 7;
                    break;
                }

                // ── 临时+5 ──
                case "buff5_manual":
                {
                    if (!targetUid.HasValue) break;
                    var allPool = GetAllUnits(Owner.Player).Concat(GetAllUnits(Owner.Enemy)).ToList();
                    var target  = allPool.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null) target.tb.atk += 5;
                    break;
                }

                // ── 眩晕（需手选 BF 目标）──
                case "stun_manual":
                {
                    if (!targetUid.HasValue) break;
                    var allOppUnits = GetAllUnits(opp);
                    var target      = allOppUnits.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null) target.stunned = true;
                    break;
                }

                // ── 临时+2+抽1 ──
                case "buff2_draw":
                {
                    if (!targetUid.HasValue) break;
                    var allPool = GetAllUnits(Owner.Player).Concat(GetAllUnits(Owner.Enemy)).ToList();
                    var target  = allPool.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null) target.tb.atk += 2;
                    if (deck.Count > 0) { hand.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1); }
                    break;
                }

                // ── 决斗架势：+1（独守时+2）──
                case "buff1_solo":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(owner).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    int bonus = 1;
                    var bf = G.bf.FirstOrDefault(b =>
                        (owner == Owner.Player ? b.pU : b.eU).Contains(target));
                    if (bf != null)
                    {
                        var myBfUnits = owner == Owner.Player ? bf.pU : bf.eU;
                        var opBfUnits = owner == Owner.Player ? bf.eU : bf.pU;
                        if (myBfUnits.Count == 1 && myBfUnits[0].uid == target.uid && opBfUnits.Count > 0)
                            bonus = 2;
                    }
                    target.tb.atk += bonus;
                    break;
                }

                // ── 强制移动敌方单位到战场 ──
                case "force_move":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(opp).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    var bfOptions = new List<int>();
                    for (int i = 0; i < G.bf.Length; i++)
                    {
                        var slots = opp == Owner.Player ? G.bf[i].pU : G.bf[i].eU;
                        if (slots.Count < GameState.MAX_BF_UNITS) bfOptions.Add(i);
                    }
                    if (bfOptions.Count == 0) break;
                    int? bfIdx = PromptBattlefield(bfOptions);
                    if (bfIdx.HasValue)
                    {
                        _cd.RemoveUnitFromField(target, opp);
                        var slots = opp == Owner.Player ? G.bf[bfIdx.Value].pU : G.bf[bfIdx.Value].eU;
                        slots.Add(target);
                    }
                    break;
                }

                // ── 大副：重置单位为活跃 ──
                case "ready_unit":
                {
                    if (!targetUid.HasValue) break;
                    var allPool = GetAllUnits(Owner.Player).Concat(GetAllUnits(Owner.Enemy)).ToList();
                    var target  = allPool.FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null) target.exhausted = false;
                    break;
                }

                // ── 弃牌+造成其费用的伤害 ──
                case "discard_deal":
                {
                    if (!targetUid.HasValue) break;
                    if (hand.Count == 0) break;
                    var card = PromptDiscard(hand);
                    if (card == null) break;
                    int idx = hand.IndexOf(card);
                    if (idx >= 0) hand.RemoveAt(idx);
                    discard.Add(card);
                    var target = GetBfUnits(opp).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target != null)
                        _cd.DealDamage(target, card.cost, opp, opLeg != null && target.uid == opLeg.uid);
                    break;
                }

                // ── 两段各2伤（第1击仅BF，第2击任意）──
                case "deal2_two":
                {
                    var bfFirst = GetBfUnits(opp);
                    if (bfFirst.Count == 0) break;
                    var t1 = targetUid.HasValue
                        ? bfFirst.FirstOrDefault(u => u.uid == targetUid.Value) ?? PromptTarget(bfFirst)
                        : PromptTarget(bfFirst);
                    if (t1 != null) _cd.DealDamage(t1, 2, opp, opLeg != null && t1.uid == opLeg.uid);
                    var remaining = GetAllUnits(opp);
                    if (remaining.Count > 0)
                    {
                        var t2 = PromptTarget(remaining);
                        if (t2 != null) _cd.DealDamage(t2, 2, opp, opLeg != null && t2.uid == opLeg.uid);
                    }
                    break;
                }

                // ── 同区域最多3个单位各1伤 ──
                case "deal1_same_zone":
                {
                    if (!targetUid.HasValue) break;
                    var target = GetAllUnits(opp).FirstOrDefault(u => u.uid == targetUid.Value);
                    if (target == null) break;
                    List<CardInstance> pool = new List<CardInstance>();
                    foreach (var b in G.bf)
                    {
                        var bfSide = opp == Owner.Player ? b.pU : b.eU;
                        if (bfSide.Contains(target)) { pool = bfSide; break; }
                    }
                    if (pool.Count == 0)
                    {
                        var opBase = G.GetBase(opp);
                        if (opBase.Contains(target)) pool = opBase;
                    }
                    pool = pool.Take(3).ToList();
                    foreach (var u in pool)
                        _cd.DealDamage(u, 1, opp, opLeg != null && u.uid == opLeg.uid);
                    break;
                }

                // ── 艾卡西亚暴雨：6×2随机 ──
                case "akasi_storm":
                {
                    var rng = new System.Random();
                    for (int i = 0; i < 6; i++)
                    {
                        var remaining = GetAllUnits(opp);
                        if (remaining.Count == 0) break;
                        var t = remaining[rng.Next(remaining.Count)];
                        _cd.DealDamage(t, 2, opp, opLeg != null && t.uid == opLeg.uid);
                    }
                    break;
                }

                // ── 反制法术（P6 逻辑层：标记存根）──
                case "counter_cost4":
                case "counter_any":
                case "negate_spell":
                    // 反制效果：中断目标法术的施放（UI 层处理；纯逻辑层无需额外状态）
                    break;

                // ── 迎敌号令：本回合所有单位活跃入场+抽1 ──
                case "rally_call":
                {
                    if (owner == Owner.Player) G.pRallyActive = true;
                    else G.eRallyActive = true;
                    if (deck.Count > 0) { hand.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1); }
                    break;
                }

                // ── 御衡守念：抽1+召出1枚符文 ──
                case "balance_resolve":
                {
                    if (deck.Count > 0) { hand.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1); }
                    if (runeDeck.Count > 0)
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        r.tapped = true;
                        myRunes.Add(r);
                    }
                    break;
                }

                // ── 装备效果（P7 装配逻辑）──
                // trinity_equip / guardian_equip / dorans_equip:
                //   玩家路径：PromptTarget 选择基地单位附着（可选，跳过则停在基地）
                //   AI路径：自动选择基地最高战力单位
                case "trinity_equip":
                case "guardian_equip":
                case "dorans_equip":
                {
                    var baseUnits = G.GetBase(owner).Where(u => u.type != CardType.Equipment).ToList();
                    if (baseUnits.Count > 0)
                    {
                        // PromptTarget：UI 层可选目标（optional），null 表示跳过，装备留在基地
                        var target = PromptTarget(baseUnits);
                        if (target != null)
                        {
                            // 装备已在基地（通过 DeployToBase 进入），此处从基地找到并附着
                            // 注：此 case 在 ApplySpell 中被调用时，装备已由 DeployToBase 放入基地
                            // 通过 G._lastDeployedEquipUid 找到刚放入基地的装备实例（如存在）
                            CardInstance equipInst = null;
                            if (G.LastDeployedUid.HasValue)
                            {
                                equipInst = G.GetBase(owner).FirstOrDefault(
                                    u => u.uid == G.LastDeployedUid.Value && u.type == CardType.Equipment);
                            }
                            // 若找不到（如 AI 直接 ApplySpell），用 spell 本身作为装备实例（已是 mk 后的实例）
                            if (equipInst == null)
                                equipInst = spell;

                            _cd.AttachEquipToUnit(equipInst, target, owner, paySchCost: false);
                            // atkBonus 已在 AttachEquipToUnit 内处理
                        }
                        // 若 target == null：装备留在基地（待命状态，可后续用 ActivateEquipAbility 附着）
                    }
                    // 若基地无单位：装备部署后待在基地
                    break;
                }

                // death_shield (中娅沙漏)：部署到基地即生效（cleanDeadAll 自动检查）
                case "death_shield":
                    // 无即时效果；触发效果在 CardDeployer.TryDeathShield 中处理
                    break;

                default:
                    break;
            }
        }

        // ─────────────────────────────────────────
        // 装备系统（P7）
        // ─────────────────────────────────────────

        /// <summary>
        /// 激活基地中装备的装配异能，等价原版 activateEquipAbility。
        /// 校验费用 → PromptTarget 选择单位 → AttachEquipToUnit（支付 equipSchCost）。
        /// 返回 true 表示成功附着；false 表示取消/费用不足。
        /// </summary>
        public bool ActivateEquipAbility(CardInstance equip, Owner owner)
        {
            if (G.turn != owner || G.phase != GamePhase.Action) return false;

            int  needSch  = equip.equipSchCost;
            var  sch      = G.GetSch(owner);
            bool canPay   = needSch <= 0 || sch.Get(equip.equipSchType) >= needSch;
            if (!canPay) return false;

            var baseUnits = G.GetBase(owner).Where(u => u.type != CardType.Equipment).ToList();
            if (baseUnits.Count == 0) return false;

            var target = PromptTarget(baseUnits);
            if (target == null) return false;

            _cd.AttachEquipToUnit(equip, target, owner, paySchCost: true);
            return true;
        }

        // ─────────────────────────────────────────
        // 法术对决系统
        // ─────────────────────────────────────────

        /// <summary>
        /// 开始法术对决，等价原版 startSpellDuel。
        /// </summary>
        public void StartSpellDuel(int bfId, Owner attacker)
        {
            G.duelActive    = true;
            G.duelBf        = bfId;
            G.duelAttacker  = attacker;
            G.duelTurn      = attacker;  // 进攻方先手
            G.duelSkips     = 0;
        }

        /// <summary>
        /// 推进对决回合，等价原版 runDuelTurn。
        /// AI 回合：调用 OnAiDuelTurn；玩家回合：等待玩家输入（UI 层处理）。
        /// </summary>
        public void RunDuelTurn()
        {
            if (!G.duelActive || G.gameOver) return;
            if (G.duelTurn == Owner.Enemy)
                OnAiDuelTurn();
            // 玩家回合：由 UI 层通过 SkipDuel / ApplySpell 推进
        }

        /// <summary>
        /// 玩家跳过对决响应，等价原版 skipDuel。
        /// </summary>
        public void SkipDuel()
        {
            if (!G.duelActive || G.duelTurn != Owner.Player) return;
            G.duelSkips++;
            if (G.duelSkips >= 2)
            {
                EndDuel();
            }
            else
            {
                G.duelTurn = Owner.Enemy;
                RunDuelTurn();
            }
        }

        /// <summary>
        /// AI 跳过对决响应（内部调用，不检查 duelTurn）。
        /// </summary>
        public void AiSkipDuel()
        {
            G.duelSkips++;
            if (G.duelSkips >= 2)
            {
                EndDuel();
            }
            else
            {
                G.duelTurn = Owner.Player;
            }
        }

        /// <summary>
        /// 结束法术对决，等价原版 endDuel。
        /// 若战场有敌方单位 → 触发战斗；否则 → 征服（尚未征服时）。
        /// </summary>
        public void EndDuel()
        {
            int   bfId     = G.duelBf!.Value;
            Owner attacker = G.duelAttacker!.Value;

            G.duelActive   = false;
            G.duelBf       = null;
            G.duelAttacker = null;
            G.duelTurn     = null;
            G.duelSkips    = 0;

            var bf = G.bf[bfId - 1];
            var opUnits = attacker == Owner.Player ? bf.eU : bf.pU;

            if (opUnits.Count > 0)
            {
                _cr.TriggerCombat(bfId, attacker);
            }
            else
            {
                // 空战场征服
                if (bf.ctrl != attacker && !bf.conqDone)
                {
                    bf.ctrl     = attacker;
                    bf.conqDone = true;
                    _tm.AddScore(attacker, 1, "conquer", bfId);
                }
                else
                {
                    bf.ctrl = attacker;
                }
                OnDuelEnded(attacker);
            }
        }
    }
}
