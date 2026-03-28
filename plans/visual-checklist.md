# 视觉清单 — FWTCG Unity 移植
> 生成于 Phase 2（2026-03-27）。完成项标记 ✅，不删除任何条目。

---

## 界面布局
- ✅ 游戏主界面（1280×720 CanvasScaler，MatchWidthOrHeight 0.5）— P13 GameUI.cs 基础布局（文字UI，无美术）
- [ ] 敌方信息栏（顶部：名称/符文/牌堆信息条）
- [ ] 玩家信息栏（底部：法力/符文/牌堆信息条）
- [ ] 两条战场区（横向双列，各含敌我两侧）
- [ ] 积分轨道（左/右两侧 0-8 格竖向轨道）
- [ ] 玩家手牌区（底部居中扇形排列）
- [ ] 传奇/英雄牌槽（两侧基地区）
- [ ] 战斗覆盖层（全屏战斗结算 overlay）
- ✅ 卡牌详情预览模态框 — P17 CardDetailPanel，居中深蓝底，显示全部文字字段，关闭按钮
- [ ] 标题界面（品牌标志 + 开始按钮 + 光效）
- [ ] 翻币界面（旋转动画 + 结果）
- [ ] 调整手牌界面（Mulligan）
- [ ] 战斗日志面板（右侧可折叠）
- ✅ Toast 通知堆（浮动）— P14 ToastSystem.cs，淡入上滑+淡出，重要战斗事件触发
- [ ] 法术提示弹窗（目标选取）

## 颜色
- ✅ 主色调：金色 #c8aa6e、深黑 #010a13、青色 #0ac8b9 — P14 GameUI C_Gold/C_Dark/C_Cyan 常量，应用到全部面板
- ✅ 6种符文专属配色（炽烈橙/灵光黄/翠意绿/摧破红/混沌紫/序理蓝）— P15 GameUI C_RuneBlazing/Radiant/Verdant/Crushing/Chaos/Order，符文面板按类型着色（横置变暗 ×0.38）
- ✅ 状态色：可打绿 #40e88a — P16 RefreshPlayerHand，canPlay → textCol (0.25,0.91,0.54) + bgCol 深绿；不可打灰色降暗；伤害红/法术蓝/装备青待后续 Phase

## 字体
- [ ] Cinzel 字体（标题、UI 标签、强调文字，多尺寸）
- [ ] 等宽字体（战斗日志 Courier New 13px）
- [ ] 文字光晕效果（金色 text-shadow 0 0 12~40px rgba(200,170,110)）

## 动画
- ✅ 手牌入场动画（PopIn 0.28s OutBack）— P15 GameUI.RefreshPlayerHand，新 uid 检测，StartCoroutine UITween.PopIn
- [ ] 卡牌可打出旋转光环（3s 线性，绿色彗星边框）
- [ ] 悬停 3D 倾斜（±18°，perspective 800px，JS 驱动）
- [ ] 全息闪光扫过（foil-sweep 0.8s）
- ✅ 战场区域呼吸动画（约 4.2s 循环）— P15 BFGlowLoop Coroutine，Sin 波 × InOutQuad，BF0/BF1 面板背景持续呼吸
- ✅ 战场控制光晕（player 青色 / enemy 红色 / neutral 灰色）— P15 BFGlowLoop + BFCtrlColor 静态方法
- ✅ 积分轨道得分脉冲（绿/红 0.5s）— P22 RefreshPlayerInfo/RefreshEnemyInfo，分数增加时 UITween.PulseColor(Text, 0.5s)；我方绿色(0.25,0.91,0.54)，敌方红色(1,0.27,0.27)
- ✅ 伤害数字浮起（1.2s ease-out，向上 60px）— P14 DamageFloatText.cs，5色分类（伤害/治疗/增益/减益/金色）
- ✅ 全屏震动（0.42s）— P14 UITween.Shake()，Coroutine 实现
- [ ] 翻币旋转（1.2s linear）
- ✅ 标题界面（简版：金色标题 + 青色副标题 + 开始按钮）— P19 TitlePanel，BuildCanvas 最后添加（保证最高层级）
- ✅ Toast 浮入/浮出（0.35s/0.25s）— P14 ToastSystem.cs，Coroutine 缓动
- [ ] 漩涡旋转（3 层不同转速 8/10/12s，10 个轨道粒子）
- [ ] 法术施法动画（目标高亮 + 投射物 + 冲击波）
- [ ] 单位死亡飞出
- [ ] Buff/Debuff 光晕（绿/红 0.6s）
- [ ] 眩晕光晕（0.6s）
- [ ] 传奇技能激活环（0.8s）
- ✅ 符文入场错落（每张 50ms 间隔）— P19 RefreshPlayerRunes，i >= oldCount 时 DelayedPopIn(0.25s, i*50ms)
- [ ] 符文回收飞行（弧线到计数器）
- [ ] 卡牌落地震动（0.3s）
- [ ] 落地涟漪波（0.55s）
- ✅ 战斗冲击波（0.38s）— P18 AppendLog 检测"死亡"触发 UITween.Shake(rootCanvasRt, 3.5f)

## 特效
- [ ] Canvas 粒子系统（75 基础粒子：主粒子+符文字形+萤火虫，含星座连线）
- [ ] 拖拽放置漩涡（3 环旋转 + 螺旋粒子）
- [ ] 爆炸粒子（散射 12 粒子）
- [ ] URP Bloom（替代所有 box-shadow glow）
- [ ] 卡牌 3D 真实翻转（替代 CSS rotateY hack）
- [ ] 粒子物理（重力/风/碰撞，替代线性运动）

## 背景与纹理
- [ ] 六边形网格纹理（SVG pattern，28×49px，0.04 alpha）
- [ ] 拉丝金属条纹（repeating-linear-gradient）
- [ ] 噪点叠加（fractal turbulence，0.035 opacity）
- [ ] 径向环境光（多层 radial-gradient，青/金双色）

## 过渡效果
- [ ] 界面淡入淡出（标题→游戏，0.7-1s）
- [ ] Prompt 弹窗弹入/弹出（scale+blur，0.4s/0.3s）
- ✅ 阶段切换横幅（FadeIn 0.25s / 停留 1.1s / FadeOut 0.3s）— P18 PhaseBanner + BannerSequence Coroutine，回合/阶段切换时触发
- [ ] 战场名称飞入（0.4s）
