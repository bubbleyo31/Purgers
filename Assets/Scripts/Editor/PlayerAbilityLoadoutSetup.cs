#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;


/// <summary>
/// 將目前仍放在 Profession Runtime Prefab 的鈎索能力搬成獨立
/// Player Ability Runtime，並建立第一份可直接調整的 Loadout 資產。
///
/// 這是明確執行、可重複檢查的遷移工具，不會在 Domain Reload 時
/// 偷偷修改資產。請由 Tools/Player Ability 執行。
/// </summary>
public static class PlayerAbilityLoadoutSetup
{
    private const string AbilityRoot =
        "Assets/_Project_Assets/Data/PlayerAbility";

    private const string DefinitionRoot =
        AbilityRoot + "/Definitions";

    private const string LoadoutRoot =
        AbilityRoot + "/Loadouts";

    private const string RuntimeRoot =
        "Assets/Prefabs/AbilityRuntime";

    private const string AttackProfessionPrefab =
        "Assets/Prefabs/ProfessionRuntime/AttackProfessionRuntime.prefab";

    private const string TankProfessionPrefab =
        "Assets/Prefabs/ProfessionRuntime/TankProfessionRuntime.prefab";

    private const string SupportProfessionPrefab =
        "Assets/Prefabs/ProfessionRuntime/SupportProfessionRuntime.prefab";

    private const string PlayerPrefab =
        "Assets/Prefabs/KCC_Player.prefab";


