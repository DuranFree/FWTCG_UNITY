# 功能清单 — FWTCG Unity 移植
> 生成于 Phase 1（2026-03-27）。完成项标记 ✅，不删除任何条目。

---

## Slice 1 — 核心回合循环
- ✅ 卡牌数据（CardData ScriptableObject：id、name、cost、atk、keywords、effect 等全字段）
- ✅ 6种符文数据（RuneData SO：blazing/radiant/verdant/crushing/chaos/order）
- ✅ 游戏状态单例（GameState：等价 G 对象，含所有字段）
- ✅ 回合阶段状态机（awaken → start → summon → draw → action → end）
- ✅ 摸牌系统（draw phase：从主牌堆抽1张到手牌；燃尽惩罚；洗牌）
- ✅ 手牌展示（最多7张）— P13 GameUI.cs PlayerHandPanel，可点击选中
- ✅ 符文区展示（玩家/AI 各自的已抽符文列表）— P13 GameUI.cs PlayerRunePanel，含横置/回收按钮
- ✅ 点击符文 → 获得法力（tap rune：tapped=true，pMana+1）
- ✅ 一键全点符文
- ✅ 符文回收（recycle rune → 符能+1，符文从场上移除回符文堆）
- ✅ 随从牌出牌到基地（deployToBase：消耗法力，进行 onSummon）
- ✅ 随从牌出牌到战场（deployToBF：消耗法力，exhausted=true 除非急速/号令）
- ✅ 单位从基地移动到战场（moveUnit，含装备强制回基地）
- ✅ 基础战斗结算（triggerCombat：roleAtk/强攻/坚守/壁垒/压制，低atk单位死亡）
- ✅ 单位死亡清理（cleanDeadAll，含绝念触发、中娅沙漏、附着装备处理）
- ✅ 积分系统（pScore/eScore，据守/征服得分，WIN_SCORE=8，第8分限制，燃尽）
- ✅ 战场控制权（ctrl：player/enemy/null，征服条件，进攻方撤退）
- ✅ 基础 AI 回合（点符文→出随从→移动→结束）
- ✅ 结束回合（playerEndTurn，doEndPhase）

---

## Slice 2 — 法术系统
- ✅ 法术牌出牌（applySpell 分发，含费用扣除）
- ✅ 法术目标选取（getSpellTargets：有目标/无目标/无可选目标三种状态）
- ✅ 法术对决系统（startSpellDuel → runDuelTurn → endDuel，2次跳过结束）
- ✅ 反应窗口（hasPlayableReactionCards，迅捷/反应时机校验）
- ✅ 迅捷/反应 关键词校验（canPlay 中的时机检查）
- ✅ 回响关键词（applySpell 末尾二次施法，echoManaCost 扣除）
- ✅ AI 法术决策（aiShouldPlaySpell + aiChooseSpellTarget + aiSpellPriority）
- ✅ AI 对决响应（aiDuelAction：反制>眩晕>增益>其他迅捷>跳过）
- ✅ 所有33个法术 effect 实现（buff_ally / stun / weaken / deal3 / debuff4 / debuff1_draw / recall_draw / buff_draw / recall_unit_rune / deal3_twice / deal6_two / deal1_repeat / deal4_draw / thunder_gal_manual / buff7_manual / buff5_manual / stun_manual / buff2_draw / buff1_solo / force_move / ready_unit / discard_deal / deal2_two / deal1_same_zone / draw1 / draw4 / summon_rune1 / rune_draw / akasi_storm / counter_cost4 / counter_any / rally_call / balance_resolve）

---

## Slice 3 — 装备系统
- ✅ 装备牌部署到基地（DeployToBase，type=Equipment，LastDeployedUid 追踪）
- ✅ 装备装配到单位（AttachEquipToUnit，ApplySpell 内 PromptTarget 选择目标）
- ✅ 装备随单位死亡进废牌堆（CleanBFSide/CleanBaseSide 已处理 attachedEquipments）
- ✅ 装备技能激活（ActivateEquipAbility，校验 equipSchCost）
- ✅ 三相之力效果（trinityEquipped 标记，+2战力）
- ✅ 守护天使效果（guardian_equip 死亡护盾，TryDeathShield 优先检查附着装备）
- ✅ 多兰之刃效果（dorans_equip，+2战力）
- ✅ 中娅沙漏效果（death_shield，基地装备触发 TryDeathShield）
- ✅ AI 装备部署（AiAction 步骤5a，优先部署+附着到最高战力单位）

---

