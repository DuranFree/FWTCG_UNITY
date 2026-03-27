# Port Plan: FWTCG
> Original codebase: E:\claudeCode\FWTCG_V3d_V9
> Target framework: Unity 2022.3.62f3c1 — URP + uGUI
> Asset path: E:\claudeCode\FWTCG_V3d_V9\tempPic
> Visual Upgrade Guide: ./plans/visual-upgrade-FWTCG.md

---

## Architectural Decisions
- **Intentional behavior changes**: none — pure 1:1 port
- **Known bugs to fix**: none
- **Asset strategy**: direct copy（卡牌图片直接 import，符文/字体需补充）
- **Visual strategy**: rebuilt using native platform capabilities per Visual Upgrade Guide
- **Card data**: ScriptableObject（预留卡组构筑扩展字段）
- **UI system**: uGUI Canvas（Screen Space - Overlay）
- **Animation**: DOTween（替代所有 CSS transition / @keyframes）
- **Localization**: LocalizationKey string 查表，当前仅中文
- **Audio**: 占位，暂不实现

---

## Phase 1: 卡牌数据 + 游戏状态
**Original files**: cards.js, engine.js（G 对象字段）
**What to port**:
- CardData ScriptableObject（id, name, region, type, cost, atk, hp, keywords[], text, emoji, effect, schCost, schType, schCost2, schType2, lore, img，预留 cardPool 字段）
- RuneData ScriptableObject（type, displayName, emoji, colors）
- GameState 单例（等价 G 对象，含全部 70+ 字段）
- `mk(card)` 等价：CardInstance 运行时类（uid, currentHp, currentAtk, exhausted, stunned, tb, buffToken, attachedEquipments）
- `atk(unit)` 等价：`CardInstance.EffectiveAtk` 属性（`Mathf.Max(1, currentAtk + tb.atk)`）
- 两套完整卡牌数据（卡莎40张+传奇，易大师40张+传奇，16张战场牌）
- 六种 RuneData 资产

**Visual handling**: 无 UI，纯数据层
**Acceptance criteria**:
- [ ] CardData SO 字段与 cards.js 原版一一对应，无缺失
- [ ] GameState 所有字段存在且类型正确
- [ ] CardInstance.EffectiveAtk = max(1, currentAtk + tb.atk)
- [ ] 80张卡牌 + 2张传奇 + 16张战场牌 SO 资产全部创建完毕
- [ ] 6种 RuneData SO 资产创建完毕
- [ ] 🟢 逻辑测试：CardInstance 数值字段赋值/读取正确

---

## Phase 2: 回合流程 + 符文/费用系统
**Original files**: engine.js（startTurn / runPhase / doAwaken / doStart / doSummon / doDraw / doEndPhase），hint.js
**What to port**:
- 回合阶段状态机（awaken → start → summon → draw → action → end）
- 符文抽取（doSummon：从 pRuneDeck 取符文）
- 摸牌（doDraw：从 pDeck 取1张到 pHand，上限7）
- 符文点击获得法力（tapRune：tapped=true，pMana+1）
- 一键全点符文（tapAllRunes）
- 符文回收（recycleRune：addSch +1，移除符文）
- 阶段推进（playerEndTurn → doEndPhase → startTurn('enemy')）
- 符能系统（getSch / addSch / spendSch / resetSch）

**Visual handling**: 占位 Debug.Log，无真实 UI
**Acceptance criteria**:
- [ ] 阶段按 awaken→start→summon→draw→action→end 顺序推进，不可逆
- [ ] tapRune：pMana 正确+1，rune.tapped = true
- [ ] tapAllRunes：批量执行 tapRune
- [ ] recycleRune：pSch[type]+1，符文从 pRunes 移除，放回 pRuneDeck 尾部
- [ ] doDraw：手牌<7时摸1张，=7时不摸
- [ ] doEndPhase：round++ / turn 切换 / cardsPlayedThisTurn 归零
- [ ] 🟢 逻辑测试：完整跑一个空回合（无出牌），所有状态字段值正确

---

