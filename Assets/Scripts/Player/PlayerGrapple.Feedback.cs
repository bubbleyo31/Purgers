using Fusion;
using UnityEngine;

/// <summary>原鈎索的聲音與本機鏡頭呈現。一次聲由 Host 廣播，循環聲讀同步拉動狀態。</summary>
public partial class PlayerGrapple
{
    [Header("鈎索世界音效")]
    [SerializeField, Tooltip("合法鉤頭正式射出時播放一次的 3D Cue。須有非零 Network ID 並登錄玩家的 Catalog；留空停用。聲源跟隨玩家。")]
    private GameplayAudioCue grappleShootCue;
    [SerializeField, Tooltip("普通鈎索真正拉動自己時循環播放的 3D Cue。直拉與左右甩共用；結束拉動後短暫淡出，慣性與 Support 拉取目標不啟動。須登錄 Catalog；留空停用。")]
    private GameplayAudioCue grapplePullLoopCue;
    [SerializeField, Min(0f), Tooltip("結束拉動時的世界聲淡出秒數，預設 0.15，建議 0.1～0.2；0 立即停止。下次鉤頭正式射出會強制切掉尾音；死亡、停用或離線立即清理。使用真實時間，不受慢動作影響。")]
    private float grapplePullFadeOutDuration = .15f;
    [SerializeField, Tooltip("鉤頭實際抵達命中點時播放一次的 3D Cue。固定在當次世界命中座標，不跟隨玩家；尚未抵達就取消不播放。須登錄 Catalog；留空停用。")]
    private GameplayAudioCue grappleImpactCue;

    [Header("成功出鈎鏡頭震動")]
    [SerializeField, Tooltip("成功建立新鈎索才震動使用者自己的畫面；無效目標、無能量、封鎖或 Q 斷繩不震動。")]
    private bool enableGrappleLaunchShake = true;
    [SerializeField, Min(0f), Tooltip("成功啟動時震動秒數；0 停用。初始 0.25 秒，參考目前電鋸輕攻擊；獨立調整。")]
    private float grappleLaunchShakeDuration = .25f;
    [SerializeField, Min(0f), Tooltip("CameraEffectsRoot 本機 XY 位置震動幅度；0 不平移。初始 1，參考目前電鋸輕攻擊，不改 KCC 位置。")]
    private float grappleLaunchPositionAmplitude = 1f;
    [SerializeField, Min(0f), Tooltip("鏡頭旋轉震動幅度，角度；0 不旋轉。初始 1.5，參考目前電鋸輕攻擊，不改實際瞄準方向。")]
    private float grappleLaunchRotationAmplitude = 1.5f;

    [Networked] private byte PullAudioVariant { get; set; }
    [Networked] private float PullAudioPitch { get; set; }
    [Networked] private int PullAudioShotSequence { get; set; }
    private int observedPullAudioShotSequence;
    private bool pullAudioFading;
    private bool feedbackSpawned;
    private NetworkPlayerAudioEmitter grappleAudioEmitter;
    private GameplayAudioService pullAudioService;
    private GameplayAudioCue playingPullCue;
    private int pullAudioHandle = -1;
    private byte playingPullVariant;
    private float playingPullPitch;
#if UNITY_EDITOR
    public int FeedbackAcceptedCount { get; private set; }
    public int FeedbackShotCount { get; private set; }
    public int FeedbackImpactCount { get; private set; }
    public int FeedbackShakeCount { get; private set; }
    public bool FeedbackPullPlaying => pullAudioService != null && pullAudioService.HasWorldLoop(pullAudioHandle);
#endif

    private void InitializeGrappleFeedback()
    {
        StopGrapplePullAudio();
        grappleAudioEmitter = GetComponent<NetworkPlayerAudioEmitter>();
        observedPullAudioShotSequence = PullAudioShotSequence;
        feedbackSpawned = true;
    }

    private bool CanSendGrappleFeedback => feedbackSpawned && Object != null && Object.IsValid &&
        Object.HasStateAuthority && Runner != null && Runner.IsForward;

    private void NotifyGrappleLaunchAccepted()
    {
        if (!CanSendGrappleFeedback) return;
#if UNITY_EDITOR
        FeedbackAcceptedCount++;
#endif
        RPC_GrappleLaunchShake();
    }

