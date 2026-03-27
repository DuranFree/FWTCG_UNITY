using System.Collections.Generic;
using System.Linq;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 出牌 & 单位操作，等价原版 spell.js 的：
    ///   canPlay / getEffectiveCost / deployToBase / deployToBF /
    ///   moveUnit / removeUnitFromField / dealDamage /
    ///   cleanDeadAll / tryDeathShield / onSummon / triggerDeathwish
    ///
    /// 纯 C# 类，不依赖 MonoBehaviour；异步提示（急速可选费用、先见机甲预知）在 P5 UI 层实现。
    /// </summary>
    public class CardDeployer
    {
        public readonly GameState G;
        private readonly TurnManager _tm;
        private LegendSystem _legendSystem;
        private BattlefieldSystem _bfSystem;

        public CardDeployer(GameState g, TurnManager tm)
        {
            G   = g;
            _tm = tm;
        }

        /// <summary>注入传奇系统（P8）。</summary>
        public void SetLegendSystem(LegendSystem ls) => _legendSystem = ls;

        /// <summary>注入战场牌系统（P9）。</summary>
        public void SetBattlefieldSystem(BattlefieldSystem bfs) => _bfSystem = bfs;

        // ── GetEffectiveCost: 计算有效费用 ──
        /// <summary>
        /// 御衡守念：对手距胜利≤3时费用-2（最低0）。其余卡牌直接返回 cost。
        /// </summary>
        public int GetEffectiveCost(CardInstance c, Owner owner)
        {
            int cost = c.cost;
            if (c.effect == "balance_resolve")
            {
                int oppScore = G.Opponent(owner) == Owner.Player ? G.pScore : G.eScore;
                if (oppScore >= GameState.WIN_SCORE - 3)
                    cost = System.Math.Max(0, cost - 2);
            }
            return cost;
        }

        // ── CanPlay: 出牌合法性检查 ──
        /// <summary>
        /// 检查费用（法力 + 双符能）、回合/阶段时机、卡牌锁定、对决时机限制。
        /// 法术目标合法性由调用方额外检查（P6 UI 层）。
        /// </summary>
        public bool CanPlay(CardInstance c, Owner owner)
        {
            if (G.gameOver) return false;
            if (G.cardLockTarget == owner) return false;

            int mana    = owner == Owner.Player ? G.pMana : G.eMana;
            int effCost = GetEffectiveCost(c, owner);
            if (mana < effCost) return false;

            var sch = G.GetSch(owner);
            if (c.schCost > 0 && sch.Get(c.schType)  < c.schCost)  return false;
            if (c.schCost2 > 0 && sch.Get(c.schType2) < c.schCost2) return false;

            if (G.duelActive)
            {
                if (G.duelTurn != owner) return false;
                bool isFast = c.keywords != null &&
                              (c.keywords.Contains("迅捷") || c.keywords.Contains("反应"));
                return isFast;
            }

            return G.turn == owner && G.phase == GamePhase.Action;
        }

        // ── DeployToBase: 手牌随从/装备 → 基地 ──
        /// <summary>
        /// 扣除法力/符能，从手牌移除，mk() 新单位，设置疲惫状态，触发 onSummon。
        /// enterActive=true：以活跃状态进场（急速付费后由 UI 层传入 true）。
        /// </summary>
        public CardInstance DeployToBase(CardInstance card, Owner owner, bool enterActive = false)
        {
            SpendCosts(card, owner);
            RemoveFromHand(card, owner);
            G.cardsPlayedThisTurn++;

            var unit = card.Mk();

            bool rallyActive = owner == Owner.Player ? G.pRallyActive : G.eRallyActive;
            unit.exhausted = !(enterActive || rallyActive);

            G.LastDeployedUid = unit.uid;
            G.GetBase(owner).Add(unit);
            OnSummon(unit, owner);
            // P8：入场后检查被动（卡莎进化触发点）
            _legendSystem?.CheckLegendPassives(owner);
            _tm.CheckWin();
            return unit;
        }

        // ── DeployToBF: 手牌随从 → 战场 ──
        /// <summary>
        /// 同 DeployToBase，但目标是战场槽位。
        /// 注：部署到战场后的法术对决触发（startSpellDuel）在 P6 UI 层调用。
        /// </summary>
        public CardInstance DeployToBF(CardInstance card, Owner owner, int bfId,
                                       bool enterActive = false)
        {
            if (bfId < 1 || bfId > G.bf.Length) return null;
            // P9：rockfall_path 禁止手牌直接出牌到此战场
            if (_bfSystem != null && !_bfSystem.CanDeployToBF(bfId, owner)) return null;
            var bf = G.bf[bfId - 1];

            SpendCosts(card, owner);
            RemoveFromHand(card, owner);
            G.cardsPlayedThisTurn++;

            var unit = card.Mk();

            bool rallyActive = owner == Owner.Player ? G.pRallyActive : G.eRallyActive;
            unit.exhausted = !(enterActive || rallyActive);

            G.LastDeployedUid = unit.uid;
            (owner == Owner.Player ? bf.pU : bf.eU).Add(unit);
            OnSummon(unit, owner);
            // P8：入场后检查被动（卡莎进化触发点）
            _legendSystem?.CheckLegendPassives(owner);
            _tm.CheckWin();
            return unit;
        }

        // ── MoveUnit: 单位在基地↔战场之间移动 ──
        /// <summary>
        /// 先从原位置移除，再添加到目标位置。
        /// toLoc = "base" 或战场 ID 字符串（"1" / "2"）。
        /// 移动触发的法术对决在 P6 UI 层处理（noTrigger 参数暂不需要）。
        /// </summary>
        public void MoveUnit(CardInstance unit, Owner owner, string toLoc)
        {
            // 规则144.2：装备不可进入战场
            if (unit.type == CardType.Equipment && toLoc != "base")
                toLoc = "base";

            // P9：vile_throat_nest 禁止单位移回基地
            if (toLoc == "base" && _bfSystem != null && !_bfSystem.CanMoveToBase(unit, owner))
                return;

            // P9：战场牌离开效果（back_alley_bar）
            if (toLoc == "base" || int.TryParse(toLoc, out _))
            {
                foreach (var b in G.bf)
                {
                    var slots = owner == Owner.Player ? b.pU : b.eU;
                    if (slots.Contains(unit))
                    {
                        _bfSystem?.OnUnitLeaveBF(b, unit, owner);
                        break;
                    }
                }
            }

            RemoveUnitFromField(unit, owner);

            if (toLoc == "base")
            {
                G.GetBase(owner).Add(unit);
            }
            else
            {
                if (int.TryParse(toLoc, out int bfId) && bfId >= 1 && bfId <= G.bf.Length)
                {
                    var bf = G.bf[bfId - 1];
                    (owner == Owner.Player ? bf.pU : bf.eU).Add(unit);
                    // P9：战场牌进入效果（trifarian_warcamp）
                    _bfSystem?.OnUnitEnterBF(bf, unit, owner);
                }
            }

            _tm.CheckWin();
        }

        // ── RemoveUnitFromField: 从场上移除单位（不摧毁）──
        public void RemoveUnitFromField(CardInstance unit, Owner owner)
        {
            var baseZone = G.GetBase(owner);
            int idx = baseZone.IndexOf(unit);
            if (idx >= 0) { baseZone.RemoveAt(idx); return; }

            foreach (var b in G.bf)
            {
                var slots = owner == Owner.Player ? b.pU : b.eU;
                int i = slots.IndexOf(unit);
                if (i >= 0) { slots.RemoveAt(i); return; }
            }
        }

        // ── DealDamage: 造成伤害（含传奇路径）──
        /// <summary>
        /// 对普通单位或传奇造成伤害，伤害后调用 CleanDeadAll。
        /// </summary>
        public void DealDamage(CardInstance target, int dmg, Owner targetOwner,
                               bool isLegend = false)
        {
            if (isLegend)
            {
                var leg = G.GetLeg(targetOwner);
                if (leg != null)
                    leg.currentHp = System.Math.Max(0, leg.currentHp - dmg);
            }
            else
            {
                target.currentHp = System.Math.Max(0, target.currentHp - dmg);
            }
            CleanDeadAll();
        }

        // ── TryDeathShield: 死亡护盾（守护天使附着 + 中娅沙漏基地）──
        /// <summary>
        /// 优先检查单位附着的守护天使（guardian_equip），其次检查基地中的中娅沙漏（death_shield）。
        /// 两者效果相同：摧毁装备，将单位以休眠状态召回基地，重置伤害。
        /// </summary>
        public bool TryDeathShield(CardInstance dying,
                                   List<CardInstance> ownerBase,
                                   List<CardInstance> ownerDiscard)
        {
            // 1. 附着的守护天使（guardian_equip）
            if (dying.attachedEquipments != null)
            {
                int guardianIdx = dying.attachedEquipments.FindIndex(e => e.effect == "guardian_equip");
                if (guardianIdx >= 0)
                {
                    var guardian = dying.attachedEquipments[guardianIdx];
                    dying.attachedEquipments.RemoveAt(guardianIdx);
                    ownerDiscard.Add(guardian);

                    dying.currentHp  = dying.currentAtk;  // 重置伤害（保留永久 buff）
                    dying.exhausted  = true;
                    dying.stunned    = false;
                    dying.tb         = new TurnBuffs { atk = 0 };
                    ownerBase.Add(dying);
                    return true;
                }
            }

            // 2. 基地中的中娅沙漏（death_shield）
            int shieldIdx = ownerBase.FindIndex(u => u.effect == "death_shield");
            if (shieldIdx < 0) return false;

            var shield = ownerBase[shieldIdx];
            ownerBase.RemoveAt(shieldIdx);
            ownerDiscard.Add(shield);

            dying.currentHp  = dying.currentAtk;  // 重置伤害（保留永久 buff）
            dying.exhausted  = true;
            dying.stunned    = false;
            dying.tb         = new TurnBuffs { atk = 0 };
            ownerBase.Add(dying);
            return true;
        }

        // ── AttachEquipToUnit: 将基地中的装备附着到单位（激活装配异能）──
        /// <summary>
        /// 从基地移除装备，附着到目标单位，应用 atkBonus 并支付 equipSchCost。
        /// 等价原版 activateEquipAbility 的核心结算逻辑。
        /// </summary>
        public void AttachEquipToUnit(CardInstance equip, CardInstance target,
                                      Owner owner, bool paySchCost = true)
        {
            var baseZone = G.GetBase(owner);
            var sch      = G.GetSch(owner);

            if (paySchCost && equip.equipSchCost > 0)
                sch.Spend(equip.equipSchType, equip.equipSchCost);

            baseZone.Remove(equip);

            if (target.attachedEquipments == null)
                target.attachedEquipments = new System.Collections.Generic.List<CardInstance>();
            target.attachedEquipments.Add(equip);

            if (equip.atkBonus > 0)
            {
                target.atk        += equip.atkBonus;
                target.currentAtk += equip.atkBonus;
                target.currentHp  += equip.atkBonus;  // HP = atk，一并提升
            }
            if (equip.effect == "trinity_equip")
                target.trinityEquipped = true;
        }

        // ── CleanDeadAll: 全局死亡清理 ──
        public void CleanDeadAll()
        {
            foreach (var bf in G.bf)
            {
                CleanBFSide(bf, Owner.Player);
                CleanBFSide(bf, Owner.Enemy);
            }
            CleanBaseSide(Owner.Player);
            CleanBaseSide(Owner.Enemy);
        }

        private void CleanBFSide(BattlefieldState bf, Owner owner)
        {
            var slots      = owner == Owner.Player ? bf.pU : bf.eU;
            var ownerBase  = G.GetBase(owner);
            var ownerDiscard = G.GetDiscard(owner);

            // 规则144.3：装备若误入战场，召回基地
            var misplacedEquip = slots.Where(u => u.type == CardType.Equipment).ToList();
            foreach (var eq in misplacedEquip) ownerBase.Add(eq);
            slots.RemoveAll(u => u.type == CardType.Equipment);

            var dead     = slots.Where(u => u.currentHp <= 0).ToList();
            var deadUids = new HashSet<int>(dead.Select(u => u.uid));

            foreach (var u in dead)
            {
                if (TryDeathShield(u, ownerBase, ownerDiscard)) continue;

                // 附着装备进废牌堆（规则144.5.b）
                if (u.attachedEquipments?.Count > 0)
                {
                    foreach (var eq in u.attachedEquipments) ownerDiscard.Add(eq);
                    u.attachedEquipments.Clear();
                }
                TriggerDeathwish(u, owner);
                ResetToBase(u);
                ownerDiscard.Add(u);
            }

            // 用 UID 集合过滤，避免 deathwish 改变 currentHp 后出现残留
            slots.RemoveAll(u => deadUids.Contains(u.uid));
        }

        private void CleanBaseSide(Owner owner)
        {
            var baseZone   = G.GetBase(owner);
            var ownerDiscard = G.GetDiscard(owner);

            // 基地：排除 death_shield 和装备（它们不直接死亡）
            var dead     = baseZone.Where(u => u.currentHp <= 0
                                            && u.effect != "death_shield"
                                            && u.type   != CardType.Equipment).ToList();
            var deadUids = new HashSet<int>(dead.Select(u => u.uid));

            foreach (var u in dead)
            {
                if (u.attachedEquipments?.Count > 0)
                {
                    foreach (var eq in u.attachedEquipments) ownerDiscard.Add(eq);
                    u.attachedEquipments.Clear();
                }
                TriggerDeathwish(u, owner);
                ResetToBase(u);
                ownerDiscard.Add(u);
            }

            baseZone.RemoveAll(u => deadUids.Contains(u.uid));
        }

        private static void ResetToBase(CardInstance u)
        {
            u.currentHp  = u.atk;
            u.currentAtk = u.atk;
            u.exhausted  = false;
            u.stunned    = false;
            u.tb         = new TurnBuffs { atk = 0 };
            u.buffToken  = false;
        }

        // ── TriggerDeathwish: 绝念效果 ──
        /// <summary>
        /// 等价原版 engine.js triggerDeathwish()。
        /// </summary>
        public void TriggerDeathwish(CardInstance unit, Owner owner)
        {
            var hand = G.GetHand(owner);
            var deck = G.GetDeck(owner);

            switch (unit.id)
            {
                case "voidling":
                {
                    if (hand.Count < GameState.MAX_HAND)
                    {
                        // 运行时创建「碎片」法术（不来自 CardData）
                        var frag = new CardInstance
                        {
                            uid      = CardInstance.AllocUid(),
                            id       = "fragment",
                            cardName = "碎片",
                            type     = CardType.Spell,
                            cost     = 0,
                            keywords = new List<string> { "迅捷" },
                            effect   = "buff_draw",
                            text     = "给一个随机盟友+1/+1。",
                            tb       = new TurnBuffs(),
                            attachedEquipments = new List<CardInstance>()
                        };
                        hand.Add(frag);
                    }
                    break;
                }
                case "void_sentinel":
                {
                    if (owner == Owner.Player) G.pNextAllyBuff += 1;
                    else G.eNextAllyBuff += 1;
                    break;
                }
                case "alert_sentinel":
                {
                    if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                    {
                        hand.Add(deck[deck.Count - 1]);
                        deck.RemoveAt(deck.Count - 1);
                    }
                    break;
                }
                case "wailing_poro":
                {
                    if (CountAlliesExcluding(unit, owner) == 0
                        && deck.Count > 0
                        && hand.Count < GameState.MAX_HAND)
                    {
                        hand.Add(deck[deck.Count - 1]);
                        deck.RemoveAt(deck.Count - 1);
                    }
                    break;
                }
            }
        }

        private int CountAlliesExcluding(CardInstance unit, Owner owner)
        {
            int count = G.GetBase(owner).Count(u => u.uid != unit.uid);
            foreach (var b in G.bf)
            {
                var slots = owner == Owner.Player ? b.pU : b.eU;
                // 只在 unit 所在区域计算同区盟友
                if (slots.Any(u => u.uid == unit.uid))
                    count += slots.Count(u => u.uid != unit.uid);
            }
            return count;
        }

        // ── OnSummon: 入场效果 ──
        /// <summary>
        /// 等价原版 spell.js onSummon()。
        /// 先处理 pNextAllyBuff（虚空哨兵遗愿），再 switch on unit.effect。
        /// 注：先见机甲（foresight_mech_enter）需要 prompt，P5 实现。
        /// </summary>
        public void OnSummon(CardInstance unit, Owner owner)
        {
            // 虚空哨兵绝念遗愿：下一名盟友入场+N/+N
            int nextBuff = owner == Owner.Player ? G.pNextAllyBuff : G.eNextAllyBuff;
            if (nextBuff > 0)
            {
                unit.currentAtk += nextBuff;
                unit.currentHp   = unit.currentAtk;
                if (owner == Owner.Player) G.pNextAllyBuff = 0;
                else G.eNextAllyBuff = 0;
            }

            var hand    = G.GetHand(owner);
            var deck    = G.GetDeck(owner);
            var baseZone = G.GetBase(owner);
            var runeDeck = G.GetRuneDeck(owner);
            var runes    = G.GetRunes(owner);

            switch (unit.effect)
            {
                case "yordel_instructor_enter":
                {
                    if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                    {
                        hand.Add(deck[deck.Count - 1]);
                        deck.RemoveAt(deck.Count - 1);
                    }
                    break;
                }
                case "darius_second_card":
                {
                    // 本回合已打出≥2张牌时（cardsPlayedThisTurn 在 Deploy 里已 +1，此时为最新值）
                    if (G.cardsPlayedThisTurn > 1)
                    {
                        unit.tb.atk += 2;
                        unit.exhausted = false;
                    }
                    break;
                }
                case "malph_enter":
                {
                    int holdCount = baseZone.Count(u => u.keywords != null
                                                     && u.keywords.Contains("坚守"));
                    unit.tb.atk += holdCount;
                    break;
                }
                case "jax_enter":
                {
                    foreach (var c in hand)
                    {
                        if (c.type == CardType.Equipment
                            && (c.keywords == null || !c.keywords.Contains("反应")))
                        {
                            if (c.keywords == null) c.keywords = new List<string>();
                            c.keywords.Add("反应");
                        }
                    }
                    break;
                }
                case "tiyana_enter":
                    // 被动：addScore 里扫描场上缇亚娜，无需在此做事
                    break;
                case "buff_rune1":
                {
                    if (runeDeck.Count > 0)
                    {
                        var r = runeDeck[runeDeck.Count - 1];
                        runeDeck.RemoveAt(runeDeck.Count - 1);
                        r.tapped = true;
                        runes.Add(r);
                    }
                    break;
                }
                case "thousand_tail_enter":
                {
                    var opp = G.Opponent(owner);
                    var allEnemies = GetAllUnitsForOwner(opp);
                    foreach (var u in allEnemies) u.tb.atk -= 3;
                    break;
                }
                case "summon_draw1":
                {
                    if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                    {
                        hand.Add(deck[deck.Count - 1]);
                        deck.RemoveAt(deck.Count - 1);
                    }
                    break;
                }
                // foresight_mech_enter: P5（需要 prompt，无法在纯逻辑层实现）
                default:
                    break;
            }
        }

        // ── Internal helpers ──
        private void SpendCosts(CardInstance c, Owner owner)
        {
            int effCost = GetEffectiveCost(c, owner);
            if (owner == Owner.Player) G.pMana -= effCost;
            else G.eMana -= effCost;

            var sch = G.GetSch(owner);
            if (c.schCost  > 0) sch.Spend(c.schType,  c.schCost);
            if (c.schCost2 > 0) sch.Spend(c.schType2, c.schCost2);
        }

        private void RemoveFromHand(CardInstance c, Owner owner)
        {
            var hand = G.GetHand(owner);
            int idx  = hand.FindIndex(h => h.uid == c.uid);
            if (idx >= 0) hand.RemoveAt(idx);
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
