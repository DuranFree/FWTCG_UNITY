# 已知 Bug
> 发现时追加，修复后标记 ✅，不删除。格式：`- [ ] <描述> — 发现于 Phase <number>`

- ✅ DeckFactory 所有卡牌初始化器使用 `name = "..."` 设置 ScriptableObject 基类属性，`card.cardName` 始终为空字符串，导致 UI 显示空白卡名 — 发现于 Phase P13，修复于 P14 前热修 — 修复：改为 `cardName = "..."`，Make() 补 `so.cardName = template.cardName`
- ✅ CardDeployer.DealDamage isLegend 路径未调用 CheckWin，导致法术/技能击杀传奇后 gameOver 不触发 — 发现于 Phase P26，修复于 P26 — 修复：isLegend 路径末尾新增 `_tm.CheckWin()`
