using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

/// <summary>Bounded, opt-in live monitoring. Does not change global Profiler recording settings.</summary>
public static class MapPhysicsMonitor
{
    public static bool Running { get; private set; }
    public static string Status { get; private set; } = "尚未取樣";
    public static Capture Last { get; private set; }

    [Serializable] public sealed class Counter
    {
        public string name, unit;
        public bool available;
        public int samples;
        public double mean, p95, max;
    }

    [Serializable] public sealed class Capture
    {
        public string utc, scope, limitation;
        public double seconds;
        public int framesElapsed, runningRunners, players, enabledMapMeshes, enabledMapBoxes;
        public Counter[] counters;
    }

    public static async void Begin(double seconds = 10)
    {
        if (Running || MapMeshColliderBenchmark.Running || !EditorApplication.isPlaying)
        { Status = "需要沒有 A/B 量測進行中的 Play Mode"; return; }
        if (seconds < 1 || seconds > 60) { Status = "取樣時間需為 1–60 秒"; return; }
        Running = true; Status = "取樣中"; Last = null;
        var recorders = new List<ProfilerRecorder>();
        var counters = new List<Counter>();
        double start = EditorApplication.timeSinceStartup;
        int firstFrame = Time.frameCount;
        try
        {
            var names = new[] { "Main Thread", "Physics.Simulate", "Physics.FetchResults",
                "Physics.Raycast", "Physics.RaycastNonAlloc", "Physics.SphereCast", "Physics.SphereCastNonAlloc",
                "Physics.CapsuleCast", "Physics.CapsuleCastNonAlloc", "Physics.OverlapCapsuleNonAlloc",
                "Physics Queries", "Active Dynamic Bodies", "Static Colliders",
                "Mesh.Bake PhysX CollisionData", "Mesh.Bake Scaled Mesh PhysX CollisionData" };
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var available = handles.Select(ProfilerRecorderHandle.GetDescription).ToArray();
            foreach (string name in names)
            {
                int index = Array.FindIndex(available, d => d.Name == name);
                if (index < 0)
                {
                    recorders.Add(default); counters.Add(new Counter { name = name, unit = "Unavailable" });
                    continue;
                }
                var match = available[index];
                var recorder = ProfilerRecorder.StartNew(match.Category, name, 8192);
                recorders.Add(recorder);
                counters.Add(new Counter { name = name, available = recorder.Valid,
                    unit = match.UnitType.ToString() == "TimeNanoseconds" ? "milliseconds" : match.UnitType.ToString() });
            }
            while (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - start < seconds)
                await Task.Delay(100);
            if (!EditorApplication.isPlaying) { Status = "播放已停止；本次取樣取消"; return; }
            for (int i = 0; i < recorders.Count; i++)
            {
                var recorder = recorders[i];
                if (!recorder.Valid) continue;
                recorder.Stop();
                if (recorder.Count == 0) continue;
                var values = recorder.ToArray().Select(s => s.Value / (counters[i].unit == "milliseconds" ? 1000000d : 1d)).OrderBy(v => v).ToArray();
                counters[i].samples = values.Length;
                counters[i].mean = values.Average();
                counters[i].p95 = values[Math.Max(0, (int)Math.Ceiling(values.Length * .95) - 1)];
                counters[i].max = values.Last();
            }
            var root = MapMeshColliderOptimizer.FindTarget();
            Last = new Capture {
                utc = DateTime.UtcNow.ToString("O"), scope = "Live Editor Play Mode; all physics scenes and collider types",
                limitation = "不是 MeshCollider 獨占時間；Main Thread 含等待與 Editor 影響。available=false 或 samples=0 是不可量測，不是零成本。沒有玩家或 Runner 的樣本不能代表戰鬥。",
                seconds = EditorApplication.timeSinceStartup - start, framesElapsed = Time.frameCount - firstFrame,
                runningRunners = Fusion.NetworkRunner.Instances.Count(r => r.IsRunning),
                players = UnityEngine.Object.FindObjectsOfType<Player>().Length,
                enabledMapMeshes = root.GetComponentsInChildren<MeshCollider>().Count(c => c.enabled),
                enabledMapBoxes = root.GetComponentsInChildren<BoxCollider>().Count(c => c.enabled),
                counters = counters.ToArray()
            };
            Directory.CreateDirectory(MapMeshColliderBenchmark.Evidence);
            File.WriteAllText(MapMeshColliderBenchmark.Evidence + "/live-" + DateTime.UtcNow.ToString("HHmmss") + ".json", JsonUtility.ToJson(Last, true));
            Status = "完成：" + Last.framesElapsed + " 幀；Runner " + Last.runningRunners + "，玩家 " + Last.players;
        }
        catch (Exception e) { Status = "失敗：" + e.Message; Debug.LogException(e); }
        finally { foreach (var recorder in recorders) if (recorder.Valid) recorder.Dispose(); Running = false; }
    }
}
