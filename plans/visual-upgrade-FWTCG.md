# Visual Upgrade Guide: FWTCG
> Source platform: Web JS (HTML/CSS/Canvas)
> Target platform: Unity 2022.3 LTS — URP + uGUI
> Generated from: styles.css, battle-enhance.css, title-enhance.css, 3d-enhance.css, equipment.css, particles.js, 3d-tilt.js, ui.js, dragAnim.js

---

## Summary

原版是深空黑底 + 金/青双色调的 Hextech 科幻风卡牌游戏，大量依赖 CSS box-shadow 模拟辉光、DOM 节点模拟粒子、CSS perspective 模拟 3D。移植到 Unity URP 后，三大核心升级：① URP Bloom + Emission 取代所有 box-shadow 辉光（视觉质量跃升最大）；② GPU Particle System 取代 Canvas JS 粒子；③ 真实 3D Transform 取代 CSS rotateY 伪 3D。整体视觉风格保持原版 Hextech 暗金配色不变，仅在渲染质量上升级。

---

## Color Palette

| 变量名 | Hex / RGBA | 用途 |
|--------|-----------|------|
| Gold Primary | #c8aa6e | 卡牌边框、标题、UI 强调 |
| Gold Light | #f0e6d2 | 积分文字、高亮文字 |
| Gold Dark | #785a28 | 卡牌暗部边框 |
| Teal | #0ac8b9 | 装备、符文、选中高亮 |
| Teal Dim | #005a82 | 战场底色渐变 |
| Background | #010a13 | 全局背景底色 |
| Background Mid | #0a1428 | 面板底色 |
| Text Primary | #a09b8c | 普通文字 |
| Text Dim | #5b5a56 | 次要文字、禁用状态 |
| Damage Red | #e84057 | 伤害数字、敌方分数 |
| Heal Green | #40e88a | Buff、玩家分数 |
| Spell Blue | rgba(96,165,250) | 法术目标高亮 |
| **炽烈 Blazing** | rgba(255,120,40) | 符文/符能：橙红 |
| **灵光 Radiant** | rgba(240,210,120) | 符文/符能：金黄 |
| **翠意 Verdant** | rgba(80,210,120) | 符文/符能：翠绿 |
| **摧破 Crushing** | rgba(220,60,80) | 符文/符能：深红 |
| **混沌 Chaos** | rgba(160,80,220) | 符文/符能：紫色 |
| **序理 Order** | rgba(80,180,240) | 符文/符能：天蓝 |

---

## Layer Architecture（渲染层从下到上）

```
Layer 0 — 背景 (Camera Background)
  └─ 深空黑 #010a13，URP Skybox 关闭

Layer 1 — 棋盘底层 (UI Canvas, Screen Space)
  ├─ 六边形网格纹理（Sprite/RawImage，TilingMode = Tiled）
  ├─ 拉丝金属条纹（Sprite + 自定义 Shader 或 Material）
  └─ 径向环境光（RadialGradient Shader 或 Gradient Texture）

Layer 2 — 游戏区域
  ├─ 战场区（BF1 / BF2 Panel，含分隔线）
  ├─ 基地区（玩家/敌方 Panel）
  └─ 积分轨道（左右 Layout Group）

Layer 3 — 游戏对象（卡牌 / 符文 / 传奇）
  ├─ 所有卡牌 Image（可打出时开启 Emission）
  └─ 符文元素（独立 Canvas，可播放入场动画）

Layer 4 — 粒子特效 (Particle System, Sorting Layer: VFX)
  ├─ 背景环境粒子（萤火虫 / 星座连线）
  └─ 事件粒子（法术冲击 / 落地涟漪 / 死亡飞出）

Layer 5 — UI 交互层
  ├─ 手牌区（bottom anchor Panel）
  ├─ 按钮 / 信息栏
  └─ Drop Zone 高亮 Overlay

Layer 6 — 浮层 / Modal
  ├─ 战斗结算 Overlay
  ├─ 卡牌详情预览
  └─ Prompt 对话框

Layer 7 — 顶层特效
  ├─ 伤害数字（WorldSpace Canvas 或 Overlay Canvas）
  ├─ Toast 通知
  └─ 拖拽卡牌 Ghost（raycastTarget=false）

Post Processing Stack (URP Volume)
  ├─ Bloom（替代所有 box-shadow glow）
  ├─ Vignette（替代 CSS vignette overlay）
  ├─ Color Adjustments（替代 CSS filter: brightness/contrast）
  └─ Film Grain（替代 SVG fractal turbulence 噪点）
```

---

## Effect-by-Effect Breakdown

---

### 1. 全局辉光 / 卡牌边框辉光