    [MenuItem(
        "Tools/Player Ability/建立初始能力 Runtime 與 Loadout",
        priority = 100
    )]
    public static void BuildInitialAbilityLoadout()
    {
        EnsureFolderTree();

        PlayerAbilityDefinition supportAerial =
            CreateOrLoadDefinition(
                "SupportAerial",
                "grapple.focus.aerial_slow",
                "空中緩速專注",
                PlayerAbilityCategory.GrappleFocus,
                100,
                "GrappleFocusMovementAuthority",
                false
            );

        PlayerAbilityDefinition tankAirDash =
            CreateOrLoadDefinition(
                "TankAirDash",
                "grapple.focus.air_dash",
                "空中衝刺專注",
                PlayerAbilityCategory.GrappleFocus,
                110,
                "GrappleFocusMovementAuthority",
                false
            );

        PlayerAbilityDefinition attackMark =
            CreateOrLoadDefinition(
                "AttackGrappleMark",
                "grapple.hit.mark",
                "鈎索命中標記",
                PlayerAbilityCategory.GrappleHit,
                200,
                "GrappleHitRopeAuthority",
                false
            );

        PlayerAbilityDefinition tankGather =
            CreateOrLoadDefinition(
                "TankGrappleGather",
                "grapple.hit.gather",
                "鈎索命中聚怪",
                PlayerAbilityCategory.GrappleHit,
                210,
                "GrappleHitRopeAuthority",
                false
            );

        PlayerAbilityDefinition supportPull =
            CreateOrLoadDefinition(
                "SupportGrapplePull",
                "grapple.hit.pull",
                "鈎索命中拉取",
                PlayerAbilityCategory.GrappleHit,
                220,
                "GrappleHitRopeAuthority",
                true
            );

        BuildRuntimePrefab<SupportAerialAbility>(
            "SupportAerialAbilityRuntime",
            SupportProfessionPrefab,
            supportAerial
        );

        BuildRuntimePrefab<TankAirDashAbility>(
            "TankAirDashAbilityRuntime",
            TankProfessionPrefab,
            tankAirDash
        );

        BuildRuntimePrefab<AttackGrappleMarkAbility>(
            "AttackGrappleMarkAbilityRuntime",
            AttackProfessionPrefab,
            attackMark
        );

        BuildRuntimePrefab<TankGrappleGatherAbility>(
            "TankGrappleGatherAbilityRuntime",
            TankProfessionPrefab,
            tankGather
        );

        BuildRuntimePrefab<SupportGrapplePullAbility>(
            "SupportGrapplePullAbilityRuntime",
            SupportProfessionPrefab,
            supportPull
        );

        PlayerAbilitySlotLayoutDefinition layout =
            CreateOrUpdateDefaultLayout();

        PlayerAbilityLoadoutDefinition loadout =
            CreateOrUpdateDefaultLoadout(
                layout,
                supportAerial,
                supportPull
            );

        ConfigurePlayerPrefab(
            loadout
        );

        RemoveLegacyAbilityComponent<SupportAerialAbility>(
            SupportProfessionPrefab
        );

        RemoveLegacyAbilityComponent<SupportGrapplePullAbility>(
            SupportProfessionPrefab
        );

        RemoveLegacyAbilityComponent<TankAirDashAbility>(
            TankProfessionPrefab
        );

        RemoveLegacyAbilityComponent<TankGrappleGatherAbility>(
            TankProfessionPrefab
        );

        RemoveLegacyAbilityComponent<AttackGrappleMarkAbility>(
            AttackProfessionPrefab
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        NetworkProjectConfigUtilities.RebuildPrefabTable();

        Selection.activeObject =
            loadout;

        Debug.Log(
            "[Player Ability Setup] 完成初始遷移。" +
            "\n預設槽位：1 GrappleFocus + 1 GrappleHit" +
            "\n預設能力：Support Aerial + Support Pull" +
            "\n所有五個鈎索能力都已建立獨立 Runtime 與 Definition。"
        );
    }


    private static PlayerAbilityDefinition CreateOrLoadDefinition(
        string assetName,
        string abilityId,
        string displayName,
        PlayerAbilityCategory category,
        int executionPriority,
        string exclusiveGroupId,
        bool includeAdditionalGrappleTargetMask
    )
    {
        string path =
            DefinitionRoot + "/" + assetName + ".asset";

        PlayerAbilityDefinition definition =
            AssetDatabase.LoadAssetAtPath<
                PlayerAbilityDefinition
            >(path);

        if (definition == null)
        {
            definition =
                ScriptableObject.CreateInstance<
                    PlayerAbilityDefinition
                >();

            AssetDatabase.CreateAsset(
                definition,
                path
            );
        }

        SerializedObject serialized =
            new SerializedObject(
                definition
            );

        serialized.FindProperty("abilityId").stringValue =
            abilityId;

        serialized.FindProperty("displayName").stringValue =
            displayName;

        serialized.FindProperty("category").enumValueIndex =
            (int)category;

        serialized.FindProperty("allowDuplicateEquip").boolValue =
            false;

        serialized.FindProperty("executionPriority").intValue =
            executionPriority;

        serialized.FindProperty("includeAdditionalGrappleTargetMask").boolValue =
            includeAdditionalGrappleTargetMask;

        SerializedProperty professionRule =
            serialized.FindProperty("professionRule");

        professionRule
            .FindPropertyRelative("restrictProfession")
            .boolValue =
                false;

        professionRule
            .FindPropertyRelative("allowedProfessions")
            .intValue =
                (int)PlayerProfessionMask.All;

        SerializedProperty groups =
            serialized.FindProperty("exclusiveGroupIds");

        groups.arraySize =
            1;

        groups.GetArrayElementAtIndex(0).stringValue =
            exclusiveGroupId;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(
            definition
        );

        return definition;
    }


    private static void BuildRuntimePrefab<TModule>(
        string prefabName,
        string legacySourcePath,
        PlayerAbilityDefinition definition
    )
        where TModule : MonoBehaviour,
            IPlayerAbilityRuntimeModule
    {
        string outputPath =
            RuntimeRoot + "/" + prefabName + ".prefab";

        GameObject existingPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                outputPath
            );

        GameObject root;
        bool loadedExisting;

        if (existingPrefab != null)
        {
            root =
                PrefabUtility.LoadPrefabContents(
                    outputPath
                );

            loadedExisting =
                true;
        }
        else
        {
            root =
                new GameObject(
                    prefabName
                );

            loadedExisting =
                false;
        }

        try
        {
            NetworkObject networkObject =
                root.GetComponent<NetworkObject>();

            if (networkObject == null)
            {
                networkObject =
                    root.AddComponent<NetworkObject>();
            }

            PlayerAbilityRuntime runtime =
                root.GetComponent<PlayerAbilityRuntime>();

            if (runtime == null)
            {
                runtime =
                    root.AddComponent<PlayerAbilityRuntime>();
            }

            TModule module =
                root.GetComponent<TModule>();

            if (module == null)
            {
                module =
                    root.AddComponent<TModule>();

                TModule source =
                    FindSourceModule<TModule>(
                        legacySourcePath
                    );

                if (source == null)
                {
                    throw new InvalidOperationException(
                        $"找不到 {typeof(TModule).Name} 的舊 Prefab 設定來源。"
                    );
                }

                EditorUtility.CopySerialized(
                    source,
                    module
                );
            }

            SerializedObject runtimeSerialized =
                new SerializedObject(
                    runtime
                );

            runtimeSerialized.FindProperty("definition").objectReferenceValue =
                definition;

            runtimeSerialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject savedPrefab =
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    outputPath
                );

            if (savedPrefab == null)
            {
                throw new InvalidOperationException(
                    $"無法儲存 Ability Runtime Prefab：{outputPath}"
                );
            }

            NetworkObject savedNetworkObject =
                savedPrefab.GetComponent<NetworkObject>();

            SerializedObject definitionSerialized =
                new SerializedObject(
                    definition
                );

            definitionSerialized.FindProperty("runtimePrefab").objectReferenceValue =
                savedNetworkObject;

            definitionSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(
                definition
            );

            AddFusionPrefabLabel(
                savedPrefab
            );
        }
        finally
        {
            if (loadedExisting)
            {
                PrefabUtility.UnloadPrefabContents(
                    root
                );
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(
                    root
                );
            }
        }
    }


    private static TModule FindSourceModule<TModule>(
        string legacySourcePath
    )
        where TModule : MonoBehaviour
    {
        GameObject sourcePrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                legacySourcePath
            );

        return sourcePrefab != null
            ? sourcePrefab.GetComponent<TModule>()
            : null;
    }


    private static PlayerAbilitySlotLayoutDefinition
        CreateOrUpdateDefaultLayout()
    {
        string path =
            LoadoutRoot + "/DefaultGrappleAbilitySlots.asset";

        PlayerAbilitySlotLayoutDefinition layout =
            AssetDatabase.LoadAssetAtPath<
                PlayerAbilitySlotLayoutDefinition
            >(path);

        if (layout == null)
        {
            layout =
                ScriptableObject.CreateInstance<
                    PlayerAbilitySlotLayoutDefinition
                >();

            AssetDatabase.CreateAsset(
                layout,
                path
            );
        }

        SerializedObject serialized =
            new SerializedObject(
                layout
            );

        SerializedProperty capacities =
            serialized.FindProperty("categoryCapacities");

        capacities.arraySize =
            2;

        SetCapacity(
            capacities.GetArrayElementAtIndex(0),
            PlayerAbilityCategory.GrappleFocus,
            1
        );

        SetCapacity(
            capacities.GetArrayElementAtIndex(1),
            PlayerAbilityCategory.GrappleHit,
            1
        );

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(
            layout
        );

        return layout;
    }


    private static void SetCapacity(
        SerializedProperty entry,
        PlayerAbilityCategory category,
        int capacity
    )
    {
        entry.FindPropertyRelative("category").enumValueIndex =
            (int)category;

        entry.FindPropertyRelative("capacity").intValue =
            capacity;
    }


    private static PlayerAbilityLoadoutDefinition
        CreateOrUpdateDefaultLoadout(
            PlayerAbilitySlotLayoutDefinition layout,
            PlayerAbilityDefinition focusAbility,
            PlayerAbilityDefinition hitAbility
        )
    {
        string path =
            LoadoutRoot + "/DefaultPlayerAbilityLoadout.asset";

        PlayerAbilityLoadoutDefinition loadout =
            AssetDatabase.LoadAssetAtPath<
                PlayerAbilityLoadoutDefinition
            >(path);

        if (loadout == null)
        {
            loadout =
                ScriptableObject.CreateInstance<
                    PlayerAbilityLoadoutDefinition
                >();

            AssetDatabase.CreateAsset(
                loadout,
                path
            );
        }

        SerializedObject serialized =
            new SerializedObject(
                loadout
            );

        serialized.FindProperty("slotLayout").objectReferenceValue =
            layout;

        SerializedProperty abilities =
            serialized.FindProperty("equippedAbilities");

        abilities.arraySize =
            2;

        abilities.GetArrayElementAtIndex(0).objectReferenceValue =
            focusAbility;

        abilities.GetArrayElementAtIndex(1).objectReferenceValue =
            hitAbility;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(
            loadout
        );

        return loadout;
    }


    private static void ConfigurePlayerPrefab(
        PlayerAbilityLoadoutDefinition loadout
    )
    {
        GameObject root =
            PrefabUtility.LoadPrefabContents(
                PlayerPrefab
            );

        try
        {
            PlayerAbilityRuntimeManager manager =
                root.GetComponent<PlayerAbilityRuntimeManager>();

            if (manager == null)
            {
                manager =
                    root.AddComponent<PlayerAbilityRuntimeManager>();
            }

            SerializedObject serialized =
                new SerializedObject(
                    manager
                );

            serialized.FindProperty("startingLoadout").objectReferenceValue =
                loadout;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(
                root,
                PlayerPrefab
            );
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(
                root
            );
        }
    }


    private static void RemoveLegacyAbilityComponent<TModule>(
        string prefabPath
    )
        where TModule : MonoBehaviour
    {
        GameObject root =
            PrefabUtility.LoadPrefabContents(
                prefabPath
            );

        try
        {
            TModule module =
                root.GetComponent<TModule>();

            if (module == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(
                module
            );

            PrefabUtility.SaveAsPrefabAsset(
                root,
                prefabPath
            );
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(
                root
            );
        }
    }


    private static void AddFusionPrefabLabel(
        GameObject prefab
    )
    {
        List<string> labels =
            new List<string>(
                AssetDatabase.GetLabels(
                    prefab
                )
            );

        if (labels.Contains("FusionPrefab") ==
            false)
        {
            labels.Add(
                "FusionPrefab"
            );

            AssetDatabase.SetLabels(
                prefab,
                labels.ToArray()
            );
        }
    }


    private static void EnsureFolderTree()
    {
        EnsureFolder(
            "Assets/_Project_Assets/Data",
            "PlayerAbility"
        );

        EnsureFolder(
            AbilityRoot,
            "Definitions"
        );

        EnsureFolder(
            AbilityRoot,
            "Loadouts"
        );

        EnsureFolder(
            "Assets/Prefabs",
            "AbilityRuntime"
        );
    }


    private static void EnsureFolder(
        string parent,
        string child
    )
    {
        string path =
            parent + "/" + child;

        if (AssetDatabase.IsValidFolder(
                path
            ))
        {
            return;
        }

        AssetDatabase.CreateFolder(
            parent,
            child
        );
    }
}
#endif
