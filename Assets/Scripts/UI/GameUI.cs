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

        // 日志面板
        private Text       _logText;
        private ScrollRect _logScroll;

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

            _gm.StartGame(DeckFactory.MakeKaisaVsMasterYi());
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
                $"[敌方]  积分:{G.eScore}/8  法力:{G.eMana}  手牌:{G.eHand.Count}  " +
                $"符文:{G.eRunes.Count}  {legTxt}  " +
                $"回合:{G.round}  阶段:{G.phase}";
        }

        private void RefreshEnemyZone()
        {
            ClearChildren(_enemyZoneTrans);
            var G = _gm.G;
            // 显示敌方基地单位（不可交互，仅显示）
            foreach (var u in G.eBase)
                AddLabel(_enemyZoneTrans, UnitLabel(u), Color.red);
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

            for (int i = 0; i < G.pRunes.Count; i++)
            {
                var r = G.pRunes[i];
                int capturedIdx = i;
                string runeLabel = RuneLabel(r);
                Color  runeCol   = r.tapped ? Color.gray : Color.green;

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
            }

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

            foreach (var card in G.pHand)
            {
                bool isSel = _sel.IsCardSelected && _sel.SelectedUid == card.uid;
                Color col = isSel ? Color.cyan : Color.white;
                int capturedUid = card.uid;

                var btn = AddButton(_playerHandTrans,
                    CardLabel(card), col,
                    () =>
                    {
                        _sel.ToggleCard(capturedUid);
                        Refresh();
                    });
                _ = btn;
            }
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
                $"[我方]  积分:{G.pScore}/8  法力:{G.pMana}  手牌:{G.pHand.Count}  " +
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
        // 事件处理器
        // ─────────────────────────────────────────────

        private void AppendLog(string msg)
        {
            _logAccum += msg + "\n";
            if (_logText != null)
            {
                _logText.text = _logAccum;
                // 强制滚动到底部
                Canvas.ForceUpdateCanvases();
                if (_logScroll != null)
                    _logScroll.verticalNormalizedPosition = 0f;
            }
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

            // ── 敌方信息栏（顶部 8%）──
            var enemyInfoPanel = MakePanel(root, "EnemyInfoPanel",
                new Vector2(0, 0.92f), new Vector2(0.75f, 1f),
                new Color(0.1f, 0.1f, 0.2f, 0.85f));
            _enemyInfoText = MakeText(enemyInfoPanel.transform, "EnemyInfoText", 13);

            // ── 敌方区域（86-92%）──
            var enemyZonePanel = MakePanel(root, "EnemyZonePanel",
                new Vector2(0, 0.77f), new Vector2(0.75f, 0.92f),
                new Color(0.2f, 0.05f, 0.05f, 0.7f));
            _enemyZoneTrans = MakeScrollContent(enemyZonePanel.transform, "EnemyZoneContent",
                horizontal: true);

            // ── 战场（44-77%）──
            var bfPanel = MakePanel(root, "BattlefieldPanel",
                new Vector2(0, 0.34f), new Vector2(0.75f, 0.77f),
                new Color(0.05f, 0.1f, 0.15f, 0.7f));
            var bfLayout = bfPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            bfLayout.childControlWidth  = true;
            bfLayout.childControlHeight = true;
            bfLayout.spacing = 4;
            bfLayout.padding = new RectOffset(4, 4, 4, 4);

            _bf0Trans = MakeScrollContent(bfPanel.transform, "BF0Content", horizontal: false);
            _bf1Trans = MakeScrollContent(bfPanel.transform, "BF1Content", horizontal: false);

            // ── 玩家基地（25-34%）──
            var playerBasePanel = MakePanel(root, "PlayerBasePanel",
                new Vector2(0, 0.25f), new Vector2(0.75f, 0.34f),
                new Color(0.05f, 0.1f, 0.2f, 0.7f));
            _playerBaseTrans = MakeScrollContent(playerBasePanel.transform, "PlayerBaseContent",
                horizontal: true);

            // ── 玩家符文（16-25%）──
            var playerRunePanel = MakePanel(root, "PlayerRunePanel",
                new Vector2(0, 0.16f), new Vector2(0.75f, 0.25f),
                new Color(0.1f, 0.15f, 0.05f, 0.7f));
            _playerRuneTrans = MakeScrollContent(playerRunePanel.transform, "PlayerRuneContent",
                horizontal: true);

            // ── 玩家手牌（5-16%）──
            var playerHandPanel = MakePanel(root, "PlayerHandPanel",
                new Vector2(0, 0.05f), new Vector2(0.75f, 0.16f),
                new Color(0.05f, 0.05f, 0.15f, 0.7f));
            _playerHandTrans = MakeScrollContent(playerHandPanel.transform, "PlayerHandContent",
                horizontal: true);

            // ── 玩家信息 + 操作栏（0-5%）──
            var actionPanel = MakePanel(root, "ActionPanel",
                new Vector2(0, 0f), new Vector2(0.75f, 0.05f),
                new Color(0.1f, 0.1f, 0.1f, 0.9f));
            var actionLayout = actionPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.childControlWidth  = false;
            actionLayout.childControlHeight = true;
            actionLayout.spacing = 6;
            actionLayout.padding = new RectOffset(4, 4, 2, 2);

            _playerInfoText = MakeText(actionPanel.transform, "PlayerInfoText", 12);
            var pInfoRt     = _playerInfoText.GetComponent<RectTransform>();
            pInfoRt.sizeDelta = new Vector2(600, 0);

            (_endTurnBtn, _endTurnBtnText) = MakeButton(actionPanel.transform, "结束回合", 14,
                () => { _sel.Clear(); _gm.PlayerEndTurn(); });

            (_legendBtn, _legendBtnText) = MakeButton(actionPanel.transform, "传奇技能", 12,
                () => { _gm.ActivateLegendAbility("ability1"); });

            // ── 右侧日志面板（75-100%宽，全高）──
            var logPanel = MakePanel(root, "LogPanel",
                new Vector2(0.75f, 0f), new Vector2(1f, 1f),
                new Color(0.05f, 0.05f, 0.05f, 0.9f));

            AddLabel(logPanel.transform, "=== 战斗日志 ===", Color.cyan);

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
            _mulliganPanel = MakePanel(root, "MulliganPanel",
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
            _duelPanel = MakePanel(root, "DuelPanel",
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
            _gameOverPanel = MakePanel(root, "GameOverPanel",
                new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.6f),
                new Color(0f, 0f, 0f, 0.9f)).gameObject;
            _gameOverText = MakeText(_gameOverPanel.transform, "GameOverText", 24);
            _gameOverText.alignment = TextAnchor.MiddleCenter;
            MakeButton(_gameOverPanel.transform, "再来一局", 16,
                () =>
                {
                    _gameOverPanel.SetActive(false);
                    _sel.Clear();
                    _gm.StartGame(DeckFactory.MakeKaisaVsMasterYi());
                });
            _gameOverPanel.SetActive(false);
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
            _ = btn;
        }

        private static Button AddButton(Transform parent, string label, Color col,
            UnityEngine.Events.UnityAction onClick)
        {
            var (btn, lbl) = MakeButton(parent, label, 11, onClick);
            lbl.color = col;
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
