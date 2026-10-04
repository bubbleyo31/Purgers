#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Editor;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;

/// <summary>只建立缺少的新技能資產；重跑不覆寫人工調整，不修改現有 Scene／玩家 Prefab。</summary>
public static class PlayerActiveAbilityAssetSetup
{
    public const string DataRoot = "Assets/_Project_Assets/Data/PlayerAbility/Active";
    public const string PrefabRoot = "Assets/Prefabs/AbilityRuntime/Active";
    public const string CatalogPath = DataRoot + "/ActiveAbilityRewardCatalog.asset";
    private sealed class Entry
    {
        public string Key, Title, Type, Description;
        public int Projectile = -1;
        public Entry(string key, string title, string type, string description, int projectile = -1)
        { Key = key; Title = title; Type = type; Description = description; Projectile = projectile; }
    }
    private static readonly Entry[] Entries = {
        new Entry("Grenade", "爆破榴彈", "PlayerProjectileAbility", "拋出碰撞即爆的榴彈。爆炸受牆壁阻擋；自身全額受傷，隊友受到 35% 傷害。", 0),
        new Entry("PiercingCannon", "貫穿砲", "PlayerPiercingCannonAbility", "短暫準備後射出直線攻擊，穿透敵人並逐次降低 25% 傷害，無法穿牆。"),
        new Entry("Shockwave", "衝擊扇波", "PlayerShockwaveAbility", "向前打出寬幅衝擊並擊退敵人。撞牆追加一次傷害與 1 秒暈眩。"),
        new Entry("Shield", "快速護盾", "PlayerShieldAbility", "獲得最大生命值 25% 的護盾。1 級持續 1.5 秒，每級增加 0.5 秒。"),
        new Entry("HealingPack", "治療包", "PlayerProjectileAbility", "拋出保留 10 秒的治療包。隊友回復投擲者等級 ×10，自用效果減半；滿血不消耗。", 1),
        new Entry("Ricochet", "彈射彈", "PlayerProjectileAbility", "射出最多反彈三次的子彈，每次反彈傷害翻倍；也會全額傷害自己與隊友。", 2),
        new Entry("Experience", "經驗增加", "PlayerExperienceAbility", "5 秒內自己獲得的經驗與受到的傷害都翻倍。效果結束才開始冷卻。"),
        new Entry("Blink", "閃現", "PlayerBlinkAbility", "先按 E 待命，5 秒內再次按 E 依方向快速衝刺，無方向則向前；遇到障礙停止。"),
        new Entry("BulletTime", "子彈時間", "PlayerBulletTimeAbility", "固定範圍名單持續 2 秒：敵人暫停，玩家移動放慢但攻擊與轉向正常；傷害於結束結算。"),
        new Entry("PrecisionLock", "精準鎖敵", "PlayerPrecisionLockAbility", "2 秒內遠程武器自動鎖定準星附近可見敵人；持近戰武器不能使用。")
    };

