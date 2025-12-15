<p align="center">
  <img src="Documentation~/Images/LiquidGlass_Cover.png" width="820" alt="Liquid Glass UI Pipeline">
</p>

<h1 align="center">🍏 Liquid Glass UI Pipeline</h1>

<p align="center">
  高质量多层 UI 捕获、模糊与折射系统<br>
  <b>Powered by URP + ScriptableRenderFeature + SDF Refraction Shader</b>
</p>

<p align="center">
  <a href="#特性">特性</a> •
  <a href="#用法">用法</a> •
  <a href="Documentation~/RenderFlow_Detailed.md">详细渲染流程</a> •
  <a href="Documentation~/UIScreenFlow.md">界面状态机</a>
</p>

---
# 📘 UICaptureCompose — Liquid Glass UI & Multi-Layer UI Blur System

> 一个用于 Unity URP 的多层 UI 捕获（UI Capture）、多重模糊预渲染（Blur Compositing）与类 iOS-style Liquid Glass UI 特效系统。
> 支持动态层级 UI Stack、每层 Canvas 独立 RenderTexture、上层 Liquid Glass 再对下层画面实时折射/色散/圆角玻璃化。

---

# ✨ Features

### ✔ **多层 UI Stack 自动分层渲染**

* 自动为每个 UIScreen 的每个子 Canvas 分配独立 Layer。
* 每层 UI 自动写入专属 RenderTexture：`UI_BG_x`。
* 上层界面可看到下层界面（带模糊、色散、折射等效果）。

### ✔ **每一层可配置 Blur 算法**

* MipMapChain（快速、柔和）
* GaussianSeparable（可配置 sigma、iterations）
* GlobalBlur（根据 LiquidGlass UI 自动计算上层需要的全局 blur 程度）

### ✔ **Liquid Glass UI（SDF + Refraction + Tint + Edge Light）**

* 自动根据 RectTransform 计算圆角矩形 SDF
* 支持:

  * 折射 Refraction (RGB chromatic aberration)
  * LOD Blur warp
  * Rim Light
  * Glass Tint
  * 多种 Debug 模式

### ✔ **智能脏标记（AutoDirty）**

* 自动检测 Canvas 图形变化（坐标/颜色/顶点/布局）
* 自动触发对应 Layer 的 UI Capture 重算（防止过度渲染）

### ✔ **编辑器扩展（Inspector Hooks）**

* UIScreen 与 LiquidGlass UI Effect 的 Inspector 变化会触发渲染管线更新

### ✔ **运行时完全动态**

* 屏幕切换（Push/Pop）
* Layer 动态配置
* LiquidGlass UI 数量可变
* UI 动态变化自动标记 Dirty

---

# 📁 项目结构

```
UICaptureCompose/
│
├── UIComponent/
│   ├── LiquidGlassUIEffect.cs           // 前端 UI 组件（核心 SDF + 参数驱动）
│   ├── LiquidGlassUIEffectEditor.cs     // Editor 自定义 Inspector
│   ├── UICaptureAutoDirty.cs            // 自动检测 UI 变化，触发 Layer Dirty
│   ├── LowerLayerBlurFade.cs            // 底层模糊渐变动画控制器
│
├── UIScreen/
│   ├── UIScreen.cs                      // 一个完整 UI Screen 的数据结构
│   ├── UIScreenEditor.cs                // 编辑器 Inspector 扩展
│   ├── UIScreenManager.cs               // 多 Screen 层级管理 + RT 分配 + RendererFeature 更新
│
├── URP/
│   ├── UICaptureComposePerLayerFeature.cs  // 主渲染特效（UI 多层捕获 + 模糊）
│   ├── UIBGReplaceFeature.cs               // UI 背景替换（Shader 特殊用途）
│
├── Runtime/
│   ├── UICaptureEffectManager.cs        // CaptureFeature 全局统一访问入口
|
└── Shader/
    └── Hidden/UI_LiquidGlass.shader     // LiquidGlass 主 shader
```

---

# 🧩 核心模块说明

## 1. UICaptureComposePerLayerFeature（URP Renderer Feature）

这是整个系统的 **渲染核心**。

### 它负责：

* 遍历所有 UIScreen & CanvasConfig
* 为每层生成：

  * 渲染目标 RenderTexture：`UI_BG_0`, `UI_BG_1` …
  * 模糊版本 RenderTexture：`UI_BG_0_BLUR`
* 控制模糊方式（MipChain / Gaussian / Hybrid）
* 管理 Stencil/LayerMask 控制 UI 的 re-draw
* 把模糊后的背景通过 Shader 设置成全局变量传给 LiquidGlass UI

### 每层生成的配置为：

```csharp
UICaptureComposePerLayerFeature.LayerConfig
{
    LayerMask layer,
    bool isForeground,
    bool blur,
    BlurAlgorithm blurAlgorithm,
    float blurMip,
    float gaussianSigma,
    int iteration,
    Color alphaBlendColor,
    GlobalBlurAlgorithm globalBlurAlgorithm,
    float globalGaussianSigma,
    ...
}
```

系统会根据 UIScreenManager 的计算结果自动填充 LayerConfig。

---

## 2. UIScreen（UI 屏幕抽象）

每个 UIScreen 表示一个“界面页面”。

### UIScreen.CanvasConfig:

* canvas（该层 UI）
* isForeground（是否遮挡下层）
* blur（是否开启该层模糊）
* blurConfig（Mip/Gaussian 参数）
* cachedLiquidGlasses（自动绑定 LiquidGlassUIEffect）

### UIScreen 本身支持：

