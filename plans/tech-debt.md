# 技术债
> 发现时追加，解决后删除。格式：`- [ ] <描述> — 原因：<why deferred> — Phase <number>`

- [ ] DOTween 已加入 manifest.json（OpenUPM com.demigiant.dotween:1.2.765），需 Unity 重启后自动下载；下载完成后可逐步将 UITween 替换为 DOTween 链式 API — Phase P29
- [ ] 装备"optional target"提示弹窗 — 原因：PromptTarget 返回 null 时装备留在基地，玩家手动激活路径需要 UI 层 Prompt，P10 实现 — Phase P7
- [ ] ascending_stairs 实现与 text 字段描述不一致（text：WIN_SCORE+1；实现：据守/征服额外+1分）— 等待规则确认再同步 text — Phase P9
- [ ] 拖拽放置漩涡螺旋粒子未实现（checklist 原文"3 环旋转 + 螺旋粒子"，目前只有 2 环旋转）— 原因：uGUI 粒子复杂，P35 跳过 — Phase P35
