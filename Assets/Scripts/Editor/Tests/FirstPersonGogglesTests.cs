using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[Category("PurgersRegression")]
public sealed class FirstPersonGogglesTests
{
    private GameObject root;
    private Component rim;
    [SetUp] public void SetUp()
    {
        var type = Type.GetType("GoggleRimGraphic, Assembly-CSharp");
        Assert.That(type, Is.Not.Null, "缺少已核准的護目鏡邊緣呈現元件。");
        root = new GameObject("isolated goggle rim", typeof(RectTransform), typeof(CanvasRenderer));
        rim = root.AddComponent(type);
        ((RectTransform)root.transform).sizeDelta = new Vector2(1920, 1080);
    }
    [TearDown] public void TearDown() { if (root != null) Object.DestroyImmediate(root); }
    private void Set(string name, float value) => rim.GetType().GetProperty(name).SetValue(rim, value);
    private Mesh Build()
    {
        var mesh = new Mesh();
        using (var helper = new VertexHelper())
        {
            rim.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Invoke(rim, new object[] { helper });
            helper.FillMesh(mesh);
        }
        return mesh;
    }
    [TestCase(1920, 1080)] [TestCase(2560, 1080)] [TestCase(1280, 720)] [TestCase(1440, 1080)]
    public void MaximumThicknessLeavesCentralViewEmptyAndDoesNotReceiveClicks(int width, int height)
    {
        ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
        Set("Thickness", 2f);
        var mesh = Build();
        try
        {
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                bool peripheral = Mathf.Min(a.y, b.y, c.y) > height * .32f ||
                    Mathf.Max(a.y, b.y, c.y) < -height * .32f ||
                    Mathf.Min(a.x, b.x, c.x) > width * .43f ||
                    Mathf.Max(a.x, b.x, c.x) < -width * .43f;
                Assert.That(peripheral, Is.True, "邊框三角形不可穿越中央視野。");
            }
            Assert.That(((Graphic)rim).raycastTarget, Is.False);
            Assert.That(((Graphic)rim).Raycast(Vector2.zero, null), Is.False);
        }
        finally { Object.DestroyImmediate(mesh); }
    }
    [Test] public void ZeroThicknessOrOpacityRemovesAllGeometry()
    {
        Set("Thickness", 0); var mesh = Build();
        try { Assert.That(mesh.vertexCount, Is.Zero); } finally { Object.DestroyImmediate(mesh); }
        Set("Thickness", 1); Set("Opacity", 0); mesh = Build();
        try { Assert.That(mesh.vertexCount, Is.Zero); } finally { Object.DestroyImmediate(mesh); }
    }
    [Test] public void OpacityChangesAlphaWithoutMovingGeometry()
    {
        Set("Opacity", 1); var full = Build(); Set("Opacity", .5f); var half = Build();
        try
        {
            Assert.That(half.vertices, Is.EqualTo(full.vertices));
            var a = full.colors32; var b = half.colors32;
            for (int i = 0; i < a.Length; i++) Assert.That((int)b[i].a, Is.EqualTo(a[i].a / 2f).Within(1));
        }
        finally { Object.DestroyImmediate(full); Object.DestroyImmediate(half); }
    }
    [Test] public void MissingRunnerHidesRimAndDisableKeepsItHidden()
    {
        var type = Type.GetType("LocalFirstPersonGoggles, Assembly-CSharp");
        Assert.That(type, Is.Not.Null);
        var controller = root.AddComponent(type);
        type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Invoke(controller, null);
        Assert.That(((Graphic)rim).enabled, Is.False);
        ((Behaviour)controller).enabled = false;
        Assert.That(((Graphic)rim).enabled, Is.False);
    }
}

