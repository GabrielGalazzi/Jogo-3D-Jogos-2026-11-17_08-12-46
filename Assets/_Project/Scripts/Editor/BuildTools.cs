using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MaidCafe.EditorTools
{
    /// <summary>
    /// One-click builds (menu or batch mode).
    /// Windows: Unity.exe -batchmode -quit -projectPath "..." -executeMethod MaidCafe.EditorTools.BuildTools.BuildWindows
    ///   Output: Builds/Windows/AlwaysAtYourService.exe (git-ignored). Zip that folder for itch.io.
    /// WebGL:   Unity.exe -batchmode -quit -projectPath "..." -executeMethod MaidCafe.EditorTools.BuildTools.BuildWebGL
    ///   Output: Builds/WebGL/ (index.html at the root). Zip the folder's contents and upload it to itch.io as an HTML game
    ///   with "SharedArrayBuffer support" ticked (the ray-traced audio runs on worker threads).
    /// The WebGL build uses the page template Assets/WebGLTemplates/MaidCafe (loads the Vercidium Audio WebAssembly build)
    /// and copies the SDK's web files from Assets/ThirdParty/VercidiumAudio/Web~ (git-ignored, see README) to vaudio/.
    /// It switches the active platform for the duration of the build and switches back afterwards.
    /// </summary>
    public static class BuildTools
    {
        public const string OutputDir = "Builds/Windows";
        public const string ExeName = "AlwaysAtYourService.exe";
        public const string WebGLOutputDir = "Builds/WebGL";
        public const string WebGLTemplate = "PROJECT:MaidCafe";

        /// <summary>The SDK's WebAssembly build (vaudio-wrapper.js + _framework/); "~" keeps Unity from importing it.</summary>
        public const string VAudioWebDir = "Assets/ThirdParty/VercidiumAudio/Web~";

        /// <summary>Quality level used by the browser build (same look as Windows; "Mobile" drops point-light shadows).</summary>
        public const string WebGLQualityLevel = "PC";

        /// <summary>Canvas size of the default WebGL template (set the same viewport on the itch.io page).</summary>
        public const int WebGLWidth = 1280;
        public const int WebGLHeight = 720;

        [MenuItem("MaidCafe/Build Windows (Release)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, Path.Combine(OutputDir, ExeName), development: false);

        [MenuItem("MaidCafe/Build Windows (Development)")]
        public static void BuildWindowsDevelopment() => Build(BuildTarget.StandaloneWindows64, Path.Combine(OutputDir, ExeName), development: true);

        [MenuItem("MaidCafe/Build WebGL (itch.io)")]
        public static void BuildWebGL()
        {
            var previous = EditorUserBuildSettings.activeBuildTarget;
            ConfigureWebGL();
            // The post-processing volumes are created in code (PostFX, PhoneController), so URP finds no volume
            // profile to keep post-processing variants for; for WebGL (gles3) that left UberPost with no variant at all
            // ("not supported or stripped": no colour grading, tonemapping or night vision). Keep them for this build.
            var stripping = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
            bool stripPost = stripping != null && stripping.stripUnusedPostProcessingVariants;
            if (stripping != null) stripping.stripUnusedPostProcessingVariants = false;
            bool ok;
            try
            {
                ok = Build(BuildTarget.WebGL, WebGLOutputDir, development: false);
            }
            finally
            {
                if (stripping != null) stripping.stripUnusedPostProcessingVariants = stripPost;
            }
            if (ok) ok = CopyAudioSdk();
            if (previous != BuildTarget.WebGL)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previous), previous);
            }
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Player settings for an itch.io upload: gzip with decompression fallback (itch.io does not send the
        /// Content-Encoding header the loader would otherwise need), browser data caching, fixed canvas size.
        /// </summary>
        private static void ConfigureWebGL()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.template = WebGLTemplate;
            PlayerSettings.defaultWebScreenWidth = WebGLWidth;
            PlayerSettings.defaultWebScreenHeight = WebGLHeight;
            SetPlatformDefaultQuality("WebGL", WebGLQualityLevel);
        }

        /// <summary>Copies the SDK's web files next to index.html (Builds/WebGL/vaudio/), where the page template loads them.</summary>
        private static bool CopyAudioSdk()
        {
            string target = Path.Combine(WebGLOutputDir, "vaudio");
            if (!File.Exists(Path.Combine(VAudioWebDir, "vaudio-wrapper.js")))
            {
                Debug.LogWarning($"[BuildTools] {VAudioWebDir} is missing: the WebGL build will use line-of-sight audio " +
                                 "(copy 3d/wasm/production from the Vercidium Audio SDK there, see README).");
                return true;
            }

            if (Directory.Exists(target)) Directory.Delete(target, true);
            int files = 0;
            foreach (string file in Directory.GetFiles(VAudioWebDir, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".br") || file.EndsWith(".gz")) continue; // the page fetches the uncompressed files
                string relative = file.Substring(VAudioWebDir.Length + 1);
                string dest = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, true);
                files++;
            }
            Debug.Log($"[BuildTools] Copied {files} Vercidium Audio web files to {target}.");
            return true;
        }

        /// <summary>Sets the default quality level of one platform (Project Settings → Quality, per-platform defaults).</summary>
        private static void SetPlatformDefaultQuality(string platform, string levelName)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (asset == null)
            {
                Debug.LogWarning("[BuildTools] QualitySettings.asset not found; WebGL quality level left unchanged.");
                return;
            }

            var so = new SerializedObject(asset);
            var levels = so.FindProperty("m_QualitySettings");
            int index = -1;
            for (int i = 0; levels != null && i < levels.arraySize; i++)
            {
                if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == levelName) index = i;
            }

            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            if (index < 0 || map == null || !map.isArray)
            {
                Debug.LogWarning($"[BuildTools] Could not set the {platform} quality level to '{levelName}'.");
                return;
            }

            // The level must also be included for the platform: URP strips shader variants for the levels included
            // for the build target, so a default level that excludes it renders with stripped shaders (nothing drawn).
            var excluded = levels.GetArrayElementAtIndex(index).FindPropertyRelative("excludedTargetPlatforms");
            for (int i = excluded != null ? excluded.arraySize - 1 : -1; i >= 0; i--)
            {
                if (excluded.GetArrayElementAtIndex(i).stringValue != platform) continue;
                excluded.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[BuildTools] Quality level '{levelName}' now included for {platform}.");
            }

            for (int i = 0; i < map.arraySize; i++)
            {
                var entry = map.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first")?.stringValue != platform) continue;
                var value = entry.FindPropertyRelative("second");
                if (value.intValue == index) return;
                value.intValue = index;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[BuildTools] {platform} default quality level set to '{levelName}'.");
                return;
            }
            Debug.LogWarning($"[BuildTools] No {platform} entry in the per-platform quality defaults.");
        }

        private static bool Build(BuildTarget target, string location, bool development)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildTools] No scenes in Build Settings.");
                return false;
            }

            string dir = Path.HasExtension(location) ? Path.GetDirectoryName(location) : location;
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location,
                target = target,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildTools] {target} build succeeded: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0}s)");
                return true;
            }

            Debug.LogError($"[BuildTools] {target} build {summary.result}: {summary.totalErrors} error(s).");
            if (Application.isBatchMode && target != BuildTarget.WebGL) EditorApplication.Exit(1);
            return false;
        }
    }
}
