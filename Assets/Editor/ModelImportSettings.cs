using UnityEditor;

// Import settings for the Blender-made equipment models in Assets/Models.
// Blender material names (M_Shell, M_Accent, ...) are remapped onto the URP materials
// with the same name that live in Assets/Resources/Materials.
public class ModelImportSettings : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/Resources/Models/")) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.bakeAxisConversion = true;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importAnimation = false;
        // the rigged worker / patients keep their skeleton (bones are posed procedurally at runtime)
        mi.animationType = assetPath.Contains("Worker") || assetPath.Contains("Patient_") ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        mi.importBlendShapes = false;
        mi.isReadable = assetPath.Contains("Patient_");   // patients: CPU vertices for the contact tests (PatientActor)
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation = ModelImporterMaterialLocation.External;
        mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        mi.materialSearch = ModelImporterMaterialSearch.Everywhere;
        mi.addCollider = false;
    }
}
