# 技术债
> 发现时追加，解决后删除。格式：`- [ ] <描述> — 原因：<why deferred> — Phase <number>`

- [ ] DOTween 未安装，所有动画用 UITween Coroutine 替代 — 原因：Asset Store 包需手动导入，暂用内置方案；后续可替换为 DOTween 获得链式 API 和性能优化 — Phase P14
- [ ] 装备"optional target"提示弹窗 — 原因：PromptTarget 返回 null 时装备留在基地，玩家手动激活路径需要 UI 层 Prompt，P10 实现 — Phase P7
- [ ] 传奇初始HP/ATK/Cost ScriptableObject 配置 — 原因：CardData SO 尚未在 Unity Editor 中填写卡莎14HP/易12HP，需 UI Phase 在 Inspector 配置 — Phase P10
- [ ] ascending_stairs 实现与 text 字段描述不一致（text：WIN_SCORE+1；实现：据守/征服额外+1分）— 等待规则确认再同步 text — Phase P9
