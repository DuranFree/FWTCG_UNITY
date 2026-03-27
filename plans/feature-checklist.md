# 功能清单 — FWTCG Unity 移植
> 生成于 Phase 1（2026-03-27）。完成项标记 ✅，不删除任何条目。

---

## Slice 1 — 核心回合循环
- ✅ 卡牌数据（CardData ScriptableObject：id、name、cost、atk、keywords、effect 等全字段）
- ✅ 6种符文数据（RuneData SO：blazing/radiant/verdant/crushing/chaos/order）
- ✅ 游戏状态单例（GameState：等价 G 对象，含所有字段）
- ✅ 回合阶段状态机（awaken → start → summon → draw → action → end）
- ✅ 摸牌系统（draw phase：从主牌堆抽1张到手牌；燃尽惩罚；洗牌）
- [ ] 手牌展示（最多7张）
- [ ] 符文区展示（玩家/AI 各自的已抽符文列表）
- ✅ 点击符文 → 获得法力（tap rune：tapped=true，pMana+1）
- ✅ 一键全点符文
- ✅ 符文回收（recycle rune → 符能+1，符文从场上移除回符文堆）
- ✅ 随从牌出牌到基地（deployToBase：消耗法力，进行 onSummon）
- ✅ 随从牌出牌到战场（deployToBF：消耗法力，exhausted=true 除非急速/号令）
- ✅ 单位从基地移动到战场（moveUnit，含装备强制回基地）
- [ ] 基础战斗结算（triggerCombat：atk vs atk，低atk单位死亡）
- ✅ 单位死亡清理（cleanDeadAll，含绝念触发、中娅沙漏、附着装备处理）
- ✅ 积分系统（pScore/eScore，据守/征服得分，WIN_SCORE=8，第8分限制，燃尽）
- [ ] 战场控制权（ctrl：player/enemy/null，征服条件）
- [ ] 基础 AI 回合（点符文→出随从→移动→结束）
- ✅ 结束回合（playerEndTurn，doEndPhase）

---

## Slice 2 — 法术系统
- [ ] 法术牌出牌（applySpell 分发，含费用扣除）
- [ ] 法术目标选取（getSpellTargets：有目标/无目标/无可选目标三种状态）
- [ ] 法术对决系统（startSpellDuel → runDuelTurn → endDuel，2次跳过结束）
- [ ] 反应窗口（playerRequestInterrupt，hasPlayableReactionCards，30秒）
- [ ] 迅捷/反应 关键词校验（canPlay 中的时机检查）
- [ ] 回响关键词（applySpell 末尾二次施法）
- [ ] AI 法术决策（aiShouldPlaySpell + aiChooseSpellTarget + aiSpellPriority）
- [ ] AI 对决响应（aiDuelAction）
- [ ] 所有33个法术 effect 实现（buff_ally / stun / weaken / deal3 / debuff4 / debuff1_draw / recall_draw / buff_draw / recall_unit_rune / deal3_twice / deal6_two / deal1_repeat / deal4_draw / thunder_gal_manual / buff7_manual / buff5_manual / stun_manual / buff2_draw / buff1_solo / force_move / ready_unit / discard_deal / deal2_two / deal1_same_zone / draw1 / draw4 / summon_rune1 / rune_draw / akasi_storm / counter_cost4 / counter_any / rally_call / balance_resolve）

---

## Slice 3 — 装备系统
- [ ] 装备牌部署到基地
- [ ] 装备装配到单位（deployEquipAttach，含符能费用）
- [ ] 装备随单位移动/死亡
- [ ] 装备技能激活（activateEquipAbility）
- [ ] 三相之力效果（trinityEquipped 标记）
- [ ] 天使圣甲效果（死亡护盾）
- [ ] 多兰之刃效果
- [ ] 中亚苦痛效果（Zhonya）

---

## Slice 4 — 传奇系统
- [ ] 传奇牌独立 HP 系统（pLeg.currentHp，与普通单位隔离）
- [ ] 传奇被动检查（checkLegendPassives）
- [ ] 传奇触发事件系统（triggerLegendEvent + 事件名匹配）
- [ ] 传奇主动技能激活（activateLegendAbility，含费用检查）
- [ ] 卡莎：进化被动（4关键词升级，+3/+3，仅一次）
- [ ] 卡莎：虚空感知主动技（消耗自身，给法术+1炽烈符能）
- [ ] 易大师：独影剑鸣被动（1名盟友独自防守时+2 ATK）
- [ ] AI 传奇行动（aiLegendActionPhase + aiLegendDuelAction）

---

## Slice 5 — 卡牌效果全集

