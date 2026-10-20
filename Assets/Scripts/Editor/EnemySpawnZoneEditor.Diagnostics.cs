using System.Collections.Generic;
using System.Linq;
using Purgers.Enemy.Encounter;
using UnityEditor;
using UnityEngine;

public sealed partial class EnemySpawnZoneEditor
{
    private double nextDiagnosticRefresh;
    private bool showConfiguration = true;
    private bool showHistory = true;
    private readonly Dictionary<int, List<EnemySpawnZoneDiagnostics.Notice>> diagnosticCache =
        new Dictionary<int, List<EnemySpawnZoneDiagnostics.Notice>>();
    private readonly Dictionary<int, string> spaceSnapshots = new Dictionary<int, string>();

    private void OnEnable() => EditorApplication.update += RefreshDiagnostics;
    private void OnDisable() => EditorApplication.update -= RefreshDiagnostics;

    private void RefreshDiagnostics()
    {
        if (EditorApplication.timeSinceStartup < nextDiagnosticRefresh) return;
        nextDiagnosticRefresh = EditorApplication.timeSinceStartup + 0.75;
        diagnosticCache.Clear();
        Repaint();
    }

    private void DrawDiagnostics()
    {
        EditorGUILayout.LabelField("生成合法性診斷", EditorStyles.boldLabel);
        foreach (EnemySpawnZone zone in targets)
        {
            if (zone == null) continue;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (targets.Length > 1) EditorGUILayout.LabelField(zone.name, EditorStyles.boldLabel);
                if (!diagnosticCache.TryGetValue(zone.GetInstanceID(), out var notices))
                    diagnosticCache[zone.GetInstanceID()] = notices = EnemySpawnZoneDiagnostics.Inspect(zone);
                bool errors = notices.Any(item => item.Type == MessageType.Error);
                bool warnings = notices.Any(item => item.Type == MessageType.Warning);
                Color old = GUI.color;
                GUI.color = errors ? new Color(1f, 0.55f, 0.5f) : new Color(1f, 0.85f, 0.4f);
                EditorGUILayout.LabelField(errors ? "● 配置有錯誤：請展開下方原因"
                    : warnings ? "● 配置有待處理項目"
                    : "● 配置初檢通過；實際生成條件待判定", EditorStyles.boldLabel);
                GUI.color = old;

                if (Application.isPlaying)
                {
                    var directors = EnemySpawnZoneDiagnostics.FindDirectors(zone);
                    if (directors.Length == 1)
                    {
                        string state = directors[0].DescribeInspectorState(zone, out int severity);
                        EditorGUILayout.HelpBox("目前狀態\n" + state, severity == 2 ? MessageType.Error : MessageType.Warning);
                    }
                    else EditorGUILayout.HelpBox("目前狀態：找不到唯一的同場景 Director。請選取 Host 本輪生成的 Chunk／Zone，Prefab 資產不會有執行結果。", MessageType.Warning);
                }
                else EditorGUILayout.HelpBox("編輯模式：檢查配置與預烘焙資料。玩家距離、遮擋、數量、輪替鎖定及地面可達性由 Play 中的 Host 判定。", MessageType.Info);

                showConfiguration = EditorGUILayout.Foldout(showConfiguration, "配置問題與處理方向", true);
                if (showConfiguration)
                    foreach (var item in notices) EditorGUILayout.HelpBox(item.Text, item.Type);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("重新檢查配置")) diagnosticCache.Remove(zone.GetInstanceID());
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                        if (GUILayout.Button("檢查飛行空間"))
                            spaceSnapshots[zone.GetInstanceID()] =
                                System.DateTime.Now.ToString("HH:mm:ss") + " 手動快照（移動物件／修改設定後請重按）\n" +
                                EnemySpawnZoneDiagnostics.CheckFlyingSpace(zone);
                }
                if (spaceSnapshots.TryGetValue(zone.GetInstanceID(), out string space))
                    EditorGUILayout.HelpBox(space, MessageType.Info);
                if (Application.isPlaying)
                {
                    showHistory = EditorGUILayout.Foldout(showHistory, $"本次 Play 最近結果（{zone.InspectorEvents.Count}）", true);
                    if (showHistory)
                    {
                        if (zone.InspectorEvents.Count == 0)
                            EditorGUILayout.HelpBox("尚無生成嘗試結果。檢查上方目前狀態，再從有效方向穿越 Gate。", MessageType.Info);
                        for (int i = zone.InspectorEvents.Count - 1; i >= 0; i--)
                            EditorGUILayout.HelpBox(zone.InspectorEvents[i], MessageType.None);
                        EditorGUILayout.LabelField("歷史結果不代表目前仍然成立；最多保留最近 8 筆。", EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }
        EditorGUILayout.Space();
    }
}
