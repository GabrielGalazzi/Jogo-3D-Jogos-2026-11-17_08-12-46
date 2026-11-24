using UnityEditor;

namespace MaidCafe.EditorTools
{
    /// <summary>
    /// Import rules for the Kenney Furniture Kit models (Assets/ThirdParty/KenneyFurnitureKit/Resources/KenneyFurniture):
    /// static props without animation or rig; meshes stay readable because floors are static-batched at runtime
    /// (StaticBatchingUtility needs CPU-side mesh data). Colours come from the FBX materials (the kit has no textures).
    /// </summary>
    public class FurnitureAssetPipeline : AssetPostprocessor
    {
        public const string Folder = "Assets/ThirdParty/KenneyFurnitureKit/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }
    }
}
