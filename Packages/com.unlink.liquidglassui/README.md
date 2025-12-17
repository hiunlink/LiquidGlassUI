# 🍏 Liquid Glass UI (URP)

<p align="center">
  <img src="Documentation~/Images/LiquidGlass_Cover.png" width="820" />
</p>

<p align="center">
  高质量 iOS 风格 Liquid Glass UI 系统（Unity URP）
</p>

---

## 这是什么？

**Liquid Glass UI** 是一个可直接使用的 UI 效果系统，用于实现：

- 毛玻璃 / 液态玻璃 UI
- 弹窗、模态框、卡片的真实折射效果
- 多层 UI 叠加 + 模糊 + 折射

你**不需要了解 URP / RenderFeature / Layer 细节**，  
只需要按步骤安装并在 UI 上使用组件即可。

---

## ✨ 能做什么

- UI 背景模糊（自动）
- UI 折射（RGB 色散）
- 圆角玻璃效果
- Edge 高光
- 多层弹窗正确显示

---

## 📦 安装（一次即可）

### 1️⃣ 导入 Package

**Window → Package Manager → Add package from disk**  
选择 `com.unlink.liquidglassui/package.json`

---

## ⚠️ 一键初始化前置条件（重要）

在执行 **`Tools > LiquidGlassUI > Install`** 之前，请确保以下条件满足。
这些条件是为了保证 **UI Capture 与 SceneView 显示互不干扰**，也是本系统设计的一部分。

---

### 1️⃣ 场景中必须存在 UI Camera

* 至少存在一个 **Screen Space – Camera** 的 `Canvas`
* 该 Canvas 必须正确设置：

  * `Render Mode` = **Screen Space – Camera**
  * `World Camera` = **UICamera**

📌 Install 菜单会通过 Canvas 自动查找 UI Camera，如果场景中不存在，将无法完成初始化。

---

### 2️⃣ URP Asset 需要配置两个 Renderer

在 **Universal Render Pipeline Asset** 中，需要准备 **两个 Renderer**：

| Renderer             | 用途                                         |
| -------------------- | ------------------------------------------ |
| **Default Renderer** | 用于 SceneView / 普通 Camera 显示                |
| **UI Renderer**      | 专门供 UI Camera 使用，承载 LiquidGlass UI Capture |

> 这是为了避免 UI Capture 影响 SceneView 显示，同时保持编辑器与运行时表现一致。

---

### 3️⃣ 一键 Install 会自动完成的配置

当你执行：

```
Tools > LiquidGlassUI > Install
```

系统会自动：

#### ✅ UI Camera Renderer

* 找到 UI Camera 实际使用的 Renderer（反射 `m_RendererIndex`）
* 在该 Renderer 上：

  * 自动添加 `UICaptureComposePerLayerFeature`
  * 从 **Transparent Layer Mask** 中剔除 LiquidGlass UI 使用的 Layer
  * 防止默认管线重复渲染 UI

#### ✅ Default Renderer（SceneView / 普通 Camera）

* **不会**添加 CaptureFeature
* 保持默认渲染行为
* 确保 SceneView 正常显示 UI（不被 Capture 逻辑影响）

#### ✅ 场景与配置

* 创建或复用 `UICaptureEffectManager`
* 创建用户可编辑的 `LiquidGlassSettings.asset`
* 自动绑定 Settings → Feature → Manager

整个流程是 **幂等的**，可以安全重复执行。

---

### 4️⃣ 为什么需要两个 Renderer？

这是一个**刻意的设计决策**：

* SceneView 使用默认 Renderer

  * 避免 Capture Pass 影响编辑体验
* UI Camera 使用专用 Renderer

  * 只在运行时对 UI Layer 执行 Capture
  * 精确控制 Layer Mask 与渲染顺序

📌 如果 UI Camera 和 SceneView 共用同一个 Renderer，会导致：

* SceneView UI 显示异常
* 重复渲染
* Debug 与实际效果不一致

---

### ✅ 推荐配置示意

```text
URP Asset
 ├── Renderer 0 : DefaultRenderer
 │    └──（无 LiquidGlass Feature）
 │
 └── Renderer 1 : UIRenderer
      └── UICaptureComposePerLayerFeature
```

```text
Camera
 ├── Main Camera
 │    └── Renderer = DefaultRenderer
 │
 └── UI Camera
      └── Renderer = UIRenderer
```

---

### 🚀 完成以上条件后

即可安全执行：

```
Tools > LiquidGlassUI > Install
```

并开始使用 Liquid Glass UI 系统。

---

## 🧩 使用方法

### 使用 Liquid Glass UI

在任何 `Image / RawImage` 上：

