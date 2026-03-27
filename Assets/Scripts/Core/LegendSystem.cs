using System;
using System.Collections.Generic;
using System.Linq;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 传奇事件上下文（等价原版 legend.js ctx 参数）。
    /// </summary>
    public class LegendEventCtx
    {
        public int? bfId;
    }

    /// <summary>
    /// 传奇技能类型。
    /// </summary>
    public enum AbilityType { Passive, Triggered, Active }

    /// <summary>
    /// 传奇技能数据定义（硬编码在 _abilityDefs，等价原版 KAISA_LEGEND.abilities / MASTERYI_LEGEND.abilities）。
    /// </summary>
    public class AbilityDef
    {
        public string id;
        public string name;
        public AbilityType type;
        public List<string> keywords = new();
        public string trigger;      // triggered 技能触发事件名
        public int cost;            // 法力费用
        public int schCost;
        public RuneType schType;
        public bool exhaust;        // 代价：休眠自身
        public bool once = true;    // 每回合限用1次（false = 无限）
    }

    /// <summary>
    /// 传奇系统，等价原版 legend.js 的全部逻辑：
    ///   LEGEND_EFFECTS / checkLegendPassives / triggerLegendEvent /
    ///   canUseLegendAbility / activateLegendAbility /
    ///   resetLegendAbilitiesForTurn /
    ///   aiLegendActionPhase / aiLegendDuelAction
    ///
    /// 纯 C# 类，不依赖 MonoBehaviour。
    /// </summary>
    public class LegendSystem
    {
        public readonly GameState G;
        private readonly TurnManager _tm;

        // ── 技能定义表（等价原版 KAISA_LEGEND.abilities / MASTERYI_LEGEND.abilities）──
        private static readonly Dictionary<string, List<AbilityDef>> _abilityDefs =
            new Dictionary<string, List<AbilityDef>>
            {
                ["kaisa"] = new List<AbilityDef>
                {
                    new AbilityDef
                    {
                        id      = "kaisa_void_sense",
                        name    = "虚空感知",
                        type    = AbilityType.Active,
                        keywords = new List<string> { "反应" },
                        cost    = 0,
                        exhaust = true,
                        once    = false    // 可多次激活（只要每次都满足 exhaust 条件）
                    },
                    new AbilityDef
                    {
                        id   = "kaisa_evolve",
                        name = "进化",
                        type = AbilityType.Passive
                    }
                },
                ["masteryi"] = new List<AbilityDef>
                {
                    new AbilityDef
                    {
                        id      = "masteryi_defend_buff",
                        name    = "独影剑鸣",
                        type    = AbilityType.Triggered,
                        trigger = "onCombatDefend"
                    }
                }
            };

        public LegendSystem(GameState g, TurnManager tm)
        {
            G   = g;
            _tm = tm;
        }

        // ────────────────────────────────────────────
        // 工具
        // ────────────────────────────────────────────

        /// <summary>
        /// 获取传奇的技能列表（静态定义，按 data.id 查表）。
        /// </summary>
        public static List<AbilityDef> GetAbilities(LegendInstance leg)
        {
            if (leg?.data == null) return new List<AbilityDef>();
            return _abilityDefs.TryGetValue(leg.data.id, out var list)
                ? list
                : new List<AbilityDef>();
        }

        // ────────────────────────────────────────────
        // 被动检查
        // ────────────────────────────────────────────

        /// <summary>
        /// 检查所有被动技能（等价原版 checkLegendPassives）。
        /// 在新单位入场、战斗结算后调用。
        /// </summary>
        public void CheckLegendPassives(Owner owner)
        {
            var leg = G.GetLeg(owner);
            if (leg == null) return;
            foreach (var ab in GetAbilities(leg))
            {
                if (ab.type == AbilityType.Passive)
                    RunEffect(ab.id, leg, owner, null);
            }
        }

        // ────────────────────────────────────────────
        // 触发型技能
        // ────────────────────────────────────────────

        /// <summary>
        /// 触发传奇事件钩子（等价原版 triggerLegendEvent）。
        /// eventType: "onCombatDefend" 等。
        /// </summary>
        public void TriggerLegendEvent(string eventType, Owner owner, LegendEventCtx ctx = null)
        {
            var leg = G.GetLeg(owner);
            if (leg == null) return;
            foreach (var ab in GetAbilities(leg))
            {
                if (ab.type == AbilityType.Triggered && ab.trigger == eventType)
                    RunEffect(ab.id, leg, owner, ctx);
            }
        }

        // ────────────────────────────────────────────
        // 主动技能
        // ────────────────────────────────────────────

        /// <summary>
        /// 检查主动技能可用性（等价原版 canUseLegendAbility）。
        /// </summary>
        public bool CanUseLegendAbility(Owner owner, AbilityDef ab)
        {
            if (ab == null || ab.type != AbilityType.Active) return false;
            var leg = G.GetLeg(owner);
            if (leg == null) return false;
            if (G.gameOver) return false;

            // 每回合限用1次检查
            if (ab.once && leg.usedThisTurn.TryGetValue(ab.id, out bool used) && used) return false;

            // 费用检查
            if (G.GetMana(owner) < ab.cost) return false;
            if (ab.schCost > 0 && G.GetSch(owner).Get(ab.schType) < ab.schCost) return false;

            // 休眠代价检查（虚空感知：自身已休眠则无法激活）
            if (ab.exhaust && leg.exhausted) return false;

            // 时机检查
            if (G.duelActive)
            {
                if (G.duelTurn != owner) return false;
                bool isFast = ab.keywords.Contains("迅捷") || ab.keywords.Contains("反应");
                if (!isFast) return false;
            }
            else
            {
                if (G.turn != owner || G.phase != GamePhase.Action) return false;
            }
            return true;
        }

        /// <summary>
        /// 激活传奇主动技能（等价原版 activateLegendAbility）。
        /// 返回 true 表示技能成功激活。
        /// </summary>
        public bool ActivateLegendAbility(Owner owner, string abilityId)
        {
            if (G.gameOver) return false;
            var leg = G.GetLeg(owner);
            if (leg == null) return false;
            var ab = GetAbilities(leg).FirstOrDefault(a => a.id == abilityId);
            if (ab == null || !CanUseLegendAbility(owner, ab)) return false;

            // 扣除费用
            G.SetMana(owner, G.GetMana(owner) - ab.cost);
            if (ab.schCost > 0) G.GetSch(owner).Spend(ab.schType, ab.schCost);

            // 标记已使用（once=true 限每回合1次；once=false 不限次）
            if (ab.once) leg.usedThisTurn[ab.id] = true;

            // 执行效果
            RunEffect(ab.id, leg, owner, null);
            _tm.CheckWin();
            return true;
        }

        // ────────────────────────────────────────────
        // 回合重置
        // ────────────────────────────────────────────

        /// <summary>
        /// 重置技能每回合使用标记（等价原版 resetLegendAbilitiesForTurn）。
        /// 在 StartTurn 阶段调用。
        /// </summary>
        public void ResetLegendAbilitiesForTurn(Owner owner)
        {
            var leg = G.GetLeg(owner);
            leg?.usedThisTurn.Clear();
        }

        // ────────────────────────────────────────────
        // AI 决策
        // ────────────────────────────────────────────

        /// <summary>
        /// AI 行动阶段使用传奇主动技能（等价原版 aiLegendActionPhase）。
        /// </summary>
        public bool AiLegendActionPhase()
        {
            var leg = G.eLeg;
            if (leg == null) return false;
            var best = GetAbilities(leg)
                .Where(ab => ab.type == AbilityType.Active && CanUseLegendAbility(Owner.Enemy, ab))
                .OrderByDescending(ab => AiLegendAbilityPriority(ab))
                .FirstOrDefault();
            if (best == null) return false;
            return ActivateLegendAbility(Owner.Enemy, best.id);
        }

        /// <summary>
        /// AI 对决中使用传奇迅捷/反应技能（等价原版 aiLegendDuelAction）。
        /// 使用后交换对决权。
        /// </summary>
        public bool AiLegendDuelAction()
        {
            var leg = G.eLeg;
            if (leg == null) return false;
            var best = GetAbilities(leg)
                .Where(ab => ab.type == AbilityType.Active && CanUseLegendAbility(Owner.Enemy, ab))
                .OrderByDescending(ab => AiLegendAbilityPriority(ab))
                .FirstOrDefault();
            if (best == null) return false;
            bool used = ActivateLegendAbility(Owner.Enemy, best.id);
            if (used && G.duelActive)
            {
                G.duelSkips = 0;
                G.duelTurn  = Owner.Player;
            }
            return used;
        }

        private static int AiLegendAbilityPriority(AbilityDef ab)
        {
            // 有战场伤害/眩晕/增益效果 → 3
            if (ab.id.Contains("stun") || ab.id.Contains("buff") || ab.id.Contains("damage")) return 3;
            // 迅捷/反应（对决期间可用）→ 2
            if (ab.keywords.Contains("迅捷") || ab.keywords.Contains("反应")) return 2;
            // 经济型（+符能/法力/抽牌）→ 1
            if (ab.id.Contains("sense") || ab.id.Contains("draw") || ab.id.Contains("mana")) return 1;
            return 0;
        }

        // ────────────────────────────────────────────
        // 效果分发（等价原版 LEGEND_EFFECTS 字典）
        // ────────────────────────────────────────────

        private void RunEffect(string effectId, LegendInstance leg, Owner owner, LegendEventCtx ctx)
        {
            switch (effectId)
            {
                case "kaisa_evolve":           EffectEvolve(leg, owner);             break;
                case "masteryi_defend_buff":   EffectMasteryiDefendBuff(leg, ctx);   break;
                case "kaisa_void_sense":       EffectKaisaVoidSense(leg, owner);      break;
            }
        }

        // ── 卡莎「进化」被动 ──
        // 条件：盟友集满4种不同关键词
        // 效果：升至等级2，永久+3/+3（仅触发一次）
        private void EffectEvolve(LegendInstance leg, Owner owner)
        {
            if (leg.evolved) return;

            var allies = _tm.GetAllUnitsForOwner(owner);
            var kwSet  = new HashSet<string>();
            foreach (var u in allies)
                if (u.keywords != null)
                    foreach (var kw in u.keywords)
                        kwSet.Add(kw);

            if (kwSet.Count < 4) return;

            leg.evolved    = true;
            leg.level      = 2;
            leg.currentAtk += 3;
            leg.maxHp      += 3;
            leg.currentHp   = Math.Min(leg.currentHp + 3, leg.maxHp);
        }

        // ── 无极剑圣「独影剑鸣」触发被动 ──
        // 条件：该战场上仅有1名友方单位在防守
        // 效果：该单位本回合战力+2
        private void EffectMasteryiDefendBuff(LegendInstance leg, LegendEventCtx ctx)
        {
            if (ctx?.bfId == null) return;
            int bfId = ctx.bfId.Value;
            if (bfId < 1 || bfId > G.bf.Length) return;

            // leg 属于哪方？通过遍历找到
            Owner owner = G.pLeg == leg ? Owner.Player : Owner.Enemy;
            var bf = G.bf[bfId - 1];
            var defenders = owner == Owner.Player ? bf.pU : bf.eU;
            if (defenders.Count != 1) return;

            var solo = defenders[0];
            if (solo.tb == null) solo.tb = new TurnBuffs { atk = 0 };
            solo.tb.atk += 2;
        }

        // ── 卡莎「虚空感知」主动·反应 ──
        // 代价：休眠自身
        // 效果：获得1点炽烈符能
        private void EffectKaisaVoidSense(LegendInstance leg, Owner owner)
        {
            // exhaust 已在 CanUseLegendAbility 中作为前置条件检查，此处直接执行
            leg.exhausted = true;
            G.GetSch(owner).Add(RuneType.Blazing, 1);
        }
    }
}
