using Fusion;
using UnityEngine;

/// <summary>
/// 鈎索使用能量的唯一持有者。保留舊類名及元件 GUID，避免破壞玩家 Prefab 引用。
/// 上限讀取主要 GameLogic 的玩家等級；Host 初始化／升級補充，既有玩家 Simulation 預測消耗。
/// 不控制鈎索物理，不處理左下動能增傷，也不自然恢復或擊殺回充。
/// </summary>
[DisallowMultipleComponent]
public class PlayerGrappleCharges : NetworkBehaviour
{
    // 舊 Prefab 的序列化引用保留供相容；能量不再讀取職業設定。
    [SerializeField, HideInInspector] private PlayerProfession playerProfession;

    [Header("玩家等級與鈎索能量")]
    [SerializeField, Min(0f), Tooltip("1 級的最大鈎索能量，單位：點；有效值至少 0。預設 50，0 代表 1 級無法發射。生成及正式切場時補滿。")]
    private float levelOneEnergy = 50f;
    [SerializeField, Min(0f), Tooltip("每提升 1 級增加的最大能量，單位：點／級；有效值至少 0。上限＝1 級能量＋(等級－1)×此值。預設 25，因此 2 級 75、3 級 100。")]
    private float energyPerLevel = 25f;
    [SerializeField, Tooltip("升級增加上限時的補充方式：補滿至新上限，或只增加新舊上限差額。預設補滿；切換不會立即補充，下一次容量增加才生效。Host 設定決定正式結果。")]
    private GrappleEnergyLevelUpMode levelUpMode = GrappleEnergyLevelUpMode.RefillToMaximum;

    [Header("鈎索消耗")]
    [SerializeField, Min(0f), Tooltip("每次確認合法鈎點並正式發射所扣的能量，單位：點；有效值至少 0，預設 1。打空不扣。必須足額且仍有能量才能發射；0 只免除發射費。")]
    private float launchEnergyCost = 1f;
    [SerializeField, Min(0f), Tooltip("鈎索尚未釋放且實際拉動玩家自己時，每秒扣除的能量；有效值至少 0，預設 1。按 Fusion DeltaTime 連續扣除。跳躍、釋放後 Momentum、拉其他目標不扣；歸零不會中斷本次拉動。")]
    private float pullEnergyPerSecond = 1f;

    [Header("除錯設定")]
    [SerializeField, Tooltip("輸出初始化、升級補充與發射消耗；不逐 Tick 輸出拉動扣點。")]
    private bool debugCharges = true;

    [Networked] public float CurrentEnergy { get; private set; }
    [Networked] public float MaximumEnergy { get; private set; }
    [Networked] public int AppliedPlayerLevel { get; private set; }
    [Networked] private NetworkBool IsInitialized { get; set; }

    private bool hasSpawned;
    private bool HasValidState => hasSpawned && Object != null && Object.IsValid && Runner != null;
    private bool CanSimulate => HasValidState && (Object.HasStateAuthority || Object.HasInputAuthority);
    public bool HasCharge => HasValidState && IsInitialized && GrappleEnergyRules.CanLaunch(CurrentEnergy, LaunchEnergyCost);
    public float NormalizedEnergy => HasValidState && IsInitialized && MaximumEnergy > 0f
        ? Mathf.Clamp01(CurrentEnergy / MaximumEnergy) : 0f;
    public float LaunchEnergyCost => Mathf.Max(0f, launchEnergyCost);
    public float PullEnergyPerSecond => Mathf.Max(0f, pullEnergyPerSecond);
    public GrappleEnergyLevelUpMode LevelUpMode => levelUpMode;

    // 舊公開介面保留為唯讀相容層；正式 HUD 使用浮點能量，避免把尾數截斷。
    public int CurrentCharges => HasValidState ? Mathf.FloorToInt(CurrentEnergy) : 0;
    public int MaxCharges => HasValidState ? Mathf.FloorToInt(MaximumEnergy) : 0;
    public float RechargeDuration => 0f;
    public bool IsRecharging => false;
    public float RechargeRemainingSeconds => 0f;
    public float RechargeProgress => 0f;

