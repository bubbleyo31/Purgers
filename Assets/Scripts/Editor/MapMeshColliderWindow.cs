using System;
using UnityEditor;
using UnityEngine;

public sealed class MapMeshColliderWindow : EditorWindow
{
    private MapMeshColliderOptimizer.Plan plan;
    private string message = "先分析 Game / Map(Z:-263)。只替換可證明完全吻合的長方體零件。";
    private Vector2 scroll;
    private bool verified;

    [MenuItem("Tools/Purgers/Physics/Map MeshCollider")]
    private static void Open() => GetWindow<MapMeshColliderWindow>("Map 碰撞優化");
    private void OnDisable() { plan?.Dispose(); plan = null; }
    private void OnInspectorUpdate() { if (MapMeshColliderBenchmark.Running || MapPhysicsMonitor.Running) Repaint(); }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Game / " + MapMeshColliderOptimizer.TargetName, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("只辨識真正封閉的長方體：八角點、六面、十二外向三角形。保留原物件位置、尺寸及階層，不合併建築，不使用 Convex。凹形、門框、複雜模型及不安全變換維持 MeshCollider。", MessageType.Info);
        using (new EditorGUI.DisabledScope(MapMeshColliderBenchmark.Running || MapPhysicsMonitor.Running))
        {
            if (GUILayout.Button("分析精確 Box 候選（不套用）"))
                Try(() => { plan?.Dispose(); plan = MapMeshColliderOptimizer.Analyze(MapMeshColliderOptimizer.FindTarget());
                    verified = MapMeshColliderOptimizer.HasMatchingValidation(plan);
                    message = verified ? "此候選有相同來源的背景驗證；仍需自然玩法驗收。" : "候選已產生；請先執行 A/B 量測，再退出 Play Mode 並重新分析。"; });
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Play Mode：隔離 A/B 量測並輸出 JSON")) Try(() => MapMeshColliderBenchmark.Begin());
                if (GUILayout.Button("Play Mode：監測目前遊戲 10 秒（Profiler）")) MapPhysicsMonitor.Begin();
            }
        }
        EditorGUILayout.HelpBox("需先進入 Play Mode 才能量測。量測不啟動網路、不改正式場景；每批包含 256 Raycast、64 SphereCast、64 CapsuleCast，另測 32 個動態球體。", MessageType.None);
        EditorGUILayout.LabelField("量測狀態", MapMeshColliderBenchmark.Status);
        EditorGUILayout.LabelField("即時監測", MapPhysicsMonitor.Status);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (plan != null)
        {
            EditorGUILayout.LabelField("啟用 MeshCollider", plan.Baseline.Length + " → " + plan.CandidateMeshCount);
            EditorGUILayout.LabelField("改用 BoxCollider", plan.Candidates.Count.ToString());
            foreach (var kv in plan.Excluded) EditorGUILayout.LabelField("保留：" + kv.Key, kv.Value.ToString());
            EditorGUILayout.HelpBox("套用會改變 Collider 類型與身分，保留原 MeshCollider 但停用。支援本次 Undo、不自動儲存場景。來源幾何改變時會拒絕舊候選。先審閱差異報告並完成行走、鉤索與射擊驗收。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || MapMeshColliderBenchmark.Running || plan.Applied || plan.Candidates.Count == 0 || !verified))
                if (GUILayout.Button("確認後套用精確 Box 候選（可 Undo）") &&
                    EditorUtility.DisplayDialog("套用碰撞候選", "是否已審閱碰撞與效能差異，接受 Collider 類型改變？", "套用", "取消"))
                    Try(() => { MapMeshColliderOptimizer.Apply(plan); message = "已套用；場景尚未儲存。Undo 可還原。"; });
        }
        EditorGUILayout.HelpBox(message, MessageType.None);
        EditorGUILayout.EndScrollView();
    }
    private void Try(Action action)
    {
        try { action(); }
        catch (Exception e) { message = e.Message; Debug.LogException(e); }
    }
}
