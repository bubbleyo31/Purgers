using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class DamageNumberBehaviorTests
{
    private DamageNumberSettings settings;
    private DamageNumberBuffer buffer;
    private Scene scene;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [SetUp] public void Setup()
    {
        settings = ScriptableObject.CreateInstance<DamageNumberSettings>();
        buffer = new DamageNumberBuffer(); scene = EditorSceneManager.NewPreviewScene();
    }
    [TearDown] public void Cleanup()
    {
        EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(settings);
    }
    private GameObject Create(string name, params System.Type[] components)
    {
        var go = new GameObject(name, components); SceneManager.MoveGameObjectToScene(go, scene); return go;
    }
    private CombatHitFeedbackData Hit(int target = 1, DamageType type = DamageType.Bullet, bool head = false) =>
        new CombatHitFeedbackData { TargetKey = target, SourceKey = 7, IsEnemyTarget = true, HasEffectiveDamage = true,
            AppliedDamage = 24f, DamageType = type, HitZone = head ? DamageHitZoneType.Head : DamageHitZoneType.Body,
            HitPoint = new Vector3(0f, 0f, 10f) };
    [Test] public void NormalHitsRemainIndividualAndContinuousTicksMerge()
    {
        Assert.That(buffer.Add(Hit(), 0f, settings), Is.Zero);
        Assert.That(buffer.Add(Hit(), .1f, settings), Is.EqualTo(1));
        var dot = Hit(type: DamageType.DamageOverTime);
        Assert.That(buffer.Add(dot, .11f, settings), Is.EqualTo(2));
        Assert.That(buffer.Add(dot, .15f, settings), Is.EqualTo(2));
        Assert.That(buffer[2].Amount, Is.EqualTo(48d));
    }
    [Test] public void TogglingBothModesChangesActualAggregation()
    {
        settings.individualHits = false; settings.mergeContinuousDamage = false;
        Assert.That(buffer.Add(Hit(), 0f, settings), Is.Zero);
        Assert.That(buffer.Add(Hit(), .1f, settings), Is.Zero);
        Assert.That(buffer.Add(Hit(type: DamageType.DamageOverTime), .11f, settings), Is.EqualTo(1));
        Assert.That(buffer.Add(Hit(type: DamageType.DamageOverTime), .15f, settings), Is.EqualTo(2));
    }
    [Test] public void MergingNeverMixesTargetsSourcesTypesOrHeadStyle()
    {
        settings.individualHits = false;
        buffer.Add(Hit(), 0f, settings);
        Assert.That(buffer.Add(Hit(2), .01f, settings), Is.EqualTo(1));
        Assert.That(buffer.Add(Hit(head: true), .02f, settings), Is.EqualTo(2));
        var other = Hit(); other.SourceKey = 8;
        Assert.That(buffer.Add(other, .03f, settings), Is.EqualTo(3));
        Assert.That(buffer.Add(Hit(type: DamageType.Explosion), .04f, settings), Is.EqualTo(4));
    }
    [Test] public void MergingRetainsFirstPointAndHasBoundedLifetime()
    {
        settings.individualHits = false;
        var first = Hit(); buffer.Add(first, 0f, settings);
        var moved = Hit(); moved.HitPoint = new Vector3(50f, 10f, 100f);
        buffer.Add(moved, .1f, settings);
        Assert.That(buffer[0].Hit.HitPoint, Is.EqualTo(first.HitPoint));
        Assert.That(buffer[0].Started, Is.Zero);
        Assert.That(buffer.Add(moved, .21f, settings), Is.EqualTo(1));
        buffer.Expire(.81f, settings); Assert.That(buffer[0].Active, Is.False);
        Assert.That(buffer[1].Active, Is.True);
    }
    [Test] public void BudgetRecyclesOldestAndShrinkRemovesExcess()
    {
        settings.maximumNumbers = 2;
        buffer.Add(Hit(), 0f, settings); buffer.Add(Hit(), .1f, settings);
        Assert.That(buffer.Add(Hit(), .2f, settings), Is.Zero);
        settings.maximumNumbers = 1; buffer.Expire(.3f, settings);
        Assert.That(buffer[1].Active, Is.False);
    }
    [Test] public void InvalidEnemyAndNonFinitePointAreRejected()
    {
        var hit = Hit(); hit.IsEnemyTarget = false; Assert.That(buffer.Add(hit, 0f, settings), Is.EqualTo(-1));
        hit = Hit(); hit.HitPoint = Vector3.one * float.NaN; Assert.That(buffer.Add(hit, 0f, settings), Is.EqualTo(-1));
        hit = Hit(); hit.HasEffectiveDamage = false; Assert.That(buffer.Add(hit, 0f, settings), Is.EqualTo(-1));
        hit = Hit(); hit.TargetKey = 0; Assert.That(buffer.Add(hit, 0f, settings), Is.EqualTo(-1));
        settings.showNumbers = false; Assert.That(buffer.Add(Hit(), 0f, settings), Is.EqualTo(-1));
    }
    [Test] public void FractionalDamageDoesNotRoundPositiveHitToZero()
    {
        Assert.That(DamageNumberRules.FormatAmount(.02d), Is.EqualTo("<0.1"));
        Assert.That(DamageNumberRules.FormatAmount(24d), Is.EqualTo("24"));
        Assert.That(DamageNumberRules.FormatAmount(24.25d), Is.EqualTo("24.3"));
    }
    [Test] public void ProjectionRejectsBehindAndOutsideButKeepsDistantHit()
    {
        var cam = Create("Camera").AddComponent<Camera>();
        Assert.That(DamageNumberRules.TryProject(cam, Vector3.back * 10f, out _), Is.False);
        Assert.That(DamageNumberRules.TryProject(cam, new Vector3(100f, 0f, 10f), out _), Is.False);
        Assert.That(DamageNumberRules.TryProject(cam, Vector3.forward * 10000f, out _), Is.True);
        Assert.That(DamageNumberRules.TryProject(cam, Vector3.zero, out _), Is.False);
    }
    [Test] public void ActualUiKeepsSizeAtDistanceAndAppliesConfigurableHeadStyleThroughWall()
    {
        var root = Create("HUD", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var hud = root.AddComponent<LocalPlayerDamageNumbers>();
        typeof(LocalPlayerDamageNumbers).GetMethod("Awake", Private).Invoke(hud, null);
        var cam = Create("Camera").AddComponent<Camera>();
        var wall = Create("Wall", typeof(BoxCollider)); wall.transform.position = Vector3.forward * 5f;
        hud.Configure(settings, null, cam);
        settings.popScale = 1f; settings.headColor = Color.magenta; settings.headScale = 1.6f;
        Add(hud, Hit(), 0f);
        var distant = Hit(2); distant.HitPoint = Vector3.forward * 1000f; Add(hud, distant, 0f);
        Add(hud, Hit(3, head: true), 0f); Render(hud, cam, .15f);
        var normal = root.transform.Find("Damage Number 0").GetComponent<TextMeshProUGUI>();
        var far = root.transform.Find("Damage Number 1").GetComponent<TextMeshProUGUI>();
        var head = root.transform.Find("Damage Number 2").GetComponent<TextMeshProUGUI>();
        Assert.That(hud.ActiveCount, Is.EqualTo(3));
        Assert.That(normal.fontSize, Is.EqualTo(far.fontSize));
        Assert.That(normal.rectTransform.localScale, Is.EqualTo(far.rectTransform.localScale));
        Assert.That(head.fontSize / normal.fontSize, Is.EqualTo(1.6f).Within(.001f));
        Assert.That(head.color, Is.EqualTo(Color.magenta)); Assert.That(head.raycastTarget, Is.False);
        var before = normal.rectTransform.anchoredPosition;
        Render(hud, cam, .25f);
        Assert.That(normal.rectTransform.anchoredPosition.y, Is.GreaterThan(before.y));
        Render(hud, cam, .9f); Assert.That(hud.ActiveCount, Is.Zero);
        Assert.That(normal.gameObject.activeSelf, Is.False);
    }
    [Test] public void CameraTurnEndsNumberAndReturningDoesNotReplayIt()
    {
        var root = Create("HUD", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var hud = root.AddComponent<LocalPlayerDamageNumbers>();
        typeof(LocalPlayerDamageNumbers).GetMethod("Awake", Private).Invoke(hud, null);
        var cam = Create("Camera").AddComponent<Camera>(); hud.Configure(settings, null, cam);
        Add(hud, Hit(), 0f); Render(hud, cam, .1f); Assert.That(hud.ActiveCount, Is.EqualTo(1));
        cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f); Render(hud, cam, .2f);
        cam.transform.rotation = Quaternion.identity; Render(hud, cam, .3f);
        Assert.That(hud.ActiveCount, Is.Zero);
    }
    private static void Add(LocalPlayerDamageNumbers hud, CombatHitFeedbackData hit, float now) =>
        typeof(LocalPlayerDamageNumbers).GetMethod("AddSnapshot", Private).Invoke(hud, new object[] { hit, now });
    private static void Render(LocalPlayerDamageNumbers hud, Camera cam, float now) =>
        typeof(LocalPlayerDamageNumbers).GetMethod("RenderSnapshots", Private).Invoke(hud, new object[] { now, cam });
}