    private void NotifyGrappleShot()
    {
        if (!CanSendGrappleFeedback) return;
        // 同步序號讓遠端即使跳過 Shooting 快照，也能切掉前一鈎的尾音。
        PullAudioShotSequence = unchecked(PullAudioShotSequence + 1);
        observedPullAudioShotSequence = PullAudioShotSequence;
        StopGrapplePullAudio();
        if (grappleAudioEmitter == null) return;
        bool sent = grappleAudioEmitter.PlayFollowingWorldOneShotFromStateAuthority(grappleShootCue);
#if UNITY_EDITOR
        if (sent) FeedbackShotCount++;
#endif
    }

    private void NotifyGrappleImpact()
    {
        if (!CanSendGrappleFeedback) return;
        bool sent = grappleAudioEmitter != null &&
            grappleAudioEmitter.PlayWorldOneShotFromStateAuthority(grappleImpactCue, GetCurrentGrapplePoint());
#if UNITY_EDITOR
        if (sent) FeedbackImpactCount++;
#endif
        // 選擇一次並同步，晚加入只重建當前 Loop，不補播發射／命中事件。
        PullAudioPitch = 0f;
        if (grapplePullLoopCue != null && grapplePullLoopCue.TryCreatePlaybackSelection(out byte variant, out float pitch))
        {
            PullAudioVariant = variant;
            PullAudioPitch = pitch;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority, Channel = RpcChannel.Reliable, TickAligned = false)]
    private void RPC_GrappleLaunchShake(RpcInfo info = default)
    {
        if (!isActiveAndEnabled || !feedbackSpawned || !Object.HasInputAuthority || !enableGrappleLaunchShake ||
            sideDashOwner == null || sideDashOwner.Health == null || !sideDashOwner.Health.IsRescueReady ||
            !sideDashOwner.Health.IsAlive || (Runner.Tick.Raw - info.Tick.Raw) * Runner.DeltaTime > .5f) return;
#if UNITY_EDITOR
        FeedbackShakeCount++;
#endif
        FirstPersonHitCameraShake.Singleton?.PlayGrappleLaunchShake(grappleLaunchShakeDuration,
            grappleLaunchPositionAmplitude, grappleLaunchRotationAmplitude);
    }

    public override void Render()
    {
        if (!isActiveAndEnabled || !feedbackSpawned || Object == null || !Object.IsValid) { StopGrapplePullAudio(); return; }
        if (observedPullAudioShotSequence != PullAudioShotSequence)
        {
            observedPullAudioShotSequence = PullAudioShotSequence;
            StopGrapplePullAudio();
        }
        bool alive = sideDashOwner != null && sideDashOwner.Health != null && sideDashOwner.Health.IsRescueReady && sideDashOwner.Health.IsAlive &&
            !sideDashOwner.Health.IsDowned;
        bool active = GrappleFeedbackRules.ShouldPlayPull(true, CurrentPhase,
            SupportInteractionTetherActive, HasAppliedAttachedPull, alive);
        var service = GameplayAudioService.Instance;
        if (!alive || grapplePullLoopCue == null || grappleAudioEmitter == null || service == null ||
            !service.isActiveAndEnabled || PullAudioPitch <= 0f)
        { StopGrapplePullAudio(); return; }
        if (!active)
        {
            if (pullAudioService != null)
            {
                pullAudioService.FadeOutWorldLoop(pullAudioHandle, grapplePullFadeOutDuration);
                pullAudioFading = true;
                if (!pullAudioService.HasWorldLoop(pullAudioHandle)) StopGrapplePullAudio();
            }
            return;
        }
        if (!pullAudioFading && pullAudioService == service && playingPullCue == grapplePullLoopCue &&
            playingPullVariant == PullAudioVariant && playingPullPitch == PullAudioPitch &&
            service.HasWorldLoop(pullAudioHandle)) return;
        StopGrapplePullAudio();
        pullAudioHandle = grappleAudioEmitter.StartFollowingWorldLoop(grapplePullLoopCue, PullAudioVariant, PullAudioPitch);
        if (pullAudioHandle > 0)
        {
            pullAudioService = service;
            playingPullCue = grapplePullLoopCue;
            playingPullVariant = PullAudioVariant;
            playingPullPitch = PullAudioPitch;
        }
    }

    private void StopGrapplePullAudio()
    {
        if (pullAudioService != null) pullAudioService.StopWorldLoop(pullAudioHandle);
        pullAudioService = null; playingPullCue = null; pullAudioHandle = -1; pullAudioFading = false;
    }
    private void DisposeGrappleFeedback() { feedbackSpawned = false; StopGrapplePullAudio(); }
    private void OnDisable() => StopGrapplePullAudio();
    private void OnDestroy() => DisposeGrappleFeedback();
}