### 入场效果（onSummon）
- [ ] 约德尔教官：入场摸1牌
- [ ] 德莱厄斯：本回合≥2张出牌时+2/+2并回刷
- [ ] 熔岩巨兽：每名坚守盟友+1 ATK
- [ ] 贾克斯：手牌装备获得【反应】
- [ ] 缇亚娜·冕卫：对手本回合不得积据守分
- [ ] 先见机甲：预看牌堆顶，可选择回收

### 绝念效果（triggerDeathwish）
- [ ] 虚空碎片：死亡时手牌生成「碎片」法术（手牌<7时）
- [ ] 虚空哨兵：下一名盟友+1/+1
- [ ] 警觉哨兵：摸1牌（牌堆>0时）
- [ ] 嚎叫波洛：仅该区无其他盟友时摸1牌

### 战场牌效果（16张）
- [ ] altar_unity — 统一祭坛
- [ ] aspirant_climb — 征战阶梯
- [ ] back_alley_bar — 暗巷酒吧
- [ ] ascending_stairs — 晋升台阶
- [ ] bandle_tree — 班德尔树
- [ ] dreaming_tree — 梦境古树
- [ ] forgotten_monument — 遗忘丰碑
- [ ] reckoner_arena — 决斗竞技场
- [ ] reaver_row — 掠夺者街
- [ ] rockfall_path — 岩崩小路
- [ ] star_peak — 星顶
- [ ] strength_obelisk — 力量方尖碑
- [ ] sunken_temple — 沉没神庙
- [ ] trifarian_warcamp — 三法里战营
- [ ] void_gate — 虚空通道
- [ ] zaun_undercity — 赞恩地下城

---

## Slice 6 — 游戏流程 & Meta
- [ ] 标题界面（开始按钮）
- [ ] 硬币决定先后手（动画 + 结果）
- [ ] 战场选择（玩家从3张中选1张）
- [ ] 先手调整（Mulligan：最多换2张）
- [ ] 积分轨道展示（0-8格，动画）
- [ ] 战斗日志（可展开/折叠）
- [ ] 游戏结束界面（胜/败）
- [ ] 卡牌详情预览（点击查看完整文字）
- [ ] 弃牌堆/放逐堆查看器
- [ ] 30秒回合计时器（倒计时 + 超时自动结束）
- [ ] 本地化字符串表（现阶段仅中文，预留多语言接口）

---

## Slice 7 — 输入 & 平台适配
- [ ] 点击出牌（PC/移动统一入口）
- [ ] 拖拽出牌（PC 优先）
- [ ] 触屏适配（uGUI EventSystem）
- [ ] 横屏响应式布局（PC + 移动横屏）
- [ ] 缩放锁定（移动端防误触）

---

## 硬编码数值（深度扫描发现）
- [ ] WIN_SCORE = 8
- [ ] 手牌上限 = 7
- [ ] 战场数量 = 2，每侧最多 2 个单位
- [ ] 符文牌堆：卡莎 炽烈×7+灵光×5，易 翠意×6+摧破×6
- [ ] 传奇初始HP：卡莎14，易12；初始ATK=5，费用=5
- [ ] 对决跳过阈值 = 2次
- [ ] 回合计时器 = 30秒
- [ ] 每副主牌堆 = 40张，符文堆 = 12张
- [ ] 卡莎进化：需4种不同关键词，加成+3/+3
- [ ] 易大师防守触发：1名盟友，加成+2 ATK（当回合）
- [ ] 德莱厄斯触发：本回合出牌≥2
- [ ] 千尾监视者：全体敌方-3 ATK（当回合）
- [ ] atk(u) 最小值 = 1
- [ ] AI行动间隔 = 700ms；Toast持续 = 1800ms

---

## 跨文件交互流程链
- [ ] 出随从完整链：点击手牌→canPlay→选择区域→executeDrop→deployToBase/BF→mk()→onSummon→急速检查→exhausted→cleanDeadAll→render()
- [ ] 法术施法链：点击手牌→getSpellTargets→目标高亮→确认→applySpell→效果→cleanDeadAll→回响检查→render()
- [ ] 法术对决链：单位移入空战场→startSpellDuel→runDuelTurn→轮流响应→skip×2→endDuel→triggerCombat
- [ ] 战斗结算链：triggerCombat→roleAtk→dealDamage×双方→cleanDeadAll→deathwish→积分→checkWin→render()
- [ ] 符文回收链：点击回收→recycleRune→飞行动画→addSch→从pRunes移除→render()
- [ ] 传奇受伤/死亡链：dealDamage(isLegend=true)→pLeg.currentHp-=dmg→checkWin→endGame
