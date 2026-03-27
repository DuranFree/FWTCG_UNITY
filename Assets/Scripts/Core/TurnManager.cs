using System.Collections.Generic;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 回合阶段状态机，等价原版 engine.js 的
    /// startTurn / runPhase / doAwaken / doStart / doSummon / doDraw / doEndPhase / playerEndTurn。
    /// 纯 C# 类，不依赖 MonoBehaviour；UI 层回调通过 MonoBehaviour 协程驱动（P6 实现）。
    /// </summary>
    public class TurnManager
    {
        public readonly GameState G;
        private LegendSystem _legendSystem;

        public TurnManager(GameState g) { G = g; }

        /// <summary>注入传奇系统（P8）。</summary>
        public void SetLegendSystem(LegendSystem ls) => _legendSystem = ls;

        // ── StartTurn: 每回合起点 ──
        /// <summary>
        /// 设置当前回合归属，清空战场征服标记和本回合得分记录。
        /// 等价原版 startTurn() 的纯状态部分（banner/render 由 UI 层处理）。
        /// </summary>
        public void StartTurn(Owner who)
        {
            G.turn = who;
            foreach (var b in G.bf) b.conqDone = false;
            G.bfScoredThisTurn.Clear();
            G.bfConqueredThisTurn.Clear();
            _legendSystem?.ResetLegendAbilitiesForTurn(who);
        }

        // ── DoAwaken: 唤醒阶段 ──
        /// <summary>
        /// 解除己方单位休眠，解除符文横置，重置符能，清空本回合出牌计数和号令标记。
        /// </summary>
        public void DoAwaken()
        {
            foreach (var u in GetAllUnitsForOwner(G.turn))
                u.exhausted = false;

            var leg = G.GetLeg(G.turn);
            if (leg != null) leg.exhausted = false;

            foreach (var r in G.GetRunes(G.turn))
                r.tapped = false;

            G.GetSch(G.turn).Reset();

            if (G.turn == Owner.Player)
            {
                G.pendingRunes.Clear();
                G.pendingMove = null;
            }

            G.cardsPlayedThisTurn = 0;
            G.cardLockTarget = null;
            if (G.turn == Owner.Player) G.pRallyActive = false;
            else G.eRallyActive = false;
        }

        // ── DoStart: 开始阶段（据守得分）──
        /// <summary>
        /// 为每个己方控制的战场发放据守分（+1/回合）。
        /// 注：战场牌特殊据守效果（altar_unity / aspirant_climb 等）在 P5 实现；
        ///     三相之力额外得分在 P3 实现；
        ///     缇亚娜·冕卫阻止、攀圣长阶/遗忘丰碑修正在 P5 实现。
        /// </summary>
        public void DoStart()
        {
            foreach (var b in G.bf)
            {
                if (b.ctrl == G.turn)
                {
                    AddScore(G.turn, 1, "hold", b.id);

                    // 三相之力额外+1分（P3 装备系统完成后生效，现已可正确读取 trinityEquipped 标记）
                    foreach (var u in (G.turn == Owner.Player ? b.pU : b.eU))
                    {
                        if (u.trinityEquipped)
                            AddScore(G.turn, 1, "hold", b.id);
                    }
                }
            }
        }

        // ── DoSummon: 召出阶段（抽符文）──
        /// <summary>
        /// 从符文牌堆顶部取出符文放到场上：普通回合2张，后手方第一回合3张。
        /// </summary>
        public void DoSummon()
        {
            bool isBackPlayer  = G.turn != G.first;
            bool firstTurnDone = G.turn == Owner.Player ? G.pFirstTurnDone : G.eFirstTurnDone;
            int  cnt           = (isBackPlayer && !firstTurnDone) ? 3 : 2;

            var rd = G.GetRuneDeck(G.turn);
            var rz = G.GetRunes(G.turn);
            for (int i = 0; i < cnt && rd.Count > 0; i++)
            {
                rz.Add(rd[rd.Count - 1]);
                rd.RemoveAt(rd.Count - 1);
            }
        }

        // ── DoDraw: 摸牌阶段 ──
        /// <summary>
        /// 从主牌堆抽1张牌进手牌；牌堆空时洗入废牌堆并触发燃尽惩罚。
        /// 抽牌完成后重置双方法力与符能（等价原版行为）。
        /// 注：先见机甲·预知效果在 P5 实现。
        /// </summary>
        public void DoDraw()
        {
            var deck    = G.GetDeck(G.turn);
            var discard = G.GetDiscard(G.turn);
            var hand    = G.GetHand(G.turn);

            if (deck.Count == 0)
            {
                if (discard.Count > 0)
                {
                    foreach (var c in discard) deck.Add(c);
                    discard.Clear();
                    Shuffle(deck);
                }
                // 燃尽惩罚：对手+1分
                AddScore(G.Opponent(G.turn), 1, "burnout", null);
            }

            if (deck.Count > 0)
            {
                hand.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }

            // 抽牌阶段末：重置双方法力和符能
            G.pMana = 0;
            G.eMana = 0;
            G.pSch.Reset();
            G.eSch.Reset();
        }

        // ── DoEndPhase: 结束阶段 ──
        /// <summary>
        /// 清除眩晕、重置标记伤害（HP=ATK）、清除临时加成，回合计数+1，切换当前回合方。
        /// 调用后应由协程/测试方调用 StartTurn(G.turn) 开始下一回合。
        /// </summary>
        public void DoEndPhase()
        {
            G.phase = GamePhase.End;

            var all = new List<CardInstance>(G.pBase);
            all.AddRange(G.eBase);
            foreach (var b in G.bf) { all.AddRange(b.pU); all.AddRange(b.eU); }

            // 规则517.1 眩晕解除
            foreach (var u in all) u.stunned = false;
            if (G.pLeg != null) G.pLeg.stunned = false;
            if (G.eLeg != null) G.eLeg.stunned = false;

            // 规则517.2 标记伤害清除，临时效果失效
            foreach (var u in all)
            {
                u.currentHp = u.currentAtk;
                u.tb = new TurnBuffs { atk = 0 };
            }
            if (G.pLeg != null) G.pLeg.tb = new TurnBuffs { atk = 0 };
            if (G.eLeg != null) G.eLeg.tb = new TurnBuffs { atk = 0 };

            G.pMana = 0; G.eMana = 0;
            G.pSch.Reset(); G.eSch.Reset();
            G.cardsPlayedThisTurn = 0;
            G.cardLockTarget      = null;
            G.pRallyActive        = false;
            G.eRallyActive        = false;

            if (G.turn == Owner.Player) G.pFirstTurnDone = true;
            else G.eFirstTurnDone = true;

            // 时间扭曲：额外回合（仍归玩家，不推进 round）
            if (G.turn == Owner.Player && G.extraTurnPending)
            {
                G.extraTurnPending = false;
                // G.turn 保持 Player，协程调用 StartTurn(Player)
                return;
            }

            G.round++;
            G.turn = G.Opponent(G.turn);
            // 协程调用 StartTurn(G.turn)
        }

        // ── PlayerEndTurn: 玩家手动结束行动 ──
        public void PlayerEndTurn()
        {
            if (G.turn != Owner.Player || G.phase != GamePhase.Action || G.gameOver) return;
            DoEndPhase();
        }

        // ── AddScore: 积分核心（含8分征服限制）──
        /// <summary>
        /// 给 who 加 pts 分，追踪据守/征服战场 ID，验证第8分限制条件，然后调用 CheckWin。
        /// 注：缇亚娜/攀圣长阶/遗忘丰碑修正在 P5 实现。
        /// </summary>
        public bool AddScore(Owner who, int pts, string type, int? bfId)
        {
            // 追踪本回合得过分的战场
            if (bfId.HasValue && !G.bfScoredThisTurn.Contains(bfId.Value))
                G.bfScoredThisTurn.Add(bfId.Value);

            if (type == "conquer" && bfId.HasValue && !G.bfConqueredThisTurn.Contains(bfId.Value))
                G.bfConqueredThisTurn.Add(bfId.Value);

            // #9 规则：征服第8分须本回合在所有战场均已征服过
            if (type == "conquer")
            {
                int current = who == Owner.Player ? G.pScore : G.eScore;
                if (current == GameState.WIN_SCORE - 1)
                {
                    bool allConquered = true;
                    foreach (var b in G.bf)
                        if (!G.bfConqueredThisTurn.Contains(b.id)) { allConquered = false; break; }

                    if (!allConquered)
                    {
                        // 改为抽1张牌
                        var deck = G.GetDeck(who);
                        var hand = G.GetHand(who);
                        if (deck.Count > 0 && hand.Count < GameState.MAX_HAND)
                        {
                            hand.Add(deck[deck.Count - 1]);
                            deck.RemoveAt(deck.Count - 1);
                        }
                        return false;
                    }
                }
            }

            if (who == Owner.Player) G.pScore += pts;
            else G.eScore += pts;

            CheckWin();
            return true;
        }

        // ── CheckWin: 胜负判定 ──
        public void CheckWin()
        {
            if (G.gameOver) return;
            if (G.pLeg != null && G.pLeg.currentHp <= 0) { G.gameOver = true; return; }
            if (G.eLeg != null && G.eLeg.currentHp <= 0) { G.gameOver = true; return; }
            if (G.pScore >= GameState.WIN_SCORE || G.eScore >= GameState.WIN_SCORE)
                G.gameOver = true;
        }

        // ── 辅助：获取指定方全部场上单位 ──
        public List<CardInstance> GetAllUnitsForOwner(Owner o)
        {
            var result = new List<CardInstance>(G.GetBase(o));
            foreach (var b in G.bf)
                result.AddRange(o == Owner.Player ? b.pU : b.eU);
            return result;
        }

        private static void Shuffle<T>(List<T> list)
        {
            var rng = new System.Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
