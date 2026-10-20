using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>User-invoked authoring UI. Background verification constructs sessions directly and never opens this window.</summary>
public sealed class HudLayoutWorkspace : EditorWindow
{
    static readonly string[] Modes = { "自己異常／格檔", "TAB 隊伍資訊窗", "敵人血條／異常", "隊友名條／異常", "現有 Scene／Prefab HUD" };
    HudLayoutSession session;
    HudLayoutSession.Kind mode;
    RectTransform source;
    RenderTexture preview;
    string message;
    Vector2 scroll;

    [MenuItem("Tools/Purgers/UI/HUD 排版")]
    public static void Open() => GetWindow<HudLayoutWorkspace>("HUD 排版");
    void OnEnable()
    {
        AssemblyReloadEvents.beforeAssemblyReload += CloseSession;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.quitting += CloseSession;
        EditorSceneManager.sceneSaving += BeforeSceneSave;
    }
    void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= CloseSession;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.quitting -= CloseSession;
        EditorSceneManager.sceneSaving -= BeforeSceneSave;
        CloseSession();
    }
    void BeforeSceneSave(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (session != null && session.Scene == scene)
            message = "排版預覽物件不會寫入場景；請用「套用版面」保存正式設定。";
    }
    void PlayModeChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) CloseSession(); }
    void OnInspectorUpdate()
    {
        if (session == null) return;
        if (!session.Scene.IsValid() || !session.Scene.isLoaded || session.Handle == null) { CloseSession(); return; }
        try { EnsurePreview(); session.Render(preview); Repaint(); }
        catch (Exception e) { message = e.GetBaseException().Message; CloseSession(); }
    }
    void EnsurePreview()
    {
        if (preview != null) return;
        preview = new RenderTexture(960, 540, 24) { hideFlags = HideFlags.HideAndDontSave }; preview.Create();
    }
    void CloseSession()
    {
        session?.Dispose(); session = null;
        if (preview != null) { preview.Release(); DestroyImmediate(preview); preview = null; }
        Repaint();
    }
    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("1. 建立排版草稿 → 2. 選取可拖曳容器，以 Rect Tool (T) 調整位置／寬高 → 3. 套用版面。範例圖示與血量不會寫入遊戲。", MessageType.Info);
        using (new EditorGUI.DisabledScope(session != null))
        {
            mode = (HudLayoutSession.Kind)EditorGUILayout.Popup("HUD", (int)mode, Modes);
            if (mode == HudLayoutSession.Kind.ExistingRect)
            {
                source = (RectTransform)EditorGUILayout.ObjectField("正式 UI 容器", source, typeof(RectTransform), true);
                if (GUILayout.Button("使用目前選取的 RectTransform")) source = Selection.activeTransform as RectTransform;
                EditorGUILayout.HelpBox("先開啟 Scene 或 Prefab 模式，再選取技能 Slots、施法條、生命、動能、武器或其他 UI 容器。只套用所選容器的 RectTransform，不保存範例子物件。", MessageType.None);
            }
            if (GUILayout.Button("建立排版草稿"))
            {
                try { session = new HudLayoutSession(mode, true, source); message = null; EnsurePreview(); session.Render(preview); }
                catch (Exception e) { message = e.GetBaseException().Message; CloseSession(); }
            }
        }
        if (session != null)
        {
            EditorGUILayout.ObjectField("正式保存來源", session.Source, typeof(UnityEngine.Object), true);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("選取可拖曳容器")) { Selection.activeGameObject = session.Handle.gameObject; Tools.current = Tool.Rect; }
            if (GUILayout.Button("在 Scene 檢視"))
            {
                // Explicit user click only; verification never invokes this focus/frame path.
                Selection.activeGameObject = session.Handle.gameObject; Tools.current = Tool.Rect;
                var view = SceneView.lastActiveSceneView;
                if (view != null) { view.in2DMode = true; view.Frame(new Bounds(session.Root.transform.position, new Vector3(1920, 1080, 1)), true); }
            }
            EditorGUILayout.EndHorizontal();
            if (mode != HudLayoutSession.Kind.TeamInfo && mode != HudLayoutSession.Kind.ExistingRect)
                session.StatusCount = EditorGUILayout.IntSlider("預覽異常數量", session.StatusCount, 0, 4);
            if (mode == HudLayoutSession.Kind.SelfStatus)
            {
                session.ShowGuard = EditorGUILayout.Toggle("預覽格檔", session.ShowGuard);
                var draft = (StatusEffectHudSettings)session.Draft;
                draft.selfIconSize = EditorGUILayout.Slider("異常圖示大小", draft.selfIconSize, 8, 64);
                draft.selfBarHeight = EditorGUILayout.Slider("異常量條高度", draft.selfBarHeight, 2, draft.selfIconSize);
                draft.selfRowGap = EditorGUILayout.Slider("異常列間距", draft.selfRowGap, 0, 32);
                draft.guardFontSize = EditorGUILayout.Slider("格檔字級", draft.guardFontSize, 8, 24);
                draft.guardWidthMultiplier = EditorGUILayout.Slider("格檔寬度倍率", draft.guardWidthMultiplier, 1, 1.5f);
                draft.guardBarHeight = EditorGUILayout.Slider("格檔量條高度", draft.guardBarHeight, 2, 24);
                draft.guardStatusGap = EditorGUILayout.Slider("格檔與異常間距", draft.guardStatusGap, 0, 40);
                EditorGUILayout.HelpBox("拖曳整組位置、左右邊緣調整異常區寬度；總高度由可見列數決定。遊戲中仍保留既有施法條避讓，因此擁擠時會暫時上移。", MessageType.None);
            }
            if (mode == HudLayoutSession.Kind.Teammate)
            {
                var draft = (TeammateDisplaySettings)session.Draft;
                draft.nameSize = EditorGUILayout.Slider("隊友名字字級", draft.nameSize, 8, 40);
            }
            if (mode == HudLayoutSession.Kind.Enemy || mode == HudLayoutSession.Kind.Teammate)
                EditorGUILayout.HelpBox("十字代表目標投影位置。上下拖曳調整頭頂距離，Rect Tool 調整寬高；水平仍由目標位置決定。異常列與名稱維持自動排列。", MessageType.None);
            if (mode == HudLayoutSession.Kind.TeamInfo)
                EditorGUILayout.HelpBox("調整整個固定四人框的位置與寬高；內部四列維持相同比例。此工具不拆散已定案的列內自動排列。", MessageType.None);
            if (preview != null)
            {
                var area = GUILayoutUtility.GetAspectRect(16f / 9f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(area, preview, null, ScaleMode.ScaleToFit);
            }
            if (GUILayout.Button("套用版面（可 Undo）"))
            {
                try { session.Apply(); message = session.LastMessage; }
                catch (Exception e) { message = e.GetBaseException().Message; }
            }
            if (GUILayout.Button("關閉草稿（未套用的調整會丟棄）")) CloseSession();
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }
}
