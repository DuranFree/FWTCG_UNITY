# 开发日志 — FWTCG Unity 移植

---

## 2026-03-28 — P20: 拖拽出牌

**Phase**: P20 Drag to Play

**Modified files**:
- `Assets/Scripts/UI/GameUI.cs` — 拖拽字段、BeginCardDrag/UpdateCardDrag/EndCardDrag 方法；AddZoneButton 添加 ZoneDropTarget；RefreshPlayerHand 添加 CardDragHandler
- `Assets/Scripts/UI/UISelectionState.cs` — 新增 SelectCard() 方法
- `Assets/Scripts/UI/CardDragHandler.cs` — 新文件，IBeginDragHandler/IDragHandler/IEndDragHandler
- `Assets/Scripts/UI/ZoneDropTarget.cs` — 新文件，记录 zone 字符串供 RaycastAll 查找

**New features**:
- 拖拽出牌：按住可出牌的手牌（canPlay=true）拖动，生成 DragGhost（深青底金色文字，挂在 Canvas 根）
- DragGhost 随指针移动（ScreenPointToLocalPointInRectangle 转 Canvas 坐标）
- 松手时 EventSystem.RaycastAll → 找 ZoneDropTarget → 触发 OnZoneClicked，等效点击出牌
- 不可出牌的手牌不挂 CardDragHandler（无视拖拽）
- UISelectionState.SelectCard() — 强制选中，不 toggle，拖拽放置时使用

**Test results**: 391/391（纯UI层，无新测试）

**Design decisions**:
- DragGhost 的 Image 和 Text 均设 raycastTarget=false，避免遮挡 ZoneDropTarget 的 RaycastAll 检测
- 只在 canPlay=true 时添加 CardDragHandler，防止玩家拖拽不可出的牌触发意外行为
- Ghost 挂在 _rootCanvasRt，不在任何 LayoutGroup 内，确保自由定位
- EndDrag 始终清理 ghost，即使未找到有效区域（防止 ghost 残留）

---

## 2026-03-28 — P19: 标题界面 + 符文入场动画 + 技术债清理

**Phase**: P19 Title Screen, Rune Stagger Animation, Tech Debt

**Modified files**:
- `Assets/Scripts/UI/GameUI.cs` — TitlePanel；Start() 改为显示标题界面；游戏结束回到标题；符文入场错落动画；PhaseBanner CanvasGroup 预创建修复

**New features**:
- `TitlePanel` — 全屏深黑覆盖层，金色48px标题"风舞天际"+ 青色18px副标题 + "开始游戏"按钮；BuildCanvas最后添加确保最高层级
- `Start()` 不再自动 StartGame，改为直接显示 TitlePanel
- 游戏结束"再来一局"→ 重置 `_prevHandUids`/`_prevRuneCount`/`_lastPhase`/`_lastTurn` 后显示 TitlePanel
- `RefreshPlayerRunes` — 检测 i >= oldCount，每张新符文延迟 i*50ms 触发 `DelayedPopIn(0.25s)`
- `DelayedPopIn(rt, duration, delay)` — 静态 IEnumerator，等待 delay 后调用 UITween.PopIn
- 技术债清理：BannerSequence CanvasGroup 懒加载改为 BuildCanvas 预创建；传奇初始HP条目已确认存在于 DeckFactory，删除

**Test results**: 391/391（纯UI层，无新测试）

**Design decisions**:
- `_prevRuneCount` 用 int 而非 HashSet，因为符文无 UID，用索引范围判断新符文
- TitlePanel 在 BuildCanvas 最后添加，利用 uGUI 后建兄弟节点在上层的特性实现覆盖效果
- 重新开始时主动清零所有动画追踪状态，防止残留旧 UID 导致新游戏动画不播放

---

## 2026-03-28 — P18: 弃牌堆查看器 + 战斗震动 + 阶段横幅

**Phase**: P18 Discard Viewer, Combat Shake, Phase Banner

**Modified files**:
- `Assets/Scripts/UI/GameUI.cs` — DiscardPanel 模态框；PhaseBanner 渐入渐出；AppendLog 接入 Shake；Refresh 阶段/回合切换检测

**New features**:
- `ShowDiscardPile()` — 列出 pDiscard/eDiscard 全部卡牌，DiscardPanel SetActive(true)
- `BannerSequence()` — CanvasGroup FadeIn(0.25s)→等待1.1s→FadeOut(0.3s)→SetActive(false)
- `PhaseName(GamePhase)` — 阶段枚举→中文名称 switch 表达式
- `_lastPhase`/`_lastTurn` 追踪，Refresh 检测切换 → ShowPhaseBanner
- 回合横幅文字：玩家回合="— 你的回合 —"，敌方="— 对手回合 —"
- 阶段横幅文字：觉醒/开始/召唤/摸牌/行动/结束 阶段
- 死亡震动：AppendLog 检测 `msg.Contains("死亡")` → `UITween.Shake(_rootCanvasRt, 3.5f, 0.38f, 10)`
- `_rootCanvasRt` 存储 Canvas 根 RectTransform（BuildCanvas 中赋值）

