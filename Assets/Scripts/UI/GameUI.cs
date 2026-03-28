using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.UI
{
    /// <summary>
    /// 主游戏 UI — 自建 Canvas 的 MonoBehaviour。
    /// 在 Awake 构建完整 uGUI 层级；Start 订阅 GameManager 事件并启动对局。
    /// 每次 OnStateChanged 触发时调用 Refresh() 全量刷新所有面板。
    ///
    /// 交互流程（两次点击出牌）：
    ///   1. 点击手牌 → ToggleCard(uid) → 高亮
    ///   2. 点击目标区域（base/0/1）→ PlayCard → Clear
    ///
    /// 交互流程（拖拽出牌）：
    ///   1. 按住手牌拖动 → CardDragHandler.OnBeginDrag → BeginCardDrag → 生成 DragGhost
    ///   2. 拖动中 → UpdateCardDrag → Ghost 跟随指针
    ///   3. 松手 → EndCardDrag → RaycastAll 找 ZoneDropTarget → OnZoneClicked
    ///
    /// 单位移动：
    ///   1. 点击己方单位 → ToggleUnit(uid)
    ///   2. 点击目标战场/基地 → MoveUnit → Clear
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // 面板引用（Awake 构建）
        // ─────────────────────────────────────────────

        private Text      _enemyInfoText;
        private Transform _enemyZoneTrans;       // 敌方基地单位 + 手牌数
        private Transform _bf0Trans;             // 战场0左半区
        private Transform _bf1Trans;             // 战场1右半区
        private Transform _playerBaseTrans;      // 玩家基地区
        private Transform _playerRuneTrans;      // 玩家符文区
        private Transform _playerHandTrans;      // 玩家手牌区
        private Text      _playerInfoText;       // 底部法力/符能文字
        private Button    _endTurnBtn;
        private Text      _endTurnBtnText;
        private Button    _legendBtn;
        private Text      _legendBtnText;

        // 模拟/换牌面板
        private GameObject _mulliganPanel;
        private Transform  _mulliganCardsTrans;
        private Text       _mulliganInfoText;

        // 对决面板
        private GameObject _duelPanel;
        private Text       _duelInfoText;

        // 游戏结束面板
        private GameObject _gameOverPanel;
        private Text       _gameOverText;

        // 战场面板背景图（BF 控制光晕）
        private Image      _bf0PanelImg;
        private Image      _bf1PanelImg;

        // 手牌入场动画追踪
        private readonly HashSet<int> _prevHandUids = new();

        // 卡牌详情面板
        private GameObject _cardDetailPanel;
        private Text       _cardDetailText;

        // 标题界面
        private GameObject _titlePanel;

        // 弃牌堆面板
        private GameObject _discardPanel;
        private Text       _discardText;

        // 阶段横幅
        private GameObject _phaseBanner;
        private Text       _phaseBannerText;

        // 震动 + 阶段追踪
        private RectTransform _rootCanvasRt;
        private GamePhase     _lastPhase   = GamePhase.Init;
        private Owner         _lastTurn    = Owner.Player;
        private int           _prevRuneCount = 0;  // 符文入场动画追踪

        // 拖拽出牌
        private GameObject   _dragGhost;
        private int          _dragUid = -1;

        // 日志面板
        private Text       _logText;
        private ScrollRect _logScroll;

        // ─────────────────────────────────────────────
        // 主题颜色常量（LoR 风格）
        // ─────────────────────────────────────────────

        private static readonly Color C_Gold    = new Color(0.78f, 0.67f, 0.43f, 1f);  // #c8aa6e
        private static readonly Color C_Cyan    = new Color(0.04f, 0.78f, 0.73f, 1f);  // #0ac8b9
        private static readonly Color C_Dark    = new Color(0.004f, 0.04f, 0.07f, 1f); // #010a13
        private static readonly Color C_DarkBg  = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        private static readonly Color C_EnemyBg = new Color(0.12f, 0.04f, 0.04f, 0.85f);
        private static readonly Color C_PlayBg  = new Color(0.03f, 0.07f, 0.12f, 0.85f);
        private static readonly Color C_BFBg    = new Color(0.04f, 0.08f, 0.14f, 0.80f);
        private static readonly Color C_RuneBg  = new Color(0.06f, 0.10f, 0.04f, 0.85f);
        private static readonly Color C_HandBg  = new Color(0.03f, 0.04f, 0.10f, 0.85f);
        private static readonly Color C_LogBg   = new Color(0.02f, 0.02f, 0.04f, 0.95f);

        // 6种符文专属颜色（炽烈橙/灵光黄/翠意绿/摧破红/混沌紫/序理蓝）
        private static readonly Color C_RuneBlazing  = new Color(1.00f, 0.45f, 0.10f);
        private static readonly Color C_RuneRadiant  = new Color(1.00f, 0.92f, 0.30f);
        private static readonly Color C_RuneVerdant  = new Color(0.27f, 0.82f, 0.27f);
        private static readonly Color C_RuneCrushing = new Color(0.90f, 0.22f, 0.22f);
        private static readonly Color C_RuneChaos    = new Color(0.72f, 0.32f, 0.95f);
        private static readonly Color C_RuneOrder    = new Color(0.28f, 0.58f, 1.00f);

        // ─────────────────────────────────────────────
        // 运行时状态
        // ─────────────────────────────────────────────

        private readonly UISelectionState _sel = new();
        private GameManager               _gm;
        private readonly List<int>        _mulliganSel = new();   // 选中换掉的手牌下标
        private string                    _logAccum = "";

        // ─────────────────────────────────────────────
        // Unity 生命周期
        // ─────────────────────────────────────────────

        private void Awake()
        {
            // 平台适配：横屏锁定 + 禁止多点触控缩放误触
            Screen.orientation     = ScreenOrientation.LandscapeLeft;
            Input.multiTouchEnabled = false;

            EnsureEventSystem();
            BuildCanvas();
        }

        private void Start()
        {
            _gm = GameManager.Instance;
            if (_gm == null)
            {
                Debug.LogError("[GameUI] GameManager.Instance is null — ensure GameManager is in the scene.");
                return;
            }
            _gm.OnStateChanged += Refresh;
            _gm.OnLog          += AppendLog;
            _gm.OnGameOver     += HandleGameOver;
            _gm.OnTimerTick    += _ => RefreshPlayerInfo();

            // 启动辅助系统
            var toastGo = new GameObject("ToastSystem");
            toastGo.AddComponent<ToastSystem>();
            var floatGo = new GameObject("DamageFloatText");
            floatGo.AddComponent<DamageFloatText>();

            // 战场控制光晕持续循环
            StartCoroutine(BFGlowLoop());

            // 显示标题界面，由玩家点击"开始游戏"触发 StartGame
        }

        private void OnDestroy()
        {
            if (_gm == null) return;
            _gm.OnStateChanged -= Refresh;
            _gm.OnLog          -= AppendLog;
            _gm.OnGameOver     -= HandleGameOver;
        }

        // ─────────────────────────────────────────────
        // 刷新（全量）
        // ─────────────────────────────────────────────

        private void Refresh()
        {
            var G = _gm.G;

            // 阶段/回合切换横幅
            bool phaseChanged = G.phase != _lastPhase;
            bool turnChanged  = G.turn  != _lastTurn && G.phase == GamePhase.Action;
            if (phaseChanged || turnChanged)
            {
                string bannerMsg = turnChanged && G.phase == GamePhase.Action
                    ? (G.turn == Owner.Player ? "— 你的回合 —" : "— 对手回合 —")
                    : PhaseName(G.phase);
                ShowPhaseBanner(bannerMsg);
                _lastPhase = G.phase;
                _lastTurn  = G.turn;
            }

            // 模拟换牌阶段：只显示 mulligan 面板
            if (G.phase == GamePhase.Init && G.pHand.Count > 0)
            {
                _mulliganPanel.SetActive(true);
                RefreshMulligan();
                return;
            }
            _mulliganPanel.SetActive(false);

            // 对决面板
            _duelPanel.SetActive(G.duelActive);
            if (G.duelActive) RefreshDuel();

            RefreshEnemyInfo();
            RefreshEnemyZone();
            RefreshBattlefields();
            RefreshPlayerBase();
            RefreshPlayerRunes();
            RefreshPlayerHand();
            RefreshPlayerInfo();
            RefreshActionButtons();
        }

        // ─────────────────────────────────────────────
        // 各面板刷新
        // ─────────────────────────────────────────────

        private void RefreshEnemyInfo()
        {
            var G = _gm.G;
            var leg = G.eLeg;
            string legTxt = leg != null
                ? $"传奇: {leg.data.cardName} HP:{leg.currentHp}/{leg.maxHp} ATK:{leg.currentAtk}"
                : "传奇: -";
            _enemyInfoText.text =
                $"[敌方]  {ScoreBar(G.eScore)} {G.eScore}/8  法力:{G.eMana}  手牌:{G.eHand.Count}  " +
                $"符文:{G.eRunes.Count}  {legTxt}  " +
                $"回合:{G.round}  阶段:{G.phase}";
        }

        private void RefreshEnemyZone()
        {
            ClearChildren(_enemyZoneTrans);
            var G = _gm.G;
            // 显示敌方基地单位（不可交互，仅显示；可点详情）
            foreach (var u in G.eBase)
            {
                var capturedU = u;
                var row = AddHorizontalGroup(_enemyZoneTrans);
                AddLabel(row, UnitLabel(u), Color.red);
                AddSmallButton(row, "详", C_Gold, () => ShowCardDetail(capturedU));
            }
        }

        private void RefreshBattlefields()
        {
            var G = _gm.G;
            RefreshBF(0, _bf0Trans, G.bf[0]);
            RefreshBF(1, _bf1Trans, G.bf[1]);
        }

        private void RefreshBF(int bfIdx, Transform trans, BattlefieldState bf)
        {
            ClearChildren(trans);
            var G = _gm.G;

            // 战场标题
            string ctrl = bf.ctrl == null ? "中立" : (bf.ctrl == Owner.Player ? "我方" : "敌方");
            AddLabel(trans,
                $"=== 战场{bf.id} [{ctrl}] BF卡:{bf.card?.cardName ?? "-"} ===",
                Color.yellow);

            // 敌方单位
            foreach (var u in bf.eU)
                AddLabel(trans, UnitLabel(u), Color.red);

            // 玩家单位（可点击选中/选目标）
            foreach (var u in bf.pU)
            {
                bool isSel = _sel.IsUnitSelected && _sel.SelectedUid == u.uid;
                AddUnitButton(trans, u, Owner.Player, isSel);
            }

            // 空战场区域：可作为出牌目标 / 移动目标
            if (_gm.IsPlayerTurn)
            {
                string zone = bfIdx.ToString();
                bool isTarget = _sel.IsCardSelected || _sel.IsUnitSelected;
                AddZoneButton(trans, $"[部署到战场{bf.id}]", zone, isTarget ? Color.cyan : Color.gray);
            }
        }

        private void RefreshPlayerBase()
        {
            ClearChildren(_playerBaseTrans);
            var G = _gm.G;

            AddLabel(_playerBaseTrans, "--- 我方基地 ---", Color.white);

            foreach (var u in G.pBase)
            {
                bool isSel = _sel.IsUnitSelected && _sel.SelectedUid == u.uid;
                AddUnitButton(_playerBaseTrans, u, Owner.Player, isSel);
            }

            if (_gm.IsPlayerTurn)
            {
                bool isTarget = _sel.IsCardSelected || _sel.IsUnitSelected;
                AddZoneButton(_playerBaseTrans, "[部署到基地]", "base", isTarget ? Color.cyan : Color.gray);
            }
        }

        private void RefreshPlayerRunes()
        {
            ClearChildren(_playerRuneTrans);
            var G = _gm.G;
            int oldCount = _prevRuneCount;

            for (int i = 0; i < G.pRunes.Count; i++)
            {
                var r = G.pRunes[i];
                int capturedIdx = i;
                string runeLabel = RuneLabel(r);
                Color  baseRune  = RuneColor(r.runeType);
                Color  runeCol   = r.tapped ? baseRune * 0.38f : baseRune;

                var row = AddHorizontalGroup(_playerRuneTrans);

                // 显示标签
                AddLabel(row, runeLabel, runeCol);

                if (_gm.IsPlayerTurn)
                {
                    // 横置按钮
                    if (!r.tapped)
                        AddSmallButton(row, "横置", Color.green,
                            () => { _gm.TapRune(capturedIdx); });

                    // 回收按钮
                    AddSmallButton(row, "回收", Color.yellow,
                        () => { _gm.RecycleRune(capturedIdx); Refresh(); });
                }

                // 新抽到的符文播放错落入场动画（每张间隔 50ms）
                if (i >= oldCount)
                {
                    float delay = (i - oldCount) * 0.05f;
                    var rowRt = row.GetComponent<RectTransform>();
                    StartCoroutine(DelayedPopIn(rowRt, 0.25f, delay));
                }
            }

            _prevRuneCount = G.pRunes.Count;

            // 符能计数
            var sch = G.pSch;
            AddLabel(_playerRuneTrans,
                $"符能: 炽烈{sch.blazing} 灵光{sch.radiant} 翠意{sch.verdant} " +
                $"摧破{sch.crushing} 混沌{sch.chaos} 序理{sch.order}",
                Color.white);
        }

        private void RefreshPlayerHand()
        {
            ClearChildren(_playerHandTrans);
            var G = _gm.G;

            var currentUids = new HashSet<int>();
            foreach (var card in G.pHand)
            {
                currentUids.Add(card.uid);
                bool isSel    = _sel.IsCardSelected && _sel.SelectedUid == card.uid;
                bool canPlay  = _gm.IsPlayerTurn && _gm.CD.CanPlay(card, Owner.Player);
                int capturedUid = card.uid;
                var capturedCard = card;

                // 文字色：选中=青，可出=绿，不可出=暗灰
                Color textCol = isSel    ? Color.cyan
                              : canPlay  ? new Color(0.25f, 0.91f, 0.54f)  // #40e88a
                              : new Color(0.45f, 0.45f, 0.45f);

                // 背景色：选中=深青，可出=深绿，不可出=默认暗
                Color bgCol = isSel    ? new Color(0.10f, 0.28f, 0.32f)
                            : canPlay  ? new Color(0.06f, 0.24f, 0.12f)
                            : new Color(0.15f, 0.15f, 0.20f);

                // 每张手牌 = [牌按钮] + [详按钮] 横向排列
                var row = AddHorizontalGroup(_playerHandTrans);

                var btn = AddButton(row, CardLabel(card), textCol,
                    () =>
                    {
                        _sel.ToggleCard(capturedUid);
                        Refresh();
                    }, bgCol);

                // 拖拽出牌支持（与点击互斥：有拖动时 Button.onClick 不触发）
                if (canPlay)
                {
                    var drag = btn.gameObject.AddComponent<CardDragHandler>();
                    drag.CardUid       = capturedUid;
                    drag.OnBeginDragCb = (id, pos) => BeginCardDrag(id, pos);
                    drag.OnDragCb      = pos        => UpdateCardDrag(pos);
                    drag.OnEndDragCb   = (id, pos)  => EndCardDrag(id, pos);
                }

                AddSmallButton(row, "详", C_Gold,
                    () => ShowCardDetail(capturedCard));

                // 仅对新进入手牌的卡播放入场动画
                if (!_prevHandUids.Contains(card.uid))
                {
                    var rowRt = row.GetComponent<RectTransform>();
                    if (rowRt != null) StartCoroutine(UITween.PopIn(rowRt, 0.28f));
                }
            }
            _prevHandUids.Clear();
            _prevHandUids.UnionWith(currentUids);
        }

        private void RefreshPlayerInfo()
        {
            var G = _gm.G;
            var leg = G.pLeg;
            string legTxt = leg != null
                ? $"传奇:{leg.data.cardName} HP:{leg.currentHp}/{leg.maxHp} ATK:{leg.currentAtk}"
                : "传奇:-";
            int timer = _gm.G.turnTimerSeconds;

            _playerInfoText.text =
                $"[我方]  {ScoreBar(G.pScore)} {G.pScore}/8  法力:{G.pMana}  手牌:{G.pHand.Count}  " +
                $"符文:{G.pRunes.Count}  {legTxt}  剩余:{timer}s";
        }

        private void RefreshActionButtons()
        {
            bool isPlayerTurn = _gm.IsPlayerTurn;
            _endTurnBtn.interactable   = isPlayerTurn;
            _endTurnBtnText.color      = isPlayerTurn ? Color.white : Color.gray;

            // 传奇技能
            var leg = _gm.G.pLeg;
            bool canLeg = isPlayerTurn && leg != null && !leg.exhausted;
            _legendBtn.interactable = canLeg;
            _legendBtnText.text     = leg != null
                ? $"传奇技能\n{leg.data.cardName}"
                : "传奇技能";
            _legendBtnText.color = canLeg ? Color.yellow : Color.gray;
        }

        private void RefreshMulligan()
        {
            ClearChildren(_mulliganCardsTrans);
            var G = _gm.G;

            _mulliganInfoText.text =
                $"换牌阶段 — 点击选择要换掉的牌（已选:{_mulliganSel.Count}）\n" +
                $"先手：{(G.first == Owner.Player ? "玩家" : "对手")}";

            for (int i = 0; i < G.pHand.Count; i++)
            {
                var card = G.pHand[i];
                int capturedIdx = i;
                bool isSel = _mulliganSel.Contains(i);
                Color col = isSel ? Color.red : Color.white;

                AddButton(_mulliganCardsTrans,
                    CardLabel(card), col,
                    () =>
                    {
                        if (_mulliganSel.Contains(capturedIdx))
                            _mulliganSel.Remove(capturedIdx);
                        else
                            _mulliganSel.Add(capturedIdx);
                        RefreshMulligan();
                    });
            }

            // 确认按钮
            AddButton(_mulliganCardsTrans, "确认换牌", Color.cyan,
                () =>
                {
                    var selected = new List<int>(_mulliganSel);
                    _mulliganSel.Clear();
                    _mulliganPanel.SetActive(false);
                    _gm.ConfirmMulligan(selected);
                });
        }

        private void RefreshDuel()
        {
            var G = _gm.G;
            _duelInfoText.text =
                $"=== 法术对决 ===\n" +
                $"战场:{G.duelBf}  攻方:{G.duelAttacker}  当前行动方:{G.duelTurn}\n" +
                $"跳过次数:{G.duelSkips}";
        }

        // ─────────────────────────────────────────────
        // 区域点击处理（出牌 / 移动目标）
        // ─────────────────────────────────────────────

        private void OnZoneClicked(string zone)
        {
            if (_sel.IsCardSelected)
            {
                // 打出手牌
                bool ok = _gm.PlayCard(_sel.SelectedUid, zone);
                if (ok) _sel.Clear();
                else    _sel.Clear();   // 即使失败也清空选中，避免卡死
                Refresh();
            }
            else if (_sel.IsUnitSelected)
            {
                // 移动单位
                _gm.MoveUnit(_sel.SelectedUid, zone);
                _sel.Clear();
                Refresh();
            }
        }

        private void OnPlayerUnitClicked(int uid)
        {
            // 如果当前有手牌选中，单击己方单位不触发移动（忽略）
            if (_sel.IsCardSelected) return;

            _sel.ToggleUnit(uid);
            Refresh();
        }

        // ─────────────────────────────────────────────
        // 拖拽出牌（CardDragHandler 回调）
        // ─────────────────────────────────────────────

        private void BeginCardDrag(int uid, Vector2 screenPos)
        {
            _dragUid = uid;

            // 构建跟随指针的幽灵卡牌（挂在 Canvas 根，不受布局影响）
            _dragGhost = new GameObject("DragGhost");
            _dragGhost.transform.SetParent(_rootCanvasRt, false);
            var rt = _dragGhost.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140, 34);
            rt.pivot     = new Vector2(0.5f, 0.5f);

            var img = _dragGhost.AddComponent<Image>();
            img.color         = new Color(0.10f, 0.28f, 0.32f, 0.85f);
            img.raycastTarget = false;

            var textGo = new GameObject("GhostLabel");
            textGo.transform.SetParent(_dragGhost.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<Text>();
            t.font            = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize        = 11;
            t.color           = C_Gold;
            t.alignment       = TextAnchor.MiddleCenter;
            t.raycastTarget   = false;
            var card = _gm.G.pHand.Find(c => c.uid == uid);
            t.text = card != null ? CardLabel(card) : "???";

            UpdateCardDrag(screenPos);
        }

        private void UpdateCardDrag(Vector2 screenPos)
        {
            if (_dragGhost == null) return;
            var canvas = _rootCanvasRt.GetComponent<Canvas>();
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rootCanvasRt, screenPos, cam, out var localPos))
                _dragGhost.GetComponent<RectTransform>().anchoredPosition = localPos;
        }

        private void EndCardDrag(int uid, Vector2 screenPos)
        {
            // 通过 RaycastAll 找落点区域
            var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
            var results   = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            string hitZone = null;
            foreach (var r in results)
            {
                var zone = r.gameObject.GetComponent<ZoneDropTarget>()
                        ?? r.gameObject.GetComponentInParent<ZoneDropTarget>();
                if (zone != null) { hitZone = zone.ZoneId; break; }
            }

            // 清理幽灵
            if (_dragGhost != null) { Destroy(_dragGhost); _dragGhost = null; }
            _dragUid = -1;

            if (hitZone != null)
            {
                _sel.SelectCard(uid);
                OnZoneClicked(hitZone);
            }
        }

        // ─────────────────────────────────────────────
        // 事件处理器
        // ─────────────────────────────────────────────

        private void AppendLog(string msg)
        {
            _logAccum += msg + "\n";
            if (_logText != null)
            {
                _logText.text = _logAccum;
                Canvas.ForceUpdateCanvases();
                if (_logScroll != null)
                    _logScroll.verticalNormalizedPosition = 0f;
            }
            // 重要事件推送 Toast（伤害/积分/阶段切换）
            if (ToastSystem.Instance != null && IsImportantLog(msg))
                ToastSystem.Instance.Show(msg, 1.8f);

            // 单位死亡 → 轻微震动
            if (_rootCanvasRt != null && msg.Contains("死亡"))
                StartCoroutine(UITween.Shake(_rootCanvasRt, 3.5f, 0.38f, 10));
        }

        private static bool IsImportantLog(string msg)
        {
            return msg.Contains("积分") || msg.Contains("战斗") || msg.Contains("死亡")
                || msg.Contains("征服") || msg.Contains("据守") || msg.Contains("进化")
                || msg.Contains("胜利") || msg.Contains("失败") || msg.Contains("对决");
        }

        private void HandleGameOver(Owner? winner)
        {
            string msg = winner == Owner.Player ? "你赢了！"
                       : winner == Owner.Enemy  ? "你输了！"
                       : "平局！";
            _gameOverText.text  = msg;
            _gameOverPanel.SetActive(true);
        }

        // ─────────────────────────────────────────────
        // Canvas 构建（Awake 调用）
        // ─────────────────────────────────────────────

        private void BuildCanvas()
        {
            // 根 Canvas
            var canvasGo = new GameObject("GameCanvas");
            var canvas   = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var root = canvas.transform;
            _rootCanvasRt = canvasGo.GetComponent<RectTransform>();

            // ── 背景底色（全屏，不受 SafeArea 限制）──
            var bgPanel = MakePanel(root, "Background", Vector2.zero, Vector2.one, C_Dark);
            _ = bgPanel;

            // ── SafeArea 容器（刘海/圆角/底部条安全区域适配）──
            var safeAreaGo = new GameObject("SafeArea");
            safeAreaGo.transform.SetParent(root, false);
            var safeAreaRt = safeAreaGo.AddComponent<RectTransform>();
            safeAreaRt.anchorMin = Vector2.zero;
            safeAreaRt.anchorMax = Vector2.one;
            safeAreaRt.offsetMin = safeAreaRt.offsetMax = Vector2.zero;
            safeAreaGo.AddComponent<SafeAreaFitter>();
            var gameRoot = safeAreaGo.transform;

            // ── 敌方信息栏（顶部 8%）──
            var enemyInfoPanel = MakePanel(gameRoot, "EnemyInfoPanel",
                new Vector2(0, 0.92f), new Vector2(0.75f, 1f), C_EnemyBg);
            _enemyInfoText = MakeText(enemyInfoPanel.transform, "EnemyInfoText", 13);
            _enemyInfoText.color = C_Gold;

            // ── 敌方区域（86-92%）──
            var enemyZonePanel = MakePanel(gameRoot, "EnemyZonePanel",
                new Vector2(0, 0.77f), new Vector2(0.75f, 0.92f), C_EnemyBg);
            _enemyZoneTrans = MakeScrollContent(enemyZonePanel.transform, "EnemyZoneContent",
                horizontal: true);

            // ── 战场（44-77%）──
            var bfPanel = MakePanel(gameRoot, "BattlefieldPanel",
                new Vector2(0, 0.34f), new Vector2(0.75f, 0.77f), C_BFBg);
            var bfLayout = bfPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            bfLayout.childControlWidth  = true;
            bfLayout.childControlHeight = true;
            bfLayout.spacing = 4;
            bfLayout.padding = new RectOffset(4, 4, 4, 4);

            _bf0Trans = MakeScrollContent(bfPanel.transform, "BF0Content", horizontal: false);
            _bf0PanelImg = _bf0Trans.gameObject.AddComponent<Image>();
            _bf0PanelImg.color = C_BFBg;

            _bf1Trans = MakeScrollContent(bfPanel.transform, "BF1Content", horizontal: false);
            _bf1PanelImg = _bf1Trans.gameObject.AddComponent<Image>();
            _bf1PanelImg.color = C_BFBg;

            // ── 玩家基地（25-34%）──
            var playerBasePanel = MakePanel(gameRoot, "PlayerBasePanel",
                new Vector2(0, 0.25f), new Vector2(0.75f, 0.34f), C_PlayBg);
            _playerBaseTrans = MakeScrollContent(playerBasePanel.transform, "PlayerBaseContent",
                horizontal: true);

            // ── 玩家符文（16-25%）──
            var playerRunePanel = MakePanel(gameRoot, "PlayerRunePanel",
                new Vector2(0, 0.16f), new Vector2(0.75f, 0.25f), C_RuneBg);
            _playerRuneTrans = MakeScrollContent(playerRunePanel.transform, "PlayerRuneContent",
                horizontal: true);

            // ── 玩家手牌（5-16%）──
            var playerHandPanel = MakePanel(gameRoot, "PlayerHandPanel",
                new Vector2(0, 0.05f), new Vector2(0.75f, 0.16f), C_HandBg);
            _playerHandTrans = MakeScrollContent(playerHandPanel.transform, "PlayerHandContent",
                horizontal: true);

            // ── 玩家信息 + 操作栏（0-5%）──
            var actionPanel = MakePanel(gameRoot, "ActionPanel",
                new Vector2(0, 0f), new Vector2(0.75f, 0.05f), C_DarkBg);
            var actionLayout = actionPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.childControlWidth  = false;
            actionLayout.childControlHeight = true;
            actionLayout.spacing = 6;
            actionLayout.padding = new RectOffset(4, 4, 2, 2);

            _playerInfoText = MakeText(actionPanel.transform, "PlayerInfoText", 12);
            _playerInfoText.color = C_Gold;
            var pInfoRt     = _playerInfoText.GetComponent<RectTransform>();
            pInfoRt.sizeDelta = new Vector2(600, 0);

            (_endTurnBtn, _endTurnBtnText) = MakeButton(actionPanel.transform, "结束回合", 14,
                () => { _sel.Clear(); _gm.PlayerEndTurn(); });
            _endTurnBtnText.color = C_Gold;

            (_legendBtn, _legendBtnText) = MakeButton(actionPanel.transform, "传奇技能", 12,
                () => { _gm.ActivateLegendAbility("ability1"); });

            MakeButton(actionPanel.transform, "废牌堆", 11,
                () => ShowDiscardPile());

            // ── 右侧日志面板（75-100%宽，全高）──
            var logPanel = MakePanel(gameRoot, "LogPanel",
                new Vector2(0.75f, 0f), new Vector2(1f, 1f), C_LogBg);

            AddLabel(logPanel.transform, "=== 战斗日志 ===", C_Cyan);

            var logScrollGo = new GameObject("LogScroll");
            logScrollGo.transform.SetParent(logPanel.transform, false);
            var logScrollRt = logScrollGo.AddComponent<RectTransform>();
            logScrollRt.anchorMin = new Vector2(0, 0);
            logScrollRt.anchorMax = new Vector2(1, 0.95f);
            logScrollRt.offsetMin = logScrollRt.offsetMax = Vector2.zero;
            _logScroll = logScrollGo.AddComponent<ScrollRect>();
            _logScroll.horizontal = false;

            var logContent = new GameObject("LogContent");
            logContent.transform.SetParent(logScrollGo.transform, false);
            var logContentRt = logContent.AddComponent<RectTransform>();
            logContentRt.anchorMin = new Vector2(0, 0);
            logContentRt.anchorMax = new Vector2(1, 1);
            logContentRt.pivot = new Vector2(0.5f, 1f);
            logContentRt.offsetMin = logContentRt.offsetMax = Vector2.zero;
            var logContentLayout = logContent.AddComponent<VerticalLayoutGroup>();
            logContentLayout.childControlHeight = false;
            logContentLayout.childForceExpandHeight = false;
            var logFitter = logContent.AddComponent<ContentSizeFitter>();
            logFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _logScroll.content = logContentRt;

            _logText = MakeText(logContent.transform, "LogText", 11);
            _logText.alignment = TextAnchor.UpperLeft;
            var logTextRt = _logText.GetComponent<RectTransform>();
            logTextRt.sizeDelta = new Vector2(0, 2000);

            var logViewport = new GameObject("LogViewport");
            logViewport.transform.SetParent(logScrollGo.transform, false);
            var logViewportRt = logViewport.AddComponent<RectTransform>();
            logViewportRt.anchorMin = Vector2.zero;
            logViewportRt.anchorMax = Vector2.one;
            logViewportRt.offsetMin = logViewportRt.offsetMax = Vector2.zero;
            logViewport.AddComponent<Mask>();
            logViewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            _logScroll.viewport = logViewportRt;

            // ── Mulligan 面板（全屏覆盖）──
            _mulliganPanel = MakePanel(gameRoot, "MulliganPanel",
                Vector2.zero, Vector2.one,
                new Color(0f, 0f, 0f, 0.88f)).gameObject;
            var mulLayout = _mulliganPanel.AddComponent<VerticalLayoutGroup>();
            mulLayout.childControlWidth  = true;
            mulLayout.childControlHeight = false;
            mulLayout.spacing = 8;
            mulLayout.padding = new RectOffset(40, 40, 40, 40);

            _mulliganInfoText = MakeText(_mulliganPanel.transform, "MulliganInfo", 16);
            _mulliganInfoText.alignment = TextAnchor.MiddleCenter;

            var mulCardsGo = new GameObject("MulliganCards");
            mulCardsGo.transform.SetParent(_mulliganPanel.transform, false);
            var mulCardsLayout = mulCardsGo.AddComponent<HorizontalLayoutGroup>();
            mulCardsLayout.childControlWidth  = false;
            mulCardsLayout.childControlHeight = true;
            mulCardsLayout.spacing = 8;
            mulCardsLayout.childAlignment = TextAnchor.MiddleCenter;
            _mulliganCardsTrans = mulCardsGo.transform;
            var mulRt = mulCardsGo.AddComponent<RectTransform>();
            mulRt.sizeDelta = new Vector2(0, 120);
            _mulliganPanel.SetActive(false);

            // ── 对决面板（全屏覆盖）──
            _duelPanel = MakePanel(gameRoot, "DuelPanel",
                new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.7f),
                new Color(0.05f, 0.05f, 0.2f, 0.95f)).gameObject;
            var duelLayout = _duelPanel.AddComponent<VerticalLayoutGroup>();
            duelLayout.childControlWidth = true;
            duelLayout.childControlHeight = false;
            duelLayout.spacing = 10;
            duelLayout.padding = new RectOffset(20, 20, 20, 20);

            _duelInfoText = MakeText(_duelPanel.transform, "DuelInfo", 16);
            _duelInfoText.alignment = TextAnchor.MiddleCenter;

            var duelBtns = new GameObject("DuelButtons");
            duelBtns.transform.SetParent(_duelPanel.transform, false);
            var duelBtnLayout = duelBtns.AddComponent<HorizontalLayoutGroup>();
            duelBtnLayout.childControlWidth  = false;
            duelBtnLayout.childControlHeight = true;
            duelBtnLayout.spacing = 10;
            duelBtnLayout.childAlignment = TextAnchor.MiddleCenter;

            MakeButton(duelBtns.transform, "跳过（Skip）", 14, () => _gm.DuelSkip());
            _duelPanel.SetActive(false);

            // ── 游戏结束面板 ──
            _gameOverPanel = MakePanel(gameRoot, "GameOverPanel",
                new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.6f),
                new Color(0f, 0f, 0f, 0.9f)).gameObject;
            _gameOverText = MakeText(_gameOverPanel.transform, "GameOverText", 24);
            _gameOverText.alignment = TextAnchor.MiddleCenter;
            MakeButton(_gameOverPanel.transform, "再来一局", 16,
                () =>
                {
                    _gameOverPanel.SetActive(false);
                    _sel.Clear();
                    _prevHandUids.Clear();
                    _prevRuneCount = 0;
                    _lastPhase = GamePhase.Init;
                    _lastTurn  = Owner.Player;
                    _titlePanel.SetActive(true);
                });
            _gameOverPanel.SetActive(false);

            // ── 弃牌堆查看面板 ──
            _discardPanel = MakePanel(gameRoot, "DiscardPanel",
                new Vector2(0.12f, 0.08f), new Vector2(0.75f, 0.92f),
                new Color(0.05f, 0.03f, 0.12f, 0.97f)).gameObject;
            var discLayout = _discardPanel.AddComponent<VerticalLayoutGroup>();
            discLayout.childControlWidth  = true;
            discLayout.childControlHeight = false;
            discLayout.spacing = 8;
            discLayout.padding = new RectOffset(18, 18, 14, 14);

            _discardText = MakeText(_discardPanel.transform, "DiscardText", 12);
            _discardText.alignment = TextAnchor.UpperLeft;
            _discardText.color     = Color.white;
            _discardText.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 600);

            MakeButton(_discardPanel.transform, "关闭", 14,
                () => _discardPanel.SetActive(false));
            _discardPanel.SetActive(false);

            // ── 阶段切换横幅 ──
            var bannerPanel = MakePanel(gameRoot, "PhaseBanner",
                new Vector2(0.2f, 0.44f), new Vector2(0.8f, 0.56f),
                new Color(0f, 0f, 0f, 0.82f));
            _phaseBanner = bannerPanel.gameObject;
            _phaseBannerText = MakeText(_phaseBanner.transform, "PhaseBannerText", 20);
            _phaseBannerText.alignment = TextAnchor.MiddleCenter;
            _phaseBannerText.color     = C_Gold;
            _phaseBanner.AddComponent<CanvasGroup>();  // 预先添加，避免懒加载重复 AddComponent
            _phaseBanner.SetActive(false);

            // ── 卡牌详情预览面板（居中模态框）──
            _cardDetailPanel = MakePanel(gameRoot, "CardDetailPanel",
                new Vector2(0.12f, 0.08f), new Vector2(0.75f, 0.92f),
                new Color(0.03f, 0.06f, 0.14f, 0.97f)).gameObject;
            var detailLayout = _cardDetailPanel.AddComponent<VerticalLayoutGroup>();
            detailLayout.childControlWidth   = true;
            detailLayout.childControlHeight  = false;
            detailLayout.spacing             = 8;
            detailLayout.padding             = new RectOffset(18, 18, 14, 14);

            _cardDetailText = MakeText(_cardDetailPanel.transform, "CardDetailText", 14);
            _cardDetailText.alignment = TextAnchor.UpperLeft;
            _cardDetailText.color     = Color.white;
            var detTextRt = _cardDetailText.GetComponent<RectTransform>();
            detTextRt.sizeDelta = new Vector2(0, 400);

            MakeButton(_cardDetailPanel.transform, "关闭", 14,
                () => _cardDetailPanel.SetActive(false));
            _cardDetailPanel.SetActive(false);

            // ── 标题界面（全屏覆盖，最后添加 = 最高层）──
            _titlePanel = MakePanel(root, "TitlePanel",
                Vector2.zero, Vector2.one,
                C_Dark).gameObject;
            var titleLayout = _titlePanel.AddComponent<VerticalLayoutGroup>();
            titleLayout.childControlWidth   = true;
            titleLayout.childControlHeight  = false;
            titleLayout.childAlignment      = TextAnchor.MiddleCenter;
            titleLayout.spacing             = 20;
            titleLayout.padding             = new RectOffset(0, 0, 160, 0);

            var titleText = MakeText(_titlePanel.transform, "TitleText", 48);
            titleText.text      = "风舞天际";
            titleText.color     = C_Gold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 70);

            var subtitleText = MakeText(_titlePanel.transform, "SubtitleText", 18);
            subtitleText.text      = "Trading Card Game";
            subtitleText.color     = C_Cyan;
            subtitleText.alignment = TextAnchor.MiddleCenter;
            subtitleText.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 30);

            var startBtnGo = new GameObject("StartBtnRow");
            startBtnGo.transform.SetParent(_titlePanel.transform, false);
            startBtnGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 52);
            var startRowLayout = startBtnGo.AddComponent<HorizontalLayoutGroup>();
            startRowLayout.childAlignment     = TextAnchor.MiddleCenter;
            startRowLayout.childControlWidth  = false;
            startRowLayout.childControlHeight = true;

            var (startBtn, startBtnLbl) = MakeButton(startBtnGo.transform, "开始游戏", 20,
                () =>
                {
                    _titlePanel.SetActive(false);
                    _gm.StartGame(DeckFactory.MakeKaisaVsMasterYi());
                });
            startBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 48);
            startBtnLbl.color = C_Gold;
            _ = startBtn;
        }

        // ─────────────────────────────────────────────
        // uGUI 工厂方法
        // ─────────────────────────────────────────────

        private static Image MakePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Color bg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = bg;
            return img;
        }

        private static Transform MakeScrollContent(Transform parent, string name, bool horizontal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            if (horizontal)
            {
                var layout = go.AddComponent<HorizontalLayoutGroup>();
                layout.childControlWidth  = false;
                layout.childControlHeight = true;
                layout.spacing = 4;
                layout.padding = new RectOffset(4, 4, 4, 4);
                layout.childAlignment = TextAnchor.MiddleLeft;
            }
            else
            {
                var layout = go.AddComponent<VerticalLayoutGroup>();
                layout.childControlWidth  = true;
                layout.childControlHeight = false;
                layout.spacing = 2;
                layout.padding = new RectOffset(4, 4, 4, 4);
            }
            return go.transform;
        }

        private static Text MakeText(Transform parent, string name, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font     = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fontSize;
            t.color    = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        private static (Button btn, Text label) MakeButton(Transform parent, string label,
            int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var go  = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120, 32);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.25f, 0.35f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.5f, 1f);
            colors.pressedColor     = new Color(0.15f, 0.15f, 0.25f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = fontSize;
            t.color     = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.text      = label;
            return (btn, t);
        }

        // ─────────────────────────────────────────────
        // 动态元素构建方法（Refresh 内调用）
        // ─────────────────────────────────────────────

        private static void AddLabel(Transform parent, string text, Color col)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 28);
            var t = go.AddComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = 12;
            t.color     = col;
            t.alignment = TextAnchor.MiddleLeft;
            t.text      = text;
        }

        private void AddUnitButton(Transform parent, CardInstance u, Owner owner, bool selected)
        {
            int capturedUid = u.uid;
            Color col = selected ? Color.cyan : Color.white;

            var (btn, lbl) = MakeButton(parent, UnitLabel(u), 11,
                () => OnPlayerUnitClicked(capturedUid));
            _ = btn;
            lbl.color = col;
        }

        private void AddZoneButton(Transform parent, string label, string zone, Color col)
        {
            var (btn, lbl) = MakeButton(parent, label, 11, () => OnZoneClicked(zone));
            lbl.color = col;
            btn.gameObject.AddComponent<ZoneDropTarget>().ZoneId = zone;
        }

        private static Button AddButton(Transform parent, string label, Color col,
            UnityEngine.Events.UnityAction onClick, Color? bgColor = null)
        {
            var (btn, lbl) = MakeButton(parent, label, 11, onClick);
            lbl.color = col;
            if (bgColor.HasValue)
                btn.GetComponent<Image>().color = bgColor.Value;
            return btn;
        }

        /// <summary>
        /// 为符文行创建一个水平 GameObject 容器。
        /// </summary>
        private static Transform AddHorizontalGroup(Transform parent)
        {
            var go = new GameObject("RuneRow");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 28);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth  = false;
            layout.childControlHeight = true;
            layout.spacing = 4;
            return go.transform;
        }

        private static void AddSmallButton(Transform parent, string label, Color col,
            UnityEngine.Events.UnityAction onClick)
        {
            var go  = new GameObject("SmallBtn_" + label);
            go.transform.SetParent(parent, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(44, 22);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("L");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<Text>();
            t.font     = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 10;
            t.color    = col;
            t.alignment = TextAnchor.MiddleCenter;
            t.text     = label;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject);
        }

        // ─────────────────────────────────────────────
        // 工具方法
        // ─────────────────────────────────────────────

        private static string CardLabel(CardInstance c)
        {
            string cost = c.cost > 0 ? $"[{c.cost}]" : "";
            string sch  = c.schCost > 0 ? $"[{c.schCost}{c.schType}]" : "";
            string stat = (c.type == CardType.Follower || c.type == CardType.Champion)
                ? $" {c.currentAtk}/{c.currentHp}"
                : "";
            return $"{cost}{sch}{c.cardName}{stat}";
        }

        private static string UnitLabel(CardInstance u)
        {
            string ex  = u.exhausted ? " [疲劳]" : "";
            string stun = u.stunned  ? " [眩晕]" : "";
            string kw  = u.keywords?.Count > 0 ? " " + string.Join(",", u.keywords) : "";
            return $"{u.cardName} {u.currentAtk}/{u.currentHp}{ex}{stun}{kw}";
        }

        private static string RuneLabel(RuneInstance r)
        {
            string state = r.tapped ? "[横]" : "[立]";
            return $"{state}{r.runeType}";
        }

        // ─────────────────────────────────────────────
        // 视觉特效辅助
        // ─────────────────────────────────────────────

        /// <summary>
        /// 战场控制光晕持续循环：每帧根据 bf[0]/bf[1].ctrl 更新 BF 面板背景色。
        /// Player = 青色光晕，Enemy = 红色光晕，Neutral = 暗色基准。
        /// </summary>
        private IEnumerator BFGlowLoop()
        {
            while (true)
            {
                if (_gm != null)
                {
                    float pulse = (Mathf.Sin(Time.time * 1.5f) + 1f) * 0.5f;  // 0→1→0，约 4.2s 周期
                    float glow  = UITween.ApplyEasePublic(pulse, UITween.Ease.InOutQuad) * 0.30f;

                    if (_bf0PanelImg != null)
                    {
                        Color target = BFCtrlColor(_gm.G.bf[0].ctrl);
                        _bf0PanelImg.color = Color.Lerp(C_BFBg, target, glow);
                    }
                    if (_bf1PanelImg != null)
                    {
                        Color target = BFCtrlColor(_gm.G.bf[1].ctrl);
                        _bf1PanelImg.color = Color.Lerp(C_BFBg, target, glow);
                    }
                }
                yield return null;
            }
        }

        private void ShowDiscardPile()
        {
            var G = _gm.G;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"=== 我方废牌堆 ({G.pDiscard.Count}) ===");
            foreach (var c in G.pDiscard)
                sb.AppendLine($"  {CardLabel(c)}");

            sb.AppendLine();
            sb.AppendLine($"=== 敌方废牌堆 ({G.eDiscard.Count}) ===");
            foreach (var c in G.eDiscard)
                sb.AppendLine($"  {CardLabel(c)}");

            _discardText.text = sb.ToString();
            _discardPanel.SetActive(true);
        }

        private void ShowPhaseBanner(string text)
        {
            _phaseBannerText.text = text;
            _phaseBanner.SetActive(true);
            StartCoroutine(BannerSequence());
        }

        private IEnumerator BannerSequence()
        {
            var cg = _phaseBanner.GetComponent<CanvasGroup>();
            yield return UITween.FadeIn(cg, 0.25f);
            yield return new WaitForSeconds(1.1f);
            yield return UITween.FadeOut(cg, 0.30f, UITween.Ease.OutQuad,
                () => _phaseBanner.SetActive(false));
        }

        private static IEnumerator DelayedPopIn(RectTransform rt, float duration, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return UITween.PopIn(rt, duration);
        }

        private void ShowCardDetail(CardInstance card)
        {
            _cardDetailText.text = FormatCardDetail(card);
            _cardDetailPanel.SetActive(true);
        }

        private static string FormatCardDetail(CardInstance c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"【{c.cardName}】");
            sb.AppendLine($"类型: {c.type}  地域: {c.region}  费用: {c.cost}");
            if (c.schCost > 0)
                sb.AppendLine($"符能费: {c.schCost} {c.schType}");
            if (c.type == CardType.Follower || c.type == CardType.Champion)
                sb.AppendLine($"战力: {c.currentAtk}/{c.currentHp}");
            if (c.keywords != null && c.keywords.Count > 0)
                sb.AppendLine($"关键词: {string.Join(" · ", c.keywords)}");
            if (!string.IsNullOrEmpty(c.text))
            {
                sb.AppendLine();
                sb.AppendLine(c.text);
            }
            return sb.ToString();
        }

        private static string PhaseName(GamePhase p) => p switch
        {
            GamePhase.Awaken => "觉醒阶段",
            GamePhase.Start  => "开始阶段",
            GamePhase.Summon => "召唤阶段",
            GamePhase.Draw   => "摸牌阶段",
            GamePhase.Action => "行动阶段",
            GamePhase.End    => "结束阶段",
            _                => p.ToString(),
        };

        /// <summary>积分轨道文字表示，如 "■■■□□□□□"。</summary>
        private static string ScoreBar(int score)
        {
            int clamped = Mathf.Clamp(score, 0, GameState.WIN_SCORE);
            return new string('■', clamped) + new string('□', GameState.WIN_SCORE - clamped);
        }

        private static Color BFCtrlColor(Owner? ctrl) => ctrl switch
        {
            Owner.Player => C_Cyan,
            Owner.Enemy  => new Color(0.90f, 0.20f, 0.20f),
            _            => new Color(0.40f, 0.40f, 0.40f),
        };

        private static Color RuneColor(RuneType t) => t switch
        {
            RuneType.Blazing  => C_RuneBlazing,
            RuneType.Radiant  => C_RuneRadiant,
            RuneType.Verdant  => C_RuneVerdant,
            RuneType.Crushing => C_RuneCrushing,
            RuneType.Chaos    => C_RuneChaos,
            RuneType.Order    => C_RuneOrder,
            _                 => Color.white,
        };

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }
        }
    }
}
