# Liquid Glass UI – Developer Guide

## 目标读者

本文件面向需要：

- 扩展 Liquid Glass UI
- 理解多层 UI Capture 原理
- 修改 RendererFeature / Layer 策略
- 做性能与架构优化

---

## 核心架构概览

```text
UIScreenManager
    ↓
UIScreen Stack
    ↓
UICaptureComposePerLayerFeature (URP)
    ↓
_UI_BG / _UI_BG_BLUR
    ↓
LiquidGlassUIEffect (Shader)
````

---

## 为什么需要多层 Capture

设计约束：

1. 某些 UI 层 **需要模糊**
2. 某些 UI 控件（LiquidGlass）**需要读取下层**
3. 不同 UI 层 **模糊次数不同**

结论：

> **必须按 UI 层级逐层 Capture 并缓存**

任何“合并 Capture”的方案都会导致视觉错误。

---

## UICaptureComposePerLayerFeature

职责：

* 按 Layer 顺序渲染 UI
* 生成 Base RT / Blur RT
* 支持多种 Blur 算法
* 向 Shader 发布 `_UI_BG`

⚠️ Feature **必须安装在 UI Camera 使用的 RendererData 上**。

---

## RendererData 绑定策略

Install 菜单会：

1. 查找 ScreenSpace-Camera Canvas
2. 反射读取 `UniversalAdditionalCameraData.m_RendererIndex`
3. 修改对应 RendererData：

* 添加 CaptureFeature
* 从 Transparent Layer Mask 中剔除 UI Layer

---

## Layer 使用规则

由 `LiquidGlassSettings` 统一管理：

* `layerStart ~ layerEnd`：UI Capture 专用
* `hiddenLayer`：内部中转层

默认 URP **不渲染这些 Layer**，避免重复绘制。

---

## 性能策略

### Capture Dirty 机制

* UI 变化 → 标记 Dirty
* 静止 UI → Capture FPS 降低（如 5~10fps）

### 推荐优化

* 仅最上层 UIScreen 实时 Capture
* 下层 Screen 使用缓存 RT
* 弹窗关闭时不重建 RT

---

## 扩展建议

* 多 UI Camera 支持
* 混合 Blur（Mip + Gaussian）
* GPU Driven UI Capture
* Runtime 切换 RendererData

---

## 文档索引

* 渲染流程（详细）：`RenderFlow_Detailed.md`
* UIScreen 状态机：`UIScreenFlow.md`