**Test results**: 391/391（纯UI层，无新测试）

**Design decisions**:
- BannerSequence 使用 CanvasGroup（懒加载 AddComponent）保持 Banner GameObject 结构简单
- 震动强度 3.5px / 0.38s，轻量感，不干扰游戏画面
- 阶段横幅只在 phase 或 turn 真实切换时触发，避免 Refresh 频繁调用重复弹出

---

## 2026-03-28 — P17: 卡牌详情预览 + 平台适配基础

**Phase**: P17 Card Detail Preview & Platform Adaptation

**Modified files**:
- `Assets/Scripts/UI/GameUI.cs` — 横屏锁定 + multiTouch 禁用；CardDetailPanel 模态框；手牌/敌方基地"详"按钮；ShowCardDetail/FormatCardDetail 方法

**New features**:
- `Screen.orientation = LandscapeLeft` + `Input.multiTouchEnabled = false` — Awake() 最先执行，确保移动端横屏且无缩放误触
- `CardDetailPanel` — 居中模态框（0.12-0.75 × 0.08-0.92），深蓝背景，VerticalLayoutGroup；含 CardDetailText（14px）+ 关闭按钮
- `ShowCardDetail(CardInstance)` + `FormatCardDetail` — 显示：卡名、类型/地域/费用、符能费、战力、关键词、效果文字
- 手牌每行改为 HorizontalGroup（[牌按钮]+[详按钮]），入场 PopIn 移至 row RectTransform
- 敌方基地单位也加"详"按钮

**Test results**: 391/391（纯UI层，无新测试）

**Design decisions**:
- `lore` 字段只在 CardData 不在 CardInstance，FormatCardDetail 不显示 lore
- 详情面板宽度限于主游戏区（0.75以内），不覆盖日志面板

---

## 2026-03-28 — P16: 状态色 + 积分轨道

**Phase**: P16 State Colors & Score Track

**Modified files**:
- `Assets/Scripts/UI/GameUI.cs` — 手牌可打状态色（textCol 绿/灰/青 + bgCol 深绿/暗/深青）；ScoreBar Unicode 轨道；RefreshPlayerInfo/EnemyInfo 嵌入积分轨道

