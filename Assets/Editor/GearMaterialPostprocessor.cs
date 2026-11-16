using UnityEditor;
using UnityEngine;

/// <summary>Gives every imported instance of gear.obj the same scene material.</summary>
[InitializeOnLoad]
public sealed class GearMaterialPostprocessor : AssetPostprocessor
{
    private const string ModelPath = "Assets/obj/gear.obj";
    private const string MaterialPath = "Assets/obj/GearDistanceToon.mat";

    static GearMaterialPostprocessor()
    {
        EditorApplication.delayCall += EnsureModelMaterial;
    }

    private static void EnsureModelMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (material == null || model == null)
            return;

        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.sharedMaterial == material)
                continue;

            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            return;
        }
    }

    public static void ValidateGearMaterial()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (material == null || material.shader == null || !material.shader.isSupported || model == null)
            throw new System.InvalidOperationException("Gear toon shader or model failed to import.");

        MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0)
            throw new System.InvalidOperationException("The gear model has no mesh renderer.");
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.sharedMaterial != material)
                throw new System.InvalidOperationException("A gear renderer has the wrong material.");
        }

        Debug.Log($"Gear toon material validated on {renderers.Length} model renderer(s).");
    }

    private void OnPostprocessModel(GameObject model)
    {
        if (assetPath != ModelPath)
            return;

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogError($"Gear material is missing at {MaterialPath}.");
            return;
        }

        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            renderer.sharedMaterial = material;
    }
}
