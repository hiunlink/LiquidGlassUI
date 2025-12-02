# Docs/UIScreenFlow.md

**UIScreenManager – Screen Stack & Lifecycle Flow Documentation**

---

# 目录

1. [整体概念](#整体概念)
2. [UIScreen 生命周期](#uiscreen-生命周期)
3. [UIScreenManager 堆栈流程](#uiscreenmanager-堆栈流程)
4. [Push 流程详解](#push-流程详解)
5. [Pop 流程详解](#pop-流程详解)
6. [Dirty / Capture 更新机制](#dirty--capture-更新机制)
7. [层级可见性与模糊能力](#层级可见性与模糊能力)
8. [淡入淡出动画支持（LowerLayerBlurFade）](#淡入淡出动画支持lowerlayerblurfade)
9. [屏幕状态机（State Machine）](#屏幕状态机state-machine)
10. [推荐工程实践](#推荐工程实践)

---

# 整体概念

你的项目采用 **“UI Stack + 背景捕获 + 液体玻璃折射”** 设计，与 iOS UIKit / Android Fragment Manager 类似。

每个界面（UIScreen）作为一个可推入/弹出的屏幕单元，统一由 UIScreenManager 管理。

优势：

* 屏幕是可管理的独立单元（生命周期、渲染、Capture 决策）
* 支持多层堆叠
* 顶层透明 UI 可以看到下层模糊背景
* 通过 UICaptureAutoDirty 精准触发背景重建（性能最优）

---

# UIScreen 生命周期

每个 UIScreen 都包含统一生命周期函数：

```
OnEnter()    // 首次进入 / 创建
OnExit()     // 最终离开 / 销毁
OnPause()    // 被更上层的屏幕覆盖（不销毁）
OnResume()   // 再次成为顶层屏幕
```

行为规则：

| 函数           | 触发时机                 | 作用          |
| ------------ | -------------------- | ----------- |
| `OnEnter()`  | Push 第一次显示           | 创建UI、绑定事件   |
| `OnExit()`   | Pop 彻底移除             | 清理 UI、解绑事件  |
| `OnPause()`  | 新屏幕 Push 覆盖当前屏幕      | 停止输入响应，但仍可见 |
| `OnResume()` | 上层屏幕被 Pop 掉，当前重新成为顶层 | 恢复可交互状态     |

UIKit/Android/Unity 开发者非常熟悉此模式。

---

# UIScreenManager 堆栈流程

UIScreenManager 维护一个栈：

```
┌─────────┐ ← Top
│ Screen3 │
├─────────┤
│ Screen2 │
├─────────┤
│ Screen1 │
└─────────┘ ← Bottom
```

规则：

1. **只有栈顶屏幕接收输入**
2. 底层屏幕仍然可渲染（用于 LiquidGlass 背景）
3. 栈顶变化时需要触发 Capture Dirty

---

# Push 流程详解

当你调用：

```csharp
UIScreenManager.Push(newScreen);
```

流程为：

```
[当前顶层 ScreenA]  → OnPause()
        ↓
[new ScreenB]       → OnEnter()
        ↓
UIScreenManager 标记 CaptureDirty
        ↓
UICaptureEffectManager 重新捕获背景
        ↓
ScreenB 成为可交互顶层
```

推入操作特点：

* 下层屏幕没有被销毁
* ScreenA 在背景仍然可见
* ScreenB 根据透明度可看到 ScreenA 模糊后的内容

---

# Pop 流程详解

当你调用：

```csharp
UIScreenManager.Pop();
```

流程为：

```
[当前顶层 ScreenB] → OnExit()
         ↓ (从栈移除)
[新的顶层 ScreenA] → OnResume()
         ↓
CaptureDirty = true
         ↓
重建背景（ScreenA 重新决定模糊能力）
```

弹出操作特点：

* 下层屏幕被“揭开”
* ScreenA 的状态恢复交互
* 下层是否需要模糊由它自身配置与 Fade 状态决定

---

# Dirty / Capture 更新机制

UI 有变化 → 必须触发背景重建：

```csharp
UICaptureAutoDirty.SetDirty();
```

会影响 `_UI_BG` 的下一帧更新。

以下情况都会触发：

* Push 新屏幕
* Pop 屏幕
* 屏幕透明度改变
* LowerLayerBlurFade 变化
* UIScreen 本身内容变化（例如 ScrollRect）
* 场景内渲染内容变化（取决于你的 layer 设计）

Dirty 会自动降频、自动优化，避免每帧重复做 GPU 工作。

---

# 层级可见性与模糊能力

总结你当前的规则：

| 屏幕状态  | 是否渲染         | 是否参与 Blur 背景      |
| ----- | ------------ | ----------------- |
| 顶层屏幕  | 渲染、接受输入      | 不参与（作为前景）         |
| 下层屏幕  | 渲染           | 可能参与（根据 Fade 透明度） |
| 被完全遮挡 | 可能跳过渲染（可选优化） | 不参与               |

Fade 透明度直接影响背景模糊能否采集：

* `alpha = 1` → 此屏幕是完全前景，不应采集下层
* `alpha < 1` → 下层需要显示（可能加 blur）

---

# 淡入淡出动画支持（LowerLayerBlurFade）

`LowerLayerBlurFade.cs` 用于处理界面显示/隐藏时的 Blur 过渡：

* 当屏幕从透明变得更透明 → 下层需要增加模糊
* 当屏幕完全显示 → 不需要模糊
* Fade 动画期间会持续触发 dirty
* 提供“可见度曲线”影响 capture 配置

此特性使你的 UI 看起来 **平滑、有立体感、真实玻璃折射感**。

---

# 屏幕状态机（State Machine）

以下是你的工程中 UIScreen 的最终状态机：

```
                   ┌──────────────────┐
        Push       │                  │
   ┌──────────────▶│     OnEnter      │
   │               │   (Active Top)   │
   │               └─────────▲────────┘
   │                         │
   │ Pop                     │ Resume
   │               ┌─────────┴────────┐
   │               │                  │
   └───────────────│     OnPause      │
                   │   (Visible BG)   │
        Push       └─────────▲────────┘
                             │
                             │ Pop
                             │
                   ┌─────────┴────────┐
                   │                  │
                   │     OnExit       │
                   │   (Destroyed)    │
                   └──────────────────┘
```

说明：

* OnPause → 不可交互，但仍可渲染（用于模糊背景）
* OnResume → 重新变为顶层屏幕
* OnExit → 最终销毁

此模型非常利于维持多层玻璃效果。

---

# 推荐工程实践

### ✔ 1. 一律通过 UIScreenManager 管理界面

避免直接 SetActive，保证生命周期正确触发。

### ✔ 2. 在任何 UI 变化时调用 SetDirty()

特别是：

* 淡入淡出
* ScrollRect 滚动
* 动画改变布局

### ✔ 3. 优先使用 MipChain

多数设备上质量足够、耗电更低。

### ✔ 4. 屏幕尽量分层（前景/内容/背景）

可充分利用 stencil 加速与遮挡合成。

### ✔ 5. 把 LiquidGlassUIEffect 用于：

* Dialog
* 半透明面板
* Floating Card
  不要用于大量小物件（性能浪费）。

