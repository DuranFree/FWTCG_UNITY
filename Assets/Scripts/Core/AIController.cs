using System;
using System.Collections.Generic;
using System.Linq;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// AI 行动决策，等价原版 ai.js 的：
    ///   aiAction / aiDecideMovement / aiCardValue / aiBoardScore /
    ///   aiEvalBattlefield / aiSimulateCombat / aiDuelAction（P6 完整实现）
    ///
    /// 纯 C# 类，不依赖 MonoBehaviour。
    /// 异步步进通过 Schedule 委托注入：
    ///   - 测试：(_, fn) => fn()（同步立即执行）
    ///   - Unity：(delay, fn) => StartCoroutine(WaitAndCall(delay, fn))
    ///
    /// P8 存根：传奇主动技能（AiLegendActionPhase / AiLegendDuelAction）
    /// </summary>
    public class AIController
    {
        public readonly GameState G;
        private readonly TurnManager    _tm;
        private readonly CardDeployer   _cd;
        private SpellSystem             _ss;   // 注入（P6+）

        /// <summary>
        /// 步进调度器：(延迟秒数, 回调)。
        /// 测试注入同步版本：(_, fn) => fn()。
        /// Unity 注入协程版本：(delay, fn) => StartCoroutine(WaitAndCall(delay, fn))。
        /// </summary>
        public Action<float, Action> Schedule;

        public AIController(GameState g, TurnManager tm, CardDeployer cd)
        {
            G        = g;
            _tm      = tm;
            _cd      = cd;
            Schedule = (_, fn) => fn();   // 默认同步（测试友好）
        }

        /// <summary>
        /// 注入 SpellSystem（P6 后必须调用，否则法术相关功能无效）。
        /// </summary>
        public void SetSpellSystem(SpellSystem ss) => _ss = ss;

        // ─────────────────────────────────────────
        // 评分工具
        // ─────────────────────────────────────────

        /// <summary>
        /// 单卡综合价值评分（等价原版 aiCardValue）。
        /// </summary>
        public static float AiCardValue(CardInstance card)
        {
            float baseAtk  = Math.Max(card.atk, 0);
            float baseCost = Math.Max(card.cost, 1);
            float score    = (baseAtk / baseCost) * 10f;
            var kw = card.keywords ?? new List<string>();
            if (kw.Contains("急速")) score += 4f;
            if (kw.Contains("壁垒")) score += 3f;
            if (kw.Contains("强攻")) score += 2f;
            if (kw.Contains("绝念")) score += 2f;
            if (kw.Contains("征服")) score += 1f;
            if (kw.Contains("鼓舞")) score += 1f;
            return score;
        }

        /// <summary>
        /// 全局局面评分（正=AI领先，负=AI落后），等价原版 aiBoardScore。
        /// </summary>
        public float AiBoardScore()
        {
            float scoreDiff = G.eScore - G.pScore;
            float handDiff  = G.eHand.Count - G.pHand.Count;
            float bfControl = G.bf.Count(b => b.ctrl == Owner.Enemy)
                            - G.bf.Count(b => b.ctrl == Owner.Player);
            float unitPow   = G.eBase.Sum(u => CombatResolver.EffAtk(u))
                            - G.pBase.Sum(u => CombatResolver.EffAtk(u));
            return scoreDiff * 3f + handDiff * 0.5f + bfControl * 2f + unitPow * 0.3f;
        }

        /// <summary>
        /// 评估单条战场态势。
        /// </summary>
        private BfEval AiEvalBattlefield(int bfIdx)
        {
            var b = G.bf[bfIdx];
            return new BfEval
            {
                MyPow      = b.eU.Sum(u => CombatResolver.EffAtk(u)),
                TheirPow   = b.pU.Sum(u => CombatResolver.EffAtk(u)),
                MyCount    = b.eU.Count,
                TheirCount = b.pU.Count,
                Ctrl       = b.ctrl
            };
        }

        /// <summary>
        /// 模拟派出 movers 后的战力对比。
        /// </summary>
        private CombatSim AiSimulateCombat(List<CardInstance> movers, int bfIdx)
        {
            var b         = G.bf[bfIdx];
            int myExist   = b.eU.Sum(u => CombatResolver.EffAtk(u));
            int myTotal   = movers.Sum(u => CombatResolver.EffAtk(u)) + myExist;
            int theirPow  = b.pU.Sum(u => CombatResolver.EffAtk(u));
            return new CombatSim
            {
                MyTotal  = myTotal,
                TheirPow = theirPow,
                WillWin  = myTotal > theirPow,
                Margin   = myTotal - theirPow
            };
        }

        /// <summary>
        /// 获取手中迅捷/反应法术的最低费用（用于保留法力）。
        /// </summary>
        private int AiMinReactiveCost()
        {
            var reactives = G.eHand
                .Where(c => c.keywords != null
                         && (c.keywords.Contains("反应") || c.keywords.Contains("迅捷"))
                         && c.type == CardType.Spell)
                .ToList();
            return reactives.Count == 0 ? 0 : reactives.Min(c => c.cost);
        }

        // ─────────────────────────────────────────
        // 法术评估 (P6)
        // ─────────────────────────────────────────

        /// <summary>
        /// 法术优先级（越高越优先），等价原版 aiSpellPriority。
        /// </summary>
        public static int AiSpellPriority(CardInstance spell)
        {
            switch (spell.effect)
            {
                case "rally_call":    return 100;
                case "balance_resolve": return 90;
                case "stun_manual":   return 80;
                case "buff7_manual":  return 75;
                case "buff5_manual":  return 70;
                case "buff2_draw":    return 65;
                case "buff1_solo":    return 60;
                default:              return 50;
            }
        }

        /// <summary>
        /// 是否值得在行动阶段主动施放此法术，等价原版 aiShouldPlaySpell。
        /// </summary>
        public bool AiShouldPlaySpell(CardInstance spell)
        {
            // 反应牌只留对决用
            if (spell.keywords?.Contains("反应") == true) return false;

            // 通用目标检查
            if (_ss != null)
            {
                var targets = _ss.GetSpellTargets(spell, Owner.Enemy);
                if (targets != null && targets.Count == 0) return false;
            }

            switch (spell.effect)
            {
                case "stun_manual":
                    return (_ss?.GetAllUnits(Owner.Player).Count(u => !u.stunned) ?? 0) > 0;
                case "buff5_manual":
                case "buff7_manual":
                    return (_ss?.GetAllUnits(Owner.Enemy).Count ?? 0) > 0;
                case "buff1_solo":
                    return (_ss?.GetAllUnits(Owner.Enemy).Count ?? 0) > 0;
                case "buff2_draw":
                    return (_ss?.GetAllUnits(Owner.Enemy).Count ?? 0) > 0
                        || (_ss?.GetAllUnits(Owner.Player).Count ?? 0) > 0;
                case "rally_call":
                {
                    int spellCost = spell.cost;
                    return G.eHand.Any(c =>
                        c.type != CardType.Spell && c.type != CardType.Equipment &&
                        c.cost <= (G.eMana - spellCost) && G.eBase.Count < 5);
                }
                case "balance_resolve":
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// AI 智能选择法术目标，等价原版 aiChooseSpellTarget。
        /// </summary>
        public int? AiChooseSpellTarget(CardInstance spell, Owner owner)
        {
            if (_ss == null) return null;
            var targets = _ss.GetSpellTargets(spell, owner);
            if (targets == null || targets.Count == 0) return null;

            switch (spell.effect)
            {
                case "stun_manual":
                {
                    // 优先眩晕战场最高攻击力的敌方单位
                    var bfEnemies = G.bf.SelectMany(b => b.pU)
                        .Where(u => targets.Contains(u.uid) && !u.stunned)
                        .OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                    if (bfEnemies != null) return bfEnemies.uid;
                    var baseEnemy = G.pBase.Where(u => targets.Contains(u.uid) && !u.stunned)
                        .OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                    if (baseEnemy != null) return baseEnemy.uid;
                    return targets[0];
                }
                case "buff5_manual":
                case "buff7_manual":
                {
                    // 优先给战场上己方攻击力最高的单位
                    var bfAlly = G.bf.SelectMany(b => b.eU)
                        .Where(u => targets.Contains(u.uid))
                        .OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                    if (bfAlly != null) return bfAlly.uid;
                    var baseAlly = G.eBase.Where(u => targets.Contains(u.uid))
                        .OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                    if (baseAlly != null) return baseAlly.uid;
                    return targets[0];
                }
                case "buff1_solo":
                {
                    // 优先给独守的己方单位
                    foreach (var b in G.bf)
                    {
                        if (b.eU.Count == 1 && b.pU.Count > 0 && targets.Contains(b.eU[0].uid))
                            return b.eU[0].uid;
                    }
                    var bfAlly = G.bf.SelectMany(b2 => b2.eU).Where(u => targets.Contains(u.uid)).FirstOrDefault();
                    if (bfAlly != null) return bfAlly.uid;
                    return targets[0];
                }
                case "buff2_draw":
                {
                    var bfAlly = G.bf.SelectMany(b => b.eU)
                        .Where(u => targets.Contains(u.uid))
                        .OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                    if (bfAlly != null) return bfAlly.uid;
                    var baseAlly = G.eBase.Where(u => targets.Contains(u.uid))
                        .OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                    if (baseAlly != null) return baseAlly.uid;
                    return targets[0];
                }
                default:
                    return targets[0];
            }
        }

        // ─────────────────────────────────────────
        // 主行动循环
        // ─────────────────────────────────────────

        /// <summary>
        /// AI 主行动入口，等价原版 aiAction()。
        /// 每步通过 Schedule 异步步进；测试中注入同步调度器。
        ///
        /// 顺序：横置符文 → [迎敌号令] → [御衡守念] → 出随从 → [法术] → [P8: 传奇技能] → 移动 → 结束回合
        /// </summary>
        public void AiAction()
        {
            if (G.gameOver || G.turn != Owner.Enemy || G.duelActive) return;

            // ── 1. 横置全部符文获取法力 ──
            foreach (var r in G.eRunes)
            {
                if (!r.tapped) { r.tapped = true; G.eMana++; }
            }

            // ── 2. 卡牌锁定检查 ──
            if (G.cardLockTarget != Owner.Enemy)
            {
                int reactiveMinCost = AiMinReactiveCost();
                int manaToSpend = reactiveMinCost > 0
                    ? Math.Max(0, G.eMana - reactiveMinCost)
                    : G.eMana;

                // ── 3. 优先施放迎敌号令（出兵前）──
                if (_ss != null)
                {
                    var rallySpell = G.eHand.FirstOrDefault(c =>
                        c.effect == "rally_call" && c.type == CardType.Spell &&
                        c.cost <= G.eMana &&
                        G.eSch.Get(c.schType)  >= c.schCost &&
                        G.eSch.Get(c.schType2) >= c.schCost2);
                    if (rallySpell != null && AiShouldPlaySpell(rallySpell))
                    {
                        G.eMana -= rallySpell.cost;
                        if (rallySpell.schCost > 0) G.eSch.Spend(rallySpell.schType, rallySpell.schCost);
                        G.eHand.Remove(rallySpell);
                        G.cardsPlayedThisTurn++;
                        G.eDiscard.Add(rallySpell);
                        _ss.ApplySpell(rallySpell, Owner.Enemy, null);
                        Schedule(0.7f, AiAction);
                        return;
                    }

                    // ── 4. 御衡守念（抽牌+符文，越早越好）──
                    var balanceSpell = G.eHand.FirstOrDefault(c =>
                    {
                        if (c.effect != "balance_resolve") return false;
                        int eff = _ss.GetEffectiveCost(c, Owner.Enemy);
                        return c.type == CardType.Spell && eff <= G.eMana &&
                               G.eSch.Get(c.schType)  >= c.schCost &&
                               G.eSch.Get(c.schType2) >= c.schCost2;
                    });
                    if (balanceSpell != null)
                    {
                        int effCost = _ss.GetEffectiveCost(balanceSpell, Owner.Enemy);
                        G.eMana -= effCost;
                        if (balanceSpell.schCost  > 0) G.eSch.Spend(balanceSpell.schType,  balanceSpell.schCost);
                        if (balanceSpell.schCost2 > 0) G.eSch.Spend(balanceSpell.schType2, balanceSpell.schCost2);
                        G.eHand.Remove(balanceSpell);
                        G.cardsPlayedThisTurn++;
                        G.eDiscard.Add(balanceSpell);
                        _ss.ApplySpell(balanceSpell, Owner.Enemy, null);
                        Schedule(0.7f, AiAction);
                        return;
                    }
                }

                // ── 5a. 部署装备（P7）──
                if (_ss != null)
                {
                    var equips = G.eHand
                        .Where(c => c.type == CardType.Equipment
                                 && c.cost <= G.eMana
                                 && G.eSch.Get(c.schType)  >= c.schCost
                                 && G.eSch.Get(c.schType2) >= c.schCost2
                                 && G.eBase.Count < 5)
                        .ToList();

                    if (equips.Count > 0)
                    {
                        var eq = equips[0];
                        // AI 装备目标：选基地最高战力单位
                        _ss.PromptTarget = candidateList =>
                            candidateList.OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                        var deployed = _cd.DeployToBase(eq, Owner.Enemy);
                        // 若基地有非装备单位，立即附着（ApplySpell 内通过 PromptTarget 处理）
                        _ss.ApplySpell(deployed, Owner.Enemy, null);
                        Schedule(0.7f, AiAction);
                        return;
                    }
                }

                // ── 5b. 出随从（按 aiCardValue 评分排序）──
                var units = G.eHand
                    .Where(c => c.type != CardType.Spell
                             && c.type != CardType.Equipment
                             && (c.keywords == null || !c.keywords.Contains("反应"))
                             && c.cost <= G.eMana
                             && G.GetSch(Owner.Enemy).Get(c.schType)  >= c.schCost
                             && G.GetSch(Owner.Enemy).Get(c.schType2) >= c.schCost2)
                    .OrderByDescending(c => AiCardValue(c))
                    .ToList();

                if (units.Count > 0 && G.eBase.Count < 5)
                {
                    var chosen = units.FirstOrDefault(c => c.cost <= manaToSpend) ?? units[0];
                    bool hasHaste = chosen.keywords != null && chosen.keywords.Contains("急速");
                    _cd.DeployToBase(chosen, Owner.Enemy, enterActive: hasHaste || G.eRallyActive);
                    Schedule(0.7f, AiAction);
                    return;
                }

                // ── 6. 施放法术 (P6) ──
                if (_ss != null)
                {
                    var playableSpells = G.eHand
                        .Where(c =>
                        {
                            if (c.type != CardType.Spell) return false;
                            int eff = _ss.GetEffectiveCost(c, Owner.Enemy);
                            return eff <= G.eMana &&
                                   G.eSch.Get(c.schType)  >= c.schCost &&
                                   G.eSch.Get(c.schType2) >= c.schCost2 &&
                                   AiShouldPlaySpell(c);
                        })
                        .OrderByDescending(c => AiSpellPriority(c))
                        .ToList();

                    if (playableSpells.Count > 0)
                    {
                        var sp      = playableSpells[0];
                        int effCost = _ss.GetEffectiveCost(sp, Owner.Enemy);
                        G.eMana -= effCost;
                        if (sp.schCost  > 0) G.eSch.Spend(sp.schType,  sp.schCost);
                        if (sp.schCost2 > 0) G.eSch.Spend(sp.schType2, sp.schCost2);
                        G.eHand.Remove(sp);
                        G.cardsPlayedThisTurn++;
                        G.eDiscard.Add(sp);
                        // 智能选择目标
                        _ss.PromptTarget = candidateList =>
                        {
                            // AI 对多段法术：选攻击力最高的
                            return candidateList.OrderByDescending(u => CombatResolver.EffAtk(u)).FirstOrDefault();
                        };
                        int? targetUid = AiChooseSpellTarget(sp, Owner.Enemy);
                        _ss.ApplySpell(sp, Owner.Enemy, targetUid);
                        Schedule(0.7f, AiAction);
                        return;
                    }
                }

                // ── P8 存根：传奇主动技能 ──
            }

            // ── 7. 移动单位至战场 ──
            var active = G.eBase.Where(u => !u.exhausted && !u.stunned).ToList();
            if (active.Count > 0)
            {
                var decision = AiDecideMovement(active);
                if (decision != null)
                {
                    foreach (var mover in decision.Movers)
                        _cd.MoveUnit(mover, Owner.Enemy, decision.TargetBfId.ToString());

                    // P6：移动后会触发法术对决，此处由 MoveUnit 调用后 UI 层处理
                    Schedule(0.4f, () => _tm.DoEndPhase());
                    return;
                }
            }

            // ── 8. 结束回合 ──
            Schedule(0.4f, () => _tm.DoEndPhase());
        }

        // ─────────────────────────────────────────
        // 法术对决响应 (P6)
        // ─────────────────────────────────────────

        /// <summary>
        /// AI 法术对决响应，等价原版 aiDuelAction（增强版）。
        /// 策略：反制 > 眩晕（落后时）> 增益（落后时）> 其他迅捷法术 > 迅捷单位 > [P8: 传奇技能] > 跳过
        /// </summary>
        public void AiDuelAction()
        {
            if (!G.duelActive || G.gameOver || _ss == null) { _ss?.AiSkipDuel(); return; }

            int bfId = G.duelBf!.Value;
            var bf   = G.bf[bfId - 1];

            int myPow    = bf.eU.Sum(u => u.stunned ? 0 : CombatResolver.EffAtk(u));
            int theirPow = bf.pU.Sum(u => u.stunned ? 0 : CombatResolver.EffAtk(u));
            int powDiff  = myPow - theirPow;

            var fastCards = G.eHand.Where(c =>
                c.type != CardType.Equipment &&
                c.cost <= G.eMana &&
                G.eSch.Get(c.schType)  >= c.schCost &&
                G.eSch.Get(c.schType2) >= c.schCost2 &&
                c.keywords != null &&
                (c.keywords.Contains("迅捷") || c.keywords.Contains("反应"))).ToList();

            var counterSpells = fastCards.Where(c =>
                c.type == CardType.Spell &&
                (c.effect == "counter_cost4" || c.effect == "counter_any" || c.effect == "negate_spell")).ToList();
            var buffSpells = fastCards.Where(c =>
                c.type == CardType.Spell &&
                (c.effect == "buff1_solo" || c.effect == "buff2_draw" ||
                 c.effect == "buff5_manual" || c.effect == "buff7_manual")).ToList();
            var stunSpells = fastCards.Where(c => c.type == CardType.Spell && c.effect == "stun_manual").ToList();
            var otherSpells = fastCards.Where(c =>
                c.type == CardType.Spell &&
                !counterSpells.Contains(c) && !buffSpells.Contains(c) && !stunSpells.Contains(c)).ToList();
            var fastUnits = fastCards.Where(c => c.type != CardType.Spell).ToList();

            // ── 情况0：反制对手法术 ──
            if (counterSpells.Count > 0 && G.lastPlayerSpellCost > 0)
            {
                var canCounter = counterSpells.FirstOrDefault(c =>
                    c.effect == "counter_any" || c.effect == "negate_spell" ||
                    (c.effect == "counter_cost4" && G.lastPlayerSpellCost <= 4));
                if (canCounter != null)
                {
                    PlayFastCard(canCounter, Owner.Enemy, null);
                    G.lastPlayerSpellCost = 0;
                    G.duelSkips   = 0;
                    G.duelTurn    = Owner.Player;
                    return;
                }
            }

            // ── 情况1：落后时眩晕 ──
            if (powDiff < 0 && stunSpells.Count > 0 && bf.pU.Count > 0)
            {
                var stun   = stunSpells[0];
                var target = bf.pU.Where(u => !u.stunned)
                               .OrderByDescending(u => CombatResolver.EffAtk(u))
                               .FirstOrDefault();
                PlayFastCard(stun, Owner.Enemy, target?.uid);
                G.duelSkips = 0; G.duelTurn = Owner.Player;
                return;
            }

            // ── 情况2：落后时增益 ──
            if (powDiff < 0 && buffSpells.Count > 0 && bf.eU.Count > 0)
            {
                var best = buffSpells.OrderByDescending(c => AiSpellPriority(c)).First();
                int? tUid = AiChooseSpellTarget(best, Owner.Enemy);
                PlayFastCard(best, Owner.Enemy, tUid);
                G.duelSkips = 0; G.duelTurn = Owner.Player;
                return;
            }

            // ── 情况3：小幅领先时用低费增益巩固 ──
            if (powDiff > 0 && powDiff <= 3 && buffSpells.Count > 0 && bf.eU.Count > 0)
            {
                var cheapBuff = buffSpells.Where(c => c.cost <= 2)
                                          .OrderBy(c => c.cost).FirstOrDefault();
                if (cheapBuff != null)
                {
                    int? tUid = AiChooseSpellTarget(cheapBuff, Owner.Enemy);
                    PlayFastCard(cheapBuff, Owner.Enemy, tUid);
                    G.duelSkips = 0; G.duelTurn = Owner.Player;
                    return;
                }
            }

            // ── 情况4：其他迅捷法术 ──
            if (otherSpells.Count > 0)
            {
                var sp    = otherSpells[0];
                int? tUid = AiChooseSpellTarget(sp, Owner.Enemy);
                PlayFastCard(sp, Owner.Enemy, tUid);
                G.duelSkips = 0; G.duelTurn = Owner.Player;
                return;
            }

            // ── 情况5：迅捷单位 ──
            if (fastUnits.Count > 0 && G.eBase.Count < 5)
            {
                var u = fastUnits[0];
                G.eMana -= u.cost;
                if (u.schCost > 0) G.eSch.Spend(u.schType, u.schCost);
                G.eHand.Remove(u);
                u.exhausted = !(u.keywords?.Contains("急速") == true);
                G.eBase.Add(u);
                G.duelSkips = 0; G.duelTurn = Owner.Player;
                return;
            }

            // ── P8 存根：传奇迅捷技能 ──

            // ── 跳过响应 ──
            _ss.AiSkipDuel();
        }

        private void PlayFastCard(CardInstance card, Owner owner, int? targetUid)
        {
            int effCost = _ss != null ? _ss.GetEffectiveCost(card, owner) : card.cost;
            G.eMana -= effCost;
            if (card.schCost  > 0) G.GetSch(owner).Spend(card.schType,  card.schCost);
            if (card.schCost2 > 0) G.GetSch(owner).Spend(card.schType2, card.schCost2);
            G.eHand.Remove(card);
            G.GetDiscard(owner).Add(card);
            _ss?.ApplySpell(card, owner, targetUid);
        }

        // ─────────────────────────────────────────
        // 反应时点检查 (P6)
        // ─────────────────────────────────────────

        /// <summary>
        /// 玩家施法后 AI 检查是否有可响应的迅捷/反应牌。
        /// </summary>
        public void AiCheckReactionPlay()
        {
            if (_ss == null || G.duelActive || G.gameOver) return;
            var fastCards = G.eHand.Where(c =>
                (c.keywords?.Contains("反应") == true || c.keywords?.Contains("迅捷") == true) &&
                c.type != CardType.Equipment &&
                c.cost <= G.eMana &&
                G.eSch.Get(c.schType)  >= c.schCost &&
                G.eSch.Get(c.schType2) >= c.schCost2).ToList();
            if (fastCards.Count == 0) return;
            var best = fastCards.OrderByDescending(c => AiSpellPriority(c)).First();
            int? tUid = AiChooseSpellTarget(best, Owner.Enemy);
            G.eMana -= best.cost;
            if (best.schCost > 0) G.eSch.Spend(best.schType, best.schCost);
            G.eHand.Remove(best);
            G.eDiscard.Add(best);
            _ss.ApplySpell(best, Owner.Enemy, tUid);
        }

        // ─────────────────────────────────────────
        // 移动决策
        // ─────────────────────────────────────────

        /// <summary>
        /// 选择最优移动目标，等价原版 aiDecideMovement。
        /// 返回 null 表示无需移动。
        /// </summary>
        public MoveDecision AiDecideMovement(List<CardInstance> active)
        {
            var sorted    = active.OrderByDescending(u => CombatResolver.EffAtk(u)).ToList();
            float boardAdv = AiBoardScore();
            int myScore   = G.eScore;
            int oppScore  = G.pScore;
            int scoreDiff = myScore - oppScore;

            MoveDecision bestPlan  = null;
            float        bestScore = -999f;

            for (int i = 0; i < G.bf.Length; i++)
            {
                var b        = G.bf[i];
                var eval     = AiEvalBattlefield(i);
                int maxSlots = GameState.MAX_BF_UNITS - eval.MyCount;
                if (maxSlots <= 0) continue;

                for (int count = 1; count <= Math.Min(sorted.Count, maxSlots); count++)
                {
                    var movers    = sorted.Take(count).ToList();
                    var sim       = AiSimulateCombat(movers, i);
                    float planSc  = 0f;

                    if (eval.TheirCount == 0)
                    {
                        planSc = b.ctrl != Owner.Enemy ? 15f : 2f;
                        if (count > 1 && b.ctrl != Owner.Enemy) planSc -= 2f;
                    }
                    else
                    {
                        if (sim.WillWin)
                        {
                            planSc = 12f + sim.Margin;
                            if (b.ctrl != Owner.Enemy) planSc += 3f;
                        }
                        else if (sim.MyTotal == sim.TheirPow)
                        {
                            planSc = 1f;
                            if (b.ctrl == Owner.Player && scoreDiff < 0) planSc += 5f;
                        }
                        else
                        {
                            planSc = -3f;
                            if (boardAdv < -3f || scoreDiff <= -3) planSc += 6f;
                            if (boardAdv > 5f)                     planSc -= 3f;
                            if (oppScore >= GameState.WIN_SCORE - 2 && b.ctrl == Owner.Player)
                                planSc += 8f;
                        }
                    }

                    if (myScore  >= GameState.WIN_SCORE - 2)                                     planSc += 3f;
                    if (oppScore >= GameState.WIN_SCORE - 2 && b.ctrl == Owner.Player && eval.TheirCount > 0)
                        planSc += 5f;

                    if (planSc > bestScore)
                    {
                        bestScore = planSc;
                        bestPlan  = new MoveDecision { Movers = movers, TargetBfId = b.id };
                    }
                }
            }

            // 分兵策略
            if (sorted.Count >= 2)
            {
                var bf0 = G.bf[0]; var bf1 = G.bf[1];
                var e0  = AiEvalBattlefield(0);
                var e1  = AiEvalBattlefield(1);
                if (e0.TheirCount == 0 && e1.TheirCount == 0 &&
                    bf0.ctrl != Owner.Enemy && bf1.ctrl != Owner.Enemy &&
                    e0.MyCount < 2 && e1.MyCount < 2)
                {
                    const float splitSc = 25f;
                    if (splitSc > bestScore)
                    {
                        bestPlan = new MoveDecision
                        {
                            Movers     = new List<CardInstance> { sorted[0] },
                            TargetBfId = bf0.id
                        };
                    }
                }
            }

            return bestPlan;
        }

        // ─────────────────────────────────────────
        // 法术对决响应（P6 完整实现）
        // ─────────────────────────────────────────

        /// <summary>
        /// AI 对决响应入口（供 SpellSystem.OnAiDuelTurn 调用）。
        /// </summary>
        public bool AiDuelAction_Legacy()
        {
            if (!G.duelActive || G.gameOver) return false;
            AiDuelAction();
            return true;
        }
    }

    // ─────────────────────────────────────────
    // 辅助数据结构
    // ─────────────────────────────────────────

    public class MoveDecision
    {
        public List<CardInstance> Movers = new();
        public int TargetBfId;
    }

    public class BfEval
    {
        public int    MyPow, TheirPow, MyCount, TheirCount;
        public Owner? Ctrl;
    }

    public class CombatSim
    {
        public int  MyTotal, TheirPow, Margin;
        public bool WillWin;
    }
}
