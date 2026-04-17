using UnityEngine;

namespace ARPlatformer
{
    [CreateAssetMenu(menuName = "AR Platformer/Runtime Config", fileName = "ARPlatformerRuntimeConfig")]
    public sealed class ARPlatformerRuntimeConfig : ScriptableObject
    {
        [Header("Marker")]
        [SerializeField, Min(0.01f)] private float markerWidthMeters = 0.12f;
        [SerializeField, Min(0f)] private float markerCheckpointRefreshDistance = 0.01f;
        [SerializeField, Range(-1f, 1f)] private float markerCheckpointRefreshForwardDotThreshold = 0.9995f;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float characterMoveSpeed = 1.6f;
        [SerializeField, Min(0f)] private float characterJumpHeight = 0.45f;
        [SerializeField] private float characterGravity = -14f;

        [Header("Spawn")]
        [SerializeField, Min(0.01f)] private float respawnRayHeight = 1.6f;
        [SerializeField, Min(0.01f)] private float respawnRayDistance = 5f;
        [SerializeField, Min(0f)] private float surfaceHoverHeight = 0.14f;

        [Header("Layout")]
        [SerializeField, Min(0.01f)] private float surfaceSampleSpacing = 0.45f;
        [SerializeField, Min(0.01f)] private float minSurfaceSpacing = 0.4f;
        [SerializeField, Min(0.01f)] private float minGoalDistance = 1.2f;
        [SerializeField, Min(1)] private int surfaceGridHalfExtent = 4;
        [SerializeField, Min(0)] private int coinCountTarget = 7;
        [SerializeField, Min(0)] private int coursePropTarget = 4;
        [SerializeField, Min(0f)] private float minCoinGoalDistance = 0.55f;
        [SerializeField, Min(0f)] private float minCoursePropSpawnDistance = 0.9f;
        [SerializeField, Min(0f)] private float minCoursePropGoalDistance = 0.85f;
        [SerializeField, Min(0f)] private float minCoursePropCoinDistance = 0.52f;

        [Header("Interaction")]
        [SerializeField, Min(0f)] private float coinCollectDistance = 0.48f;
        [SerializeField, Min(0f)] private float goalReachDistance = 0.72f;
        [SerializeField, Min(0f)] private float goalReachHeightTolerance = 1.5f;

        [Header("Runtime")]
        [SerializeField, Min(0.01f)] private float scanCollisionSyncInterval = 0.35f;
        [SerializeField, Min(0.01f)] private float vuforiaStartupTimeoutSeconds = 15f;
        [SerializeField, Min(0.01f)] private float uiRefreshInterval = 0.1f;

        public float MarkerWidthMeters => markerWidthMeters;
        public float MarkerCheckpointRefreshDistance => markerCheckpointRefreshDistance;
        public float MarkerCheckpointRefreshDistanceSqr => markerCheckpointRefreshDistance * markerCheckpointRefreshDistance;
        public float MarkerCheckpointRefreshForwardDotThreshold => markerCheckpointRefreshForwardDotThreshold;
        public float CharacterMoveSpeed => characterMoveSpeed;
        public float CharacterJumpHeight => characterJumpHeight;
        public float CharacterGravity => characterGravity;
        public float RespawnRayHeight => respawnRayHeight;
        public float RespawnRayDistance => respawnRayDistance;
        public float SurfaceHoverHeight => surfaceHoverHeight;
        public float SurfaceSampleSpacing => surfaceSampleSpacing;
        public float MinSurfaceSpacing => minSurfaceSpacing;
        public float MinGoalDistance => minGoalDistance;
        public int SurfaceGridHalfExtent => surfaceGridHalfExtent;
        public int CoinCountTarget => coinCountTarget;
        public int CoursePropTarget => coursePropTarget;
        public float MinCoinGoalDistance => minCoinGoalDistance;
        public float MinCoursePropSpawnDistance => minCoursePropSpawnDistance;
        public float MinCoursePropGoalDistance => minCoursePropGoalDistance;
        public float MinCoursePropCoinDistance => minCoursePropCoinDistance;
        public float CoinCollectDistance => coinCollectDistance;
        public float GoalReachDistance => goalReachDistance;
        public float GoalReachHeightTolerance => goalReachHeightTolerance;
        public float ScanCollisionSyncInterval => scanCollisionSyncInterval;
        public float VuforiaStartupTimeoutSeconds => vuforiaStartupTimeoutSeconds;
        public float UiRefreshInterval => uiRefreshInterval;

        public static ARPlatformerRuntimeConfig CreateTransientDefaults()
        {
            var config = CreateInstance<ARPlatformerRuntimeConfig>();
            config.hideFlags = HideFlags.HideAndDontSave;
            return config;
        }
    }
}