## Phase 3: 手牌系统 + 出牌到基地
**Original files**: spell.js（canPlay / handleCardClick / deployToBase / onSummon / executeDrop），ui.js（手牌渲染）
**What to port**:
- canPlay 检查（法力 / 符能 / 时机 / 手牌状态）
- 手牌点击选牌 → 显示有效放置区
- deployToBase：扣费 → mk() → onSummon → exhausted 设置（急速例外）
- onSummon 分发（约德尔教官 / 德莱厄斯 / 熔岩巨兽 / 贾克斯 / 缇亚娜 / 先见机甲）
- cleanDeadAll（含装备守卫：type=equipment 不触发死亡）
- dealDamage 基础路径（currentHp -= dmg，触发 cleanDeadAll）
- 手牌 UI（卡牌排列，可打/不可打高亮，点击交互）

**Visual handling**: 手牌区基础布局 + 可打出绿色高亮（旋转彗星 Shader 可后补）
**Acceptance criteria**:
- [ ] 法力不足时 canPlay 返回 false，卡牌显示不可打状态
- [ ] 符能不足时同上
- [ ] deployToBase：pMana 正确扣减，CardInstance 进入 pBase
- [ ] 急速单位 exhausted = false，普通单位 exhausted = true
- [ ] onSummon 各效果触发正确（约德尔教官入场摸1牌等）
- [ ] cleanDeadAll：currentHp≤0 的单位被移除，装备不被误判死亡
- [ ] 手牌 UI 渲染：卡牌数量、顺序、可打高亮状态正确
- [ ] 🟢 逻辑测试：出随从完整流程（费用→部署→onSummon→手牌更新）

---

## Phase 4: 战场系统 + 战斗结算
**Original files**: combat.js（triggerCombat / moveUnit / roleAtk），engine.js（addScore / checkWin / triggerDeathwish）
**What to port**:
- moveUnit：基地→战场，exhausted 状态保持，每侧上限2
- triggerCombat：roleAtk 计算（含强攻/坚守），dealDamage 双向，cleanDeadAll
- triggerDeathwish（虚空碎片/虚空哨兵/警觉哨兵/嚎叫波洛）
- addScore：据守/征服积分，bfScoredThisTurn 防重复
- checkWin：pScore/eScore >= WIN_SCORE(8) → endGame
- 战场 UI（两条战场，单位槽，控制权标记，积分轨道 0-8）

**Visual handling**: 战场布局基础 UI，积分轨道显示，战斗结算占位动画
**Acceptance criteria**:
- [ ] 单位只能移动到未满（<2）的战场侧
- [ ] roleAtk：强攻单位用 strongAtkBonus，坚守单位+1
- [ ] 双向伤害正确：低atk单位 currentHp≤0 死亡
- [ ] 战斗后 currentHp 重置为 currentAtk（非 hp 字段）
- [ ] 4种绝念效果各自在正确条件下触发
- [ ] 据守/征服积分逻辑正确，同一战场同一回合不重复计分
- [ ] pScore/eScore 达到8时 endGame 触发
- [ ] 战场 UI：单位位置、控制权标记、积分轨道更新正确
- [ ] 🟢 逻辑测试：一次完整战斗（双方各1单位对打，死亡清理，积分）

---

## Phase 5: 基础 AI
**Original files**: ai.js（aiAction / aiShouldPlaySpell / aiChooseSpellTarget / aiDecideMovement / aiBoardScore）
**What to port**:
- aiAction 主循环（点符文→出随从→出法术→移动→结束）
- aiCardValue 启发式评分
- aiBoardScore 局面评分
- aiDecideMovement（选单位+目标战场）
- aiDuelAction（对决响应）
- aiLegendActionPhase（传奇技能决策）
- AI 行动间隔 700ms（coroutine）

**Visual handling**: AI 行动对应 UI 更新（与玩家侧共用）
**Acceptance criteria**:
- [ ] AI 每回合能完成：全点符文 → 选牌出牌 → 移动单位 → 结束回合
- [ ] AI 不出超出法力的牌
- [ ] AI 移动优先进攻己方劣势战场
- [ ] AI 行动间每步有 700ms 延迟（不同步卡顿）
- [ ] 🟢 逻辑测试：跑10个 AI 回合，无报错，pMana 不出现负值

---