**Original implementation**: `box-shadow: 0 0 20px rgba(X,X,X,0.3), 0 0 40px rgba(X,X,X,0.15)` 多层叠加
**Original limitation**: CSS shadow 没有 HDR，不会影响邻近像素，只是模糊阴影
**Target platform solution**: URP Post Processing → **Bloom**
**Implementation notes**:
- 所有"发光"元素材质开启 Emission，颜色匹配原版 rgba
- URP Volume: Bloom Intensity = 1.2~2.0，Threshold = 0.8
- 卡牌 Image Material = URP/Unlit + Emission property（可脚本控制强度）
- 符文 Bloom Intensity 比普通卡牌高 1.5x
**Priority**: High — 整个游戏视觉风格的基础

---

### 2. 卡牌可打出旋转彗星边框

**Original implementation**: CSS `@property --playable-angle`，conic-gradient 旋转，mask 限制到 2px 边框
**Original limitation**: DOM 级别 animation，CPU 驱动，无法 GPU 批处理
**Target platform solution**: **Shader Graph** — 边框旋转发光 Shader
**Implementation notes**:
- 在 Shader Graph 中：用 `Time` 节点驱动 `atan2` 极坐标 → 生成旋转彗星
- 彗星头部颜色：`rgba(200,255,218,1)`，尾部渐变到透明
- 只激活边框区域：用 UV 距离 mask（只渲染最外 2px）
- 脚本控制 `_PlayableActive` float 属性（0=关闭，1=开启）
- 每帧消耗：单卡 <0.1ms（GPU shader）
**Priority**: High — 核心玩法视觉反馈

---

### 3. 卡牌 3D 倾斜 / 视差效果

**Original implementation**: JS 追踪鼠标位置 → `perspective(800px) rotateX/Y(max 18°)` CSS transform
**Original limitation**: CSS perspective 是伪 3D，单轴线性投影，不是真正相机旋转
**Target platform solution**: **RectTransform.localRotation** + DOTween Lerp
**Implementation notes**:
- 鼠标进入 card → 记录 card 中心坐标
- 每帧：`Vector2 delta = mousePos - cardCenter`，归一化到 ±1
- `targetRotX = -delta.y * 18f`，`targetRotY = delta.x * 18f`
- DOTween: `transform.DOLocalRotate(target, 0.12f)` on enter，`0.08f` on exit
- 叠加全息高光：子 Image，alphaFromShine shader，跟随鼠标位置
**Priority**: Medium

---

### 4. 全息闪光扫过（Foil Sweep）

**Original implementation**: `105°` linear-gradient 从左到右动画扫过，金+青双色
**Original limitation**: CSS 无法做 HDR 色散，无法做真正彩虹 iridescence
**Target platform solution**: **Shader Graph — Iridescence Shader**
**Implementation notes**:
- `_Time.y * _ScrollSpeed` → UV offset → Sample gradient texture（金→青→紫→金）
- Fresnel 节点控制边缘强度
- 仅在 hover 事件时开启（脚本设 `_FoilActive = 1`）
- 持续时间：0.8s，对应原版 `foil-sweep` timing
**Priority**: Low（视觉加分项，不影响可读性）

---

### 5. 背景六边形网格

**Original implementation**: SVG `<pattern>` inline，六边形 path，0.04 alpha，tile 到全屏
**Original limitation**: SVG DOM 在浏览器里性能ok，但无法做深度或视差
**Target platform solution**: **UI RawImage + 贴图（Tiled）**
**Implementation notes**:
- 导出六边形 tile 为 64×64px PNG（背景透明，线条 rgba(255,255,255,0.04)）
- RawImage 组件，`uvRect` 设定 tiling，保持原版 28×49 比例
- 或使用 Shader Graph Procedural Hex 节点（零贴图占用）
**Priority**: Medium

---

### 6. 径向环境光晕

**Original implementation**: 多层 `radial-gradient`，青色上方 + 金色右下，叠加到背景
**Original limitation**: 静态图层，无法动态变化
**Target platform solution**: **Gradient Texture + UI Image + Additive Blend**
**Implementation notes**:
- 预烘焙 2 张 radial gradient 贴图（512×512 即可）
- UI Image，Material = URP/Unlit，Blend = Additive
- 青色圆心：50% x，46% y；金色圆心：85% x，60% y
- 可加轻微呼吸动画：DOTween alpha 0.08↔0.15，周期 4s
**Priority**: Medium

---

### 7. 拖拽放置漩涡

