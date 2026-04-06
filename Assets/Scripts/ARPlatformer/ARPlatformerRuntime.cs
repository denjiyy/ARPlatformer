using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Vuforia;
using Image = UnityEngine.UI.Image;

namespace ARPlatformer
{
    public sealed class ARPlatformerRuntime : MonoBehaviour
    {
        private const string MarkerRelativePath = "Markers/platformer-marker.png";
        private const string MarkerTargetName = "PlatformerSpawnMarker";
        private const string TemplateRootName = "AR Platformer Templates";
        private const string PlayerTemplateName = "Hero Template";
        private const string CoinTemplateName = "Coin Template";
        private const string GoalTemplateName = "Goal Flag Template";
        private const string CourseBlockTemplateName = "Course Block Template";
        private const string TallCourseBlockTemplateName = "Tall Course Block Template";
        private const string CheckpointTemplateName = "Checkpoint Template";
        private const float MarkerWidthMeters = 0.12f;
        private const float CharacterMoveSpeed = 1.6f;
        private const float CharacterJumpHeight = 0.45f;
        private const float CharacterGravity = -14f;
        private const float RespawnRayHeight = 1.6f;
        private const float RespawnRayDistance = 5f;
        private const float SurfaceHoverHeight = 0.14f;
        private const float SurfaceSampleSpacing = 0.45f;
        private const float MinSurfaceSpacing = 0.4f;
        private const float MinGoalDistance = 1.2f;
        private const int SurfaceGridHalfExtent = 4;
        private const int CoinCountTarget = 7;
        private const int CoursePropTarget = 4;
        private const int IgnoreRaycastLayer = 2;
        private const float ScanCollisionSyncInterval = 0.35f;
        private const float CoinCollectDistance = 0.48f;
        private const float GoalReachDistance = 0.72f;
        private const float VuforiaStartupTimeoutSeconds = 15f;
        private const float UiRefreshInterval = 0.1f;
        private const float MarkerCheckpointRefreshDistanceSqr = 0.0001f;
        private const float MarkerCheckpointRefreshForwardDotThreshold = 0.9995f;

        private sealed class CoinInstance
        {
            public GameObject Root;
            public Transform Transform;
            public bool Collected;
        }

        private enum SessionState
        {
            Booting,
            Scanning,
            WaitingForMarker,
            Playing,
            Completed,
            Error
        }

        private SessionState _state = SessionState.Booting;

        private AreaTargetCaptureBehaviour _capture;
        private RuntimeMeshRenderingBehaviour _scanMeshRenderer;
        private ImageTargetBehaviour _imageTarget;
        private PlatformerCharacterController _playerController;
        private GameObject _playerRoot;
        private readonly List<CoinInstance> _coins = new();
        private readonly List<GameObject> _courseProps = new();
        private GameObject _goalRoot;
        private Transform _goalTransform;
        private GameObject _checkpointRoot;
        private Texture2D _markerTexture;
        private Material _scanMeshMaterial;
        private Material _playerSkinMaterial;
        private Material _playerHatMaterial;
        private Material _playerOverallsMaterial;
        private Material _playerShoesMaterial;
        private Material _playerHairMaterial;
        private Material _coinMaterial;
        private Material _goalPoleMaterial;
        private Material _goalFlagMaterial;
        private Material _goalOrbMaterial;
        private Material _coursePropMaterial;
        private Material _coursePropTrimMaterial;
        private Material _checkpointPadMaterial;
        private Material _checkpointOrbMaterial;
        private Sprite _whiteSprite;
        private Font _defaultFont;
        [SerializeField] private GameObject defaultPlayerPrefab;
        private Transform _templateRoot;
        private GameObject _playerTemplate;
        private GameObject _coinTemplate;
        private GameObject _goalTemplate;
        private GameObject _courseBlockTemplate;
        private GameObject _tallCourseBlockTemplate;
        private GameObject _checkpointTemplate;

        private Canvas _canvas;
        private Text _headlineText;
        private Text _hintText;
        private Text _statsText;
        private Button _finishScanButton;
        private Button _resetButton;
        private Button _jumpButton;
        private Button _respawnButton;
        private RawImage _markerPreview;
        private GameObject _markerPreviewRoot;
        private GameObject _gameplayControlsRoot;
        private TouchJoystick _joystick;

        private bool _markerTracked;
        private bool _playerSpawned;
        private bool _restartSessionOnResume;
        private Coroutine _sessionRoutine;
        private Vector3 _playerCheckpointPosition;
        private Vector3 _playerCheckpointForward = Vector3.forward;
        private int _collectedCoins;
        private int _totalCoins;
        private float _levelStartTime;
        private float _levelFinishTime;
        private float _nextScanCollisionSyncTime;
        private float _nextUiRefreshTime;
        private bool _hasMarkerCheckpointPose;
        private Vector3 _lastMarkerCheckpointPosition;
        private Vector3 _lastMarkerCheckpointForward;

        private void Awake()
        {
            name = "AR Platformer Runtime";
            ApplyPerformanceDefaults();
            EnsureEventSystem();
            CacheSceneTemplates();
            BuildUi();
            RefreshUi();
        }

        private void Start()
        {
            RestartSession();
        }

        private void Update()
        {
            SyncScanMeshCollisionIfNeeded();

            if (_playerController != null && _joystick != null)
                _playerController.SetMoveInput(_joystick.Value);

            if ((_state == SessionState.Playing || _state == SessionState.Completed) &&
                _markerTracked &&
                _imageTarget != null &&
                ShouldRefreshCheckpointFromMarker(_imageTarget.transform))
            {
                UpdateCheckpointFromMarker(_imageTarget.transform);
            }

            if (_state == SessionState.Playing)
                UpdateGameplayInteractions();

            if (_finishScanButton != null)
                _finishScanButton.interactable = CanFinishScan();

            if (_respawnButton != null)
                _respawnButton.interactable = _playerSpawned && _state == SessionState.Playing;

            UpdateUiIfNeeded();
        }

        private void OnDestroy()
        {
            CleanupRuntimeObjects();

            if (_scanMeshMaterial != null)
                Destroy(_scanMeshMaterial);

            if (_playerSkinMaterial != null)
                Destroy(_playerSkinMaterial);
            if (_playerHatMaterial != null)
                Destroy(_playerHatMaterial);
            if (_playerOverallsMaterial != null)
                Destroy(_playerOverallsMaterial);
            if (_playerShoesMaterial != null)
                Destroy(_playerShoesMaterial);
            if (_playerHairMaterial != null)
                Destroy(_playerHairMaterial);
            if (_coinMaterial != null)
                Destroy(_coinMaterial);
            if (_goalPoleMaterial != null)
                Destroy(_goalPoleMaterial);
            if (_goalFlagMaterial != null)
                Destroy(_goalFlagMaterial);
            if (_goalOrbMaterial != null)
                Destroy(_goalOrbMaterial);
            if (_coursePropMaterial != null)
                Destroy(_coursePropMaterial);
            if (_coursePropTrimMaterial != null)
                Destroy(_coursePropTrimMaterial);
            if (_checkpointPadMaterial != null)
                Destroy(_checkpointPadMaterial);
            if (_checkpointOrbMaterial != null)
                Destroy(_checkpointOrbMaterial);

            if (_whiteSprite != null)
                Destroy(_whiteSprite);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                _restartSessionOnResume = true;
                return;
            }

            if (_restartSessionOnResume)
            {
                _restartSessionOnResume = false;
                RestartSession();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _restartSessionOnResume = true;
                return;
            }

