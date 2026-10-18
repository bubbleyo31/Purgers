using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 讀取原骨架的小量預先選定蒙皮樣本，讓同一部位的 PhysX／Fusion 幾何一致。
/// 不播放動畫、不修改骨架、不處理生命／傷害／移動；Host 仍擁有命中結果。
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(11000)]
public sealed class EnemyJellyfishCollisionPose : MonoBehaviour
{
    public const int MaximumSamples = 1024;
    [Serializable]
    public struct Sample
    {
        public int Bone0, Bone1, Bone2, Bone3;
        public Vector4 Weights;
        public Vector3 Point0, Point1, Point2, Point3;
    }
    [Serializable]
    public sealed class Part
    {
        public Transform Anchor;
        public Quaternion Rotation = Quaternion.identity;
        public Hitbox Hitbox;
        public Collider Collider;
        public float Padding = .02f;
        public Sample[] Samples;
        [NonSerialized] public Vector3[] Points;
    }
    [SerializeField] private Transform[] bones = Array.Empty<Transform>();
    [SerializeField] private Part[] parts = Array.Empty<Part>();
    private Matrix4x4[] matrices;
    private EnemyActor actor;
    private bool ready;
    private bool prepared;

    public void Configure(Transform[] sourceBones, Part[] sourceParts)
    {
        bones = sourceBones; parts = sourceParts; ready = false; prepared = false;
        Prepare();
        RefreshPose();
    }
    private void Awake() { actor = GetComponent<EnemyActor>(); Prepare(); }
    private void LateUpdate()
    {
        if (actor != null && (!actor.IsFusionSpawned || !actor.IsAlive)) return;
        RefreshPose();
    }
    private void Prepare()
    {
        prepared = true;
        if (bones == null || bones.Length == 0 || bones.Length > 256 || parts == null ||
            parts.Length == 0 || parts.Length > HitboxRoot.MAX_HITBOXES) return;
        int count = 0;
        foreach (var bone in bones) if (bone == null) return;
        foreach (var part in parts)
        {
            if (part == null || part.Anchor == null || part.Hitbox == null || part.Collider == null ||
                part.Samples == null || part.Samples.Length == 0 ||
                (!(part.Collider is BoxCollider) && !(part.Collider is CapsuleCollider))) return;
            count += part.Samples.Length;
            foreach (var sample in part.Samples)
                if (sample.Bone0 < 0 || sample.Bone0 >= bones.Length || sample.Bone1 < 0 || sample.Bone1 >= bones.Length ||
                    sample.Bone2 < 0 || sample.Bone2 >= bones.Length || sample.Bone3 < 0 || sample.Bone3 >= bones.Length) return;
            part.Points = new Vector3[part.Samples.Length];
        }
        if (count > MaximumSamples) return;
        matrices = new Matrix4x4[bones.Length]; ready = true;
    }
    /// <summary>Editor 驗證可明確呼叫；執行期每個呈現幀更新，沒有逐幀配置或 BakeMesh。</summary>
    public void RefreshPose()
    {
        if (!prepared) Prepare();
        if (!ready) return;
        for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix;
        foreach (var part in parts)
        {
            float scale = part.Anchor.lossyScale.x;
            if (scale <= .000001f) continue;
            Matrix4x4 frame = part.Anchor.localToWorldMatrix *
                Matrix4x4.TRS(Vector3.zero, part.Rotation, Vector3.one / scale);
            Matrix4x4 inverse = frame.inverse;
            for (int i = 0; i < part.Samples.Length; i++)
            {
                var s = part.Samples[i];
                Vector3 world = matrices[s.Bone0].MultiplyPoint3x4(s.Point0) * s.Weights.x +
                    matrices[s.Bone1].MultiplyPoint3x4(s.Point1) * s.Weights.y +
                    matrices[s.Bone2].MultiplyPoint3x4(s.Point2) * s.Weights.z +
                    matrices[s.Bone3].MultiplyPoint3x4(s.Point3) * s.Weights.w;
                part.Points[i] = inverse.MultiplyPoint3x4(world);
            }
            Fit(part.Points, part.Collider is CapsuleCollider, part.Padding,
                out var center, out var size, out float radius, out float height);
            var target = part.Hitbox.transform;
            target.localPosition = part.Anchor.InverseTransformPoint(frame.MultiplyPoint3x4(center));
            target.localRotation = part.Rotation;
            target.localScale = Vector3.one / scale;
            part.Hitbox.Offset = Vector3.zero;
            if (part.Collider is BoxCollider box)
            {
                box.center = Vector3.zero; box.size = size;
                part.Hitbox.BoxExtents = size * .5f;
            }
            else if (part.Collider is CapsuleCollider capsule)
            {
                capsule.center = Vector3.zero; capsule.radius = radius; capsule.height = height;
                part.Hitbox.CapsuleRadius = radius; part.Hitbox.CapsuleExtents = height;
            }
        }
    }
    public static void Fit(Vector3[] points, bool capsule, float padding,
        out Vector3 center, out Vector3 size, out float radius, out float height)
    {
        var bounds = new Bounds(points[0], Vector3.zero);
        for (int i = 1; i < points.Length; i++) bounds.Encapsulate(points[i]);
        center = bounds.center; size = bounds.size + Vector3.one * (padding * 2);
        radius = 0; height = 0;
        if (!capsule) return;
        float radialSquared = .0004f;
        foreach (var point in points)
        {
            float x = point.x - center.x, z = point.z - center.z;
            radialSquared = Mathf.Max(radialSquared, x * x + z * z);
        }
        float baseRadius = Mathf.Sqrt(radialSquared);
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (var point in points)
        {
            float x = point.x - center.x, z = point.z - center.z;
            float cap = Mathf.Sqrt(Mathf.Max(0, radialSquared - x * x - z * z));
            bottom = Mathf.Min(bottom, point.y + cap); top = Mathf.Max(top, point.y - cap);
        }
        center.y = (bottom + top) * .5f;
        radius = baseRadius + padding;
        height = Mathf.Max(0, top - bottom) + radius * 2;
    }
}