**Original implementation**: JS 动态创建 3 个 div 环 + 10 个 span 粒子，CSS animation 驱动旋转
**Original limitation**: DOM 节点创建/销毁有 GC 压力，每次拖拽都重新 instantiate
**Target platform solution**: **Particle System（预热 + 对象池）**
**Implementation notes**:
- 预建 VortexFX Prefab：Particle System x3（3个环形发射器，不同旋转速度）
  - 环1：8s/圈，半径 60px
  - 环2：10s/圈，反转，半径 45px
  - 环3：12s/圈，半径 30px
- 螺旋粒子：Emission Rate = 20，Speed = 随机 40-80
- 对象池：DragDropManager 持有，PlayOnEnable / StopEmitting on disable
- Drop 时触发 ImpactBurst（12粒子向外散射，0.45s，ease-out）
**Priority**: High — 直接影响拖放手感

---

### 8. 背景环境粒子系统

**Original implementation**: Canvas 2D API，~75 粒子（主粒子+符文字形+萤火虫），CPU 循环，含星座连线
**Original limitation**: 单线程 CPU 粒子，星座连线是 drawLine 每帧重算
**Target platform solution**: **GPU Particle System × 3 layers**
**Implementation notes**:
- Layer A — 主浮动粒子（45 粒子）：
  - 颜色：青 `#0ac8b9` ~ 金 `#c8aa6e`，2% 透明度
  - 随机游走：Noise Module（频率 0.3，强度 0.5）
  - Size：2-4px
- Layer B — 符文字形（8 粒子）：
  - Texture Sheet Animation：6帧（对应6种符文符号）
  - 尺寸 8-10px，极低 alpha（0.08~0.15）
- Layer C — 萤火虫（12 粒子）：
  - 长弧线运动，Trails 组件（trail 长度 0.3s）
  - 颜色：随机从符文色盘取色
- 星座连线：Line Renderer + C# Job System（距离检测批处理）
**Priority**: Low（背景氛围，不影响玩法可读性）

---

### 9. 伤害数字浮起

**Original implementation**: `position: absolute`，CSS `@keyframes dmg-float-up`（translateY -60px，opacity 0→1→0，1.2s）
**Original limitation**: DOM 插入/删除，批量伤害时有明显卡顿
**Target platform solution**: **World Space Canvas + Object Pool**
**Implementation notes**:
- DamageTextPool：预建 20 个 TextMeshPro 对象
- 触发时：从池取出 → 设文字 + 颜色 → DOTween（anchoredPosition.y +60，1.2s，ease-out）+ fade
- 颜色：玩家伤害红 `#e84057`，Buff 绿 `#40e88a`，法术蓝 `rgba(96,165,250)`
- Canvas 模式：World Space（跟随卡牌位置）或 Overlay（固定屏幕坐标）
**Priority**: High — 核心战斗反馈

---

### 10. 全屏震动

**Original implementation**: `@keyframes screenShake`，`#board-wrapper` translate 随机抖动，0.42s
**Original limitation**: 移动整个 DOM 树，触发全局 layout
**Target platform solution**: **Cinemachine Virtual Camera Shake** 或 **Canvas RectTransform DOShake**
**Implementation notes**:
- 方案A（推荐）：Main Camera DOShake（DOTween）：`camera.DOShakePosition(0.42f, strength: 8f, vibrato: 10)`
- 方案B：顶层 Canvas RectTransform `DOShakeAnchorPos(0.42f, 15f)`
- cubic-bezier(0.1, 0.8, 0.4, 1) → AnimationCurve 等效：快进慢出带过冲
**Priority**: Medium

---

### 11. Toast 通知

**Original implementation**: `#toast-stack` flex column，CSS `toastFloat`（0.4s up）+ `toastFloatOut`（0.3s）
**Original limitation**: CSS animation，无法做物理堆叠
**Target platform solution**: **Vertical Layout Group + DOTween Sequence**
**Implementation notes**:
- ToastManager：持有 VerticalLayoutGroup，最多6条
- 每条 Toast：RectTransform anchored bottom，`DOFade(0→1, 0.4s)` + `DOAnchorPosY(+30→0, 0.4s)`
- 超时（1.8s）：`DOFade(1→0, 0.35s)` + Destroy/Return to pool
- Toast 类型颜色（匹配原版）：法术对决=蓝，得分=绿/红，重要=金，阶段=灰
**Priority**: High — 主要信息反馈渠道

---

### 12. URP Post Processing Stack（全局）

