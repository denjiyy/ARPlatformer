using NUnit.Framework;
using UnityEngine;

namespace ARPlatformer.Tests
{
    public sealed class ARPlatformerContentFactoryTests
    {
        private const string TemplateRootName = "AR Platformer Templates";

        [Test]
        public void CreateCoin_UsesCachedTemplate_AndParentsToRuntimeRoot()
        {
            var runtimeRoot = new GameObject("Runtime Root");
            var templateRoot = new GameObject(TemplateRootName);
            templateRoot.transform.SetParent(runtimeRoot.transform, false);

            var coinTemplate = new GameObject("Coin Template");
            coinTemplate.transform.SetParent(templateRoot.transform, false);
            coinTemplate.SetActive(false);
            new GameObject("Coin Body").transform.SetParent(coinTemplate.transform, false);

            var factory = new ARPlatformerContentFactory();
            factory.CacheSceneTemplates(runtimeRoot.transform);

            var coinPosition = new Vector3(1.25f, 0.2f, -0.4f);
            var coin = factory.CreateCoin(3, coinPosition);

            Assert.That(coin, Is.Not.Null);
            Assert.That(coin.name, Is.EqualTo("Coin_3"));
            Assert.That(coin.transform.parent, Is.EqualTo(runtimeRoot.transform));
            Assert.That(coin.activeSelf, Is.True);
            Assert.That(coin.transform.position, Is.EqualTo(coinPosition));
            Assert.That(coin.transform.Find("Coin Body"), Is.Not.Null);

            factory.Dispose();
            Object.DestroyImmediate(runtimeRoot);
        }

        [Test]
        public void CreateCourseProp_UsesTallTemplate_WhenStackHeightIsHigh()
        {
            var runtimeRoot = new GameObject("Runtime Root");
            var templateRoot = new GameObject(TemplateRootName);
            templateRoot.transform.SetParent(runtimeRoot.transform, false);

            var regularTemplate = new GameObject("Course Block Template");
            regularTemplate.transform.SetParent(templateRoot.transform, false);
            regularTemplate.SetActive(false);
            new GameObject("Regular Marker").transform.SetParent(regularTemplate.transform, false);

            var tallTemplate = new GameObject("Tall Course Block Template");
            tallTemplate.transform.SetParent(templateRoot.transform, false);
            tallTemplate.SetActive(false);
            new GameObject("Tall Marker").transform.SetParent(tallTemplate.transform, false);

            var factory = new ARPlatformerContentFactory();
            factory.CacheSceneTemplates(runtimeRoot.transform);

            var courseProp = factory.CreateCourseProp(1, Vector3.zero, stackHeight: 2f);

            Assert.That(courseProp, Is.Not.Null);
            Assert.That(courseProp.transform.Find("Tall Marker"), Is.Not.Null);
            Assert.That(courseProp.transform.Find("Regular Marker"), Is.Null);

            factory.Dispose();
            Object.DestroyImmediate(runtimeRoot);
        }

        [Test]
        public void CreateOrUpdateCheckpointMarker_ReusesExistingObject()
        {
            var factory = new ARPlatformerContentFactory();
            var initial = factory.CreateOrUpdateCheckpointMarker(
                checkpointRoot: null,
                position: Vector3.zero,
                forward: Vector3.forward);

            Assert.That(initial, Is.Not.Null);

            var updated = factory.CreateOrUpdateCheckpointMarker(
                initial,
                new Vector3(2f, 0.5f, 1f),
                Vector3.right);

            Assert.That(updated, Is.SameAs(initial));
            Assert.That(updated.transform.position, Is.EqualTo(new Vector3(2f, 0.5f, 1f)));
            Assert.That(Vector3.Distance(updated.transform.forward, Vector3.right), Is.LessThan(0.0001f));

            factory.Dispose();
            Object.DestroyImmediate(initial);
        }
    }
}
