using System.Collections.Generic;
using System.Linq;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 战斗结算，等价原版 combat.js 的：
    ///   triggerCombat / cleanDead / roleAtk / assignDmg
    ///
    /// 纯 C# 类，不依赖 MonoBehaviour。
    /// 战斗后触发器（战场牌能力 postCombatTriggers）在 P5 实现。
    /// triggerLegendEvent 调用（易大师独影剑鸣被动）在 P5 连接。
    /// </summary>
    public class CombatResolver
    {
        public readonly GameState G;
        private readonly TurnManager _tm;
        private readonly CardDeployer _deployer;
        private LegendSystem _legendSystem;

        private BattlefieldSystem _bfSystem;

        public CombatResolver(GameState g, TurnManager tm, CardDeployer deployer)
        {
            G         = g;
            _tm       = tm;
            _deployer = deployer;
        }

        /// <summary>注入传奇系统（P8）。</summary>
        public void SetLegendSystem(LegendSystem ls) => _legendSystem = ls;

        /// <summary>注入战场牌系统（P9）。</summary>
        public void SetBattlefieldSystem(BattlefieldSystem bfs) => _bfSystem = bfs;

        // ── 有效战力 = max(1, currentAtk + tb.atk) ──
        public static int EffAtk(CardInstance u)
            => System.Math.Max(1, u.currentAtk + (u.tb?.atk ?? 0));

        // ── 角色战力（含强攻/坚守加成）──
        /// <summary>
        /// 等价原版 roleAtk lambda：
        ///   atk(u) + strongAtkBonus（若【强攻】且为进攻方）
        ///          + guardBonus（若【坚守】且为防守方）
        /// </summary>
        public static int RoleAtk(CardInstance u, string role)
        {
            int b = 0;
            if (u.keywords != null)
            {
                if (role == "attacker" && u.keywords.Contains("强攻"))
                    b += (u.strongAtkBonus > 0 ? u.strongAtkBonus : 1);
                if (role == "defender" && u.keywords.Contains("坚守"))
                    b += (u.guardBonus > 0 ? u.guardBonus : 1);
            }
            return EffAtk(u) + b;
        }

        // ── 战斗结算主入口 ──
        /// <summary>
        /// 等价原版 triggerCombat(bfId, attacker)。
        /// 注：reckoner_arena 战场牌效果在 P5 实现；
        ///     postCombatTriggers（战场牌征服效果）在 P5 实现。
        /// </summary>
        public CombatResult TriggerCombat(int bfId, Owner attacker)
        {
            if (bfId < 1 || bfId > G.bf.Length) return CombatResult.BothDead;

            var bf    = G.bf[bfId - 1];
            var atkUs = attacker == Owner.Player ? bf.pU : bf.eU;
            var defUs = attacker == Owner.Player ? bf.eU : bf.pU;

            // ── P9：战场牌战斗开始效果（reckoner_arena）──
            _bfSystem?.OnCombatStart(bf, attacker);

            // ── P8：防守方传奇触发被动（独影剑鸣：1名盟友独守时+2战力）──
            Owner defOwner = G.Opponent(attacker);
            _legendSystem?.TriggerLegendEvent("onCombatDefend", defOwner, new LegendEventCtx { bfId = bfId });

            // ── 伤害计算（眩晕单位输出强制为 0）──
            int atkPow = atkUs.Sum(u => u.stunned ? 0 : RoleAtk(u, "attacker"));
            int defPow = defUs.Sum(u => u.stunned ? 0 : RoleAtk(u, "defender"));

            // ── 伤害分配（壁垒优先，同类按最低HP先吸收）──
            int atkOverflow = AssignDmg(atkPow, defUs);
            int defOverflow = AssignDmg(defPow, atkUs);

            // ── 压制：溢出伤害打到传奇 ──
            if (atkOverflow > 0 && atkUs.Any(u => u.keywords != null && u.keywords.Contains("压制")))
            {
                var oLeg = attacker == Owner.Player ? G.eLeg : G.pLeg;
                if (oLeg != null)
                    oLeg.currentHp = System.Math.Max(0, oLeg.currentHp - atkOverflow);
            }
            if (defOverflow > 0 && defUs.Any(u => u.keywords != null && u.keywords.Contains("压制")))
            {
                var oLeg = attacker == Owner.Player ? G.pLeg : G.eLeg;
                if (oLeg != null)
                    oLeg.currentHp = System.Math.Max(0, oLeg.currentHp - defOverflow);
            }

            // ── 累计伤害追踪（易大师「无极升华」被动条件）──
            G.pAllyDmgDealt += attacker == Owner.Player ? atkPow : defPow;
            G.eAllyDmgDealt += attacker == Owner.Enemy  ? atkPow : defPow;

            // ── 死亡清理（BF 侧 + 基地侧）──
            CleanDead(bfId);

            // ── P8：战斗后检查被动（卡莎进化触发点）──
            _legendSystem?.CheckLegendPassives(attacker);
            _legendSystem?.CheckLegendPassives(G.Opponent(attacker));

            // ── 规则 627.5：战斗结束后重置所有存活单位的标记伤害 ──
            var allSurvivors = new List<CardInstance>(G.pBase);
            allSurvivors.AddRange(G.eBase);
            foreach (var b in G.bf)
            {
                allSurvivors.AddRange(b.pU);
                allSurvivors.AddRange(b.eU);
            }
            foreach (var u in allSurvivors)
                u.currentHp = u.currentAtk;

            // ── 胜负判定 ──
            _tm.CheckWin();

            bool atkAlive = (attacker == Owner.Player ? bf.pU : bf.eU).Count > 0;
            bool defAlive = (attacker == Owner.Player ? bf.eU : bf.pU).Count > 0;

            CombatResult result;

            if (!atkAlive && !defAlive)
            {
                bf.ctrl = null;
                result  = CombatResult.BothDead;
            }
            else if (atkAlive && !defAlive)
            {
                // 进攻方全歼防守方
                Owner prevCtrl = bf.ctrl ?? attacker;
                if (bf.ctrl != attacker && !bf.conqDone)
                {
                    bf.ctrl     = attacker;
                    bf.conqDone = true;
                    _tm.AddScore(attacker, 1, "conquer", bfId);
                    // P9：战场牌征服效果
                    _bfSystem?.OnConquer(bf, attacker);
                    // P9：防守方失守效果
                    _bfSystem?.OnDefenseFailure(bf, G.Opponent(attacker));
                    result = CombatResult.Conquer;
                }
                else
                {
                    bf.ctrl = attacker;
                    result  = CombatResult.Conquer;
                }
            }
            else if (!atkAlive && defAlive)
            {
                // 防守方守住
                bf.ctrl = G.Opponent(attacker);
                result  = CombatResult.Defend;
            }
            else
            {
                // 双方均存活：进攻方撤回基地，防守方保持控制
                bf.ctrl = G.Opponent(attacker);
                var retUnits = attacker == Owner.Player ? bf.pU : bf.eU;
                var retBase  = G.GetBase(attacker);
                foreach (var u in retUnits)
                {
                    u.exhausted = true;
                    retBase.Add(u);
                }
                if (attacker == Owner.Player) bf.pU = new List<CardInstance>();
                else                          bf.eU = new List<CardInstance>();
                result = CombatResult.Draw;
            }

            return result;
        }

        // ── 伤害分配（壁垒优先，最低HP先吸收）──
        /// <summary>
        /// 等价原版 assignDmg lambda。
        /// 返回溢出伤害（dmgPool 未被完全吸收的部分）。
        /// </summary>
        private static int AssignDmg(int dmgPool, List<CardInstance> targets)
        {
            // 壁垒单位排前，同组内按当前HP升序（低HP先吸收）
            var order = targets
                .Where(u => u.keywords != null && u.keywords.Contains("壁垒") && u.currentHp > 0)
                .OrderBy(u => u.currentHp)
                .Concat(
                    targets
                        .Where(u => (u.keywords == null || !u.keywords.Contains("壁垒")) && u.currentHp > 0)
                        .OrderBy(u => u.currentHp))
                .ToList();

            foreach (var u in order)
            {
                if (dmgPool <= 0) break;
                if (u.currentHp <= 0) continue;
                int d = System.Math.Min(dmgPool, u.currentHp);
                u.currentHp -= d;
                dmgPool -= d;
            }

            return dmgPool; // 溢出伤害
        }

        // ── 战斗专用死亡清理（BF 指定侧 + 基地侧）──
        /// <summary>
        /// 等价原版 cleanDead(bfId)，同时也清理基地侧（溢出伤害可能波及基地）。
        /// </summary>
        public void CleanDead(int bfId)
        {
            if (bfId < 1 || bfId > G.bf.Length) return;
            var bf = G.bf[bfId - 1];

            CleanBFSide(bf, Owner.Player);
            CleanBFSide(bf, Owner.Enemy);

            // 基地侧也可能因 deathwish 链或溢出而存在死亡单位
            CleanBaseSide(Owner.Player);
            CleanBaseSide(Owner.Enemy);
        }

        private void CleanBFSide(BattlefieldState bf, Owner owner)
        {
            var slots        = owner == Owner.Player ? bf.pU : bf.eU;
            var ownerBase    = G.GetBase(owner);
            var ownerDiscard = G.GetDiscard(owner);

            // 规则 144.3：装备若出于任何原因进入战场，在清理时召回至控制者基地
            var misplacedEquip = slots.Where(u => u.type == CardType.Equipment).ToList();
            foreach (var eq in misplacedEquip) ownerBase.Add(eq);
            slots.RemoveAll(u => u.type == CardType.Equipment);

            var dead     = slots.Where(u => u.currentHp <= 0).ToList();
            var deadUids = new HashSet<int>(dead.Select(u => u.uid));

            foreach (var u in dead)
            {
                if (_deployer.TryDeathShield(u, ownerBase, ownerDiscard)) continue;

                if (u.attachedEquipments?.Count > 0)
                {
                    foreach (var eq in u.attachedEquipments) ownerDiscard.Add(eq);
                    u.attachedEquipments.Clear();
                }
                _deployer.TriggerDeathwish(u, owner);
                ResetUnit(u);
                ownerDiscard.Add(u);
            }

            slots.RemoveAll(u => deadUids.Contains(u.uid));
        }

        private void CleanBaseSide(Owner owner)
        {
            var baseZone     = G.GetBase(owner);
            var ownerDiscard = G.GetDiscard(owner);

            var dead = baseZone
                .Where(u => u.currentHp <= 0
                         && u.effect != "death_shield"
                         && u.type   != CardType.Equipment)
                .ToList();
            var deadUids = new HashSet<int>(dead.Select(u => u.uid));

            foreach (var u in dead)
            {
                if (u.attachedEquipments?.Count > 0)
                {
                    foreach (var eq in u.attachedEquipments) ownerDiscard.Add(eq);
                    u.attachedEquipments.Clear();
                }
                _deployer.TriggerDeathwish(u, owner);
                ResetUnit(u);
                ownerDiscard.Add(u);
            }

            baseZone.RemoveAll(u => deadUids.Contains(u.uid));
        }

        private static void ResetUnit(CardInstance u)
        {
            u.currentHp  = u.atk;
            u.currentAtk = u.atk;
            u.exhausted  = false;
            u.stunned    = false;
            u.tb         = new TurnBuffs { atk = 0 };
            u.buffToken  = false;
        }
    }

    /// <summary>
    /// 战斗结果枚举（对应原版四种结局）。
    /// </summary>
    public enum CombatResult
    {
        Conquer,   // 进攻方全歼防守方
        Defend,    // 防守方击退进攻
        Draw,      // 双方均存活，进攻方撤退
        BothDead   // 双方全灭
    }
}
