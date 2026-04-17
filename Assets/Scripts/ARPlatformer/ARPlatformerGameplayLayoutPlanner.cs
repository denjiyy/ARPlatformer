using System.Collections.Generic;
using UnityEngine;

namespace ARPlatformer
{
    internal readonly struct ARPlatformerCoursePropPlacement
    {
        public readonly Vector3 Position;
        public readonly int StackHeight;

        public ARPlatformerCoursePropPlacement(Vector3 position, int stackHeight)
        {
            Position = position;
            StackHeight = stackHeight;
        }
    }

    internal sealed class ARPlatformerGameplayLayoutPlan
    {
        public bool HasGoal { get; }
        public Vector3 GoalPosition { get; }
        public List<Vector3> CoinPositions { get; }
        public List<ARPlatformerCoursePropPlacement> CourseProps { get; }

        public ARPlatformerGameplayLayoutPlan(
            bool hasGoal,
            Vector3 goalPosition,
            List<Vector3> coinPositions,
            List<ARPlatformerCoursePropPlacement> courseProps)
        {
            HasGoal = hasGoal;
            GoalPosition = goalPosition;
            CoinPositions = coinPositions;
            CourseProps = courseProps;
        }
    }

    internal static class ARPlatformerGameplayLayoutPlanner
    {
        public static List<Vector3> SampleSurfacePoints(
            Vector3 spawnPosition,
            Vector3 spawnForward,
            float respawnRayHeight,
            float respawnRayDistance,
            float surfaceHoverHeight,
            float surfaceSampleSpacing,
            float minSurfaceSpacing,
            int surfaceGridHalfExtent)
        {
            var samples = new List<Vector3>();
            var right = Vector3.Cross(Vector3.up, spawnForward);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            else
                right.Normalize();

            for (var z = -surfaceGridHalfExtent; z <= surfaceGridHalfExtent; z++)
            {
                for (var x = -surfaceGridHalfExtent; x <= surfaceGridHalfExtent; x++)
                {
                    var lateralOffset = x * surfaceSampleSpacing;
                    var forwardOffset = z * surfaceSampleSpacing;
                    var probeOrigin = spawnPosition + right * lateralOffset + spawnForward * forwardOffset + Vector3.up * respawnRayHeight;

                    if (!Physics.Raycast(probeOrigin, Vector3.down, out var hit, respawnRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;

                    if (hit.normal.y < 0.72f)
                        continue;

                    var samplePoint = hit.point + Vector3.up * surfaceHoverHeight;
                    if ((samplePoint - spawnPosition).sqrMagnitude < 0.3025f)
                        continue;

                    if (Mathf.Abs(samplePoint.y - spawnPosition.y) > 1.25f)
                        continue;

                    if (IsTooCloseToExistingSample(samplePoint, samples, minSurfaceSpacing))
                        continue;

                    samples.Add(samplePoint);
                }
            }

            samples.Sort((left, rightPoint) =>
                (spawnPosition - left).sqrMagnitude.CompareTo((spawnPosition - rightPoint).sqrMagnitude));

            return samples;
        }

        public static ARPlatformerGameplayLayoutPlan CreatePlan(
            List<Vector3> sampledSurfaces,
            Vector3 spawnPosition,
            int coinCountTarget,
            int coursePropTarget,
            float minGoalDistance,
            float minCoinGoalDistance,
            float minCoursePropSpawnDistance,
            float minCoursePropGoalDistance,
            float minCoursePropCoinDistance)
        {
            if (sampledSurfaces == null || sampledSurfaces.Count == 0)
            {
                return new ARPlatformerGameplayLayoutPlan(
                    false,
                    Vector3.zero,
                    new List<Vector3>(),
                    new List<ARPlatformerCoursePropPlacement>());
            }

            var availableSurfaces = new List<Vector3>(sampledSurfaces);
            var goalIndex = FindGoalSurfaceIndex(availableSurfaces, spawnPosition, minGoalDistance);
            var goalPosition = availableSurfaces[goalIndex];
            availableSurfaces.RemoveAt(goalIndex);

            var coinPositions = new List<Vector3>();
            for (var i = 0; i < availableSurfaces.Count && coinPositions.Count < coinCountTarget; i++)
            {
                var coinPosition = availableSurfaces[i];
                if (Vector3.Distance(coinPosition, goalPosition) < minCoinGoalDistance)
                    continue;

                coinPositions.Add(coinPosition);
            }

            var courseProps = new List<ARPlatformerCoursePropPlacement>();
            var minCoursePropSpawnDistanceSqr = minCoursePropSpawnDistance * minCoursePropSpawnDistance;
            var minCoursePropGoalDistanceSqr = minCoursePropGoalDistance * minCoursePropGoalDistance;
            var minCoursePropCoinDistanceSqr = minCoursePropCoinDistance * minCoursePropCoinDistance;

            for (var i = availableSurfaces.Count - 1; i >= 0 && courseProps.Count < coursePropTarget; i--)
            {
                var surfacePosition = availableSurfaces[i];
                if ((surfacePosition - spawnPosition).sqrMagnitude < minCoursePropSpawnDistanceSqr)
                    continue;

                if ((surfacePosition - goalPosition).sqrMagnitude < minCoursePropGoalDistanceSqr)
                    continue;

                if (IsNearAny(surfacePosition, coinPositions, minCoursePropCoinDistanceSqr))
                    continue;

                courseProps.Add(new ARPlatformerCoursePropPlacement(surfacePosition, courseProps.Count % 2 == 0 ? 1 : 2));
            }

            return new ARPlatformerGameplayLayoutPlan(true, goalPosition, coinPositions, courseProps);
        }

        private static bool IsTooCloseToExistingSample(Vector3 samplePoint, List<Vector3> samples, float minSurfaceSpacing)
        {
            var minSurfaceSpacingSqr = minSurfaceSpacing * minSurfaceSpacing;
            for (var i = 0; i < samples.Count; i++)
            {
                if ((samplePoint - samples[i]).sqrMagnitude < minSurfaceSpacingSqr)
                    return true;
            }

            return false;
        }

        private static int FindGoalSurfaceIndex(List<Vector3> surfaces, Vector3 spawnPosition, float minGoalDistance)
        {
            var goalIndex = surfaces.Count - 1;
            var farthestDistance = -1f;
            var minGoalDistanceSqr = minGoalDistance * minGoalDistance;

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

        private static bool IsNearAny(Vector3 surfacePosition, List<Vector3> positions, float maxDistanceSqr)
        {
            for (var i = 0; i < positions.Count; i++)
            {
                if ((surfacePosition - positions[i]).sqrMagnitude < maxDistanceSqr)
                    return true;
            }

            return false;
        }
    }
}
