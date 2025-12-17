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

### 2️⃣ 一键初始化（非常重要）

执行菜单：

```

Tools > LiquidGlassUI > Install

```

这个操作会自动：

- 配置 URP（无需手动改 Renderer）
- 创建必要的 Settings
- 设置 UI 渲染层级
- 创建管理对象

**只需要执行一次**（可重复执行，不会破坏配置）。

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
* 上层可选择性地 **模糊下层 UIScreen 的指定 Canvas**

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



