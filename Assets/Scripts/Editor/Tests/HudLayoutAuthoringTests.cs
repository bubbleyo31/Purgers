using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEditor;

[Category("PurgersRegression")]
public sealed class HudLayoutAuthoringTests
{
    [TestCase(HudLayoutSession.Kind.SelfStatus)]
    [TestCase(HudLayoutSession.Kind.TeamInfo)]
    [TestCase(HudLayoutSession.Kind.Enemy)]
    [TestCase(HudLayoutSession.Kind.Teammate)]
    public void DraftCreationAndDisposalNeverMutatesTheSource(HudLayoutSession.Kind kind)
    {
        Object source; string json; var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool dirty = active.isDirty;
        using (var session = new HudLayoutSession(kind, false))
        {
            source = session.Source; json = EditorJsonUtility.ToJson(source);
            Assert.IsNotNull(session.Handle);
            Assert.IsNotNull(session.Root.GetComponentInChildren<Canvas>(true));
            Assert.IsEmpty(session.Root.GetComponentsInChildren<Fusion.NetworkRunner>(true));
            session.Handle.anchoredPosition += new Vector2(0, -20); session.Tick();
            Assert.AreEqual(json, EditorJsonUtility.ToJson(source));
        }
        Assert.AreEqual(json, EditorJsonUtility.ToJson(source));
        Assert.AreEqual(active, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Assert.AreEqual(dirty, active.isDirty);
    }

    [Test]
    public void DragApplyUndoPersistsOnlyTheRequestedSelfLayout()
    {
        var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<StatusEffectHudSettings>(StatusEffectHudSetup.SettingsPath));
        string original = EditorJsonUtility.ToJson(source);
        try
        {
            using (var session = new HudLayoutSession(HudLayoutSession.Kind.SelfStatus, false, settingsSource: source))
            {
                session.Handle.anchoredPosition = new Vector2(140, -170);
                session.Handle.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 330);
                session.Tick(); session.Apply(); Undo.FlushUndoRecordObjects();
                Assert.AreEqual(140, source.selfHorizontalOffset); Assert.AreEqual(170, source.selfCrosshairOffset);
                Assert.AreEqual(330, source.selfWidth); Assert.AreEqual(5, source.healthGap, "World placement is outside this edit.");
                Undo.PerformUndo(); Assert.AreEqual(original, EditorJsonUtility.ToJson(source));
            }
        }
        finally { Object.DestroyImmediate(source); }
    }

    [Test]
    public void ConcurrentSourceEditsAreRejectedInsteadOfOverwritten()
    {
        var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TeamInfoPanelSettings>(TeamInfoPanelSetup.SettingsPath));
        try
        {
            using (var session = new HudLayoutSession(HudLayoutSession.Kind.TeamInfo, false, settingsSource: source))
            {
                source.windowOffset = new Vector2(222, 44);
                Assert.Throws<System.InvalidOperationException>(() => session.Apply());
                Assert.AreEqual(new Vector2(222, 44), source.windowOffset);
            }
        }
        finally { Object.DestroyImmediate(source); }
    }

    [Test]
    public void ExistingRectAppliesPlacementWithoutCopyingSampleChildren()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Source canvas", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var source = new GameObject("Source UI", typeof(RectTransform)).GetComponent<RectTransform>();
            source.SetParent(root.transform, false);
            using (var session = new HudLayoutSession(HudLayoutSession.Kind.ExistingRect, false, source))
            {
                session.Handle.anchoredPosition = new Vector2(170, -70);
                session.Handle.sizeDelta = new Vector2(240, 60);
                session.Apply();
                Assert.AreEqual(new Vector2(170, -70), source.anchoredPosition);
                Assert.AreEqual(new Vector2(240, 60), source.sizeDelta);
                Assert.AreEqual(0, source.childCount);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void ExistingSkillContainerUsesRealStatesAndDoesNotMutatePrefabTemplate()
    {
        var prefab = PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/StageHUD.prefab");
        try
        {
            var hud = prefab.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
            var properties = new SerializedObject(hud);
            var slots = (RectTransform)properties.FindProperty("slotsRoot").objectReferenceValue;
            var template = (GameObject)properties.FindProperty("slotTemplate").objectReferenceValue;
            bool active = template.activeSelf; int children = slots.childCount;
            using (var session = new HudLayoutSession(HudLayoutSession.Kind.ExistingRect, false, slots))
            {
                Assert.AreEqual(2, session.Handle.GetComponentsInChildren<AbilitySlotStateGraphic>(false).Length);
                Assert.AreEqual(active, template.activeSelf);
                Assert.AreEqual(children, slots.childCount);
                Assert.AreEqual(0, session.Handle.GetComponentsInChildren<Fusion.NetworkObject>(true).Length);
            }
            Assert.AreEqual(active, template.activeSelf); Assert.AreEqual(children, slots.childCount);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    [Test]
    public void NativeSceneWorkspaceClosesWithoutChangingTheActiveScene()
    {
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        int count = UnityEngine.SceneManagement.SceneManager.sceneCount;
        using (var session = new HudLayoutSession(HudLayoutSession.Kind.SelfStatus, true))
        {
            Assert.AreEqual(active, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Assert.IsTrue((session.Root.hideFlags & HideFlags.DontSaveInEditor) != 0);
            Assert.AreEqual(HideFlags.DontSave, session.Handle.gameObject.hideFlags);
        }
        Assert.AreEqual(count, UnityEngine.SceneManagement.SceneManager.sceneCount);
        Assert.AreEqual(active, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    [Test]
    public void OverlayCanvasPreviewDoesNotRenderIntoTheUsersGameView()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Source overlay", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var source = root.GetComponent<Canvas>(); source.renderMode = RenderMode.ScreenSpaceOverlay;
            using (var session = new HudLayoutSession(HudLayoutSession.Kind.ExistingRect, false, (RectTransform)root.transform))
            {
                Assert.AreEqual(RenderMode.WorldSpace, session.Handle.GetComponent<Canvas>().renderMode);
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, source.renderMode);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    [Test]
    public void SelfHorizontalPlacementSurvivesCastAvoidanceRefresh()
    {
        var settings = ScriptableObject.CreateInstance<StatusEffectHudSettings>();
        var root = new GameObject("HUD layout test", typeof(RectTransform));
        try
        {
            ((RectTransform)root.transform).sizeDelta = new Vector2(1920, 1080);
            var field = typeof(StatusEffectHudSettings).GetField("selfHorizontalOffset");
            Assert.NotNull(field, "Self HUD needs a persisted horizontal placement, not a transient preview transform.");
            field.SetValue(settings, 125f);
            var view = LocalPlayerGuardHUD.Create((RectTransform)root.transform);
            view.Present(new GuardHudSnapshot(true, true, false, 70, 100, 0, 0), settings, 280);
            view.SetCrosshairOffset(110);
            Assert.AreEqual(new Vector2(125, -110), ((RectTransform)view.transform).anchoredPosition);
            var statuses = StatusEffectListView.Create((RectTransform)root.transform);
            statuses.Present(new StatusEffectSnapshot[0], 0, settings, 280, true);
            statuses.SetSelfCrosshairOffset(110, 40);
            Assert.AreEqual(new Vector2(125, -150), ((RectTransform)statuses.transform).anchoredPosition);
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(settings); }
    }

    [Test]
    public void TeamWindowReadsSavedPositionWithoutAPlayerOrRunner()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var settings = ScriptableObject.CreateInstance<TeamInfoPanelSettings>();
        try
        {
            var field = typeof(TeamInfoPanelSettings).GetField("windowOffset");
            Assert.NotNull(field, "TAB position must be consumed by the real view builder.");
            field.SetValue(settings, new Vector2(90, -40));
            var root = new GameObject("Team layout test", typeof(LocalTeamInfoPanel));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var panel = root.GetComponent<LocalTeamInfoPanel>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(LocalTeamInfoPanel).GetField("settings", flags).SetValue(panel, settings);
            typeof(LocalTeamInfoPanel).GetMethod("Build", flags).Invoke(panel, null);
            var window = (RectTransform)root.transform.Find("Team Info Canvas/Fixed four player frame");
            Assert.AreEqual(new Vector2(90, -40), window.anchoredPosition);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(settings); }
    }
}
