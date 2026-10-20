using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>只讀本機有效玩家結果，單次事件可自然連續刷新；不訂閱觀戰對象、也不猜測其傷害。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(ScreenEdgeGraphic))]
public sealed class LocalPlayerScreenEdgeEffects : MonoBehaviour
{
    [SerializeField, Tooltip("三種外框的共用樣式與混合設定。")]
    private ScreenEdgeEffectsSettings settings;
    [SerializeField, Tooltip("留空只接受唯一 ProvideInput 本機 Runner；多 Peer 必須明確指定。")]
    private NetworkRunner runner;
    private NetworkRunner resolved;
    private PlayerHealth health;
    private PlayerGrappleMomentumEnergy energy;
    private ScreenEdgeGraphic graphic;
    private readonly ScreenEdgePulse healing = new ScreenEdgePulse(), damage = new ScreenEdgePulse();
    private float nextResolve, momentum;
    private static readonly HashSet<LocalPlayerScreenEdgeEffects> instances = new HashSet<LocalPlayerScreenEdgeEffects>();
    public Vector3 Weights { get; private set; }
    public PlayerHealth BoundHealth => health;
    public int HealingEvents { get; private set; }
    public int DamageEvents { get; private set; }
    public ScreenEdgeEffectsSettings Settings => settings;
    public static bool OwnsHealing(PlayerHealth value)
    {
        foreach (var item in instances)
            if (item != null && item.isActiveAndEnabled && item.health == value && value != null &&
                item.settings != null && item.settings.effectsEnabled && item.settings.healing.strength > 0 &&
                item.graphic != null && item.graphic.IsReady) return true;
        return false;
    }
    public void Configure(ScreenEdgeEffectsSettings value, NetworkRunner ownerRunner)
    { Unbind(); settings=value; runner=ownerRunner; resolved=null; nextResolve=0; EnsureGraphic(); }
    private void OnEnable() { instances.Add(this); EnsureGraphic(); ConfigureLayer(); }
    private void OnTransformParentChanged() => ConfigureLayer();
    private void ConfigureLayer()
    {
        var ownCanvas=GetComponent<Canvas>();
        if (ownCanvas != null && transform.parent != null) { ownCanvas.overrideSorting=true; ownCanvas.sortingOrder=-5; }
    }
    private void EnsureGraphic()
    { if (graphic==null) graphic=GetComponent<ScreenEdgeGraphic>(); if(graphic!=null) { if(Application.isPlaying)graphic.Configure(settings); graphic.enabled=false; } }
    private void OnDisable() { instances.Remove(this); Unbind(); resolved=null; }
    private void OnDestroy() { instances.Remove(this); Unbind(); }
    public void Clear()
    { healing.Clear(); damage.Clear(); momentum=0; Weights=Vector3.zero; if(graphic!=null)graphic.enabled=false; }
    private void Unbind()
    {
        if(health!=null) { health.LocalHealingReceived-=OnHealing; health.LocalDamageReceived-=OnDamage; }
        health=null; energy=null; Clear();
    }
    private bool LivingOwner => health!=null && health.IsRescueReady && health.Object.HasInputAuthority && health.IsAlive && !health.IsDowned;
    private void OnHealing(float amount)
    { if(LivingOwner && settings!=null && settings.effectsEnabled && ScreenEdgePulse.IsPositive(amount)) { healing.Trigger(Time.unscaledTime,settings.healing,amount); HealingEvents++; } }
    private void OnDamage(PlayerDamageReceivedInfo info)
    { if(LivingOwner && settings!=null && settings.effectsEnabled && ScreenEdgePulse.IsPositive(info.AppliedDamage)) { damage.Trigger(Time.unscaledTime,settings.damage,info.AppliedDamage); DamageEvents++; } }
    private void LateUpdate()
    {
        if(!Application.isPlaying || settings==null || !settings.effectsEnabled) { Unbind(); return; }
        float now=Time.unscaledTime;
        if(runner!=null) resolved=runner;
        else if(now>=nextResolve)
        {
            nextResolve=now+.25f; resolved=null;
            foreach(var candidate in NetworkRunner.Instances)
            {
                if(!candidate.IsRunning || !candidate.ProvideInput || !candidate.LocalPlayer.IsRealPlayer)continue;
                if(resolved!=null) { resolved=null; break; } resolved=candidate;
            }
        }
        PlayerHealth found=null;
        if(resolved!=null && resolved.IsRunning && !resolved.IsSceneManagerBusy &&
            resolved.TryGetPlayerObject(resolved.LocalPlayer,out var obj) && obj!=null && obj.IsValid && obj.HasInputAuthority)
            found=obj.GetComponent<PlayerHealth>();
        if(found!=health)
        {
            Unbind(); health=found;
            if(health!=null) { health.LocalHealingReceived+=OnHealing; health.LocalDamageReceived+=OnDamage; energy=health.GetComponent<PlayerGrappleMomentumEnergy>(); }
        }
        if(!LivingOwner) { Clear(); return; }
        float target=energy!=null && energy.Object!=null && energy.Object.IsValid ? ScreenEdgePulse.FullMomentumWeight(energy.NormalizedEnergy) : 0;
        momentum=Mathf.MoveTowards(momentum,target,Time.unscaledDeltaTime/Mathf.Max(.01f,target>momentum?settings.momentum.fadeIn:settings.momentum.fadeOut));
        Weights=ScreenEdgePulse.Prioritize(healing.Evaluate(now,settings.healing),damage.Evaluate(now,settings.damage),momentum,settings.prioritySuppression);
        if(graphic==null) EnsureGraphic();
        graphic.Present(settings,Weights,now);
    }
}
