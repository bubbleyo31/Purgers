using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 每個玩家各自持有的本地視覺容器。只消費已確認命中，不執行 Gameplay Raycast。
/// 彈孔使用共享 Quad／材質；FIFO 有限容量，火花播完釋放。
/// </summary>
public sealed class WeaponImpactVisuals : IDisposable
{
    private sealed class Hole
    {
        public GameObject Object;
        public Transform Surface;
        public Vector3 LocalPoint;
        public Vector3 LocalNormal;
        public float Angle;
        public float Offset;
    }

    private sealed class Spark
    {
        public GameObject Object;
        public ParticleSystem[] Systems;
        public float[] EmissionEnds;
        public bool[] Stopped;
        public float Age;
    }

    private readonly WeaponImpactSettings settings;
    private readonly List<Hole> holes = new List<Hole>();
    private readonly List<Spark> sparks = new List<Spark>();
    private readonly Scene scene;
    private GameObject root;
    private Mesh quad;
    public int BulletHoleCount => holes.Count;
    public int SparkCount => sparks.Count;

    public WeaponImpactVisuals(WeaponImpactSettings settings, Scene scene)
    {
        this.settings = settings;
        this.scene = scene;
    }

    public void Present(Vector3 point, Vector3 normal, Transform surface, byte effects, int variant, float angle)
    {
        if (settings == null || !scene.IsValid() || !scene.isLoaded || !ValidPoint(point) ||
            !ValidPoint(normal) || normal.sqrMagnitude < 0.0001f) return;
        normal.Normalize();
        PruneAndTrim();
        // 沒有可辨識的表面時不留下浮空彈孔；火花仍可在權威命中點短暫播放。
        if ((effects & 1) != 0 && surface != null && settings.GetMaterial(variant) != null)
            AddHole(point, normal, surface, variant, angle);
        if ((effects & 2) != 0 && settings.SparkPrefab != null)
            AddSpark(point, normal);
    }

