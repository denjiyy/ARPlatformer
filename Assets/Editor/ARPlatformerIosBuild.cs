using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace ARPlatformer.Editor
{
    public static class ARPlatformerIosBuild
    {
        private const string DefaultBuildPath = "Builds/iOS";
        private const string BuildPathEnvVar = "ARPLATFORMER_IOS_BUILD_PATH";
        private const string BundleIdentifierEnvVar = "ARPLATFORMER_IOS_BUNDLE_ID";

        [MenuItem("Tools/AR Platformer/Build iOS Xcode Project")]
        public static void BuildIosFromMenu()
        {
            BuildIos(throwOnFailure: false);
        }

        public static void BuildIosBatchMode()
        {
            BuildIos(throwOnFailure: true);
        }

        private static void BuildIos(bool throwOnFailure)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                Fail("No enabled scenes were found in Build Settings.", throwOnFailure);

            var buildPath = ResolveBuildPath();
            Directory.CreateDirectory(buildPath);

            var bundleIdentifier = Environment.GetEnvironmentVariable(BundleIdentifierEnvVar);
            if (!string.IsNullOrWhiteSpace(bundleIdentifier))
            {
#pragma warning disable CS0618
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, bundleIdentifier.Trim());
#pragma warning restore CS0618
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                Fail("Unity could not switch the active build target to iOS.", throwOnFailure);

            ApplyVuforiaIosDefaults();

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = buildPath,
                targetGroup = BuildTargetGroup.iOS,
                target = BuildTarget.iOS,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Fail($"iOS build failed with result {report.summary.result}.", throwOnFailure);
                return;
            }

            UnityEngine.Debug.Log($"AR Platformer iOS Xcode project exported to: {buildPath}");
        }

        private static void ApplyVuforiaIosDefaults()
        {
            var setterType = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "Vuforia.Unity.Editor")
                ?.GetType("Vuforia.EditorClasses.PlayerSettingsDefaultSetter");

            var setterMethod = setterType?.GetMethod(
                "SetDefaultSettings",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(BuildTarget) },
                modifiers: null);

            setterMethod?.Invoke(null, new object[] { BuildTarget.iOS });
        }

        private static string ResolveBuildPath()
        {
            var configuredPath = Environment.GetEnvironmentVariable(BuildPathEnvVar);
            if (string.IsNullOrWhiteSpace(configuredPath))
                configuredPath = DefaultBuildPath;

            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
        }

        private static void Fail(string message, bool throwOnFailure)
        {
            if (throwOnFailure)
                throw new InvalidOperationException(message);

            UnityEngine.Debug.LogError(message);
        }
    }
}
