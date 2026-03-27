# 深度扫描结果 — FWTCG Unity 移植
> 生成于 Phase 1（2026-03-27）。此文件之后不再修改。

---

## 原始代码库概况

| 文件 | 行数 | 职责 |
|------|------|------|
| cards.js | ~129 | 卡牌数据定义（KAISA_MAIN / MASTERYI_MAIN / BATTLEFIELDS / 传奇） |
| engine.js | ~405 | 核心状态(G)、回合/阶段流程、积分、符文系统 |
| legend.js | ~335 | 传奇技能系统（被动/触发/主动） |
| ui.js | ~1219 | 渲染引擎、区域更新、卡牌预览、Toast |
| spell.js | ~2458 | 出牌逻辑、法术效果分发、伤害结算、对决系统 |
| combat.js | ~374 | 战斗解算、单位移动、控制权追踪 |
| dragAnim.js | ~633 | 拖拽 UI（覆盖 spell.js 同名函数） |
| ai.js | ~725 | AI 决策（出牌/法术/移动/传奇） |
| main.js | ~430 | 游戏初始化、标题/翻币/选战场/Mulligan |
| hint.js | ~123 | 符文操作 UI |
| particles.js | ~458 | Canvas 粒子特效 |
| **合计** | **~7872** | |

---

## 扫描1 — 硬编码数值与隐藏游戏规则

| 数值 | 位置 | 含义 |
|------|------|------|
| `WIN_SCORE = 8` | engine.js | 胜利所需积分 |
| 手牌上限 7 | engine.js/ui.js | 超过7张不再摸牌 |
| 战场数量 2 | engine.js | bf[] 固定长度2 |
| 每侧战场单位上限 2 | combat.js/spell.js | pU/eU.length < 2 |
| 卡莎符文：炽烈×7 + 灵光×5 | cards.js | 总12张符文 |
| 易符文：翠意×6 + 摧破×6 | cards.js | 总12张符文 |
| 卡莎初始HP = 14 | cards.js | champion hp字段 |
| 易大师初始HP = 12 | cards.js | champion hp字段 |
| 传奇初始ATK = 5，费用 = 5 | cards.js | 两位传奇均同 |
| 对决跳过阈值 = 2 | spell.js | G.duelSkips >= 2 → endDuel |
| 回合计时器 = 30秒 | engine.js | turnTimerSeconds |
| 主牌堆 = 40张 | cards.js | 两副均40 |
| 符文堆 = 12张 | cards.js | 两副均12 |
| 卡莎进化关键词数 = 4种 | legend.js | 不同类型≥4 |
| 卡莎进化加成 = +3/+3 | legend.js | currentAtk+3, atk+3, hp+3 |
| 易大师防守加成 = +2 ATK | legend.js | tb.atk += 2 |
| 易大师触发条件 = 1名盟友 | legend.js | defUnits.length === 1 |
| 德莱厄斯触发条件 = ≥2张 | spell.js | cardsPlayedThisTurn >= 2 |
| 千尾监视者 = -3 ATK | spell.js | currentAtk -= 3 |
| atk(u) 最小值 = 1 | engine.js | Math.max(1, ...) |
| AI行动间隔 = 700ms | ai.js | delay(700) |
| Toast持续 = 1800ms | ui.js | 默认duration |
| 符文入场动画错落 = 50ms×index | ui.js | idx * 0.05s |
| 积分轨道格数 = 9（0-8） | ui.js | sc-circle×9 |

---

## 扫描2 — 配置与数据文件

**结论：原版无任何外部配置文件。** 所有数据均硬编码在 JS 文件中：

- 卡牌数据 → `cards.js`（KAISA_MAIN / MASTERYI_MAIN / BATTLEFIELDS 常量数组）
- 符文定义 → `engine.js`（RUNE_DEFS 对象）
- 传奇数据 → `cards.js`（KAISA_LEGEND / MASTERYI_LEGEND 对象）
- 关键词说明 → `ui.js`（KEYWORD_DETAILS 对象）

**Unity 移植方式**：全部转换为 ScriptableObject（.asset 文件），Inspector 可直接编辑，为未来卡组构筑系统预留扩展。

---

## 扫描3 — 用户交互流程链（跨文件）

