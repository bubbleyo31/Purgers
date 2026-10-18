using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 水母的明確碰撞遷移入口。輸入須來自實際模型／動畫取樣與預覽核對；不從 Renderer.bounds 猜部位。
/// 不更換 FBX、不寫動畫、不動 Scene，不增加 NetworkBehaviour 或傷害 owner。
/// </summary>
public static class EnemyJellyfishCollisionSetup
{
    public const string PrefabPath = "Assets/Prefabs/Enemy/Variants/Enemy_Ranged_A.prefab";
    public const string ModelPath = "VisualRoot/水母動畫1";
    public const string GeneratedPrefix = "JellyfishCollision_";
    // 原 enum 數值保留供歷史校準資料辨識；正式配置改為八大區塊。
    public enum Region { SmallBell, Neck, ShortTentacle, MainBell, Skirt, LongTentacle, NeckAndShortTentacles }
    public enum Shape { Box, Capsule }

    [Serializable]
    public sealed class Part
    {
        public string Id;
        public Region BodyRegion;
        // 相對 Prefab Root 的原始骨架路徑，不建立／改名骨架。
        public string BonePath;
        public Vector3 BoneLocalCenter;
        public Quaternion BoneLocalRotation = Quaternion.identity;
        // 碰撞節點校正為世界單位縮放。Size 與 Radius/Height 均為公尺。
        public Shape Geometry;
        public Vector3 Size;
        public float Radius;
        public float Height;
        public float Padding = .02f;
        public EnemyJellyfishCollisionPose.Sample[] SkinSamples;
    }

