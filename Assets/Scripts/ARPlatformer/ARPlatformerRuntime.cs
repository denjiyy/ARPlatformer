using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using Vuforia;

namespace ARPlatformer
{
    public sealed class ARPlatformerRuntime : MonoBehaviour
    {
        private const string MarkerRelativePath = "Markers/platformer-marker.png";
        private const string MarkerTargetName = "PlatformerSpawnMarker";
        private const float ResumeValidationTimeoutSeconds = 1.5f;

        private enum SessionState
        {
            Booting,
            Scanning,
            WaitingForMarker,
            Playing,
            Completed,
            Error
        }

        private enum ScanMeshRenderMode
        {
            Hidden,
            Preview,
            Occlusion
        }

        private SessionState _state = SessionState.Booting;

        private AreaTargetCaptureBehaviour _capture;
        private RuntimeMeshRenderingBehaviour _scanMeshRenderer;
        private ImageTargetBehaviour _imageTarget;
        private Texture2D _markerTexture;
        private Material _scanMeshPreviewMaterial;
        private Material _scanMeshOcclusionMaterial;
        [SerializeField] private GameObject defaultPlayerPrefab;
        [SerializeField] private ARPlatformerRuntimeConfig runtimeConfig;
        private readonly ARPlatformerContentFactory _contentFactory = new();
        private readonly ARPlatformerRuntimeUi _runtimeUi = new();
        private ARPlatformerRuntimeConfig _fallbackRuntimeConfig;
        private ARPlatformerGameplaySession _gameplaySession;

        private bool _markerTracked;
        private bool _appWasBackgrounded;
        private Coroutine _sessionRoutine;
        private Coroutine _resumeValidationRoutine;
        private float _nextScanCollisionSyncTime;
        private float _nextUiRefreshTime;
        private string _errorMessage;

        private ARPlatformerRuntimeConfig Config => runtimeConfig != null
            ? runtimeConfig
            : _fallbackRuntimeConfig ??= ARPlatformerRuntimeConfig.CreateTransientDefaults();

        private void Awake()
        {
            name = "AR Platformer Runtime";
            ApplyPerformanceDefaults();
            EnsureEventSystem();
            _contentFactory.CacheSceneTemplates(transform);
            _gameplaySession = new ARPlatformerGameplaySession(_contentFactory, Config, defaultPlayerPrefab, HandlePlayerRespawnRequested);
            _runtimeUi.Build(transform, HandleFinishScanPressed, RestartSession, RespawnPlayer, QueueJumpFromUi);
            RefreshUi();
        }

        private void Start()
        {
            RestartSession();
        }

        private void Update()
        {
            SyncScanMeshCollisionIfNeeded();

            if (_runtimeUi.Joystick != null)
                _gameplaySession?.SetMoveInput(_runtimeUi.Joystick.Value);

            if ((_state == SessionState.Playing || _state == SessionState.Completed) &&
                _markerTracked &&
                _imageTarget != null &&
                _gameplaySession != null)
            {
                var gameplayCamera = GetGameplayCamera();
                if (_gameplaySession.ShouldRefreshCheckpointFromMarker(_imageTarget.transform, gameplayCamera))
                    _gameplaySession.UpdateCheckpointFromMarker(_imageTarget.transform, gameplayCamera);
            }

            if (_state == SessionState.Playing &&
                _gameplaySession != null &&
                _gameplaySession.UpdateGameplayInteractions())
            {
                CompleteLevel();
            }

            UpdateUiIfNeeded();
        }

        private void OnDestroy()
        {
            if (_resumeValidationRoutine != null)
                StopCoroutine(_resumeValidationRoutine);

            CleanupRuntimeObjects();

            DestroyObject(ref _scanMeshPreviewMaterial);
            DestroyObject(ref _scanMeshOcclusionMaterial);
            DestroyObject(ref _fallbackRuntimeConfig);
            _runtimeUi.Dispose();
            _gameplaySession = null;
            _contentFactory.Dispose();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                HandleAppBackgrounded();
                return;
            }

            HandleAppResumed();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                HandleAppBackgrounded();
                return;
            }

            HandleAppResumed();
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

            if (_resumeValidationRoutine != null)
            {
                StopCoroutine(_resumeValidationRoutine);
                _resumeValidationRoutine = null;
            }