**New features**:
- `ScoreBar(int score)` — 返回 "■■■□□□□□" 形式字符串，长度固定8，嵌入玩我/敌方信息栏
- 可打出牌高亮：`_gm.CD.CanPlay(card, Owner.Player)` 检查，可打 → 绿文字(#40e88a)+深绿bg，不可打 → 灰色降暗，已选中 → 青色+深青bg
- `AddButton` 新增 `Color? bgColor` 可选参数，允许覆盖按钮背景色

**Test results**: 391/391（纯UI层，无新测试）

**Design decisions**:
- ScoreBar 嵌入现有文字栏而非新建面板，避免改动 Canvas 布局
- `canPlay` 检查发生在 RefreshPlayerHand，非玩家回合时 canPlay=false → 所有手牌显示暗色

---

## 2026-03-28 — P15: 视觉动画层（符文配色 + 战场光晕 + 手牌入场）

**Phase**: P15 Visual Animation Layer

**Modified files**:
- `Assets/Scripts/UI/GameUI.cs` — 添加 BFGlowLoop 战场呼吸动画协程（Sin × InOutQuad，约 4.2s 周期；ctrl=Player 青/Enemy 红/null 灰）；6种符文专属色常量（炽烈橙/灵光黄/翠意绿/摧破红/混沌紫/序理蓝）；手牌入场 PopIn 动画（新 uid 检测，0.28s OutBack）

**New features**:
- `BFGlowLoop()` — 持续 Coroutine，每帧根据 bf[0/1].ctrl 更新 `_bf0PanelImg`/`_bf1PanelImg` 颜色，base=C_BFBg，glow 强度 max 30%
- `BFCtrlColor(Owner?)` — 静态 switch，Player→C_Cyan，Enemy→红(0.9,0.2,0.2)，null→灰
- `RuneColor(RuneType)` — 静态 switch，6 种符文 → 专属 Color 常量
- `_prevHandUids` (HashSet<int>) — 追踪上次渲染手牌，仅对新加入 uid 触发 `UITween.PopIn`
- `_bf0PanelImg`/`_bf1PanelImg` (Image) — BuildCanvas 中对 BF 内容区添加 Image 组件并存储引用

**Test results**: 391/391（无新测试，纯视觉层）

**Design decisions**:
- BF 光晕用单个持久 Coroutine（非每次 Refresh 重启），避免光晕闪烁/重置
- 符文横置时亮度乘 0.38（非纯灰），保留色相以便玩家仍能识别符文类型
- PopIn 改用 0.28s（短于默认 0.35s），减少每次打出牌的延迟感

---

## 2026-03-28 — P14: 视觉特效基础层（无 DOTween）

**Phase**: P14 Visual Effects (Base Layer)

**New files**:
- `Assets/Scripts/UI/UITween.cs` — 轻量 Coroutine 补间工具（FadeIn/Out, TintTo, PulseColor, MoveY, ScaleTo, PopIn, Shake），5种缓动函数，替代 DOTween 基础功能
- `Assets/Scripts/UI/ToastSystem.cs` — 浮动通知单例，淡入上滑+淡出队列，Canvas sortingOrder=100 置顶
- `Assets/Scripts/UI/DamageFloatText.cs` — 伤害数字浮起系统，5色分类（伤害红/治疗绿/增益蓝/减益橙/金色），1.2s OutQuad 向上60px

**Modified files**:
- `Assets/Scripts/UI/GameUI.cs` — 接入 Toast/DamageFloat，添加 LoR 主题色常量（C_Gold/C_Dark/C_Cyan 等），应用到全部面板背景
- `Assets/Scripts/UI/DeckFactory.cs` — 修复 cardName bug（`name = "..."` → `cardName = "..."`，Make() 补 `so.cardName = template.cardName`）

**Test results**: 391/391 passed（无新测试，纯 UI/视觉层不含可测逻辑）

**Design decisions**:
- DOTween 未在项目中安装，用 Coroutine + `UITween.Lerp01` 自行实现等价动画，接口设计参考 DOTween API 风格
- ToastSystem 仅触发"重要事件"（积分/战斗/死亡/征服/据守/进化/对决关键词），避免每条日志都弹窗
- `ApplyEasePublic` 作为 `private ApplyEase` 的公共包装，供 ToastSystem 等外部调用

**Problems encountered**:
- `ToastSystem.cs` 引用 `UITween.ApplyEasePublic` 但该方法初版是 `private` → 添加 `public static` 包装解决
- cardName bug（P13 遗留技术债）在 P14 开始前热修

---

## 2026-03-28 — P13: 主游戏 UI（自建 Canvas）

**Phase**: P13 Main Game UI

**New files**:
- `Assets/Scripts/UI/UISelectionState.cs` — 纯 C# 选牌/选单位状态机（UIPhase：Idle/CardSelected/UnitSelected）
- `Assets/Scripts/UI/DeckFactory.cs` — 运行时牌组配置工厂，无需 .asset 文件，含卡莎 vs 易大师全套 40 张卡
- `Assets/Scripts/UI/GameUI.cs` — 自建 uGUI Canvas 的 MonoBehaviour；Awake 构建 9 个面板，订阅 GameManager.OnStateChanged → Refresh()
- `Assets/Tests/EditMode/UISelectionStateTests.cs` — 14 个行为测试，覆盖状态机全转换路径

**Test results**: 391/391 passed（P12 377 → P13 391，+14 新测试）

**Design decisions**:
- 采用运行时全量构建方案（无 Unity Editor 场景依赖），DeckFactory 用 `ScriptableObject.CreateInstance<CardData>()` 代替 .asset 文件
- GameUI.Refresh() 全量重建面板子节点（简洁正确，避免增量更新复杂度）
- 两次点击出牌流：点击手牌 → ToggleCard → 点击区域按钮 → PlayCard/MoveUnit → Clear
- 对决、游戏结束、换牌三个 overlay 面板，通过 SetActive(bool) 切换可见性

**Problems encountered**:
- `DeckFactory.cs` 编译错误：`new[]{"..."}` 不能赋给 `List<string>`，`"blazing"` 不能赋给 `RuneType` enum → 全部改为 `new List<string>{...}` 和 `RuneType.Blazing`
- `GameUI.cs` 编译错误：`MakePanel()` 返回 `Image`，对其调用 `AddComponent<>()` 报错 → 改为 `.gameObject.AddComponent<>()`

---

## 2026-03-27 — P12: TDD 规范修复（测试重构）

**Phase**: P12 TDD 规范合规修复

**New files**: 无

**Modified files**:
- `Assets/Tests/EditMode/RuneControllerTests.cs` — 重写所有断言 `pendingRunes` 内部状态的测试（Rule 2 违规），改为通过 `ConfirmRunes()` 验证最终行为
- `Assets/Tests/EditMode/SpellSystemTests.cs` — 移除 `OnAiDuelTurn` mock stub（Rule 3 违规），改用真实 `_ai.AiDuelAction`；删除 `SkipDuel_Once_FlipsToEnemy`（同时违反 Rule 2 + Rule 3）
- `Assets/Tests/EditMode/GameInitializerTests.cs` — 移除 `ShuffleCards/ShuffleRunes` 注入 `_ => {}` stub（Rule 3 违规）；合并横向单字段测试为行为测试（Rule 1 违规）；删除 `SelectBattlefields_DoesNotModifyBFPool`（Rule 2 违规）

**Test results**: 377/377 passed（P11 384 → P12 377，净减 7 个冗余/违规测试）

**Design decisions**:
- 移除 7 个违规测试，未损失任何真实行为覆盖率
- RuneController 测试统一走 TapRune→ConfirmRunes 完整路径，验证法力/符文状态
- SpellSystem 对决测试用空手牌 AI（无牌 → 自动跳过），避免 mock 自己的代码
- GameInitializer shuffle 委托本质是算法实现，不属于系统边界，不应 mock

**Problems encountered**:
- Unity batch mode 加 `-quit` 标志会在测试执行前退出 → 去掉 `-quit` 解决

---

## 2026-03-27 — P11: GameManager（MonoBehaviour 桥接层）

**Phase**: P11 Unity 桥接 — GameManager

**New files**:
- `Assets/Scripts/Core/GameManager.cs` — MonoBehaviour 单例，持有全部纯 C# 系统，将逻辑层接入 Unity 生命周期：
  - Awake: 实例化所有系统 + 注入跨系统依赖 + AI.Schedule → Coroutine + Timer.OnTimeout → PlayerEndTurn
  - StartGame(DeckConfig): 调用 GI.SetupDecks / CoinFlip / SelectBattlefields → 触发 OnStateChanged
  - ConfirmMulligan(List<int>): 调用 GI.ConfirmMulligan → 启动 RunGame() Coroutine
  - RunGame / RunTurn 协程：五阶段（Awaken/Start/Summon/Draw/Action）→ 玩家等待 G.phase==End，AI AiAction() 自调 TM.DoEndPhase() → G.phase=End
  - 计时器：Update() 每帧 Tick；超时自动调 TM.PlayerEndTurn()
  - 玩家 API：PlayerEndTurn / PlayCard / TapRune / RecycleRune / MoveUnit / ActivateLegendAbility / DuelSkip / DuelPlayCard
  - 事件：OnStateChanged / OnPhaseChanged / OnLog / OnGameOver / OnTimerTick

**Test results**: 384/384 passed（GameManager 是 MonoBehaviour，无 EditMode 测试；编译通过，现有测试全绿）

**Design decisions**:
- 回合结束信号通过 `G.phase == GamePhase.End` 检测，而非额外 flag；DoEndPhase() 无论被谁调用（玩家/AI/超时）都能正确触发
- AI.Schedule → `StartCoroutine(ScheduleCoroutine(delay, fn))` 实现 0.7s 逐步行动
- LegendSystem 构造为 `new LegendSystem(G, TM)`（只接受2个参数，非4个）
- 对外 API 尽量细粒度，UI 层只调用，不直接修改 G

**Technical debt**: 无新增

---

## 2026-03-27 — P10: 游戏初始化流程 & Meta

**Phase**: P10 游戏流程 & Meta（GameInitializer / TurnTimerSystem / LocalizationTable / ForesightMech）

**New files**:
- `Assets/Scripts/Core/GameInitializer.cs` — 游戏初始化流程：CoinFlip（先后手）/ SetupDecks（牌堆+符文堆+传奇+战场牌池+初始摸牌4张）/ SelectBattlefields（各方随机选1张战场牌）/ ConfirmMulligan（最多换2张手牌）；KaisaRuneTypes/MasterYiRuneTypes 便利工厂；ShuffleCards/ShuffleRunes 可注入
- `Assets/Scripts/Core/TurnTimerSystem.cs` — 30秒倒计时逻辑：Reset/Start/Stop/Tick(delta)/OnTimeout；IsRunning 状态；TimeRemaining 属性（整秒，ceil）
- `Assets/Scripts/Core/LocalizationTable.cs` — 中文字符串表（当前仅中文）：Get(key) / Format(key, args)；覆盖阶段、回合、翻币、Mulligan、区域、战斗、法术对决、积分、符文、关键词、先见机甲等全套字符串
- `Assets/Tests/EditMode/GameInitializerTests.cs` — 54 项行为验证测试

**Modified files**:
- `CardDeployer.cs` — 新增 PromptForesight delegate；实现 foresight_mech_enter（查看库顶，可选回收至库底；空牌库无抛异常）

**Test results**: 384/384 passed (prior 330 + 54 new P10)

**Design decisions**:
- GameInitializer 独立于 ScriptableObject，通过 DeckConfig 注入卡牌数据，EditMode 测试可直接 new
- ShuffleCards / ShuffleRunes 均为 Action<List<T>>，测试注入空操作以固定顺序
- CoinFlip: GetRandom() < 0.5f → Player；边界值 0.5f → Enemy（与 JS Math.random() < 0.5 一致）
- ConfirmMulligan: 降序移除避免索引位移；超出2张限制取前2个（降序后最大的2个）
- foresight_mech_enter: PromptForesight(topCard) → true 则 RemoveAt(deck.Count-1) + Insert(0, card)；不给符能（与卡牌文字一致：仅循环，不是"回收"）

**Technical debt**: 传奇 CardData ScriptableObject 尚未在 Unity Editor Inspector 中配置 HP/ATK/Cost

---

## 2026-03-27 — P9: 战场牌效果全集

**Phase**: P9 卡牌效果全集 (BattlefieldSystem)

**New files**:
- `Assets/Scripts/Core/BattlefieldSystem.cs` — 集中实现全部 16 张战场牌触发逻辑：OnHold / ModifyAddScore / IsTiyanaBlockingHold / OnCombatStart / OnConquer / OnDefenseFailure / OnUnitEnterBF / OnUnitLeaveBF / OnSpellTargetAlly / ModifySpellDamage / CanDeployToBF / CanMoveToBase；5个 Prompt 委托（PromptBaseUnit / PromptDiscardUnit / PromptHandCard / PromptTappedRune / PromptConfirm）
- `Assets/Tests/EditMode/BattlefieldSystemTests.cs` — 42 项行为验证测试（涵盖全部 16 张战场牌 + 缇亚娜）

**Modified files**:
- `TurnManager.cs` — SetBattlefieldSystem；DoStart 调用 OnHold；AddScore 调用 ModifyAddScore + IsTiyanaBlockingHold
- `CombatResolver.cs` — SetBattlefieldSystem；TriggerCombat 伤害前 OnCombatStart；征服后 OnConquer + OnDefenseFailure
- `CardDeployer.cs` — SetBattlefieldSystem；DeployToBF 前置 CanDeployToBF 检查；MoveUnit 增加 CanMoveToBase + OnUnitLeaveBF + OnUnitEnterBF 调用
- `SpellSystem.cs` — SetBattlefieldSystem；新增 DealSpellDmg 包装器（经 ModifySpellDamage 后调用 _cd.DealDamage）；ApplySpell 增加 dreaming_tree 触发（OnSpellTargetAlly）

**Test results**: 330/330 passed (prior 288 + 42 new P9)

**Design decisions**:
- BattlefieldSystem 独立类（非 MonoBehaviour），通过 SetBattlefieldSystem 注入到 4 个系统
- DealSpellDmg 包装所有法术伤害，void_gate (+1) 通过 ModifySpellDamage 透明加成
- ascending_stairs 实现与 text 不一致（text: WIN_SCORE+1; 实现: 每次+1pts）—— 记入 tech-debt 等规则确认

**Technical debt**: ascending_stairs text/实现不一致待规则确认；先见机甲 P10 补全

**Problems encountered**:
- DealSpellDmg 方法体内部的 `_cd.DealDamage(...)` 也被 replace_all 误替换成 `DealSpellDmg(...)` → 自调用导致 StackOverflowException；手动修正后全绿

---

## 2026-03-27 — P8: 传奇系统

**Phase**: P8 传奇系统 (Legend System)

**New files**:
- `Assets/Scripts/Core/LegendSystem.cs` — AbilityDef 技能定义表（硬编码 kaisa/masteryi）；CheckLegendPassives / TriggerLegendEvent / CanUseLegendAbility / ActivateLegendAbility / ResetLegendAbilitiesForTurn；AiLegendActionPhase / AiLegendDuelAction
- `Assets/Tests/EditMode/LegendSystemTests.cs` — 25 项行为验证测试

**Modified files**:
- `GameState.cs` — LegendInstance 新增 `maxHp`（进化后动态最大HP）
- `TurnManager.cs` — SetLegendSystem；StartTurn 调用 ResetLegendAbilitiesForTurn
- `CombatResolver.cs` — SetLegendSystem；TriggerCombat 伤害前 TriggerLegendEvent("onCombatDefend")，CleanDead 后 CheckLegendPassives
- `CardDeployer.cs` — SetLegendSystem；DeployToBase/ToBF 调用 CheckLegendPassives
- `AIController.cs` — SetLegendSystem；AiAction P8 调用 AiLegendActionPhase；AiDuelAction P8 调用 AiLegendDuelAction

**Test results**: 288/288 passed (prior 263 + 25 new P8)

**Design decisions**:
- 技能定义硬编码在 `_abilityDefs` 字典（按 data.id 查表），不用 ScriptableObject 嵌套
- kaisa_void_sense: once=false（每次只要未 exhausted 均可激活）
- maxHp 字段新增，避免修改只读 data.hp（ScriptableObject）

**Technical debt**: 无

**Problems encountered**: 无（首次无编译错误/测试失败的 Phase）

---

## P5（移植）: 基础 AI — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 ai.js 全文（aiAction / aiDecideMovement / aiCardValue / aiBoardScore / aiDuelAction）
- Assets/Scripts/Core/AIController.cs — AI 决策核心：
  - `AiCardValue(card)`：(atk/cost)×10 + 关键词加成（急速+4/壁垒+3/强攻+2/绝念+2）
  - `AiBoardScore()`：scoreDiff×3 + handDiff×0.5 + bfControl×2 + unitPow×0.3
  - `AiEvalBattlefield(i)` / `AiSimulateCombat(movers, i)`：局面评估
  - `AiDecideMovement(active)`：8种情景评分（空战场征服/战斗胜负/紧迫感/分兵策略）
  - `AiAction()`：横置符文→出随从（价值排序）→移动→结束回合；急速单位以活跃入场
  - `Schedule` 委托：测试注入同步版本，Unity 注入协程版本
  - P6/P8 存根：法术施放、传奇技能
  - `AiDuelAction()`：P5 返回 false，P6 完整实现
- `MoveDecision` / `BfEval` / `CombatSim` 辅助数据结构
- Assets/Tests/EditMode/AIControllerTests.cs — 31 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (179/179，含 P1~P4 的 148 个)

**Decisions made**:
- Schedule 委托注入：与 Unity 协程解耦，测试时同步执行完整 AI 回合
- 急速单位入场：AIController 传 `enterActive: hasHaste` 给 DeployToBase，与原版行为一致
- 测试中同步调度器导致 DoEndPhase 在 AiAction 结束后立即执行，eMana 归零；测试只验证符文 tapped 状态

**Technical debt**: 无

**Problems encountered**:
- 2 个测试初版写错预期值：TapsAllUntappedRunes 断言 eMana==2（DoEndPhase 已归零）；HasteUnit_EntersNonExhausted 未锁 BF 槽位（单位被移到战场后 eBase 为空）——均已修正
- Unity "project already open" crash：kill 旧进程→删 lockfile→重新运行（P4 已知问题）

---

## P4（移植）: 基础战斗结算 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 combat.js 全文（triggerCombat/cleanDead/roleAtk/assignDmg）
- CardData.cs 补充：`guardBonus` 字段（默认1，等价原版 `u.guardBonus || 1`）
- CardInstance.cs 补充：`guardBonus` 字段，From() 和 Mk() 均已复制
- Assets/Scripts/Core/CombatResolver.cs — 战斗结算核心：
  - `RoleAtk(u, role)`：atk(u) + 强攻 strongAtkBonus（进攻方）+ 坚守 guardBonus（防守方）
  - `AssignDmg(pool, targets)`：壁垒单位优先吸收，同组内按最低HP先吸收
  - `TriggerCombat(bfId, attacker)`：计算双方战力→分配伤害→压制溢出打传奇→CleanDead→HP重置（规则627.5）→胜负判定（Conquer/Defend/Draw/BothDead）
  - `CleanDead(bfId)`：BF侧（装备召回基地、death_shield检查、deathwish、重置、废牌）+ 基地侧（同步清理）
- `CombatResult` 枚举：Conquer / Defend / Draw / BothDead
- Assets/Tests/EditMode/CombatResolverTests.cs — 28 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (148/148，含 P1~P3 的 120 个)

**Decisions made**:
- CombatResolver 依赖 CardDeployer（共用 TryDeathShield + TriggerDeathwish），避免逻辑重复
- reckoner_arena 战场牌在战斗时赋予关键词 → P5 实现
- postCombatTriggers（战场牌征服效果）→ P5 实现
- triggerLegendEvent('onCombatDefend', ...) 易大师独影剑鸣 → P5 连接

**Technical debt**: 无

**Problems encountered**: 运行测试时 Unity 因"project already open in another instance"崩溃——与 P1/P2 同样的 UnityLockfile 问题；kill Unity + rm Temp/UnityLockfile 后重新运行正常

---

## Phase 1: 需求挖掘 + 深度扫描 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 完成全部 Grill Me 决策（Q1~Q8）
- 对原版 7872 行代码执行三项深度扫描
- 生成并经用户确认完整功能清单（7个 Slice，60+ 条目）
- 初始化 Unity 目标项目目录结构
- 关联 GitHub 远端：https://github.com/DuranFree/FWTCG_UNITY.git

**Decisions made**:
- 目标引擎：Unity 2022.3.62f3c1
- 目标平台：PC + Mobile（横屏）
- UI 系统：uGUI（Canvas）— UI Toolkit 在 2022.3 runtime 不成熟
- 卡牌数据：ScriptableObject — 预留未来卡组构筑扩展
- 垂直切片顺序：Slice1(核心回合) → Slice2(法术) → Slice3(装备) → Slice4(传奇) → Slice5(效果全集) → Slice6(游戏流程) → Slice7(平台适配)
- 音频：占位，暂不移植
- 存档：无需（每次重新开局）
- 本地化：预留多语言接口，当前仅中文

**Technical debt**: 无

**Problems encountered**: 无

---

## Phase 2: 视觉分析 + 视觉升级指南 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 彻底读取全部 5 个 CSS 文件 + particles.js + 3d-tilt.js + ui.js + dragAnim.js
- 生成完整视觉清单（50+ 条目，用户已确认）
- 生成 visual-upgrade-FWTCG.md（14 个效果逐一分析，含 A/B/C 分类）

**Decisions made**:
- 辉光系统：URP Bloom + Emission 替代全部 CSS box-shadow（B类升级）
- 粒子系统：GPU Particle System 替代 Canvas JS 粒子（B类升级）
- 3D 倾斜：RectTransform.localRotation + DOTween 替代 CSS perspective（A类移植）
- 旋转彗星边框：Shader Graph 极坐标 shader 替代 CSS conic-gradient
- 全局后处理：Bloom + Vignette + Film Grain + Color Adjustments（URP Volume）
- 动画系统：DOTween 替代所有 CSS transition / @keyframes
- 外部依赖：需导入 DOTween（Asset Store 免费版）、Cinzel 字体（Google Fonts）

**Technical debt**: 无

**Problems encountered**: 无

---

## Phase 3: 移植计划 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 生成 port-FWTCG.md：12个移植阶段，每阶段含原版文件、移植内容、验收标准
- 粒度经用户确认：P1数据层→P2回合→P3手牌→P4战斗→P5 AI→P6法术→P7装备→P8传奇→P9效果全集→P10流程→P11视觉→P12平台

**Decisions made**:
- 12阶段垂直切片，每阶段独立可验证
- 视觉层（P11）延后到逻辑全部完成后统一实施

**Technical debt**: 无

**Problems encountered**: 注：Phase 1/2 收尾时未显式报告清单状态，已在 CLAUDE.md 第8条补丁修复

---

## P1（移植）: 卡牌数据 + 游戏状态 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 创建 Unity 项目（复用 FWTCG_UNITY 的 ProjectSettings/Packages，URP + UTF 已就绪）
- Assets/Scripts/Data/CardData.cs — ScriptableObject，全字段含未来卡组构筑扩展口
- Assets/Scripts/Data/RuneData.cs — ScriptableObject，6种符文
- Assets/Scripts/Core/CardInstance.cs — 运行时实例，等价 mk()，含 EffectiveAtk 属性
- Assets/Scripts/Core/GameState.cs — 等价 G 对象，含全部 70+ 字段
- Assets/Tests/EditMode/CardInstanceTests.cs — 16个行为验证测试
- 🟢 [逻辑测试] 全部通过 (16/16)

**Decisions made**:
- asmdef 需要 `"includePlatforms": ["Editor"]` + `"defineConstraints": ["UNITY_INCLUDE_TESTS"]` 才能被 UTF 发现（首次踩坑，已修复）
- CardInstance 纯 C# 类，不依赖 MonoBehaviour，可直接在 EditMode 测试中实例化

**Technical debt**: 无

**Problems encountered**: UTF asmdef 首次运行0个测试——缺少 includePlatforms + defineConstraints，第二次修复后全绿

---

## P2（移植）: 回合流程 + 符文/费用系统 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 engine.js（doAwaken/doSummon/doDraw/doEndPhase/addScore/checkWin）和 hint.js（符文两步确认系统）
- GameState.cs 补充：PendingRune 类、PendingRuneAction 枚举、pendingRunes 改为 List<PendingRune>、LegendInstance 增加 stunned + tb 字段
- Assets/Scripts/Core/TurnManager.cs — 回合阶段状态机：StartTurn / DoAwaken / DoStart / DoSummon / DoDraw / DoEndPhase / PlayerEndTurn / AddScore / CheckWin
- Assets/Scripts/Core/RuneController.cs — 符文两步确认系统：TapRune / TapAllRunes / RecycleRune / CancelRunes / ConfirmRunes
- Assets/Tests/EditMode/TurnManagerTests.cs — 25 个行为验证测试
- Assets/Tests/EditMode/RuneControllerTests.cs — 25 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (66/66，含 P1 的 16 个)

**Decisions made**:
- TurnManager 纯 C# 类（无 MonoBehaviour），DoEndPhase 修改 G.turn 和 G.round 后由协程调用 StartTurn（P6 UI 层实现）
- DoStart / DoDraw 的异步 prompt 部分（战场牌据守效果、先见机甲预知）留 P5 实现，P2 仅实现纯状态逻辑
- AddScore 包含第8分征服限制逻辑；缇亚娜/攀圣长阶/遗忘丰碑修正留 P5

**Technical debt**: 无

**Problems encountered**: 首次运行 -quit 标志导致 TestResults.xml 未写入，移除 -quit 后正常（重现 P1 踩坑，需记住不加 -quit）

---

## P3（移植）: 手牌 & 出牌系统 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 spell.js（canPlay / deployToBase / deployToBF / moveUnit / cleanDeadAll / onSummon / triggerDeathwish / tryDeathShield / dealDamage）
- CardInstance.cs 补充：Mk() 实例方法（等价 mk(c)）、AllocUid() 静态方法（用于运行时创建临时牌如碎片）
- Assets/Scripts/Core/CardDeployer.cs — 出牌核心逻辑：CanPlay / GetEffectiveCost / DeployToBase / DeployToBF / MoveUnit / RemoveUnitFromField / DealDamage / CleanDeadAll / TryDeathShield / OnSummon / TriggerDeathwish
- Assets/Tests/EditMode/CardDeployerTests.cs — 54 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (120/120，含 P1+P2 的 66 个)

**Decisions made**:
- OnSummon 实现全部已知入场效果框架；先见机甲（foresight_mech_enter）需要 prompt 留 P5
- DealDamage 内部调用 CleanDeadAll（与原版一致），清理后单位 HP 已重置；测试验证"单位进废牌堆"而非"HP==0"
- 急速可选付费路径（deployToBase/ToBF 的 askPrompt）通过 enterActive bool 参数预留，P6 UI 层传入

**Technical debt**: 无

**Problems encountered**: 4 个测试初版写错期望值（MakeCard helper 的 atk 固定为 2 与 cost 不同；CleanDeadAll 后 HP 被 reset）——已修正测试逻辑

---

## 2026-03-27 — P6: 法术系统

**Phase**: P6 法术系统 (SpellSystem)

**New files**:
- `Assets/Scripts/Core/SpellSystem.cs` — 完整法术系统：CanPlay / GetSpellTargets / ApplySpell（33 效果）/ 法术对决（StartSpellDuel / RunDuelTurn / SkipDuel / AiSkipDuel / EndDuel）/ HasPlayableReactionCards / GetEffectiveCost；提示委托注入（PromptTarget / PromptDiscard / PromptBattlefield / PromptEcho / OnAiDuelTurn）
- `Assets/Tests/EditMode/SpellSystemTests.cs` — 45 项行为验证测试

**Modified files**:
- `Assets/Scripts/Data/CardData.cs` — 添加 `echoManaCost` 字段
- `Assets/Scripts/Core/CardInstance.cs` — 添加 `echoManaCost` 字段（From + Mk 同步）
- `Assets/Scripts/Core/GameState.cs` — `LegendInstance` 添加 `uid` 字段（含静态计数器）
- `Assets/Scripts/Core/AIController.cs` — 注入 SetSpellSystem；实现 AiShouldPlaySpell / AiChooseSpellTarget / AiSpellPriority / AiDuelAction（5策略）/ AiCheckReactionPlay；AiAction 增加 rally_call（步骤3）/ balance_resolve（步骤4）/ 法术施放（步骤6）

**Test results**: 242/242 passed (prior 197 + 45 new P6)

**Design decisions**:
- Prompt delegates 注入替代 async/await，使 EditMode 测试可同步运行
- isEcho bool 参数防止回响无限递归
- LegendInstance.uid 新增，供 SpellSystem 中 target.uid == opLeg.uid 对比
- AiAction 递归调用 Schedule 同步时会触发完整 AI 回合（测试依此设计）

**Technical debt**: 对决后 AI 继续行动链（OnDuelEnded 回调）由 UI 层 P10 接管

**Problems encountered**:
- LegendInstance 无 uid → 添加静态 UID 计数器解决
- 旧 AIControllerTests 期望 AiDuelAction 返回 bool → 改为 void + DoesNotThrow
- deal3_twice 测试初版单位 HP=5（两次3伤死亡）→ 改 HP=8
- rally_call 测试检查 eRallyActive（DoEndPhase 重置）→ 改检查 eDiscard + unit.exhausted
- balance_resolve 测试绘制的单位 cost=1 被后续 AiAction 递归部署 → 改 cost=4

---

## 2026-03-27 — P7: 装备系统

**Phase**: P7 装备系统 (Equipment System)

**New files**:
- `Assets/Tests/EditMode/EquipmentSystemTests.cs` — 21 项行为验证测试

**Modified files**:
- `Assets/Scripts/Data/CardData.cs` — 添加 `atkBonus` 字段（装配战力加成）
- `Assets/Scripts/Core/CardInstance.cs` — 添加 `atkBonus` 字段（From + Mk 同步）
- `Assets/Scripts/Core/GameState.cs` — 添加 `LastDeployedUid` 字段（追踪最近部署单位 uid）
- `Assets/Scripts/Core/CardDeployer.cs` — TryDeathShield 扩展（优先检查 guardian_equip 附着）；新增 AttachEquipToUnit 方法；DeployToBase/ToBF 设置 LastDeployedUid
- `Assets/Scripts/Core/SpellSystem.cs` — trinity/guardian/dorans/death_shield case 实现；新增 ActivateEquipAbility 方法
- `Assets/Scripts/Core/AIController.cs` — AiAction 步骤5a：AI 部署装备并立即附着到最高战力单位

**Test results**: 263/263 passed (prior 242 + 21 new P7)

**Design decisions**:
- guardian_equip 检查优先于 death_shield（守护天使先于中娅触发）
- 装备 ApplySpell 通过 PromptTarget 委托让玩家选择目标（null=跳过，留在基地）
- AI 装备通过 PromptTarget=OrderByDescending(EffAtk) 选最强单位

**Technical debt**: P7 UI 层需实现"optional target"提示弹窗

**Problems encountered**:
- 测试断言 `IsFalse(pDiscard.Contains(unit))` 错误（死亡单位确实进废牌堆）→ 改为 IsTrue
