using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only exact cuboid recognition. Never wraps a building in a convex hull or merges colliders.</summary>
public static class MapMeshColliderOptimizer
{
    public const string TargetName = "Map(Z:-263)";
    public const string ExclusionsPath = "deliverables/MeshColliderAudit_20261020/mesh-exclusions.json";
    public const string VerifiedPath = "deliverables/MeshColliderAudit_20261020/verified-plan.json";
    [Serializable] public sealed class MeshExclusions { public string[] meshKeys = Array.Empty<string>(); }
    [Serializable] public sealed class VerifiedPlan { public string fingerprint, utc; public int probes, boxes; }

    public static string Fingerprint(Plan plan)
    {
        var text = new StringBuilder();
        var replacements = new HashSet<MeshCollider>(plan.Candidates.Select(c => c.Source));
        foreach (var c in plan.Baseline)
        {
            for (var t = c.transform; t; t = t.parent) text.Append(t.name).Append('#').Append(t.GetSiblingIndex()).Append('/');
            text.Append(MeshKey(c.sharedMesh)).Append('|').Append(SettingsKey(c)).Append('|')
                .Append(replacements.Contains(c)).Append('|').Append(c.sharedMesh.vertexCount).Append('|');
            var matrix = c.transform.localToWorldMatrix;
            for (int i = 0; i < 16; i++) text.Append(matrix[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            text.AppendLine();
        }
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
    }

    public static bool HasMatchingValidation(Plan plan)
    {
        if (plan == null || plan.Applied || !File.Exists(VerifiedPath)) return false;
        var proof = JsonUtility.FromJson<VerifiedPlan>(File.ReadAllText(VerifiedPath));
        return proof != null && proof.probes > 0 && proof.boxes == plan.Candidates.Count && proof.fingerprint == Fingerprint(plan);
    }

    public static string MeshKey(Mesh mesh)
    {
        return mesh && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId)
            ? guid + ":" + localId : "";
    }

    public static HashSet<string> LoadMeshExclusions()
    {
        if (!File.Exists(ExclusionsPath)) return new HashSet<string>();
        var data = JsonUtility.FromJson<MeshExclusions>(File.ReadAllText(ExclusionsPath));
        if (data == null || data.meshKeys == null) throw new InvalidOperationException("量測排除檔無效");
        return new HashSet<string>(data.meshKeys);
    }

    public sealed class Candidate
    {
        public MeshCollider Source;
        public Mesh Mesh;
        public Matrix4x4 World;
        public Bounds Bounds;
        public string Settings;
    }

    public sealed class Plan : IDisposable
    {
        public GameObject Root;
        public MeshCollider[] Baseline;
        public readonly List<Candidate> Candidates = new List<Candidate>();
        public readonly Dictionary<string, int> Excluded = new Dictionary<string, int>();
        public int CandidateMeshCount => Baseline.Length - Candidates.Count;
        public bool Applied;
        public void Dispose() { Candidates.Clear(); }
    }

    public static GameObject FindTarget()
    {
        var matches = UnityEngine.Object.FindObjectsOfType<Transform>(true)
            .Where(t => t.parent == null && t.name == TargetName && t.gameObject.scene.name == "Game")
            .Select(t => t.gameObject).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("需要唯一的 Game / " + TargetName);
        return matches[0];
    }

    public static bool IsOrthogonalPositive(Matrix4x4 m)
    {
        Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
        if (!Finite(x) || !Finite(y) || !Finite(z) || m.determinant <= 0 ||
            x.sqrMagnitude < 1e-12f || y.sqrMagnitude < 1e-12f || z.sqrMagnitude < 1e-12f) return false;
        return Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) < 1e-5f &&
               Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) < 1e-5f &&
               Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) < 1e-5f;
    }

    private static bool Finite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
          float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    public static string SettingsKey(MeshCollider c) =>
        c.gameObject.layer + "|" + c.tag + "|" + (c.sharedMaterial ? c.sharedMaterial.GetInstanceID() : 0) +
        "|" + c.contactOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
        "|" + (int)c.cookingOptions + "|" + c.includeLayers.value + "|" + c.excludeLayers.value +
        "|" + c.layerOverridePriority + "|" + c.providesContacts + "|" + c.hasModifiableContacts;

    public static void CopySettings(Collider source, Collider target)
    {
        target.isTrigger = source.isTrigger;
        target.sharedMaterial = source.sharedMaterial; target.contactOffset = source.contactOffset;
        target.includeLayers = source.includeLayers; target.excludeLayers = source.excludeLayers;
        target.layerOverridePriority = source.layerOverridePriority; target.providesContacts = source.providesContacts;
        target.hasModifiableContacts = source.hasModifiableContacts;
    }

    private static HashSet<int> ReferencedObjects()
    {
        var result = new HashSet<int>();
        foreach (var script in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (!script || !script.gameObject.scene.IsValid()) continue;
            using (var so = new SerializedObject(script))
            {
                var it = so.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue)
                    {
                        var o = it.objectReferenceValue;
                        if (o is Collider || o is GameObject || o is Transform) result.Add(o.GetInstanceID());
                    }
            }
        }
        return result;
    }

    private static string Exclusion(MeshCollider c, HashSet<int> referenced)
    {
        if (c.convex || c.isTrigger || c.attachedRigidbody || c.attachedArticulationBody) return "凸形／Trigger／Rigidbody";
        // contactOffset and modifiable contacts are not serialized by Unity's Undo/scene format.
        if (c.contactOffset != Physics.defaultContactOffset || c.hasModifiableContacts) return "非預設接觸參數";
        if (!c.sharedMesh) return "缺少 Mesh";
        if (c.GetComponents<MonoBehaviour>().Length > 0) return "碰撞器物件上有腳本";
        if (c.GetComponents<Collider>().Length != 1) return "同物件有多個 Collider";
        if (c.GetComponentInParent<Animator>() || c.GetComponentInParent<Animation>()) return "動畫階層";
        if (c.GetComponentsInParent<MonoBehaviour>(true).Any(s =>
                !s || (s.GetType().Name != "MapChunk" && s.GetType().Name != "NavMeshSurface")))
            return "未核准的祖先腳本";
        if (referenced.Contains(c.GetInstanceID()) || referenced.Contains(c.gameObject.GetInstanceID()) ||
            referenced.Contains(c.transform.GetInstanceID())) return "腳本直接引用";
        if (!IsOrthogonalPositive(c.transform.localToWorldMatrix)) return "負縮放／剪切／退化變換";
        return null;
    }

    // Exact vertex coordinates only: no tolerance which could turn an opening or bevel into a box.
    // Each of six faces must consist of two outward triangles sharing that face's diagonal.
    public static bool TryGetExactBox(Mesh mesh, out Bounds bounds)
    {
        bounds = default;
        if (!mesh) return false;
        using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
        using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
        {
            if (vertices.Length < 8) return false;
            data[0].GetVertices(vertices);
            Vector3 min = vertices[0], max = vertices[0];
            foreach (var v in vertices)
            {
                if (!Finite(v)) return false;
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
            }
            var size = max - min;
            if (size.x <= 1e-6f || size.y <= 1e-6f || size.z <= 1e-6f) return false;
            var corners = new int[vertices.Length];
            var present = new HashSet<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i]; int mask = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (v[axis] == max[axis]) mask |= 1 << axis;
                    else if (v[axis] != min[axis]) return false;
                }
                corners[i] = mask; present.Add(mask);
            }
            if (present.Count != 8) return false;
            var faces = Enumerable.Range(0, 6).Select(_ => new List<int[]>()).ToArray();
            int triangleCount = 0;
            for (int s = 0; s < data[0].subMeshCount; s++)
            {
                var descriptor = data[0].GetSubMesh(s);
                if (descriptor.topology != MeshTopology.Triangles || descriptor.indexCount % 3 != 0) return false;
                using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                {
                    data[0].GetIndices(indices, s, true);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        if (++triangleCount > 12) return false;
                        int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                        if (ia < 0 || ib < 0 || ic < 0 || ia >= corners.Length || ib >= corners.Length || ic >= corners.Length) return false;
                        int a = corners[ia], b = corners[ib], c = corners[ic];
                        if (a == b || b == c || a == c) return false;
                        int face = -1;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            int bit = 1 << axis;
                            if ((a & bit) != (b & bit) || (a & bit) != (c & bit)) continue;
                            face = axis * 2 + ((a & bit) == 0 ? 0 : 1);
                            var normal = Vector3.Cross(vertices[ib] - vertices[ia], vertices[ic] - vertices[ia]);
                            if (normal[axis] * ((a & bit) == 0 ? -1 : 1) <= 0) return false;
                            break;
                        }
                        if (face < 0) return false;
                        faces[face].Add(new[] { a, b, c });
                    }
                }
            }
            if (triangleCount != 12) return false;
            foreach (var face in faces)
            {
                if (face.Count != 2 || face.SelectMany(t => t).Distinct().Count() != 4) return false;
                var edge = face[0].Intersect(face[1]).ToArray();
                if (edge.Length != 2) return false;
                int delta = edge[0] ^ edge[1];
                if (delta != 3 && delta != 5 && delta != 6) return false;
            }
            bounds = new Bounds((min + max) * .5f, size);
            return true;
        }
    }

    public static Plan Analyze(GameObject root)
    {
        if (!root || !root.scene.IsValid()) throw new ArgumentException("需要場景物件");
        var plan = new Plan { Root = root, Baseline = root.GetComponentsInChildren<MeshCollider>(true)
            .Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray() };
        var refs = ReferencedObjects();
        var measuredExclusions = LoadMeshExclusions();
        foreach (var c in plan.Baseline)
        {
            string reason = Exclusion(c, refs);
            if (reason == null && measuredExclusions.Contains(MeshKey(c.sharedMesh))) reason = "實測掃掠差異，保留原 MeshCollider";
            Bounds bounds = default;
            if (reason == null && !TryGetExactBox(c.sharedMesh, out bounds)) reason = "不是可證明的封閉長方體";
            if (reason != null)
            {
                plan.Excluded.TryGetValue(reason, out int previous); plan.Excluded[reason] = previous + 1;
                continue;
            }
            plan.Candidates.Add(new Candidate { Source = c, Mesh = c.sharedMesh, World = c.transform.localToWorldMatrix,
                Bounds = bounds, Settings = SettingsKey(c) });
        }
        return plan;
    }

    public static void ValidateUnchanged(Plan plan)
    {
        if (plan == null || !plan.Root || plan.Applied) throw new InvalidOperationException("請重新分析");
        var current = plan.Root.GetComponentsInChildren<MeshCollider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy);
        if (!new HashSet<MeshCollider>(current).SetEquals(plan.Baseline)) throw new InvalidOperationException("碰撞器清單已改變，請重新分析");
        var refs = ReferencedObjects();
        foreach (var c in plan.Candidates)
            if (!c.Source || c.Source.sharedMesh != c.Mesh || c.Source.transform.localToWorldMatrix != c.World ||
                SettingsKey(c.Source) != c.Settings || Exclusion(c.Source, refs) != null ||
                !TryGetExactBox(c.Mesh, out var bounds) || bounds != c.Bounds)
                throw new InvalidOperationException("來源幾何／設定／引用已改變，請重新分析");
    }

    public static void Apply(Plan plan)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("只能在編輯模式套用");
        ValidateUnchanged(plan);
        if (plan.Root.name == TargetName && !HasMatchingValidation(plan))
            throw new InvalidOperationException("此 Map 候選尚未通過相同來源的背景 A/B 檢查，請先量測");
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("精確長方體 MeshCollider 優化");
        try
        {
            foreach (var c in plan.Candidates)
            {
                var box = Undo.AddComponent<BoxCollider>(c.Source.gameObject);
                Undo.RecordObject(box, "設定精確長方體");
                box.center = c.Bounds.center; box.size = c.Bounds.size;
                CopySettings(c.Source, box);
                Undo.RecordObject(c.Source, "保留並停用原 MeshCollider"); c.Source.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(c.Source);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undo); plan.Applied = true;
            // Deliberately do not save the scene or unrelated dirty assets.
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }
}
