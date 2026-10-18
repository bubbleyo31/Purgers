using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

/// <summary>既有 ActionGate 的捕食獨立來源。只由敵人的 State Authority 取得與釋放。</summary>
public partial class PlayerActionGate
{
    [Networked] private NetworkBool CaptureActive { get; set; }
    [Networked] private NetworkBool CaptureWeaponAllowed { get; set; }
    [Networked] public NetworkObject Captor { get; private set; }
    [Networked] private Vector3 CapturePosition { get; set; }
    [Networked] private TickTimer GunshotNotice { get; set; }
    public bool IsCaptured => IsFusionSpawned && CaptureActive;
    public bool CanUseWeaponWhileCaptured => IsCaptured && CaptureWeaponAllowed;
    public bool HasRecentGunshot => IsFusionSpawned && !GunshotNotice.ExpiredOrNotRunning(Runner);
    private PlayerActionBlockMask CaptureBlocks => CapturedActionBlocks(CanUseWeaponWhileCaptured);

    public static PlayerActionBlockMask CapturedActionBlocks(bool allowWeapon)
    {
        var blocks = PlayerActionBlockMask.Aim | PlayerActionBlockMask.Movement |
            PlayerActionBlockMask.Grapple | PlayerActionBlockMask.QuickAction | PlayerActionBlockMask.ActiveAbility;
        return allowWeapon ? blocks : blocks | PlayerActionBlockMask.Fire | PlayerActionBlockMask.Reload;
    }

    /// <summary>捕食只放行主武器及換彈（單人／多人相同）；不還原其他控制鎖已清除的輸入。</summary>
    public static NetInput FilterCapturedInput(NetInput input, bool allowWeapon)
    {
        var buttons = input.Buttons;
        input.Buttons = default;
        if (allowWeapon)
        {
            input.Buttons.Set(InputButton.Fire, buttons.IsSet(InputButton.Fire));
            input.Buttons.Set(InputButton.Reload, buttons.IsSet(InputButton.Reload));
        }
        input.Direction = Vector2.zero;
        return input;
    }

    private void InitializeCapture()
    {
        if (!Object.HasStateAuthority) return;
        CaptureActive = false; CaptureWeaponAllowed = false; Captor = null; CapturePosition = Vector3.zero; GunshotNotice = TickTimer.None;
    }

    /// <summary>只有實際成功射擊才通知，不把空槍／單純按住左鍵當作開槍。</summary>
    public void NotifyGunshot()
    {
        if (IsFusionSpawned && Object.HasStateAuthority)
            GunshotNotice = TickTimer.CreateFromTicks(Runner, 2);
    }

    public bool TryBeginCapture(NetworkObject source)
    {
        if (!IsFusionSpawned || !Object.HasStateAuthority || CaptureActive || source == null || !source.IsValid ||
            source.Runner != Runner || !source.HasStateAuthority) return false;
        var player = GetComponent<Player>();
        if (player == null || player.Health == null || !player.Health.IsAlive || IsCrowdControlImmune) return false;
        CapturePosition = player.Movement.KCC.FixedData.TargetPosition;
        Captor = source; CaptureActive = true; CaptureWeaponAllowed = true;
        player.Grapple?.CancelFromSpecialAbility(false, true);
        player.Grapple?.CancelReleaseMomentumFromSpecialAbility(true);
        player.SupportGrapplePlayerPullReceiver?.CancelPull();
        player.SlideController?.CancelForCapture();
        player.ProfessionRuntimeManager?.InterruptForCapture();
        RefreshCapturedAbilities(player);
        ClearCaptureVelocity(player.Movement.KCC);
        return true;
    }

    public void SetCapturePosition(NetworkObject source, Vector3 position)
    {
        if (IsCaptured && Object.HasStateAuthority && Captor == source) CapturePosition = position;
    }

    public void ReleaseCapture(NetworkObject source)
    {
        if (!IsCaptured || !Object.HasStateAuthority || Captor != source) return;
        var kcc = GetComponent<KCC>();
        ClearCaptureVelocity(kcc);
        CaptureActive = false; CaptureWeaponAllowed = false; Captor = null;
        RefreshCapturedAbilities(GetComponent<Player>());
    }

    private static void RefreshCapturedAbilities(Player player)
    {
        if (player == null || player.AbilityRuntimeManager == null) return;
        for (int i = 0; i < PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots; ++i)
            player.AbilityRuntimeManager.GetEquippedRuntimeAtSlot(i)?.RefreshProfessionAvailabilityNow();
    }

    private void TickCaptureLifetime()
    {
        if (!IsCaptured) return;
        var health = GetComponent<PlayerHealth>();
        if (Captor == null || !Captor.IsValid || health == null || !health.IsAlive)
            ReleaseCapture(Captor);
    }

    private static void ClearCaptureVelocity(KCC kcc)
    {
        if (kcc == null) return;
        kcc.SetInputDirection(Vector3.zero);
        kcc.SetKinematicVelocity(Vector3.zero);
        kcc.SetDynamicVelocity(Vector3.zero);
        kcc.FixedData.ExternalDelta = Vector3.zero;
    }

    /// <summary>既有 KCC PrepareData 的最高優先位移；保留 KCC 碰撞解算。</summary>
    public bool ApplyCaptureMotion(KCCData data)
    {
        if (!IsCaptured) return false;
        data.KinematicVelocity = Vector3.zero;
        data.DynamicVelocity = Vector3.zero;
        data.ExternalDelta = CapturePosition - data.BasePosition;
        return true;
    }
}