## Phase 6: 法术系统
**Original files**: spell.js（applySpell / getSpellTargets / startSpellDuel / runDuelTurn / endDuel），engine.js
**What to port**:
- getSpellTargets（3种返回：有目标uid[] / null无需目标 / []无有效目标）
- applySpell 分发（33个 effect，全部实现）
- 法术对决流程（startSpellDuel → runDuelTurn → 2次skip → endDuel）
- 反应窗口（playerRequestInterrupt，30秒，hasPlayableReactionCards）
- 回响关键词（applySpell 末尾二次施法）
- 迅捷/反应时机校验

**Visual handling**: 法术目标高亮，对决 UI banner，反应窗口按钮
**Acceptance criteria**:
- [ ] 33个 spell effect 各自行为与原版一致（逐一核对 cards.js text 字段）
- [ ] 对决：2次skip后 endDuel 触发，若有敌则 triggerCombat
- [ ] 反应窗口：30秒超时自动关闭，无反应牌时按钮封印
- [ ] 回响：法术施法后自动触发第二次（isEcho 标记防无限循环）
- [ ] 🟢 逻辑测试：抽取典型法术各类效果（伤害/召回/buff/反制）验证

---

## Phase 7: 装备系统
**Original files**: spell.js（deployEquipAttach / activateEquipAbility / tryDeathShield）
**What to port**:
- 装备部署到基地（不进战场）
- 装备装配到单位（含符能费用）
- 装备随单位移动/死亡（附着关系维护）
- activateEquipAbility（三相之力/天使圣甲/多兰之刃/中亚苦痛）
- tryDeathShield（天使圣甲死亡护盾）
- cleanDeadAll 装备守卫（type=equipment 不误判死亡）

**Visual handling**: 装备附着指示，装备槽 UI
**Acceptance criteria**:
- [ ] 装备只能部署到基地，不能直接上战场
- [ ] 装配扣除正确符能
- [ ] 单位死亡时附着装备一并移除
- [ ] tryDeathShield：死亡时有天使圣甲则救活一次，装备消耗
- [ ] cleanDeadAll 不误杀装备（currentHp=0 的装备不被视为死亡）
- [ ] 🟢 逻辑测试：装备完整流程（部署→装配→激活→随单位死亡）

---

## Phase 8: 传奇系统
**Original files**: legend.js, cards.js（KAISA_LEGEND / MASTERYI_LEGEND）
**What to port**:
- 传奇独立 HP 系统（currentHp 独立，不走 mk() 路径）
- checkLegendPassives（每回合被动检查）
- triggerLegendEvent（事件名精确匹配）
- activateLegendAbility（主动技能，含费用/exhausted检查）
- 卡莎：进化被动（4种关键词，+3/+3，仅触发一次，_evolved 标记）
- 卡莎：虚空感知主动（exhaust → 给法术+1炽烈符能）
- 易大师：独影剑鸣被动（1名盟友防守时+2 ATK）
- resetLegendAbilitiesForTurn（每回合重置）

**Visual handling**: 传奇槽 UI，HP 条，技能按钮，进化视觉变化
**Acceptance criteria**:
- [ ] 传奇受伤操作 currentHp，不操作 hp 字段
- [ ] 传奇 currentHp≤0 → endGame，不走 cleanDeadAll
- [ ] 卡莎进化：盟友集满4种关键词后+3/+3，_evolved=true后不再触发
- [ ] 易大师：防守时恰好1名盟友，该单位当回合+2 ATK（tb.atk）
- [ ] activateLegendAbility：exhausted=true 后本回合不可再用（若 once=true）
- [ ] 🟢 逻辑测试：卡莎进化条件触发，易大师防守加成正确计算

---

## Phase 9: 卡牌效果全集
**Original files**: spell.js（onSummon / triggerDeathwish 补全），engine.js，cards.js（16张战场牌）
**What to port**:
- onSummon 6个效果（约德尔/德莱厄斯/熔岩巨兽/贾克斯/缇亚娜/先见机甲）
- triggerDeathwish 4个效果（虚空碎片/虚空哨兵/警觉哨兵/嚎叫波洛）
- 16张战场牌效果（每张独立触发逻辑）
- pNextAllyBuff / eNextAllyBuff 等追踪字段联动