* 下层模糊强度（lowerBlurStrength）
* 下层模糊算法（lowerCanvasBlurConfig）
* 自动加入 UIScreenManager
* Inspector 变化自动更新渲染

---

## 3. UIScreenManager（渲染调度器）

这是整个系统的中枢大脑。

### 功能包括：

✔ 维护屏幕列表（按照 Hierarchy 次序排序）
✔ 为每个 CanvasConfig 分配 Layer
✔ 为每个 UIScreen 分配 RenderTexture 编号
✔ 为每层生成最终的 LayerConfig
✔ 更新 RendererFeature
✔ 设置 Dirty（强制重渲染某层）
✔ 设置 LiquidGlass 所需背景 RT 名称

它会生成：

```
UI_BG_1 → 第 1 层背景
UI_BG_2 → 第 2 层背景
UI_BG_3 → …
```

每次渲染只更新必要的部分（有 LiquidGlass 或 Dirty 的界面）。

---

## 4. LiquidGlassUIEffect（前端 UI 特效组件）

这是使用者最直接操作的 UI 效果组件。

### 自动执行：

✔ 自动赋予 LiquidGlass Shader
✔ 基于 RectTransform 计算 SDF（圆角矩形）
✔ 驱动 shader 属性：

* `_RoundedRectHalfSize`
* `_RoundedRadius`
* `_EdgeDim`
* `_RefrMag`
* `_TintColor`
* `_UI_BG` / `_UI_BG_BLUR`

✔ 每帧更新（支持自动跟随 UI 动画）

---

## 5. UICaptureAutoDirty（UI 变化监听器）

自动检测 UI Canvas 变化（颜色/位置/布局/RectTransform）
一旦检测到变化 → 设为 Dirty → 触发该层背景重绘。

---

## 6. LowerLayerBlurFade（底层模糊渐变）

提供 UI 入场 / 对话框弹出时的底层淡入模糊动画。

---

## 7. UIScreenEditor & LiquidGlassUIEffectEditor

* LiquidGlassUIEffectEditor：
* UIScreenEditor：

Inspector 发生变化时会自动调用：

```
UIScreenManager.Instance.UpdateRendererFeature();
UIScreenManager.Instance.SetLowerUIScreenDirty(...)
```

确保编辑器修改立即更新渲染。

---

# 🔧 渲染流程（全链路说明）

以下为系统完整渲染顺序：

```
1. UIScreenManager 收集所有 UIScreen
2. 依据 Hierarchy 自动排层（上层覆盖下层）
3. 为每个 CanvasConfig 分配 Layer + RenderTexture
4. 生成 featureLayerConfigs 列表（给 RendererFeature）
5. UICaptureComposePerLayerFeature 捕获每层 UI → 输出 UI_BG_x
6. 若该层开启 blur → 对 UI_BG_x 进行模糊 → UI_BG_x_BLUR
7. 将背景纹理名写入 LiquidGlassUIEffect.backgroundRTName
8. LiquidGlass shader 读取背景 RT，执行：
    - SDF（圆角矩形）
    - Refraction（可带色散）
    - Tint
    - Rim Light
9. 当 UI 有变化（AutoDirty）→ 重跑某层
10. 完整 UI 合成
```

适合多层 UI 重叠、半透明、玻璃质感、iOS Liquid Glass 风格。

---

# 🚀 如何使用

## 1. 把库导入 Unity 工程

本项目基于 **Unity 2022.3+ URP**。

## 2. 在 URP Renderer Data 上添加：

* **UICaptureComposePerLayerFeature**
* **UIBGReplaceFeature**（可选）

## 3. 在场景添加：

```
UICaptureEffectManager
```

## 4. 创建一个 UIScreen（UI Root）

```
UIScreen
└── CanvasConfig 0（背景 UI 层）
└── CanvasConfig 1（内容 UI 层）
└── CanvasConfig 2（弹窗 UI 层）
```

## 5. 在某些 UI 上挂 LiquidGlassUIEffect

可选配置：

* corner radius
* border width
* tint
* blur algorithm
* refraction

## 6. 如需自动检测 UI 变化

给 Canvas 挂上：

```
UICaptureAutoDirty
```

## 7. 运行时自动：

* 多层 UI 背景缓存
* 液态玻璃实时折射
* 下层模糊自动渐变

---

# 📊 层级与 RenderTexture 分配示例

```
UIScreen A（底）
  Canvas0 → Layer6 → UI_BG_1
  Canvas1 → Layer7 → UI_BG_2

UIScreen B（中）
  Canvas0 → Layer8 → UI_BG_3

UIScreen C（顶）
  Canvas0(LiquidGlass) → Layer9 → 读取 UI_BG_3 作为背景
```

自动完成，无需手动指定 Layer。

---

# 📦 依赖关系图

```
LiquidGlassUIEffect
       ↑ uses
UICaptureEffectManager
       ↑ controls
UICaptureComposePerLayerFeature  ←  UIScreenManager
       ↑ receives LayerConfig
UIScreen
UIScreenEditor / LiquidGlassUIEffectEditor
UICaptureAutoDirty
```

---

# 🛠 开发者扩展

你可以自由添加：

### ✔ 多屏幕切换（Push/Pop）

### ✔ 自定义 Layer 顺序

### ✔ 多种模糊算法叠加（Hybrid）

### ✔ Idle 渲染优化（UI 静止→降低 FPS）

### ✔ 全局阴影、Bloom 与 DoF 效果

---

# 📜 License

商用自由，可视为 MIT（如需，我可为你写正式 LICENCE 文件）。

