using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>人工掛在一般敵人 Root 的顯示入口；只在 Render 讀取原生命快照，沒有 Networked 欄位。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(TestDamageReceiver))]
public sealed class EnemyHealthBarTarget : NetworkBehaviour
{
    public enum DisplayMode { Hidden, Normal, Large }
    [Header("敵人血條類型")]
    [SerializeField, Tooltip("Normal 為一般頭頂血條；Large 啟用頭頂出框備用位置；Hidden 完全排除。Boss 不掛此元件，繼承此元件時必須選 Hidden。")]
    private DisplayMode displayMode = DisplayMode.Normal;
    [Header("人工錨點與主要身體範圍")]
    [SerializeField, Tooltip("正常血條跟隨的頭頂 Transform；留空時使用主要身體盒的上緣中心。")]
    private Transform headAnchor;
    [SerializeField, Tooltip("主要身體盒的本機座標中心（公尺），相對本元件 Transform；不包含武器、翅膀、觸手及特效。")]
    private Vector3 bodyCenter = new Vector3(0, 1, 0);
    [SerializeField, Tooltip("主要身體盒的本機全尺寸（公尺），各軸至少 0.01；應貼合不透明主要身體，過大會誤判可見。")]
    private Vector3 bodySize = new Vector3(1, 2, 1);

    private static readonly List<EnemyHealthBarTarget> active = new List<EnemyHealthBarTarget>();
    public static IReadOnlyList<EnemyHealthBarTarget> Active => active;
    private TestDamageReceiver health;
    private bool spawned;
    private StatusEffectSources statusSources;
    public int ReadStatusEffects(NetworkRunner owner, StatusEffectSnapshot[] buffer) => spawned && HasSnapshot &&
        Object != null && Object.IsValid && Runner == owner && statusSources != null
        ? statusSources.Read(owner.LocalPlayer, buffer) : 0;
    private EnemyHealthBarActivity activity;
    public bool HasSnapshot { get; private set; }
    public float HealthRatio { get; private set; }
    public bool IsLarge => displayMode == DisplayMode.Large;
    public Bounds LocalBody => new Bounds(bodyCenter, new Vector3(Mathf.Max(.01f, bodySize.x),
        Mathf.Max(.01f, bodySize.y), Mathf.Max(.01f, bodySize.z)));
    public Vector3 HeadPosition => headAnchor != null ? headAnchor.position :
        transform.TransformPoint(bodyCenter + Vector3.up * LocalBody.extents.y);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();

    public override void Spawned()
    {
        health = GetComponent<TestDamageReceiver>();
        statusSources = new StatusEffectSources(gameObject);
        spawned = true;
        activity.Reset();
        HasSnapshot = false;
        if (isActiveAndEnabled && !active.Contains(this)) active.Add(this);
    }

    private void OnEnable()
    {
        if (spawned && !active.Contains(this)) active.Add(this);
    }

    private void OnDisable()
    {
        active.Remove(this);
        HasSnapshot = false;
    }

    /// <summary>延到 Render 才讀其他 NetworkBehaviour，避免 Spawned 呼叫順序依賴。</summary>
    public override void Render()
    {
        HasSnapshot = false;
        if (!spawned || !isActiveAndEnabled || displayMode == DisplayMode.Hidden ||
            health == null || Object == null || !Object.IsValid) return;
        float current = health.CurrentHealth, maximum = health.MaxHealth;
        activity.Observe(current, maximum, Time.unscaledTimeAsDouble);
        HealthRatio = EnemyHealthBarRules.Ratio(current, maximum);
        HasSnapshot = HealthRatio > 0;
    }

    public bool IsReadyFor(NetworkRunner owner, bool hasVisibleStatus = false) => spawned && HasSnapshot && isActiveAndEnabled &&
        displayMode != DisplayMode.Hidden && Object != null && Object.IsValid && Runner == owner &&
        StatusEffectRules.ShowEnemy(HealthRatio > 0, activity.IsVisible(Time.unscaledTimeAsDouble), hasVisibleStatus);

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        active.Remove(this);
        spawned = false;
        activity.Reset();
        HasSnapshot = false;
        HealthRatio = 0;
    }

    private void OnDestroy() => active.Remove(this);
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(LocalBody.center, LocalBody.size);
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawSphere(HeadPosition, .06f);
    }
}

