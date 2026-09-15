using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerGrapple))]
public sealed class PlayerGrappleMomentumEnergy :
    NetworkBehaviour,
    IOutgoingDamageModifier,
    IOutgoingDamageResultListener
{
    [Header("充能來源")]

    [SerializeField]
    [Tooltip("普通鈎索 Attached 且正在拉動玩家本人時，是否允許累積能量。")]
    private bool chargeWhileGrappleAttached =
        true;

    [SerializeField]
    [Tooltip("斷繩後的 GrappleAirborne Momentum 期間，是否允許繼續累積能量。")]
    private bool chargeWhileReleaseMomentum =
        true;

    [Header("充能設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("開始累積能量所需的最低世界速度。")]
    private float requiredChargeSpeed =
        100f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("持續符合充能條件時，從零充滿所需秒數。")]
    private float fullChargeDuration =
        10f;

    [Header("衰退設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("低於此速度時視為完全停止，改用快速衰退。")]
    private float stationarySpeedThreshold =
        0.1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("完全停止時，從滿能量衰退到零所需秒數。")]
    private float fastDecayDuration =
        2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("仍在移動時，從滿能量衰退到零所需秒數。")]
    private float slowDecayDuration =
        6f;

    [Header("傷害設定")]

    [SerializeField]
    [Min(1f)]
    [Tooltip("滿能量時，武器與近戰的最高傷害倍率。")]
    private float maximumDamageMultiplier =
        2f;

    [Networked]
    public float NormalizedEnergy
    {
        get;
        private set;
    }

    public float EnergyPercent =>
        Mathf.Clamp01(NormalizedEnergy) *
        100f;

    public float DamageMultiplier =>
        Mathf.Lerp(
            1f,
            Mathf.Max(1f, maximumDamageMultiplier),
            Mathf.Clamp01(NormalizedEnergy)
        );

    public float CurrentWorldSpeed =>
    movement != null
        ? Mathf.Max(
            0f,
            movement.CurrentWorldSpeed
        )
        : 0f;

    public float RequiredChargeSpeed =>
        Mathf.Max(
            0f,
            requiredChargeSpeed
        );

    public float StationarySpeedThreshold =>
        Mathf.Max(
            0f,
            stationarySpeedThreshold
        );

    public float FullChargeDuration =>
        Mathf.Max(
            0f,
            fullChargeDuration
        );

    public float FastDecayDuration =>
        Mathf.Max(
            0f,
            fastDecayDuration
        );

    public float SlowDecayDuration =>
        Mathf.Max(
            0f,
            slowDecayDuration
        );

    public bool ChargeWhileGrappleAttached =>
        chargeWhileGrappleAttached;

    public bool ChargeWhileReleaseMomentum =>
        chargeWhileReleaseMomentum;

    public bool IsAttachedMovementActive =>
        grapple != null &&
        grapple.IsNormalPlayerPullAttached;

    public bool IsReleaseMomentumMovementActive =>
        grapple != null &&
        grapple.IsReleaseMomentumActive;

    public bool HasEligibleChargeSource =>
        (chargeWhileGrappleAttached &&
        IsAttachedMovementActive) ||
        (chargeWhileReleaseMomentum &&
        IsReleaseMomentumMovementActive);

    public bool IsChargingNow =>
        HasEligibleChargeSource &&
        CurrentWorldSpeed >=
            RequiredChargeSpeed;

    public bool IsFastDecayingNow =>
        IsChargingNow == false &&
        CurrentWorldSpeed <=
            StationarySpeedThreshold;
            
    private PlayerMovement movement;
    private PlayerGrapple grapple;

    private void Awake()
    {
        movement =
            GetComponent<PlayerMovement>();

        grapple =
            GetComponent<PlayerGrapple>();
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            NormalizedEnergy =
                0f;
        }
    }

    public void TickSimulation()
    {
        if (isActiveAndEnabled == false ||
            Runner == null ||
            movement == null ||
            grapple == null)
        {
            return;
        }

        float currentSpeed =
            CurrentWorldSpeed;

        bool shouldCharge =
            IsChargingNow;

        if (shouldCharge)
        {
            if (fullChargeDuration <= 0f)
            {
                NormalizedEnergy =
                    1f;
            }
            else
            {
                NormalizedEnergy =
                    Mathf.Clamp01(
                        NormalizedEnergy +
                        Runner.DeltaTime /
                        fullChargeDuration
                    );
            }

            return;
        }

        float decayDuration =
            currentSpeed <=
                Mathf.Max(0f, stationarySpeedThreshold)
                ? fastDecayDuration
                : slowDecayDuration;

        if (decayDuration <= 0f)
        {
            NormalizedEnergy =
                0f;

            return;
        }

        NormalizedEnergy =
            Mathf.Clamp01(
                NormalizedEnergy -
                Runner.DeltaTime /
                decayDuration
            );
    }

    public void ModifyOutgoingDamage(
        ref DamageRequest request
    )
    {
        if (isActiveAndEnabled == false)
            return;

        bool supportedDamageType =
            request.DamageType ==
                DamageType.Bullet ||
            request.DamageType ==
                DamageType.Melee;

        if (supportedDamageType == false ||
            request.RequestedDamage <= 0f)
        {
            return;
        }

        request.RequestedDamage *=
            DamageMultiplier;
    }

    public void OnOutgoingDamageResolved(
        in DamageResult result
    )
    {
        if (result.Accepted == false ||
            result.KilledTarget == false)
        {
            return;
        }

        HandleTargetKilled(
            result
        );
    }

    private void HandleTargetKilled(
        in DamageResult result
    )
    {
        /*
         * 擊殺減少技能冷卻的預留入口。
         *
         * 目前刻意不呼叫：
         * Player.RestoreGrappleChargeFromKill()
         *
         * 也不修改既有 Grapple Charge／Cooldown。
         */
    }
}