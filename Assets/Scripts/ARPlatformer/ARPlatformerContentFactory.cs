using System;
using UnityEngine;

namespace ARPlatformer
{
    public sealed class ARPlatformerContentFactory : IDisposable
    {
        private const string TemplateRootName = "AR Platformer Templates";
        private const string CoinTemplateName = "Coin Template";
        private const string GoalTemplateName = "Goal Flag Template";
        private const string CourseBlockTemplateName = "Course Block Template";
        private const string TallCourseBlockTemplateName = "Tall Course Block Template";
        private const string CheckpointTemplateName = "Checkpoint Template";
        private const string IgnoreRaycastLayerName = "Ignore Raycast";

        private Transform _runtimeRoot;
        private Transform _templateRoot;
        private GameObject _coinTemplate;
        private GameObject _goalTemplate;
        private GameObject _courseBlockTemplate;
        private GameObject _tallCourseBlockTemplate;
        private GameObject _checkpointTemplate;

        private static int IgnoreRaycastLayer => LayerMask.NameToLayer(IgnoreRaycastLayerName);

        public void CacheSceneTemplates(Transform parent)
        {
            _runtimeRoot = parent;
            _templateRoot = FindTemplateRoot(parent);

            _coinTemplate = FindTemplate(_templateRoot, CoinTemplateName);
            _goalTemplate = FindTemplate(_templateRoot, GoalTemplateName);
            _courseBlockTemplate = FindTemplate(_templateRoot, CourseBlockTemplateName);
            _tallCourseBlockTemplate = FindTemplate(_templateRoot, TallCourseBlockTemplateName);
            _checkpointTemplate = FindTemplate(_templateRoot, CheckpointTemplateName);
        }

        public GameObject InstantiatePlayer(GameObject defaultPlayerPrefab, string name)
        {
            if (defaultPlayerPrefab == null)
                return null;

            var instance = UnityEngine.Object.Instantiate(defaultPlayerPrefab);
            instance.name = name;
            AttachToRuntimeRoot(instance.transform);
            return instance;
        }

        public void SanitizePlayerHierarchy(GameObject player, CharacterController characterController)
        {
            if (player == null)
                return;

            AttachToRuntimeRoot(player.transform);
            PlatformerCharacterControllerDefaults.Apply(characterController);
        }

        public void ConfigurePlayerVisuals(GameObject player)
        {
            if (player == null)
                return;

            if (player.GetComponentInChildren<Animator>(true) == null)
                return;

            if (player.GetComponent<PlatformerCharacterAnimator>() != null)
                return;

            player.AddComponent<PlatformerCharacterAnimator>();
        }

        public GameObject CreateGoalFlag(Vector3 position)
        {
            var instance = InstantiateTemplate(_goalTemplate, "GoalFlag", position, Quaternion.identity);
            return instance ?? CreateFallbackGoalFlag(position);
        }

        public GameObject CreateCoin(int index, Vector3 position)
        {
            var instance = InstantiateTemplate(_coinTemplate, $"Coin_{index}", position, Quaternion.identity);
            return instance ?? CreateFallbackCoin(index, position);
        }

        public GameObject CreateCourseProp(int index, Vector3 position, float stackHeight)
        {
            var template = stackHeight > 1.5f ? _tallCourseBlockTemplate : _courseBlockTemplate;
            var instance = InstantiateTemplate(template, $"CourseProp_{index}", position, Quaternion.identity);
            return instance ?? CreateFallbackCourseProp(index, position, stackHeight);
        }

        public GameObject CreateOrUpdateCheckpointMarker(GameObject checkpointRoot, Vector3 position, Vector3 forward)
        {
            var rotation = CreateHorizontalRotation(forward);

            if (checkpointRoot != null)
            {
                checkpointRoot.transform.SetPositionAndRotation(position, rotation);
                return checkpointRoot;
            }

            var instance = InstantiateTemplate(_checkpointTemplate, "CheckpointMarker", position, rotation);
            return instance ?? CreateFallbackCheckpoint(position, rotation);
        }

        public void Dispose()
        {
            _runtimeRoot = null;
            _templateRoot = null;
            _coinTemplate = null;
            _goalTemplate = null;
            _courseBlockTemplate = null;
            _tallCourseBlockTemplate = null;
            _checkpointTemplate = null;
        }

