using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ARPlatformer.Tests
{
    public sealed class ARPlatformerGameplayLayoutPlannerTests
    {
        [Test]
        public void CreatePlan_ReturnsEmptyLayout_WhenNoSurfacesAreAvailable()
        {
            var plan = ARPlatformerGameplayLayoutPlanner.CreatePlan(
                new List<Vector3>(),
                Vector3.zero,
                coinCountTarget: 7,
                coursePropTarget: 4,
                minGoalDistance: 1.2f,
                minCoinGoalDistance: 0.55f,
                minCoursePropSpawnDistance: 0.9f,
                minCoursePropGoalDistance: 0.85f,
                minCoursePropCoinDistance: 0.52f);

            Assert.That(plan.HasGoal, Is.False);
            Assert.That(plan.GoalPosition, Is.EqualTo(Vector3.zero));
            Assert.That(plan.CoinPositions, Is.Empty);
            Assert.That(plan.CourseProps, Is.Empty);
        }

        [Test]
        public void CreatePlan_UsesFarthestValidSurfaceForGoal_AndSkipsCoinsTooCloseToGoal()
        {
            var sampledSurfaces = new List<Vector3>
            {
                new(0.5f, 0f, 0f),
                new(1.2f, 0f, 0f),
                new(2.5f, 0f, 0f),
                new(4.8f, 0f, 0f),
                new(6.5f, 0f, 0f)
            };

            var plan = ARPlatformerGameplayLayoutPlanner.CreatePlan(
                sampledSurfaces,
                Vector3.zero,
                coinCountTarget: 3,
                coursePropTarget: 0,
                minGoalDistance: 2f,
                minCoinGoalDistance: 2f,
                minCoursePropSpawnDistance: 0.9f,
                minCoursePropGoalDistance: 0.85f,
                minCoursePropCoinDistance: 0.52f);

            Assert.That(plan.HasGoal, Is.True);
            Assert.That(plan.GoalPosition, Is.EqualTo(new Vector3(6.5f, 0f, 0f)));
            Assert.That(plan.CoinPositions, Has.Count.EqualTo(3));
            Assert.That(plan.CoinPositions, Is.EqualTo(new[]
            {
                new Vector3(0.5f, 0f, 0f),
                new Vector3(1.2f, 0f, 0f),
                new Vector3(2.5f, 0f, 0f)
            }));
        }

        [Test]
        public void CreatePlan_KeepsCoursePropsAwayFromSpawnGoalAndCoins()
        {
            var sampledSurfaces = new List<Vector3>
            {
                new(1.0f, 0f, 0f),
                new(2.2f, 0f, 0f),
                new(3.1f, 0f, 0f),
                new(4.2f, 0f, 0f),
                new(5.8f, 0f, 0f),
                new(7.0f, 0f, 0f)
            };

            var plan = ARPlatformerGameplayLayoutPlanner.CreatePlan(
                sampledSurfaces,
                Vector3.zero,
                coinCountTarget: 2,
                coursePropTarget: 3,
                minGoalDistance: 2f,
                minCoinGoalDistance: 1.3f,
                minCoursePropSpawnDistance: 2.5f,
                minCoursePropGoalDistance: 1.5f,
                minCoursePropCoinDistance: 1.0f);

            Assert.That(plan.GoalPosition, Is.EqualTo(new Vector3(7.0f, 0f, 0f)));
            Assert.That(plan.CoinPositions, Is.EqualTo(new[]
            {
                new Vector3(1.0f, 0f, 0f),
                new Vector3(2.2f, 0f, 0f)
            }));
            Assert.That(plan.CourseProps, Has.Count.EqualTo(1));
            Assert.That(plan.CourseProps[0].Position, Is.EqualTo(new Vector3(4.2f, 0f, 0f)));
            Assert.That(plan.CourseProps[0].StackHeight, Is.EqualTo(1));
        }
    }
}