**Original implementation**: 分散在各 CSS filter/box-shadow，无统一管道
**Original limitation**: 无 HDR，无真实模糊，无 vignette
**Target platform solution**: **URP Global Volume**
**Implementation notes**:
- **Bloom**: Intensity 1.5, Threshold 0.8, Scatter 0.7 — 对应所有发光元素
- **Vignette**: Intensity 0.35, Smoothness 0.5 — 替代原版暗角 overlay
- **Film Grain**: Intensity 0.08, Type = Blue Noise — 替代 SVG turbulence 噪点
- **Color Adjustments**: Contrast +5, Saturation -5 — 保持原版偏暗沉的科幻感
- **Lift/Gamma/Gain**: Shadows 略偏蓝黑（匹配 #010a13 基调）
**Priority**: High — 一次配置，全局生效

---

### 13. 积分轨道动画

**Original implementation**: `.sc-circle.active` 用 box-shadow 脉冲，CSS `score-pulse-green/red` 0.5s
**Original limitation**: box-shadow 无 HDR
**Target platform solution**: **DOTween + Emission Color**
**Implementation notes**:
- 每格 Image 材质开启 Emission
- 得分时：`image.material.DOColor(glowColor, "_EmissionColor", 0.5f).SetLoops(2, LoopType.Yoyo)`
- 玩家得分绿 `#40e88a`，敌方得分红 `#e84057`
**Priority**: Medium

---

### 14. 标题界面多层入场序列

**Original implementation**: 9 层 DOM 元素，各自 CSS animation-delay（0.2s→1.7s 递增）
**Original limitation**: CSS animation 不易同步控制，不能中途打断
**Target platform solution**: **DOTween Sequence**
**Implementation notes**:
```csharp
var seq = DOTween.Sequence();
seq.Insert(0.2f,  shellText.DOFadeIn(0.7f));
seq.Insert(0.4f,  kickerText.DOFadeIn(0.7f));
seq.Insert(0.5f,  titleH1.DOFadeIn(0.7f).Join(titleH1.transform.DOLocalMoveY(0, 0.7f)));
seq.Insert(0.6f,  crestLine.DOScaleX(1f, 0.8f));
seq.Insert(0.8f,  subtitle.DOFadeIn(0.7f));
seq.Insert(1.0f,  matchup.DOFadeIn(0.7f));
seq.Insert(1.2f,  brief.DOFadeIn(0.7f));
seq.Insert(1.5f,  startBtn.DOFadeIn(0.7f));
seq.Insert(1.7f,  footnote.DOFadeIn(0.7f));
```
- 所有 cubic-bezier(0.22,0.61,0.36,1) → `Ease.OutCubic`
**Priority**: Low（标题界面，游戏内不涉及）

---

## Implementation Order（推荐实施顺序）

1. **URP Post Processing 全局 Volume** — 一次搞定，后续所有特效自动受益
2. **卡牌 Emission Material** — Bloom 的前提，所有卡牌发光的基础
3. **伤害数字对象池** — 频繁触发，必须性能稳定
4. **Toast 通知系统** — Slice 1 就需要
5. **拖拽漩涡粒子** — Slice 1 UI 核心交互
6. **积分轨道动画** — Slice 1 核心反馈
7. **战场控制光晕** — Slice 1 战斗反馈
8. **卡牌可打出旋转彗星 Shader** — Slice 2 法术交互
9. **背景六边形/径向光** — Slice 6 视觉完善
10. **粒子背景三层** — Slice 6 视觉完善
11. **3D 倾斜 + 全息闪光** — Slice 6 最终润色
12. **标题序列动画** — Slice 6 最后做

---

## Assets Required

| 资源 | 类型 | 来源 | 备注 |
|------|------|------|------|
| 卡牌图片（卡莎系列） | PNG | tempPic/cards/ksha/ | 已有，直接 import |
| 战场背景图片 | PNG | tempPic/cards/ | 需确认路径 |
| 六边形 tile 纹理 | PNG | 程序生成或从 SVG 导出 | 64×64px |
| 径向渐变贴图 | PNG | 程序烘焙 | 512×512px x2 |
| Cinzel 字体 | TTF/OTF | Google Fonts（免费） | 已在原版使用 |
| 符文符号字形贴图 | PNG | 从原版 emoji 截图 | 用于粒子 texture sheet |
| DOTween | Unity Package | Asset Store（免费版足够） | 动画系统核心依赖 |

---

## Known Gaps（无直接等效方案）

| 原版效果 | Unity 替代方案 | 降级说明 |
|---------|--------------|---------|
| CSS conic-gradient 边框（精确像素级） | Shader Graph 极坐标近似 | 视觉上等价，实现更 GPU 高效 |
| SVG fractal turbulence 噪点 | URP Film Grain / Shader 噪声 | Film Grain 更自然 |
| backdrop-filter: blur（毛玻璃） | Full Screen Pass Renderer Feature | 2022.3 需要自定义 RenderFeature |
| CSS mix-blend-mode: overlay | URP Blend Mode Material | 效果相同，需自定义材质 |
