using System.IO;
using System.Linq;
using ARPlatformer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using Vuforia;

namespace ARPlatformer.Editor
{
    public static class ARPlatformerSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/ARRoomPlatformer.unity";
        private const string MarkerAssetPath = "Assets/StreamingAssets/Markers/platformer-marker.png";
        private const string ImportedHeroPrefabPath = "Assets/Pack_Heros/Prefabs/Hero_Nature.prefab";
        private const string RuntimeConfigAssetPath = "Assets/Settings/ARPlatformerRuntimeConfig.asset";
        private const string RuntimeRootName = "AR Room Platformer";
        private const string TemplateRootName = "AR Platformer Templates";
        private const string LegacyHeroTemplateName = "Mario Template";

        [MenuItem("Tools/AR Platformer/Apply Scene Setup")]
        public static void ApplySetup()
        {
            EnsureMarkerTexture();
            ConfigureScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void ApplySetupBatchMode()
        {
            ApplySetup();
        }

        private static void ConfigureScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sceneChanged = false;

            var runtimeManager = Object.FindFirstObjectByType<ARPlatformerRuntime>();
            if (runtimeManager == null)
            {
                var runtimeObject = new GameObject(RuntimeRootName);
                runtimeManager = runtimeObject.AddComponent<ARPlatformerRuntime>();
                Undo.RegisterCreatedObjectUndo(runtimeObject, "Create AR Room Platformer");
                sceneChanged = true;
            }
            else if (runtimeManager.name != RuntimeRootName)
            {
                runtimeManager.name = RuntimeRootName;
                sceneChanged = true;
            }

            if (ConfigureRuntimeConfig(runtimeManager))
                sceneChanged = true;

            if (ConfigureImportedHero(runtimeManager))
                sceneChanged = true;

            var vuforiaBehaviour = Object.FindFirstObjectByType<VuforiaBehaviour>();
            if (vuforiaBehaviour != null)
            {
                var camera = vuforiaBehaviour.GetComponent<Camera>();
                if (camera != null)
                {
                    if (!Mathf.Approximately(camera.nearClipPlane, 0.03f))
                    {
                        camera.nearClipPlane = 0.03f;
                        sceneChanged = true;
                    }

                    if (!Mathf.Approximately(camera.farClipPlane, 60f))
                    {
                        camera.farClipPlane = 60f;
                        sceneChanged = true;
                    }

                    if (camera.allowMSAA)
                    {
                        camera.allowMSAA = false;
                        sceneChanged = true;
                    }
                }
            }

            var directionalLight = Object.FindFirstObjectByType<Light>();
            if (directionalLight != null && directionalLight.type == LightType.Directional && directionalLight.shadows != LightShadows.None)
            {
                directionalLight.shadows = LightShadows.None;
                sceneChanged = true;
            }

            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystemObject.AddComponent<EventSystem>();

                // Try to add the new Input System UI input module if available.
                var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputModuleType != null)
                    eventSystemObject.AddComponent(inputModuleType);

                Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");
                sceneChanged = true;
            }
            else
            {
                var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputModuleType != null && eventSystem.GetComponent(inputModuleType) == null)
                {
                    eventSystem.gameObject.AddComponent(inputModuleType);
                    sceneChanged = true;
                }

                var legacyInputModule = eventSystem.GetComponent<StandaloneInputModule>();
                if (legacyInputModule != null)
                {
                    Object.DestroyImmediate(legacyInputModule);
                    sceneChanged = true;
                }
            }

            if (sceneChanged)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            EditorUtility.SetDirty(runtimeManager);
        }

        private static bool ConfigureImportedHero(ARPlatformerRuntime runtimeManager)
        {
            var importedHeroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ImportedHeroPrefabPath);
            if (importedHeroPrefab == null)
                return false;

            var sceneChanged = AssignImportedHeroPrefab(runtimeManager, importedHeroPrefab);
            var templateRoot = EnsureTemplateRoot(runtimeManager.transform, ref sceneChanged);
            if (templateRoot != null)
            {
                sceneChanged |= RemoveObsoleteTemplateObject(templateRoot, LegacyHeroTemplateName);
                sceneChanged |= RemoveObsoleteTemplateObject(templateRoot, "Hero Template");
                sceneChanged |= RemoveObsoleteTemplateObject(templateRoot, "Hero Preview");
            }

            sceneChanged |= RemoveObsoleteRootObject("Hero Preview");
            return sceneChanged;
        }

        private static bool ConfigureRuntimeConfig(ARPlatformerRuntime runtimeManager)
        {
            var configAsset = EnsureRuntimeConfigAsset();
            if (configAsset == null)
                return false;

            var serializedRuntime = new SerializedObject(runtimeManager);
            var runtimeConfigProperty = serializedRuntime.FindProperty("runtimeConfig");
            if (runtimeConfigProperty == null || runtimeConfigProperty.objectReferenceValue == configAsset)
                return false;

            runtimeConfigProperty.objectReferenceValue = configAsset;
            serializedRuntime.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtimeManager);
            return true;
        }

        private static bool AssignImportedHeroPrefab(ARPlatformerRuntime runtimeManager, GameObject importedHeroPrefab)
        {
            var serializedRuntime = new SerializedObject(runtimeManager);
            var defaultPlayerPrefabProperty = serializedRuntime.FindProperty("defaultPlayerPrefab");
            if (defaultPlayerPrefabProperty == null || defaultPlayerPrefabProperty.objectReferenceValue == importedHeroPrefab)
                return false;

            defaultPlayerPrefabProperty.objectReferenceValue = importedHeroPrefab;
            serializedRuntime.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtimeManager);
            return true;
        }

        private static ARPlatformerRuntimeConfig EnsureRuntimeConfigAsset()
        {
            var runtimeConfig = AssetDatabase.LoadAssetAtPath<ARPlatformerRuntimeConfig>(RuntimeConfigAssetPath);
            if (runtimeConfig != null)
                return runtimeConfig;

            var directory = Path.GetDirectoryName(RuntimeConfigAssetPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            runtimeConfig = ScriptableObject.CreateInstance<ARPlatformerRuntimeConfig>();
            AssetDatabase.CreateAsset(runtimeConfig, RuntimeConfigAssetPath);
            EditorUtility.SetDirty(runtimeConfig);
            return runtimeConfig;
        }

        private static Transform EnsureTemplateRoot(Transform runtimeRoot, ref bool sceneChanged)
        {
            var templateRoots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(candidate => candidate.name == TemplateRootName)
                .OrderByDescending(candidate => candidate.childCount)
                .ToArray();

            Transform templateRoot;
            if (templateRoots.Length == 0)
            {
                var templateRootObject = new GameObject(TemplateRootName);
                Undo.RegisterCreatedObjectUndo(templateRootObject, "Create AR Platformer Templates");
                templateRoot = templateRootObject.transform;
                sceneChanged = true;
            }
            else
            {
                templateRoot = templateRoots[0];
            }

            if (templateRoot.parent != runtimeRoot)
            {
                templateRoot.SetParent(runtimeRoot, false);
                sceneChanged = true;
            }

            for (var i = 1; i < templateRoots.Length; i++)
            {
                sceneChanged |= MergeTemplateRoots(templateRoots[i], templateRoot);
            }

            return templateRoot;
        }

        private static GameObject FindTemplateObject(Transform parent, string name)
        {
            var child = parent.Find(name);
            return child != null ? child.gameObject : null;
        }

        private static bool RemoveObsoleteTemplateObject(Transform templateRoot, string objectName)
        {
            var templateObject = FindTemplateObject(templateRoot, objectName);
            if (templateObject == null)
                return false;

            Object.DestroyImmediate(templateObject);
            return true;
        }

        private static bool RemoveObsoleteRootObject(string objectName)
        {
            var target = GameObject.Find(objectName);
            if (target == null)
                return false;

            Object.DestroyImmediate(target);
            return true;
        }

        private static bool MergeTemplateRoots(Transform sourceRoot, Transform destinationRoot)
        {
            if (sourceRoot == null || destinationRoot == null || sourceRoot == destinationRoot)
                return false;

            while (sourceRoot.childCount > 0)
            {
                var child = sourceRoot.GetChild(0);
                var existingChild = destinationRoot.Find(child.name);
                if (existingChild == null)
                    child.SetParent(destinationRoot, false);
                else
                    Object.DestroyImmediate(child.gameObject);
            }

            Object.DestroyImmediate(sourceRoot.gameObject);
            return true;
        }

        private static void EnsureMarkerTexture()
        {
            if (File.Exists(MarkerAssetPath))
                return;

            var directory = Path.GetDirectoryName(MarkerAssetPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var texture = PlatformerMarkerTextureFactory.CreateTexture(768, "platformer-marker");
            File.WriteAllBytes(MarkerAssetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(MarkerAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(MarkerAssetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
    }
}
