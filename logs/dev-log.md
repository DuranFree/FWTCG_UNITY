# 开发日志 — FWTCG Unity 移植

---

## 2026-03-27 — P8: 传奇系统

**Phase**: P8 传奇系统 (Legend System)

**New files**:
- `Assets/Scripts/Core/LegendSystem.cs` — AbilityDef 技能定义表（硬编码 kaisa/masteryi）；CheckLegendPassives / TriggerLegendEvent / CanUseLegendAbility / ActivateLegendAbility / ResetLegendAbilitiesForTurn；AiLegendActionPhase / AiLegendDuelAction
- `Assets/Tests/EditMode/LegendSystemTests.cs` — 25 项行为验证测试

**Modified files**:
- `GameState.cs` — LegendInstance 新增 `maxHp`（进化后动态最大HP）
- `TurnManager.cs` — SetLegendSystem；StartTurn 调用 ResetLegendAbilitiesForTurn
- `CombatResolver.cs` — SetLegendSystem；TriggerCombat 伤害前 TriggerLegendEvent("onCombatDefend")，CleanDead 后 CheckLegendPassives
- `CardDeployer.cs` — SetLegendSystem；DeployToBase/ToBF 调用 CheckLegendPassives
- `AIController.cs` — SetLegendSystem；AiAction P8 调用 AiLegendActionPhase；AiDuelAction P8 调用 AiLegendDuelAction

**Test results**: 288/288 passed (prior 263 + 25 new P8)

**Design decisions**:
- 技能定义硬编码在 `_abilityDefs` 字典（按 data.id 查表），不用 ScriptableObject 嵌套
- kaisa_void_sense: once=false（每次只要未 exhausted 均可激活）
- maxHp 字段新增，避免修改只读 data.hp（ScriptableObject）

**Technical debt**: 无

**Problems encountered**: 无（首次无编译错误/测试失败的 Phase）

---

## P5（移植）: 基础 AI — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 ai.js 全文（aiAction / aiDecideMovement / aiCardValue / aiBoardScore / aiDuelAction）
- Assets/Scripts/Core/AIController.cs — AI 决策核心：
  - `AiCardValue(card)`：(atk/cost)×10 + 关键词加成（急速+4/壁垒+3/强攻+2/绝念+2）
  - `AiBoardScore()`：scoreDiff×3 + handDiff×0.5 + bfControl×2 + unitPow×0.3
  - `AiEvalBattlefield(i)` / `AiSimulateCombat(movers, i)`：局面评估
  - `AiDecideMovement(active)`：8种情景评分（空战场征服/战斗胜负/紧迫感/分兵策略）
  - `AiAction()`：横置符文→出随从（价值排序）→移动→结束回合；急速单位以活跃入场
  - `Schedule` 委托：测试注入同步版本，Unity 注入协程版本
  - P6/P8 存根：法术施放、传奇技能
  - `AiDuelAction()`：P5 返回 false，P6 完整实现
- `MoveDecision` / `BfEval` / `CombatSim` 辅助数据结构
- Assets/Tests/EditMode/AIControllerTests.cs — 31 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (179/179，含 P1~P4 的 148 个)

**Decisions made**:
- Schedule 委托注入：与 Unity 协程解耦，测试时同步执行完整 AI 回合
- 急速单位入场：AIController 传 `enterActive: hasHaste` 给 DeployToBase，与原版行为一致
- 测试中同步调度器导致 DoEndPhase 在 AiAction 结束后立即执行，eMana 归零；测试只验证符文 tapped 状态

**Technical debt**: 无

**Problems encountered**:
- 2 个测试初版写错预期值：TapsAllUntappedRunes 断言 eMana==2（DoEndPhase 已归零）；HasteUnit_EntersNonExhausted 未锁 BF 槽位（单位被移到战场后 eBase 为空）——均已修正
- Unity "project already open" crash：kill 旧进程→删 lockfile→重新运行（P4 已知问题）

---

## P4（移植）: 基础战斗结算 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 combat.js 全文（triggerCombat/cleanDead/roleAtk/assignDmg）
- CardData.cs 补充：`guardBonus` 字段（默认1，等价原版 `u.guardBonus || 1`）
- CardInstance.cs 补充：`guardBonus` 字段，From() 和 Mk() 均已复制
- Assets/Scripts/Core/CombatResolver.cs — 战斗结算核心：
  - `RoleAtk(u, role)`：atk(u) + 强攻 strongAtkBonus（进攻方）+ 坚守 guardBonus（防守方）
  - `AssignDmg(pool, targets)`：壁垒单位优先吸收，同组内按最低HP先吸收
  - `TriggerCombat(bfId, attacker)`：计算双方战力→分配伤害→压制溢出打传奇→CleanDead→HP重置（规则627.5）→胜负判定（Conquer/Defend/Draw/BothDead）
  - `CleanDead(bfId)`：BF侧（装备召回基地、death_shield检查、deathwish、重置、废牌）+ 基地侧（同步清理）