```

Add Component → LiquidGlassUIEffect

````

你可以直接调整：
- 圆角半径
- 折射强度
- Tint
- Edge 强度

无需指定背景贴图，系统会自动处理。

---

### UI 结构建议（简单）

```text
UIScreen
 ├── Canvas（背景，可模糊）
 ├── Canvas（内容）
 └── Canvas（弹窗 / Glass）
````

> 你不需要手动管理 Layer 或 Render 顺序。

---

## 🧪 Samples 示例（Two UIScreen · 下层被模糊）

本 Package 内置的 Sample 场景演示了 **两个 UIScreen 叠加** 的典型用法：

* **下层 UIScreen：内容展示层**
* **上层 UIScreen：弹窗 / Glass 层**
* 上层可选择性地 **模糊下层 UIScreen **

---

## 📂 示例路径

```text
Assets/Samples/Liquid Glass UI/Basic/
└── Scenes/
    └── Sample_LiquidGlass_TwoScreens.unity
```

---

## 🎬 场景整体结构

```text
UIScreen (UILayer)      ← 下层界面
 ├── BgCanvas
 ├── CharacterCanvas
 ├── HeadImage
 └── UILayer (普通 UI)

UIScreen (UITop)        ← 上层界面
 └── UITop Canvas
      ├── Button
      └── Image (LiquidGlass)
```

* **UILayer**：主内容界面（Layer = `UI_BG3`）
* **UITop**：弹窗 / Glass 界面（Layer = `UI_BG4`）

---

## 🔹 下层 UIScreen（UILayer）配置说明

> 该 UIScreen **不启用下层模糊**，只作为被采样对象。

### Inspector 关键配置

* **Lower Blur**：❌ 关闭
* **Canvas Configs（共 4 个）**

| Canvas          | Is Foreground | Blur | 说明           |
| --------------- | ------------- | ---- | ------------ |
| BgCanvas        | ❌             | ✅    | 背景画面，可被模糊    |
| CharacterCanvas | ✅             | ❌    | 人物主体（前景，不模糊） |
| HeadImage       | ❌             | ❌    | 装饰元素         |
| UILayer         | ✅             | ❌    | 普通 UI 层      |

📌 说明：

* `Is Foreground` 用于区分 **前景 / 背景合成顺序**
* `Blur` 表示 **该 Canvas 会被写入 Blur RT**
* 下层 UIScreen 的 Blur 只在 **被上层引用时才生效**

---

## 🔹 上层 UIScreen（UITop）配置说明

> 该 UIScreen **开启 Lower Blur**，用于模糊下层 UIScreen。

### Inspector 关键配置

* **Lower Blur**：✅ 开启
* **Lower Blur Strength**：`0.573`
* **Blur Algorithm**：`Gaussian Separable`
* **Gaussian Sigma**：`4.87`
* **Iteration**：`1`

### Lower Canvas Blur Config

```text
Canvas: UITop (Canvas)
Is Foreground: ❌
Blur: ❌
```

📌 说明：

* 上层本身 **不参与模糊**
* 它的作用是：

    * 触发下层 Capture
    * 将下层内容写入 `_UI_BG`
    * 供 LiquidGlassUIEffect 采样

---

## ✨ 实际运行时效果

运行场景后你可以观察到：

* 上层 `UITop` 中的 Glass Image：

    * **实时模糊下层 UILayer**
    * 折射背景人物与 UI
* 下层人物（CharacterCanvas）：

    * 因为标记为 `Is Foreground`
    * 不会被错误模糊
* 下层 UI 静止时：

    * Capture 自动降频（性能优化）

---

## 🧠 这个 Sample 演示了什么关键能力？

✔ 多 UIScreen 分层管理
✔ 上层界面模糊 **指定的下层 Canvas**
✔ 前景 / 背景正确分离
✔ Gaussian Blur 在 UI Capture 中的实际用法
✔ Liquid Glass 与 Capture 系统的协作方式

---

## ⚠️ 使用这个 Sample 时的注意点

* ❌ **不是全屏后处理模糊**
* ❌ **不是 Camera Stack**
* ✅ 是 **UIScreen → Canvas → Layer 精确控制的 UI Capture**

如果你想复刻该效果，只需：

1. 拷贝 UIScreen 结构
2. 正确设置 Canvas Configs
3. 在上层 UI 添加 `LiquidGlassUIEffect`

---

## 🧠 常见问题

### Q：会影响性能吗？

* UI 不变化时几乎没有额外消耗
* 支持低频 Capture（自动）
* 已为多界面叠加优化

### Q：可以用在正式项目吗？

可以，本项目已按 **可复用 Package** 设计。

---

## 📄 更多文档

* 👉 **开发者文档**：`Documentation~/README_Developer.md`
* 👉 渲染流程（详细）：`Documentation~/RenderFlow_Detailed.md`
* 👉 UIScreen 状态机：`Documentation~/UIScreenFlow.md`

---



