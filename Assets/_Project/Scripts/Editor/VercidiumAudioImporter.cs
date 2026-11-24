using UnityEditor;

namespace MaidCafe.EditorTools
{
    /// <summary>
    /// Import rules for the Vercidium Audio native SDK (Assets/ThirdParty/VercidiumAudio, git-ignored: see README).
    /// - Plugins/Dev/vaudionative.dll: Editor only (argument validation + the vaudio-debug-window.exe helper beside it).
    /// - Plugins/Production/vaudionative.dll: Windows x86_64 player only (the build ships this one).
    /// - Plugins/Dev/glfw3.dll: never loaded by Unity; the debug window helper loads it from its own folder.
    /// Applied after import (plugin settings changed during preprocessing are not saved), so a fresh clone only needs
    /// the SDK files copied in. Reimports only when a setting differs, so it cannot loop.
    /// </summary>
    public class VercidiumAudioImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/ThirdParty/VercidiumAudio/";
        private const string DevDll = Folder + "Plugins/Dev/vaudionative.dll";
        private const string ProductionDll = Folder + "Plugins/Production/vaudionative.dll";
        private const string Glfw = Folder + "Plugins/Dev/glfw3.dll";

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (!path.StartsWith(Folder)) continue;
                if (path == DevDll) Apply(path, editor: true, player: false);
                else if (path == ProductionDll) Apply(path, editor: false, player: true);
                else if (path == Glfw) Apply(path, editor: false, player: false);
            }
        }

        /// <summary>Re-applies the rules to all three files (menu entry, in case the files were copied in while Unity was closed).</summary>
        [MenuItem("MaidCafe/Audio/Reapply Vercidium Audio Import Settings")]
        public static void ReapplyAll()
        {
            Apply(DevDll, editor: true, player: false);
            Apply(ProductionDll, editor: false, player: true);
            Apply(Glfw, editor: false, player: false);
        }

        private static void Apply(string path, bool editor, bool player)
        {
            if (!(AssetImporter.GetAtPath(path) is PluginImporter plugin)) return;
            if (Matches(plugin, editor, player)) return;

            plugin.SetCompatibleWithAnyPlatform(false);
            plugin.SetExcludeEditorFromAnyPlatform(true);
            plugin.SetCompatibleWithEditor(editor);
            if (editor)
            {
                plugin.SetEditorData("CPU", "x86_64");
                plugin.SetEditorData("OS", "Windows");
            }
            plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, player);
            if (player) plugin.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            plugin.SaveAndReimport();
        }

        private static bool Matches(PluginImporter plugin, bool editor, bool player)
        {
            return !plugin.GetCompatibleWithAnyPlatform()
                   && plugin.GetCompatibleWithEditor() == editor
                   && plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64) == player
                   && !plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows)
                   && !plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneLinux64)
                   && !plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX);
        }
    }
}