- `CombatResult` 枚举：Conquer / Defend / Draw / BothDead
- Assets/Tests/EditMode/CombatResolverTests.cs — 28 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (148/148，含 P1~P3 的 120 个)

**Decisions made**:
- CombatResolver 依赖 CardDeployer（共用 TryDeathShield + TriggerDeathwish），避免逻辑重复
- reckoner_arena 战场牌在战斗时赋予关键词 → P5 实现
- postCombatTriggers（战场牌征服效果）→ P5 实现
- triggerLegendEvent('onCombatDefend', ...) 易大师独影剑鸣 → P5 连接

**Technical debt**: 无

**Problems encountered**: 运行测试时 Unity 因"project already open in another instance"崩溃——与 P1/P2 同样的 UnityLockfile 问题；kill Unity + rm Temp/UnityLockfile 后重新运行正常

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

---

## P1（移植）: 卡牌数据 + 游戏状态 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 创建 Unity 项目（复用 FWTCG_UNITY 的 ProjectSettings/Packages，URP + UTF 已就绪）
- Assets/Scripts/Data/CardData.cs — ScriptableObject，全字段含未来卡组构筑扩展口
- Assets/Scripts/Data/RuneData.cs — ScriptableObject，6种符文
- Assets/Scripts/Core/CardInstance.cs — 运行时实例，等价 mk()，含 EffectiveAtk 属性
- Assets/Scripts/Core/GameState.cs — 等价 G 对象，含全部 70+ 字段
- Assets/Tests/EditMode/CardInstanceTests.cs — 16个行为验证测试
- 🟢 [逻辑测试] 全部通过 (16/16)

**Decisions made**:
- asmdef 需要 `"includePlatforms": ["Editor"]` + `"defineConstraints": ["UNITY_INCLUDE_TESTS"]` 才能被 UTF 发现（首次踩坑，已修复）
- CardInstance 纯 C# 类，不依赖 MonoBehaviour，可直接在 EditMode 测试中实例化

**Technical debt**: 无

**Problems encountered**: UTF asmdef 首次运行0个测试——缺少 includePlatforms + defineConstraints，第二次修复后全绿

---

## P2（移植）: 回合流程 + 符文/费用系统 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 engine.js（doAwaken/doSummon/doDraw/doEndPhase/addScore/checkWin）和 hint.js（符文两步确认系统）
- GameState.cs 补充：PendingRune 类、PendingRuneAction 枚举、pendingRunes 改为 List<PendingRune>、LegendInstance 增加 stunned + tb 字段
- Assets/Scripts/Core/TurnManager.cs — 回合阶段状态机：StartTurn / DoAwaken / DoStart / DoSummon / DoDraw / DoEndPhase / PlayerEndTurn / AddScore / CheckWin
- Assets/Scripts/Core/RuneController.cs — 符文两步确认系统：TapRune / TapAllRunes / RecycleRune / CancelRunes / ConfirmRunes
- Assets/Tests/EditMode/TurnManagerTests.cs — 25 个行为验证测试
- Assets/Tests/EditMode/RuneControllerTests.cs — 25 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (66/66，含 P1 的 16 个)

**Decisions made**:
- TurnManager 纯 C# 类（无 MonoBehaviour），DoEndPhase 修改 G.turn 和 G.round 后由协程调用 StartTurn（P6 UI 层实现）
- DoStart / DoDraw 的异步 prompt 部分（战场牌据守效果、先见机甲预知）留 P5 实现，P2 仅实现纯状态逻辑
- AddScore 包含第8分征服限制逻辑；缇亚娜/攀圣长阶/遗忘丰碑修正留 P5

**Technical debt**: 无

**Problems encountered**: 首次运行 -quit 标志导致 TestResults.xml 未写入，移除 -quit 后正常（重现 P1 踩坑，需记住不加 -quit）

---

## P3（移植）: 手牌 & 出牌系统 — 2026-03-27

**Status**: ✅ Completed

**What was done**:
- 读取 spell.js（canPlay / deployToBase / deployToBF / moveUnit / cleanDeadAll / onSummon / triggerDeathwish / tryDeathShield / dealDamage）
- CardInstance.cs 补充：Mk() 实例方法（等价 mk(c)）、AllocUid() 静态方法（用于运行时创建临时牌如碎片）
- Assets/Scripts/Core/CardDeployer.cs — 出牌核心逻辑：CanPlay / GetEffectiveCost / DeployToBase / DeployToBF / MoveUnit / RemoveUnitFromField / DealDamage / CleanDeadAll / TryDeathShield / OnSummon / TriggerDeathwish
- Assets/Tests/EditMode/CardDeployerTests.cs — 54 个行为验证测试
- 🟢 [逻辑测试] 全部通过 (120/120，含 P1+P2 的 66 个)

