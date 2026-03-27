# 技术债
> 发现时追加，解决后删除。格式：`- [ ] <描述> — 原因：<why deferred> — Phase <number>`

- [ ] 装备"optional target"提示弹窗 — 原因：PromptTarget 返回 null 时装备留在基地，玩家手动激活路径需要 UI 层 Prompt，P10 实现 — Phase P7
- [ ] foresight_mech_enter（先见机甲预知）— 原因：DoDraw 中需 prompt 展示牌堆顶，纯逻辑层 BattlefieldSystem.PromptConfirm 已预留接口，P10 UI 层补全 — Phase P3
- [ ] ascending_stairs 实现与 text 字段描述不一致（text：WIN_SCORE+1；实现：据守/征服额外+1分）— 等待规则确认再同步 text — Phase P9
