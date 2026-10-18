#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>只遷移本次三個 Prefab 的光束呈現；不開視窗、不改 Scene、不重置戰鬥數值。</summary>
public static class BeamPresentationSetup
{
    public const string MaterialPath = "Assets/Materials/BeamEnergy.mat";
    public const string ParticleMaterialPath = "Assets/Materials/BeamSparks.mat";
    private static Material Material(string path, float radial)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var shader = Shader.Find("Purgers/BeamEnergy");
        if (shader == null) throw new InvalidOperationException("BeamEnergy shader not imported");
        material = new Material(shader);
        material.SetFloat("_Radial", radial);
        material.SetFloat("_Streak", radial > 0 ? 0 : .12f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
    public static string Apply()
    {
        var line = Material(MaterialPath, 0);
        var sparks = Material(ParticleMaterialPath, 1);
        foreach (string path in new[] {
            "Assets/Prefabs/Enemy/Variants/Enemy_Ranged_B.prefab",
            "Assets/Prefabs/Enemy/Variants/Enemy_Ranged_C_Beam.prefab",
            "Assets/Prefabs/AbilityRuntime/Active/PiercingCannon.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var presenter = root.GetComponentInChildren<EnemyBeamPresenter>(true);
                UnityEngine.Object component = presenter != null ? (UnityEngine.Object)presenter : root.GetComponent<PlayerPiercingCannonAbility>();
                if (component == null) throw new InvalidOperationException(path + ": missing beam owner");
                var serialized = new SerializedObject(component);
                var settings = serialized.FindProperty("presentation");
                settings.FindPropertyRelative("lineMaterial").objectReferenceValue = line;
                settings.FindPropertyRelative("particles.material").objectReferenceValue = sparks;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (presenter != null)
                {
                    var attack = new SerializedObject(root.GetComponent<EnemyRangedBeamAttack>());
                    if (!attack.FindProperty("useWindupStageRatio").boolValue)
                    {
                        float tracking = attack.FindProperty("trackingSeconds").floatValue;
                        float locked = attack.FindProperty("lockedDelaySeconds").floatValue;
                        attack.FindProperty("trackingStageRatio").floatValue = tracking / Mathf.Max(.001f, tracking + locked);
                        attack.FindProperty("useWindupStageRatio").boolValue = true;
                        attack.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return "Three beam prefabs configured; existing timing and damage retained; audio slots unassigned.";
    }
}
#endif