**Decisions made**:
- OnSummon 实现全部已知入场效果框架；先见机甲（foresight_mech_enter）需要 prompt 留 P5
- DealDamage 内部调用 CleanDeadAll（与原版一致），清理后单位 HP 已重置；测试验证"单位进废牌堆"而非"HP==0"
- 急速可选付费路径（deployToBase/ToBF 的 askPrompt）通过 enterActive bool 参数预留，P6 UI 层传入

**Technical debt**: 无

**Problems encountered**: 4 个测试初版写错期望值（MakeCard helper 的 atk 固定为 2 与 cost 不同；CleanDeadAll 后 HP 被 reset）——已修正测试逻辑

---

## 2026-03-27 — P6: 法术系统

**Phase**: P6 法术系统 (SpellSystem)

**New files**:
- `Assets/Scripts/Core/SpellSystem.cs` — 完整法术系统：CanPlay / GetSpellTargets / ApplySpell（33 效果）/ 法术对决（StartSpellDuel / RunDuelTurn / SkipDuel / AiSkipDuel / EndDuel）/ HasPlayableReactionCards / GetEffectiveCost；提示委托注入（PromptTarget / PromptDiscard / PromptBattlefield / PromptEcho / OnAiDuelTurn）
- `Assets/Tests/EditMode/SpellSystemTests.cs` — 45 项行为验证测试

**Modified files**:
- `Assets/Scripts/Data/CardData.cs` — 添加 `echoManaCost` 字段
- `Assets/Scripts/Core/CardInstance.cs` — 添加 `echoManaCost` 字段（From + Mk 同步）
- `Assets/Scripts/Core/GameState.cs` — `LegendInstance` 添加 `uid` 字段（含静态计数器）
- `Assets/Scripts/Core/AIController.cs` — 注入 SetSpellSystem；实现 AiShouldPlaySpell / AiChooseSpellTarget / AiSpellPriority / AiDuelAction（5策略）/ AiCheckReactionPlay；AiAction 增加 rally_call（步骤3）/ balance_resolve（步骤4）/ 法术施放（步骤6）

**Test results**: 242/242 passed (prior 197 + 45 new P6)

**Design decisions**:
- Prompt delegates 注入替代 async/await，使 EditMode 测试可同步运行
- isEcho bool 参数防止回响无限递归
- LegendInstance.uid 新增，供 SpellSystem 中 target.uid == opLeg.uid 对比
- AiAction 递归调用 Schedule 同步时会触发完整 AI 回合（测试依此设计）

**Technical debt**: 对决后 AI 继续行动链（OnDuelEnded 回调）由 UI 层 P10 接管

**Problems encountered**:
- LegendInstance 无 uid → 添加静态 UID 计数器解决
- 旧 AIControllerTests 期望 AiDuelAction 返回 bool → 改为 void + DoesNotThrow
- deal3_twice 测试初版单位 HP=5（两次3伤死亡）→ 改 HP=8
- rally_call 测试检查 eRallyActive（DoEndPhase 重置）→ 改检查 eDiscard + unit.exhausted
- balance_resolve 测试绘制的单位 cost=1 被后续 AiAction 递归部署 → 改 cost=4

---

## 2026-03-27 — P7: 装备系统

**Phase**: P7 装备系统 (Equipment System)

**New files**:
- `Assets/Tests/EditMode/EquipmentSystemTests.cs` — 21 项行为验证测试

**Modified files**:
- `Assets/Scripts/Data/CardData.cs` — 添加 `atkBonus` 字段（装配战力加成）
- `Assets/Scripts/Core/CardInstance.cs` — 添加 `atkBonus` 字段（From + Mk 同步）
- `Assets/Scripts/Core/GameState.cs` — 添加 `LastDeployedUid` 字段（追踪最近部署单位 uid）
- `Assets/Scripts/Core/CardDeployer.cs` — TryDeathShield 扩展（优先检查 guardian_equip 附着）；新增 AttachEquipToUnit 方法；DeployToBase/ToBF 设置 LastDeployedUid
- `Assets/Scripts/Core/SpellSystem.cs` — trinity/guardian/dorans/death_shield case 实现；新增 ActivateEquipAbility 方法
- `Assets/Scripts/Core/AIController.cs` — AiAction 步骤5a：AI 部署装备并立即附着到最高战力单位

**Test results**: 263/263 passed (prior 242 + 21 new P7)

**Design decisions**:
- guardian_equip 检查优先于 death_shield（守护天使先于中娅触发）
- 装备 ApplySpell 通过 PromptTarget 委托让玩家选择目标（null=跳过，留在基地）
- AI 装备通过 PromptTarget=OrderByDescending(EffAtk) 选最强单位

**Technical debt**: P7 UI 层需实现"optional target"提示弹窗

**Problems encountered**:
- 测试断言 `IsFalse(pDiscard.Contains(unit))` 错误（死亡单位确实进废牌堆）→ 改为 IsTrue