    public override void Spawned()
    {
        hasSpawned = true;
        if (!Object.HasStateAuthority) return;
        IsInitialized = false;
        CurrentEnergy = 0f;
        MaximumEnergy = 0f;
        AppliedPlayerLevel = 0;
        SynchronizeLevelStateAuthority();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        hasSpawned = false;
    }

    /// <summary>
    /// 在 Player 取輸入之前由 Host 同步等級，沒有輸入的 Tick 也能處理升級。
    /// 等待正式等級資料，不用臨時的 1 級覆蓋存檔或晚加入狀態。
    /// </summary>
    public void SynchronizeLevelStateAuthority()
    {
        if (!HasValidState || !Object.HasStateAuthority) return;
        GameLogic logic = GameLogic.GetPrimaryForRunner(Runner);
        if (logic == null || !logic.TryGetPlayerExperience(Object.InputAuthority, out var experience)) return;

        int level = Mathf.Max(1, experience.Level);
        float maximum = GrappleEnergyRules.MaximumEnergyForLevel(level, levelOneEnergy, energyPerLevel);
        if (IsInitialized && AppliedPlayerLevel == level && MaximumEnergy == maximum) return;

        CurrentEnergy = IsInitialized
            ? GrappleEnergyRules.ApplyCapacityIncrease(CurrentEnergy, MaximumEnergy, maximum,
                levelUpMode == GrappleEnergyLevelUpMode.RefillToMaximum)
            : maximum;
        MaximumEnergy = maximum;
        AppliedPlayerLevel = level;
        IsInitialized = true;
        if (debugCharges) Debug.Log($"[鈎索能量] 等級 {level}：{CurrentEnergy:F2}/{MaximumEnergy:F2}，升級補充：{levelUpMode}", this);
    }

    /// <summary>合法鈎點確認後沿用原本出鈎交易入口；Client 只做本機預測，Host 校正正式結果。</summary>
    public bool ConsumeCharge()
    {
        if (!CanSimulate || !IsInitialized ||
            !GrappleEnergyRules.TryConsumeLaunch(CurrentEnergy, LaunchEnergyCost, out float remaining)) return false;
        CurrentEnergy = remaining;
        if (debugCharges) Debug.Log($"[鈎索能量] 發射後：{CurrentEnergy:F2}/{MaximumEnergy:F2}", this);
        return true;
    }

    /// <summary>只由 PlayerGrapple 實際完成普通拉動的 Tick 呼叫；不以能量歸零停止該次拉動。</summary>
    public void ConsumePullEnergy(float deltaTime)
    {
        if (!CanSimulate || !IsInitialized) return;
        CurrentEnergy = GrappleEnergyRules.ConsumePull(CurrentEnergy, PullEnergyPerSecond, deltaTime);
    }

    /// <summary>主要 GameLogic 完成正式切場後呼叫，包含沿用原 Player 的場景流程。</summary>
    public void RefillForSceneTransitionStateAuthority()
    {
        if (!HasValidState || !Object.HasStateAuthority) return;
        SynchronizeLevelStateAuthority();
        if (IsInitialized) CurrentEnergy = MaximumEnergy;
    }

    /// <summary>相容入口只同步等級；已停用自然回充。</summary>
    public void TickRecharge() => SynchronizeLevelStateAuthority();

    /// <summary>保留明確手動補能量入口；沒有自然／擊殺呼叫方，只有 State Authority 可寫入。</summary>
    public void RestoreCharge(int amount)
    {
        if (!HasValidState || !Object.HasStateAuthority || !IsInitialized || amount <= 0) return;
        CurrentEnergy = Mathf.Clamp(CurrentEnergy + amount, 0f, MaximumEnergy);
    }

    /// <summary>擊殺回充暫停；保留舊入口避免既有呼叫方或 UnityEvent 引用中斷。</summary>
    public void RestoreChargeFromKill() { }
}