    public static bool ValidPoint(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private void EnsureRoot()
    {
        if (root != null) return;
        root = new GameObject("LocalWeaponImpacts");
        SceneManager.MoveGameObjectToScene(root, scene);
    }

    private void AddHole(Vector3 point, Vector3 normal, Transform surface, int variant, float angle)
    {
        while (holes.Count >= settings.MaxBulletHoles) RemoveHole(0);
        EnsureRoot();
        if (quad == null)
        {
            quad = new Mesh { name = "WeaponImpactQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateBounds();
        }
        var go = new GameObject("BulletHole");
        go.transform.SetParent(root.transform, false);
        go.transform.localScale = Vector3.one * settings.BulletHoleSize;
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = settings.GetMaterial(variant);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var hole = new Hole { Object = go, Surface = surface, LocalPoint = surface.InverseTransformPoint(point),
            // 法線用逆轉置的反向運算，支援非均勻縮放表面的方向還原。
            LocalNormal = surface.localToWorldMatrix.transpose.MultiplyVector(normal).normalized,
            Angle = angle, Offset = settings.SurfaceOffset };
        holes.Add(hole);
        UpdateHole(hole);
    }

    private static void UpdateHole(Hole hole)
    {
        Vector3 normal = hole.Surface.worldToLocalMatrix.transpose.MultiplyVector(hole.LocalNormal).normalized;
        Vector3 point = hole.Surface.TransformPoint(hole.LocalPoint) + normal * hole.Offset;
        hole.Object.transform.SetPositionAndRotation(point,
            Quaternion.LookRotation(normal) * Quaternion.Euler(0, 0, hole.Angle));
    }

    private void AddSpark(Vector3 point, Vector3 normal)
    {
        var prefab = settings.SparkPrefab;
        if (prefab.GetComponentInChildren<NetworkObject>(true) != null ||
            prefab.GetComponentInChildren<ParticleSystem>(true) == null) return;
        while (sparks.Count >= settings.MaxSparks) RemoveSpark(0);
        EnsureRoot();
        var container = new GameObject("ImpactSpark");
        container.SetActive(false);
        container.transform.SetParent(root.transform, false);
        container.transform.SetPositionAndRotation(point + normal * settings.SurfaceOffset,
            Quaternion.LookRotation(normal) * Quaternion.Euler(settings.SparkRotationOffset));
        container.transform.localScale = Vector3.one * settings.SparkScale;
        var instance = UnityEngine.Object.Instantiate(prefab, container.transform, false);
        // 特效是世界呈現，不繼承來源 Prefab 的 ViewModel／Enemy Layer。
        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
        var entry = new Spark { Object = container, Systems = systems,
            EmissionEnds = new float[systems.Length], Stopped = new bool[systems.Length] };
        for (int i = 0; i < systems.Length; i++)
        {
            var ps = systems[i];
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.None;
            main.useUnscaledTime = false;
            entry.EmissionEnds[i] = LastEmissionTime(ps);
        }
        container.SetActive(true);
        foreach (var ps in systems)
            if (ps.gameObject.activeInHierarchy) ps.Play(false);
        sparks.Add(entry);
    }

    /// <summary>一次 Burst 素材不必空等 duration=10；最後一次排程發射後停止發射，保留存活粒子。</summary>
    private static float LastEmissionTime(ParticleSystem ps)
    {
        var main = ps.main;
        var emission = ps.emission;
        float last = 0f;
        if (emission.enabled)
        {
            if (emission.rateOverTime.mode != ParticleSystemCurveMode.Constant ||
                emission.rateOverDistance.mode != ParticleSystemCurveMode.Constant ||
                emission.rateOverTime.constant > 0f || emission.rateOverDistance.constant > 0f)
                last = main.duration;
            for (int i = 0; i < emission.burstCount; i++)
            {
                var burst = emission.GetBurst(i);
                last = Mathf.Max(last, burst.cycleCount == 0 ? main.duration :
                    burst.time + Mathf.Max(0, burst.cycleCount - 1) * burst.repeatInterval);
            }
        }
        return main.startDelay.constantMax + last + 0.05f;
    }

    public void Tick(float deltaSeconds)
    {
        if (settings == null) { Dispose(); return; }
        PruneAndTrim();
        foreach (var hole in holes) UpdateHole(hole);
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var spark = sparks[i];
            spark.Age += Mathf.Max(0f, deltaSeconds);
            bool alive = false;
            for (int j = 0; j < spark.Systems.Length; j++)
            {
                var ps = spark.Systems[j];
                if (ps == null) continue;
                if (!spark.Stopped[j] && spark.Age >= spark.EmissionEnds[j])
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    spark.Stopped[j] = true;
                }
                alive |= ps.IsAlive(true);
            }
            if (!alive || spark.Age >= settings.SparkTimeout) RemoveSpark(i);
        }
    }

    private void PruneAndTrim()
    {
        for (int i = holes.Count - 1; i >= 0; i--)
            if (holes[i].Object == null || holes[i].Surface == null ||
                !holes[i].Surface.gameObject.activeInHierarchy) RemoveHole(i);
        for (int i = sparks.Count - 1; i >= 0; i--)
            if (sparks[i].Object == null) RemoveSpark(i);
        while (holes.Count > settings.MaxBulletHoles) RemoveHole(0);
        while (sparks.Count > settings.MaxSparks) RemoveSpark(0);
    }

    private void RemoveHole(int index) { DestroyOwned(holes[index].Object); holes.RemoveAt(index); }
    private void RemoveSpark(int index) { DestroyOwned(sparks[index].Object); sparks.RemoveAt(index); }

    public void Dispose()
    {
        for (int i = holes.Count - 1; i >= 0; i--) RemoveHole(i);
        for (int i = sparks.Count - 1; i >= 0; i--) RemoveSpark(i);
        DestroyOwned(root);
        DestroyOwned(quad);
        root = null;
        quad = null;
    }

    private static void DestroyOwned(UnityEngine.Object item)
    {
        if (item == null) return;
        if (item is GameObject go) go.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(item);
        else UnityEngine.Object.DestroyImmediate(item);
    }
}
