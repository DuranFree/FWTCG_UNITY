# 开发日志 — FWTCG Unity 移植

---

## Phase 1: 需求挖掘 + 深度扫描 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 完成全部 Grill Me 决策（Q1~Q8）
- 对原版 7872 行代码执行三项深度扫描
- 生成并经用户确认完整功能清单（7个 Slice，60+ 条目）
- 初始化 Unity 目标项目目录结构
- 关联 GitHub 远端：https://github.com/DuranFree/FWTCG_UNITY.git

**Decisions made**:
- 目标引擎：Unity 2022.3.62f3c1
- 目标平台：PC + Mobile（横屏）
- UI 系统：uGUI（Canvas）— UI Toolkit 在 2022.3 runtime 不成熟
- 卡牌数据：ScriptableObject — 预留未来卡组构筑扩展
- 垂直切片顺序：Slice1(核心回合) → Slice2(法术) → Slice3(装备) → Slice4(传奇) → Slice5(效果全集) → Slice6(游戏流程) → Slice7(平台适配)
- 音频：占位，暂不移植
- 存档：无需（每次重新开局）
- 本地化：预留多语言接口，当前仅中文

**Technical debt**: 无

**Problems encountered**: 无