### 流程1：随从出牌完整链
```
点击手牌(ui.js)
→ onCardClick(spell.js)
→ canPlay检查(spell.js)
→ buildDropZones(spell.js) / dragAnim.js高亮
→ 点击/拖拽到目标区域(dragAnim.js)
→ executeDrop(spell.js)
→ deployToBase / deployToBF(spell.js)
→ mk()创建实例(engine.js)
→ 法力扣除
→ onSummon(spell.js)
→ 急速关键词检查 → exhausted设置
→ cleanDeadAll(spell.js)
→ render()(ui.js)
```

### 流程2：法术施法完整链
```
点击手牌(ui.js)
→ onCardClick(spell.js)
→ getSpellTargets(spell.js)
→ [有目标] showSpellTargetPopup → 玩家选目标
→ [无目标] 直接确认
→ [无有效目标] 阻止出牌
→ applySpell(spell.js)
→ 效果分发(switch on spell.effect)
→ dealDamage / buff / recall 等
→ cleanDeadAll(spell.js)
→ [回响] 二次applySpell
→ render()(ui.js)
```

### 流程3：法术对决完整链
```
单位移动到空战场(combat.js)
→ startSpellDuel(bfId, attacker)(spell.js)
→ G.duelActive = true
→ runDuelTurn(isAttacker)(spell.js)
  → [player turn] 展示响应UI，30秒计时
  → [enemy turn] aiDuelAction()(spell.js/legend.js)
→ 出牌/跳过 → G.duelSkips++
→ [skips < 2] 切换duelTurn → runDuelTurn
→ [skips >= 2] endDuel()
→ [有敌方单位] triggerCombat()(combat.js)
→ [无敌方单位] 征服战场，+分
```

### 流程4：战斗结算完整链
```
triggerCombat(bfId, attacker)(combat.js)
→ roleAtk(unit, role) = atk(u) ± 强攻/坚守加成
→ dealDamage(attacker, defAtk)(spell.js)
→ dealDamage(defender, atkAtk)(spell.js)
→ cleanDeadAll()(spell.js)
  → triggerDeathwish(unit, owner)(engine.js)
→ postCombatTriggers()(combat.js)
→ 积分检查(addScore)(engine.js)
→ checkWin()(engine.js)
→ render()(ui.js)
```

### 流程5：符文回收完整链
```
点击符文回收按钮(hint.js)
→ recycleRune(uid)(hint.js)
→ 符文飞行动画(ui.js)
→ addSch(owner, runeType, 1)(engine.js)
→ 从G.pRunes移除
→ 放回G.pRuneDeck尾部
→ render()(ui.js)
```

### 流程6：传奇受伤/死亡完整链
```
dealDamage(target, dmg, owner, isLegend=true)(spell.js)
→ G.pLeg.currentHp -= dmg（不操作hp字段）
→ 显示伤害数字动画
→ checkWin()(engine.js)
  → G.pLeg.currentHp <= 0 → endGame()
```

### 流程7：悬停手牌→识别费用缺口→高亮符文
```
鼠标悬停手牌(dragAnim.js)
→ calcResourceFix(card, zoneType)(spell.js)
→ 识别缺少的法力/符能
→ 高亮对应符文（addClasses on rune elements）
→ 拖牌到区域(dragAnim.js)
→ applyResourceFix(fix)(spell.js) 自动补足
→ executeDrop(spell.js)
```

---

## G 对象核心字段（70+字段，全部必须在GameState中实现）

见功能清单中的游戏状态单例条目。完整字段列表已在深度扫描1中整理，包含：
- 积分：pScore / eScore
- 区域：pDeck / eDeck / pHand / eHand / pBase / eBase / pDiscard / eDiscard / pRunes / eRunes / pRuneDeck / eRuneDeck
- 战场：bf[2]（id / pU[] / eU[] / ctrl / conqDone / standby / card）
- 资源：pMana / eMana / pSch / eSch（6种×2）
- 传奇：pLeg / eLeg
- 回合：round / turn / phase / first
- 对决：duelActive / duelBf / duelAttacker / duelTurn / duelSkips
- 追踪：cardsPlayedThisTurn / pRallyActive / eRallyActive / bfScoredThisTurn[] / bfConqueredThisTurn[]
- 状态：gameOver / prompting / reactionWindowOpen
