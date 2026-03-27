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

---

## Phase 2: 视觉分析 + 视觉升级指南 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 彻底读取全部 5 个 CSS 文件 + particles.js + 3d-tilt.js + ui.js + dragAnim.js
- 生成完整视觉清单（50+ 条目，用户已确认）
- 生成 visual-upgrade-FWTCG.md（14 个效果逐一分析，含 A/B/C 分类）

**Decisions made**:
- 辉光系统：URP Bloom + Emission 替代全部 CSS box-shadow（B类升级）
- 粒子系统：GPU Particle System 替代 Canvas JS 粒子（B类升级）
- 3D 倾斜：RectTransform.localRotation + DOTween 替代 CSS perspective（A类移植）
- 旋转彗星边框：Shader Graph 极坐标 shader 替代 CSS conic-gradient
- 全局后处理：Bloom + Vignette + Film Grain + Color Adjustments（URP Volume）
- 动画系统：DOTween 替代所有 CSS transition / @keyframes
- 外部依赖：需导入 DOTween（Asset Store 免费版）、Cinzel 字体（Google Fonts）

**Technical debt**: 无

**Problems encountered**: 无

---

## Phase 3: 移植计划 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 生成 port-FWTCG.md：12个移植阶段，每阶段含原版文件、移植内容、验收标准
- 粒度经用户确认：P1数据层→P2回合→P3手牌→P4战斗→P5 AI→P6法术→P7装备→P8传奇→P9效果全集→P10流程→P11视觉→P12平台

**Decisions made**:
- 12阶段垂直切片，每阶段独立可验证
- 视觉层（P11）延后到逻辑全部完成后统一实施

**Technical debt**: 无

**Problems encountered**: 注：Phase 1/2 收尾时未显式报告清单状态，已在 CLAUDE.md 第8条补丁修复
