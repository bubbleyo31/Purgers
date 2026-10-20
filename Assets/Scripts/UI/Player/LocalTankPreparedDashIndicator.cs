using Fusion;
using UnityEngine;

/// <summary>只屬於操作電鋸玩家的本機視覺。沒有 NetworkObject、RPC、傷害或移動寫入。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10000)]
public sealed partial class LocalTankPreparedDashIndicator : MonoBehaviour
{
    TankMeleeCombo source;
    Camera explicitCamera;
    Canvas canvas;
    RectTransform bracketViewport;
    TankTargetBracketGraphic bracket;
    readonly TankPreparedDashPreviewQuery query = new TankPreparedDashPreviewQuery();
    Collider displayedCollider;
    Rect displayedRect;
    bool hasRect;
    float ringAlpha, nextGroundRefresh;
    public bool BracketVisible => bracket != null && bracket.enabled;
    public float GroundRingAlpha => ringAlpha;
    public Collider SelectedHitCollider => BracketVisible ? query.HitCollider : null;

    public void Initialize(TankMeleeCombo owner) { source = owner; }
    /// <summary>同 Editor Multi Peer 驗證需指定對應世界相機；不共用另一 Runner 的 Camera.main。</summary>
    public void SetWorldCamera(Camera camera) { explicitCamera = camera; HideImmediately(); }

    Camera ResolveCamera()
    {
        if (explicitCamera != null) return explicitCamera;
        int count = 0; NetworkRunner unique = null;
        foreach (var runner in NetworkRunner.Instances)
            if (runner.IsRunning && runner.LocalPlayer.IsRealPlayer) { count++; unique = runner; }
        return count == 1 && source != null && source.Runner == unique ? Camera.main : null;
    }

    void LateUpdate()
    {
        if (source == null || !source.TryGetPreparedDashPreview(out var frame)) { HideImmediately(); return; }
        var camera = ResolveCamera();
        if (camera == null || !camera.isActiveAndEnabled) { HideImmediately(); return; }
        var settings = source.PreparedDashIndicatorSettings;
        float targetAlpha = TankPreparedDashIndicatorRules.RingAlpha(frame.Preparing, frame.Grounded, frame.Frozen);
        ringAlpha = Mathf.MoveTowards(ringAlpha, targetAlpha, Time.unscaledDeltaTime / Mathf.Max(.01f, settings.ringFadeSeconds));
        if (targetAlpha > 0f && (groundMesh == null || Time.unscaledTime >= nextGroundRefresh))
        {
            RefreshGround(frame, settings); nextGroundRefresh = Time.unscaledTime + Mathf.Max(.01f, settings.ringRefreshSeconds);
        }
        // During takeoff fade, retain the last valid ground samples instead of drawing a floating ring.
        DrawGround(camera, settings, ringAlpha);

        Vector3 origin = default;
        bool valid = frame.Frozen && ActiveAbilityPhysics.TryGetShotOrigin(frame.Owner, frame.AimHeight, 0f, frame.ObstructionMask, out origin) &&
            query.TrySelect(frame.Owner, origin, frame.Direction, frame.Range, frame.HalfAngle, frame.EnemyMask, frame.ObstructionMask);
        if (valid)
        {
            // Match the existing already-at-stop rule: there will be no approach movement here.
            Vector3 direction = (query.HitPoint - origin).normalized;
            Vector3 destination = TankPreparedDashRules.Destination(query.HitPoint, direction, frame.StopDistance, Vector3.up * frame.AimHeight);
            valid = Vector3.Dot(destination - frame.Position, direction) > .05f;
        }
        float pixels = Mathf.Max(.5f, camera.pixelHeight / 1080f);
        if (!valid || !query.TryGetFrameGeometry(frame.EnemyMask, settings.largeEnemySize, out var bounds, out var matrix) ||
            !TankPreparedDashIndicatorRules.TryProject(camera, bounds, matrix, settings.bracketPadding * pixels, out var rect))
        { HideBracket(); return; }
        EnsureBracket(camera);
        // No smoothing between different enemies/body parts. Only one selected collider is ever displayed.
        if (!hasRect || displayedCollider != query.HitCollider) displayedRect = rect;
        else
        {
            float t = settings.bracketSmoothSeconds <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / settings.bracketSmoothSeconds);
            displayedRect = Rect.MinMaxRect(Mathf.Lerp(displayedRect.xMin, rect.xMin, t), Mathf.Lerp(displayedRect.yMin, rect.yMin, t),
                Mathf.Lerp(displayedRect.xMax, rect.xMax, t), Mathf.Lerp(displayedRect.yMax, rect.yMax, t));
        }
        displayedCollider = query.HitCollider; hasRect = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(bracketViewport, displayedRect.center, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var point);
        bracket.rectTransform.anchoredPosition = point;
        bracket.rectTransform.sizeDelta = displayedRect.size;
        bracket.SetStyle(settings.bracketColor, settings.bracketWidth * pixels, settings.bracketCornerLength * pixels);
        bracket.enabled = true;
    }

    void EnsureBracket(Camera camera)
    {
        if (canvas == null)
        {
            var root = new GameObject("Local Tank target frame", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            // 保留真實投影框的位置，只裁掉相機視窗外的線段；不把超大目標變成全螢幕四角框。
            var viewport = new GameObject("Target viewport", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            bracketViewport = (RectTransform)viewport.transform;
            bracketViewport.anchorMin = bracketViewport.anchorMax = bracketViewport.pivot = Vector2.one * .5f;
            var child = new GameObject("Target corners", typeof(RectTransform), typeof(TankTargetBracketGraphic));
            child.transform.SetParent(bracketViewport, false); bracket = child.GetComponent<TankTargetBracketGraphic>();
            bracket.rectTransform.anchorMin = bracket.rectTransform.anchorMax = bracket.rectTransform.pivot = Vector2.one * .5f;
            bracket.raycastTarget = false;
        }
        canvas.targetDisplay = camera.targetDisplay;
        // 每次更新以支援視窗大小或 camera.pixelRect 變更，Overlay Canvas 沒有 CanvasScaler。
        var canvasRoot = (RectTransform)canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, camera.pixelRect.center,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var center);
        bracketViewport.anchoredPosition = center;
        bracketViewport.sizeDelta = camera.pixelRect.size;
    }

    void HideBracket() { if (bracket != null) bracket.enabled = false; hasRect = false; displayedCollider = null; query.Clear(); }
    void HideImmediately() { HideBracket(); ringAlpha = 0f; nextGroundRefresh = 0f; }
    public void Shutdown()
    {
        HideImmediately(); source = null;
        if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); canvas = null; bracket = null; bracketViewport = null; }
        ReleaseGround();
    }
    void OnDisable() => HideImmediately();
    void OnDestroy() => Shutdown();
}
