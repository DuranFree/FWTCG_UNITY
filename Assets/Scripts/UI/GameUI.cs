using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private int           _prevPScore    = 0;  // 积分脉冲追踪（我方）
        private int           _prevEScore    = 0;  // 积分脉冲追踪（敌方）
        private HashSet<int>  _prevPBaseUids = new(); // 落地震动追踪（玩家基地）
        private HashSet<int>  _prevPBFUids   = new(); // 落地震动追踪（玩家战场）
        private CanvasGroup   _gameRootCg;            // 标题→游戏淡入
        private string        _prevBF0CardId = "";    // 战场名称飞入追踪
        private string        _prevBF1CardId = "";

        // P30: 字体资源（Awake 加载）
        private Font _cinzelFont;
        private Font _cinzelBold;
        private Font _courierFont;

        // P30: 标题光效追踪
        private Text      _titleText;
        private Coroutine _titlePulseRoutine;

        // P28: 翻币界面
        private GameObject _coinPanel;
        private Text       _coinResultText;
        private bool       _coinPanelShowing;         // 翻币面板显示期间阻止 Mulligan 弹出

        // P28: Mulligan 首次弹入追踪
        private bool _mulliganPopInDone;

        // P28: 单位死亡飞出（uid→名称 映射）
        private readonly Dictionary<int, string> _unitNames = new();

        // P29: Buff/Debuff 光晕 + 眩晕光晕追踪
        private readonly Dictionary<int, int> _prevUnitAtk    = new();
        private readonly HashSet<int>         _prevStunnedUids = new();

        // P29: 日志折叠
        private GameObject _logScrollGo;
        private bool       _logVisible    = true;
        private Text       _logToggleText;          // 折叠按钮文字（▼/▶）

        // P31: 积分轨道圆圈（玩家8个 + 敌方8个）
        private readonly Image[] _pScoreCircles = new Image[8];
        private readonly Image[] _eScoreCircles = new Image[8];

        // P31: 传奇牌槽显示引用
        private Text  _pLegSlotStats;   // 玩家传奇：名/HP/ATK
        private Image _pLegSlotHpFill;  // 玩家传奇 HP 填充条
        private Text  _pLegSlotEmoji;   // 玩家传奇 emoji
        private Text  _eLegSlotStats;
        private Image _eLegSlotHpFill;
        private Text  _eLegSlotEmoji;

        // P31: 日志浮动覆盖层
        private GameObject _logOverlayPanel;

        // P33: 法术目标区域高亮
        private Image _enemyZonePanelImg;
        private Image _playerBasePanelImg;
        private readonly List<(Image img, Color orig, Coroutine co)> _spellZoneGlows = new();

        // P32: 战斗闪光覆盖层
        private GameObject _combatOverlay;
        private Text        _combatOverlayText;
        private bool        _combatFlashRunning;

        // P35: 拖拽漩涡
        private readonly List<GameObject> _dropVortices = new();

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

            // P30: 加载字体资源（Resources/Fonts/ 目录）
            _cinzelFont  = Resources.Load<Font>("Fonts/Cinzel-Regular");
            _cinzelBold  = Resources.Load<Font>("Fonts/Cinzel-Bold");
            _courierFont = Font.CreateDynamicFontFromOSFont("Courier New", 13);

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

            // 模拟换牌阶段：只显示 mulligan 面板（翻币面板显示期间跳过）
            if (G.phase == GamePhase.Init && G.pHand.Count > 0 && !_coinPanelShowing)
            {
                _mulliganPanel.SetActive(true);
                if (!_mulliganPopInDone)
                {
                    _mulliganPopInDone = true;
                    StartCoroutine(UITween.PopIn(_mulliganPanel.GetComponent<RectTransform>(), 0.4f));
                }
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
            RefreshScoreTracks();          // P31: 积分轨道圆圈颜色
            RefreshLegendSlots();          // P31: 传奇牌槽数据
            RefreshSpellTargetHighlight(); // P33: 法术目标区域高亮
            RebuildUnitTracking();         // P29: 每帧末尾更新 Buff/眩晕追踪基准值
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

            // 积分增加时红色脉冲
            if (G.eScore > _prevEScore)
                StartCoroutine(UITween.PulseColor(_enemyInfoText,
                    new Color(1f, 0.27f, 0.27f), 0.5f));
            _prevEScore = G.eScore;
        }

        private void RefreshEnemyZone()
        {
            ClearChildren(_enemyZoneTrans);
            var G = _gm.G;
            // P31: 敌方基地单位改为小卡片（compact 横版）
            foreach (var u in G.eBase)
                AddUnitCard(_enemyZoneTrans, u, Owner.Enemy, false, compact: true);
        }

        private void RefreshBattlefields()
        {
            var G = _gm.G;
            RefreshBF(0, _bf0Trans, G.bf[0]);
            RefreshBF(1, _bf1Trans, G.bf[1]);

            // 战场 pU 追踪更新（两个战场都跑完后统一更新）
            _prevPBFUids.Clear();
            foreach (var b in G.bf)
                foreach (var u in b.pU)
                    _prevPBFUids.Add(u.uid);
        }

        private void RefreshBF(int bfIdx, Transform trans, BattlefieldState bf)
        {
            DetectDeathsAndAnimate(trans, bf.pU); // P28: 玩家侧死亡飞出
            ClearChildren(trans);
            var G = _gm.G;

            // 战场标题（战场牌首次出现时 PopIn 飞入）
            string ctrl    = bf.ctrl == null ? "中立" : (bf.ctrl == Owner.Player ? "我方" : "敌方");
            string cardId  = bf.card?.id ?? "";
            string prevId  = bfIdx == 0 ? _prevBF0CardId : _prevBF1CardId;
            var    titleRt = AddLabelRt(trans,
                $"=== 战场{bf.id} [{ctrl}] BF卡:{bf.card?.cardName ?? "-"} ===",
                Color.yellow);

            if (!string.IsNullOrEmpty(cardId) && cardId != prevId)
                StartCoroutine(UITween.PopIn(titleRt, 0.4f));

            if (bfIdx == 0) _prevBF0CardId = cardId;
            else            _prevBF1CardId = cardId;

            // P31: 敌方单位 — 竖版小卡片
            foreach (var u in bf.eU)
                AddUnitCard(trans, u, Owner.Enemy, false);

            // P31: 玩家单位 — 竖版小卡片（可点击）
            foreach (var u in bf.pU)
            {
                bool isSel = _sel.IsUnitSelected && _sel.SelectedUid == u.uid;
                bool isNew = !_prevPBFUids.Contains(u.uid);
                AddUnitCard(trans, u, Owner.Player, isSel, isNew);
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
            var G = _gm.G;
            DetectDeathsAndAnimate(_playerBaseTrans, G.pBase); // P28: 死亡飞出
            ClearChildren(_playerBaseTrans);

            // P31: 玩家基地单位 — compact 横版小卡片
            foreach (var u in G.pBase)
            {
                bool isSel = _sel.IsUnitSelected && _sel.SelectedUid == u.uid;
                bool isNew = !_prevPBaseUids.Contains(u.uid);
                AddUnitCard(_playerBaseTrans, u, Owner.Player, isSel, isNew, compact: true);
            }

            if (_gm.IsPlayerTurn)
            {
                bool isTarget = _sel.IsCardSelected || _sel.IsUnitSelected;
                AddZoneButton(_playerBaseTrans, "[部署到基地]", "base", isTarget ? Color.cyan : Color.gray);
            }

            // 更新追踪集合
            _prevPBaseUids.Clear();
            foreach (var u in G.pBase) _prevPBaseUids.Add(u.uid);
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

                var row   = AddHorizontalGroup(_playerRuneTrans);
                var rowRt = row.GetComponent<RectTransform>(); // P27 飞行动画用
                string capturedLabel = runeLabel;
                Color  capturedCol   = runeCol;

                // 显示标签
                AddLabel(row, runeLabel, runeCol);

                if (_gm.IsPlayerTurn)
                {
                    // 横置按钮
                    if (!r.tapped)
                        AddSmallButton(row, "横置", Color.green,
                            () => { _gm.TapRune(capturedIdx); });

                    // 回收按钮 — P27：先生成飞行 ghost，再执行回收
                    AddSmallButton(row, "回收", Color.yellow,
                        () =>
                        {
                            // 获取符文行的画布局部坐标（此时 row 仍在场景中）
                            var canvas = _rootCanvasRt.GetComponent<Canvas>();
                            var cam    = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                                         ? null : canvas.worldCamera;
                            Vector3[] corners = new Vector3[4];
                            rowRt.GetWorldCorners(corners);
                            var worldCenter = (corners[0] + corners[2]) * 0.5f;
                            var screenPt    = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
                            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                    _rootCanvasRt, screenPt, cam, out var localPos))
                                StartCoroutine(RuneRecycleFly(localPos, capturedLabel, capturedCol));
                            _gm.RecycleRune(capturedIdx);
                            Refresh();
                        });
                }

                // 新抽到的符文播放错落入场动画（每张间隔 50ms）
                if (i >= oldCount)
                {
                    float delay = (i - oldCount) * 0.05f;
                    StartCoroutine(DelayedPopIn(rowRt, 0.25f, delay)); // rowRt 已在上方声明
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
            int n = G.pHand.Count;
            if (n == 0) return;

            const float arcRadius  = 700f;
            const float maxAngleDeg = 20f;

            var currentUids = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                var card = G.pHand[i];
                currentUids.Add(card.uid);

                // 弧线角度：-maxAngle（左）→ +maxAngle（右）线性插值
                float t        = n == 1 ? 0f : (i / (float)(n - 1)) * 2f - 1f;
                float angleDeg = t * maxAngleDeg;
                float angleRad = angleDeg * Mathf.Deg2Rad;

                // 弧线位置（圆心在容器底部以下 arcRadius 处）
                float xPos = arcRadius * Mathf.Sin(angleRad);
                float yPos = arcRadius * (Mathf.Cos(angleRad) - 1f); // 0 at center, negative at edges

                var cardRt = MkHandCard(_playerHandTrans, card);
                // 以容器底部中心为锚点，手动放置
                cardRt.anchorMin        = new Vector2(0.5f, 0f);
                cardRt.anchorMax        = new Vector2(0.5f, 0f);
                cardRt.pivot            = new Vector2(0.5f, 0f);
                cardRt.anchoredPosition = new Vector2(xPos, yPos + 4f);
                cardRt.localEulerAngles = new Vector3(0f, 0f, -angleDeg);

                // 仅对新进入手牌的卡播放入场动画
                if (!_prevHandUids.Contains(card.uid))
                    StartCoroutine(UITween.PopIn(cardRt, 0.28f));
            }
            _prevHandUids.Clear();
            _prevHandUids.UnionWith(currentUids);
        }

        /// <summary>P32 — 构建单张手牌卡片（76×110px 竖版）：emoji / 名称 / 费用 / 数值。</summary>
        private RectTransform MkHandCard(Transform parent, CardInstance card)
        {
            int  capturedUid  = card.uid;
            var  capturedCard = card;
            bool isSel        = _sel.IsCardSelected && _sel.SelectedUid == card.uid;
            bool canPlay      = _gm.IsPlayerTurn && _gm.CD.CanPlay(card, Owner.Player);

            Color bgCol     = isSel    ? new Color(0.10f, 0.28f, 0.32f, 0.97f)
                            : canPlay  ? new Color(0.06f, 0.22f, 0.12f, 0.97f)
                                       : new Color(0.10f, 0.12f, 0.18f, 0.97f);
            Color borderCol = isSel    ? C_Cyan
                            : canPlay  ? new Color(0.25f, 0.91f, 0.54f, 0.9f)
                                       : new Color(C_Gold.r, C_Gold.g, C_Gold.b, 0.4f);

            // ── 容器 ──
            var go = new GameObject($"HandCard_{capturedUid}");
            go.transform.SetParent(parent, false);
            var rt        = go.AddComponent<RectTransform>();
            rt.sizeDelta  = new Vector2(76f, 110f);

            var bg        = go.AddComponent<Image>();
            bg.color      = bgCol;

            // 描边（1px 金/绿/青）
            var bdrGo     = new GameObject("Border");
            bdrGo.transform.SetParent(go.transform, false);
            var bdrRt     = bdrGo.AddComponent<RectTransform>();
            bdrRt.anchorMin = Vector2.zero; bdrRt.anchorMax = Vector2.one;
            bdrRt.offsetMin = bdrRt.offsetMax = Vector2.zero;
            var bdrImg    = bdrGo.AddComponent<Image>();
            bdrImg.color  = borderCol; bdrImg.raycastTarget = false;

            var fillGo    = new GameObject("Fill");
            fillGo.transform.SetParent(go.transform, false);
            var fillRt    = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1f, 1f); fillRt.offsetMax = new Vector2(-1f, -1f);
            var fillImg   = fillGo.AddComponent<Image>();
            fillImg.color = bgCol; fillImg.raycastTarget = false;

            // ── 费用（左上角圆形区）──
            var costTxt   = MakeTextAt(go.transform, "Cost",
                new Vector2(0f, 0.80f), new Vector2(0.38f, 1f),
                card.cost.ToString(), 14, canPlay ? C_Cyan : Color.gray, bold: true);

            // ── Emoji（中上）──
            MakeTextAt(go.transform, "Emoji",
                new Vector2(0.08f, 0.46f), new Vector2(0.92f, 0.80f),
                card.emoji ?? "?", 22, Color.white);

            // ── 卡名（中间）──
            var nameTxt   = MakeTextAt(go.transform, "Name",
                new Vector2(0f, 0.30f), new Vector2(1f, 0.46f),
                card.cardName, 8,
                isSel ? Color.cyan : canPlay ? new Color(0.25f, 0.91f, 0.54f) : Color.white);
            nameTxt.resizeTextForBestFit = true;
            nameTxt.resizeTextMinSize    = 6;
            nameTxt.resizeTextMaxSize    = 9;
            if (_cinzelFont != null) nameTxt.font = _cinzelFont;

            // ── 底部：ATK/HP（单位）或 类型标签（法术/装备）──
            bool isUnit = card.type == CardType.Follower || card.type == CardType.Champion;
            if (isUnit)
            {
                MakeTextAt(go.transform, "Stats",
                    new Vector2(0f, 0.02f), new Vector2(1f, 0.30f),
                    $"{card.atk}/{card.atk}", 9, C_Gold);
            }
            else
            {
                Color typeCol = card.type == CardType.Spell
                    ? new Color(0.4f, 0.6f, 1f) : C_Cyan;
                string typeLabel = card.type == CardType.Spell ? "法术" : "装备";
                MakeTextAt(go.transform, "TypeLabel",
                    new Vector2(0f, 0.02f), new Vector2(1f, 0.30f),
                    typeLabel, 9, typeCol);
            }

            // ── 点击按钮 ──
            var btn        = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var btnCols    = btn.colors;
            btnCols.highlightedColor = new Color(0.14f, 0.28f, 0.42f, 1f);
            btnCols.pressedColor     = new Color(0.04f, 0.10f, 0.18f, 1f);
            btn.colors     = btnCols;
            btn.onClick.AddListener(() => { _sel.ToggleCard(capturedUid); Refresh(); });

            // ── 悬停缩放 ──
            go.AddComponent<HoverScale>();

            // ── 拖拽出牌（可出时）──
            if (canPlay)
            {
                var drag           = go.AddComponent<CardDragHandler>();
                drag.CardUid       = capturedUid;
                drag.OnBeginDragCb = (id, pos) => BeginCardDrag(id, pos);
                drag.OnDragCb      = pos        => UpdateCardDrag(pos);
                drag.OnEndDragCb   = (id, pos)  => EndCardDrag(id, pos);
            }

            // ── P33: 旋转光弧（可打出时）──
            if (canPlay)
            {
                var glowGo  = new GameObject("CanPlayGlow");
                glowGo.transform.SetParent(go.transform, false);
                var glowRt  = glowGo.AddComponent<RectTransform>();
                glowRt.anchorMin = Vector2.zero;
                glowRt.anchorMax = Vector2.one;
                glowRt.offsetMin = new Vector2(-4f, -4f);
                glowRt.offsetMax = new Vector2(4f,  4f);
                var glowImg = glowGo.AddComponent<Image>();
                glowImg.type       = Image.Type.Filled;
                glowImg.fillMethod = Image.FillMethod.Radial360;
                glowImg.fillAmount = 0.22f;  // ~80° 弧 = 彗星尾
                glowImg.color      = new Color(0.25f, 0.91f, 0.54f, 0.55f);
                glowImg.raycastTarget = false;
                glowGo.AddComponent<CanPlayGlow>();
                glowGo.transform.SetAsFirstSibling(); // 渲染在卡背景之上、其余内容之下
            }

            // ── P34: 全息扫光（可打出时）──
            if (canPlay)
            {
                var sweepGo  = new GameObject("FoilSweep");
                sweepGo.transform.SetParent(go.transform, false);
                var sweepRt  = sweepGo.AddComponent<RectTransform>();
                sweepRt.anchorMin        = new Vector2(0.5f, 0f);
                sweepRt.anchorMax        = new Vector2(0.5f, 1f);
                sweepRt.pivot            = new Vector2(0.5f, 0.5f);
                sweepRt.sizeDelta        = new Vector2(28f, 0f);
                sweepRt.localEulerAngles = new Vector3(0f, 0f, 20f); // 斜向 20°
                var sweepImg = sweepGo.AddComponent<Image>();
                sweepImg.color        = new Color(1f, 1f, 1f, 0.18f);
                sweepImg.raycastTarget = false;
                sweepGo.AddComponent<FoilSweep>();
            }

            // ── 详情按钮（右上角小覆盖）──
            var detGo     = new GameObject("DetailBtn");
            detGo.transform.SetParent(go.transform, false);
            var detRt     = detGo.AddComponent<RectTransform>();
            detRt.anchorMin = new Vector2(0.62f, 0.82f);
            detRt.anchorMax = new Vector2(1f, 1f);
            detRt.offsetMin = new Vector2(0f, -2f); detRt.offsetMax = new Vector2(-2f, 0f);
            var detImg    = detGo.AddComponent<Image>();
            detImg.color  = new Color(0.18f, 0.18f, 0.18f, 0.85f);
            var detBtn    = detGo.AddComponent<Button>();
            detBtn.targetGraphic = detImg;
            detBtn.onClick.AddListener(() => ShowCardDetail(capturedCard));
            var detTxt    = MakeTextAt(detGo.transform, "L",
                Vector2.zero, Vector2.one, "详", 8, C_Gold);
            _ = detTxt; // suppress unused warning

            return rt;
        }

        /// <summary>P32 内部辅助 — 快速在指定锚点范围创建 Text 并返回。</summary>
        private Text MakeTextAt(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax,
            string text, int fontSize, Color color, bool bold = false)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(2f, 1f); rt.offsetMax = new Vector2(-2f, -1f);
            var t   = go.AddComponent<Text>();
            t.text      = text;
            t.fontSize  = fontSize;
            t.color     = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (bold) t.fontStyle = FontStyle.Bold;
            return t;
        }

        // ─────────────────────────────────────────────
        // P34 — 背景环境光辅助方法
        // ─────────────────────────────────────────────

        /// <summary>P34 — 在指定父级添加 3 层径向渐变环境光（青/金双色，极低 alpha）。</summary>
        private static void BuildAmbientLights(Transform parent)
        {
            var spr = MakeRadialGradientSprite(256);
            // 青色：右上角，直径 900px
            MakeAmbientCircle(parent, "AmbLight0", spr,
                new Color(0.04f, 0.78f, 0.73f, 0.04f),
                new Vector2(0.75f, 0.75f), new Vector2(900f, 900f));
            // 金色：左下角，直径 700px
            MakeAmbientCircle(parent, "AmbLight1", spr,
                new Color(0.78f, 0.67f, 0.43f, 0.03f),
                new Vector2(0.25f, 0.25f), new Vector2(700f, 700f));
            // 青色：屏幕中央，直径 1200px（超出屏边营造晕染感）
            MakeAmbientCircle(parent, "AmbLight2", spr,
                new Color(0.04f, 0.78f, 0.73f, 0.025f),
                new Vector2(0.5f, 0.5f), new Vector2(1200f, 1200f));
        }

        private static void MakeAmbientCircle(Transform parent, string name, Sprite sprite,
            Color col, Vector2 anchorPos, Vector2 size)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.anchorMin        = anchorPos;
            rt.anchorMax        = anchorPos;
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = size;
            var img = go.AddComponent<Image>();
            img.sprite          = sprite;
            img.color           = col;
            img.raycastTarget   = false;
        }

        /// <summary>P34 — 生成 size×size 径向渐变 Sprite（中心 alpha=1 → 边缘 alpha=0）。</summary>
        private static Sprite MakeRadialGradientSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist  = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1f - dist / center);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
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

            // 积分增加时绿色脉冲
            if (G.pScore > _prevPScore)
                StartCoroutine(UITween.PulseColor(_playerInfoText,
                    new Color(0.25f, 0.91f, 0.54f), 0.5f));
            _prevPScore = G.pScore;
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

        /// <summary>P29 — 每帧末尾重建 Buff/眩晕追踪基准值（供下一帧比较用）。</summary>
        private void RebuildUnitTracking()
        {
            _prevUnitAtk.Clear();
            _prevStunnedUids.Clear();
            var G = _gm.G;
            foreach (var u in G.pBase)
            {
                _prevUnitAtk[u.uid] = u.currentAtk;
                if (u.stunned) _prevStunnedUids.Add(u.uid);
            }
            foreach (var b in G.bf)
                foreach (var u in b.pU)
                {
                    _prevUnitAtk[u.uid] = u.currentAtk;
                    if (u.stunned) _prevStunnedUids.Add(u.uid);
                }
        }

        /// <summary>P29 — 传奇技能激活：金色脉冲 0.5s + 按钮 Scale 1→1.2→1（0.35s）。</summary>
        private IEnumerator LegendActivateFlash()
        {
            var rt = _legendBtn.GetComponent<RectTransform>();
            StartCoroutine(UITween.PulseColor(_legendBtnText, C_Gold, 0.5f));
            yield return UITween.ScaleTo(rt, new Vector3(1.18f, 1.18f, 1f), 0.15f, UITween.Ease.OutQuad);
            yield return UITween.ScaleTo(rt, Vector3.one,               0.20f, UITween.Ease.OutQuad);
        }

        /// <summary>P32 — 战斗闪光：全屏覆盖层 alpha 0→0.5→0，持续 0.6s。</summary>
        private IEnumerator CombatFlash()
        {
            if (_combatOverlay == null) yield break;
            _combatFlashRunning = true;
            _combatOverlay.SetActive(true);
            var cg = _combatOverlay.GetComponent<CanvasGroup>();
            if (cg == null) cg = _combatOverlay.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            // 淡入 0.18s
            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(t / 0.18f) * 0.5f;
                yield return null;
            }
            cg.alpha = 0.5f;

            // 停留 0.12s
            yield return new WaitForSecondsRealtime(0.12f);

            // 淡出 0.30s
            t = 0f;
            while (t < 0.30f)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = (1f - Mathf.Clamp01(t / 0.30f)) * 0.5f;
                yield return null;
            }
            cg.alpha = 0f;
            _combatOverlay.SetActive(false);
            _combatFlashRunning = false;
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

            SpawnDropVortices(); // P35: 放置区漩涡
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

            // 清理幽灵 + 放置区漩涡
            if (_dragGhost != null) { Destroy(_dragGhost); _dragGhost = null; }
            DestroyDropVortices(); // P35
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

            // P32: 战斗触发 → 全屏闪光覆盖
            if (!_combatFlashRunning && msg.Contains("战斗") && _combatOverlay != null)
                StartCoroutine(CombatFlash());

            // P35: 法术施放 → 投射物动画
            if (msg.Contains("法术") || msg.Contains("施放"))
                StartCoroutine(SpellProjectile());
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
            StartCoroutine(UITween.PopIn(_gameOverPanel.GetComponent<RectTransform>(), 0.4f));
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
            BuildAmbientLights(bgPanel.transform);           // P34: 径向环境光（3 层渐变圆）
            bgPanel.gameObject.AddComponent<VortexRings>();  // P34: 漩涡旋转（3 弧 + 6 符文）
            BuildBackgroundTextures(bgPanel.transform);      // P36: 六边形网格 + 拉丝条纹 + 噪点
            bgPanel.gameObject.AddComponent<CanvasParticles>(); // P38: 40粒子系统（含粒子物理）

            // ── SafeArea 容器（刘海/圆角/底部条安全区域适配）──
            var safeAreaGo = new GameObject("SafeArea");
            safeAreaGo.transform.SetParent(root, false);
            var safeAreaRt = safeAreaGo.AddComponent<RectTransform>();
            safeAreaRt.anchorMin = Vector2.zero;
            safeAreaRt.anchorMax = Vector2.one;
            safeAreaRt.offsetMin = safeAreaRt.offsetMax = Vector2.zero;
            safeAreaGo.AddComponent<SafeAreaFitter>();
            _gameRootCg  = safeAreaGo.AddComponent<CanvasGroup>();
            var gameRoot = safeAreaGo.transform;

            // ── P31: 敌方信息栏（91-100%，4.5%侧边留给积分轨道）──
            var enemyInfoPanel = MakePanel(gameRoot, "EnemyInfoPanel",
                new Vector2(0.045f, 0.91f), new Vector2(0.955f, 1f), C_EnemyBg);
            _enemyInfoText = MakeText(enemyInfoPanel.transform, "EnemyInfoText", 12);
            _enemyInfoText.color = C_Gold;
            AddShadow(_enemyInfoText); // P35: 文字光晕

            // ── P31: 敌方区域（80-91%）— 左侧 13% 为传奇牌槽，其余为单位区 ──
            var enemyZonePanel = MakePanel(gameRoot, "EnemyZonePanel",
                new Vector2(0.045f, 0.80f), new Vector2(0.955f, 0.91f), C_EnemyBg);
            _enemyZonePanelImg = enemyZonePanel; // P33: 法术目标高亮
            BuildLegendSlot(enemyZonePanel.transform,
                new Vector2(0f, 0f), new Vector2(0.13f, 1f), false,
                out _eLegSlotEmoji, out _eLegSlotStats, out _eLegSlotHpFill);
            _enemyZoneTrans = MakeScrollContentAnchored(enemyZonePanel.transform, "EnemyZoneContent",
                horizontal: true, new Vector2(0.13f, 0f), new Vector2(1f, 1f));

            // ── P31: 战场（31-80%，扩大后约 353px）──
            var bfPanel = MakePanel(gameRoot, "BattlefieldPanel",
                new Vector2(0.045f, 0.31f), new Vector2(0.955f, 0.80f), C_BFBg);
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

            // ── P31: 玩家基地（22-31%）— 左侧 87% 为单位区，右侧 13% 为传奇牌槽 ──
            var playerBasePanel = MakePanel(gameRoot, "PlayerBasePanel",
                new Vector2(0.045f, 0.22f), new Vector2(0.955f, 0.31f), C_PlayBg);
            _playerBasePanelImg = playerBasePanel; // P33: 法术目标高亮
            _playerBaseTrans = MakeScrollContentAnchored(playerBasePanel.transform, "PlayerBaseContent",
                horizontal: true, new Vector2(0f, 0f), new Vector2(0.87f, 1f));
            BuildLegendSlot(playerBasePanel.transform,
                new Vector2(0.87f, 0f), new Vector2(1f, 1f), true,
                out _pLegSlotEmoji, out _pLegSlotStats, out _pLegSlotHpFill);

            // ── P31: 玩家符文（13-22%）──
            var playerRunePanel = MakePanel(gameRoot, "PlayerRunePanel",
                new Vector2(0.045f, 0.13f), new Vector2(0.955f, 0.22f), C_RuneBg);
            _playerRuneTrans = MakeScrollContent(playerRunePanel.transform, "PlayerRuneContent",
                horizontal: true);

            // ── P32: 玩家手牌（2-16%，扩高容纳 110px 卡片）— 纯容器，弧线手动定位 ──
            var playerHandPanel = MakePanel(gameRoot, "PlayerHandPanel",
                new Vector2(0.045f, 0.02f), new Vector2(0.955f, 0.16f), C_HandBg);
            {
                var handGo = new GameObject("PlayerHandContent");
                handGo.transform.SetParent(playerHandPanel.transform, false);
                var handRt = handGo.AddComponent<RectTransform>();
                handRt.anchorMin = Vector2.zero;
                handRt.anchorMax = Vector2.one;
                handRt.offsetMin = handRt.offsetMax = Vector2.zero;
                _playerHandTrans = handGo.transform;
            }

            // ── P31: 玩家操作栏（0-5%）──
            var actionPanel = MakePanel(gameRoot, "ActionPanel",
                new Vector2(0.045f, 0f), new Vector2(0.955f, 0.05f), C_DarkBg);
            var actionLayout = actionPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.childControlWidth  = false;
            actionLayout.childControlHeight = true;
            actionLayout.spacing = 6;
            actionLayout.padding = new RectOffset(4, 4, 2, 2);

            _playerInfoText = MakeText(actionPanel.transform, "PlayerInfoText", 11);
            _playerInfoText.color = C_Gold;
            AddShadow(_playerInfoText); // P35: 文字光晕
            var pInfoRt     = _playerInfoText.GetComponent<RectTransform>();
            pInfoRt.sizeDelta = new Vector2(480, 0);

            (_endTurnBtn, _endTurnBtnText) = MakeButton(actionPanel.transform, "结束回合", 13,
                () => { _sel.Clear(); _gm.PlayerEndTurn(); });
            _endTurnBtnText.color = C_Gold;

            (_legendBtn, _legendBtnText) = MakeButton(actionPanel.transform, "传奇技能", 11,
                () =>
                {
                    _gm.ActivateLegendAbility("ability1");
                    StartCoroutine(LegendActivateFlash()); // P29: 激活光环
                });

            MakeButton(actionPanel.transform, "废牌堆", 10,
                () => ShowDiscardPile());

            // P31: 日志浮动按钮（覆盖层开关）
            MakeButton(actionPanel.transform, "日志", 10,
                () =>
                {
                    bool nowVisible = !(_logOverlayPanel?.activeSelf ?? false);
                    _logOverlayPanel?.SetActive(nowVisible);
                });

            // ── P31: 日志浮动覆盖层（默认隐藏，点"日志"按钮弹出）──
            var logPanel = MakePanel(gameRoot, "LogPanel",
                new Vector2(0.55f, 0f), new Vector2(1f, 1f), C_LogBg);
            _logOverlayPanel = logPanel.gameObject;
            _logOverlayPanel.SetActive(false);

            // P29: 日志标题行（标签 + 折叠按钮）
            var logHeaderGo = new GameObject("LogHeader");
            logHeaderGo.transform.SetParent(logPanel.transform, false);
            var logHeaderRt = logHeaderGo.AddComponent<RectTransform>();
            logHeaderRt.anchorMin = new Vector2(0, 0.95f);
            logHeaderRt.anchorMax = Vector2.one;
            logHeaderRt.offsetMin = logHeaderRt.offsetMax = Vector2.zero;
            var logHeaderLayout = logHeaderGo.AddComponent<HorizontalLayoutGroup>();
            logHeaderLayout.childControlWidth  = false;
            logHeaderLayout.childControlHeight = true;
            logHeaderLayout.childAlignment     = TextAnchor.MiddleLeft;
            logHeaderLayout.padding            = new RectOffset(4, 4, 2, 2);
            logHeaderLayout.spacing            = 6;

            var logTitleRt = AddLabelRt(logHeaderGo.transform, "=== 战斗日志 ===", C_Cyan);
            logTitleRt.sizeDelta = new Vector2(130, 24);

            // 折叠/展开按钮（_logToggleText 先存为字段，供 lambda 捕获）
            var (logToggleBtn, logToggleLblTmp) = MakeButton(logHeaderGo.transform, "▼", 11,
                () =>
                {
                    _logVisible = !_logVisible;
                    if (_logScrollGo  != null) _logScrollGo.SetActive(_logVisible);
                    if (_logToggleText != null) _logToggleText.text = _logVisible ? "▼" : "▶";
                });
            _logToggleText = logToggleLblTmp;
            logToggleBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(28, 22);
            _logToggleText.color = C_Cyan;

            var logScrollGo = new GameObject("LogScroll");
            _logScrollGo = logScrollGo; // P29: 折叠引用
            logScrollGo.transform.SetParent(logPanel.transform, false);
            var logScrollRt = logScrollGo.AddComponent<RectTransform>();
            logScrollRt.anchorMin = new Vector2(0, 0);
            logScrollRt.anchorMax = new Vector2(1, 0.95f); // 留出顶部 5% 给 LogHeader
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
            if (_courierFont != null) _logText.font = _courierFont; // P30: 等宽字体
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

            // ── P31: 玩家积分轨道（左侧，y: 0.05-0.85）──
            {
                var trackPanel = MakePanel(gameRoot, "PlayerScoreTrack",
                    new Vector2(0f, 0.05f), new Vector2(0.045f, 0.85f),
                    new Color(0.02f, 0.02f, 0.06f, 0.9f));
                BuildScoreTrack(trackPanel.transform, _pScoreCircles, isPlayer: true);
            }

            // ── P31: 敌方积分轨道（右侧，y: 0.15-0.95）──
            {
                var trackPanel = MakePanel(gameRoot, "EnemyScoreTrack",
                    new Vector2(0.955f, 0.15f), new Vector2(1f, 0.95f),
                    new Color(0.06f, 0.02f, 0.02f, 0.9f));
                BuildScoreTrack(trackPanel.transform, _eScoreCircles, isPlayer: false);
            }

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
            var mulRt = mulCardsGo.GetComponent<RectTransform>();
            mulRt.sizeDelta = new Vector2(0, 120);
            _mulliganPanel.SetActive(false);

            // ── 翻币结果面板（P28）──
            _coinPanel = MakePanel(gameRoot, "CoinPanel",
                new Vector2(0.3f, 0.35f), new Vector2(0.7f, 0.65f),
                new Color(0.02f, 0.02f, 0.08f, 0.95f)).gameObject;
            var coinLayout = _coinPanel.AddComponent<VerticalLayoutGroup>();
            coinLayout.childControlWidth  = true;
            coinLayout.childControlHeight = false;
            coinLayout.childAlignment     = TextAnchor.MiddleCenter;
            coinLayout.spacing            = 14;
            coinLayout.padding            = new RectOffset(20, 20, 24, 20);

            var coinTitle = MakeText(_coinPanel.transform, "CoinTitle", 16);
            coinTitle.text      = "⚙ 翻币决定先手";
            coinTitle.alignment = TextAnchor.MiddleCenter;
            coinTitle.color     = C_Gold;
            coinTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 28);

            _coinResultText = MakeText(_coinPanel.transform, "CoinResult", 28);
            _coinResultText.alignment = TextAnchor.MiddleCenter;
            _coinResultText.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 80);
            _coinPanel.SetActive(false);

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
                    _prevPScore    = 0;
                    _prevEScore    = 0;
                    _prevPBaseUids.Clear();
                    _prevPBFUids.Clear();
                    _prevBF0CardId    = "";
                    _prevBF1CardId    = "";
                    _mulliganPopInDone = false;
                    _coinPanelShowing  = false;
                    _lastPhase = GamePhase.Init;
                    _lastTurn  = Owner.Player;
                    _titlePanel.SetActive(true);
                    _titlePulseRoutine = StartCoroutine(TitlePulse()); // P30: 重启标题脉冲
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
                () => StartCoroutine(ClosePanel(_discardPanel, 0.25f)));
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
                () => StartCoroutine(ClosePanel(_cardDetailPanel, 0.25f)));
            _cardDetailPanel.SetActive(false);

            // ── P32: 战斗闪光覆盖层（全屏半透明，默认隐藏）──
            _combatOverlay = MakePanel(root, "CombatOverlay",
                Vector2.zero, Vector2.one,
                new Color(0f, 0f, 0f, 0f)).gameObject;
            _combatOverlay.AddComponent<CanvasGroup>().blocksRaycasts = false;
            _combatOverlayText = MakeText(_combatOverlay.transform, "CombatText", 36);
            _combatOverlayText.alignment = TextAnchor.MiddleCenter;
            _combatOverlayText.color     = C_Gold;
            _combatOverlayText.text      = "⚔ 战斗！";
            _combatOverlayText.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            _combatOverlayText.GetComponent<RectTransform>().anchorMax = Vector2.one;
            if (_cinzelBold != null) _combatOverlayText.font = _cinzelBold;
            _combatOverlay.SetActive(false);

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

            // P37: 品牌标志行（VerticalLayout 首项：大光晕 + 圆环 + 剑符文）
            var logoGo = new GameObject("BrandLogo");
            logoGo.transform.SetParent(_titlePanel.transform, false);
            logoGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 88);
            // 大光晕圆（600×600，忽略布局，视觉溢出）
            var glowGo = new GameObject("TitleGlow");
            glowGo.transform.SetParent(logoGo.transform, false);
            glowGo.AddComponent<LayoutElement>().ignoreLayout = true;
            var glowRt = glowGo.AddComponent<RectTransform>();
            glowRt.anchorMin = glowRt.anchorMax = Vector2.one * 0.5f;
            glowRt.pivot     = Vector2.one * 0.5f;
            glowRt.sizeDelta = new Vector2(600f, 600f);
            var glowImg = glowGo.AddComponent<Image>();
            glowImg.sprite        = MakeRadialGradientSprite(256);
            glowImg.color         = new Color(0.78f, 0.67f, 0.43f, 0.04f);
            glowImg.raycastTarget = false;
            StartCoroutine(TitleGlowPulse(glowImg));
            // 金色圆环（80×80）
            var ringGo = new GameObject("LogoRing");
            ringGo.transform.SetParent(logoGo.transform, false);
            var ringRt = ringGo.AddComponent<RectTransform>();
            ringRt.anchorMin = ringRt.anchorMax = Vector2.one * 0.5f;
            ringRt.pivot     = Vector2.one * 0.5f;
            ringRt.sizeDelta = new Vector2(80f, 80f);
            var ringImg = ringGo.AddComponent<Image>();
            ringImg.sprite        = MakeRingSprite(64, 4f);
            ringImg.color         = C_Gold;
            ringImg.raycastTarget = false;
            // 剑符文字
            var swordGo = new GameObject("LogoSword");
            swordGo.transform.SetParent(logoGo.transform, false);
            var swordRt = swordGo.AddComponent<RectTransform>();
            swordRt.anchorMin = swordRt.anchorMax = Vector2.one * 0.5f;
            swordRt.pivot     = Vector2.one * 0.5f;
            swordRt.sizeDelta = new Vector2(60f, 60f);
            var swordTxt = swordGo.AddComponent<Text>();
            swordTxt.text          = "⚔";
            swordTxt.fontSize      = 36;
            swordTxt.alignment     = TextAnchor.MiddleCenter;
            swordTxt.color         = C_Gold;
            swordTxt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            swordTxt.raycastTarget = false;

            var titleText = MakeText(_titlePanel.transform, "TitleText", 48);
            titleText.text      = "风舞天际";
            titleText.color     = C_Gold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 70);
            if (_cinzelBold != null) titleText.font = _cinzelBold; // P30: Cinzel Bold
            _titleText = titleText; // P30: 标题光效引用
            AddShadow(titleText); // P35: 文字光晕

            var subtitleText = MakeText(_titlePanel.transform, "SubtitleText", 18);
            subtitleText.text      = "Trading Card Game";
            subtitleText.color     = C_Cyan;
            subtitleText.alignment = TextAnchor.MiddleCenter;
            subtitleText.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 30);
            if (_cinzelFont != null) subtitleText.font = _cinzelFont; // P30: Cinzel Regular

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
                    if (_titlePulseRoutine != null) StopCoroutine(_titlePulseRoutine); // P30
                    if (_titleText != null) _titleText.color = C_Gold; // 恢复原色
                    _gameRootCg.alpha = 0f;
                    _gm.StartGame(DeckFactory.MakeKaisaVsMasterYi());
                    StartCoroutine(UITween.FadeIn(_gameRootCg, 0.7f));
                    StartCoroutine(ShowCoinFlipResult());
                });
            startBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 48);
            startBtnLbl.color = C_Gold;
            // P37: 按钮旋转青色光弧
            var btnGlowGo = new GameObject("BtnGlowArc");
            btnGlowGo.transform.SetParent(startBtn.transform, false);
            var btnGlowRt = btnGlowGo.AddComponent<RectTransform>();
            btnGlowRt.anchorMin = btnGlowRt.anchorMax = Vector2.one * 0.5f;
            btnGlowRt.pivot     = Vector2.one * 0.5f;
            btnGlowRt.sizeDelta = new Vector2(244f, 92f);
            var btnGlowImg = btnGlowGo.AddComponent<Image>();
            btnGlowImg.type          = Image.Type.Filled;
            btnGlowImg.fillMethod    = Image.FillMethod.Radial360;
            btnGlowImg.fillAmount    = 0.25f;
            btnGlowImg.color         = new Color(0.04f, 0.78f, 0.73f, 0.70f);
            btnGlowImg.raycastTarget = false;
            btnGlowGo.AddComponent<CanPlayGlow>(); // 120°/s = 3s/圈
            _ = startBtn;

            // P30: 启动标题脉冲（BuildCanvas 末尾，标题面板已创建）
            _titlePulseRoutine = StartCoroutine(TitlePulse());
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

        /// <summary>P31 — 指定 anchorMin/Max 的 ScrollContent 容器（与 MakeScrollContent 逻辑相同，但支持自定义锚点）。</summary>
        private static Transform MakeScrollContentAnchored(Transform parent, string name, bool horizontal,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
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

        /// <summary>
        /// P31 — 积分轨道：8 个圆圈纵向排列，底部=1分 / 顶部=8分。
        /// circles[] 由调用方传入，供 RefreshScoreTracks 每帧更新颜色。
        /// </summary>
        private static void BuildScoreTrack(Transform parent, Image[] circles, bool isPlayer)
        {
            var go = new GameObject("ScoreCircles");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment        = TextAnchor.MiddleCenter;
            layout.childControlWidth     = true;
            layout.childControlHeight    = false;
            layout.spacing               = 2;
            layout.padding               = new RectOffset(3, 3, 6, 6);
            layout.reverseArrangement    = true;   // 1分在底部

            // 标题文字（"我"/"敌"）
            var titleGo = new GameObject("TrackLabel");
            titleGo.transform.SetParent(go.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(0, 14);
            var titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleTxt.fontSize  = 8;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color     = isPlayer ? new Color(0.25f, 0.91f, 0.54f) : new Color(1f, 0.4f, 0.4f);
            titleTxt.text      = isPlayer ? "我" : "敌";

            // 8 个得分圆圈
            for (int i = 0; i < 8; i++)
            {
                var circleGo = new GameObject($"Circle_{i + 1}");
                circleGo.transform.SetParent(go.transform, false);
                var cRt = circleGo.AddComponent<RectTransform>();
                cRt.sizeDelta = new Vector2(0, 20);

                var bg = circleGo.AddComponent<Image>();
                bg.color = new Color(0.22f, 0.22f, 0.28f, 0.7f);
                circles[i] = bg;

                // 分值文字
                var numGo = new GameObject("Num");
                numGo.transform.SetParent(circleGo.transform, false);
                var numRt = numGo.AddComponent<RectTransform>();
                numRt.anchorMin = Vector2.zero;
                numRt.anchorMax = Vector2.one;
                numRt.offsetMin = numRt.offsetMax = Vector2.zero;
                var numTxt = numGo.AddComponent<Text>();
                numTxt.font           = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                numTxt.fontSize       = 9;
                numTxt.alignment      = TextAnchor.MiddleCenter;
                numTxt.color          = Color.white;
                numTxt.text           = (i + 1).ToString();
                numTxt.raycastTarget  = false;
            }
        }

        /// <summary>
        /// P31 — 传奇牌槽（62×全高，静态面板，Refresh 时只更新文字/颜色）。
        /// isPlayer=true 置于玩家基地右侧，false 置于敌方区域左侧。
        /// </summary>
        private void BuildLegendSlot(Transform parent, Vector2 anchorMin, Vector2 anchorMax, bool isPlayer,
            out Text emojiText, out Text statsText, out Image hpFill)
        {
            var slot = new GameObject(isPlayer ? "PlayerLegendSlot" : "EnemyLegendSlot");
            slot.transform.SetParent(parent, false);
            var slotRt = slot.AddComponent<RectTransform>();
            slotRt.anchorMin = anchorMin;
            slotRt.anchorMax = anchorMax;
            slotRt.offsetMin = new Vector2(2, 2);
            slotRt.offsetMax = new Vector2(-2, -2);

            var slotBg = slot.AddComponent<Image>();
            slotBg.color = isPlayer
                ? new Color(0.04f, 0.10f, 0.20f, 0.92f)
                : new Color(0.18f, 0.04f, 0.04f, 0.92f);

            // 顶部边框线
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(slot.transform, false);
            var borderRt = borderGo.AddComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = Vector2.zero;
            borderRt.offsetMax = Vector2.zero;
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.color = new Color(C_Gold.r, C_Gold.g, C_Gold.b, 0.45f);
            borderImg.raycastTarget = false;
            // 用内边距模拟细描边
            var innerGo = new GameObject("Inner");
            innerGo.transform.SetParent(slot.transform, false);
            var innerRt = innerGo.AddComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(1, 1);
            innerRt.offsetMax = new Vector2(-1, -1);
            var innerImg = innerGo.AddComponent<Image>();
            innerImg.color = slotBg.color;
            innerImg.raycastTarget = false;

            // Emoji / 卡图区域（上方 55%）
            var artGo = new GameObject("Art");
            artGo.transform.SetParent(slot.transform, false);
            var artRt = artGo.AddComponent<RectTransform>();
            artRt.anchorMin = new Vector2(0f, 0.38f);
            artRt.anchorMax = Vector2.one;
            artRt.offsetMin = new Vector2(2, 0);
            artRt.offsetMax = new Vector2(-2, -2);

            var artBg = artGo.AddComponent<Image>();
            artBg.color = new Color(0.08f, 0.08f, 0.12f, 0.8f);
            artBg.raycastTarget = false;

            var eGo = new GameObject("EmojiText");
            eGo.transform.SetParent(artGo.transform, false);
            var eRt = eGo.AddComponent<RectTransform>();
            eRt.anchorMin = Vector2.zero;
            eRt.anchorMax = Vector2.one;
            eRt.offsetMin = eRt.offsetMax = Vector2.zero;
            emojiText = eGo.AddComponent<Text>();
            emojiText.font           = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            emojiText.fontSize       = 20;
            emojiText.alignment      = TextAnchor.MiddleCenter;
            emojiText.color          = Color.white;
            emojiText.text           = "?";
            emojiText.raycastTarget  = false;

            // 数据区（下方 38%）
            var statsGo = new GameObject("Stats");
            statsGo.transform.SetParent(slot.transform, false);
            var statsRt = statsGo.AddComponent<RectTransform>();
            statsRt.anchorMin = new Vector2(0f, 0f);
            statsRt.anchorMax = new Vector2(1f, 0.38f);
            statsRt.offsetMin = new Vector2(2, 14);
            statsRt.offsetMax = new Vector2(-2, 0);

            statsText = statsGo.AddComponent<Text>();
            statsText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statsText.fontSize      = 8;
            statsText.alignment     = TextAnchor.UpperCenter;
            statsText.color         = Color.white;
            statsText.text          = "-";
            statsText.raycastTarget = false;

            // HP 条背景（下边缘 13px）
            var hpBgGo = new GameObject("HpBarBg");
            hpBgGo.transform.SetParent(slot.transform, false);
            var hpBgRt = hpBgGo.AddComponent<RectTransform>();
            hpBgRt.anchorMin = new Vector2(0f, 0f);
            hpBgRt.anchorMax = new Vector2(1f, 0f);
            hpBgRt.offsetMin = new Vector2(3, 3);
            hpBgRt.offsetMax = new Vector2(-3, 12);
            hpBgGo.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 1f);

            // HP 条填充（Image.Filled）
            var hpFillGo = new GameObject("HpFill");
            hpFillGo.transform.SetParent(hpBgGo.transform, false);
            var hpFillRt = hpFillGo.AddComponent<RectTransform>();
            hpFillRt.anchorMin = Vector2.zero;
            hpFillRt.anchorMax = Vector2.one;
            hpFillRt.offsetMin = hpFillRt.offsetMax = Vector2.zero;
            hpFill = hpFillGo.AddComponent<Image>();
            hpFill.type       = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillAmount = 1f;
            hpFill.color      = isPlayer
                ? new Color(0.25f, 0.85f, 0.42f)
                : new Color(0.88f, 0.30f, 0.30f);
            hpFill.raycastTarget = false;
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
            AddLabelRt(parent, text, col);
        }

        /// <summary>创建文字标签并返回其 RectTransform（供需要动画的调用方使用）。</summary>
        private static RectTransform AddLabelRt(Transform parent, string text, Color col)
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
            return rt;
        }

        private void AddUnitButton(Transform parent, CardInstance u, Owner owner, bool selected,
            bool isNew = false)
        {
            int capturedUid = u.uid;
            Color col = selected ? Color.cyan : Color.white;

            var (btn, lbl) = MakeButton(parent, UnitLabel(u), 11,
                () => OnPlayerUnitClicked(capturedUid));
            btn.gameObject.name = $"UnitBtn_{u.uid}"; // P28: 死亡检测用
            _unitNames[u.uid]   = u.cardName;          // P28: 死亡飞出动画用
            _ = btn;
            lbl.color = col;

            // P29: Buff/Debuff 光晕（ATK 增加→绿，减少→红）
            if (_prevUnitAtk.TryGetValue(u.uid, out int prevAtk) && !isNew)
            {
                if (u.currentAtk > prevAtk)
                    StartCoroutine(UITween.PulseColor(lbl, new Color(0.25f, 0.91f, 0.54f), 0.6f));
                else if (u.currentAtk < prevAtk)
                    StartCoroutine(UITween.PulseColor(lbl, new Color(1f, 0.30f, 0.30f), 0.6f));
            }

            // P29: 眩晕光晕（新晕→橙黄）
            if (u.stunned && !_prevStunnedUids.Contains(u.uid))
                StartCoroutine(UITween.PulseColor(lbl, new Color(1f, 0.80f, 0.20f), 0.6f));

            // 新入场单位：0.3s 落地震动
            if (isNew)
                StartCoroutine(UITween.Shake(btn.GetComponent<RectTransform>(), 4f, 0.3f));
        }

        private void AddZoneButton(Transform parent, string label, string zone, Color col)
        {
            var (btn, lbl) = MakeButton(parent, label, 11, () => OnZoneClicked(zone));
            lbl.color = col;
            btn.gameObject.AddComponent<ZoneDropTarget>().ZoneId = zone;
        }

        /// <summary>
        /// P31 — 单位小卡片（战场用竖版 70×95px，基地用横版 110×52px）。
        /// 取代原 AddUnitButton 的文字按钮；保留 "UnitBtn_{uid}" 命名供死亡检测。
        /// </summary>
        private void AddUnitCard(Transform parent, CardInstance u, Owner owner,
            bool selected, bool isNew = false, bool compact = false)
        {
            int capturedUid = u.uid;
            bool isEnemy = owner == Owner.Enemy;

            // ── 容器 ──
            var cardGo = new GameObject($"UnitBtn_{u.uid}");
            cardGo.transform.SetParent(parent, false);
            var cardRt = cardGo.AddComponent<RectTransform>();
            cardRt.sizeDelta = compact ? new Vector2(110, 52) : new Vector2(70, 95);

            var bg = cardGo.AddComponent<Image>();
            bg.color = selected ? new Color(0.04f, 0.35f, 0.42f, 0.95f)
                     : isEnemy  ? new Color(0.28f, 0.05f, 0.05f, 0.9f)
                                : new Color(0.05f, 0.11f, 0.22f, 0.9f);

            // 描边（1px 金色/红色外框）
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(cardGo.transform, false);
            var borderRt = borderGo.AddComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = Vector2.zero;
            borderRt.offsetMax = Vector2.zero;
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.color = selected ? C_Cyan
                            : isEnemy  ? new Color(0.8f, 0.25f, 0.25f, 0.6f)
                                       : new Color(C_Gold.r, C_Gold.g, C_Gold.b, 0.4f);
            borderImg.raycastTarget = false;
            var innerOverlay = new GameObject("BgFill");
            innerOverlay.transform.SetParent(cardGo.transform, false);
            var ioRt = innerOverlay.AddComponent<RectTransform>();
            ioRt.anchorMin = Vector2.zero;
            ioRt.anchorMax = Vector2.one;
            ioRt.offsetMin = new Vector2(1, 1);
            ioRt.offsetMax = new Vector2(-1, -1);
            var ioImg = innerOverlay.AddComponent<Image>();
            ioImg.color = bg.color;
            ioImg.raycastTarget = false;

            // 按钮（仅玩家单位）
            if (!isEnemy)
            {
                var btn = cardGo.AddComponent<Button>();
                btn.targetGraphic = bg;
                var cols = btn.colors;
                cols.highlightedColor = new Color(0.10f, 0.26f, 0.38f, 1f);
                cols.pressedColor     = new Color(0.02f, 0.08f, 0.16f, 1f);
                btn.colors = cols;
                btn.onClick.AddListener(() => OnPlayerUnitClicked(capturedUid));
            }

            _unitNames[u.uid] = u.cardName; // P28: 死亡飞出

            // ── Emoji / 图标区域 ──
            float emojiAnchorY = compact ? 0.30f : 0.36f;
            float emojiLeft    = compact ? 0f    : 0f;
            float emojiRight   = compact ? 0.35f : 1f;

            var artGo = new GameObject("Art");
            artGo.transform.SetParent(cardGo.transform, false);
            var artRt = artGo.AddComponent<RectTransform>();
            artRt.anchorMin = new Vector2(emojiLeft,  emojiAnchorY);
            artRt.anchorMax = new Vector2(emojiRight, 1f);
            artRt.offsetMin = artRt.offsetMax = Vector2.zero;
            artGo.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.14f, 0.5f);

            var eGo = new GameObject("Emoji");
            eGo.transform.SetParent(artGo.transform, false);
            var eRt = eGo.AddComponent<RectTransform>();
            eRt.anchorMin = Vector2.zero;
            eRt.anchorMax = Vector2.one;
            eRt.offsetMin = eRt.offsetMax = Vector2.zero;
            var emojiTxt = eGo.AddComponent<Text>();
            emojiTxt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            emojiTxt.fontSize      = compact ? 16 : 20;
            emojiTxt.alignment     = TextAnchor.MiddleCenter;
            emojiTxt.color         = Color.white;
            emojiTxt.text          = u.emoji ?? "?";
            emojiTxt.raycastTarget = false;

            // ── 名称 ──
            float nameLeft   = compact ? 0.37f : 0f;
            float nameTop    = compact ? 0.55f : emojiAnchorY;
            float nameBottom = compact ? 1f    : emojiAnchorY + 0.18f;

            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(cardGo.transform, false);
            var nameRt = nameGo.AddComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(nameLeft, compact ? 0.55f : (emojiAnchorY - 0.18f));
            nameRt.anchorMax = new Vector2(1f,       compact ? 1f    : emojiAnchorY);
            nameRt.offsetMin = new Vector2(2, 0);
            nameRt.offsetMax = new Vector2(-2, 0);
            var nameTxt = nameGo.AddComponent<Text>();
            nameTxt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            nameTxt.fontSize      = 8;
            nameTxt.alignment     = compact ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter;
            nameTxt.color         = new Color(0.85f, 0.85f, 0.85f);
            string displayName    = u.cardName.Length > 5 ? u.cardName.Substring(0, 5) : u.cardName;
            nameTxt.text          = displayName;
            nameTxt.raycastTarget = false;

            // ── ATK/HP 数值 ──
            var statsGo = new GameObject("Stats");
            statsGo.transform.SetParent(cardGo.transform, false);
            var statsRt = statsGo.AddComponent<RectTransform>();
            statsRt.anchorMin = new Vector2(compact ? 0.37f : 0f, 0f);
            statsRt.anchorMax = new Vector2(1f, compact ? 0.55f : (emojiAnchorY - 0.18f));
            statsRt.offsetMin = new Vector2(2, 1);
            statsRt.offsetMax = new Vector2(-2, -1);
            statsGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var stGo = new GameObject("StatsTxt");
            stGo.transform.SetParent(statsGo.transform, false);
            var stRt = stGo.AddComponent<RectTransform>();
            stRt.anchorMin = Vector2.zero;
            stRt.anchorMax = Vector2.one;
            stRt.offsetMin = stRt.offsetMax = Vector2.zero;
            var statsTxt = stGo.AddComponent<Text>();
            statsTxt.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statsTxt.fontSize   = 10;
            statsTxt.fontStyle  = FontStyle.Bold;
            statsTxt.alignment  = TextAnchor.MiddleCenter;
            statsTxt.color      = selected ? C_Cyan
                                : isEnemy  ? new Color(1f, 0.65f, 0.65f)
                                           : Color.white;
            string exStr   = u.exhausted ? "疲" : "";
            string stunStr = u.stunned   ? "晕" : "";
            string suffix  = string.IsNullOrEmpty(exStr + stunStr) ? "" : $"[{exStr}{stunStr}]";
            statsTxt.text = $"{u.currentAtk}/{u.currentHp}{suffix}";
            statsTxt.raycastTarget = false;

            // P29: Buff/Debuff 光晕
            if (_prevUnitAtk.TryGetValue(u.uid, out int prevAtk) && !isNew)
            {
                if (u.currentAtk > prevAtk)
                    StartCoroutine(UITween.PulseColor(statsTxt, new Color(0.25f, 0.91f, 0.54f), 0.6f));
                else if (u.currentAtk < prevAtk)
                    StartCoroutine(UITween.PulseColor(statsTxt, new Color(1f, 0.30f, 0.30f), 0.6f));
            }
            if (u.stunned && !_prevStunnedUids.Contains(u.uid))
                StartCoroutine(UITween.PulseColor(statsTxt, new Color(1f, 0.80f, 0.20f), 0.6f));

            // P28: 落地震动
            if (isNew)
                StartCoroutine(UITween.Shake(cardRt, 4f, 0.3f));

            // P33: 落地涟漪波
            if (isNew)
                StartCoroutine(UnitLandRipple(cardRt));

            // 敌方单位：右上角"?"详情按钮
            if (isEnemy)
            {
                var capturedU = u;
                var detGo = new GameObject("DetailBtn");
                detGo.transform.SetParent(cardGo.transform, false);
                var detRt = detGo.AddComponent<RectTransform>();
                detRt.anchorMin = new Vector2(0.6f, 0.78f);
                detRt.anchorMax = Vector2.one;
                detRt.offsetMin = detRt.offsetMax = Vector2.zero;
                var detBg = detGo.AddComponent<Image>();
                detBg.color = new Color(0.2f, 0.1f, 0.1f, 0.85f);
                var detBtn = detGo.AddComponent<Button>();
                detBtn.targetGraphic = detBg;
                detBtn.onClick.AddListener(() => ShowCardDetail(capturedU));
                var detTxtGo = new GameObject("L");
                detTxtGo.transform.SetParent(detGo.transform, false);
                var detTxtRt = detTxtGo.AddComponent<RectTransform>();
                detTxtRt.anchorMin = Vector2.zero;
                detTxtRt.anchorMax = Vector2.one;
                detTxtRt.offsetMin = detTxtRt.offsetMax = Vector2.zero;
                var detTxt = detTxtGo.AddComponent<Text>();
                detTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                detTxt.fontSize  = 8;
                detTxt.color     = C_Gold;
                detTxt.alignment = TextAnchor.MiddleCenter;
                detTxt.text      = "?";
            }
        }

        /// <summary>P31 — 刷新积分轨道圆圈颜色（每次 Refresh 调用）。</summary>
        private void RefreshScoreTracks()
        {
            var G = _gm.G;
            var green = new Color(0.25f, 0.91f, 0.54f);
            var red   = new Color(1f,    0.38f, 0.38f);
            var empty = new Color(0.22f, 0.22f, 0.28f, 0.7f);

            for (int i = 0; i < 8; i++)
            {
                if (_pScoreCircles[i] != null)
                    _pScoreCircles[i].color = i < G.pScore
                        ? (G.pScore >= 8 ? C_Gold : green)
                        : empty;
                if (_eScoreCircles[i] != null)
                    _eScoreCircles[i].color = i < G.eScore
                        ? (G.eScore >= 8 ? C_Gold : red)
                        : empty;
            }
        }

        /// <summary>P31 — 刷新传奇牌槽显示（emoji / 名称 / ATK / HP条）。</summary>
        private void RefreshLegendSlots()
        {
            var G = _gm.G;
            UpdateLegendSlot(G.pLeg, _pLegSlotEmoji, _pLegSlotStats, _pLegSlotHpFill, isPlayer: true);
            UpdateLegendSlot(G.eLeg, _eLegSlotEmoji, _eLegSlotStats, _eLegSlotHpFill, isPlayer: false);
        }

        private static void UpdateLegendSlot(LegendInstance leg,
            Text emojiTxt, Text statsTxt, Image hpFill, bool isPlayer)
        {
            if (emojiTxt == null || statsTxt == null || hpFill == null) return;

            if (leg == null)
            {
                emojiTxt.text  = "?";
                statsTxt.text  = "-";
                hpFill.fillAmount = 1f;
                return;
            }

            emojiTxt.text = (leg.data != null ? leg.data.emoji : null) ?? (isPlayer ? "⚔" : "💀");
            statsTxt.text = $"{(leg.data != null ? leg.data.cardName : "?")}\nATK:{leg.currentAtk}";
            float pct     = leg.maxHp > 0 ? Mathf.Clamp01((float)leg.currentHp / leg.maxHp) : 1f;
            hpFill.fillAmount = pct;
            hpFill.color      = pct > 0.5f ? new Color(0.25f, 0.85f, 0.42f)
                              : pct > 0.25f ? new Color(0.95f, 0.75f, 0.15f)
                                            : new Color(0.9f, 0.2f, 0.2f);
        }

        // ─────────────────────────────────────────────
        // P33 新增方法
        // ─────────────────────────────────────────────

        /// <summary>P33 — 法术/装备牌选中时高亮所有可放置区域，取消选中时停止。</summary>
        private void RefreshSpellTargetHighlight()
        {
            var G = _gm.G;
            CardInstance selCard = _sel.IsCardSelected
                ? G.pHand.Find(c => c.uid == _sel.SelectedUid)
                : null;
            bool needGlow = selCard != null
                && (selCard.type == CardType.Spell || selCard.type == CardType.Equipment)
                && _gm.IsPlayerTurn;

            if (needGlow && _spellZoneGlows.Count == 0)
            {
                // 启动各区域青色脉冲循环
                Image[] zones = { _bf0PanelImg, _bf1PanelImg,
                                  _enemyZonePanelImg, _playerBasePanelImg };
                var glowCol = new Color(0.04f, 0.78f, 0.73f, 0.55f);
                foreach (var img in zones)
                {
                    if (img == null) continue;
                    var orig = img.color;
                    var co   = StartCoroutine(SpellZoneGlowLoop(img, glowCol));
                    _spellZoneGlows.Add((img, orig, co));
                }
                // Toast 提示
                if (ToastSystem.Instance != null)
                    ToastSystem.Instance.Show($"⚡ 请选择目标：{selCard.cardName}", 2.0f);
            }
            else if (!needGlow && _spellZoneGlows.Count > 0)
            {
                foreach (var (img, orig, co) in _spellZoneGlows)
                {
                    if (co  != null) StopCoroutine(co);
                    if (img != null) img.color = orig;
                }
                _spellZoneGlows.Clear();
            }
        }

        /// <summary>P33 — 区域高亮脉冲循环协程（origColor ↔ glowColor 0.4s）。</summary>
        private IEnumerator SpellZoneGlowLoop(Image img, Color glowColor)
        {
            Color origColor = img.color;
            while (true)
            {
                yield return UITween.TintTo(img, glowColor,  0.40f, UITween.Ease.InOutQuad);
                yield return UITween.TintTo(img, origColor,  0.40f, UITween.Ease.InOutQuad);
            }
        }

        /// <summary>P33 — 单位落地涟漪波：圆形 ghost 从单位中心向外扩散（0.55s）。</summary>
        private IEnumerator UnitLandRipple(RectTransform sourceRt)
        {
            if (_rootCanvasRt == null) yield break;

            // 在根 Canvas 上创建涟漪 Ghost
            var ripGo  = new GameObject("LandRipple");
            ripGo.transform.SetParent(_rootCanvasRt, false);
            var ripRt  = ripGo.AddComponent<RectTransform>();
            ripRt.sizeDelta   = new Vector2(88f, 88f);
            ripRt.localScale  = Vector3.zero;
            ripRt.pivot       = new Vector2(0.5f, 0.5f);

            // 定位到 sourceRt 世界中心
            var corners = new Vector3[4];
            sourceRt.GetWorldCorners(corners);
            Vector3 worldCenter = (corners[0] + corners[2]) * 0.5f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rootCanvasRt,
                RectTransformUtility.WorldToScreenPoint(null, worldCenter),
                null, out Vector2 localPos);
            ripRt.anchoredPosition = localPos;

            var ripImg = ripGo.AddComponent<Image>();
            ripImg.color = new Color(0.25f, 0.91f, 0.54f, 0.50f);
            ripImg.raycastTarget = false;

            // 扩散 + 淡出
            const float duration = 0.55f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p     = Mathf.Clamp01(t / duration);
                float eased = 1f - (1f - p) * (1f - p); // OutQuad
                ripRt.localScale = Vector3.one * Mathf.Lerp(0f, 1.6f, eased);
                ripImg.color     = new Color(0.25f, 0.91f, 0.54f, Mathf.Lerp(0.50f, 0f, p));
                yield return null;
            }

            Destroy(ripGo);
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
            StartCoroutine(UITween.PopIn(_discardPanel.GetComponent<RectTransform>(), 0.4f));
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

        /// <summary>缩小到 0 后隐藏面板，并重置 localScale 供下次弹入使用。</summary>
        private IEnumerator ClosePanel(GameObject panel, float duration)
        {
            var rt = panel.GetComponent<RectTransform>();
            yield return UITween.ScaleTo(rt, Vector3.zero, duration, UITween.Ease.InQuad);
            panel.SetActive(false);
            rt.localScale = Vector3.one;
        }

        /// <summary>
        /// <summary>P30 — 标题文字持续金色脉冲（dim→bright，2.2s 周期无限循环）。</summary>
        private IEnumerator TitlePulse()
        {
            if (_titleText == null) yield break;
            var bright = new Color(1f, 0.95f, 0.65f); // 亮金
            while (true)
            {
                yield return UITween.PulseColor(_titleText, bright, 2.2f);
                yield return new WaitForSeconds(0.3f);
            }
        }

        /// P28/P37 — 翻币结果面板：
        /// PopIn(0.4s) → P37 CardFlip3D(0→90°→0，0.44s) → 等 1.3s → ClosePanel。
        /// </summary>
        private IEnumerator ShowCoinFlipResult()
        {
            // 初始占位文字
            _coinResultText.text  = "？";
            _coinResultText.color = Color.white;
            _coinPanelShowing = true;
            _coinPanel.SetActive(true);
            yield return UITween.PopIn(_coinPanel.GetComponent<RectTransform>(), 0.4f);

            // P37: 真实 Y 轴翻转（替代 Y-scale hack）
            var coinTxtRt    = _coinResultText.GetComponent<RectTransform>();
            bool playerFirst = _gm.G.first == Owner.Player;
            yield return CardFlip3D(coinTxtRt, () =>
            {
                _coinResultText.text  = playerFirst ? "正面\n玩家先手！" : "反面\n对手先手！";
                _coinResultText.color = playerFirst
                    ? new Color(0.25f, 0.91f, 0.54f)
                    : new Color(1f,    0.45f, 0.45f);
            });

            yield return new WaitForSeconds(1.3f);
            yield return StartCoroutine(ClosePanel(_coinPanel, 0.25f));
            coinTxtRt.localEulerAngles = Vector3.zero; // 保证下次复用时旋转归零
            _coinPanelShowing = false;
        }

        private static IEnumerator DelayedPopIn(RectTransform rt, float duration, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return UITween.PopIn(rt, duration);
        }

        /// <summary>
        /// P27 — 符文回收飞行动画：在 root canvas 生成 ghost label，
        /// 向上飘 60px + 淡出 0.7s，模拟符文飞入符能计数器效果。
        /// </summary>
        private IEnumerator RuneRecycleFly(Vector2 startPos, string label, Color col)
        {
            var ghost  = new GameObject("RuneRecycleGhost");
            ghost.transform.SetParent(_rootCanvasRt, false);
            var ghostRt = ghost.AddComponent<RectTransform>();
            ghostRt.sizeDelta        = new Vector2(120f, 28f);
            ghostRt.anchoredPosition = startPos;
            var txt           = ghost.AddComponent<Text>();
            txt.text          = label;
            txt.color         = col;
            txt.fontSize      = 18;
            txt.alignment     = TextAnchor.MiddleCenter;
            txt.font          = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var cg            = ghost.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable   = false;
            // 向上飘 + 淡出并行（弧线飞向符能计数器）
            StartCoroutine(UITween.MoveY(ghostRt, 60f, 0.7f, UITween.Ease.OutQuad));
            yield return UITween.FadeOut(cg, 0.7f, UITween.Ease.OutQuad);
            Destroy(ghost);
        }

        /// <summary>
        /// P28 — 单位死亡飞出检测：遍历容器内 UnitBtn_* 子节点，
        /// 对比当前存活单位列表，为已消失的单位启动死亡飞出动画。
        /// 必须在 ClearChildren 之前调用（此时 RectTransform 仍有效）。
        /// </summary>
        private void DetectDeathsAndAnimate(Transform container, IEnumerable<CardInstance> survivors)
        {
            var survSet = new HashSet<int>(survivors.Select(u => u.uid));
            var canvas  = _rootCanvasRt.GetComponent<Canvas>();
            var cam     = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (!child.name.StartsWith("UnitBtn_")) continue;
                if (!int.TryParse(child.name.Substring(8), out int uid)) continue;
                if (survSet.Contains(uid)) continue;
                var rt = child.GetComponent<RectTransform>();
                if (rt == null) continue;
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector2 worldCenter = (corners[0] + corners[2]) * 0.5f;
                Vector2 screenPt    = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _rootCanvasRt, screenPt, cam, out Vector2 localPos))
                {
                    string n = _unitNames.TryGetValue(uid, out var saved) ? saved : "单位";
                    StartCoroutine(UnitDeathFly(localPos, n));
                    StartCoroutine(ExplosionBurst(localPos)); // P36
                }
            }
        }

        /// <summary>P28 — 单位死亡飞出：红色文字向上 +80px + 淡出 0.65s。</summary>
        private IEnumerator UnitDeathFly(Vector2 localPos, string label)
        {
            var ghost   = new GameObject("UnitDeathGhost");
            ghost.transform.SetParent(_rootCanvasRt, false);
            var ghostRt = ghost.AddComponent<RectTransform>();
            ghostRt.sizeDelta        = new Vector2(130f, 26f);
            ghostRt.anchoredPosition = localPos;
            var ghostTxt = ghost.AddComponent<Text>();
            ghostTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ghostTxt.fontSize  = 14;
            ghostTxt.color     = new Color(1f, 0.35f, 0.35f);
            ghostTxt.text      = $"\u2715 {label}";
            ghostTxt.alignment = TextAnchor.MiddleCenter;
            var ghostCg = ghost.AddComponent<CanvasGroup>();
            ghostCg.blocksRaycasts = false;
            ghostCg.interactable   = false;
            StartCoroutine(UITween.MoveY(ghostRt, 80f, 0.65f, UITween.Ease.OutQuad));
            yield return UITween.FadeOut(ghostCg, 0.65f);
            if (ghost != null) Destroy(ghost);
        }

        private void ShowCardDetail(CardInstance card)
        {
            _cardDetailText.text = FormatCardDetail(card);
            _cardDetailPanel.SetActive(true);
            StartCoroutine(UITween.PopIn(_cardDetailPanel.GetComponent<RectTransform>(), 0.4f));
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

        // ─────────────────────────────────────────────
        // P37 — 标题完整版 + 卡牌 3D 翻转
        // ─────────────────────────────────────────────

        /// <summary>P37 — 生成圆环 Sprite（size×size，外半径-1px，厚度 thickness px，白色）。</summary>
        private static Sprite MakeRingSprite(int size, float thickness)
        {
            var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pix    = new Color32[size * size];
            float center = size * 0.5f;
            float outerR = center - 1f;
            float innerR = outerR - thickness;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center, dy = y + 0.5f - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                bool onRing = dist >= innerR && dist <= outerR;
                pix[y * size + x] = onRing
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pix);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        }

        /// <summary>P37 — 标题大光晕持续金色脉冲（dim↔bright，3s 周期，随面板隐藏自动停止）。</summary>
        private IEnumerator TitleGlowPulse(Image img)
        {
            var dimCol    = new Color(0.78f, 0.67f, 0.43f, 0.04f);
            var brightCol = new Color(0.78f, 0.67f, 0.43f, 0.09f);
            while (img != null && img.gameObject.activeInHierarchy)
            {
                yield return UITween.TintTo(img, brightCol, 1.5f, UITween.Ease.InOutQuad);
                if (img == null || !img.gameObject.activeInHierarchy) yield break;
                yield return UITween.TintTo(img, dimCol,    1.5f, UITween.Ease.InOutQuad);
            }
        }

        /// <summary>
        /// P37 — 卡牌真实 3D Y 轴翻转：0°→90° InQuad(halfDur) → onMid() → 90°→0° OutBack(halfDur)。
        /// 替代原有 ScaleY 1→0→1 hack。
        /// </summary>
        private static IEnumerator CardFlip3D(RectTransform rt, System.Action onMid, float halfDur = 0.22f)
        {
            // 前半段：0° → 90° InQuad
            float t = 0f;
            while (t < halfDur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / halfDur);
                rt.localEulerAngles = new Vector3(0f, p * p * 90f, 0f);
                yield return null;
            }
            rt.localEulerAngles = new Vector3(0f, 90f, 0f);
            onMid?.Invoke();

            // 后半段：90° → 0° OutBack（弹开展示结果面）
            t = 0f;
            const float c1 = 1.70158f, c3 = 2.70158f;
            while (t < halfDur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / halfDur);
                float q = p - 1f;
                float e = 1f + c3 * q * q * q + c1 * q * q; // OutBack 0→1
                rt.localEulerAngles = new Vector3(0f, (1f - e) * 90f, 0f);
                yield return null;
            }
            rt.localEulerAngles = Vector3.zero;
        }

        // ─────────────────────────────────────────────
        // P36 — 背景纹理（六边形网格 + 拉丝条纹 + 噪点）
        // ─────────────────────────────────────────────

        /// <summary>P36 — 在背景面板添加 3 层程序化纹理叠加（均极低 alpha，raycastTarget=false）。</summary>
        private static void BuildBackgroundTextures(Transform parent)
        {
            AddTexOverlay(parent, "HexGrid",    MakeHexGridSprite());
            AddTexOverlay(parent, "BrushStripe", MakeBrushStripeSprite());
            AddTexOverlay(parent, "Noise",       MakeNoiseSprite());
        }

        private static void AddTexOverlay(Transform parent, string name, Sprite spr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite        = spr;
            img.type          = Image.Type.Tiled;
            img.color         = Color.white;
            img.raycastTarget = false;
        }

        /// <summary>P36 — 64×64 程序化六边形网格 Sprite（Repeat tile，alpha≈0.04）。</summary>
        private static Sprite MakeHexGridSprite()
        {
            const int Sz    = 64;
            const float R   = 9f;              // 六边形外接圆半径（px）
            const float InR = 7.794f;          // R * sqrt(3)/2，内切圆半径
            const byte  A   = 10;              // ≈ 0.04 alpha
            const float Thr = 0.6f;            // 边线宽度阈值（px）

            var tex = new Texture2D(Sz, Sz, TextureFormat.RGBA32, false);
            var pix = new Color32[Sz * Sz];

            for (int y = 0; y < Sz; y++)
            for (int x = 0; x < Sz; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                // 轴坐标（flat-top 六边形）
                float q = (2f / 3f) * px / R;
                float r = (-1f / 3f) * px / R + (Mathf.Sqrt(3f) / 3f) * py / R;
                float s = -q - r;
                // 取最近六边形中心
                float rq = Mathf.Round(q), rr = Mathf.Round(r), rs = Mathf.Round(s);
                float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
                if (dq > dr && dq > ds)      rq = -rr - rs;
                else if (dr > ds)            rr = -rq - rs;
                // 六边形中心像素坐标
                float cx = R * 1.5f * rq;
                float cy = R * Mathf.Sqrt(3f) * (rr + rq * 0.5f);
                float lx = px - cx, ly = py - cy;
                // Hexagon SDF（IQ 公式，flat-top，内切圆半径 InR）
                float d = SdHexagon(lx, ly, InR);
                pix[y * Sz + x] = Mathf.Abs(d) < Thr
                    ? new Color32(255, 255, 255, A)
                    : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pix);
            tex.Apply();
            tex.wrapMode   = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, Sz, Sz), Vector2.one * 0.5f, 1f);
        }

        /// <summary>六边形有向距离函数（flat-top，内切圆半径 r）。负值=内部，0=边线，正值=外部。</summary>
        private static float SdHexagon(float px, float py, float r)
        {
            const float kx = -0.866025404f, ky = 0.5f, kz = 0.577350269f;
            float ax = Mathf.Abs(px), ay = Mathf.Abs(py);
            float dot = Mathf.Min(kx * ax + ky * ay, 0f);
            ax -= 2f * dot * kx;
            ay -= 2f * dot * ky;
            float qx = ax - Mathf.Clamp(ax, -kz * r, kz * r);
            float qy = ay - r;
            return Mathf.Sqrt(qx * qx + qy * qy) * Mathf.Sign(qy);
        }

        /// <summary>P36 — 32×4 程序化拉丝金属条纹 Sprite（水平亮线间距 4px，alpha≈0.04）。</summary>
        private static Sprite MakeBrushStripeSprite()
        {
            const int W = 32, H = 4;
            const byte A = 10; // ≈ 0.04 alpha
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var pix = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                pix[0 * W + x] = new Color32(255, 255, 255, A); // 亮线
                pix[1 * W + x] = new Color32(0, 0, 0, 0);
                pix[2 * W + x] = new Color32(0, 0, 0, 0);
                pix[3 * W + x] = new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pix);
            tex.Apply();
            tex.wrapMode   = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, W, H), Vector2.one * 0.5f, 1f);
        }

        /// <summary>P36 — 128×128 程序化随机噪点 Sprite（alpha≈0.035）。</summary>
        private static Sprite MakeNoiseSprite()
        {
            const int Sz = 128;
            const byte A = 9; // ≈ 0.035 alpha
            var tex = new Texture2D(Sz, Sz, TextureFormat.RGBA32, false);
            var pix = new Color32[Sz * Sz];
            var rng = new System.Random(42);
            for (int i = 0; i < pix.Length; i++)
            {
                byte v = (byte)rng.Next(200, 256);
                pix[i] = new Color32(v, v, v, A);
            }
            tex.SetPixels32(pix);
            tex.Apply();
            tex.wrapMode   = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, Sz, Sz), Vector2.one * 0.5f, 1f);
        }

        // ─────────────────────────────────────────────
        // P36 — 爆炸粒子
        // ─────────────────────────────────────────────

        /// <summary>
        /// P36 — 单位死亡爆炸：12 粒子从 center 向 30° 间隔方向散射，
        /// 飞行 60px + FadeOut 0.5s OutQuad，颜色橙/青/金交替。
        /// DetectDeathsAndAnimate 触发。
        /// </summary>
        private IEnumerator ExplosionBurst(Vector2 center)
        {
            var colors = new Color[]
            {
                new Color(1f, 0.50f, 0.10f),             // 橙
                new Color(0.04f, 0.78f, 0.73f),           // 青
                new Color(0.78f, 0.67f, 0.43f),           // 金
                new Color(1f, 0.50f, 0.10f),
                new Color(0.04f, 0.78f, 0.73f),
                new Color(0.78f, 0.67f, 0.43f),
                new Color(1f, 0.50f, 0.10f),
                new Color(0.04f, 0.78f, 0.73f),
                new Color(0.78f, 0.67f, 0.43f),
                new Color(1f, 1f, 1f),
                new Color(1f, 1f, 1f),
                new Color(1f, 1f, 1f),
            };
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                StartCoroutine(ExplosionParticle(center, angle, colors[i]));
            }
            yield return null;
        }

        /// <summary>P36 — 单颗爆炸粒子：8×8px 圆形，OutQuad 飞行 + 同步淡出，0.5s 后自销毁。</summary>
        private IEnumerator ExplosionParticle(Vector2 center, float angle, Color col)
        {
            var go = new GameObject("ExplPart");
            go.transform.SetParent(_rootCanvasRt, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(8f, 8f);
            rt.anchoredPosition = center;

            var img = go.AddComponent<Image>();
            img.color         = col;
            img.raycastTarget = false;

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable   = false;

            float dx = Mathf.Cos(angle) * 60f;
            float dy = Mathf.Sin(angle) * 60f;
            const float Dur = 0.5f;
            float t = 0f;
            while (t < Dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / Dur);
                float e = 1f - (1f - p) * (1f - p); // OutQuad
                rt.anchoredPosition = center + new Vector2(dx * e, dy * e);
                cg.alpha = 1f - p;
                yield return null;
            }
            Destroy(go);
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }
        }

        // ─────────────────────────────────────────────
        // P35 — 文字光晕 / 法术投射物 / 拖拽漩涡
        // ─────────────────────────────────────────────

        /// <summary>
        /// P35 — 给 Text 元素添加 2 层金色 Shadow，模拟 text-shadow 光晕效果。
        /// </summary>
        private static void AddShadow(Text t)
        {
            var s1 = t.gameObject.AddComponent<Shadow>();
            s1.effectColor    = new Color(0.78f, 0.67f, 0.43f, 0.60f);
            s1.effectDistance = new Vector2(1f, -1f);
            var s2 = t.gameObject.AddComponent<Shadow>();
            s2.effectColor    = new Color(0.78f, 0.67f, 0.43f, 0.60f);
            s2.effectDistance = new Vector2(2f, -2f);
        }

        /// <summary>
        /// P35 — 在所有 ZoneDropTarget 中央生成 DropVortex 漩涡 GO（父节点为 rootCanvas）。
        /// BeginCardDrag 调用。
        /// </summary>
        private void SpawnDropVortices()
        {
            var zones = _rootCanvasRt.GetComponentsInChildren<ZoneDropTarget>(false);
            var canvas = _rootCanvasRt.GetComponent<Canvas>();
            var cam    = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            foreach (var zone in zones)
            {
                var rt = zone.GetComponent<RectTransform>();
                if (rt == null) continue;
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                var worldCenter = (corners[0] + corners[2]) * 0.5f;
                var screenPt = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _rootCanvasRt, screenPt, cam, out var localPos)) continue;
                var go = new GameObject("DropVortex");
                go.transform.SetParent(_rootCanvasRt, false);
                var vRt = go.AddComponent<RectTransform>();
                vRt.anchorMin = new Vector2(0.5f, 0.5f);
                vRt.anchorMax = new Vector2(0.5f, 0.5f);
                vRt.pivot     = new Vector2(0.5f, 0.5f);
                vRt.anchoredPosition = localPos;
                go.AddComponent<DropVortex>();
                _dropVortices.Add(go);
            }
        }

        /// <summary>P35 — 销毁所有拖拽漩涡 GO。EndCardDrag 调用。</summary>
        private void DestroyDropVortices()
        {
            foreach (var go in _dropVortices)
                if (go != null) Destroy(go);
            _dropVortices.Clear();
        }

        /// <summary>
        /// P35 — 法术施放投射物：24px 青色圆点从手牌区飞向敌方区域（0.45s OutQuad），
        /// 到达后触发目标区域高亮脉冲 + 0.05s 后全屏震动（0.38s），然后淡出销毁。
        /// </summary>
        private IEnumerator SpellProjectile()
        {
            var handRt  = _playerHandTrans?.GetComponent<RectTransform>();
            var enemyRt = _enemyZoneTrans?.GetComponent<RectTransform>();
            if (handRt == null || enemyRt == null) yield break;

            Vector2 startLocal = RtToRootLocal(handRt);
            Vector2 endLocal   = RtToRootLocal(enemyRt);

            var proj = new GameObject("SpellProj");
            proj.transform.SetParent(_rootCanvasRt, false);
            var rt = proj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.anchoredPosition = startLocal;

            var img = proj.AddComponent<Image>();
            img.color = C_Cyan;
            img.raycastTarget = false;
            var sh = proj.AddComponent<Shadow>();
            sh.effectColor    = new Color(0.04f, 0.78f, 0.73f, 0.80f);
            sh.effectDistance = new Vector2(0f, 0f);

            var cg = proj.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable   = false;

            // 飞行 0.45s OutQuad
            const float dur = 0.45f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - p) * (1f - p); // OutQuad
                rt.anchoredPosition = Vector2.Lerp(startLocal, endLocal, e);
                yield return null;
            }

            // 目标区域高亮脉冲（0.35s）
            if (_enemyZonePanelImg != null)
                StartCoroutine(UITween.PulseColor(_enemyZonePanelImg, C_Cyan, 0.35f));

            // 延迟 0.05s 后冲击震动
            yield return new WaitForSeconds(0.05f);
            StartCoroutine(UITween.Shake(_rootCanvasRt, 3.5f, 0.38f, 10));

            // 淡出 0.15s
            yield return UITween.FadeOut(cg, 0.15f);
            Destroy(proj);
        }

        /// <summary>P35 — 将 RectTransform 的世界中心点转换为 rootCanvas 本地坐标。</summary>
        private Vector2 RtToRootLocal(RectTransform rt)
        {
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var worldCenter = (corners[0] + corners[2]) * 0.5f;
            var canvas = _rootCanvasRt.GetComponent<Canvas>();
            var cam    = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var screenPt = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rootCanvasRt, screenPt, cam, out var localPos);
            return localPos;
        }
    }
}
