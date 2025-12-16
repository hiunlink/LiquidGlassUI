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

        [MenuItem("Tools/LiquidGlassUI/Install")]
        public static void Install()
        {
            // 0) 找 UI Camera（必须先做，因为后面一切都以 UI RendererData 为准）
            var uiCam = FindUICameraFromCanvas() ?? Camera.main;
            if (uiCam == null)
            {
                Debug.LogError("[LiquidGlassUI] UI Camera not found. Please set Canvas(RenderMode=ScreenSpace-Camera).worldCamera.");
                return;
            }
            if (!uiCam.TryGetComponent(out UniversalAdditionalCameraData camData))
            {
                Debug.LogError($"[LiquidGlassUI] Camera '{uiCam.name}' has no UniversalAdditionalCameraData.");
                return;
            }

            // 1) URP Asset
            var urp = UniversalRenderPipeline.asset;
            if (urp == null)
            {
                Debug.LogError("[LiquidGlassUI] URP Asset not found. Check Project Settings > Graphics.");
                return;
            }

            // 2) UI Camera 使用的 RendererData（反射 m_RendererIndex）
            int uiRendererIndex = GetRendererIndex(camData);
            var uiRD = GetRendererDataByIndex(urp, uiRendererIndex);
            if (uiRD == null)
            {
                Debug.LogError($"[LiquidGlassUI] RendererData not found for UI camera rendererIndex={uiRendererIndex}.");
                return;
            }

            // 3) 确保用户 Settings.asset
            var userSettings = EnsureUserSettingsAsset();

            // 4) 确保 CaptureFeature, ReplaceFeature 存在（装到 UI RendererData 上）
            var captureFeature = EnsureRendererFeature<UICaptureComposePerLayerFeature>(
                uiRD, "UICaptureComposePerLayerFeature");

            if (captureFeature == null)
            {
                Debug.LogError("[LiquidGlassUI] Failed to create/find UICaptureComposePerLayerFeature on UI RendererData.");
                return;
            }
            var replaceFeature = EnsureRendererFeature<UIBGReplaceFeature>(
                uiRD, "UIBGReplaceFeature");

            if (replaceFeature == null)
            {
                Debug.LogError("[LiquidGlassUI] Failed to create/find UIBGReplaceFeature on UI RendererData.");
                return;
            }

            // 5) 绑定 Settings 到 Feature
            //    绑定 replaceMaterial 
            if (userSettings != null)
                BindSettingsToFeature(captureFeature, userSettings);
            var asset = AssetDatabase.LoadAssetAtPath<LiquidGlassSettings>(UserSettingsPath);
            if (asset != null && asset.replaceMat != null)
                BindMatToReplaceFeature(replaceFeature, asset.replaceMat);
            
            // 6) 确保场景有 Manager，并绑定 captureFeature, replaceFeature
            var mgr = EnsureManagerInScene();
            if (mgr != null)
            {
                BindManagerFeature(mgr, captureFeature);
                BindManagerFeature(mgr, replaceFeature);
            }

            // 7) 排除 UI RendererData 的 Transparent Layer Mask（避免默认管线再画一次）
            int reservedMask = BuildReservedMask(userSettings.layerStart, userSettings.layerEnd, userSettings.hiddenLayer);
            bool changed = ExcludeMaskFromTransparent(uiRD, reservedMask, backupKey: BackupKeyPrefix + uiRD.name);

            FinalizeAssets(uiRD);
            
            // 8) 加入 UICaptureSceneFeature 到 SceneViewRenderer
            int sceneRendererIndex = GetDefaultRendererIndex(urp);
            var sceneRD = GetRendererDataByIndex(urp, sceneRendererIndex);
            if (sceneRD == null)
            {
                Debug.LogError($"[LiquidGlassUI] RendererData not found for scene camera rendererIndex={sceneRendererIndex}.");
                return;
            }
            var sceneFeature = EnsureRendererFeature<UICaptureSceneFeature>(sceneRD, "UICaptureSceneFeature");
            if (sceneFeature == null)
            {
                Debug.LogError("[LiquidGlassUI] Failed to create/find UICaptureSceneFeature on Scene RendererData.");
                return;
            }

            sceneFeature.captureFeature = captureFeature;
            

            Debug.Log(
                "[LiquidGlassUI] Install completed.\n" +
                $"- UICamera: {uiCam.name}\n" +
                $"- UIRendererIndex: {uiRendererIndex}\n" +
                $"- UIRendererData: {uiRD.name}\n" +
                $"- CaptureFeature: {captureFeature.name}\n" +
                $"- Settings: {(userSettings ? AssetDatabase.GetAssetPath(userSettings) : "(none)")}\n" +
                $"- TransparentMaskUpdated: {changed}"
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
