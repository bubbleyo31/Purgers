using System;
using System.IO;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>看門菇的明確資產接線入口。只處理指定 Prefab／衍生動畫，不重匯入原始 FBX、不保存 Scene。</summary>
public static class EnemySuicideModelSetup
{
    public const string ModelPath = "Assets/_Project_Assets/Models/Enemy/Lingzhi/看門菇/看門菇-動畫1.fbx";
    public const string Folder = "Assets/_Project_Assets/Animations/Enemy/Suicide";
    public const string ClipPath = Folder + "/Pounce_InPlace.anim";
    public const string ControllerPath = Folder + "/Suicide.controller";
    private const string BodyPath = "物体/body";

    public static AnimationClip SourceClip(string suffix) =>
        AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == "物体|" + suffix);

    // 使用 Hermite 曲線分割，保留離地之前的原始曲線；最後一個切點後固定高度。
    public static AnimationCurve HoldAfter(AnimationCurve source, float time, float duration)
    {
        var keys = source.keys.Where(k => k.time < time).ToList();
        float epsilon = .00001f;
        float slope = (source.Evaluate(time) - source.Evaluate(Mathf.Max(0, time - epsilon))) / epsilon;
        keys.Add(new Keyframe(time, source.Evaluate(time), slope, 0f));
        if (time < duration) keys.Add(new Keyframe(duration, source.Evaluate(time), 0f, 0f));
        return new AnimationCurve(keys.ToArray()) {preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever};
    }

    public static AnimationClip RebuildPounce(float takeoffFrame, float fps)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        if (fps <= 0 || float.IsNaN(fps) || float.IsInfinity(fps)) throw new ArgumentOutOfRangeException(nameof(fps));
        EnsureFolder(Folder);
        var original = SourceClip("PounceStart");
        float takeoffTime = takeoffFrame / fps;
        if (takeoffTime < 0 || takeoffTime > original.length) throw new ArgumentOutOfRangeException(nameof(takeoffFrame));
        var copy = Object.Instantiate(original);
        copy.name = "Pounce_InPlace";
        copy.hideFlags = HideFlags.None;
        copy.wrapMode = WrapMode.ClampForever;
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        AnimationUtility.SetAnimationEvents(copy, new AnimationEvent[0]);
        var bindings = AnimationUtility.GetCurveBindings(copy);
        var forward = bindings.Single(b => b.path == BodyPath && b.propertyName == "m_LocalPosition.y");
        var up = bindings.Single(b => b.path == BodyPath && b.propertyName == "m_LocalPosition.z");
        float start = AnimationUtility.GetEditorCurve(original, forward).Evaluate(0f);
        AnimationUtility.SetEditorCurve(copy, forward, AnimationCurve.Constant(0, original.length, start));
        AnimationUtility.SetEditorCurve(copy, up, HoldAfter(AnimationUtility.GetEditorCurve(original, up), takeoffTime, original.length));
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (existing == null) AssetDatabase.CreateAsset(copy, ClipPath);
        else { EditorUtility.CopySerialized(copy, existing); Object.DestroyImmediate(copy); copy = existing; }
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssetIfDirty(copy);
        return copy;
    }

    public static void InstallConfirmedModel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        var root = PrefabUtility.LoadPrefabContents(EnemySuicideSetup.PrefabPath);
        try
        {
            if (root.GetComponentInChildren<Animator>(true) != null)
                throw new InvalidOperationException("已存在 Animator；請勿重複套用首次替換。只需改 Inspector 或重建原地副本。");
            var clip = RebuildPounce(8f, 24f);
            var controller = CreateController(clip);
            var visual = root.transform.Find("VisualRoot");
            if (visual == null) throw new InvalidOperationException("找不到原 VisualRoot。");
            // 保留物件身分與死亡停用引用，移除灰盒網格。
            Object.DestroyImmediate(visual.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(visual.GetComponent<MeshFilter>());
            visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity; visual.localScale = Vector3.one;
            foreach (Transform child in visual)
                if (child.GetComponent<MeshRenderer>() != null) child.gameObject.SetActive(false); // 保留舊方塊眼睛供回溯，模型替換後停用。
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), visual);
            model.name = "LingzhiModel";
            float scale = 2.1f / 2.113f;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = Vector3.up * (.044f * scale);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
            var animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var driver = root.AddComponent<EnemyPresentationAnimatorDriver>();
            Set(driver, "animator", animator);
            Set(driver, "pounceNormalizedTimeName", "PounceNormalizedTime");
            // 目前人工 ChaseSpeed=20；獨立動畫基準避免 24 FPS 跑步被播放成四倍速。
            Set(driver, "chaseAnimationReferenceMoveSpeed", new SerializedObject(root.GetComponent<EnemyGroundChaseMotor>()).FindProperty("chaseSpeed").floatValue);
            var attack = root.GetComponent<EnemySuicideAttack>();
            Set(attack, "useAnimationFrameTiming", true); Set(attack, "takeoffFrame", 8f);
            Set(attack, "takeoffTiming.ClipDuration", clip.length);
            Set(attack, "takeoffTiming.FramesPerSecond", 24f); Set(attack, "takeoffTiming.PlaybackSpeed", 1f);
            Set(attack, "flightSeconds", .6f);
            Set(attack, "explosionCenterOffset", Vector3.up * 1.05f);
            var capsule = root.GetComponent<CapsuleCollider>();
            capsule.height = 2.1f; capsule.radius = .65f; capsule.center = Vector3.up * 1.05f;
            foreach (var motor in new Component[] {root.GetComponent<EnemyGroundPatrolNavigator>(), root.GetComponent<EnemyGroundChaseMotor>()})
            { Set(motor, "bodyRadius", capsule.radius); Set(motor, "bodyHeight", capsule.height); }
            Set(root.GetComponent<EnemyPerceptionController>(), "fallbackEyeHeight", 1.65f);
            // Hitbox 留在中立尺度的 VisualRoot，完整包覆 Idle 身形（含後方尾部）。
            var hitbox = visual.GetComponent<Hitbox>();
            hitbox.BoxExtents = new Vector3(.70f, 1.05f, .96f);
            hitbox.Offset = new Vector3(0f, 1.05f, -.165f);
            var hitRoot = root.GetComponent<HitboxRoot>();
            hitRoot.Offset = hitbox.Offset; hitRoot.BroadRadius = hitbox.BoxExtents.magnitude + .05f;
            hitRoot.Hitboxes = new[] {hitbox};
            var anchor = root.transform.Find("EnemyHealthBarAnchor");
            if (anchor == null) {anchor = new GameObject("EnemyHealthBarAnchor").transform; anchor.SetParent(root.transform, false);}
            anchor.localPosition = Vector3.up * 2.25f;
            var bar = root.GetComponent<EnemyHealthBarTarget>();
            Set(bar, "headAnchor", anchor); Set(bar, "bodyCenter", capsule.center);
            Set(bar, "bodySize", new Vector3(1.4f, 2.1f, 1.3f));
            if (PrefabUtility.SaveAsPrefabAsset(root, EnemySuicideSetup.PrefabPath) == null)
                throw new InvalidOperationException("Enemy_Suicide 儲存失敗。");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static AnimatorController CreateController(AnimationClip pounce)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            throw new InvalidOperationException("Controller 已存在，請保留人工設定，不重建。");
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var name in new[] {"IsAlive", "IsAlerted", "IsAnnouncing", "IsUsingCombatAction"})
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        foreach (var name in new[] {"MoveSpeed", "PatrolPlaybackSpeed", "ChasePlaybackSpeed", "PounceNormalizedTime"})
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        foreach (var name in new[] {"BrainState", "CombatOptionId", "CombatActionPhase"})
            controller.AddParameter(name, AnimatorControllerParameterType.Int);
        var sm = controller.layers[0].stateMachine;
        var idle = sm.AddState("Idle", new Vector3(220, 20)); idle.motion = SourceClip("idle");
        var walk = sm.AddState("Walk", new Vector3(220, 100)); walk.motion = SourceClip("walk");
        walk.speedParameterActive = true; walk.speedParameter = "PatrolPlaybackSpeed";
        var run = sm.AddState("Run", new Vector3(220, 180)); run.motion = SourceClip("run");
        run.speedParameterActive = true; run.speedParameter = "ChasePlaybackSpeed";
        var jump = sm.AddState("Pounce", new Vector3(480, 100)); jump.motion = pounce;
        jump.timeParameterActive = true; jump.timeParameter = "PounceNormalizedTime";
        sm.defaultState = idle;
        var t = Transition(sm, jump, 0f);
        t.AddCondition(AnimatorConditionMode.If, 0, "IsUsingCombatAction");
        t.AddCondition(AnimatorConditionMode.Equals, (int)EnemyCombatOptionId.SuicidePounce, "CombatOptionId");
        t = Transition(sm, idle, .08f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsUsingCombatAction");
        t.AddCondition(AnimatorConditionMode.Less, .05f, "MoveSpeed");
        t = Transition(sm, walk, .08f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsUsingCombatAction");
        t.AddCondition(AnimatorConditionMode.Greater, .049f, "MoveSpeed"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAlerted");
        t = Transition(sm, run, .08f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsUsingCombatAction");
        t.AddCondition(AnimatorConditionMode.Greater, .049f, "MoveSpeed"); t.AddCondition(AnimatorConditionMode.If, 0, "IsAlerted");
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) { EditorUtility.SetDirty(asset); Undo.ClearUndo(asset); }
        EditorUtility.SetDirty(sm); Undo.ClearUndo(sm);
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }
    private static AnimatorStateTransition Transition(AnimatorStateMachine sm, AnimatorState state, float duration)
    {
        var t = sm.AddAnyStateTransition(state); t.hasExitTime = false; t.hasFixedDuration = true;
        t.duration = duration; t.canTransitionToSelf = false; return t;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    private static void Set(Object obj, string name, object value)
    {
        var so = new SerializedObject(obj); var p = so.FindProperty(name);
        if (p == null) throw new InvalidOperationException(obj.name + " 缺少欄位 " + name);
        if (value is bool b) p.boolValue = b;
        else if (value is float f) p.floatValue = f;
        else if (value is string s) p.stringValue = s;
        else if (value is Vector3 v) p.vector3Value = v;
        else if (value is Object o) p.objectReferenceValue = o;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

/// <summary>離地幀變更後可明確重建衍生曲線；不隱性改寫動畫或原 FBX。</summary>
[CustomEditor(typeof(EnemySuicideAttack))]
public sealed class EnemySuicideAttackEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var timing = serializedObject.FindProperty("takeoffTiming");
        float fps = timing.FindPropertyRelative("FramesPerSecond").floatValue;
        float speed = timing.FindPropertyRelative("PlaybackSpeed").floatValue;
        float frame = serializedObject.FindProperty("takeoffFrame").floatValue;
        if (fps > 0 && speed > 0)
            EditorGUILayout.HelpBox("離地：" + (frame / fps / speed).ToString("0.000") + " 秒。幀數採 0 起算。空中尾幀保持到既有飛行期限。\n改離地幀／FPS 後，按下方按鈕更新原地副本的垂直補償；僅改倍率不用重建。", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("重建看門菇原地撲擊副本（保留原 FBX）"))
                EnemySuicideModelSetup.RebuildPounce(frame, fps);
    }
}
