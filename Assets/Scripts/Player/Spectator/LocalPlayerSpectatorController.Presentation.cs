using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>觀戰只切換本機呈現來源；不變更 Input Authority、玩家輸入或遠端武器引用。</summary>
public partial class LocalPlayerSpectatorController
{
    private static readonly List<LocalPlayerSpectatorController> activeControllers = new List<LocalPlayerSpectatorController>();
    private readonly Dictionary<Renderer, ShadowCastingMode> observedBody = new Dictionary<Renderer, ShadowCastingMode>();
    private Player presentationPlayer;
    private PlayerProfession observedProfession;
    private PlayerGrappleVisual observedGrapple;
    private ProfessionViewModelManager observedViewModels;
    private FirstPersonViewModelMotionController observedMotion;

    private void OnEnable()
    {
        if (!activeControllers.Contains(this)) activeControllers.Add(this);
    }

    /// <summary>HUD 的唯一只讀來源路由。本機角色優先，死亡觀戰才讀同 Runner 的有效隊友。</summary>
    public static bool TryGetViewedPlayerObject(NetworkRunner sourceRunner, out NetworkObject playerObject)
    {
        playerObject = null;
        if (sourceRunner == null || !sourceRunner.IsRunning || !sourceRunner.LocalPlayer.IsRealPlayer)
            return false;
        if (sourceRunner.TryGetPlayerObject(sourceRunner.LocalPlayer, out var local) && local != null && local.IsValid)
        {
            playerObject = local;
            return true;
        }
        foreach (var controller in activeControllers)
        {
            if (controller == null || !controller.isActiveAndEnabled || controller.runner != sourceRunner ||
                !controller.spectatorActive || !controller.IsCurrentSpectatorTargetValid()) continue;
            playerObject = controller.spectatedPlayerObject;
            return true;
        }
        return false;
    }

    /// <summary>只讀呈現資格，也適用於該玩家擁有的職業 Runtime；不能用於 Gameplay 授權。</summary>
    public static bool IsViewedObject(NetworkObject value)
    {
        return value != null && value.IsValid &&
            TryGetViewedPlayerObject(value.Runner, out var viewed) &&
            value.InputAuthority == viewed.InputAuthority;
    }

    public static bool IsObserving(NetworkRunner sourceRunner) =>
        TryGetViewedPlayerObject(sourceRunner, out var viewed) && viewed.InputAuthority != sourceRunner.LocalPlayer;

    public static PlayerRef ViewedPlayer(NetworkRunner sourceRunner) =>
        TryGetViewedPlayerObject(sourceRunner, out var viewed) ? viewed.InputAuthority : PlayerRef.None;

    private void UpdateFirstPersonPresentation()
    {
        if (!IsCurrentSpectatorTargetValid() || CameraFollow.Singleton == null) return;
        if (presentationPlayer != spectatedPlayerBehaviour)
        {
            ClearObservedPresentation();
            presentationPlayer = spectatedPlayerBehaviour;
            observedProfession = presentationPlayer.GetComponent<PlayerProfession>();
            observedGrapple = presentationPlayer.GetComponent<PlayerGrappleVisual>();
            var anchor = presentationPlayer.GetComponent<TeammateVisualAnchor>();
            HideObservedBody(anchor != null ? anchor.bodyRenderers : null);
            if (FirstPersonMovementEffects.Singleton != null) FirstPersonMovementEffects.Singleton.SetTarget(presentationPlayer);
            if (FirstPersonSlideReadySpeedLines.Singleton != null) FirstPersonSlideReadySpeedLines.Singleton.SetTarget(presentationPlayer);
            if (GrappleFovEffect.Singleton != null) GrappleFovEffect.Singleton.SetTarget(presentationPlayer);
            if (observedGrapple != null) observedGrapple.BindLocalVisual();
        }
        if (observedViewModels != ProfessionViewModelManager.Singleton)
        {
            if (observedViewModels != null) observedViewModels.ClearTarget(observedProfession);
            observedViewModels = ProfessionViewModelManager.Singleton;
            if (observedViewModels != null) observedViewModels.SetObservedTarget(observedProfession);
        }
        if (observedMotion == null)
            observedMotion = CameraFollow.Singleton.GetComponentInChildren<FirstPersonViewModelMotionController>(true);
        if (observedMotion != null) observedMotion.BindObservedProfession(observedProfession);

        // 遠端 PlayerLocalView 不會更新 Pitch；只更新視覺錨點，不寫 KCC 或 Networked 資料。
        spectatedMovement.UpdateCameraTargetVisual();
        var eye = spectatedMovement.CamTarget;
        CameraFollow.Singleton.SetWorldPose(transform, eye.position, eye.rotation);
    }

    private void HideObservedBody(Renderer[] renderers)
    {
        if (renderers == null) return;
        foreach (var body in renderers)
        {
            if (body == null || observedBody.ContainsKey(body)) continue;
            observedBody.Add(body, body.shadowCastingMode);
            body.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }

    private void RestoreObservedBody()
    {
        foreach (var pair in observedBody)
            if (pair.Key != null) pair.Key.shadowCastingMode = pair.Value;
        observedBody.Clear();
    }

    private void ClearObservedPresentation()
    {
        RestoreObservedBody();
        // Unity destroyed-object 也要解除；否則遠端 Despawn 後第一人稱模型會留在鏡頭前。
        if (observedViewModels != null) observedViewModels.ClearTarget(observedProfession);
        if (observedMotion != null) observedMotion.ClearObservedProfession(observedProfession);
        if (FirstPersonMovementEffects.Singleton != null) FirstPersonMovementEffects.Singleton.ClearTarget(presentationPlayer);
        if (FirstPersonSlideReadySpeedLines.Singleton != null) FirstPersonSlideReadySpeedLines.Singleton.ClearTarget(presentationPlayer);
        if (GrappleFovEffect.Singleton != null) GrappleFovEffect.Singleton.ClearTarget(presentationPlayer);
        if (observedGrapple != null) observedGrapple.UnbindLocalVisual();
        observedProfession = null;
        observedGrapple = null;
        presentationPlayer = null;
        observedViewModels = null;
        observedMotion = null;
    }
}