    [MenuItem("Tools/Player Ability/建立十項 E 主動技能資產（僅補缺）", priority = 120)]
    public static void CreateMissingAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先離開 Play Mode 再建立資產。");
        EnsureFolder(DataRoot); EnsureFolder(DataRoot + "/Rewards");
        EnsureFolder(DataRoot + "/Loadouts"); EnsureFolder(PrefabRoot);
        var definitions = new List<PlayerAbilityDefinition>();
        var rewards = new List<PlayerRewardDefinition>();
        foreach (var entry in Entries)
        {
            Type type = typeof(Player).Assembly.GetType(entry.Type);
            if (type == null) throw new InvalidOperationException("技能腳本尚未編譯：" + entry.Type);
            string definitionPath = DataRoot + "/" + entry.Key + ".asset";
            var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(definitionPath);
            bool newDefinition = definition == null;
            if (newDefinition)
            {
                definition = ScriptableObject.CreateInstance<PlayerAbilityDefinition>();
                AssetDatabase.CreateAsset(definition, definitionPath);
                var data = new SerializedObject(definition);
                data.FindProperty("abilityId").stringValue = "active." + entry.Key.ToLowerInvariant();
                data.FindProperty("displayName").stringValue = entry.Title;
                data.FindProperty("category").intValue = (int)PlayerAbilityCategory.GrappleFocus;
                data.FindProperty("requiresRangedWeapon").boolValue = entry.Key == "PrecisionLock";
                data.FindProperty("executionPriority").intValue = 120;
                data.FindProperty("professionRule").FindPropertyRelative("restrictProfession").boolValue = false;
                var groups = data.FindProperty("exclusiveGroupIds"); groups.arraySize = 1;
                groups.GetArrayElementAtIndex(0).stringValue = "GrappleFocusMovementAuthority";
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            string prefabPath = PrefabRoot + "/" + entry.Key + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                var root = new GameObject(entry.Key + "Runtime");
                try
                {
                    root.AddComponent<NetworkObject>();
                    var runtime = root.AddComponent<PlayerAbilityRuntime>();
                    var module = root.AddComponent(type);
                    SetReference(runtime, "definition", definition);
                    var config = new SerializedObject(module);
                    config.FindProperty("castDelaySeconds").floatValue =
                        entry.Projectile == 0 || entry.Projectile == 1 || entry.Key == "PiercingCannon" ? .25f : 0f;
                    if (entry.Projectile >= 0)
                    {
                        GameObject projectile = CreateProjectile(entry);
                        config.FindProperty("projectilePrefab").objectReferenceValue =
                            projectile.GetComponent(typeof(Player).Assembly.GetType("PlayerAbilityProjectile"));
                        config.FindProperty("projectileKind").intValue = entry.Projectile;
                        if (entry.Projectile == 2)
                        {
                            config.FindProperty("projectileSpeed").floatValue = 60f;
                            config.FindProperty("gravity").floatValue = 0f;
                        }
                    }
                    config.ApplyModifiedPropertiesWithoutUndo();
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    AddFusionLabel(prefab);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            if (newDefinition || definition.RuntimePrefab == null)
                SetReference(definition, "runtimePrefab", prefab.GetComponent<NetworkObject>());
            definitions.Add(definition);
            rewards.Add(CreateReward(entry, definition));
            CreateLoadout(entry, definition);
        }
        if (AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>(CatalogPath) == null)
        {
            var catalog = ScriptableObject.CreateInstance<PlayerRewardCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            var current = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>("Assets/Resources/Progression/PlayerRewardCatalog.asset");
            var combined = new List<PlayerRewardDefinition>();
            if (current != null) foreach (var item in current.Rewards) if (item != null) combined.Add(item);
            foreach (var item in rewards) if (!combined.Contains(item)) combined.Add(item);
            var serialized = new SerializedObject(catalog);
            var items = serialized.FindProperty("rewards"); items.arraySize = combined.Count;
            for (int i = 0; i < combined.Count; i++) items.GetArrayElementAtIndex(i).objectReferenceValue = combined[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        AssetDatabase.SaveAssets();
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Debug.Log("[E 主動技能] 已補齊十項資產；既有 Scene、玩家 Prefab 與人工調整未改寫。");
    }

    [MenuItem("Tools/Player Ability/將十項 E 技能加入現有獎勵池（保留既有項目）", priority = 121)]
    public static void AppendToCurrentRewardCatalog()
    {
        CreateMissingAssets();
        var catalog = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>("Assets/Resources/Progression/PlayerRewardCatalog.asset");
        if (catalog == null) throw new InvalidOperationException("現有獎勵池不存在。");
        Undo.RecordObject(catalog, "加入 E 技能獎勵");
        var data = new SerializedObject(catalog);
        var items = data.FindProperty("rewards");
        foreach (var entry in Entries)
        {
            var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(DataRoot + "/Rewards/" + entry.Key + ".asset");
            bool found = false;
            foreach (var existing in catalog.Rewards)
                if (existing != null && existing.StableRewardId == reward.StableRewardId) { found = true; break; }
            if (found) continue;
            int index = items.arraySize++; items.GetArrayElementAtIndex(index).objectReferenceValue = reward;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("[E 主動技能] 十項技能已加入現有獎勵池，舊項目與權重均保留。");
    }

    private static GameObject CreateProjectile(Entry entry)
    {
        string path = PrefabRoot + "/" + entry.Key + "Projectile.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var root = new GameObject(entry.Key + "Projectile");
        try
        {
            root.AddComponent<NetworkObject>(); root.AddComponent<NetworkTransform>();
            root.AddComponent(typeof(Player).Assembly.GetType("PlayerAbilityProjectile"));
            var visual = GameObject.CreatePrimitive(entry.Projectile == 1 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            visual.name = "示意外觀"; visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * (entry.Projectile == 2 ? .12f : .25f);
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            string materialPath = DataRoot + "/" + entry.Key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("找不到專案 URP Unlit shader。");
                material = new Material(shader);
                material.SetColor("_BaseColor", entry.Projectile == 1 ? Color.green : entry.Projectile == 2 ? Color.cyan : new Color(1f,.35f,.06f));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            visual.GetComponent<Renderer>().sharedMaterial = material;
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path); AddFusionLabel(saved); return saved;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static PlayerRewardDefinition CreateReward(Entry entry, PlayerAbilityDefinition definition)
    {
        string path = DataRoot + "/Rewards/" + entry.Key + ".asset";
        var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(path);
        if (reward != null) return reward;
        reward = ScriptableObject.CreateInstance<PlayerRewardDefinition>(); AssetDatabase.CreateAsset(reward, path);
        var data = new SerializedObject(reward);
        data.FindProperty("stableRewardId").stringValue = definition.AbilityId;
        data.FindProperty("category").intValue = (int)RewardCategory.GrappleFocus;
        data.FindProperty("repeatPolicy").intValue = (int)RewardRepeatPolicy.Repeatable;
        data.FindProperty("abilityDefinition").objectReferenceValue = definition;
        data.FindProperty("description").stringValue = entry.Description;
        data.ApplyModifiedPropertiesWithoutUndo();
        return reward;
    }

    private static void CreateLoadout(Entry entry, PlayerAbilityDefinition definition)
    {
        string path = DataRoot + "/Loadouts/" + entry.Key + ".asset";
        if (AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>(path) != null) return;
        var loadout = ScriptableObject.CreateInstance<PlayerAbilityLoadoutDefinition>(); AssetDatabase.CreateAsset(loadout, path);
        var template = AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Loadouts/DefaultPlayerAbilityLoadout.asset");
        if (template == null) throw new InvalidOperationException("找不到既有預設 Loadout。");
        var items = new List<PlayerAbilityDefinition>();
        foreach (var old in template.EquippedAbilities)
            if (old != null && old.Category != PlayerAbilityCategory.GrappleFocus) items.Add(old);
        items.Add(definition);
        var data = new SerializedObject(loadout);
        data.FindProperty("slotLayout").objectReferenceValue = template.SlotLayout;
        var abilities = data.FindProperty("equippedAbilities"); abilities.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) abilities.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetReference(UnityEngine.Object owner, string field, UnityEngine.Object value)
    {
        var data = new SerializedObject(owner); data.FindProperty(field).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(owner);
    }
    private static void AddFusionLabel(GameObject prefab)
    {
        var labels = new List<string>(AssetDatabase.GetLabels(prefab));
        if (!labels.Contains("FusionPrefab")) labels.Add("FusionPrefab");
        AssetDatabase.SetLabels(prefab, labels.ToArray());
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); string parent = path.Substring(0, split);
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }
}
#endif
