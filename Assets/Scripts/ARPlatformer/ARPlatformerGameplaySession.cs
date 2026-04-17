using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPlatformer
{
    public sealed class ARPlatformerGameplaySession : IDisposable
    {
        private sealed class CoinInstance
        {
            public GameObject Root;
            public Transform Transform;
            public bool Collected;
        }

        private readonly ARPlatformerContentFactory _contentFactory;
        private readonly ARPlatformerRuntimeConfig _config;
        private readonly GameObject _defaultPlayerPrefab;
        private readonly Action _respawnRequestedHandler;
        private readonly List<CoinInstance> _coins = new();
        private readonly List<GameObject> _courseProps = new();

        private PlatformerCharacterController _playerController;
        private GameObject _playerRoot;
        private GameObject _goalRoot;
        private Transform _goalTransform;
        private GameObject _checkpointRoot;
        private Vector3 _playerCheckpointPosition;
        private Vector3 _playerCheckpointForward = Vector3.forward;
        private int _collectedCoins;
        private int _totalCoins;
        private float _levelStartTime;
        private float _levelFinishTime;
        private bool _hasMarkerCheckpointPose;
        private Vector3 _lastMarkerCheckpointPosition;
        private Vector3 _lastMarkerCheckpointForward = Vector3.forward;

        public ARPlatformerGameplaySession(
            ARPlatformerContentFactory contentFactory,
            ARPlatformerRuntimeConfig config,
            GameObject defaultPlayerPrefab,
            Action respawnRequestedHandler)
        {
            _contentFactory = contentFactory;
            _config = config;
            _defaultPlayerPrefab = defaultPlayerPrefab;
            _respawnRequestedHandler = respawnRequestedHandler;
        }

        public PlatformerCharacterController PlayerController => _playerController;
        public bool PlayerSpawned => _playerRoot != null;
        public int CollectedCoins => _collectedCoins;
        public int TotalCoins => _totalCoins;
        public bool AllCoinsCollected => _collectedCoins >= _totalCoins;

        public void Dispose()
        {
            ResetSession();
        }

        public void ResetSession()
        {
            ClearPlayer();
            ClearGameplayLayout();

            _playerCheckpointPosition = Vector3.zero;
            _playerCheckpointForward = Vector3.forward;
            _collectedCoins = 0;
            _totalCoins = 0;
            _levelStartTime = 0f;
            _levelFinishTime = 0f;
            _hasMarkerCheckpointPose = false;
            _lastMarkerCheckpointPosition = Vector3.zero;
            _lastMarkerCheckpointForward = Vector3.forward;
        }

        public void SetMoveInput(Vector2 moveInput)
        {
            _playerController?.SetMoveInput(moveInput);
        }

        public void QueueJump()
        {
            _playerController?.QueueJump();
        }

        public bool ShouldRefreshCheckpointFromMarker(Transform markerTransform, Camera gameplayCamera)
        {
            if (markerTransform == null)
                return false;

            if (!_hasMarkerCheckpointPose)
                return true;

            var markerPosition = markerTransform.position;
            if ((markerPosition - _lastMarkerCheckpointPosition).sqrMagnitude > _config.MarkerCheckpointRefreshDistanceSqr)
                return true;

            var markerForward = GetSpawnForward(markerTransform, gameplayCamera);
            return Vector3.Dot(markerForward, _lastMarkerCheckpointForward) < _config.MarkerCheckpointRefreshForwardDotThreshold;
        }

        public void UpdateCheckpointFromMarker(Transform markerTransform, Camera gameplayCamera)
        {
            if (markerTransform == null)
                return;

            _playerCheckpointPosition = ResolveSpawnPosition(markerTransform.position);
            _playerCheckpointForward = GetSpawnForward(markerTransform, gameplayCamera);
            _lastMarkerCheckpointPosition = markerTransform.position;
            _lastMarkerCheckpointForward = _playerCheckpointForward;
            _hasMarkerCheckpointPose = true;

            if (_playerController != null)
                _playerController.SetCheckpoint(_playerCheckpointPosition, _playerCheckpointForward);

            if (_checkpointRoot != null)
                CreateOrUpdateCheckpointMarker();
        }

        public void SpawnPlayer(Transform markerTransform, Camera gameplayCamera)
        {
            if (markerTransform == null)
                return;

            ClearPlayer();
            UpdateCheckpointFromMarker(markerTransform, gameplayCamera);

            var player = _contentFactory.InstantiatePlayer(_defaultPlayerPrefab, "Hero");
            if (player == null)
                player = new GameObject("Hero");

            player.transform.SetPositionAndRotation(
                _playerCheckpointPosition,
                Quaternion.LookRotation(_playerCheckpointForward, Vector3.up));

            var characterController = player.GetComponent<CharacterController>();
            if (characterController == null)
                characterController = player.AddComponent<CharacterController>();

            PlatformerCharacterControllerDefaults.Apply(characterController);

            _playerController = player.GetComponent<PlatformerCharacterController>();
            if (_playerController == null)
                _playerController = player.AddComponent<PlatformerCharacterController>();

            _contentFactory.SanitizePlayerHierarchy(player, characterController);

            _playerController.Configure(
                gameplayCamera != null ? gameplayCamera.transform : null,
                _config.CharacterMoveSpeed,
                _config.CharacterJumpHeight,
                _config.CharacterGravity);
            _playerController.SetCheckpoint(_playerCheckpointPosition, _playerCheckpointForward);
            _playerController.SetMovementEnabled(true);

            if (_respawnRequestedHandler != null)
                _playerController.RespawnRequested += _respawnRequestedHandler;

            _contentFactory.ConfigurePlayerVisuals(player);

            _playerRoot = player;
            GenerateGameplayLayout();
        }

        public void RespawnPlayer(Transform markerTransform, bool markerTracked, Camera gameplayCamera)
        {
            if (!PlayerSpawned || _playerController == null)
                return;

            if (markerTracked && markerTransform != null)
                UpdateCheckpointFromMarker(markerTransform, gameplayCamera);

            _playerController.RespawnToCheckpoint();
        }

        public bool UpdateGameplayInteractions()
        {
            if (_playerRoot == null)
                return false;

            var playerCenter = _playerRoot.transform.position + Vector3.up * PlatformerCharacterControllerDefaults.Center.y;
            var coinCollectDistanceSqr = _config.CoinCollectDistance * _config.CoinCollectDistance;

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
                    UnityEngine.Object.Destroy(coin.Root);
            }

            if (_goalTransform == null || _goalRoot == null)
                return false;

            var horizontalPlayer = _playerRoot.transform.position;
            horizontalPlayer.y = 0f;
            var horizontalGoal = _goalTransform.position;
            horizontalGoal.y = 0f;

            if ((horizontalPlayer - horizontalGoal).sqrMagnitude > _config.GoalReachDistance * _config.GoalReachDistance)
                return false;

            if (Mathf.Abs(_playerRoot.transform.position.y - _goalTransform.position.y) > _config.GoalReachHeightTolerance)
                return false;

            return _collectedCoins >= _totalCoins;
        }

        public void CompleteLevel()
        {
            _levelFinishTime = Time.time;

            if (_playerController == null)
                return;

            _playerController.SetMoveInput(Vector2.zero);
            _playerController.SetMovementEnabled(false);
        }

        public float GetDisplayTime(float currentTime)
        {
            return _levelFinishTime > 0f
                ? _levelFinishTime - _levelStartTime
                : Mathf.Max(0f, currentTime - _levelStartTime);
        }

        private void ClearPlayer()
        {
            if (_playerController != null && _respawnRequestedHandler != null)
                _playerController.RespawnRequested -= _respawnRequestedHandler;

            if (_playerRoot != null)
                UnityEngine.Object.Destroy(_playerRoot);

            _playerRoot = null;
            _playerController = null;
        }

        private void ClearGameplayLayout()
        {
            foreach (var coin in _coins)
            {
                if (coin.Root != null)
                    UnityEngine.Object.Destroy(coin.Root);
            }

            _coins.Clear();

            foreach (var courseProp in _courseProps)
            {
                if (courseProp != null)
                    UnityEngine.Object.Destroy(courseProp);
            }

            _courseProps.Clear();

            if (_goalRoot != null)
            {
                UnityEngine.Object.Destroy(_goalRoot);
                _goalRoot = null;
                _goalTransform = null;
            }

            if (_checkpointRoot != null)
            {
                UnityEngine.Object.Destroy(_checkpointRoot);
                _checkpointRoot = null;
            }
        }

        private void GenerateGameplayLayout()
        {
            ClearGameplayLayout();

            var sampledSurfaces = ARPlatformerGameplayLayoutPlanner.SampleSurfacePoints(
                _playerCheckpointPosition,
                _playerCheckpointForward,
                _config.RespawnRayHeight,
                _config.RespawnRayDistance,
                _config.SurfaceHoverHeight,
                _config.SurfaceSampleSpacing,
                _config.MinSurfaceSpacing,
                _config.SurfaceGridHalfExtent);
            var layoutPlan = ARPlatformerGameplayLayoutPlanner.CreatePlan(
                sampledSurfaces,
                _playerCheckpointPosition,
                _config.CoinCountTarget,
                _config.CoursePropTarget,
                _config.MinGoalDistance,
                _config.MinCoinGoalDistance,
                _config.MinCoursePropSpawnDistance,
                _config.MinCoursePropGoalDistance,
                _config.MinCoursePropCoinDistance);

            _collectedCoins = 0;
            _totalCoins = 0;
            _levelStartTime = Time.time;
            _levelFinishTime = 0f;

            if (!layoutPlan.HasGoal)
            {
                CreateOrUpdateCheckpointMarker();
                return;
            }

            _goalRoot = _contentFactory.CreateGoalFlag(layoutPlan.GoalPosition);
            _goalTransform = _goalRoot != null ? _goalRoot.transform : null;

            for (var i = 0; i < layoutPlan.CoinPositions.Count; i++)
            {
                var coinRoot = _contentFactory.CreateCoin(_coins.Count + 1, layoutPlan.CoinPositions[i]);
                if (coinRoot == null)
                    continue;

                _coins.Add(new CoinInstance
                {
                    Root = coinRoot,
                    Transform = coinRoot.transform,
                    Collected = false
                });
            }

            for (var i = 0; i < layoutPlan.CourseProps.Count; i++)
            {
                var prop = layoutPlan.CourseProps[i];
                var courseProp = _contentFactory.CreateCourseProp(_courseProps.Count + 1, prop.Position, prop.StackHeight);
                if (courseProp != null)
                    _courseProps.Add(courseProp);
            }

            _totalCoins = _coins.Count;
            CreateOrUpdateCheckpointMarker();
        }

        private void CreateOrUpdateCheckpointMarker()
        {
            _checkpointRoot = _contentFactory.CreateOrUpdateCheckpointMarker(
                _checkpointRoot,
                _playerCheckpointPosition,
                _playerCheckpointForward);
        }

        private Vector3 ResolveSpawnPosition(Vector3 markerPosition)
        {
            var rayOrigin = markerPosition + Vector3.up * _config.RespawnRayHeight;
            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, _config.RespawnRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;

            return markerPosition + Vector3.up * 0.2f;
        }

        private Vector3 GetSpawnForward(Transform markerTransform, Camera gameplayCamera)
        {
            var referenceForward = markerTransform.forward;
            if (gameplayCamera != null)
                referenceForward = gameplayCamera.transform.forward;

            referenceForward.y = 0f;
            if (referenceForward.sqrMagnitude < 0.001f)
                referenceForward = Vector3.forward;

            return referenceForward.normalized;
        }
    }
}
