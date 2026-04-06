using System.IO;
using System.Linq;
using ARPlatformer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Vuforia;

namespace ARPlatformer.Editor
{
    public static class ARPlatformerSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/ARRoomPlatformer.unity";
        private const string MarkerAssetPath = "Assets/StreamingAssets/Markers/platformer-marker.png";
        private const string ImportedHeroPrefabPath = "Assets/Pack_Heros/Prefabs/Hero_Nature.prefab";
        private const string RuntimeRootName = "AR Room Platformer";
        private const string TemplateRootName = "AR Platformer Templates";
        private const string HeroTemplateName = "Hero Template";
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

            if (ConfigureImportedHero(runtimeManager, scene))
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
                eventSystemObject.AddComponent<InputSystemUIInputModule>();
                sceneChanged = true;
            }
            else
            {
                if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                {
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
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

        private static bool ConfigureImportedHero(ARPlatformerRuntime runtimeManager, Scene scene)
        {
            var importedHeroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ImportedHeroPrefabPath);
            if (importedHeroPrefab == null)
                return false;

            var sceneChanged = AssignImportedHeroPrefab(runtimeManager, importedHeroPrefab);
            var templateRoot = EnsureTemplateRoot(runtimeManager.transform, ref sceneChanged);
            if (templateRoot != null)
            {
                sceneChanged |= SyncHeroTemplate(importedHeroPrefab, templateRoot, scene);
                sceneChanged |= RemoveObsoleteTemplateObject(templateRoot, LegacyHeroTemplateName);
                sceneChanged |= RemoveObsoleteTemplateObject(templateRoot, "Hero Preview");
            }

            sceneChanged |= RemoveObsoleteRootObject("Hero Preview");
            return sceneChanged;
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

        private static bool SyncHeroTemplate(GameObject importedHeroPrefab, Transform templateRoot, Scene scene)
        {
            var heroTemplate = FindTemplateObject(templateRoot, HeroTemplateName);
            var templateChanged = heroTemplate == null ||
                                  PrefabUtility.GetCorrespondingObjectFromSource(heroTemplate) != importedHeroPrefab;

            if (templateChanged)
            {
                if (heroTemplate != null)
                    Object.DestroyImmediate(heroTemplate);

                heroTemplate = PrefabUtility.InstantiatePrefab(importedHeroPrefab, scene) as GameObject;
                if (heroTemplate == null)
                    return true;
            }

            var sceneChanged = templateChanged;
            sceneChanged |= SetParentAndTransform(heroTemplate.transform, templateRoot, new Vector3(-2f, 0f, 0f), Quaternion.identity, Vector3.one);

            if (heroTemplate.name != HeroTemplateName)
            {
                heroTemplate.name = HeroTemplateName;
                sceneChanged = true;
            }

            if (heroTemplate.activeSelf)
            {
                heroTemplate.SetActive(false);
                sceneChanged = true;
            }

            sceneChanged |= ConfigureHeroCharacterRoot(heroTemplate);
            return sceneChanged;
        }

        private static GameObject FindTemplateObject(Transform parent, string name)
        {
            var child = parent.Find(name);
            return child != null ? child.gameObject : null;
        }

        private static bool SetParentAndTransform(Transform target, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var changed = false;

            if (target.parent != parent)
            {
                target.SetParent(parent, false);
                changed = true;
            }

            if (target.localPosition != localPosition)
            {
                target.localPosition = localPosition;
                changed = true;
            }

            if (target.localRotation != localRotation)
            {
                target.localRotation = localRotation;
                changed = true;
            }

            if (target.localScale != localScale)
            {
                target.localScale = localScale;
                changed = true;
            }

            return changed;
        }

        private static bool ConfigureHeroCharacterRoot(GameObject heroRoot)
        {
            var changed = false;

            var characterController = heroRoot.GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = Undo.AddComponent<CharacterController>(heroRoot);
                changed = true;
            }

            changed |= ConfigureCharacterController(characterController);

            if (heroRoot.GetComponent<PlatformerCharacterController>() == null)
            {
                Undo.AddComponent<PlatformerCharacterController>(heroRoot);
                changed = true;
            }

            var animationDriver = heroRoot.GetComponent<PlatformerCharacterAnimator>();
            if (animationDriver == null)
            {
                animationDriver = Undo.AddComponent<PlatformerCharacterAnimator>(heroRoot);
                changed = true;
            }

            animationDriver.RefreshAnimator();

            var animator = heroRoot.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.applyRootMotion)
            {
                animator.applyRootMotion = false;
                changed = true;
            }

            return changed;
        }

        private static bool ConfigureCharacterController(CharacterController characterController)
        {
            var changed = false;

            if (characterController.center != new Vector3(0f, 0.9f, 0f))
            {
                characterController.center = new Vector3(0f, 0.9f, 0f);
                changed = true;
            }

            if (!Mathf.Approximately(characterController.height, 1.8f))
            {
                characterController.height = 1.8f;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.radius, 0.24f))
            {
                characterController.radius = 0.24f;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.slopeLimit, 55f))
            {
                characterController.slopeLimit = 55f;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.stepOffset, 0.2f))
            {
                characterController.stepOffset = 0.2f;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.skinWidth, 0.02f))
            {
                characterController.skinWidth = 0.02f;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.minMoveDistance, 0f))
            {
                characterController.minMoveDistance = 0f;
                changed = true;
            }

            return changed;
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

            var texture = GenerateMarkerTexture(768);
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

        private static Texture2D GenerateMarkerTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "platformer-marker",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var white = new Color32(245, 244, 238, 255);
            var black = new Color32(19, 21, 24, 255);
            var accent = new Color32(208, 61, 31, 255);
            var accentTwo = new Color32(24, 129, 104, 255);

            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = white;

            texture.SetPixels32(pixels);

            var border = size / 18;
            FillRect(texture, 0, 0, size, border, black);
            FillRect(texture, 0, size - border, size, border, black);
            FillRect(texture, 0, 0, border, size, black);
            FillRect(texture, size - border, 0, border, size, black);

            var grid = 12;
            var cell = (size - (border * 2)) / grid;
            for (var y = 0; y < grid; y++)
            {
                for (var x = 0; x < grid; x++)
                {
                    var value = (x * 17 + y * 23 + (x * y * 7)) % 13;
                    if (value < 4)
                    {
                        var color = (x + y) % 3 == 0 ? accent : ((x * 2 + y) % 4 == 0 ? accentTwo : black);
                        FillRect(texture, border + x * cell, border + y * cell, cell - 6, cell - 6, color);
                    }
                }
            }

            DrawCornerMarker(texture, border + cell / 3, border + cell / 3, cell, black, accent);
            DrawCornerMarker(texture, size - border - (cell * 3) - cell / 3, border + cell / 3, cell, black, accentTwo);
            DrawCornerMarker(texture, border + cell / 3, size - border - (cell * 3) - cell / 3, cell, black, accentTwo);

            texture.Apply(false, false);
            return texture;
        }

        private static void DrawCornerMarker(Texture2D texture, int startX, int startY, int size, Color32 outer, Color32 inner)
        {
            FillRect(texture, startX, startY, size * 3, size * 3, outer);
            FillRect(texture, startX + size / 2, startY + size / 2, size * 2, size * 2, new Color32(245, 244, 238, 255));
            FillRect(texture, startX + size, startY + size, size, size, inner);
        }

        private static void FillRect(Texture2D texture, int x, int y, int width, int height, Color32 color)
        {
            for (var yIndex = y; yIndex < y + height; yIndex++)
            {
                for (var xIndex = x; xIndex < x + width; xIndex++)
                    texture.SetPixel(xIndex, yIndex, color);
            }
        }
    }
}
