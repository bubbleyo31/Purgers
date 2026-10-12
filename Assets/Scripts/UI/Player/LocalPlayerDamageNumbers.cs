using Fusion;
using TMPro;
using UnityEngine;

/// <summary>訂閱本機玩家已確認的敵人傷害，以世界命中快照投影到螢幕；不追蹤敵人、不判斷遮擋、不寫入 Gameplay。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform)), DefaultExecutionOrder(210)]
public sealed class LocalPlayerDamageNumbers : MonoBehaviour
{
    [Header("傷害跳字引用")]
    [SerializeField, Tooltip("共用顯示設定；需指定。數值與開關可在 Play Mode 調整。")]
    private DamageNumberSettings settings;
    [SerializeField, Tooltip("所屬 Runner；留空只在唯一有效本機 Runner 時自動綁定，Multi Peer 必須指定。")]
    private NetworkRunner runner;
    [SerializeField, Tooltip("玩家世界相機，不可指定 ViewModel 相機。單一 Runner 可留空使用 Camera.main；Multi Peer 必須指定。")]
    private Camera worldCamera;
    [SerializeField, Tooltip("螢幕空間 Canvas 下的容器；留空使用本物件。不要放在 World Space Canvas 或會裁切的 Mask 中。")]
    private RectTransform visualRoot;
    private bool explicitRunner, explicitCamera;
    private float nextResolve;
    private Canvas canvas;
    private PlayerCombatFeedbackRelay relay;
    private PlayerHealth health;
    private readonly DamageNumberBuffer buffer = new DamageNumberBuffer();
    private readonly View[] views = new View[DamageNumberBuffer.Capacity];
    public int ActiveCount { get; private set; }
    public int ReceivedEventCount { get; private set; }
    public PlayerCombatFeedbackRelay BoundRelay => relay;
    public DamageNumberSettings Settings => settings;

