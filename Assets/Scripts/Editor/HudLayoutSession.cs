using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Editor-only native RectTransform workspace. Samples never become gameplay objects.</summary>
public sealed partial class HudLayoutSession : IDisposable
{
    public enum Kind { SelfStatus, TeamInfo, Enemy, Teammate, ExistingRect }
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public readonly Kind Mode;
    public readonly Scene Scene;
    public RectTransform Handle { get; private set; }
    public Object Source { get; private set; }
    public ScriptableObject Draft { get; private set; }
    public Camera Camera { get; private set; }
    public GameObject Root { get; private set; }
    public bool ShowGuard = true;
    public int StatusCount = 4;
    public string LastMessage { get; private set; }
    readonly bool previewScene;
    readonly bool ownsScene;
    readonly ScriptableObject configuredSource;
    RectTransform canvasRect;
    StatusEffectHudSettings statusStyle;
    LocalPlayerGuardHUD guard;
    StatusEffectListView statuses;
    EnemyHealthBarGraphic enemy;
    LocalHealthSegmentView teammate;
    TMP_Text teammateName;
    RectTransform teamPlane;
    string originalJson;
    RectState originalRect;
    bool disposed;

    [Serializable]
    public struct RectState
    {
        public Vector2 min, max, pivot, position, size;
        public Vector3 scale;
        public Quaternion rotation;
        public static RectState Read(RectTransform r) => new RectState {
            min = r.anchorMin, max = r.anchorMax, pivot = r.pivot,
            position = r.anchoredPosition, size = r.sizeDelta, scale = r.localScale, rotation = r.localRotation };
        public void Write(RectTransform r)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot;
            r.anchoredPosition = position; r.sizeDelta = size; r.localScale = scale; r.localRotation = rotation;
        }
        public bool Same(RectState other) => JsonUtility.ToJson(this) == JsonUtility.ToJson(other);
    }

    public HudLayoutSession(Kind kind, bool visibleScene, RectTransform sourceRect = null, ScriptableObject settingsSource = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先離開 Play Mode。");
        if (kind == Kind.ExistingRect && (sourceRect == null || EditorUtility.IsPersistent(sourceRect)))
            throw new InvalidOperationException("請先在 Scene 或 Prefab 模式選取要調整的 UI 容器，不能直接修改 Project 裡的 Prefab 資產。");
        if (kind == Kind.ExistingRect && (sourceRect.GetComponentInChildren<Fusion.NetworkObject>(true) != null || sourceRect.GetComponentInChildren<Fusion.NetworkRunner>(true) != null))
            throw new InvalidOperationException("請選取純 UI 容器；排版草稿不複製網路玩家或 Runner。");
        if (kind == Kind.ExistingRect && (sourceRect.GetComponent<BattleHudReferenceFrame>() != null || IsLayoutDriven(sourceRect)))
            throw new InvalidOperationException("這個物件由版面控制器自動定位；請改選外層容器，或在原 Inspector 調整 Layout Group 的間距／尺寸。");
        Mode = kind; previewScene = !visibleScene; configuredSource = settingsSource;
        var active = SceneManager.GetActiveScene();
        var stage = visibleScene ? PrefabStageUtility.GetCurrentPrefabStage() : null;
        ownsScene = !visibleScene;
        // A DontSave root in the current stage works even with an untitled, unsaved scene.
        // Do not open/close the user's scene or Prefab stage just to display editing handles.
        Scene = stage != null ? stage.scene : visibleScene ? active : EditorSceneManager.NewPreviewScene();
        if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        try
        {
            Root = new GameObject("HUD 排版草稿（請用套用版面保存）");
            SceneManager.MoveGameObjectToScene(Root, Scene);
            Root.hideFlags = HideFlags.DontSave;
            // Keep the temporary canvas clear of the user's level geometry. No camera or SceneView is focused.
            Root.transform.position = new Vector3(10000, 10000, 0);
            var cameraObject = new GameObject("HUD preview camera", typeof(Camera));
            cameraObject.transform.SetParent(Root.transform, false);
            Camera = cameraObject.GetComponent<Camera>(); Camera.enabled = false; Camera.scene = Scene;
            Camera.orthographic = true; Camera.orthographicSize = 540;
            Camera.transform.localPosition = new Vector3(0, 0, -100);
            Camera.nearClipPlane = .1f; Camera.farClipPlane = 200;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color32(23, 29, 22, 255);
            canvasRect = Rect(Root.transform, "1920 x 1080", new Vector2(1920, 1080));
            var canvas = canvasRect.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = Camera;
            switch (Mode)
            {
                case Kind.SelfStatus: BuildSelf(); break;
                case Kind.TeamInfo: BuildTeam(); break;
                case Kind.Enemy: BuildEnemy(); break;
                case Kind.Teammate: BuildTeammate(); break;
                case Kind.ExistingRect: BuildExisting(sourceRect); break;
            }
            if (Source == null || Handle == null) throw new InvalidOperationException("找不到正式設定或 HUD 引用。");
            originalJson = EditorJsonUtility.ToJson(Source);
            if (Source is RectTransform rect) originalRect = RectState.Read(rect);
            Tick(); ProtectSamples();
        }
        catch { Dispose(); throw; }
    }

    T LoadDraft<T>(string path) where T : ScriptableObject
    {
        var source = configuredSource != null ? configuredSource as T : AssetDatabase.LoadAssetAtPath<T>(path);
        if (source == null) throw new InvalidOperationException("找不到設定：" + path);
        Source = source;
        var draft = Object.Instantiate(source); draft.hideFlags = HideFlags.HideAndDontSave;
        Draft = draft; return draft;
    }
    static bool IsLayoutDriven(RectTransform rect)
    {
        var group = rect.parent != null ? rect.parent.GetComponent<LayoutGroup>() : null;
        var element = rect.GetComponent<LayoutElement>();
        return group != null && group.isActiveAndEnabled && (element == null || !element.ignoreLayout);
    }
    static RectTransform Rect(Transform parent, string name, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = size; return rect;
    }
    static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset");
    static TMP_Text Label(Transform parent, string name, Vector2 size, string value)
    {
        var text = Rect(parent, name, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = Font; text.text = value; text.fontSize = 20; text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false; return text;
    }
    void OriginMarker()
    {
        for (int axis = 0; axis < 2; axis++)
        {
            var line = Rect(canvasRect, "位置參考", axis == 0 ? new Vector2(20, 2) : new Vector2(2, 20)).gameObject.AddComponent<Image>();
            line.color = new Color(1, 1, 1, .4f); line.raycastTarget = false;
        }
    }
    void BuildSelf()
    {
        var draft = LoadDraft<StatusEffectHudSettings>(StatusEffectHudSetup.SettingsPath);
        Handle = Rect(canvasRect, "可拖曳：自己異常與格檔", new Vector2(draft.selfWidth, 160));
        Handle.pivot = new Vector2(.5f, 1);
        Handle.anchoredPosition = new Vector2(draft.selfHorizontalOffset, -draft.selfCrosshairOffset);
        var font = Label(Handle, "字型來源", Vector2.zero, "");
        guard = LocalPlayerGuardHUD.Create(Handle); Object.DestroyImmediate(font.gameObject);
        statuses = StatusEffectListView.Create(Handle);
        statusStyle = Object.Instantiate(draft); statusStyle.hideFlags = HideFlags.HideAndDontSave;
        OriginMarker();
    }
    void BuildTeam()
    {
        var draft = LoadDraft<TeamInfoPanelSettings>(TeamInfoPanelSetup.SettingsPath);
        var host = new GameObject("TAB preview owner", typeof(LocalTeamInfoPanel)); host.transform.SetParent(Root.transform, false);
        var panel = host.GetComponent<LocalTeamInfoPanel>(); panel.enabled = false;
        typeof(LocalTeamInfoPanel).GetField("settings", Private).SetValue(panel, draft);
        typeof(LocalTeamInfoPanel).GetMethod("Build", Private).Invoke(panel, null);
        var canvas = host.GetComponentInChildren<Canvas>(true); canvas.gameObject.SetActive(true);
        canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = Camera;
        canvas.GetComponent<CanvasScaler>().enabled = false;
        var rect = (RectTransform)canvas.transform; rect.sizeDelta = draft.referenceResolution;
        rect.localPosition = Vector3.zero; rect.localScale = Vector3.one;
        Handle = (RectTransform)rect.Find("Fixed four player frame");
        teamPlane = (RectTransform)Handle.Find("Reference plane");
        FillTeamSamples(panel, draft, teamPlane);
        Camera.orthographicSize = draft.referenceResolution.y * .5f;
    }
    void BuildEnemy()
    {
        var draft = LoadDraft<EnemyHealthBarSettings>(EnemyHealthBarSetup.SettingsPath);
        Handle = Rect(canvasRect, "可拖曳：敵人血條（上下間距與寬高）", draft.Size);
        Handle.anchoredPosition = new Vector2(0, draft.padding + draft.Size.y * .5f);
        enemy = Handle.gameObject.AddComponent<EnemyHealthBarGraphic>();
        statuses = StatusEffectListView.Create(Handle);
        statusStyle = Object.Instantiate(StatusEffectHudSettings.Default); statusStyle.hideFlags = HideFlags.HideAndDontSave;
        OriginMarker();
    }
    void BuildTeammate()
    {
        var draft = LoadDraft<TeammateDisplaySettings>(TeammateDisplaySetup.SettingsPath);
        Handle = Rect(canvasRect, "可拖曳：隊友名條（上下間距與寬高）", new Vector2(draft.nameWidth * .66f, draft.barHeight));
        Handle.anchoredPosition = new Vector2(0, draft.headPadding);
        teammate = Handle.gameObject.AddComponent<LocalHealthSegmentView>();
        teammateName = Label(Handle, "隊友名稱（自動避開異常列）", new Vector2(draft.nameWidth, 32), "隊友 A");
        statuses = StatusEffectListView.Create(Handle);
        statusStyle = Object.Instantiate(StatusEffectHudSettings.Default); statusStyle.hideFlags = HideFlags.HideAndDontSave;
        OriginMarker();
    }

    public void Tick()
    {
        if (disposed || Handle == null) return;
        switch (Mode)
        {
            case Kind.SelfStatus:
                EditorUtility.CopySerialized(Draft, statusStyle);
                statusStyle.selfHorizontalOffset = statusStyle.selfCrosshairOffset = 0;
                float width = Mathf.Max(40, Handle.rect.width);
                // The original guard clamp is against the full screen, not against the narrower authoring handle.
                float guardWidth = width * Mathf.Clamp(statusStyle.guardWidthMultiplier, 1, 1.5f);
                float leading = guard.Present(new GuardHudSnapshot(ShowGuard, true, false, 70, 100, 0, 0), statusStyle, width);
                if (ShowGuard && guardWidth > width)
                {
                    var old = Handle.sizeDelta; Handle.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, guardWidth);
                    leading = guard.Present(new GuardHudSnapshot(true, true, false, 70, 100, 0, 0), statusStyle, width);
                    Handle.sizeDelta = old;
                }
                PinTop((RectTransform)guard.transform, 0);
                statuses.Present(Samples(false), StatusCount, statusStyle, width, true, leading);
                PinTop((RectTransform)statuses.transform, leading);
                Handle.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, leading + ((RectTransform)statuses.transform).rect.height);
                break;
            case Kind.TeamInfo:
                teamPlane.localScale = new Vector3(Handle.rect.width / 1104f, Handle.rect.height / 824f, 1);
                break;
            case Kind.Enemy:
                enemy.SetSnapshot(.65f, (EnemyHealthBarSettings)Draft);
                statuses.Present(Samples(true), StatusCount, statusStyle, Handle.rect.width, false);
                break;
            case Kind.Teammate:
                var settings = (TeammateDisplaySettings)Draft;
                teammate.ConfigureTeammatePresentation(settings); teammate.ApplyHealthSnapshot(70, 100); teammate.SetShieldHealth(40);
                statuses.Present(Samples(false), StatusCount, statusStyle, Handle.rect.width, false);
                teammateName.font = settings.font != null ? settings.font : Font;
                teammateName.fontSize = settings.nameSize;
                teammateName.rectTransform.sizeDelta = new Vector2(Handle.rect.width / .66f, 32);
                teammateName.rectTransform.anchoredPosition = new Vector2(0, 20 + statusStyle.Height(StatusCount, false));
                break;
        }
    }
    static void PinTop(RectTransform rect, float offset)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -offset);
    }
    static StatusEffectSnapshot[] Samples(bool enemy)
    {
        var kinds = enemy ? new[] { StatusEffectKind.Stun, StatusEffectKind.Root, StatusEffectKind.BulletTime, StatusEffectKind.Mark }
            : new[] { StatusEffectKind.Captured, StatusEffectKind.BulletTime, StatusEffectKind.GrappleBlocked, StatusEffectKind.Slow };
        var samples = new StatusEffectSnapshot[kinds.Length];
        for (int i = 0; i < samples.Length; i++) samples[i] = StatusEffectSnapshot.FromProgress(kinds[i], .8f - i * .15f);
        return samples;
    }
    void ProtectSamples()
    {
        foreach (var t in Root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave | (t == Handle ? HideFlags.None : HideFlags.NotEditable);
    }

    public void Apply()
    {
        if (disposed || Source == null || Handle == null) throw new InvalidOperationException("來源或草稿已關閉，請重新載入。");
        if (Mode == Kind.ExistingRect ? !originalRect.Same(RectState.Read((RectTransform)Source)) : originalJson != EditorJsonUtility.ToJson(Source))
            throw new InvalidOperationException("正式來源已在別處變更。請先關閉草稿並重新載入，避免覆蓋較新的調整。");
        if (Mode != Kind.ExistingRect && (Handle.localRotation != Quaternion.identity ||
            Handle.anchorMin != Vector2.one * .5f || Handle.anchorMax != Vector2.one * .5f ||
            Handle.pivot != (Mode == Kind.SelfStatus ? new Vector2(.5f, 1) : Vector2.one * .5f)))
            throw new InvalidOperationException("此動態 HUD 請保留置中錨點、原 Pivot 與零旋轉；以位置及寬高排版。");
        if (Mode != Kind.ExistingRect && Mode != Kind.TeamInfo && Handle.localScale != Vector3.one)
            throw new InvalidOperationException("請用 Rect Tool 調整寬高，保留 Scale 1；動態項目使用原有尺寸規則。");
        if (Mode == Kind.TeamInfo && (Mathf.Abs(Handle.localScale.x - Handle.localScale.y) > .001f || Handle.localScale.x < .7f || Handle.localScale.x > 1.2f))
            throw new InvalidOperationException("TAB 請使用相同的 X/Y 縮放，範圍 0.7～1.2。");
        ValidateDimensions();
        Undo.RecordObject(Source, "套用 HUD 版面");
        switch (Mode)
        {
            case Kind.SelfStatus:
                var self = (StatusEffectHudSettings)Source; var selfDraft = (StatusEffectHudSettings)Draft;
                self.selfHorizontalOffset = Handle.anchoredPosition.x; self.selfCrosshairOffset = -Handle.anchoredPosition.y;
                self.selfWidth = Handle.rect.width;
                self.selfIconSize = selfDraft.selfIconSize; self.selfBarHeight = selfDraft.selfBarHeight; self.selfRowGap = selfDraft.selfRowGap;
                self.guardFontSize = selfDraft.guardFontSize; self.guardWidthMultiplier = selfDraft.guardWidthMultiplier;
                self.guardBarHeight = selfDraft.guardBarHeight; self.guardStatusGap = selfDraft.guardStatusGap;
                break;
            case Kind.TeamInfo:
                var team = (TeamInfoPanelSettings)Source;
                team.windowOffset = Handle.anchoredPosition; team.windowSize = Handle.rect.size; team.scale = Handle.localScale.x;
                break;
            case Kind.Enemy:
                var enemySettings = (EnemyHealthBarSettings)Source;
                enemySettings.barSize = Handle.rect.size; enemySettings.padding = Handle.anchoredPosition.y - Handle.rect.height * .5f;
                break;
            case Kind.Teammate:
                var teammateSettings = (TeammateDisplaySettings)Source;
                teammateSettings.nameWidth = Handle.rect.width / .66f; teammateSettings.barHeight = Handle.rect.height;
                teammateSettings.headPadding = Handle.anchoredPosition.y;
                teammateSettings.nameSize = ((TeammateDisplaySettings)Draft).nameSize;
                break;
            case Kind.ExistingRect:
                var target = (RectTransform)Source; RectState.Read(Handle).Write(target);
                if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                originalRect = RectState.Read(target);
                break;
        }
        EditorUtility.SetDirty(Source);
        if (EditorUtility.IsPersistent(Source)) AssetDatabase.SaveAssetIfDirty(Source);
        originalJson = EditorJsonUtility.ToJson(Source);
        LastMessage = Mode == Kind.ExistingRect ? "已套用到原物件；請依原本流程儲存 Scene／Prefab。" : "已保存到正式設定；下次建立 HUD 會沿用。Ctrl+Z 可復原本次套用。";
    }
    void ValidateDimensions()
    {
        var size = Handle.rect.size; var p = Handle.anchoredPosition;
        if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || !float.IsFinite(p.x) || !float.IsFinite(p.y) || size.x <= 0 || size.y < 0)
            throw new InvalidOperationException("位置與尺寸必須是有效數值，寬度須大於零。");
        if (Mode == Kind.SelfStatus && (p.y > 0 || size.x < 40 || size.x > 1920))
            throw new InvalidOperationException("自己異常區須位於準心下方，寬度為 40～1920。");
        if (Mode == Kind.Enemy && (Mathf.Abs(p.x) > .01f || size.x < 20 || size.y < 3 || p.y < size.y * .5f))
            throw new InvalidOperationException("敵人血條保持頭頂水平置中；寬至少 20、高至少 3，底部不可低於參考十字。");
        if (Mode == Kind.Teammate && (Mathf.Abs(p.x) > .01f || size.x < 40 * .66f || size.y < 3 || p.y < 0))
            throw new InvalidOperationException("隊友名條保持頭頂水平置中；寬至少 26.4、高至少 3，Y 不得小於零。");
    }

    public void Render(RenderTexture target)
    {
        Tick(); Canvas.ForceUpdateCanvases();
        ProtectSamples();
        foreach (var text in Root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
        Camera.targetTexture = target;
        try { Camera.Render(); } finally { Camera.targetTexture = null; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (Root != null) Object.DestroyImmediate(Root);
        if (Draft != null) Object.DestroyImmediate(Draft);
        if (statusStyle != null) Object.DestroyImmediate(statusStyle);
        if (ownsScene && Scene.IsValid() && Scene.isLoaded)
        { if (previewScene) EditorSceneManager.ClosePreviewScene(Scene); else EditorSceneManager.CloseScene(Scene, true); }
    }
}