    // 沒有 InitializeOnLoad、OnValidate 或自動執行 Menu：必須明確提供已核對的部位資料。
    public static void ApplyReviewedLayout(Part[] parts, EnemyCollisionEnvelope movement,
        float skin, int obstacleMask, Bounds animatedHitboxBounds, string[] skinBonePaths = null, string prefabPath = PrefabPath)
    {
        if (prefabPath != PrefabPath && prefabPath != "Assets/Prefabs/Enemy/Variants/Enemy_Ranged_B.prefab")
            throw new ArgumentException("只接受已核對的水母 A／B Prefab。");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("請在未播放且編譯完成後套用。");
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ConfigureContents(root, parts, movement, skin, obstacleMask, animatedHitboxBounds, skinBonePaths);
            if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                throw new InvalidOperationException("水母碰撞 Prefab 儲存失敗。");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void ConfigureContents(GameObject root, Part[] parts, EnemyCollisionEnvelope movement,
        float skin, int obstacleMask, Bounds animatedHitboxBounds, string[] skinBonePaths = null)
    {
        Validate(root, parts, movement, skin, obstacleMask, animatedHitboxBounds);
        if (skinBonePaths == null && root.GetComponent<EnemyJellyfishCollisionPose>() != null)
            throw new ArgumentException("已有動態部位配置，重新套用時不可遺失蒙皮取樣。");
        if (skinBonePaths != null)
        {
            if (skinBonePaths.Length == 0 || skinBonePaths.Length > 256 ||
                skinBonePaths.Any(path => string.IsNullOrEmpty(path) || !path.StartsWith(ModelPath + "/骨架/", StringComparison.Ordinal) || root.transform.Find(path) == null) ||
                parts.Any(p => p.SkinSamples == null || p.SkinSamples.Length == 0 || !EnemyCollisionEnvelope.Finite(p.Padding) || p.Padding <= 0) ||
                parts.Sum(p => p.SkinSamples.Length) > EnemyJellyfishCollisionPose.MaximumSamples)
                throw new ArgumentException("蒙皮取樣資料無效或超出單隻敵人 1024 點預算。");
            foreach (var p in parts)
            foreach (var s in p.SkinSamples)
                if (new[] {s.Bone0,s.Bone1,s.Bone2,s.Bone3}.Any(i => i < 0 || i >= skinBonePaths.Length) ||
                    !Finite(s.Point0) || !Finite(s.Point1) || !Finite(s.Point2) || !Finite(s.Point3) ||
                    !EnemyCollisionEnvelope.Finite(s.Weights.x+s.Weights.y+s.Weights.z+s.Weights.w) ||
                    s.Weights.x < 0 || s.Weights.y < 0 || s.Weights.z < 0 || s.Weights.w < 0 ||
                    Mathf.Abs(s.Weights.x+s.Weights.y+s.Weights.z+s.Weights.w-1) > .001f)
                    throw new ArgumentException("蒙皮取樣權重或骨架索引無效。");
        }
        // 固定原四權重蒙皮；畫質切換不能令 Host/Client 模型輪廓與碰撞樣本分歧。
        foreach (var renderer in root.transform.Find(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderer.quality = SkinQuality.Bone4;
        var hitRoot = root.GetComponent<HitboxRoot>();
        var death = root.GetComponent<CombatDeathHandler>();
        var oldColliders = root.GetComponentsInChildren<Collider>(true);
        var retainedDeathColliders = ReadDeathColliders(death).Except(oldColliders).ToList();
        int hitLayer = root.transform.Find("Body") != null
            ? root.transform.Find("Body").gameObject.layer : oldColliders[0].gameObject.layer;

        // 保留舊 Body/Head 物件身分與可能存在的錨點引用，只移除已核對的舊命中元件。
        foreach (var hitbox in root.GetComponentsInChildren<Hitbox>(true)) Object.DestroyImmediate(hitbox);
        foreach (var collider in oldColliders) Object.DestroyImmediate(collider);
        foreach (var zone in root.GetComponentsInChildren<WeaponHitZone>(true)) Object.DestroyImmediate(zone);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith(GeneratedPrefix, StringComparison.Ordinal)).ToArray())
            Object.DestroyImmediate(t.gameObject);

        var hitboxes = new List<Hitbox>();
        var poseParts = new List<EnemyJellyfishCollisionPose.Part>();
        foreach (var part in parts)
        {
            var bone = root.transform.Find(part.BonePath);
            var node = new GameObject(GeneratedPrefix + part.Id) { layer = hitLayer };
            node.transform.SetParent(bone, false);
            node.transform.localPosition = part.BoneLocalCenter;
            node.transform.localRotation = part.BoneLocalRotation.normalized;
            node.transform.localScale = Vector3.one / bone.lossyScale.x;
            var hitbox = node.AddComponent<Hitbox>();
            hitbox.Root = hitRoot;
            hitbox.Offset = Vector3.zero;
            Collider collider;
            if (part.Geometry == Shape.Box)
            {
                var box = node.AddComponent<BoxCollider>();
                box.size = part.Size;
                hitbox.Type = HitboxTypes.Box;
                hitbox.BoxExtents = part.Size * .5f;
                collider = box;
            }
            else
            {
                var capsule = node.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = part.Radius;
                capsule.height = part.Height;
                hitbox.Type = HitboxTypes.Capsule;
                hitbox.CapsuleRadius = part.Radius;
                // 本專案 Fusion 版本以完整高度儲存 CapsuleExtents，內部再乘 0.5 減半徑。
                hitbox.CapsuleExtents = part.Height;
                collider = capsule;
            }
            var zone = new SerializedObject(node.AddComponent<WeaponHitZone>());
            zone.FindProperty("zoneType").intValue = (int)(part.BodyRegion == Region.SmallBell
                ? WeaponHitZoneType.Head : WeaponHitZoneType.Body);
            zone.FindProperty("damageMultiplier").floatValue = 1;
            zone.ApplyModifiedPropertiesWithoutUndo();
            hitboxes.Add(hitbox);
            retainedDeathColliders.Add(collider);
            if (skinBonePaths != null) poseParts.Add(new EnemyJellyfishCollisionPose.Part {
                Anchor = bone, Rotation = part.BoneLocalRotation.normalized, Hitbox = hitbox,
                Collider = collider, Padding = part.Padding, Samples = part.SkinSamples
            });
        }
        hitRoot.Hitboxes = hitboxes.ToArray();
        hitRoot.Offset = animatedHitboxBounds.center;
        hitRoot.BroadRadius = animatedHitboxBounds.extents.magnitude + skin;
        var deathSo = new SerializedObject(death);
        var deathArray = deathSo.FindProperty("collidersToDisableOnDeath");
        deathArray.arraySize = retainedDeathColliders.Count;
        for (int i = 0; i < retainedDeathColliders.Count; i++)
            deathArray.GetArrayElementAtIndex(i).objectReferenceValue = retainedDeathColliders[i];
        deathSo.ApplyModifiedPropertiesWithoutUndo();
        SetMovement(root.GetComponent<EnemyFlyingPatrolNavigator>(), movement, skin, obstacleMask);
        SetMovement(root.GetComponent<EnemyFlyingChaseMotor>(), movement, skin, obstacleMask);
        if (skinBonePaths != null)
        {
            var pose = root.GetComponent<EnemyJellyfishCollisionPose>() ?? root.AddComponent<EnemyJellyfishCollisionPose>();
            pose.Configure(skinBonePaths.Select(path => root.transform.Find(path)).ToArray(), poseParts.ToArray());
        }
    }

    private static void Validate(GameObject root, Part[] parts, EnemyCollisionEnvelope movement,
        float skin, int mask, Bounds animatedBounds)
    {
        if (root == null || (root.name != "Enemy_Ranged_A" && root.name != "Enemy_Ranged_B") || root.transform.Find(ModelPath) == null ||
            root.GetComponent<HitboxRoot>() == null || root.GetComponent<CombatDeathHandler>() == null ||
            root.GetComponent<EnemyFlyingPatrolNavigator>() == null || root.GetComponent<EnemyFlyingChaseMotor>() == null)
            throw new InvalidOperationException("只接受具有現行模型與既有 owner 的 Enemy_Ranged_A／B。");
        if ((root.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f)
            throw new InvalidOperationException("Root 必須維持單位縮放，避免公尺設定與 Root-local 包絡混用。");
        if (!movement.IsValid || !EnemyCollisionEnvelope.Finite(skin) || skin <= 0 || mask == 0 ||
            !Finite(animatedBounds.center) || !Finite(animatedBounds.size) ||
            animatedBounds.size.x <= 0 || animatedBounds.size.y <= 0 || animatedBounds.size.z <= 0)
            throw new ArgumentException("缺少有效且已取樣的移動／受擊包絡。");
        if (parts == null || parts.Length != 8 ||
            parts.Any(p => p == null) || parts.Select(p => p.Id).Distinct().Count() != parts.Length)
            throw new ArgumentException("正式水母配置必須恰好八個不重複的部位。");
        foreach (var region in new[] { Region.SmallBell, Region.NeckAndShortTentacles, Region.MainBell, Region.Skirt })
            if (parts.Count(p => p.BodyRegion == region) != 1)
                throw new ArgumentException("八區塊配置須恰好一個 " + region);
        if (parts.Count(p => p.BodyRegion == Region.LongTentacle) != 4)
            throw new ArgumentException("長觸鬚依前後左右分成四組。");
        foreach (var direction in new[] { "back", "forward", "Left", "right" })
            if (!parts.Any(p => p.BodyRegion == Region.LongTentacle && p.Id == "LongTentacles_" + direction))
                throw new ArgumentException("缺少長觸鬚方向組：" + direction);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(GeneratedPrefix, StringComparison.Ordinal) &&
                (t.childCount != 0 || t.GetComponents<Component>().Any(c => c == null ||
                    (!(c is Transform) && !(c is Collider) && !(c is Hitbox) && !(c is WeaponHitZone)))))
                throw new InvalidOperationException("既有產生節點已有人工內容，停止重建：" + t.name);
        foreach (var part in parts)
        {
            if (!Enum.IsDefined(typeof(Region), part.BodyRegion) || string.IsNullOrWhiteSpace(part.Id) || part.Id.Contains("/") ||
                string.IsNullOrEmpty(part.BonePath) || !part.BonePath.StartsWith(ModelPath + "/骨架/", StringComparison.Ordinal) ||
                !Finite(part.BoneLocalCenter) || !Finite(new Vector3(part.BoneLocalRotation.x, part.BoneLocalRotation.y, part.BoneLocalRotation.z)) ||
                !EnemyCollisionEnvelope.Finite(part.BoneLocalRotation.w) ||
                !EnemyCollisionEnvelope.Finite(Quaternion.Dot(part.BoneLocalRotation, part.BoneLocalRotation)) ||
                Quaternion.Dot(part.BoneLocalRotation, part.BoneLocalRotation) < .0001f)
                throw new ArgumentException("部位名稱、骨架路徑或變換無效：" + part.Id);
            var bone = root.transform.Find(part.BonePath);
            if (bone == null || !UniformPositive(bone.lossyScale))
                throw new ArgumentException("找不到骨架或非均勻／負縮放：" + part.BonePath);
            if (part.BodyRegion == Region.SmallBell && !HasAncestor(bone, "head_01", "head_02"))
                throw new ArgumentException("爆頭區必須位於上方小傘頭骨架。");
            if (part.Geometry == Shape.Box)
            {
                if (!Finite(part.Size) || part.Size.x <= 0 || part.Size.y <= 0 || part.Size.z <= 0)
                    throw new ArgumentException("Box 尺寸無效：" + part.Id);
            }
            else if (part.Geometry != Shape.Capsule || !EnemyCollisionEnvelope.Finite(part.Radius) ||
                !EnemyCollisionEnvelope.Finite(part.Height) || part.Radius <= 0 || part.Height < part.Radius * 2)
                throw new ArgumentException("Capsule 尺寸無效：" + part.Id);
        }
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
        {
            if ((mask & (1 << c.gameObject.layer)) != 0)
                throw new ArgumentException("障礙遮罩不可包含敵人自己的碰撞 Layer。");
            if (!Owned(c.transform, root.transform))
                throw new InvalidOperationException("發現方案外 Collider，停止遷移：" + c.name);
        }
        foreach (var c in root.GetComponentsInChildren<Hitbox>(true))
            if (!Owned(c.transform, root.transform)) throw new InvalidOperationException("方案外 Hitbox：" + c.name);
        foreach (var c in root.GetComponentsInChildren<WeaponHitZone>(true))
            if (!Owned(c.transform, root.transform)) throw new InvalidOperationException("方案外命中部位：" + c.name);
        var replaced = new HashSet<Object>(root.GetComponentsInChildren<Collider>(true).Cast<Object>()
            .Concat(root.GetComponentsInChildren<Hitbox>(true)).Concat(root.GetComponentsInChildren<WeaponHitZone>(true)));
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null || replaced.Contains(component) || component is Transform ||
                component is CombatDeathHandler || component is HitboxRoot || component is EnemyJellyfishCollisionPose) continue;
            var property = new SerializedObject(component).GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                    replaced.Contains(property.objectReferenceValue))
                    throw new InvalidOperationException("原命中元件仍被其他系統引用，須先遷移：" + component.GetType().Name + "." + property.propertyPath);
        }
        if (!root.GetComponentsInChildren<Collider>(true).Any())
            throw new InvalidOperationException("缺少原始碰撞 Layer 的來源。");
    }

    private static bool Owned(Transform t, Transform root) =>
        t.name.StartsWith(GeneratedPrefix, StringComparison.Ordinal) ||
        (t.parent == root && new[] { "Head", "Body", "Body (1)", "Body (2)", "Body (3)", "Body (4)" }.Contains(t.name));
    private static bool HasAncestor(Transform t, params string[] names)
    {
        for (; t != null; t = t.parent) if (names.Contains(t.name)) return true;
        return false;
    }
    private static bool UniformPositive(Vector3 scale) => Finite(scale) && scale.x > 0 &&
        Mathf.Abs(scale.x - scale.y) <= scale.x * .0001f && Mathf.Abs(scale.x - scale.z) <= scale.x * .0001f;
    private static bool Finite(Vector3 v) => EnemyCollisionEnvelope.Finite(v.x) &&
        EnemyCollisionEnvelope.Finite(v.y) && EnemyCollisionEnvelope.Finite(v.z);
    private static Collider[] ReadDeathColliders(CombatDeathHandler death)
    {
        var values = new SerializedObject(death).FindProperty("collidersToDisableOnDeath");
        return Enumerable.Range(0, values.arraySize)
            .Select(i => values.GetArrayElementAtIndex(i).objectReferenceValue as Collider).Where(c => c != null).ToArray();
    }
    private static void SetMovement(Object component, EnemyCollisionEnvelope envelope, float skin, int mask)
    {
        var so = new SerializedObject(component);
        so.FindProperty("bodyRadius").floatValue = envelope.Radius;
        so.FindProperty("bodyHeight").floatValue = envelope.Height;
        so.FindProperty("bodyBaseOffset").floatValue = envelope.BaseOffset;
        so.FindProperty("collisionSkin").floatValue = skin;
        so.FindProperty("obstacleMask").intValue = mask;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