## Slice 4 — 传奇系统（P8 ✅ 已完成，263 tests）
- ✅ 传奇牌独立 HP 系统（pLeg.currentHp / maxHp，与普通单位隔离）
- ✅ 传奇被动检查（checkLegendPassives）
- ✅ 传奇触发事件系统（triggerLegendEvent + 事件名匹配）
- ✅ 传奇主动技能激活（activateLegendAbility，含费用检查）
- ✅ 卡莎：进化被动（4关键词升级，+3/+3，仅一次）
- ✅ 卡莎：虚空感知主动技（消耗自身，给法术+1炽烈符能）
- ✅ 易大师：独影剑鸣被动（1名盟友独自防守时+2 ATK）
- ✅ AI 传奇行动（aiLegendActionPhase + aiLegendDuelAction）

---

## Slice 5 — 卡牌效果全集（P9 ✅ 已完成，330 tests）

### 入场效果（onSummon）
- ✅ 约德尔教官：入场摸1牌（`yordel_instructor_enter`）
- ✅ 德莱厄斯：本回合≥2张出牌时+2/+2并回刷（`darius_second_card`，cardsPlayedThisTurn>1）
- ✅ 熔岩巨兽：每名坚守盟友+1 ATK（`malph_enter`，统计 baseZone 中含"坚守"单位数）
- ✅ 贾克斯：手牌装备获得【反应】（`jax_enter`，遍历 hand 添加关键词）
- ✅ 缇亚娜·冕卫：对手本回合不得积据守分（BattlefieldSystem.IsTiyanaBlockingHold，AddScore 调用）
- ✅ 先见机甲：预看牌堆顶，可选择回收至库底（CardDeployer.PromptForesight delegate，P10）

### 绝念效果（triggerDeathwish）
- ✅ 虚空碎片：死亡时手牌生成「碎片」法术（`voidling`，手牌<7时）
- ✅ 虚空哨兵：下一名盟友+1/+1（`void_sentinel`，pNextAllyBuff）
- ✅ 警觉哨兵：摸1牌（`alert_sentinel`，牌堆>0时）
- ✅ 嚎叫波洛：仅该区无其他盟友时摸1牌（`wailing_poro`，CountAlliesExcluding==0）

### 战场牌效果（16张）
- ✅ altar_unity — 据守召唤新兵（BattlefieldSystem.OnHold）
- ✅ aspirant_climb — 据守支付1法力强化基地单位（BattlefieldSystem.OnHold）
- ✅ back_alley_bar — 单位离开时+1 tb.atk（BattlefieldSystem.OnUnitLeaveBF）
- ✅ ascending_stairs — 据守/征服额外+1分（BattlefieldSystem.ModifyAddScore）
- ✅ bandle_tree — 据守：≥3地域+1法力（BattlefieldSystem.OnHold）
- ✅ dreaming_tree — 法术目标为此处盟友→抽1牌（BattlefieldSystem.OnSpellTargetAlly）
- ✅ forgotten_monument — 第3回合前阻断据守分（BattlefieldSystem.ModifyAddScore）
- ✅ reckoner_arena — 战斗开始atk≥5获强攻/坚守（BattlefieldSystem.OnCombatStart）
- ✅ reaver_row — 征服：召回废牌≤2费单位（BattlefieldSystem.OnConquer）
- ✅ rockfall_path — 禁止手牌直接出牌到此（BattlefieldSystem.CanDeployToBF）
- ✅ star_peak — 据守：召出休眠符文+1法力（BattlefieldSystem.OnHold）
- ✅ strength_obelisk — 据守/征服各+1符文（BattlefieldSystem.OnHold/OnConquer）
- ✅ sunken_temple — 防守失败支付2法力抽1牌（BattlefieldSystem.OnDefenseFailure）
- ✅ trifarian_warcamp — 进入时获得 buffToken（BattlefieldSystem.OnUnitEnterBF）
- ✅ void_gate — 法术/技能伤害+1（BattlefieldSystem.ModifySpellDamage）
- ✅ zaun_undercity — 征服：弃1手牌抽1牌（BattlefieldSystem.OnConquer）
- ✅ vile_throat_nest — 禁止单位移回基地（BattlefieldSystem.CanMoveToBase）
- ✅ hirana — 征服：消耗增益抽1牌（BattlefieldSystem.OnConquer）
- ✅ thunder_rune — 征服：回收1枚符文（BattlefieldSystem.OnConquer）

---