        private static Transform FindTemplateRoot(Transform parent)
        {
            if (parent != null)
            {
                var localRoot = parent.Find(TemplateRootName);
                if (localRoot != null)
                    return localRoot;
            }

            var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (var i = 0; i < allTransforms.Length; i++)
            {
                if (allTransforms[i].name == TemplateRootName)
                    return allTransforms[i];
            }

            return null;
        }

        private static GameObject FindTemplate(Transform templateRoot, string templateName)
        {
            if (templateRoot == null)
                return null;

            var templateTransform = templateRoot.Find(templateName);
            return templateTransform != null ? templateTransform.gameObject : null;
        }

        private GameObject InstantiateTemplate(GameObject template, string instanceName, Vector3 position, Quaternion rotation)
        {
            if (template == null)
                return null;

            var instance = UnityEngine.Object.Instantiate(template);
            instance.name = instanceName;
            instance.SetActive(true);
            instance.transform.SetPositionAndRotation(position, rotation);
            AttachToRuntimeRoot(instance.transform);
            return instance;
        }

        private void AttachToRuntimeRoot(Transform transformToAttach)
        {
            if (transformToAttach == null || _runtimeRoot == null)
                return;

            transformToAttach.SetParent(_runtimeRoot, true);
        }

        private static GameObject CreateFallbackGoalFlag(Vector3 position)
        {
            var root = new GameObject("GoalFlag");
            root.transform.position = position;

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            pole.transform.localScale = new Vector3(0.04f, 0.7f, 0.04f);

            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "Flag";
            flag.transform.SetParent(root.transform, false);
            flag.transform.localPosition = new Vector3(0.17f, 1.1f, 0f);
            flag.transform.localScale = new Vector3(0.3f, 0.16f, 0.04f);

            ApplyIgnoreRaycastLayer(root);
            return root;
        }

        private static GameObject CreateFallbackCoin(int index, Vector3 position)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = $"Coin_{index}";
            root.transform.position = position;
            root.transform.localScale = new Vector3(0.14f, 0.03f, 0.14f);
            root.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            var collider = root.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);

            var floatingVisual = root.AddComponent<FloatingItemVisual>();
            floatingVisual.Configure(90f, 0.05f, 2.4f);

            ApplyIgnoreRaycastLayer(root);
            return root;
        }

        private static GameObject CreateFallbackCourseProp(int index, Vector3 position, float stackHeight)
        {
            var clampedHeight = Mathf.Max(0.1f, stackHeight);
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = $"CourseProp_{index}";
            root.transform.position = position + Vector3.up * (clampedHeight * 0.5f);
            root.transform.localScale = new Vector3(0.24f, 0.18f * clampedHeight, 0.24f);
            ApplyIgnoreRaycastLayer(root);
            return root;
        }

        private static GameObject CreateFallbackCheckpoint(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("CheckpointMarker");
            root.transform.SetPositionAndRotation(position, rotation);

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Checkpoint Pad";
            pad.transform.SetParent(root.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            pad.transform.localScale = new Vector3(0.18f, 0.01f, 0.18f);

            var orbRoot = new GameObject("Checkpoint Orb Root");
            orbRoot.transform.SetParent(root.transform, false);
            orbRoot.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            var orbVisual = orbRoot.AddComponent<FloatingItemVisual>();
            orbVisual.Configure(55f, 0.03f, 2.3f);

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Checkpoint Orb";
            orb.transform.SetParent(orbRoot.transform, false);
            orb.transform.localScale = Vector3.one * 0.12f;

            var orbCollider = orb.GetComponent<Collider>();
            if (orbCollider != null)
                UnityEngine.Object.Destroy(orbCollider);

            ApplyIgnoreRaycastLayer(root);
            return root;
        }

        private static Quaternion CreateHorizontalRotation(Vector3 forward)
        {
            var flattenedForward = forward;
            flattenedForward.y = 0f;
            if (flattenedForward.sqrMagnitude < 0.001f)
                flattenedForward = Vector3.forward;

            return Quaternion.LookRotation(flattenedForward.normalized, Vector3.up);
        }

        private static void ApplyIgnoreRaycastLayer(GameObject root)
        {
            var ignoreLayer = IgnoreRaycastLayer;
            if (ignoreLayer < 0 || root == null)
                return;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.layer = ignoreLayer;
        }
    }
}
