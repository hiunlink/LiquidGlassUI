# Docs/RenderFlow_Detailed.md

**Liquid Glass UI – Full Rendering Pipeline (Detailed Version)**
**适用：UICaptureComposePerLayerFeature.cs / UIScreenManager / LiquidGlassUIEffect**

---

# 目录

1. [概述](#概述)
2. [屏幕系统与 Dirty 驱动机制](#屏幕系统与-dirty-驱动机制)
3. [UICapture 渲染管线总览](#uicapture-渲染管线总览)
4. [详细渲染流程（按执行顺序）](#详细渲染流程)

    * 4.1 RT 初始化
    * 4.2 Clear Pass
    * 4.3 Phase S：前景模板预写
    * 4.4 Phase B/F：按层绘制 + 模糊 + 合成
    * 4.5 Phase M：最终 MIP 链生成
    * 4.6 设置全局纹理供 LiquidGlass 使用
5. [模糊算法细节](#模糊算法细节)
6. [Liquid Glass Shader 使用流程](#liquid-glass-shader-使用流程)
7. [多屏幕 Stack 渲染模型（UIScreenManager）](#多屏幕-stack-渲染模型)
8. [性能策略](#性能策略)
9. [未来可扩展方向](#未来可扩展方向)

---

# 概述

本工程实现了一个高性能、可扩展、可缓存的 **多层 UI 背景捕获与折射模糊系统**，用于渲染 **Liquid Glass（液态玻璃）** 效果。

系统由三大部分组成：

1. **UIScreenManager**
   维护 UI Stack、推入弹出界面并触发 Capture Dirty。

2. **UICaptureComposePerLayerFeature**（URP RenderFeature）
   捕获背景 → 多层混合 → 多种模糊 → 输出全局 `_UI_BG`。

3. **LiquidGlassUIEffect + Shader**
   在 UI 元素上实时计算 **圆角矩形 SDF + 折射 + 色散 + Rim Light + Tint**。

---

# 屏幕系统与 Dirty 驱动机制

每当 UI 有任何变动（Push、Pop、Fade、下层界面可见范围改变），
都会触发：

```csharp
UICaptureAutoDirty.SetDirty();
```

Dirty 为 true → 下一帧 RenderFeature 必须重新捕获背景。
Dirty 为 false → 可以跳过所有 Pass，复用 `_baseCol` 缓存，节省大量 GPU 成本。

---

# UICapture 渲染管线总览

渲染链条如下图（逻辑流程）：

```
UIScreenManager
    ↓ 判断 Stack
UICaptureEffectManager
    ↓ 产出 CaptureConfig
UICaptureComposePerLayerFeature
    ↓
    [Clear]
       ↓
    [Phase S - Stencil Prepass]
       ↓
    [Phase B/F - Layer Draw + Blur + Composite]
       ↓
    [Phase M - Generate Mips]
       ↓
SetGlobalTexture _UI_BG
    ↓
UI Shader (LiquidGlass) 最终渲染
```

---

# 详细渲染流程

## 4.1 RT 初始化

根据 Camera 分辨率与 `resolutionScale` 创建：

* `_baseCol`：最终输出，液态玻璃背景
* `_baseDS`：Depth+Stencil，用于前景裁剪
* `_blurRT`：用于模糊通道的中间图

当尺寸变化或未创建时重新分配。

---

## 4.2 Clear Pass

使用 OneShot Pass 清空：

```
_baseCol  → clearColor
_baseDS   → depth clear + stencil clear
_blurRT   → Color.clear
```

---

## 4.3 Phase S：前景层 Stencil 预写

对所有 `isForeground = true` 的 LayerConfig：

* 使用 `LightMode="StencilPrepass"` Pass
* 写入 Stencil = 1
* 视优化情况可能同时绘制 Opaque 区域（减少之后的 alpha 开销）

目标：在正式绘制前把前景遮挡区域标记好。

---

## 4.4 Phase B/F：按层（0→N）绘制 + Blur + Composite

对每个 `LayerConfig[j]`：

---

### (A) 预处理 blurRT

如果该层需要模糊：

* **multiLayerMix = false**

    * 清 `_blurRT`（该层单独模糊，无叠加）

* **multiLayerMix = true**

    * Blit `_baseCol → _blurRT`
      （叠加模式：每一层模糊基于前面合成结果）

---

### (B) 绘制该层本身

* **前景层 → RenderForeground**

    * 若已在 Phase S 做 Opaque → 这里只做 AlphaOnly
    * 否则完整 DrawDefaultPass

* **非前景层**

    * 直接 DrawDefaultPass 到 `_baseCol` 或 `_blurRT`
    * 使用 `UseStencilClip()` 决定是否启用 `Stencil NotEqual 1` 剪裁被遮挡区域

---

### (C) 模糊阶段：依据 LayerConfig.blurAlgorithm

#### 1) MipChain 模糊

```
MipChainBlurPass:
    - 取 mipLevel
    - 根据 mip 级采样实现模糊
    - 用 Hidden/UIBGCompositeStencilMip 合成回 _baseCol
    - 依照 stencil 决定裁剪区域
```

优点：省电 / 非常快
缺点：低频模糊，无法做大 sigma。

---

#### 2) Gaussian 分离模糊（H/V）

```
GaussianWrapperPass (GaussianBlurPass):
    for iteration:
       H-pass: src → tmp
       V-pass: tmp → dst
    使用 CompositeCopyStencil 合成 dst → baseCol
```

优点：效果高质量
缺点：比 MipChain 成本高。

---

## 4.5 Phase M：最终 Mip 链生成

为 baseCol 生成 mipmap：

```
GenMipsPass:
    cmd.GenerateMips(_baseCol)
```

用于 LiquidGlass shader 的 LOD 采样，提高边缘折射模糊感。

---

## 4.6 设置全局贴图

Feature 最后执行：

```csharp
Shader.SetGlobalTexture("_UI_BG", _baseCol);
Shader.SetGlobalVector("_UIBG_UVScale", scale);
```

提供给所有 UI（LiquidGlassUIEffect）在主相机透明阶段使用。

---

# 模糊算法细节

| 模糊模式             | 优点           | 缺点    | 建议用途              |
| ---------------- | ------------ | ----- | ----------------- |
| **MipChain**     | 极快，手机友好      | 模糊较粗  | 大部分 UI Panel 背景   |
| **Gaussian**     | 高质量，可大 sigma | 比较吃性能 | 主页面背景、Blur 特效面板   |
| **Hybrid**（自行扩展） | 多层模糊组合       | 实现复杂  | 主界面毛玻璃效果、iOS 风格玻璃 |

---

# Liquid Glass Shader 使用流程

**LiquidGlassUIEffect.cs** 会：

1. 读取 RectTransform 宽高，换算成 SDF 参数
2. 传入圆角 & border 宽度
3. 提供 `_UI_BG` 的 UV scale（避免 RT scale 造成缩放错误）
4. Shader 做 SDF 距离场
5. 依据距离与法线偏移做折射
6. RGB 通道具有不同 IOR → 色散折射
7. 加上 Tint、Rim、Reflection
8. 叠加 UI 本身的主纹理

最终呈现 Apple Liquid Glass 的视觉效果。

---

# 多屏幕 Stack 渲染模型

UIScreenManager 管理屏幕堆叠：

```
Push → 新屏幕 OnEnter()
Pop  → 当前屏幕 OnExit()
Pause/Resume → 下层屏幕可见但不可操作
```

当界面变化时：

```csharp
UICaptureAutoDirty.SetDirty();
```

让下一帧 RenderFeature 重算背景。

你支持的特性：

* 多界面堆叠
* 顶层界面透明度影响下层捕获
* 低频更新：若全屏界面不透明 → 可直接跳过捕获
* 多层淡入淡出支持（LowerLayerBlurFade.cs）

---

# 性能策略

1. **Dirty 缓存复用**
   无变化时跳过捕获（整条 RenderFeature 流程被短路）。

2. **低捕获帧率**（可选）
   当 UIScreen 全静止时，可将捕获刷新频率降低到 10 FPS，大幅节省 GPU。

3. **MipChain 优先**
   90% 背景模糊用 MipChain 即可。

4. **RT Scale**
   resolutionScale 设为 0.5–0.75 是最佳性能/质量平衡。

---

# 未来可扩展方向

* 每个 UIScreen 拥有独立的 `_UI_BG`（针对复杂界面）。
* Hybrid 模糊：先 MipChain 再小 sigma Gaussian。
* 进一步的 stencil-based 局部重渲染。
* 自动检测屏幕移动程度（ScrollRect）动态提高/降低捕获频率。


