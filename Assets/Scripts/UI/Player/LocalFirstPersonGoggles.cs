using Fusion;
using UnityEngine;

/// <summary>只讀既有本機／觀戰視角來源。無玩家、死亡無目標或離線時隱藏，不更改輸入、相機或網路狀態。</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(GoggleRimGraphic))]
[AddComponentMenu("Purgers/UI/第一人稱護目鏡")]
public sealed class LocalFirstPersonGoggles : MonoBehaviour
{
    [Header("本機視角來源")]
    [SerializeField, Tooltip("留空時只接受唯一正在執行且 ProvideInput 的本機 Runner；多 Peer 必須指定。沿用既有觀戰路由，倒地仍顯示、死亡無目標時隱藏。")]
    private NetworkRunner runner;
    [Header("編輯器預覽")]
    [SerializeField, Tooltip("只在非 Play Mode 顯示邊框，方便檢查外觀；預設關閉。遊戲中仍依有效第一人稱玩家判定，不會強制覆蓋選單。")]
    private bool previewInEditor;
    private GoggleRimGraphic rim;
    private NetworkRunner resolvedRunner;
    private NetworkObject viewedObject;
    private PlayerHealth viewedHealth;
    private float nextResolve;
    public bool IsVisible => rim != null && rim.enabled && rim.Opacity > 0f && rim.Thickness > 0f;
    public NetworkObject ViewedObject => viewedObject;

    public void BindRunner(NetworkRunner value)
    {
        runner = value; resolvedRunner = null; viewedObject = null; viewedHealth = null; nextResolve = 0;
        Show(false);
    }
    private void OnEnable() { rim = GetComponent<GoggleRimGraphic>(); ConfigureOwnLayer(); nextResolve = 0; Refresh(); }
    private void OnTransformParentChanged() => ConfigureOwnLayer();
    private void ConfigureOwnLayer()
    {
        var ownCanvas = GetComponent<Canvas>();
        if (ownCanvas == null || transform.parent == null) return;
        ownCanvas.overrideSorting = true; ownCanvas.sortingOrder = -10;
    }
    private void LateUpdate() => Refresh();
    private void OnDisable() { Show(false); viewedObject = null; viewedHealth = null; resolvedRunner = null; }
    private void Refresh()
    {
        if (rim == null) rim = GetComponent<GoggleRimGraphic>();
        if (!Application.isPlaying) { Show(previewInEditor); return; }
        ResolveRunner();
        NetworkObject target = null;
        bool valid = resolvedRunner != null && resolvedRunner.IsRunning && !resolvedRunner.IsSceneManagerBusy &&
            LocalPlayerSpectatorController.TryGetViewedPlayerObject(resolvedRunner, out target);
        if (!valid) target = null;
        if (target != viewedObject)
        {
            viewedObject = target;
            viewedHealth = target != null ? target.GetComponent<PlayerHealth>() : null;
        }
        // IsRescueReady 是既有 Spawned/Despawned 生命週期防護；之後才允許讀 Networked IsDead。
        Show(valid && viewedHealth != null && viewedHealth.IsRescueReady && !viewedHealth.IsDead);
    }
    private void ResolveRunner()
    {
        if (runner != null) { resolvedRunner = runner; return; }
        if (Time.unscaledTime < nextResolve && resolvedRunner != null && resolvedRunner.IsRunning) return;
        if (Time.unscaledTime < nextResolve && resolvedRunner == null) return;
        nextResolve = Time.unscaledTime + .25f; resolvedRunner = null;
        foreach (var candidate in NetworkRunner.Instances)
        {
            if (!candidate.IsRunning || !candidate.ProvideInput || !candidate.LocalPlayer.IsRealPlayer) continue;
            if (resolvedRunner != null) { resolvedRunner = null; return; }
            resolvedRunner = candidate;
        }
    }
    private void Show(bool visible)
    {
        if (rim != null && rim.enabled != visible) rim.enabled = visible;
    }
#if UNITY_EDITOR
    private void OnValidate() { if (!Application.isPlaying && rim != null) Show(isActiveAndEnabled && previewInEditor); }
#endif
}

