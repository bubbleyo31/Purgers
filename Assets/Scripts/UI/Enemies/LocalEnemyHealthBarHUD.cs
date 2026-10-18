using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>每端 Screen Space HUD；只呈現同 Runner 已 Spawn 敵人的快照，所有遮擋與位置均為本機判斷。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class LocalEnemyHealthBarHUD : MonoBehaviour
{
    [Header("顯示引用")]
    [SerializeField, Tooltip("共用百分比分格與可見性設定；需要指定。")]
    private EnemyHealthBarSettings settings;
    [SerializeField, Tooltip("所屬 Runner；留空只接受唯一有效本機 Runner，Multi Peer 須同時指定 Runner 與世界相機。")]
    private NetworkRunner runner;
    [SerializeField, Tooltip("遊戲世界相機，不能指定手部相機；單 Runner 留空使用 Camera.main。")]
    private Camera worldCamera;
    private bool explicitRunner, explicitCamera;
    private Canvas canvas;
    private RectTransform root;
    private readonly EnemyHealthBarVisibility visibility = new EnemyHealthBarVisibility();
    private readonly List<Candidate> candidates = new List<Candidate>();
    private readonly Dictionary<EnemyHealthBarTarget, View> views = new Dictionary<EnemyHealthBarTarget, View>();
    private readonly List<EnemyHealthBarTarget> removed = new List<EnemyHealthBarTarget>();
    private readonly Stack<View> pool = new Stack<View>();
    private readonly StatusEffectSnapshot[] statusBuffer = new StatusEffectSnapshot[StatusEffectSources.Capacity];
    private struct Candidate { public EnemyHealthBarTarget Target; public float Distance; public int StatusCount; }
    private sealed class View
    {
        public EnemyHealthBarGraphic Graphic;
        public StatusEffectListView Status;
        public bool Seen, Fallback;
        public Vector2 Screen;
    }
    public EnemyHealthBarSettings Settings => settings;
    public int VisibleBarCount { get; private set; }

    private void Awake()
    {
        root = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        explicitRunner = runner != null; explicitCamera = worldCamera != null;
    }

    public void Configure(EnemyHealthBarSettings value, NetworkRunner owner, Camera camera)
    {
        Clear(); settings = value; runner = owner; worldCamera = camera;
        explicitRunner = owner != null; explicitCamera = camera != null;
    }

    public bool TryGetPresentation(EnemyHealthBarTarget target, out Vector2 screen, out bool fallback, out float ratio)
    {
        screen = default; fallback = false; ratio = 0;
        if (!views.TryGetValue(target, out var view) || !view.Seen) return false;
        screen = view.Screen; fallback = view.Fallback; ratio = view.Graphic.DisplayedRatio;
        return true;
    }

    private void Resolve()
    {
        if (explicitRunner && explicitCamera) return;
        NetworkRunner unique = null; int count = 0;
        foreach (var item in NetworkRunner.Instances)
            if (item.IsRunning && item.LocalPlayer.IsRealPlayer) { unique = item; count++; }
        if (!explicitRunner)
        {
            var next = count == 1 ? unique : null;
            if (next != runner) { Clear(); runner = next; }
        }
        if (!explicitCamera) worldCamera = count == 1 ? Camera.main : null;
    }

    private Vector2 Local(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local);
        return local - root.rect.center;
    }

    private View Acquire()
    {
        if (pool.Count > 0) return pool.Pop();
        var obj = new GameObject("Enemy health percentage", typeof(RectTransform), typeof(EnemyHealthBarGraphic));
        var rect = (RectTransform)obj.transform; rect.SetParent(root, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        return new View { Graphic = obj.GetComponent<EnemyHealthBarGraphic>(), Status = StatusEffectListView.Create(rect) };
    }

    private void LateUpdate()
    {
        Resolve(); VisibleBarCount = 0;
        foreach (var view in views.Values) view.Seen = false;
        if (settings == null || canvas == null || canvas.renderMode == RenderMode.WorldSpace ||
            (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null) ||
            worldCamera == null || !worldCamera.isActiveAndEnabled || runner == null ||
            !runner.IsRunning || runner.IsSceneManagerBusy ||
            (!explicitCamera && !Purgers.GameFlow.Transition.LocalSceneTransition.TryGetReadyLocalPlayer(runner, out _)))
        { RecycleUnseen(); return; }

        var physics = runner.GetPhysicsScene();
        Transform observer = runner.TryGetPlayerObject(runner.LocalPlayer, out var player) && player != null ? player.transform : null;
        Vector3 cameraPosition = worldCamera.transform.position;
        candidates.Clear();
        var statusSettings = StatusEffectHudSettings.Default;
        foreach (var target in EnemyHealthBarTarget.Active)
        {
            if (target == null) continue;
            int statusCount = target.ReadStatusEffects(runner, statusBuffer);
            if (!target.IsReadyFor(runner, statusCount > 0)) continue;
            float distance = (target.transform.TransformPoint(target.LocalBody.center) - cameraPosition).sqrMagnitude;
            if (settings.maxDistance > 0 && distance > settings.maxDistance * settings.maxDistance) continue;
            candidates.Add(new Candidate { Target = target, Distance = distance, StatusCount = statusCount });
        }
        candidates.Sort(CompareCandidates);
        float pixels = worldCamera.pixelHeight / 1080f;
        Vector2 size = settings.Size * pixels;
        Rect safe = EnemyHealthBarRules.SafeArea(worldCamera.pixelRect, size, Mathf.Max(0, settings.edgeInset) * pixels);
        if (safe.width <= 0 || safe.height <= 0) { RecycleUnseen(); return; }
        Vector2 units = new Vector2(
            Mathf.Abs((Local(worldCamera.pixelRect.center + Vector2.right) - Local(worldCamera.pixelRect.center)).x),
            Mathf.Abs((Local(worldCamera.pixelRect.center + Vector2.up) - Local(worldCamera.pixelRect.center)).y));
        int checks = 0;
        foreach (var candidate in candidates)
        {
            if (VisibleBarCount >= Mathf.Clamp(settings.maxVisibleBars, 1, 128) ||
                checks >= Mathf.Clamp(settings.maxVisibilityChecks, 1, 256)) break;
            var target = candidate.Target;
            if (!visibility.TryProject(worldCamera, target.transform, target.LocalBody, out var projected)) continue;
            views.TryGetValue(target, out var view);
            Vector3 head = worldCamera.WorldToScreenPoint(target.HeadPosition);
            if (head.z < worldCamera.nearClipPlane || head.z > worldCamera.farClipPlane) head.z = -1;
            // 一般敵人錨點出框時不花遮擋預算；大型敵人仍需檢查畫面內身體。
            if (!EnemyHealthBarRules.TryPosition(worldCamera.pixelRect, head, projected, target.IsLarge,
                view != null && view.Fallback, size, settings.edgeInset * pixels, settings.hysteresis * pixels,
                settings.padding * pixels, out _, out _)) continue;
            checks++;
            if (!visibility.TryVisibleRegion(worldCamera, target.transform, target.LocalBody, projected,
                physics, settings.obstructionMask, observer, out var visible)) continue;
            if (!EnemyHealthBarRules.TryPosition(worldCamera.pixelRect, head, visible, target.IsLarge,
                view != null && view.Fallback, size, settings.edgeInset * pixels, settings.hysteresis * pixels,
                settings.padding * pixels, out bool fallback, out var point)) continue;
            bool fresh = view == null;
            if (fresh) { view = Acquire(); views.Add(target, view); }
            float blend = settings.followSpeed <= 0 ? 1 : 1 - Mathf.Exp(-settings.followSpeed * Time.unscaledDeltaTime);
            Rect statusSafe = safe;
            float extra = statusSettings != null ? statusSettings.Height(candidate.StatusCount, false) * pixels : 0f;
            statusSafe.yMax = Mathf.Max(statusSafe.yMin, statusSafe.yMax - extra);
            view.Screen = EnemyHealthBarRules.Clamp(statusSafe, fresh ? point : Vector2.Lerp(view.Screen, point, blend));
            view.Seen = true; view.Fallback = fallback;
            view.Graphic.gameObject.SetActive(true);
            var rect = view.Graphic.rectTransform;
            rect.anchoredPosition = Local(view.Screen);
            rect.sizeDelta = settings.Size;
            rect.localScale = new Vector3(pixels * units.x, pixels * units.y, 1);
            view.Graphic.SetSnapshot(target.HealthRatio, settings);
            int count = target.ReadStatusEffects(runner, statusBuffer);
            view.Status.Present(statusBuffer, count, statusSettings, settings.Size.x, false);
            VisibleBarCount++;
        }
        RecycleUnseen();
    }

    private static int CompareCandidates(Candidate a, Candidate b)
    {
        int order = a.Distance.CompareTo(b.Distance);
        return order != 0 ? order : a.Target.GetInstanceID().CompareTo(b.Target.GetInstanceID());
    }

    private void RecycleUnseen()
    {
        removed.Clear();
        foreach (var pair in views) if (!pair.Value.Seen) removed.Add(pair.Key);
        foreach (var target in removed)
        {
            var view = views[target]; view.Graphic.gameObject.SetActive(false);
            views.Remove(target); pool.Push(view);
        }
        int capacity = settings == null ? 0 : Mathf.Clamp(settings.maxVisibleBars, 1, 128);
        while (pool.Count + views.Count > capacity && pool.Count > 0) DestroyView(pool.Pop());
    }

    private static void DestroyView(View view)
    {
        if (view.Graphic == null) return;
        if (Application.isPlaying) Destroy(view.Graphic.gameObject); else DestroyImmediate(view.Graphic.gameObject);
    }
    private void Clear()
    {
        foreach (var view in views.Values) DestroyView(view);
        while (pool.Count > 0) DestroyView(pool.Pop());
        views.Clear(); candidates.Clear(); removed.Clear(); VisibleBarCount = 0;
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
}

