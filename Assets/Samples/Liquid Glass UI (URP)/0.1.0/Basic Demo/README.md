# Liquid Glass UI (URP)

A reusable Unity Package providing:

- URP ScriptableRendererFeature for UI capture + blur composition
- Liquid Glass UI shader (SDF rounded-rect refraction)
- UIScreen stack helpers (optional)
- Editor utilities + Sample scene

## Install

Using Git URL with path:

```json
{
  "dependencies": {
    "com.unlink.liquidglassui": "https://github.com/<you>/<repo>.git?path=/Packages/com.yourcompany.liquid-glass-ui#v0.1.0"
  }
}
````

## Quick Start

1. Add `UICaptureComposePerLayerFeature` to your URP RendererData.
2. Make sure `_UI_BG` and `_UIBG_UVScale` are published (feature does this).
3. Add `LiquidGlassUIEffect` to any `Image` / `RawImage` / `Text` etc.
4. Ensure default material exists: `Resources/DefaultMaterials/UI_LiquidGlass_Default.mat`.

## Docs

* `Documentation~/RenderFlow_Detailed.md`
* `Documentation~/RenderFlow_Short.md`
* `Documentation~/UIScreenFlow.md`

## Samples

Import `Basic Demo` from Package Manager.