    private sealed class View
    {
        public TextMeshProUGUI Text;
        public Material Material;
        public TMP_FontAsset Font;
        public int Revision = -1;
        public float OutlineWidth = -1f;
        public Color OutlineColor;
    }
    private void Awake()
    {
        explicitRunner = runner != null;
        explicitCamera = worldCamera != null;
        if (visualRoot == null) visualRoot = (RectTransform)transform;
        canvas = visualRoot.GetComponentInParent<Canvas>();
    }
    /// <summary>供既有 HUD 建立者指定 Runner 與世界相機；換綁時清除上一位玩家的短暫回饋。</summary>
    public void Configure(DamageNumberSettings value, NetworkRunner owner, Camera camera)
    {
        Unbind(); settings = value; runner = owner; worldCamera = camera;
        explicitRunner = owner != null; explicitCamera = camera != null; nextResolve = 0f;
    }
    private void OnDisable() => Unbind();
    private void OnDestroy()
    {
        Unbind();
        foreach (var view in views)
        {
            if (view == null) continue;
            Release(view.Material);
            if (view.Text != null) Release(view.Text.gameObject);
        }
    }
    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
    private void Unbind()
    {
        if (relay != null) relay.LocalHitConfirmed -= Receive;
        relay = null; health = null; Clear();
    }
    public void Clear()
    {
        buffer.Clear(); ActiveCount = 0;
        foreach (var view in views) if (view != null && view.Text != null) view.Text.gameObject.SetActive(false);
    }
    private void Resolve(float now)
    {
        if (now < nextResolve) return;
        nextResolve = now + .2f;
        int localRunners = 0;
        NetworkRunner unique = null;
        foreach (var candidate in NetworkRunner.Instances)
        {
            if (!candidate.IsRunning || !candidate.LocalPlayer.IsRealPlayer) continue;
            localRunners++; unique = candidate;
        }
        if (!explicitRunner) runner = localRunners == 1 ? unique : null;
        if (!explicitCamera) worldCamera = localRunners == 1 ? Camera.main : null;
        PlayerCombatFeedbackRelay found = null;
        if (runner != null && runner.IsRunning && runner.TryGetPlayerObject(runner.LocalPlayer, out var player) &&
            player != null && player.IsValid && player.HasInputAuthority)
            found = player.GetComponent<PlayerCombatFeedbackRelay>();
        if (found == relay) return;
        Unbind(); relay = found;
        if (relay != null)
        {
            health = relay.GetComponent<PlayerHealth>();
            relay.LocalHitConfirmed += Receive;
        }
    }
    private bool HasLocalOwner() => relay != null && relay.Object != null && relay.Object.IsValid &&
        relay.Object.HasInputAuthority && (health == null || health.IsAlive);
    private void Receive(CombatHitFeedbackData hit)
    {
        if (!HasLocalOwner() || settings == null || !settings.showNumbers || !DamageNumberRules.ShouldDisplay(hit)) return;
        ReceivedEventCount++;
        // 畫面外的新事件不進池；不會在玩家稍後轉頭時突然補播。
        if (!DamageNumberRules.TryProject(worldCamera, hit.HitPoint, out _)) return;
        AddSnapshot(hit, Time.unscaledTime);
    }
    private void AddSnapshot(CombatHitFeedbackData hit, float now) => buffer.Add(hit, now, settings);
    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        Resolve(now);
        if (!HasLocalOwner() || settings == null || !settings.showNumbers ||
            worldCamera == null || !worldCamera.isActiveAndEnabled) { Clear(); return; }
        RenderSnapshots(now, worldCamera);
    }
    private Vector2 LocalPoint(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(visualRoot, screen,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local);
        return local - visualRoot.rect.center;
    }
    private View CreateView(int index)
    {
        var go = new GameObject("Damage Number " + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(visualRoot, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false; text.enableWordWrapping = false; text.enableAutoSizing = false;
        text.richText = false; text.alignment = TextAlignmentOptions.Center; text.overflowMode = TextOverflowModes.Overflow;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = new Vector2(300f, 120f);
        return new View { Text = text };
    }
    /// <summary>每幀只更新位置／顏色／樣式；傷害文字僅在新增或合併時重建，沒有物理查詢。</summary>
    private void RenderSnapshots(float now, Camera camera)
    {
        if (settings == null || visualRoot == null || canvas == null || canvas.renderMode == RenderMode.WorldSpace) { Clear(); return; }
        buffer.Expire(now, settings);
        float pixels = camera.pixelHeight / 1080f;
        // 換算螢幕像素到實際容器座標，兼容既有 BattleHudReferenceFrame 的巢狀縮放。
        Vector2 center = LocalPoint(camera.pixelRect.center);
        float unitsPerPixel = Mathf.Abs((LocalPoint(camera.pixelRect.center + Vector2.up) - center).y);
        float scale = pixels * unitsPerPixel;
        ActiveCount = 0;
        for (int i = 0; i < views.Length; i++)
        {
            var entry = buffer[i]; var view = views[i];
            if (!entry.Active || !DamageNumberRules.TryProject(camera, entry.Hit.HitPoint, out var point))
            {
                // 離開畫面後結束本筆，不做邊緣指示或轉回時補播。
                entry.Active = false;
                if (view != null) view.Text.gameObject.SetActive(false);
                continue;
            }
            if (view == null) view = views[i] = CreateView(i);
            var text = view.Text;
            var font = settings.font != null ? settings.font : TMP_Settings.defaultFontAsset;
            if (font == null) { text.gameObject.SetActive(false); continue; }
            if (view.Font != font)
            {
                Release(view.Material); view.Font = font; text.font = font;
                view.Material = new Material(font.material); text.fontSharedMaterial = view.Material; view.OutlineWidth = -1f;
            }
            float outline = Mathf.Clamp(settings.outlineWidth, 0f, .4f);
            if (view.Material.HasProperty(ShaderUtilities.ID_OutlineWidth) &&
                (view.OutlineWidth != outline || view.OutlineColor != settings.outlineColor))
            {
                view.OutlineWidth = outline; view.OutlineColor = settings.outlineColor;
                view.Material.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
                view.Material.SetColor(ShaderUtilities.ID_OutlineColor, settings.outlineColor);
                text.UpdateMeshPadding();
            }
            if (view.Revision != entry.Revision)
            {
                text.text = DamageNumberRules.FormatAmount(entry.Amount);
                view.Revision = entry.Revision;
            }
            float age = Mathf.Max(0f, now - entry.Started);
            float duration = Mathf.Clamp(settings.lifetime, .1f, 3f);
            bool head = DamageNumberRules.IsHeadHit(entry.Hit);
            float pop = Mathf.Lerp(Mathf.Clamp(settings.popScale, 1f, 1.5f), 1f, Mathf.Clamp01(age / .12f));
            text.fontSize = Mathf.Clamp(settings.fontSize, 8f, 96f) * (head ? Mathf.Clamp(settings.headScale, 1f, 3f) : 1f) * pop;
            Color color = head ? settings.headColor : settings.normalColor;
            color.a *= DamageNumberRules.Alpha(age, duration, settings.holdFraction); text.color = color;
            Vector2 offset = settings.hitOffset + Vector2.up * (Mathf.Clamp(settings.riseDistance, 0f, 80f) * age / duration +
                entry.Lane * Mathf.Clamp(settings.stackSpacing, 0f, 40f));
            point += offset * pixels;
            // 只有已確認在畫面內的命中點才保護字框邊界；絕不把後方命中投影到螢幕邊緣。
            Vector2 preferred = text.GetPreferredValues(text.text) * pixels;
            Rect bounds = camera.pixelRect;
            float halfWidth = Mathf.Min(preferred.x * .5f + 4f * pixels, bounds.width * .5f);
            float halfHeight = Mathf.Min(preferred.y * .5f + 4f * pixels, bounds.height * .5f);
            point.x = Mathf.Clamp(point.x, bounds.xMin + halfWidth, bounds.xMax - halfWidth);
            point.y = Mathf.Clamp(point.y, bounds.yMin + halfHeight, bounds.yMax - halfHeight);
            text.rectTransform.anchoredPosition = LocalPoint(point);
            text.rectTransform.localScale = Vector3.one * scale;
            text.gameObject.SetActive(true); ActiveCount++;
        }
    }
}
