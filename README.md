# Liquid Glass UI - Agent Guidelines

## Overview
This is a Unity URP UI effects package (`com.unlink.liquidglassui`) that provides iOS-style liquid glass UI effects with blur, refraction, and multi-layer UI capture. The package works with Unity 2022.3+ and URP 14.0.0.

## Build & Development Commands

### Unity-Specific Workflow
- **No CLI build system** - This is a pure Unity package
- **Installation**: Use `Tools > LiquidGlassUI > Install` in Unity Editor
- **Testing**: Run the sample scene `Samples~/BasicDemo/Scenes/Sample_LiquidGlass_TwoScreens.unity`
- **Validation**: After installation, verify UI Camera renderer configuration in `UniversalRenderPipelineAsset`

### Package Development
- **Package path**: `Packages/com.unlink.liquidglassui/`
- **Assembly definitions**: Two separate assemblies for Editor and Runtime code
- **Dependencies**: `com.unity.render-pipelines.universal` version 14.0.0
