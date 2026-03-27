# 技术债
> 发现时追加，解决后删除。格式：`- [ ] <描述> — 原因：<why deferred> — Phase <number>`

- [ ] DeckFactory 使用 `name = "..."` (ScriptableObject.name) 而非 `cardName = "..."` 初始化卡名，导致运行时 `card.cardName` 为空 — 原因：CardData 初始化器写法延续了旧习惯，需全量改为 `cardName = "..."` 并在 Make() 添加 `so.cardName = template.cardName` — Phase P13
- [ ] 装备"optional target"提示弹窗 — 原因：PromptTarget 返回 null 时装备留在基地，玩家手动激活路径需要 UI 层 Prompt，P10 实现 — Phase P7
- [ ] 传奇初始HP/ATK/Cost ScriptableObject 配置 — 原因：CardData SO 尚未在 Unity Editor 中填写卡莎14HP/易12HP，需 UI Phase 在 Inspector 配置 — Phase P10
- [ ] ascending_stairs 实现与 text 字段描述不一致（text：WIN_SCORE+1；实现：据守/征服额外+1分）— 等待规则确认再同步 text — Phase P9