            if (_restartSessionOnResume)
            {
                _restartSessionOnResume = false;
                RestartSession();
            }
        }

        private void ApplyPerformanceDefaults()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Time.fixedDeltaTime = 1f / 60f;
        }

        private void RestartSession()
        {
            if (_sessionRoutine != null)
                StopCoroutine(_sessionRoutine);

            _sessionRoutine = StartCoroutine(RestartSessionRoutine());
        }

        private IEnumerator RestartSessionRoutine()
        {
            _state = SessionState.Booting;
            RefreshUi();

            CacheSceneTemplates();
            CleanupRuntimeObjects();
            yield return WaitForVuforia();

            if (_state == SessionState.Error)
                yield break;

            _capture = FindFirstObjectByType<AreaTargetCaptureBehaviour>();
            if (_capture == null)
            {
                SetError("No AreaTargetCapture object was found in the scene. Keep the Vuforia AreaTargetCapture prefab in the main AR room scene.");
                yield break;
            }

            _capture.DisplayPreviewMesh = false;
            DisableBuiltInCaptureUi();
            SafeDestroyCapture();
            yield return null;

            CreateScanMeshRenderer();

            try
            {
                _capture.StartCapture();
                _state = SessionState.Scanning;
            }
            catch (Exception exception)
            {
                SetError($"Could not start room scanning. {exception.Message}");
                yield break;
            }

            RefreshUi();
        }

        private IEnumerator WaitForVuforia()
        {
            var timeoutAt = Time.realtimeSinceStartup + VuforiaStartupTimeoutSeconds;
            while (VuforiaBehaviour.Instance == null ||
                   VuforiaApplication.Instance == null ||
                   !VuforiaApplication.Instance.IsRunning)
            {
                if (Time.realtimeSinceStartup >= timeoutAt)
                {
                    SetError("Vuforia did not finish starting. Check the license key, allow camera access, and run on an ARKit-capable iPhone or iPad.");
                    yield break;
                }

                yield return null;
            }
        }

        private void CreateScanMeshRenderer()
        {
            if (_scanMeshRenderer != null)
                Destroy(_scanMeshRenderer.gameObject);

            _scanMeshMaterial ??= CreatePreviewMaterial();
            var runtimeMeshObject = VuforiaBehaviour.Instance.ObserverFactory.CreateRuntimeMeshRenderingBehaviour(
                _capture,
                _scanMeshMaterial,
                true);
            _scanMeshRenderer = runtimeMeshObject != null
                ? runtimeMeshObject.GetComponent<RuntimeMeshRenderingBehaviour>()
                : null;

            if (_scanMeshRenderer != null)
            {
                runtimeMeshObject.name = "Platformer Scan Mesh";
                SetScanMeshVisible(true);
                EnsureScanMeshCollision();
            }
        }

        private void CleanupRuntimeObjects()
        {
            if (_imageTarget != null)
            {
                _imageTarget.OnTargetStatusChanged -= HandleImageTargetStatusChanged;
                Destroy(_imageTarget.gameObject);
                _imageTarget = null;
            }

            if (_playerRoot != null)
            {
                if (_playerController != null)
                    _playerController.RespawnRequested -= HandlePlayerRespawnRequested;

                Destroy(_playerRoot);
                _playerRoot = null;
                _playerController = null;
            }

            if (_scanMeshRenderer != null)
            {
                Destroy(_scanMeshRenderer.gameObject);
                _scanMeshRenderer = null;
            }

            if (_markerTexture != null)
            {
                Destroy(_markerTexture);
                _markerTexture = null;
            }

            _markerTracked = false;
            _playerSpawned = false;
            _playerCheckpointPosition = Vector3.zero;
            _playerCheckpointForward = Vector3.forward;
            _collectedCoins = 0;
            _totalCoins = 0;
            _levelStartTime = 0f;
            _levelFinishTime = 0f;
            _nextScanCollisionSyncTime = 0f;
            _nextUiRefreshTime = 0f;
            _hasMarkerCheckpointPose = false;
            _lastMarkerCheckpointPosition = Vector3.zero;
            _lastMarkerCheckpointForward = Vector3.forward;

            ClearGameplayLayout();

            SafeDestroyCapture();
        }

        private void ClearGameplayLayout()
        {
            foreach (var coin in _coins)
            {
                if (coin.Root != null)
                    Destroy(coin.Root);
            }

            _coins.Clear();

            foreach (var courseProp in _courseProps)
            {
                if (courseProp != null)
                    Destroy(courseProp);
            }

            _courseProps.Clear();

            if (_goalRoot != null)
            {
                Destroy(_goalRoot);
                _goalRoot = null;
                _goalTransform = null;
            }

            if (_checkpointRoot != null)
            {
                Destroy(_checkpointRoot);
                _checkpointRoot = null;
            }
        }

        private void SafeDestroyCapture()
        {
            if (_capture == null)
                return;

            try
            {
                if (_capture.Status == AreaTargetCaptureStatus.GENERATING)
                    _capture.CancelTargetGeneration();

                _capture.DestroyCapture();
            }
            catch
            {
                // DestroyCapture is a best-effort reset here.
            }
        }

        private void HandleFinishScanPressed()
        {
            if (_capture == null || !CanFinishScan())
                return;

            try
            {
                _capture.StopCapture();
            }
            catch (Exception exception)
            {
                SetError($"Failed to stop scanning cleanly. {exception.Message}");
                return;
            }

            SetScanMeshVisible(false);

            if (!CreateRuntimeImageTarget())
                return;

            _state = SessionState.WaitingForMarker;
            RefreshUi();
        }

        private bool CreateRuntimeImageTarget()
        {
            if (_imageTarget != null)
                return true;

            try
            {
                _markerTexture = LoadMarkerTexture();
                _imageTarget = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(
                    _markerTexture,
                    MarkerWidthMeters,
                    MarkerTargetName);
            }
            catch (Exception exception)
            {
                SetError($"Could not create the runtime ImageTarget. {exception.Message}");
                return false;
            }

            if (_imageTarget == null)
            {
                SetError("Vuforia did not return a runtime ImageTarget.");
                return false;
            }

            _imageTarget.gameObject.name = "Platformer Spawn Marker";
            _imageTarget.OnTargetStatusChanged += HandleImageTargetStatusChanged;
            if (_markerPreview != null)
                _markerPreview.texture = _markerTexture;

            return true;
        }

        private void HandleImageTargetStatusChanged(ObserverBehaviour observerBehaviour, TargetStatus targetStatus)
        {
            _markerTracked = IsTracked(targetStatus);

            if (_markerTracked)
            {
                _hasMarkerCheckpointPose = false;
                UpdateCheckpointFromMarker(observerBehaviour.transform);

                if (!_playerSpawned)
                {
                    SpawnPlayer(observerBehaviour.transform);
                    _state = SessionState.Playing;
                }
            }

            RefreshUi();
        }

        private void SpawnPlayer(Transform markerTransform)
        {
            if (_playerRoot != null)
            {
                if (_playerController != null)
                    _playerController.RespawnRequested -= HandlePlayerRespawnRequested;

                Destroy(_playerRoot);
                _playerRoot = null;
                _playerController = null;
            }

            UpdateCheckpointFromMarker(markerTransform);

            var player = InstantiatePlayer("Hero");
            if (player == null)
                player = new GameObject("Hero");

            player.transform.SetPositionAndRotation(
                _playerCheckpointPosition,
                Quaternion.LookRotation(_playerCheckpointForward, Vector3.up));

            var characterController = player.GetComponent<CharacterController>();
            if (characterController == null)
                characterController = player.AddComponent<CharacterController>();

            characterController.center = new Vector3(0f, 0.9f, 0f);
            characterController.height = 1.8f;
            characterController.radius = 0.24f;
            characterController.slopeLimit = 55f;
            characterController.stepOffset = 0.2f;
            characterController.skinWidth = 0.02f;
            characterController.minMoveDistance = 0f;

            _playerController = player.GetComponent<PlatformerCharacterController>();
            if (_playerController == null)
                _playerController = player.AddComponent<PlatformerCharacterController>();

            StripPlayerPhysicsColliders(player, characterController);

            var gameplayCamera = GetGameplayCamera();
            _playerController.Configure(
                gameplayCamera != null ? gameplayCamera.transform : null,
                CharacterMoveSpeed,
                CharacterJumpHeight,
                CharacterGravity);
            _playerController.SetCheckpoint(_playerCheckpointPosition, _playerCheckpointForward);
            _playerController.SetMovementEnabled(true);
            _playerController.RespawnRequested += HandlePlayerRespawnRequested;

            ConfigurePlayerVisuals(player);

            _playerRoot = player;
            _playerSpawned = true;
            GenerateGameplayLayout();
        }

        private void UpdateCheckpointFromMarker(Transform markerTransform)
        {
            _playerCheckpointPosition = ResolveSpawnPosition(markerTransform.position);
            _playerCheckpointForward = GetSpawnForward(markerTransform);
            _lastMarkerCheckpointPosition = markerTransform.position;
            _lastMarkerCheckpointForward = _playerCheckpointForward;
            _hasMarkerCheckpointPose = true;

            if (_playerController != null)
                _playerController.SetCheckpoint(_playerCheckpointPosition, _playerCheckpointForward);

            if (_checkpointRoot != null)
                CreateOrUpdateCheckpointMarker();
        }

        private void GenerateGameplayLayout()
        {
            ClearGameplayLayout();

            var sampledSurfaces = SampleGameplaySurfacePoints(_playerCheckpointPosition, _playerCheckpointForward);
            if (sampledSurfaces.Count == 0)
            {
                CreateOrUpdateCheckpointMarker();
                _levelStartTime = Time.time;
                return;
            }

            var goalIndex = FindGoalSurfaceIndex(sampledSurfaces, _playerCheckpointPosition);
            var goalPosition = sampledSurfaces[goalIndex];
            sampledSurfaces.RemoveAt(goalIndex);

            CreateGoalFlag(goalPosition);

            var createdCoins = 0;
            for (var i = 0; i < sampledSurfaces.Count && createdCoins < CoinCountTarget; i++)
            {
                var coinPosition = sampledSurfaces[i];
                if (Vector3.Distance(coinPosition, goalPosition) < 0.55f)
                    continue;

                CreateCoin(coinPosition);
                createdCoins++;
            }

            CreateCourseProps(sampledSurfaces, _playerCheckpointPosition, goalPosition);

            _collectedCoins = 0;
            _totalCoins = _coins.Count;
            _levelStartTime = Time.time;
            _levelFinishTime = 0f;
        }

        private List<Vector3> SampleGameplaySurfacePoints(Vector3 spawnPosition, Vector3 spawnForward)
        {
            var samples = new List<Vector3>();
            var right = Vector3.Cross(Vector3.up, spawnForward);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            else
                right.Normalize();

            for (var z = -SurfaceGridHalfExtent; z <= SurfaceGridHalfExtent; z++)
            {
                for (var x = -SurfaceGridHalfExtent; x <= SurfaceGridHalfExtent; x++)
                {
                    var lateralOffset = x * SurfaceSampleSpacing;
                    var forwardOffset = z * SurfaceSampleSpacing;
                    var probeOrigin = spawnPosition + right * lateralOffset + spawnForward * forwardOffset + Vector3.up * RespawnRayHeight;

                    if (!Physics.Raycast(probeOrigin, Vector3.down, out var hit, RespawnRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;

                    if (hit.normal.y < 0.72f)
                        continue;

                    var samplePoint = hit.point + Vector3.up * SurfaceHoverHeight;
                    if ((samplePoint - spawnPosition).sqrMagnitude < 0.3025f)
                        continue;

                    if (Mathf.Abs(samplePoint.y - spawnPosition.y) > 1.25f)
                        continue;

                    if (IsTooCloseToExistingSample(samplePoint, samples))
                        continue;

                    samples.Add(samplePoint);
                }
            }

            samples.Sort((left, rightPoint) =>
                (spawnPosition - left).sqrMagnitude.CompareTo((spawnPosition - rightPoint).sqrMagnitude));

            return samples;
        }

        private bool IsTooCloseToExistingSample(Vector3 samplePoint, List<Vector3> samples)
        {
            var minSurfaceSpacingSqr = MinSurfaceSpacing * MinSurfaceSpacing;
            for (var i = 0; i < samples.Count; i++)
            {
                if ((samplePoint - samples[i]).sqrMagnitude < minSurfaceSpacingSqr)
                    return true;
            }

            return false;
        }

        private int FindGoalSurfaceIndex(List<Vector3> surfaces, Vector3 spawnPosition)
        {
            var goalIndex = surfaces.Count - 1;
            var farthestDistance = -1f;
            var minGoalDistanceSqr = MinGoalDistance * MinGoalDistance;

            for (var i = 0; i < surfaces.Count; i++)
            {
                var distance = (spawnPosition - surfaces[i]).sqrMagnitude;
                if (distance < minGoalDistanceSqr)
                    continue;

                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    goalIndex = i;
                }
            }

            return goalIndex;
        }

        private void CreateCoin(Vector3 surfacePosition)
        {
            var coinTemplateInstance = InstantiateTemplate(_coinTemplate, $"Coin {_coins.Count + 1}");
            if (coinTemplateInstance != null)
            {
                coinTemplateInstance.transform.position = surfacePosition;
                var coinVisual = coinTemplateInstance.GetComponent<FloatingItemVisual>();
                if (coinVisual == null)
                    coinVisual = coinTemplateInstance.AddComponent<FloatingItemVisual>();
                coinVisual.Configure(135f, 0.06f, 2.8f);
                ApplyCoinTemplateMaterials(coinTemplateInstance.transform);

                _coins.Add(new CoinInstance
                {
                    Root = coinTemplateInstance,
                    Transform = coinTemplateInstance.transform,
                    Collected = false
                });
                return;
            }

            _coinMaterial ??= CreateColoredMaterial(new Color(0.96f, 0.82f, 0.12f, 1f));

            var coinRoot = new GameObject($"Coin {_coins.Count + 1}");
            coinRoot.transform.position = surfacePosition;

            var visual = coinRoot.AddComponent<FloatingItemVisual>();
            visual.Configure(135f, 0.06f, 2.8f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(coinRoot.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            body.transform.localScale = new Vector3(0.14f, 0.03f, 0.14f);

            var collider = body.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _coinMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            _coins.Add(new CoinInstance
            {
                Root = coinRoot,
                Transform = coinRoot.transform,
                Collected = false
            });
        }

        private void CreateGoalFlag(Vector3 surfacePosition)
        {
            var goalTemplateInstance = InstantiateTemplate(_goalTemplate, "Goal Flag");
            if (goalTemplateInstance != null)
            {
                goalTemplateInstance.transform.position = surfacePosition;
                _goalRoot = goalTemplateInstance;
                _goalTransform = _goalRoot.transform;
                ApplyGoalTemplateMaterials(goalTemplateInstance.transform);
                return;
            }

            _goalPoleMaterial ??= CreateColoredMaterial(new Color(0.88f, 0.89f, 0.92f, 1f));
            _goalFlagMaterial ??= CreateColoredMaterial(new Color(0.17f, 0.72f, 0.34f, 1f));
            _goalOrbMaterial ??= CreateColoredMaterial(new Color(0.98f, 0.94f, 0.62f, 1f));

            _goalRoot = new GameObject("Goal Flag");
            _goalRoot.transform.position = surfacePosition;
            _goalTransform = _goalRoot.transform;

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = "Pole";
            pole.transform.SetParent(_goalRoot.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            pole.transform.localScale = new Vector3(0.04f, 1.4f, 0.04f);
            Destroy(pole.GetComponent<Collider>());
            var poleRenderer = pole.GetComponent<Renderer>();
            if (poleRenderer != null)
            {
                poleRenderer.sharedMaterial = _goalPoleMaterial;
                poleRenderer.shadowCastingMode = ShadowCastingMode.Off;
                poleRenderer.receiveShadows = false;
            }

            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "Flag";
            flag.transform.SetParent(_goalRoot.transform, false);
            flag.transform.localPosition = new Vector3(0.18f, 1.08f, 0f);
            flag.transform.localScale = new Vector3(0.32f, 0.18f, 0.04f);
            Destroy(flag.GetComponent<Collider>());
            var flagRenderer = flag.GetComponent<Renderer>();
            if (flagRenderer != null)
            {
                flagRenderer.sharedMaterial = _goalFlagMaterial;
                flagRenderer.shadowCastingMode = ShadowCastingMode.Off;
                flagRenderer.receiveShadows = false;
            }

            var orbRoot = new GameObject("Goal Orb");
            orbRoot.transform.SetParent(_goalRoot.transform, false);
            orbRoot.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            var orbVisual = orbRoot.AddComponent<FloatingItemVisual>();
            orbVisual.Configure(70f, 0.04f, 2.1f);

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Orb";
            orb.transform.SetParent(orbRoot.transform, false);
            orb.transform.localScale = new Vector3(0.16f, 0.16f, 0.16f);
            Destroy(orb.GetComponent<Collider>());
            var orbRenderer = orb.GetComponent<Renderer>();
            if (orbRenderer != null)
            {
                orbRenderer.sharedMaterial = _goalOrbMaterial;
                orbRenderer.shadowCastingMode = ShadowCastingMode.Off;
                orbRenderer.receiveShadows = false;
            }
        }

        private void CreateCourseProps(List<Vector3> sampledSurfaces, Vector3 spawnPosition, Vector3 goalPosition)
        {
            var createdProps = 0;

            for (var i = sampledSurfaces.Count - 1; i >= 0 && createdProps < CoursePropTarget; i--)
            {
                var surfacePosition = sampledSurfaces[i];
                if ((surfacePosition - spawnPosition).sqrMagnitude < 0.81f)
                    continue;

                if ((surfacePosition - goalPosition).sqrMagnitude < 0.7225f)
                    continue;

                if (IsNearCollectedItem(surfacePosition, 0.52f))
                    continue;

                CreateCourseProp(surfacePosition, createdProps % 2 == 0 ? 1 : 2);
                createdProps++;
            }

            CreateOrUpdateCheckpointMarker();
        }

        private bool IsNearCollectedItem(Vector3 surfacePosition, float maxDistance)
        {
            var maxDistanceSqr = maxDistance * maxDistance;
            for (var i = 0; i < _coins.Count; i++)
            {
                var coinTransform = _coins[i].Transform;
                if (coinTransform != null && (surfacePosition - coinTransform.position).sqrMagnitude < maxDistanceSqr)
                    return true;
            }

            return false;
        }

        private void CreateCourseProp(Vector3 surfacePosition, int stackHeight)
        {
            var blockTemplate = stackHeight > 1 ? _tallCourseBlockTemplate : _courseBlockTemplate;
            var coursePropTemplateInstance = InstantiateTemplate(blockTemplate, $"Course Block {_courseProps.Count + 1}");
            if (coursePropTemplateInstance != null)
            {
                coursePropTemplateInstance.transform.position = surfacePosition - Vector3.up * (SurfaceHoverHeight - 0.01f);
                ApplyCoursePropTemplateMaterials(coursePropTemplateInstance.transform);
                _courseProps.Add(coursePropTemplateInstance);
                return;
            }

            _coursePropMaterial ??= CreateColoredMaterial(new Color(0.78f, 0.24f, 0.12f, 1f));
            _coursePropTrimMaterial ??= CreateColoredMaterial(new Color(0.97f, 0.84f, 0.42f, 1f));

            var propRoot = new GameObject($"Course Block {_courseProps.Count + 1}");
            propRoot.transform.position = surfacePosition - Vector3.up * (SurfaceHoverHeight - 0.01f);
            SetLayerRecursively(propRoot, IgnoreRaycastLayer);

            for (var level = 0; level < stackHeight; level++)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Block {level + 1}";
                block.transform.SetParent(propRoot.transform, false);
                block.transform.localPosition = new Vector3(0f, 0.09f + level * 0.18f, 0f);
                block.transform.localScale = new Vector3(0.24f, 0.18f, 0.24f);
                block.layer = IgnoreRaycastLayer;

                var blockRenderer = block.GetComponent<Renderer>();
                if (blockRenderer != null)
                {
                    blockRenderer.sharedMaterial = _coursePropMaterial;
                    blockRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    blockRenderer.receiveShadows = false;
                }

                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Trim {level + 1}";
                stripe.transform.SetParent(block.transform, false);
                stripe.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                stripe.transform.localScale = new Vector3(0.96f, 0.12f, 0.96f);
                stripe.layer = IgnoreRaycastLayer;

                var stripeCollider = stripe.GetComponent<Collider>();
                if (stripeCollider != null)
                    Destroy(stripeCollider);

                var stripeRenderer = stripe.GetComponent<Renderer>();
                if (stripeRenderer != null)
                {
                    stripeRenderer.sharedMaterial = _coursePropTrimMaterial;
                    stripeRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    stripeRenderer.receiveShadows = false;
                }
            }

            _courseProps.Add(propRoot);
        }

        private void CreateOrUpdateCheckpointMarker()
        {
            if (_checkpointRoot == null)
            {
                var checkpointTemplateInstance = InstantiateTemplate(_checkpointTemplate, "Checkpoint Marker");
                if (checkpointTemplateInstance != null)
                {
                    _checkpointRoot = checkpointTemplateInstance;
                    ApplyCheckpointTemplateMaterials(_checkpointRoot.transform);
                }
            }

            if (_checkpointRoot == null)
            {
                _checkpointPadMaterial ??= CreateColoredMaterial(new Color(0.12f, 0.45f, 0.94f, 0.92f));
                _checkpointOrbMaterial ??= CreateColoredMaterial(new Color(0.99f, 0.93f, 0.64f, 1f));

                _checkpointRoot = new GameObject("Checkpoint Marker");
                SetLayerRecursively(_checkpointRoot, IgnoreRaycastLayer);

                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = "Checkpoint Pad";
                pad.transform.SetParent(_checkpointRoot.transform, false);
                pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                pad.transform.localScale = new Vector3(0.18f, 0.02f, 0.18f);
                pad.layer = IgnoreRaycastLayer;

                var padCollider = pad.GetComponent<Collider>();
                if (padCollider != null)
                    Destroy(padCollider);

                var padRenderer = pad.GetComponent<Renderer>();
                if (padRenderer != null)
                {
                    padRenderer.sharedMaterial = _checkpointPadMaterial;
                    padRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    padRenderer.receiveShadows = false;
                }

                var orbRoot = new GameObject("Checkpoint Orb");
                orbRoot.transform.SetParent(_checkpointRoot.transform, false);
                orbRoot.transform.localPosition = new Vector3(0f, 0.28f, 0f);
                orbRoot.layer = IgnoreRaycastLayer;

                var orbMotion = orbRoot.AddComponent<FloatingItemVisual>();
                orbMotion.Configure(55f, 0.03f, 2.3f);

                var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = "Orb";
                orb.transform.SetParent(orbRoot.transform, false);
                orb.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
                orb.layer = IgnoreRaycastLayer;

                var orbCollider = orb.GetComponent<Collider>();
                if (orbCollider != null)
                    Destroy(orbCollider);

                var orbRenderer = orb.GetComponent<Renderer>();
                if (orbRenderer != null)
                {
                    orbRenderer.sharedMaterial = _checkpointOrbMaterial;
                    orbRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    orbRenderer.receiveShadows = false;
                }
            }

            _checkpointRoot.transform.SetPositionAndRotation(
                _playerCheckpointPosition - Vector3.up * 0.03f,
                Quaternion.LookRotation(_playerCheckpointForward, Vector3.up));
        }

        private void UpdateGameplayInteractions()
        {
            if (_playerRoot == null)
                return;

            var playerCenter = _playerRoot.transform.position + Vector3.up * 0.9f;
            var coinCollectDistanceSqr = CoinCollectDistance * CoinCollectDistance;

            for (var i = 0; i < _coins.Count; i++)
            {
                var coin = _coins[i];
                if (coin.Collected || coin.Transform == null)
                    continue;

                if ((playerCenter - coin.Transform.position).sqrMagnitude > coinCollectDistanceSqr)
                    continue;

                coin.Collected = true;
                _collectedCoins++;

                if (coin.Root != null)
                    Destroy(coin.Root);
            }

            if (_goalTransform == null || _goalRoot == null)
                return;

            var horizontalPlayer = _playerRoot.transform.position;
            horizontalPlayer.y = 0f;
            var horizontalGoal = _goalTransform.position;
            horizontalGoal.y = 0f;

            if ((horizontalPlayer - horizontalGoal).sqrMagnitude > GoalReachDistance * GoalReachDistance)
                return;

            if (Mathf.Abs(_playerRoot.transform.position.y - _goalTransform.position.y) > 1.5f)
                return;

            if (_collectedCoins >= _totalCoins)
                CompleteLevel();
        }

        private void CompleteLevel()
        {
            if (_state == SessionState.Completed)
                return;

            _levelFinishTime = Time.time;
            _state = SessionState.Completed;

            if (_playerController != null)
            {
                _playerController.SetMoveInput(Vector2.zero);
                _playerController.SetMovementEnabled(false);
            }
        }

        private Vector3 ResolveSpawnPosition(Vector3 markerPosition)
        {
            var rayOrigin = markerPosition + Vector3.up * RespawnRayHeight;
            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, RespawnRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;

            return markerPosition + Vector3.up * 0.2f;
        }

        private Vector3 GetSpawnForward(Transform markerTransform)
        {
            var referenceForward = markerTransform.forward;
            var gameplayCamera = GetGameplayCamera();
            if (gameplayCamera != null)
                referenceForward = gameplayCamera.transform.forward;

            referenceForward.y = 0f;
            if (referenceForward.sqrMagnitude < 0.001f)
                referenceForward = Vector3.forward;

            return referenceForward.normalized;
        }

        private void SyncScanMeshCollisionIfNeeded()
        {
            if (_scanMeshRenderer == null || _scanMeshRenderer.RuntimeMeshRoot == null)
                return;

            if (_state != SessionState.Scanning &&
                _state != SessionState.WaitingForMarker &&
                _state != SessionState.Playing &&
                _state != SessionState.Completed)
            {
                return;
            }

            if (Time.unscaledTime < _nextScanCollisionSyncTime)
                return;

            _nextScanCollisionSyncTime = Time.unscaledTime + ScanCollisionSyncInterval;
            EnsureScanMeshCollision();
        }

        private void EnsureScanMeshCollision()
        {
            if (_scanMeshRenderer == null || _scanMeshRenderer.RuntimeMeshRoot == null)
                return;

            var meshFilters = _scanMeshRenderer.RuntimeMeshRoot.GetComponentsInChildren<MeshFilter>(true);
            foreach (var meshFilter in meshFilters)
            {
                var sharedMesh = meshFilter.sharedMesh;
                if (sharedMesh == null)
                    continue;

                var meshCollider = meshFilter.GetComponent<MeshCollider>();
                if (meshCollider == null)
                    meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();

                if (meshCollider.sharedMesh != sharedMesh)
                    meshCollider.sharedMesh = sharedMesh;

                meshCollider.convex = false;
            }
        }

        private void BuildCharacterVisual(Transform parent)
        {
            _playerSkinMaterial ??= CreateColoredMaterial(new Color(0.96f, 0.84f, 0.69f, 1f));
            _playerHatMaterial ??= CreateColoredMaterial(new Color(0.83f, 0.12f, 0.1f, 1f));
            _playerOverallsMaterial ??= CreateColoredMaterial(new Color(0.12f, 0.31f, 0.86f, 1f));
            _playerShoesMaterial ??= CreateColoredMaterial(new Color(0.33f, 0.21f, 0.11f, 1f));
            _playerHairMaterial ??= CreateColoredMaterial(new Color(0.15f, 0.09f, 0.05f, 1f));

            CreateCharacterPart(parent, "Torso", PrimitiveType.Capsule, new Vector3(0f, 0.88f, 0f), new Vector3(0.42f, 0.55f, 0.34f), _playerOverallsMaterial);
            CreateCharacterPart(parent, "Head", PrimitiveType.Sphere, new Vector3(0f, 1.52f, 0f), new Vector3(0.34f, 0.34f, 0.34f), _playerSkinMaterial);
            CreateCharacterPart(parent, "Cap Top", PrimitiveType.Cube, new Vector3(0f, 1.76f, 0f), new Vector3(0.38f, 0.12f, 0.38f), _playerHatMaterial);
            CreateCharacterPart(parent, "Cap Brim", PrimitiveType.Cube, new Vector3(0f, 1.69f, 0.16f), new Vector3(0.42f, 0.04f, 0.16f), _playerHatMaterial);
            CreateCharacterPart(parent, "Mustache", PrimitiveType.Cube, new Vector3(0f, 1.43f, 0.16f), new Vector3(0.18f, 0.05f, 0.06f), _playerHairMaterial);
            CreateCharacterPart(parent, "Nose", PrimitiveType.Sphere, new Vector3(0f, 1.49f, 0.17f), new Vector3(0.09f, 0.09f, 0.09f), _playerSkinMaterial);
            CreateCharacterPart(parent, "Arm Left", PrimitiveType.Capsule, new Vector3(-0.28f, 1.02f, 0f), new Vector3(0.12f, 0.32f, 0.12f), _playerHatMaterial);
            CreateCharacterPart(parent, "Arm Right", PrimitiveType.Capsule, new Vector3(0.28f, 1.02f, 0f), new Vector3(0.12f, 0.32f, 0.12f), _playerHatMaterial);
            CreateCharacterPart(parent, "Leg Left", PrimitiveType.Capsule, new Vector3(-0.12f, 0.38f, 0f), new Vector3(0.14f, 0.34f, 0.14f), _playerOverallsMaterial);
            CreateCharacterPart(parent, "Leg Right", PrimitiveType.Capsule, new Vector3(0.12f, 0.38f, 0f), new Vector3(0.14f, 0.34f, 0.14f), _playerOverallsMaterial);
            CreateCharacterPart(parent, "Shoe Left", PrimitiveType.Cube, new Vector3(-0.12f, 0.05f, 0.07f), new Vector3(0.16f, 0.08f, 0.24f), _playerShoesMaterial);
            CreateCharacterPart(parent, "Shoe Right", PrimitiveType.Cube, new Vector3(0.12f, 0.05f, 0.07f), new Vector3(0.16f, 0.08f, 0.24f), _playerShoesMaterial);
        }

        private void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;

            for (var i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        private void CacheSceneTemplates()
        {
            if (_templateRoot == null)
            {
                var templateRootObject = transform.Find(TemplateRootName)?.gameObject ?? GameObject.Find(TemplateRootName);
                if (templateRootObject != null)
                    _templateRoot = templateRootObject.transform;
            }

            if (_templateRoot == null)
                return;

            _playerTemplate = FindTemplate(PlayerTemplateName);
            _coinTemplate = FindTemplate(CoinTemplateName);
            _goalTemplate = FindTemplate(GoalTemplateName);
            _courseBlockTemplate = FindTemplate(CourseBlockTemplateName);
            _tallCourseBlockTemplate = FindTemplate(TallCourseBlockTemplateName);
            _checkpointTemplate = FindTemplate(CheckpointTemplateName);
        }

        private GameObject FindTemplate(string templateName)
        {
            if (_templateRoot == null)
                return null;

            var templateTransform = _templateRoot.Find(templateName);
            return templateTransform != null ? templateTransform.gameObject : null;
        }

        private GameObject InstantiateTemplate(GameObject template, string instanceName)
        {
            if (template == null)
                return null;

            var instance = Instantiate(template);
            instance.name = instanceName;
            instance.SetActive(true);
            return instance;
        }

        private GameObject InstantiatePlayer(string instanceName)
        {
            if (defaultPlayerPrefab != null)
            {
                var spawnedDefaultPlayer = Instantiate(defaultPlayerPrefab);
                spawnedDefaultPlayer.name = instanceName;
                spawnedDefaultPlayer.SetActive(true);
                return spawnedDefaultPlayer;
            }

            var player = InstantiateTemplate(_playerTemplate, instanceName);
            if (player != null)
                return player;

            return null;
        }

        private void ConfigurePlayerVisuals(GameObject player)
        {
            if (player.transform.childCount == 0)
            {
                BuildCharacterVisual(player.transform);
                ApplyCharacterTemplateMaterials(player.transform, true);
                return;
            }

            var replaceMaterials = UsesPrimitiveCharacterTemplate(player.transform);
            ApplyCharacterTemplateMaterials(player.transform, replaceMaterials);
            EnsurePlayerAnimator(player);
        }

        private void StripPlayerPhysicsColliders(GameObject player, CharacterController rootCharacterController)
        {
            var colliders = player.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider == null || collider == rootCharacterController)
                    continue;

                Destroy(collider);
            }

            var rigidbodies = player.GetComponentsInChildren<Rigidbody>(true);
            foreach (var body in rigidbodies)
                Destroy(body);
        }

        private bool UsesPrimitiveCharacterTemplate(Transform root)
        {
            return root.Find("Torso") != null ||
                   root.Find("Head") != null ||
                   root.Find("Cap Top") != null;
        }

        private void EnsurePlayerAnimator(GameObject player)
        {
            var animator = player.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return;

            animator.applyRootMotion = false;

            var animationDriver = player.GetComponent<PlatformerCharacterAnimator>();
            if (animationDriver == null)
                animationDriver = player.AddComponent<PlatformerCharacterAnimator>();

            animationDriver.RefreshAnimator();
        }

        private void ApplyCharacterTemplateMaterials(Transform root, bool replaceMaterials)
        {
            if (replaceMaterials)
            {
                _playerSkinMaterial ??= CreateColoredMaterial(new Color(0.96f, 0.84f, 0.69f, 1f));
                _playerHatMaterial ??= CreateColoredMaterial(new Color(0.83f, 0.12f, 0.1f, 1f));
                _playerOverallsMaterial ??= CreateColoredMaterial(new Color(0.12f, 0.31f, 0.86f, 1f));
                _playerShoesMaterial ??= CreateColoredMaterial(new Color(0.33f, 0.21f, 0.11f, 1f));
                _playerHairMaterial ??= CreateColoredMaterial(new Color(0.15f, 0.09f, 0.05f, 1f));
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var meshRenderer in renderers)
            {
                if (replaceMaterials)
                {
                    var material = ResolveCharacterMaterial(meshRenderer.gameObject.name);
                    if (material != null)
                        meshRenderer.sharedMaterial = material;
                }

                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }

        private Material ResolveCharacterMaterial(string partName)
        {
            if (partName.Contains("Torso", StringComparison.OrdinalIgnoreCase) ||
                partName.Contains("Leg", StringComparison.OrdinalIgnoreCase))
            {
                return _playerOverallsMaterial;
            }

            if (partName.Contains("Cap", StringComparison.OrdinalIgnoreCase) ||
                partName.Contains("Arm", StringComparison.OrdinalIgnoreCase))
            {
                return _playerHatMaterial;
            }

            if (partName.Contains("Shoe", StringComparison.OrdinalIgnoreCase))
                return _playerShoesMaterial;

            if (partName.Contains("Head", StringComparison.OrdinalIgnoreCase) ||
                partName.Contains("Nose", StringComparison.OrdinalIgnoreCase))
            {
                return _playerSkinMaterial;
            }

            if (partName.Contains("Mustache", StringComparison.OrdinalIgnoreCase))
                return _playerHairMaterial;

            return null;
        }

        private void ApplyCoinTemplateMaterials(Transform root)
        {
            _coinMaterial ??= CreateColoredMaterial(new Color(0.96f, 0.82f, 0.12f, 1f));
            ApplyMaterialToNamedRenderers(root, "Coin", _coinMaterial);
            ApplyMaterialToNamedRenderers(root, "Body", _coinMaterial);
        }

        private void ApplyGoalTemplateMaterials(Transform root)
        {
            _goalPoleMaterial ??= CreateColoredMaterial(new Color(0.88f, 0.89f, 0.92f, 1f));
            _goalFlagMaterial ??= CreateColoredMaterial(new Color(0.17f, 0.72f, 0.34f, 1f));
            _goalOrbMaterial ??= CreateColoredMaterial(new Color(0.98f, 0.94f, 0.62f, 1f));

            ApplyMaterialToNamedRenderers(root, "Pole", _goalPoleMaterial);
            ApplyMaterialToNamedRenderers(root, "Flag", _goalFlagMaterial);
            ApplyMaterialToNamedRenderers(root, "Orb", _goalOrbMaterial);
        }

        private void ApplyCoursePropTemplateMaterials(Transform root)
        {
            _coursePropMaterial ??= CreateColoredMaterial(new Color(0.78f, 0.24f, 0.12f, 1f));
            _coursePropTrimMaterial ??= CreateColoredMaterial(new Color(0.97f, 0.84f, 0.42f, 1f));

            ApplyMaterialToNamedRenderers(root, "Block", _coursePropMaterial);
            ApplyMaterialToNamedRenderers(root, "Trim", _coursePropTrimMaterial);
        }

        private void ApplyCheckpointTemplateMaterials(Transform root)
        {
            _checkpointPadMaterial ??= CreateColoredMaterial(new Color(0.12f, 0.45f, 0.94f, 0.92f));
            _checkpointOrbMaterial ??= CreateColoredMaterial(new Color(0.99f, 0.93f, 0.64f, 1f));

            ApplyMaterialToNamedRenderers(root, "Pad", _checkpointPadMaterial);
            ApplyMaterialToNamedRenderers(root, "Orb", _checkpointOrbMaterial);
        }

        private void ApplyMaterialToNamedRenderers(Transform root, string nameFragment, Material material)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var meshRenderer in renderers)
            {
                if (!meshRenderer.gameObject.name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
                    continue;

                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }

        private Camera GetGameplayCamera()
        {
            if (Camera.main != null)
                return Camera.main;

            if (VuforiaBehaviour.Instance != null &&
                VuforiaBehaviour.Instance.TryGetComponent<Camera>(out var vuforiaCamera))
            {
                return vuforiaCamera;
            }

            return FindFirstObjectByType<Camera>();
        }

        private bool CanFinishScan()
        {
            if (_capture == null)
                return false;

            return _capture.Status == AreaTargetCaptureStatus.CAPTURING ||
                   _capture.Status == AreaTargetCaptureStatus.PAUSED;
        }

        private bool IsTracked(TargetStatus targetStatus)
        {
            return targetStatus.Status == Status.TRACKED ||
                   targetStatus.Status == Status.EXTENDED_TRACKED;
        }

        private bool ShouldRefreshCheckpointFromMarker(Transform markerTransform)
        {
            if (!_hasMarkerCheckpointPose)
                return true;

            var markerPosition = markerTransform.position;
            if ((markerPosition - _lastMarkerCheckpointPosition).sqrMagnitude > MarkerCheckpointRefreshDistanceSqr)
                return true;

            var markerForward = GetSpawnForward(markerTransform);
            return Vector3.Dot(markerForward, _lastMarkerCheckpointForward) < MarkerCheckpointRefreshForwardDotThreshold;
        }

        private void HandlePlayerRespawnRequested()
        {
            RespawnPlayer();
        }

        private void RespawnPlayer()
        {
            if (!_playerSpawned || _playerController == null || _state != SessionState.Playing)
                return;

            if (_markerTracked && _imageTarget != null)
                UpdateCheckpointFromMarker(_imageTarget.transform);

            _playerController.RespawnToCheckpoint();
        }

        private void SetError(string message)
        {
            _state = SessionState.Error;
            if (_headlineText != null)
                _headlineText.text = "AR Platformer Setup Error";
            if (_hintText != null)
                _hintText.text = message;
            RefreshUi();
        }

        private void DisableBuiltInCaptureUi()
        {
            if (_capture == null)
                return;

            var captureCanvases = _capture.GetComponentsInChildren<Canvas>(true);
            foreach (var captureCanvas in captureCanvases)
                captureCanvas.gameObject.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

            var standaloneInputModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (standaloneInputModule != null)
                Destroy(standaloneInputModule);
        }

        private void UpdateUiIfNeeded()
        {
            var needsLiveRefresh =
                _state == SessionState.Scanning ||
                _state == SessionState.Playing ||
                _state == SessionState.Completed;

            if (!needsLiveRefresh || Time.unscaledTime < _nextUiRefreshTime)
                return;

            RefreshUi();
        }

        private void SetScanMeshVisible(bool isVisible)
        {
            if (_scanMeshRenderer == null || _scanMeshRenderer.RuntimeMeshRoot == null)
                return;

            var renderers = _scanMeshRenderer.RuntimeMeshRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var meshRenderer in renderers)
            {
                meshRenderer.enabled = isVisible;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }

        private Material CreatePreviewMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");

            var material = new Material(shader);
            var color = new Color(0.15f, 0.95f, 0.78f, 0.28f);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private Material CreatePlayerMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard") ??
                         Shader.Find("Unlit/Color");

            var material = new Material(shader);
            var color = new Color(0.87f, 0.18f, 0.15f, 1f);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            return material;
        }

        private Material CreateColoredMaterial(Color color)
        {
            var material = CreatePlayerMaterial();

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            return material;
        }

        private void CreateCharacterPart(
            Transform parent,
            string partName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            var part = GameObject.CreatePrimitive(primitiveType);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;

            var collider = part.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = part.GetComponent<Renderer>();
            if (renderer == null)
                return;

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private Texture2D LoadMarkerTexture()
        {
            var filePath = Path.Combine(Application.streamingAssetsPath, MarkerRelativePath);
            if (File.Exists(filePath))
            {
                var bytes = File.ReadAllBytes(filePath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.name = "PlatformerMarker";
                texture.LoadImage(bytes, false);
                return texture;
            }

            return GenerateFallbackMarkerTexture(768);
        }

        private Texture2D GenerateFallbackMarkerTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GeneratedPlatformerMarker",
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

        private void DrawCornerMarker(Texture2D texture, int startX, int startY, int size, Color32 outer, Color32 inner)
        {
            FillRect(texture, startX, startY, size * 3, size * 3, outer);
            FillRect(texture, startX + size / 2, startY + size / 2, size * 2, size * 2, new Color32(245, 244, 238, 255));
            FillRect(texture, startX + size, startY + size, size, size, inner);
        }

        private void FillRect(Texture2D texture, int x, int y, int width, int height, Color32 color)
        {
            for (var yIndex = y; yIndex < y + height; yIndex++)
            {
                for (var xIndex = x; xIndex < x + width; xIndex++)
                    texture.SetPixel(xIndex, yIndex, color);
            }
        }

        private void BuildUi()
        {
            _canvas = CreateCanvas();
            var root = (RectTransform)_canvas.transform;

            var topPanel = CreatePanel(root, "Top Panel", new Color(0.05f, 0.08f, 0.1f, 0.72f));
            Stretch((RectTransform)topPanel.transform, new Vector2(0.04f, 0.78f), new Vector2(0.96f, 0.97f), Vector2.zero, Vector2.zero);

            _headlineText = CreateText(topPanel.transform, "Headline", 32, FontStyle.Bold, TextAnchor.UpperLeft);
            Stretch((RectTransform)_headlineText.transform, new Vector2(0.04f, 0.48f), new Vector2(0.6f, 0.9f), Vector2.zero, Vector2.zero);

            _hintText = CreateText(topPanel.transform, "Hint", 24, FontStyle.Normal, TextAnchor.UpperLeft);
            Stretch((RectTransform)_hintText.transform, new Vector2(0.04f, 0.08f), new Vector2(0.78f, 0.5f), Vector2.zero, Vector2.zero);
            _hintText.color = new Color(0.88f, 0.92f, 0.95f, 1f);

            _statsText = CreateText(topPanel.transform, "Stats", 24, FontStyle.Bold, TextAnchor.UpperRight);
            Stretch((RectTransform)_statsText.transform, new Vector2(0.56f, 0.5f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero);
            _statsText.color = new Color(0.95f, 0.88f, 0.53f, 1f);

            _finishScanButton = CreateButton(root, "Finish Scan", new Vector2(280f, 96f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Color(0.12f, 0.54f, 0.44f, 0.92f));
            _finishScanButton.onClick.AddListener(HandleFinishScanPressed);

            _resetButton = CreateButton(root, "Reset Session", new Vector2(220f, 84f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-150f, -120f), new Color(0.66f, 0.25f, 0.16f, 0.92f), 24);
            _resetButton.onClick.AddListener(RestartSession);

            _markerPreviewRoot = CreatePanel(root, "Marker Preview", new Color(0.05f, 0.08f, 0.1f, 0.78f));
            Stretch((RectTransform)_markerPreviewRoot.transform, new Vector2(0.3f, 0.15f), new Vector2(0.7f, 0.52f), Vector2.zero, Vector2.zero);

            _markerPreview = new GameObject("Marker Texture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _markerPreview.transform.SetParent(_markerPreviewRoot.transform, false);
            Stretch((RectTransform)_markerPreview.transform, new Vector2(0.18f, 0.24f), new Vector2(0.82f, 0.9f), Vector2.zero, Vector2.zero);
            _markerPreview.color = Color.white;

            var markerText = CreateText(_markerPreviewRoot.transform, "Marker Hint", 24, FontStyle.Normal, TextAnchor.UpperCenter);
            markerText.text = "Track this marker from a printout or a second screen to spawn the character.";
            Stretch((RectTransform)markerText.transform, new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.22f), Vector2.zero, Vector2.zero);
            markerText.alignment = TextAnchor.MiddleCenter;

            _gameplayControlsRoot = new GameObject("Gameplay Controls", typeof(RectTransform));
            _gameplayControlsRoot.transform.SetParent(root, false);
            Stretch((RectTransform)_gameplayControlsRoot.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _joystick = CreateJoystick(_gameplayControlsRoot.transform);
            _respawnButton = CreateButton(
                _gameplayControlsRoot.transform,
                "Respawn",
                new Vector2(180f, 84f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 160f),
                new Color(0.15f, 0.39f, 0.72f, 0.92f),
                26);
            _respawnButton.onClick.AddListener(RespawnPlayer);

            _jumpButton = CreateButton(
                _gameplayControlsRoot.transform,
                "Jump",
                new Vector2(180f, 180f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-160f, 160f),
                new Color(0.93f, 0.54f, 0.17f, 0.92f),
                30);
            _jumpButton.onClick.AddListener(() =>
            {
                if (_playerController != null)
                    _playerController.QueueJump();
            });
        }

        private Canvas CreateCanvas()
        {
            var canvasObject = new GameObject("AR Platformer Canvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            var image = panel.GetComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Sliced;
            image.color = color;

            return panel;
        }

        private Button CreateButton(
            Transform parent,
            string label,
            Vector2 size,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Color backgroundColor,
            int fontSize = 28)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rectTransform = (RectTransform)buttonObject.transform;
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = anchoredPosition;

            var image = buttonObject.GetComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.color = backgroundColor;

            var text = CreateText(buttonObject.transform, "Label", fontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch((RectTransform)text.transform, Vector2.zero, Vector2.one, new Vector2(20f, 16f), new Vector2(-20f, -16f));

            return buttonObject.GetComponent<Button>();
        }

        private TouchJoystick CreateJoystick(Transform parent)
        {
            var joystickObject = new GameObject("Move Joystick", typeof(RectTransform), typeof(Image), typeof(TouchJoystick));
            joystickObject.transform.SetParent(parent, false);

            var rootRect = (RectTransform)joystickObject.transform;
            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(0f, 0f);
            rootRect.sizeDelta = new Vector2(220f, 220f);
            rootRect.anchoredPosition = new Vector2(160f, 160f);

            var background = joystickObject.GetComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = new Color(0.04f, 0.08f, 0.11f, 0.56f);

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(joystickObject.transform, false);

            var handleRect = (RectTransform)handleObject.transform;
            handleRect.sizeDelta = new Vector2(96f, 96f);

            var handleImage = handleObject.GetComponent<Image>();
            handleImage.sprite = GetWhiteSprite();
            handleImage.color = new Color(0.92f, 0.77f, 0.36f, 0.96f);

            var joystick = joystickObject.GetComponent<TouchJoystick>();
            joystick.Configure(handleRect, 72f);
            return joystick;
        }

        private Text CreateText(Transform parent, string name, int fontSize, FontStyle fontStyle, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = GetDefaultFont();
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.supportRichText = false;

            return text;
        }

        private void Stretch(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }

        private Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null)
                return _whiteSprite;

            _whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f));

            return _whiteSprite;
        }

        private Font GetDefaultFont()
        {
            _defaultFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _defaultFont;
        }

        private void RefreshUi()
        {
            if (_headlineText == null || _hintText == null)
                return;

            _nextUiRefreshTime = Time.unscaledTime + UiRefreshInterval;

            switch (_state)
            {
                case SessionState.Booting:
                    _headlineText.text = "Preparing Vuforia";
                    _hintText.text = "Waiting for the AR session and room capture pipeline to start.";
                    break;

                case SessionState.Scanning:
                    _headlineText.text = $"Scan Room ({_capture?.Status.ToString() ?? "Unknown"})";
                    _hintText.text = BuildScanningHint();
                    break;

                case SessionState.WaitingForMarker:
                    _headlineText.text = _markerTracked ? "Marker Found" : "Find Spawn Marker";
                    _hintText.text = "Point the camera at the marker to place the player into the scanned room. The room mesh stays as collision only for better performance.";
                    break;

                case SessionState.Playing:
                    _headlineText.text = "AR Platformer Running";
                    _hintText.text = _collectedCoins >= _totalCoins && _totalCoins > 0
                        ? "All coins collected. Reach the flag to clear the level."
                        : "Collect every coin, then reach the flag. Use Respawn if the hero falls off the scanned room.";
                    break;

                case SessionState.Completed:
                    _headlineText.text = "Course Clear";
                    _hintText.text = $"You collected {_collectedCoins}/{_totalCoins} coins. Reset Session to scan a new room and generate a fresh layout.";
                    break;

                case SessionState.Error:
                    if (string.IsNullOrWhiteSpace(_headlineText.text))
                        _headlineText.text = "AR Platformer Setup Error";
                    break;
            }

            if (_finishScanButton != null)
                _finishScanButton.gameObject.SetActive(_state == SessionState.Scanning);

            if (_markerPreviewRoot != null)
                _markerPreviewRoot.SetActive(_state == SessionState.WaitingForMarker);

            if (_gameplayControlsRoot != null)
                _gameplayControlsRoot.SetActive(_state == SessionState.Playing);

            if (_statsText != null)
            {
                var displayTime = _levelFinishTime > 0f
                    ? _levelFinishTime - _levelStartTime
                    : Mathf.Max(0f, Time.time - _levelStartTime);

                if (_state == SessionState.Playing || _state == SessionState.Completed)
                    _statsText.text = $"Coins  {_collectedCoins}/{_totalCoins}\nTime   {displayTime:0.0}s";
                else
                    _statsText.text = string.Empty;
            }
        }

        private string BuildScanningHint()
        {
            if (_capture == null)
                return "AreaTargetCapture is missing from the scene.";

            return _capture.StatusInfo switch
            {
                AreaTargetCaptureStatusInfo.RELOCALIZING =>
                    "Vuforia is relocalizing. Move back toward an area you already scanned and keep textured surfaces in view.",
                AreaTargetCaptureStatusInfo.EXCESSIVE_MOTION =>
                    "Move more slowly and keep the phone steadier so the room mesh stays stable.",
                AreaTargetCaptureStatusInfo.CAPACITY_WARNING =>
                    "The scan has enough data. Finish soon or reset and scan a smaller play area for better performance.",
                AreaTargetCaptureStatusInfo.INTERRUPTED =>
                    "Tracking was interrupted. Point back at the floor, furniture edges, and textured parts of the room.",
                _ when CanFinishScan() =>
                    "The room mesh is ready. Keep scanning until the floor and obstacle tops are covered, then tap Finish Scan.",
                _ =>
                    "Walk around the play area and capture the floor plus furniture surfaces your hero should be able to stand on."
            };
        }
    }
}
