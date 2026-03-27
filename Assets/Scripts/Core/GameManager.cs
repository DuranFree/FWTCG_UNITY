using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 游戏主控制器 — MonoBehaviour 单例。
    /// 持有所有纯 C# 系统实例，将逻辑层接入 Unity 生命周期：
    ///
    ///   Awake    → 实例化 + 注入所有系统依赖
    ///   StartGame→ 初始化流程（牌堆 → 翻币 → 战场选择）→ Mulligan（UI 确认后 ConfirmMulligan）
    ///   回合循环  → RunGame Coroutine：每回合依次执行五阶段
    ///   玩家行动  → Action 阶段等待 G.phase 变为 End（PlayerEndTurn/计时器 → TM.DoEndPhase()）
    ///   AI 行动   → AiAction() 通过 Schedule Coroutine 逐步执行，最终自调 TM.DoEndPhase()
    ///
    /// 对外事件（UI 订阅）：
    ///   OnStateChanged    — 任何状态变化，UI 应全量刷新
    ///   OnPhaseChanged    — 进入新阶段（GamePhase, Owner）
    ///   OnLog             — 战斗日志消息（string）
    ///   OnGameOver        — 游戏结束（Owner? winner，null = 平局）
    ///   OnTimerTick       — 剩余秒数（int）
    ///
    /// 公共 API（UI 按钮调用）：
    ///   StartGame / ConfirmMulligan / PlayerEndTurn
    ///   PlayCard / TapRune / RecycleRune / MoveUnit / ActivateLegendAbility
    ///   DuelSkip / DuelPlayCard
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ────────────────────────────────────────────
        // 单例
        // ────────────────────────────────────────────

        public static GameManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitSystems();
        }

        // ────────────────────────────────────────────
        // 系统实例（只读，供 UI 层读取 G 字段）
        // ────────────────────────────────────────────

        public GameState         G      { get; private set; }
        public TurnManager       TM     { get; private set; }
        public CardDeployer      CD     { get; private set; }
        public CombatResolver    CR     { get; private set; }
        public SpellSystem       SS     { get; private set; }
        public AIController      AI     { get; private set; }
        public LegendSystem      LS     { get; private set; }
        public BattlefieldSystem BFS    { get; private set; }
        public GameInitializer   GI     { get; private set; }
        public TurnTimerSystem   Timer  { get; private set; }

        // ────────────────────────────────────────────
        // 事件（UI 订阅）
        // ────────────────────────────────────────────

        /// <summary>任何状态发生变化，UI 调用全量刷新。</summary>
        public event Action OnStateChanged;

        /// <summary>进入新阶段。</summary>
        public event Action<GamePhase, Owner> OnPhaseChanged;

        /// <summary>战斗日志新消息。</summary>
        public event Action<string> OnLog;

        /// <summary>游戏结束（winner == null 表示平局）。</summary>
        public event Action<Owner?> OnGameOver;

        /// <summary>计时器整秒刻度。</summary>
        public event Action<int> OnTimerTick;

        // ────────────────────────────────────────────
        // 只读状态
        // ────────────────────────────────────────────

        public bool IsPlayerTurn => !G.gameOver && G.turn == Owner.Player
                                    && G.phase == GamePhase.Action;
        public bool IsAITurn     => !G.gameOver && G.turn == Owner.Enemy;
        public bool IsDuelActive => G.duelActive;

        // ────────────────────────────────────────────
        // 系统初始化（Awake 内调用）
        // ────────────────────────────────────────────

        private void InitSystems()
        {
            G     = new GameState();
            TM    = new TurnManager(G);
            CD    = new CardDeployer(G, TM);
            CR    = new CombatResolver(G, TM, CD);
            SS    = new SpellSystem(G, TM, CD, CR);
            LS    = new LegendSystem(G, TM);
            BFS   = new BattlefieldSystem(G);
            AI    = new AIController(G, TM, CD);
            GI    = new GameInitializer(G);
            Timer = new TurnTimerSystem();

            // ── 注入跨系统依赖 ──
            TM.SetLegendSystem(LS);
            TM.SetBattlefieldSystem(BFS);
            CD.SetLegendSystem(LS);
            CD.SetBattlefieldSystem(BFS);
            CR.SetLegendSystem(LS);
            CR.SetBattlefieldSystem(BFS);
            SS.SetBattlefieldSystem(BFS);
            AI.SetSpellSystem(SS);
            AI.SetLegendSystem(LS);

            // ── AI 步进 → Coroutine（0.7s 间隔模拟原版行为）──
            AI.Schedule = (delay, fn) => StartCoroutine(ScheduleCoroutine(delay, fn));

            // ── 计时器超时 → 自动结束玩家回合 ──
            Timer.OnTimeout = () =>
            {
                if (G.turn == Owner.Player && G.phase == GamePhase.Action && !G.gameOver)
                {
                    Log(LocalizationTable.Get("timer.timeout"));
                    TM.PlayerEndTurn();  // 设 G.phase = End，RunTurn while 循环退出
                    Notify();
                }
            };
        }

        // ────────────────────────────────────────────
        // 游戏启动 API（标题界面 → 按钮调用）
        // ────────────────────────────────────────────

        /// <summary>
        /// 开始新一局：构建牌组 → 翻硬币 → 选战场牌。
        /// 完成后触发 OnStateChanged，UI 显示 Mulligan 界面。
        /// 玩家确认后调用 ConfirmMulligan()。
        /// </summary>
        public void StartGame(GameInitializer.DeckConfig config)
        {
            StopAllCoroutines();
            G.gameOver = false;
            GI.SetupDecks(config);
            GI.CoinFlip();
            GI.SelectBattlefields();

            Log(LocalizationTable.Get("game.start"));
            Log(G.first == Owner.Player
                ? LocalizationTable.Get("coin.player_first")
                : LocalizationTable.Get("coin.enemy_first"));

            Notify();
        }

        /// <summary>
        /// 玩家确认调整手牌后调用，随即进入回合循环。
        /// </summary>
        public void ConfirmMulligan(List<int> selectedIndices)
        {
            GI.ConfirmMulligan(selectedIndices);
            Notify();
            StartCoroutine(RunGame());
        }

        // ────────────────────────────────────────────
        // 回合主循环（Coroutine）
        // ────────────────────────────────────────────

        private IEnumerator RunGame()
        {
            while (!G.gameOver)
            {
                yield return StartCoroutine(RunTurn(G.turn));
            }
            Owner? winner = G.pScore >= GameState.WIN_SCORE ? Owner.Player
                          : G.eScore >= GameState.WIN_SCORE ? Owner.Enemy
                          : (Owner?)null;
            Log(winner == Owner.Player ? LocalizationTable.Get("score.win")
              : winner == Owner.Enemy  ? LocalizationTable.Get("score.lose")
              :                          LocalizationTable.Get("score.draw"));
            OnGameOver?.Invoke(winner);
        }

        /// <summary>
        /// 单回合流程：
        ///   StartTurn → Awaken → Start → Summon → Draw → Action（玩家等待 / AI 自行结束）
        ///
        /// 回合结束信号：G.phase == GamePhase.End（DoEndPhase() 设置）
        ///   - 玩家：PlayerEndTurn() → TM.PlayerEndTurn() → TM.DoEndPhase()
        ///   - AI  ：AiAction() 通过 Schedule 最终调用 TM.DoEndPhase()
        ///   - 超时：Timer.OnTimeout → TM.PlayerEndTurn() → TM.DoEndPhase()
        /// </summary>
        private IEnumerator RunTurn(Owner who)
        {
            TM.StartTurn(who);
            TM.DoAwaken();
            SetPhase(GamePhase.Awaken);
            yield return null;

            TM.DoStart();
            SetPhase(GamePhase.Start);
            Notify();
            yield return null;

            TM.DoSummon();
            SetPhase(GamePhase.Summon);
            Notify();
            yield return null;

            TM.DoDraw();
            SetPhase(GamePhase.Draw);
            Notify();
            yield return null;

            SetPhase(GamePhase.Action);
            Notify();

            if (who == Owner.Player)
            {
                // 启动计时器；等待 G.phase 变为 End（任何路径结束回合均可触发）
                Timer.Reset();
                Timer.Start();
                while (G.phase == GamePhase.Action && !G.gameOver)
                    yield return null;
                Timer.Stop();
            }
            else
            {
                // AI 行动：AiAction() 最终自调 TM.DoEndPhase() → G.phase = End
                AI.AiAction();
                while (G.phase == GamePhase.Action && !G.gameOver)
                    yield return null;
            }

            Notify();
        }

        private void Update()
        {
            // 驱动计时器（仅玩家回合计时）
            if (G != null && !G.gameOver
                && G.turn == Owner.Player && G.phase == GamePhase.Action
                && Timer.IsRunning)
            {
                int before = Timer.TimeRemaining;
                Timer.Tick(Time.deltaTime);
                if (Timer.TimeRemaining != before)
                    OnTimerTick?.Invoke(Timer.TimeRemaining);
            }
        }

        // ────────────────────────────────────────────
        // 玩家行动 API（UI 按钮调用）
        // ────────────────────────────────────────────

        /// <summary>玩家手动结束回合。</summary>
        public void PlayerEndTurn()
        {
            if (!IsPlayerTurn) return;
            TM.PlayerEndTurn();   // 内部调用 DoEndPhase()，G.phase → End
            Notify();
        }

        /// <summary>
        /// 打出手牌（随从 / 法术 / 装备）。
        /// location: "base" | "0" | "1"（战场 id 字符串）
        /// 返回 true 表示出牌成功。
        /// </summary>
        public bool PlayCard(int cardUid, string location)
        {
            if (!IsPlayerTurn) return false;
            var card = FindInHand(cardUid, Owner.Player);
            if (card == null || !CD.CanPlay(card, Owner.Player)) return false;

            bool ok = false;
            if (card.type == CardType.Spell)
            {
                // 法术：获取目标 → 对决检查 → 直接施放
                var targets = SS.GetSpellTargets(card, Owner.Player);
                if (targets != null && targets.Count == 0) return false;  // 需要目标但无合法目标

                int? targetUid = (targets != null && targets.Count > 0) ? targets[0] : (int?)null;
                // TODO: 多目标时 UI 层应覆写为弹窗选择；此处暂选第一个合法目标
                G.pHand.Remove(card);
                SS.ApplySpell(card, Owner.Player, targetUid);
                ok = true;
            }
            else if (location == "base")
            {
                ok = CD.DeployToBase(card, Owner.Player) != null;
            }
            else if (int.TryParse(location, out int bfId))
            {
                ok = CD.DeployToBF(card, Owner.Player, bfId) != null;
            }

            if (ok) Notify();
            return ok;
        }

        /// <summary>点击（横置）一枚符文，获得 1 点法力。</summary>
        public bool TapRune(int runeIndex)
        {
            if (!IsPlayerTurn) return false;
            var runes = G.pRunes;
            if (runeIndex < 0 || runeIndex >= runes.Count) return false;
            var r = runes[runeIndex];
            if (r.tapped) return false;
            r.tapped = true;
            G.pMana++;
            Notify();
            return true;
        }

        /// <summary>回收一枚符文：移除并获得 +1 对应符能。</summary>
        public bool RecycleRune(int runeIndex)
        {
            if (!IsPlayerTurn) return false;
            var runes = G.pRunes;
            if (runeIndex < 0 || runeIndex >= runes.Count) return false;
            var r = runes[runeIndex];
            G.GetSch(Owner.Player).Add(r.runeType, 1);
            runes.RemoveAt(runeIndex);
            Notify();
            return true;
        }

        /// <summary>将基地单位移动到指定战场（或基地 "base"）。</summary>
        public bool MoveUnit(int unitUid, string destination)
        {
            if (!IsPlayerTurn) return false;
            // 在基地和战场中搜索
            var unit = FindInBase(unitUid, Owner.Player)
                    ?? FindInBattlefield(unitUid, Owner.Player);
            if (unit == null) return false;
            CD.MoveUnit(unit, Owner.Player, destination);
            Notify();
            return true;
        }

        /// <summary>激活传奇主动技能。</summary>
        public void ActivateLegendAbility(string abilityId)
        {
            if (!IsPlayerTurn) return;
            LS.ActivateLegendAbility(Owner.Player, abilityId);
            Notify();
        }

        // ── 法术对决 ──

        /// <summary>法术对决中：玩家跳过（skip）。</summary>
        public void DuelSkip()
        {
            if (!G.duelActive || G.duelTurn != Owner.Player) return;
            SS.SkipDuel();
            Notify();
        }

        /// <summary>法术对决中：玩家打出响应牌。</summary>
        public bool DuelPlayCard(int cardUid)
        {
            if (!G.duelActive || G.duelTurn != Owner.Player) return false;
            var card = FindInHand(cardUid, Owner.Player);
            if (card == null) return false;
            G.pHand.Remove(card);
            SS.ApplySpell(card, Owner.Player, null);
            Notify();
            return true;
        }

        // ────────────────────────────────────────────
        // 内部辅助
        // ────────────────────────────────────────────

        private IEnumerator ScheduleCoroutine(float delay, Action fn)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            fn();
            Notify();
        }

        private void SetPhase(GamePhase phase)
        {
            G.phase = phase;
            OnPhaseChanged?.Invoke(phase, G.turn);
        }

        private void Notify()
        {
            OnStateChanged?.Invoke();
            if (G.gameOver)
                OnGameOver?.Invoke(
                    G.pScore >= GameState.WIN_SCORE ? Owner.Player :
                    G.eScore >= GameState.WIN_SCORE ? Owner.Enemy : (Owner?)null);
        }

        internal void Log(string msg)
        {
            OnLog?.Invoke(msg);
            Debug.Log($"[FWTCG] {msg}");
        }

        // ── 搜索辅助 ──

        private CardInstance FindInHand(int uid, Owner owner)
        {
            foreach (var c in G.GetHand(owner))
                if (c.uid == uid) return c;
            return null;
        }

        private CardInstance FindInBase(int uid, Owner owner)
        {
            foreach (var c in G.GetBase(owner))
                if (c.uid == uid) return c;
            return null;
        }

        private CardInstance FindInBattlefield(int uid, Owner owner)
        {
            foreach (var b in G.bf)
            {
                var slots = owner == Owner.Player ? b.pU : b.eU;
                foreach (var c in slots)
                    if (c.uid == uid) return c;
            }
            return null;
        }
    }
}
