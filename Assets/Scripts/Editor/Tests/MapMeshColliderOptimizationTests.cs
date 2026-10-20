using NUnit.Framework;
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

[Category("PurgersRegression")]
public sealed class MapMeshColliderOptimizationTests
{
    private GameObject root;
    private Mesh owned;
    [SetUp] public void SetUp() { root = new GameObject("ColliderTest"); }
    [TearDown] public void TearDown() { if (root) Object.DestroyImmediate(root); if (owned) Object.DestroyImmediate(owned); }
    private MeshCollider Cube(Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        var c = go.AddComponent<MeshCollider>(); c.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh; return c;
    }
    [Test] public void ExactBoxAcceptsDuplicateRenderVerticesWithoutApproximating()
    {
        var c = Cube(Vector3.zero, Vector3.one);
        Assert.That(MapMeshColliderOptimizer.TryGetExactBox(c.sharedMesh, out var bounds), Is.True);
        Assert.That(bounds.center, Is.EqualTo(Vector3.zero)); Assert.That(bounds.size, Is.EqualTo(Vector3.one));
    }
    [Test] public void DoorwayIsNeverReplacedByOneBoundingBox()
    {
        var walls = new[] { Cube(new Vector3(-2, 1.5f, 0), new Vector3(2, 3, .4f)),
            Cube(new Vector3(2, 1.5f, 0), new Vector3(2, 3, .4f)), Cube(new Vector3(0, 3, 0), new Vector3(2, 1, .4f)) };
        owned = new Mesh(); owned.CombineMeshes(walls.Select(c => new CombineInstance { mesh = c.sharedMesh, transform = c.transform.localToWorldMatrix }).ToArray());
        Assert.That(MapMeshColliderOptimizer.TryGetExactBox(owned, out _), Is.False);
        using (var plan = MapMeshColliderOptimizer.Analyze(root))
        {
            Assert.That(plan.Candidates.Count, Is.EqualTo(3));
            MapMeshColliderOptimizer.Apply(plan); Physics.SyncTransforms();
            foreach (var box in root.GetComponentsInChildren<BoxCollider>())
                Assert.That(box.Raycast(new Ray(new Vector3(0, 1, -3), Vector3.forward), out _, 6), Is.False);
            Assert.That(walls.All(c => !c.enabled), Is.True);
            Undo.PerformUndo();
            Assert.That(walls.All(c => c.enabled), Is.True);
            Assert.That(root.GetComponentsInChildren<BoxCollider>(), Is.Empty);
        }
    }
    [Test] public void MissingFaceInwardFaceAndSlantedGeometryAreRejected()
    {
        var c = Cube(Vector3.zero, Vector3.one); owned = Object.Instantiate(c.sharedMesh);
        var tris = owned.triangles; owned.triangles = tris.Take(tris.Length - 3).ToArray();
        Assert.That(MapMeshColliderOptimizer.TryGetExactBox(owned, out _), Is.False);
        owned.triangles = tris.Reverse().ToArray(); Assert.That(MapMeshColliderOptimizer.TryGetExactBox(owned, out _), Is.False);
        owned.triangles = tris; var vertices = owned.vertices; vertices[0] += Vector3.right * .00001f; owned.vertices = vertices;
        Assert.That(MapMeshColliderOptimizer.TryGetExactBox(owned, out _), Is.False);
    }
    [Test] public void CylinderIsNotMistakenForABox()
    {
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylinder.transform.SetParent(root.transform);
        Assert.That(MapMeshColliderOptimizer.TryGetExactBox(cylinder.GetComponent<MeshFilter>().sharedMesh, out _), Is.False);
    }
    [Test] public void MultipleSubmeshesPreserveClosedBoxRecognition()
    {
        var c = Cube(Vector3.zero, Vector3.one); owned = Object.Instantiate(c.sharedMesh);
        var indices = owned.triangles; owned.subMeshCount = 2;
        owned.SetTriangles(indices.Take(18).ToArray(), 0); owned.SetTriangles(indices.Skip(18).ToArray(), 1);
        Assert.That(MapMeshColliderOptimizer.TryGetExactBox(owned, out var bounds), Is.True);
        Assert.That(bounds.size, Is.EqualTo(Vector3.one));
    }
    [Test] public void NegativeScaleAndShearAreRejected()
    {
        Assert.That(MapMeshColliderOptimizer.IsOrthogonalPositive(Matrix4x4.Scale(new Vector3(-1, 1, 1))), Is.False);
        var m = Matrix4x4.identity; m.m01 = .3f; Assert.That(MapMeshColliderOptimizer.IsOrthogonalPositive(m), Is.False);
        Cube(Vector3.zero, new Vector3(-1, 1, 1));
        using (var plan = MapMeshColliderOptimizer.Analyze(root)) Assert.That(plan.Candidates, Is.Empty);
    }
    [Test] public void DisabledAndConvexCollidersStayUntouchedAndStaleTransformFails()
    {
        var a = Cube(Vector3.one, Vector3.one); var disabled = Cube(Vector3.one * 3, Vector3.one); disabled.enabled = false;
        var convex = Cube(Vector3.one * 5, Vector3.one); convex.convex = true;
        using (var plan = MapMeshColliderOptimizer.Analyze(root))
        {
            Assert.That(plan.Candidates.Count, Is.EqualTo(1)); MapMeshColliderOptimizer.ValidateUnchanged(plan);
            a.transform.localPosition += Vector3.right;
            Assert.Throws<InvalidOperationException>(() => MapMeshColliderOptimizer.ValidateUnchanged(plan));
            Assert.That(disabled.enabled, Is.False); Assert.That(convex.enabled, Is.True);
        }
    }
    [Test] public void InPlaceMeshEditInvalidatesPlan()
    {
        var c = Cube(Vector3.zero, Vector3.one); owned = Object.Instantiate(c.sharedMesh); c.sharedMesh = owned;
        using (var plan = MapMeshColliderOptimizer.Analyze(root))
        {
            var v = owned.vertices; v[0] += Vector3.right * .1f; owned.vertices = v;
            Assert.Throws<InvalidOperationException>(() => MapMeshColliderOptimizer.ValidateUnchanged(plan));
        }
    }
    [Test] public void MaterialContactOffsetLayerAndTransformSurviveApplyAndUndo()
    {
        root.transform.SetPositionAndRotation(new Vector3(8, 2, -263), Quaternion.Euler(0, 37, 0));
        var c = Cube(new Vector3(2, 3, 4), new Vector3(2, 4, 6)); c.gameObject.layer = 2;
        c.includeLayers = 1 << 8; c.excludeLayers = 1 << 9; c.layerOverridePriority = 3; c.providesContacts = true;
        var matrix = c.transform.localToWorldMatrix;
        using (var plan = MapMeshColliderOptimizer.Analyze(root))
        {
            MapMeshColliderOptimizer.Apply(plan); var box = c.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null); Assert.That(box.contactOffset, Is.EqualTo(c.contactOffset));
            Assert.That(box.sharedMaterial, Is.EqualTo(c.sharedMaterial)); Assert.That(box.gameObject.layer, Is.EqualTo(2));
            Assert.That(box.transform.localToWorldMatrix, Is.EqualTo(matrix));
            Undo.PerformUndo(); Assert.That(c.enabled, Is.True); Assert.That(c.GetComponent<BoxCollider>(), Is.Null);
            Undo.PerformRedo(); box = c.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null); Assert.That(box.size, Is.EqualTo(Vector3.one));
            Assert.That(box.contactOffset, Is.EqualTo(Physics.defaultContactOffset));
            Assert.That(box.includeLayers.value, Is.EqualTo(1 << 8)); Assert.That(box.excludeLayers.value, Is.EqualTo(1 << 9));
            Assert.That(box.layerOverridePriority, Is.EqualTo(3)); Assert.That(box.providesContacts, Is.True);
            Assert.That(c.enabled, Is.False);
            Undo.PerformUndo();
        }
    }
    [Test] public void NonSerializedContactSettingsArePreservedByExcludingSource()
    {
        var c = Cube(Vector3.zero, Vector3.one); c.contactOffset = Physics.defaultContactOffset * 2;
        using (var plan = MapMeshColliderOptimizer.Analyze(root)) Assert.That(plan.Candidates, Is.Empty);
        Assert.That(c.enabled, Is.True);
    }
    [Test] public void TimingP95UsesObservedSamplesAndEmptySetIsNotZero()
    {
        var t = MapMeshColliderBenchmark.Timing.From(Enumerable.Range(1, 100).Select(x => (double)x));
        Assert.That(t.meanMs, Is.EqualTo(50.5)); Assert.That(t.p95Ms, Is.EqualTo(95));
        Assert.Throws<ArgumentException>(() => MapMeshColliderBenchmark.Timing.From(Array.Empty<double>()));
    }
}