## Slice 6 — 游戏流程 & Meta（P10 ✅ 逻辑层，P11 ✅ GameManager 桥接，384 tests）
- [ ] 标题界面（开始按钮）— UI Phase
- ✅ 硬币决定先后手（逻辑：GameInitializer.CoinFlip）
- ✅ 战场选择（逻辑：GameInitializer.SelectBattlefields，从各方战场牌池随机抽1张）
- ✅ 先手调整（逻辑：GameInitializer.ConfirmMulligan，最多换2张）
- [ ] 积分轨道展示（0-8格，动画）— UI Phase
- ✅ 战斗日志（滚动面板）— P13 GameUI.cs 右侧 LogPanel，AppendLog 追加
- ✅ 游戏结束界面（胜/败）— P13 GameUI.cs GameOverPanel + 再来一局按钮
- [ ] 卡牌详情预览（点击查看完整文字）— UI Phase
- [ ] 弃牌堆/放逐堆查看器— UI Phase
- ✅ 30秒回合计时器（逻辑：TurnTimerSystem.Reset/Start/Stop/Tick/OnTimeout）
- ✅ 本地化字符串表（LocalizationTable，中文全套，Format 支持占位符）

---

## Slice 7 — 输入 & 平台适配
- ✅ 点击出牌（PC/移动统一入口）— P13 GameUI.cs 两次点击流程（选牌→点区域）
- [ ] 拖拽出牌（PC 优先）
- ✅ 触屏适配（uGUI EventSystem）— P13 GameUI.EnsureEventSystem 自建 EventSystem+StandaloneInputModule
- [ ] 横屏响应式布局（PC + 移动横屏）
- [ ] 缩放锁定（移动端防误触）

---

## 硬编码数值（深度扫描发现）
- ✅ WIN_SCORE = 8（GameState.const，TurnManager.AddScore/CheckWin 全部引用）
- ✅ 手牌上限 = 7（GameState.MAX_HAND，所有摸牌/绝念/法术效果均守卫）
- ✅ 战场数量 = 2，每侧最多 2 个单位（bf[2] + MAX_BF_UNITS，AIController 引用）
- ✅ 符文牌堆：卡莎 炽烈×7+灵光×5，易 翠意×6+摧破×6（GameInitializer.KaisaRuneTypes/MasterYiRuneTypes，P10）
- [ ] 传奇初始HP：卡莎14，易12；初始ATK=5，费用=5（CardData ScriptableObject 尚未配置，UI Phase）
- ✅ 对决跳过阈值 = 2次（SpellSystem duelSkips >= 2，两处均守卫）
- ✅ 回合计时器 = 30秒（GameState.TIMER_SECONDS，TurnTimerSystem，P10）
- ✅ 每副主牌堆 = 40张，符文堆 = 12张（GameInitializer.SetupDecks DeckConfig 约束，P10）
- ✅ 卡莎进化：需4种不同关键词，加成+3/+3（LegendSystem.EffectEvolve，P8）
- ✅ 易大师防守触发：1名盟友，加成+2 ATK（当回合）（LegendSystem.EffectMasteryiDefendBuff，P8）
- ✅ 德莱厄斯触发：本回合出牌≥2（CardDeployer.OnSummon darius_second_card，cardsPlayedThisTurn>1）
- ✅ 千尾监视者：全体敌方-3 ATK（当回合）（CardDeployer.OnSummon thousand_tail_enter，tb.atk-=3）
- ✅ atk(u) 最小值 = 1（CombatResolver.EffAtk = Math.Max(1, currentAtk + tb.atk)）
- ✅ AI行动间隔 = 700ms（AIController Schedule(0.7f, ...)；Toast 属 UI 层，P10）

---

## 跨文件交互流程链
- ✅ 出随从完整链：点击手牌→ToggleCard→点区域→PlayCard→DeployToBase/BF→Mk()→OnSummon→exhausted→cleanDeadAll→OnStateChanged→Refresh()
- ✅ 法术施法链：点击手牌→ToggleCard→点区域→PlayCard→ApplySpell→效果→cleanDeadAll→OnStateChanged→Refresh()
- [ ] 法术对决链：DuelPanel可见→SkipBtn→DuelSkip()（完整链待测试）
- ✅ 战斗结算链：AI→TriggerCombat→RoleAtk→DealDamage→CleanDeadAll→Deathwish→AddScore→CheckWin→OnStateChanged→Refresh()
- ✅ 符文回收链：点击回收→RecycleRune→AddSch→从pRunes移除→OnStateChanged→Refresh()
- [ ] 传奇受伤/死亡链：dealDamage(isLegend=true)→pLeg.currentHp-=dmg→checkWin→endGame