**Visual handling**: 效果触发时 Toast / log 提示
**Acceptance criteria**:
- [ ] 16张战场牌效果与 cards.js text 字段描述一致（逐一对照）
- [ ] onSummon 6个效果各自触发条件/结果正确
- [ ] deathwish 4个效果条件守卫正确（手牌上限/区域空检查等）
- [ ] 🟢 逻辑测试：每张战场牌跑触发场景，逐一验证

---

## Phase 10: 游戏流程
**Original files**: main.js（startGame / showCoinFlip / showBFSelect / showMulligan / confirmMulligan / initGame）
**What to port**:
- 标题界面（开始按钮）
- 翻币决定先后手（动画 + G.first 赋值）
- 战场选择（玩家从3张中选1张，G.bf 初始化）
- Mulligan（最多换2张，seedPlayerOpeningHand）
- initGame（G 对象完整初始化，双方牌组洗牌）
- 游戏结束界面（胜/败，endGame）

**Visual handling**: 标题/翻币/选战场/Mulligan/结束 各界面 UI，多层入场动画
**Acceptance criteria**:
- [ ] 翻币结果正确赋值 G.first，先手方先开始
- [ ] 战场选择后 bf[] 正确初始化，战场牌效果绑定
- [ ] Mulligan：最多换2张，换掉的牌洗回牌堆，重新抽取
- [ ] initGame：G 全字段正确归零/初始化，牌堆随机洗牌
- [ ] endGame：显示胜负结果，可重新开始
- [ ] 🟢 逻辑测试：initGame 后 G 各字段值符合初始状态预期

---

## Phase 11: 视觉特效
**Original files**: visual-upgrade-FWTCG.md（Visual Upgrade Guide）
**What to port**:
- URP Post Processing Volume（Bloom / Vignette / Film Grain / Color Adjustments）
- 卡牌 Emission Material + 可打出旋转彗星 Shader
- 伤害数字对象池（WorldSpace Canvas + DOTween）
- Toast 通知系统（VerticalLayoutGroup + DOTween）
- 拖拽漩涡粒子（预制 Particle System × 3 环）
- 积分轨道得分脉冲（DOTween Emission Color）
- 战场控制光晕（Player/Enemy 颜色 DOTween loop）
- 手牌入场动画（DOTween sequence）
- 符文入场错落动画（50ms stagger）
- 背景六边形纹理 + 径向环境光
- 背景粒子三层（主粒子/符文/萤火虫）
- 战斗冲击波 + 落地涟漪
- 翻币旋转动画
- 标题多层入场序列

**Visual handling**: 本 Phase 纯视觉
**Acceptance criteria**:
- [ ] URP Bloom 对所有 Emission 材质生效，无过爆
- [ ] 旋转彗星 Shader 在可打出牌上正常循环
- [ ] 伤害数字出现/消失流畅，无 GC 峰值
- [ ] Toast 堆叠/淡出无穿插异常
- [ ] 所有 DOTween 动画时序与原版 CSS timing 对应
- [ ] 🔵 引擎测试：在目标设备（PC + 模拟移动）跑完整一局，帧率≥60

---

## Phase 12: 平台适配
**Original files**: zoomLock.js，ui.js（响应式逻辑）
**What to port**:
- 点击出牌（PC鼠标 + 触屏统一入口，EventSystem）
- 拖拽出牌（PointerDrag 接口，PC 优先）
- 横屏布局锁定（Screen.orientation = Landscape）
- 1280×720 Canvas Scaler（Scale With Screen Size，参考分辨率 1280×720）
- 触屏防误触缩放（禁用多点触控缩放）
- Safe Area 适配（刘海屏边距）

**Visual handling**: 响应式 Canvas 布局
**Acceptance criteria**:
- [ ] PC 鼠标点击/拖拽出牌正常
- [ ] 移动端触屏点击/拖拽出牌正常
- [ ] 横屏强制，竖屏时提示旋转
- [ ] 1280×720 内容在不同屏幕比例下居中+黑边，不拉伸
- [ ] 无多点触控缩放误触
- [ ] 🔵 引擎测试：在 iOS / Android 模拟器跑完整一局
