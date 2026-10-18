using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>共用本機光束、粒子與狀態音效；只接受 owner 的已同步狀態，不送傷害或 RPC。</summary>
public sealed partial class BeamPresentationRuntime : IDisposable
{
    private readonly Transform owner;
    private readonly LineRenderer line, core;
    private readonly bool ownsLine;
    private readonly BeamPresentationEventGate events = new BeamPresentationEventGate();
    private BeamPresentationStage previous;
    private int previousSequence;
    private GameplayAudioService audioService;
    private int loopHandle = -1;
    private Material fallbackMaterial;
    private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
    public int BurstCount { get; private set; }
    public int StageEventCount { get; private set; }
    public int OneShotCount { get; private set; }

    public BeamPresentationRuntime(Transform owner, LineRenderer existing = null)
    {
        this.owner = owner;
        ownsLine = existing == null;
        line = existing != null ? existing : CreateLine("Beam (Local)");
        core = CreateLine("Beam Core (Local)");
        Configure(line); Configure(core);
    }

    private LineRenderer CreateLine(string name)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(owner, false);
        return go.AddComponent<LineRenderer>();
    }
    private static void Configure(LineRenderer renderer)
    {
        renderer.useWorldSpace = true; renderer.positionCount = 2;
        renderer.widthCurve = AnimationCurve.Constant(0, 1, 1);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false; renderer.enabled = false;
    }

    public bool Present(BeamPresentationSettings settings, BeamPresentationStage stage, int sequence,
        Vector3 start, Vector3 end, float shotScale = 1f, bool drawShot = true)
    {
        bool trigger = events.Observe(sequence, (int)stage);
        if (stage != previous || sequence != previousSequence) StopLoop();
        bool newAttack = events.NewAttack;
        var sound = settings.audio;
        if (trigger)
        {
            StageEventCount++;
            if (newAttack) PlayOneShot(sound.prepare);
            if (stage == BeamPresentationStage.Aim && !sound.loopAim) PlayOneShot(sound.aim);
            if (stage == BeamPresentationStage.Charge && !sound.loopCharge) PlayOneShot(sound.charge);
            if (stage == BeamPresentationStage.Fired)
            {
                PlayOneShot(sound.fire);
                if (drawShot) EmitParticles(settings.particles, start, end);
            }
        }
        if (stage == BeamPresentationStage.Aim && sound.loopAim) EnsureLoop(sound.aim);
        if (stage == BeamPresentationStage.Charge && sound.loopCharge) EnsureLoop(sound.charge);
        previous = stage; previousSequence = sequence;

        Material material = settings.lineMaterial;
        if (material == null)
        {
            if (fallbackMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) fallbackMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            material = fallbackMaterial;
        }
        line.sharedMaterial = core.sharedMaterial = material;
        bool fired = stage == BeamPresentationStage.Fired;
        float width = stage == BeamPresentationStage.Aim ? settings.aimWidth :
            stage == BeamPresentationStage.Charge ? settings.chargeWidth : settings.shotWidth * Mathf.Clamp01(shotScale);
        Color color = stage == BeamPresentationStage.Aim ? settings.aimColor :
            stage == BeamPresentationStage.Charge ? settings.chargeColor : settings.shotColor;
        bool visible = stage != BeamPresentationStage.Hidden && (!fired || drawShot) && width > .00001f;
        Draw(line, visible, width, color, start, end);
        Draw(core, visible && fired && settings.coreWidthRatio > 0f, width * settings.coreWidthRatio,
            settings.coreColor, start, end);
        return trigger && fired;
    }

    private void Draw(LineRenderer renderer, bool visible, float width, Color color, Vector3 start, Vector3 end)
    {
        renderer.enabled = visible;
        renderer.widthMultiplier = visible ? Mathf.Max(0f, width) : 0f;
        if (!visible) return;
        bool supportsTint = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Tint");
        renderer.startColor = renderer.endColor = supportsTint ? Color.white : color;
        renderer.GetPropertyBlock(tint);
        tint.SetColor("_Tint", color);
        renderer.SetPropertyBlock(tint);
        renderer.SetPosition(0, start); renderer.SetPosition(1, end);
    }

    private void PlayOneShot(BeamAudioSlot slot)
    {
        if (slot == null || slot.cue == null) return;
        var service = GameplayAudioService.Instance;
        if (service != null && service.PlayWorldOneShotFollowingTransform(slot.cue,
            (byte)Mathf.Clamp(slot.clipIndex, 0, 255), slot.pitch, owner, slot.volume)) OneShotCount++;
    }
    private void EnsureLoop(BeamAudioSlot slot)
    {
        if (slot == null || slot.cue == null) return;
        if (audioService != null && audioService.HasWorldLoop(loopHandle)) return;
        audioService = GameplayAudioService.Instance;
        if (audioService == null) return;
        loopHandle = audioService.StartWorldLoopWithVolume(slot.cue, (byte)Mathf.Clamp(slot.clipIndex, 0, 255),
            slot.pitch, owner, slot.volume);
    }
    private void StopLoop()
    {
        if (audioService != null && loopHandle >= 0) audioService.StopWorldLoop(loopHandle);
        loopHandle = -1;
    }
    public void Hide(bool clearParticles = true)
    {
        StopLoop(); previous = BeamPresentationStage.Hidden;
        line.enabled = core.enabled = false;
        line.widthMultiplier = core.widthMultiplier = 0;
        if (clearParticles && particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    public void Dispose()
    {
        Hide();
        if (ownsLine && line != null) Destroy(line.gameObject);
        if (core != null) Destroy(core.gameObject);
        if (particles != null) Destroy(particles.gameObject);
        if (fallbackMaterial != null) Destroy(fallbackMaterial);
    }
    private static void Destroy(UnityEngine.Object obj)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
        else UnityEngine.Object.DestroyImmediate(obj);
    }
}

