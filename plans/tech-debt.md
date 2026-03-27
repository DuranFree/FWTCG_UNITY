# 技术债
> 发现时追加，解决后删除。格式：`- [ ] <描述> — 原因：<why deferred> — Phase <number>`

- [ ] 装备"optional target"提示弹窗 — 原因：PromptTarget 返回 null 时装备留在基地，玩家手动激活路径需要 UI 层 Prompt，P10 实现 — Phase P7
- [ ] foresight_mech_enter（先见机甲预知）— 原因：需要 prompt 展示牌堆顶，纯逻辑层无法实现，P9/P10 实现 — Phase P3
- [ ] 缇亚娜·冕卫被动（对手本回合不得积据守分）— 原因：AddScore 中需扫描场上缇亚娜，P9 实现 — Phase P3
- [ ] 战场牌效果（16 张）— 原因：DoStart/TriggerCombat/AddScore 多处需要分发，P9 实现 — Phase P3
