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
    /// P6 存根：法术施放（rally_call / balance_resolve / 其他法术）
    /// P8 存根：传奇主动技能（aiLegendActionPhase / aiLegendDuelAction）
    /// </summary>
    public class AIController
    {
        public readonly GameState G;
        private readonly TurnManager _tm;
        private readonly CardDeployer _cd;

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
        // 主行动循环
        // ─────────────────────────────────────────

        /// <summary>
        /// AI 主行动入口，等价原版 aiAction()。
        /// 每步通过 Schedule 异步步进；测试中注入同步调度器。
        ///
        /// 顺序：横置符文 → 出随从 → [P6: 法术] → [P8: 传奇技能] → 移动 → 结束回合
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

                // ── P6 存根：迎敌号令（rally_call）优先于出兵 ──
                // ── P6 存根：御衡守念（balance_resolve）──

                // ── 3. 出随从（按 aiCardValue 评分排序）──
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
                    // 优先出在保留法力范围内的牌；保留不了就出最高价值的
                    var chosen = units.FirstOrDefault(c => c.cost <= manaToSpend) ?? units[0];
                    // 急速单位以活跃状态入场（等价原版 exhausted = !keywords.includes('急速')）
                    bool hasHaste = chosen.keywords != null && chosen.keywords.Contains("急速");
                    _cd.DeployToBase(chosen, Owner.Enemy, enterActive: hasHaste);
                    Schedule(0.7f, AiAction);
                    return;
                }

                // ── P6 存根：出法术 ──
                // ── P8 存根：传奇主动技能 ──
            }

            // ── 4. 移动单位至战场 ──
            var active = G.eBase.Where(u => !u.exhausted && !u.stunned).ToList();
            if (active.Count > 0)
            {
                var decision = AiDecideMovement(active);
                if (decision != null)
                {
                    foreach (var mover in decision.Movers)
                        _cd.MoveUnit(mover, Owner.Enemy, decision.TargetBfId.ToString());

                    // P6：移动后会触发法术对决，此处 P5 直接结束回合
                    Schedule(0.4f, () => _tm.DoEndPhase());
                    return;
                }
            }

            // ── 5. 结束回合 ──
            Schedule(0.4f, () => _tm.DoEndPhase());
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
                        // 空战场
                        planSc = b.ctrl != Owner.Enemy ? 15f : 2f;
                        if (count > 1 && b.ctrl != Owner.Enemy) planSc -= 2f;
                    }
                    else
                    {
                        // 有敌方 → 战斗
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
                            // 会输
                            planSc = -3f;
                            if (boardAdv < -3f || scoreDiff <= -3) planSc += 6f;
                            if (boardAdv > 5f)                     planSc -= 3f;
                            if (oppScore >= GameState.WIN_SCORE - 2 && b.ctrl == Owner.Player)
                                planSc += 8f;
                        }
                    }

                    // 紧迫感
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

            // 分兵策略：双空战场且双方均未控制时，各派1名单位
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
        // 法术对决响应（P6 完整实现；P5 仅存根）
        // ─────────────────────────────────────────

        /// <summary>
        /// AI 对决响应，等价原版 aiDuelAction()。
        /// P5 只返回 false（跳过响应），P6 实现完整逻辑。
        /// </summary>
        public bool AiDuelAction()
        {
            if (!G.duelActive || G.gameOver) return false;
            // P6：反制法术 / buff / 眩晕逻辑
            return false;
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
