#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unlink.LiquidGlassUI.Editor
{
    public static class LiquidGlassPackageSetup
    {
        // ====== Paths ======
        private const string UserSettingsDir  = "Assets/ThirdParty/LiquidGlassUI/Settings";
        private const string UserSettingsPath = "Assets/ThirdParty/LiquidGlassUI/Settings/LiquidGlassSettings.asset";

        // 可选：你可以在 package 里放一份默认 asset 用于复制
        private const string PackageDefaultSettingsPath =
            "Packages/com.unlink.liquidglassui/Resources/DefaultSettings/LiquidGlassSettings_Default.asset";

        private const string BackupKeyPrefix = "Unlink.LiquidGlassUI.TransparentMaskBackup.";
        private struct InstallContext
        {
            public UniversalRenderPipelineAsset urp;
            public Camera uiCam;
            public UniversalAdditionalCameraData camData;
            public int uiRendererIndex;
            public ScriptableRendererData uiRendererData;
            public ScriptableRendererData defaultRendererData;
            public LiquidGlassSettings settings;
        }

        private static bool ValidateAndRepairBeforeInstall(out InstallContext ctx)
        {
            ctx = default;

            // A) 找 UICamera（从 ScreenSpace-Camera Canvas 来）
            var uiCam = FindUICameraFromCanvas();
            if (uiCam == null)
            {
                EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    "未找到 UI Camera。\n\n请确保场景中至少有一个 Canvas：\n- Render Mode = Screen Space - Camera\n- World Camera 指向 UICamera",
                    "OK"
                );
                return false;
            }

            // B) UICamera 必须有 UniversalAdditionalCameraData
            if (!uiCam.TryGetComponent(out UniversalAdditionalCameraData camData))
            {
                EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    $"Camera '{uiCam.name}' 缺少 UniversalAdditionalCameraData。\n\n请确认该 Camera 使用 URP，并已添加 Universal Additional Camera Data 组件。",
                    "OK"
                );
                Selection.activeObject = uiCam.gameObject;
                EditorGUIUtility.PingObject(uiCam.gameObject);
                return false;
            }

            // C) URP asset 必须存在
            var urp = UniversalRenderPipeline.asset;
            if (urp == null)
            {
                EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    "未找到 UniversalRenderPipeline.asset。\n\n请到 Project Settings > Graphics / Quality 设置 URP Asset。",
                    "OK"
                );
                return false;
            }

            // D) 读取 renderer list，检查是否至少 2 个
            int rendererCount = GetRendererCount(urp);
            if (rendererCount < 2)
            {
                EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    "URP Asset 需要至少配置两个 Renderer。\n\n建议：\n- Renderer 0：DefaultRenderer（用于 Main/SceneView）\n- Renderer 1：UIRenderer（供 UICamera 使用）\n\n请在 URP Asset 的 Renderer List 中新增一个 RendererData。",
                    "OK"
                );

                Selection.activeObject = urp;
                EditorGUIUtility.PingObject(urp);
                return false;
            }

            // E) 读取 UICamera 的 rendererIndex（反射 m_RendererIndex）
            int uiRendererIndex = GetRendererIndex(camData);
            if (uiRendererIndex < 0 || uiRendererIndex >= rendererCount)
            {
                EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    $"UICamera 的 Renderer Index = {uiRendererIndex} 不合法（Renderer 总数 = {rendererCount}）。\n\n请在 UICamera 的 UniversalAdditionalCameraData 中选择正确的 Renderer。",
                    "OK"
                );
                Selection.activeObject = uiCam.gameObject;
                EditorGUIUtility.PingObject(uiCam.gameObject);
                return false;
            }

            // F) 拿到 Default / UI RendererData
            int sceneRendererIndex = GetDefaultRendererIndex(urp);
            var defaultRD = GetRendererDataByIndex(urp, sceneRendererIndex);
            var uiRD = GetRendererDataByIndex(urp, uiRendererIndex);

            if (defaultRD == null || uiRD == null)
            {
                EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    "无法从 URP Asset 解析 RendererData。\n\n可能原因：URP 版本差异或 RendererDataList 为空。",
                    "OK"
                );
                return false;
            }

            // G) 建议：UIRenderer != DefaultRenderer（给 Warning，不阻断）
            if (uiRendererIndex == 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    "检测到 UICamera 正在使用 Renderer 0（Default Renderer）。\n\n建议：UICamera 使用独立的 UIRenderer（例如 Renderer 1），以避免 SceneView/普通相机受到 CaptureFeature 影响。\n\n是否仍要继续安装（将把 CaptureFeature 安装在 Renderer 0 上）？",
                    "继续安装",
                    "取消"
                );

                if (!proceed) return false;
            }

            // H) 确保 Settings（可自动修）
            var settings = EnsureUserSettingsAsset();
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    "无法创建/加载 LiquidGlassSettings.asset。",
                    "OK"
                );
                return false;
            }
            
            // ✅ NEW: validate layers exist in TagManager
            if (!ValidateLayersConfigured(settings, out var layerErrorMsg))
            {
                bool autoFix = EditorUtility.DisplayDialog(
                    "LiquidGlassUI Install",
                    layerErrorMsg + "\n\n是否自动为这些 Layer 填入默认名称？",
                    "自动修复",
                    "取消"
                );

                if (!autoFix)
                {
                    SettingsService.OpenProjectSettings("Project/Tags and Layers");
                    return false;
                }

                AutoConfigureLayers(settings);
            }
            
            // I) 自动修：把保留层从 UI Renderer 的 Transparent Mask 剔除
            int reservedMask = BuildReservedMask(settings.layerStart, settings.layerEnd, settings.hiddenLayer);
            bool maskChanged = ExcludeMaskFromTransparent(uiRD, reservedMask, backupKey: BackupKeyPrefix + uiRD.name);

            // J) 建议检查：默认 Renderer 上是否已经装了 CaptureFeature（提示，不自动删）
            var wrong = FindRendererFeature<UICaptureComposePerLayerFeature>(defaultRD);
            if (wrong != null && uiRD != defaultRD)
            {
                Debug.LogWarning("[LiquidGlassUI] 检测到 DefaultRenderer 上存在 UICaptureComposePerLayerFeature。建议移除，避免 SceneView/普通相机被影响。");
            }

            if (maskChanged)
                Debug.Log("[LiquidGlassUI] 已自动更新 UI Renderer Transparent Layer Mask（剔除保留 UI Layers）。");

            ctx = new InstallContext
            {
                urp = urp,
                uiCam = uiCam,
                camData = camData,
                uiRendererIndex = uiRendererIndex,
                uiRendererData = uiRD,
                defaultRendererData = defaultRD,
                settings = settings
            };
            return true;
        }
        
        private static bool ValidateLayersConfigured(LiquidGlassSettings s, out string msg)
        {
            msg = null;

            if (!ValidateLayerRange(s.layerStart, s.layerEnd, s.hiddenLayer, out msg))
                return false;

            // 收集需要存在的 layer slots
            var missing = new System.Collections.Generic.List<int>();

            for (int i = s.layerStart; i <= s.layerEnd; i++)
            {
                if (!IsUserLayerSlotConfigured(i))
                    missing.Add(i);
            }

            if (s.hiddenLayer >= 0 && s.hiddenLayer <= 31)
            {
                if (!IsUserLayerSlotConfigured(s.hiddenLayer))
                    missing.Add(s.hiddenLayer);
            }

            if (missing.Count > 0)
            {
                msg =
                    "LiquidGlassSettings 中指定的 Layer 尚未在 Project Settings > Tags and Layers 配置完成。\n\n" +
                    "请先为以下 Layer Index 填写名称（不能留空）：\n" +
                    $"- {string.Join(", ", missing)}\n\n" +
                    "建议命名规则：\n" +
                    $"- UI_BG{missing[0]} ...（按你的项目习惯）\n\n" +
                    "已为你打开 Tags and Layers 设置页。";
                return false;
            }

            return true;
        }

        private static bool ValidateLayerRange(int layerStart, int layerEnd, int hiddenLayer, out string msg)
        {
            msg = null;

            if (layerStart < 0 || layerStart > 31 || layerEnd < 0 || layerEnd > 31 || hiddenLayer < 0 || hiddenLayer > 31)
            {
                msg =
                    "LiquidGlassSettings 的 Layer 配置不合法：\n\n" +
                    $"- layerStart = {layerStart}\n" +
                    $"- layerEnd   = {layerEnd}\n" +
                    $"- hiddenLayer= {hiddenLayer}\n\n" +
                    "Layer 必须在 0..31 范围内。";
                return false;
            }

            if (layerEnd < layerStart)
            {
                msg =
                    "LiquidGlassSettings 的 Layer 配置不合法：\n\n" +
                    $"layerEnd ({layerEnd}) 必须 >= layerStart ({layerStart})。";
                return false;
            }

            // 0..7 是 Unity 内建层，建议不要让用户用（给 warning，不阻断也可以）
            if (layerStart < 8)
            {
                Debug.LogWarning("[LiquidGlassUI] layerStart < 8：建议使用用户层（8..31），避免占用 Unity 内建 Layer。");
            }

            return true;
        }

        /// <summary>
        /// Unity 的用户 Layer 名称存于 TagManager 的 layers[8..31]，
        /// 如果该 slot 为空字符串，说明未配置。
        /// </summary>
        private static bool IsUserLayerSlotConfigured(int layerIndex)
        {
            // 内建 0..7 我们视为已存在（虽然不建议用）
            if (layerIndex < 8) return true;

            var so = new UnityEditor.SerializedObject(
                UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

            var layersProp = so.FindProperty("layers");
            if (layersProp == null || !layersProp.isArray) return false;

            if (layerIndex < 0 || layerIndex >= layersProp.arraySize) return false;

            var p = layersProp.GetArrayElementAtIndex(layerIndex);
            string name = p.stringValue;
            return !string.IsNullOrEmpty(name);
        }
        
        private static void AutoConfigureLayers(LiquidGlassSettings s)
        {
            var obj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var so = new SerializedObject(obj);
            var layersProp = so.FindProperty("layers");

            void SetLayerName(int idx, string name)
            {
                if (idx < 8 || idx >= layersProp.arraySize) return; // 只改用户层
                var p = layersProp.GetArrayElementAtIndex(idx);
                if (string.IsNullOrEmpty(p.stringValue))
                    p.stringValue = name;
            }

            for (int i = s.layerStart; i <= s.layerEnd; i++)
                SetLayerName(i, $"UI_BG{i}");

            SetLayerName(s.hiddenLayer, $"UI_Hidden{s.hiddenLayer}");

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log("[LiquidGlassUI] 已自动配置 Tags and Layers（填入默认 Layer 名称）。");
        }

        private static int GetRendererCount(UniversalRenderPipelineAsset urp)
        {
            var so = new SerializedObject(urp);

            var pList = so.FindProperty("m_RendererDataList");
            if (pList != null && pList.isArray)
                return pList.arraySize;

            // 旧版单 renderer
            var pSingle = so.FindProperty("m_RendererData");
            return pSingle != null && pSingle.objectReferenceValue != null ? 1 : 0;
        }
        
        [MenuItem("Tools/LiquidGlassUI/Validate")]
        public static void Validate()
        {
            if (ValidateAndRepairBeforeInstall(out _))
                EditorUtility.DisplayDialog("LiquidGlassUI Validate", "检查通过 ✅", "OK");
        }
    
        [MenuItem("Tools/LiquidGlassUI/Install")]
        public static void Install()
        {
            // ctx 已包含：urp、uiCam、camData、uiRendererIndex、uiRendererData、settings 等
            // 后续安装逻辑都用 ctx.uiRendererData，不要再用 renderer 0
            if (!ValidateAndRepairBeforeInstall(out var ctx))
                return;
            
            // 确保 CaptureFeature, ReplaceFeature 存在（装到 UI RendererData 上）
            var captureFeature = EnsureRendererFeature<UICaptureComposePerLayerFeature>(
                ctx.uiRendererData, "UICaptureComposePerLayerFeature");

            if (captureFeature == null)
            {
                Debug.LogError("[LiquidGlassUI] Failed to create/find UICaptureComposePerLayerFeature on UI RendererData.");
                return;
            }
            var replaceFeature = EnsureRendererFeature<UIBGReplaceFeature>(
                ctx.uiRendererData, "UIBGReplaceFeature");

            if (replaceFeature == null)
            {
                Debug.LogError("[LiquidGlassUI] Failed to create/find UIBGReplaceFeature on UI RendererData.");
                return;
            }

            // 绑定 Settings 到 Feature
            // 绑定 replaceMaterial 
            if (ctx.settings != null)
                BindSettingsToFeature(captureFeature, ctx.settings);
            var asset = AssetDatabase.LoadAssetAtPath<LiquidGlassSettings>(UserSettingsPath);
            if (asset != null && asset.replaceMat != null)
                BindMatToReplaceFeature(replaceFeature, asset.replaceMat);
            
            // 确保场景有 Manager，并绑定 captureFeature, replaceFeature
            var mgr = EnsureManagerInScene();
            if (mgr != null)
            {
                BindManagerFeature(mgr, captureFeature);
                BindManagerFeature(mgr, replaceFeature);
            }
            FinalizeAssets(ctx.uiRendererData);
            
            // 加入 UICaptureSceneFeature 到 SceneViewRenderer
           if (ctx.defaultRendererData == null)
            {
                Debug.LogError($"[LiquidGlassUI] RendererData not found for scene camera.");
                return;
            }
            var sceneFeature = EnsureRendererFeature<UICaptureSceneFeature>(ctx.defaultRendererData, "UICaptureSceneFeature");
            if (sceneFeature == null)
            {
                Debug.LogError("[LiquidGlassUI] Failed to create/find UICaptureSceneFeature on Scene RendererData.");
                return;
            }
            sceneFeature.captureFeature = captureFeature;
            
            Debug.Log(
                "[LiquidGlassUI] Install completed.\n" +
                $"- UICamera: {ctx.uiCam.name}\n" +
                $"- UIRendererIndex: {ctx.uiRendererIndex}\n" +
                $"- UIRendererData: {ctx.uiRendererData.name}\n" +
                $"- CaptureFeature: {captureFeature.name}\n" +
                $"- Settings: {(ctx.settings ? AssetDatabase.GetAssetPath(ctx.settings) : "(none)")}\n"
            );
        }

        // ---------------------------
        // Settings asset management
        // ---------------------------
        private static LiquidGlassSettings EnsureUserSettingsAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<LiquidGlassSettings>(UserSettingsPath);
            if (asset != null) return asset;

            if (!Directory.Exists(UserSettingsDir))
                Directory.CreateDirectory(UserSettingsDir);

            // 优先复制 package 默认 asset（如果你有放）
            if (File.Exists(PackageDefaultSettingsPath))
            {
                AssetDatabase.CopyAsset(PackageDefaultSettingsPath, UserSettingsPath);
                AssetDatabase.ImportAsset(UserSettingsPath);
                asset = AssetDatabase.LoadAssetAtPath<LiquidGlassSettings>(UserSettingsPath);
                if (asset != null) return asset;
            }
            // fallback：绝对路径 File.Copy（更稳定）
            var srcAbs = ToAbsolutePath(PackageDefaultSettingsPath);
            var dstAbs = ToAbsolutePath(UserSettingsPath);

            if (File.Exists(srcAbs))
            {
                try
                {
                    File.Copy(srcAbs, dstAbs, overwrite: true);
                    // meta 不拷也行，让 Unity 生成新的；如果你要稳定 GUID 才需要拷 meta
                    AssetDatabase.ImportAsset(UserSettingsPath, ImportAssetOptions.ForceUpdate);
                    var copied = AssetDatabase.LoadAssetAtPath<LiquidGlassSettings>(UserSettingsPath);
                    if (copied != null) return copied;
                }
                catch (IOException e)
                {
                    Debug.LogWarning($"[LiquidGlassUI] File.Copy failed: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[LiquidGlassUI] Source asset not found on disk: {srcAbs}");
            }

            // fallback：没有默认 asset 就直接创建
            asset = ScriptableObject.CreateInstance<LiquidGlassSettings>();
            AssetDatabase.CreateAsset(asset, UserSettingsPath);
            AssetDatabase.ImportAsset(UserSettingsPath);
            return asset;
        }
        /// <summary>
        /// 把 "Assets/.." 或 "Packages/.." 转成磁盘绝对路径
        /// </summary>
        private static string ToAbsolutePath(string projectRelativePath)
        {
            // Application.dataPath = ".../<Project>/Assets"
            // Project root = parent of Assets
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var abs = Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
            return abs.Replace('\\', '/');
        }

        // ---------------------------
        // Find UI camera
        // ---------------------------
        private static Camera FindUICameraFromCanvas()
        {
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (c == null) continue;
                if (c.renderMode != RenderMode.ScreenSpaceCamera) continue;
                if (c.worldCamera != null) return c.worldCamera;
            }
            return null;
        }

        // ---------------------------
        // Read renderer index (reflection)
        // ---------------------------
        private static int GetRendererIndex(UniversalAdditionalCameraData camData)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 新版本可能有 property rendererIndex
            var prop = typeof(UniversalAdditionalCameraData).GetProperty("rendererIndex", flags);
            if (prop != null && prop.PropertyType == typeof(int))
            {
                try { return (int)prop.GetValue(camData); } catch { /* ignore */ }
            }

            // 你当前版本：私有字段 m_RendererIndex
            var field = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", flags);
            if (field != null && field.FieldType == typeof(int))
            {
                try { return (int)field.GetValue(camData); } catch { /* ignore */ }
            }

            return 0;
        }

        private static int GetDefaultRendererIndex(UniversalRenderPipelineAsset urp)
        {
            var so = new SerializedObject(urp);
            var p = so.FindProperty("m_DefaultRendererIndex");
            if (p != null)
                return p.intValue;
            return -1;
        }

        // ---------------------------
        // URP Asset -> RendererData by index (version tolerant)
        // ---------------------------
        private static ScriptableRendererData GetRendererDataByIndex(UniversalRenderPipelineAsset urp, int index)
        {
            var so = new SerializedObject(urp);

            var pList = so.FindProperty("m_RendererDataList");
            if (pList != null && pList.isArray && pList.arraySize > 0)
            {
                index = Mathf.Clamp(index, 0, pList.arraySize - 1);
                return pList.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
            }

            var pSingle = so.FindProperty("m_RendererData");
            if (pSingle != null)
                return pSingle.objectReferenceValue as ScriptableRendererData;

            return null;
        }

        // ---------------------------
        // Ensure RendererFeature exists on RendererData
        // ---------------------------
        private static T EnsureRendererFeature<T>(ScriptableRendererData rendererData, string name)
            where T : ScriptableRendererFeature
        {
            var existing = FindRendererFeature<T>(rendererData);
            if (existing != null) return existing;

            var feature = ScriptableObject.CreateInstance<T>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, rendererData);

            var so = new SerializedObject(rendererData);
            var pFeatures = so.FindProperty("m_RendererFeatures");
            if (pFeatures == null || !pFeatures.isArray)
            {
                Debug.LogError("[LiquidGlassUI] RendererData missing m_RendererFeatures.");
                return null;
            }

            int newIndex = pFeatures.arraySize;
            pFeatures.InsertArrayElementAtIndex(newIndex);
            pFeatures.GetArrayElementAtIndex(newIndex).objectReferenceValue = feature;

            // 某些 URP 版本还有 enable map（有就同步一下）
            var pMap = so.FindProperty("m_RendererFeatureMap");
            if (pMap != null && pMap.isArray)
            {
                int mapIndex = pMap.arraySize;
                pMap.InsertArrayElementAtIndex(mapIndex);
                pMap.GetArrayElementAtIndex(mapIndex).intValue = 1;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            return feature;
        }

        private static T FindRendererFeature<T>(ScriptableRendererData rendererData)
            where T : ScriptableRendererFeature
        {
            var so = new SerializedObject(rendererData);
            var pFeatures = so.FindProperty("m_RendererFeatures");
            if (pFeatures == null || !pFeatures.isArray) return null;

            for (int i = 0; i < pFeatures.arraySize; i++)
            {
                var obj = pFeatures.GetArrayElementAtIndex(i).objectReferenceValue;
                if (obj is T t) return t;
            }
            return null;
        }

        // ---------------------------
        // Bind LiquidGlassSettings to Feature
        // ---------------------------
        private static void BindSettingsToFeature(ScriptableRendererFeature feature, LiquidGlassSettings settings)
        {
            var soF = new SerializedObject(feature);

            // 推荐：feature.settings.config
            var pSettings = soF.FindProperty("settings");
            if (pSettings != null)
            {
                var pConfig = pSettings.FindPropertyRelative("config");
                if (pConfig != null)
                {
                    pConfig.objectReferenceValue = settings;
                    soF.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(feature);
                    return;
                }
            }

            // 兜底：feature.config
            var pConfig2 = soF.FindProperty("config");
            if (pConfig2 != null)
            {
                pConfig2.objectReferenceValue = settings;
                soF.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(feature);
                return;
            }

            Debug.LogWarning("[LiquidGlassUI] Could not bind LiquidGlassSettings. " +
                             "Please expose serialized field 'settings.config' (recommended) or 'config'.");
        }

        private static void BindMatToReplaceFeature(ScriptableRendererFeature feature, Material mat)
        {
            var soF = new SerializedObject(feature);

            // 推荐：feature.settings.replaceMaterial
            var pSettings = soF.FindProperty("settings");
            if (pSettings != null)
            {
                var pMat = pSettings.FindPropertyRelative("replaceMaterial");
                if (pMat != null)
                {
                    pMat.objectReferenceValue = mat;
                    soF.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(feature);
                    return;
                }
            }
            Debug.LogWarning("[LiquidGlassUI] Could not bind replaceMaterial. " +
                             "Please expose serialized field 'settings.replaceMaterial' (recommended) or 'replaceMaterial'.");
        }

        // ---------------------------
        // Exclude reserved layers from Transparent Layer Mask
        // ---------------------------
        private static bool ExcludeMaskFromTransparent(ScriptableRendererData rendererData, int reservedMask, string backupKey)
        {
            var so = new SerializedObject(rendererData);
            var pTrans = so.FindProperty("m_TransparentLayerMask");
            if (pTrans == null)
            {
                Debug.LogWarning("[LiquidGlassUI] RendererData has no m_TransparentLayerMask (URP version mismatch).");
                return false;
            }

            int oldMask = pTrans.intValue;
            EditorPrefs.SetInt(backupKey, oldMask);

            int newMask = oldMask & ~reservedMask;
            if (newMask == oldMask) return false;

            pTrans.intValue = newMask;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static int BuildReservedMask(int start, int end, int hidden)
        {
            start = Mathf.Clamp(start, 0, 31);
            end   = Mathf.Clamp(end, 0, 31);
            if (end < start) (start, end) = (end, start);

            int m = 0;
            for (int i = start; i <= end; i++) m |= (1 << i);
            if (hidden >= 0 && hidden <= 31) m |= (1 << hidden);
            return m;
        }

        // ---------------------------
        // Ensure manager exists in scene & bind
        // ---------------------------
        private static UICaptureEffectManager EnsureManagerInScene()
        {
            var mgr = Object.FindFirstObjectByType<UICaptureEffectManager>(FindObjectsInactive.Include);
            if (mgr != null)
            {
                Selection.activeObject = mgr.gameObject;
                EditorGUIUtility.PingObject(mgr.gameObject);
                return mgr;
            }

            var go = new GameObject(nameof(UICaptureEffectManager));
            Undo.RegisterCreatedObjectUndo(go, "Create UICaptureEffectManager");
            mgr = go.AddComponent<UICaptureEffectManager>();

            Selection.activeObject = go;
            EditorGUIUtility.PingObject(go);
            return mgr;
        }

        private static void BindManagerFeature(UICaptureEffectManager mgr, UICaptureComposePerLayerFeature feature)
        {
            var soMgr = new SerializedObject(mgr);
            var pCap = soMgr.FindProperty("captureFeature");
            if (pCap != null)
            {
                pCap.objectReferenceValue = feature;
                soMgr.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mgr);
            }
        }
        private static void BindManagerFeature(UICaptureEffectManager mgr, UIBGReplaceFeature feature)
        {
            var asset = AssetDatabase.LoadAssetAtPath<LiquidGlassSettings>(UserSettingsPath);
            var soMgr = new SerializedObject(mgr);
            var pCap = soMgr.FindProperty("replaceFeature");
            if (pCap != null)
            {
                pCap.objectReferenceValue = feature;
                soMgr.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mgr);
            }
        }

        private static void FinalizeAssets(Object obj)
        {
            if (obj == null) return;
            EditorUtility.SetDirty(obj);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