            _sessionRoutine = StartCoroutine(RestartSessionRoutine());
        }

        private IEnumerator RestartSessionRoutine()
        {
            _state = SessionState.Booting;
            RefreshUi();

            _contentFactory.CacheSceneTemplates(transform);
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
            var timeoutAt = Time.realtimeSinceStartup + Config.VuforiaStartupTimeoutSeconds;
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

            _scanMeshPreviewMaterial ??= CreatePreviewMaterial();
            var runtimeMeshObject = VuforiaBehaviour.Instance.ObserverFactory.CreateRuntimeMeshRenderingBehaviour(
                _capture,
                _scanMeshPreviewMaterial,
                true);
            _scanMeshRenderer = runtimeMeshObject != null
                ? runtimeMeshObject.GetComponent<RuntimeMeshRenderingBehaviour>()
                : null;

            if (_scanMeshRenderer != null)
            {
                runtimeMeshObject.name = "Platformer Scan Mesh";
                SetScanMeshRenderMode(ScanMeshRenderMode.Preview);
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
            _appWasBackgrounded = false;
            _nextScanCollisionSyncTime = 0f;
            _nextUiRefreshTime = 0f;
            _errorMessage = string.Empty;
            _gameplaySession?.ResetSession();

            SafeDestroyCapture();
            _capture = null;
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

            SetScanMeshRenderMode(ScanMeshRenderMode.Occlusion);

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
                    Config.MarkerWidthMeters,
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
            return true;
        }

        private void HandleImageTargetStatusChanged(ObserverBehaviour observerBehaviour, TargetStatus targetStatus)
        {
            _markerTracked = IsTracked(targetStatus);

            if (_markerTracked && _gameplaySession != null)
            {
                var gameplayCamera = GetGameplayCamera();

                if (!_gameplaySession.PlayerSpawned)
                {
                    _gameplaySession.SpawnPlayer(observerBehaviour.transform, gameplayCamera);
                    _state = SessionState.Playing;
                }
                else
                {
                    _gameplaySession.UpdateCheckpointFromMarker(observerBehaviour.transform, gameplayCamera);
                }
            }

            RefreshUi();
        }

        private void CompleteLevel()
        {
            if (_state == SessionState.Completed)
                return;

            _gameplaySession?.CompleteLevel();
            _state = SessionState.Completed;
        }

        private void SyncScanMeshCollisionIfNeeded()
        {
            if (_scanMeshRenderer == null || _scanMeshRenderer.RuntimeMeshRoot == null)
                return;

            if (_state != SessionState.Scanning &&
                _state != SessionState.WaitingForMarker)
            {
                return;
            }

            if (Time.unscaledTime < _nextScanCollisionSyncTime)
                return;

            _nextScanCollisionSyncTime = Time.unscaledTime + Config.ScanCollisionSyncInterval;
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

        private void HandlePlayerRespawnRequested()
        {
            RespawnPlayer();
        }

        private void DestroyObject<T>(ref T instance) where T : UnityEngine.Object
        {
            if (instance == null)
                return;

            Destroy(instance);
            instance = null;
        }

        private void RespawnPlayer()
        {
            if (_state != SessionState.Playing || _gameplaySession == null || !_gameplaySession.PlayerSpawned)
                return;

            _gameplaySession.RespawnPlayer(_imageTarget != null ? _imageTarget.transform : null, _markerTracked, GetGameplayCamera());
        }

        private void SetError(string message)
        {
            _state = SessionState.Error;
            _errorMessage = message;
            RefreshUi();
        }

        private void QueueJumpFromUi()
        {
            _gameplaySession?.QueueJump();
        }

        private void DisableBuiltInCaptureUi()
        {
            if (_capture == null)
                return;

            var captureCanvases = _capture.GetComponentsInChildren<Canvas>(true);
            foreach (var captureCanvas in captureCanvases)
                captureCanvas.gameObject.SetActive(false);
        }

        private void HandleAppBackgrounded()
        {
            _appWasBackgrounded = true;
        }

        private void HandleAppResumed()
        {
            if (!_appWasBackgrounded)
                return;

            _appWasBackgrounded = false;

            if (_resumeValidationRoutine != null)
                StopCoroutine(_resumeValidationRoutine);

            _resumeValidationRoutine = StartCoroutine(ValidateSessionAfterResume());
        }

        private IEnumerator ValidateSessionAfterResume()
        {
            var timeoutAt = Time.realtimeSinceStartup + ResumeValidationTimeoutSeconds;
            while (VuforiaApplication.Instance != null &&
                   !VuforiaApplication.Instance.IsRunning &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            _resumeValidationRoutine = null;

            if (ShouldRestartSessionAfterResume())
            {
                RestartSession();
                yield break;
            }

            RefreshUi();
        }

        private bool ShouldRestartSessionAfterResume()
        {
            if (_state == SessionState.Booting || _state == SessionState.Error)
                return false;

            if (VuforiaApplication.Instance == null || !VuforiaApplication.Instance.IsRunning)
                return true;

            if (_scanMeshRenderer == null || _scanMeshRenderer.RuntimeMeshRoot == null)
                return true;

            if (_state == SessionState.WaitingForMarker ||
                _state == SessionState.Playing ||
                _state == SessionState.Completed)
            {
                return _imageTarget == null;
            }

            return false;
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

        private void SetScanMeshRenderMode(ScanMeshRenderMode renderMode)
        {
            if (_scanMeshRenderer == null || _scanMeshRenderer.RuntimeMeshRoot == null)
                return;

            var shouldRender = renderMode != ScanMeshRenderMode.Hidden;
            Material targetMaterial = null;
            if (renderMode == ScanMeshRenderMode.Preview)
                targetMaterial = _scanMeshPreviewMaterial ??= CreatePreviewMaterial();
            else if (renderMode == ScanMeshRenderMode.Occlusion)
                targetMaterial = _scanMeshOcclusionMaterial ??= CreateOcclusionMaterial();

            var renderers = _scanMeshRenderer.RuntimeMeshRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var meshRenderer in renderers)
            {
                meshRenderer.enabled = shouldRender;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;

                if (shouldRender && targetMaterial != null && meshRenderer.sharedMaterial != targetMaterial)
                    meshRenderer.sharedMaterial = targetMaterial;
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

        private Material CreateOcclusionMaterial()
        {
            var shader = Shader.Find("Hidden/Internal-Colored") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");

            var material = new Material(shader);
            var invisibleColor = new Color(0f, 0f, 0f, 0f);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", invisibleColor);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", invisibleColor);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 0f);

            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Back);

            if (material.HasProperty("_ZTest"))
                material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);

            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 1f);

            material.renderQueue = (int)RenderQueue.Geometry - 10;
            return material;
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

            return PlatformerMarkerTextureFactory.CreateTexture(768, "GeneratedPlatformerMarker");
        }

        private void RefreshUi()
        {
            _nextUiRefreshTime = Time.unscaledTime + Config.UiRefreshInterval;
            _runtimeUi.Apply(CreateUiModel());
        }

        private ARPlatformerRuntimeUiModel CreateUiModel()
        {
            string headline;
            string hint;

            switch (_state)
            {
                case SessionState.Booting:
                    headline = "Preparing Vuforia";
                    hint = "Waiting for the AR session and room capture pipeline to start.";
                    break;

                case SessionState.Scanning:
                    headline = $"Scan Room ({_capture?.Status.ToString() ?? "Unknown"})";
                    hint = BuildScanningHint();
                    break;

                case SessionState.WaitingForMarker:
                    headline = _markerTracked ? "Marker Found" : "Find Spawn Marker";
                    hint = "Point the camera at the marker to place the player into the scanned room. The room mesh stays invisible but still drives real-world occlusion and collision.";
                    break;

                case SessionState.Playing:
                    headline = "AR Platformer Running";
                    hint = _gameplaySession != null && _gameplaySession.AllCoinsCollected
                        ? "All coins collected. Reach the flag to clear the level."
                        : "Collect every coin, then reach the flag. Use Respawn if the hero falls off the scanned room.";
                    break;

                case SessionState.Completed:
                    headline = "Course Clear";
                    hint = _gameplaySession == null
                        ? "Reset Session to scan a new room and generate a fresh layout."
                        : $"You collected {_gameplaySession.CollectedCoins}/{_gameplaySession.TotalCoins} coins. Reset Session to scan a new room and generate a fresh layout.";
                    break;

                case SessionState.Error:
                    headline = "AR Platformer Setup Error";
                    hint = string.IsNullOrWhiteSpace(_errorMessage)
                        ? "An unexpected error interrupted the AR platformer session."
                        : _errorMessage;
                    break;

                default:
                    headline = string.Empty;
                    hint = string.Empty;
                    break;
            }

            var stats = string.Empty;
            var displayTime = _gameplaySession?.GetDisplayTime(Time.time) ?? 0f;

            if ((_state == SessionState.Playing || _state == SessionState.Completed) && _gameplaySession != null)
                stats = $"Coins  {_gameplaySession.CollectedCoins}/{_gameplaySession.TotalCoins}\nTime   {displayTime:0.0}s";

            return new ARPlatformerRuntimeUiModel(
                headline,
                hint,
                stats,
                _state == SessionState.Scanning,
                CanFinishScan(),
                _state == SessionState.WaitingForMarker,
                _markerTexture,
                _state == SessionState.Playing,
                _gameplaySession != null && _gameplaySession.PlayerSpawned && _state == SessionState.Playing);
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
                    "The room mesh is ready. Keep scanning until the floor and obstacle tops are covered, then tap Finish Scan to switch to occlusion and collision mode.",
                _ =>
                    "Walk around the play area and capture the floor plus furniture surfaces your hero should be able to stand on."
            };
        }
    }
}
