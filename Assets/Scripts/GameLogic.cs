using Fusion;
using System;
using System.Collections.Generic;
using Purgers.Progression;
using UnityEngine;

public struct NetworkPlayerExperience : INetworkStruct
{
    public int Level;
    public int Experience;
    public int PendingRewards;

    public NetworkPlayerExperience(PlayerExperienceState state)
    {
        Level = state.Level;
        Experience = state.Experience;
        PendingRewards = state.PendingRewards;
    }

    public PlayerExperienceState ToState() =>
        new PlayerExperienceState(Level, Experience, PendingRewards);
}


/// <summary>
/// 遊戲中的玩家 NetworkObject 生命週期管理器。
///
/// ====================================================================
///
/// State Authority 負責：
///
/// 1. 玩家加入時生成 Player NetworkObject。
/// 2. 將 PlayerRef 與 Player NetworkObject 正式綁定。
/// 3. 訂閱 PlayerHealth.Died。
/// 4. 玩家死亡後安全 Despawn 舊 Player。
/// 5. 等待完整 Respawn Delay。
/// 6. 從 Spawn Point 陣列選擇下一個出生點。
/// 7. 重新 Spawn 一個全新的 Player NetworkObject。
///
/// ====================================================================
///
/// 為什麼重生要由 GameLogic 負責：
///
/// 玩家死亡後整個 Player NetworkObject 都會被 Despawn，
/// 因此 Respawn Timer 不能放在即將被移除的 Player 身上。
///
/// GameLogic 是持續存在的 NetworkObject，
/// 可以在玩家物件消失的三秒期間繼續保存 Respawn Timer。
/// </summary>
[DisallowMultipleComponent]
public class GameLogic :
    NetworkBehaviour,
    IPlayerJoined,
    IPlayerLeft,
    ISceneLoadDone
{
    private static readonly Dictionary<NetworkRunner, GameLogic>
        primaryByRunner =
            new Dictionary<NetworkRunner, GameLogic>();

    /// <summary>
    /// True only for the persistent GameLogic that owns this Runner.
    /// Scene replacements exist briefly so the primary can adopt their
    /// spawn configuration before Fusion despawns them.
    /// </summary>
    public bool IsPrimaryForRunner =>
        Runner != null &&
        primaryByRunner.TryGetValue(Runner, out GameLogic primary) &&
        primary == this;

    public static GameLogic GetPrimaryForRunner(NetworkRunner runner) =>
        runner != null &&
        primaryByRunner.TryGetValue(runner, out GameLogic primary)
            ? primary
            : null;

    // =====================================================================
    #region Player Prefab

    [Header("玩家生成 預置物")]

    [SerializeField]
    [Tooltip(
        "玩家加入或死亡重生時，由 State Authority 使用 Runner.Spawn 生成的 Player Network Prefab。" +
        "此 Prefab Root 必須包含 NetworkObject、Player 與 PlayerHealth。")]
    private NetworkPrefabRef playerPrefab;

    #endregion

    // =====================================================================
    #region Spawn Points

    [Header("玩家出生點")]

    [SerializeField]
    [Tooltip(
        "玩家第一次進入與死亡重生時可使用的出生點陣列。\n\n" +
        "系統會由 State Authority 依陣列順序輪流選擇非 Null 的 Transform，" +
        "讓多名玩家不會每次都疊在同一個位置。\n\n" +
        "Transform 的 Position 是出生位置；Rotation 是玩家出生朝向。")]
    private Transform[] playerSpawnPoints;

    [SerializeField]
    [Tooltip(
        "當 Spawn Point 陣列為空，或陣列內所有元素都是 Null 時使用的安全備援出生位置。" +
        "正常正式場景應完整設定 Spawn Point；此欄位只用來避免設定錯誤時完全無法生成玩家。")]
    private Vector3 fallbackSpawnPosition =
        Vector3.up;



    [Header("地圖入口抽選原型")]
    [SerializeField]
    [Tooltip(
        "可選的開發測試入口來源。由 Host 套用至所有玩家首次出生；" +
        "死亡重生仍使用原出生點。留空、停用或設定無效時沿用既有流程。")]
    private MapRunSelectionPrototype initialSpawnPrototype;

    #endregion

    // =====================================================================
    #region Respawn Settings

    [Header("玩家死亡重生")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "玩家死亡並完成 Despawn 後，需要等待幾秒才生成新的 Player NetworkObject。\n\n" +
        "Timer 由持續存在的 GameLogic 保存，不會因舊 Player 被 Despawn 而消失。\n" +
        "目前規則使用完整 3 秒。")]
    private float respawnDelay =
        3f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip(
        "開啟後輸出玩家加入、死亡排程、Despawn、Respawn Timer 與重新生成位置。" +
        "多人測試完成後可以關閉。")]
    private bool debugPlayerLifecycle =
        true;

    [Header("玩家升級獎勵")]
    [SerializeField]
    [Tooltip("正式獎勵清單。留空時讀取 Resources/Progression/PlayerRewardCatalog。")]
    private PlayerRewardCatalog rewardCatalog;

    #endregion

    // =====================================================================
    #region Networked Player State

    /// <summary>
    /// 目前場上仍存在的 Player NetworkBehaviour。
    ///
    /// 玩家死亡並準備 Despawn 時會先從這裡移除；
    /// 重生成功後才加入新的 Player 實例。
    /// </summary>
    [Networked, Capacity(12)]
    private NetworkDictionary<PlayerRef, Player>
        Players => default;

    /// <summary>
    /// 已死亡、目前正在等待重生的 PlayerRef 與 Timer。
    ///
    /// Timer 必須放在 GameLogic，而不是舊 Player，
    /// 因為舊 Player 會在死亡時被 Despawn。
    /// </summary>
    [Networked, Capacity(12)]
    private NetworkDictionary<PlayerRef, TickTimer>
        RespawnTimers => default;

    /// <summary>
    /// 每名連線玩家在 Player NetworkObject 之外持續保存的正式職業。
    ///
    /// 死亡 Despawn 前會記錄目前 Attack／Tank／Support；
    /// 新 Player 生成時會在 OnBeforeSpawned 階段還原。
    ///
    /// 不能只依賴 Player Prefab 的 Default Profession，
    /// 否則玩家切換職業後死亡，重生會錯誤回到 Prefab 預設職業。
    /// </summary>
    [Networked, Capacity(12)]
    private NetworkDictionary<PlayerRef, PlayerProfessionType>
        SavedProfessions => default;

    /// <summary>
    /// State Authority-owned run experience, independent of the disposable
    /// Player NetworkObject and retained by the persistent GameLogic.
    /// </summary>
    [Networked, Capacity(12)]
    private NetworkDictionary<PlayerRef, NetworkPlayerExperience>
        PlayerExperiences => default;

    [Networked, Capacity(12)]
    private NetworkDictionary<PlayerRef, PlayerRewardNetworkState>
        PlayerRewardDrafts => default;

    /// <summary>
    /// 下一次嘗試使用的 Spawn Point Index。
    ///
    /// State Authority 每次成功取得一個出生點後向後輪替，
    /// 避免所有玩家永遠使用陣列第一個位置。
    /// </summary>
    [Networked]
    private int NextSpawnPointIndex
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region Local Authority Runtime State

    /// <summary>
    /// State Authority 本機保存的 PlayerHealth 訂閱。
    ///
    /// C# Event 本身不是 Networked State，
    /// 因此不需要放進 NetworkDictionary。
    /// </summary>
    private readonly Dictionary<PlayerRef, PlayerHealth>
        subscribedHealthByPlayer =
            new Dictionary<PlayerRef, PlayerHealth>();

    /// <summary>
    /// 每名玩家真正加入 PlayerHealth.Died 的 Callback。
    ///
    /// 必須保存 Callback 實例，
    /// 才能在 Despawn 或 PlayerLeft 時正確取消訂閱。
    /// </summary>
    private readonly Dictionary<PlayerRef, Action>
        deathCallbackByPlayer =
            new Dictionary<PlayerRef, Action>();

    /// <summary>
    /// PlayerHealth.Died 觸發時先放入此集合。
    ///
    /// 不在 PlayerHealth.ReceiveDamage() 的呼叫堆疊中立刻 Despawn 自己，
    /// 避免仍在執行該 Player NetworkBehaviour 時移除整個 NetworkObject。
    /// </summary>
    private readonly HashSet<PlayerRef>
        pendingDeathPlayers =
            new HashSet<PlayerRef>();

    /// <summary>
    /// 迭代 Pending Death 時使用的安全 Buffer。
    /// </summary>
    private readonly List<PlayerRef>
        pendingDeathBuffer =
            new List<PlayerRef>(12);

    /// <summary>
    /// 迭代 NetworkDictionary 時不能直接移除元素，
    /// 因此先把到期的 PlayerRef 收進這個 Buffer。
    /// </summary>
    private readonly List<PlayerRef>
        expiredRespawnBuffer =
            new List<PlayerRef>(12);

    /// <summary>
    /// 關卡結果提交後到新場景完成載入前，暫停死亡與重生排程。
    /// 新場景完成後由 SceneLoadDone 清除，並補回缺少的玩家。
    /// </summary>
    private bool suppressRespawnForSceneTransition;

    private readonly Dictionary<PlayerRef, HashSet<string>>
        acquiredRewardIdsByPlayer =
            new Dictionary<PlayerRef, HashSet<string>>();

    private readonly Dictionary<PlayerRef, Player>
        restoredRewardLoadoutByPlayer =
            new Dictionary<PlayerRef, Player>();

    private readonly HashSet<PlayerRef> missingRewardDraftLogged =
        new HashSet<PlayerRef>();

    #endregion

    // =====================================================================
    #region Fusion Lifecycle

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPrimaryRegistry()
    {
        primaryByRunner.Clear();
    }

    public override void Spawned()
    {
        if (primaryByRunner.TryGetValue(
                Runner,
                out GameLogic existingPrimary) &&
            existingPrimary != null &&
            existingPrimary != this)
        {
            if (Object.HasStateAuthority)
            {
                existingPrimary.AdoptSceneConfigurationFrom(this);
                Runner.Despawn(Object);
            }

            return;
        }

        primaryByRunner[Runner] = this;
        ResolveRewardCatalog();
        // Fusion Multi-Peer attaches scene objects under a scene root on Client.
        // MakeDontDestroyOnLoad requires a root (or its own persistence-root child).
        transform.SetParent(null, true);
        Runner.MakeDontDestroyOnLoad(gameObject);

        if (Object.HasStateAuthority == false)
        {
            return;
        }

        ReconcileConnectedPlayers();

        /*
         * 一般流程中，PlayerJoined() 會在每個玩家加入時建立訂閱。
         *
         * 這裡額外掃描一次已存在的 Players，
         * 作為 GameLogic 重新 Spawn 或 Authority 狀態重建時的保險。
         */
        foreach (
            KeyValuePair<PlayerRef, Player> pair
                in Players
        )
        {
            Player playerBehaviour =
                pair.Value;

            if (playerBehaviour == null)
            {
                continue;
            }

            SubscribeToPlayerDeath(
                pair.Key,
                playerBehaviour.Health
            );
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority == false)
        {
            return;
        }

        if (suppressRespawnForSceneTransition)
            return;

        ProcessPendingPlayerDeaths();
        ProcessExpiredRespawnTimers();
        RestoreSelectedRewardAbilities();
        InputManager localInput = Runner.GetComponent<InputManager>();
        if (localInput != null && Runner.LocalPlayer.IsRealPlayer &&
            localInput.TryConsumeHostRewardChoice(
                out int rewardRevision, out int rewardChoiceIndex))
            TryClaimRewardStateAuthority(Runner.LocalPlayer,
                rewardRevision, rewardChoiceIndex, out _);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsPrimaryForRunner && localInput != null &&
            Runner.LocalPlayer.IsRealPlayer &&
            localInput.TryConsumeHostDebugLevelUp())
            TryDebugLevelUpStateAuthority(Runner.LocalPlayer);
#endif
        EnsurePendingRewardDrafts();
    }

    public void SceneLoadDone(in SceneLoadDoneArgs sceneInfo)
    {
        if (Object.HasStateAuthority == false ||
            !primaryByRunner.TryGetValue(Runner, out GameLogic primary) ||
            primary != this)
        {
            return;
        }

        /*
         * 場景切換會移除舊場景中的 Player NetworkObject，但連線中的
         * PlayerRef 仍然存在。等新場景物件完成 Spawn、出生設定也完成
         * 接管後，再補齊缺少的 Player，避免使用上一個場景的出生點。
         */
        bool refillGrappleEnergy = suppressRespawnForSceneTransition;
        suppressRespawnForSceneTransition = false;
        ReconcileConnectedPlayers();
        // 只在正式關卡／安全屋轉場補滿，不能因額外載入場景或晚加入而替其他玩家補能量。
        if (refillGrappleEnergy)
        {
            foreach (KeyValuePair<PlayerRef, Player> pair in Players)
                if (pair.Value != null && pair.Value.GrappleCharges != null)
                    pair.Value.GrappleCharges.RefillForSceneTransitionStateAuthority();
        }
    }

    /// <summary>
    /// 只供 State Authority 的正式場景流程在已提交結果、即將切場時呼叫。
    /// 清除舊場景的死亡佇列與重生 Timer，避免在載入安全屋期間生成 Player。
    /// </summary>
    public void PrepareForAuthoritativeSceneTransition()
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        suppressRespawnForSceneTransition = true;
        pendingDeathPlayers.Clear();
        pendingDeathBuffer.Clear();
        expiredRespawnBuffer.Clear();
        restoredRewardLoadoutByPlayer.Clear();
        RespawnTimers.Clear();
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        if (primaryByRunner.TryGetValue(
                runner,
                out GameLogic primary) &&
            primary == this)
        {
            primaryByRunner.Remove(runner);
        }

        UnsubscribeAllPlayerDeaths();

        pendingDeathPlayers.Clear();
        pendingDeathBuffer.Clear();
        expiredRespawnBuffer.Clear();
        acquiredRewardIdsByPlayer.Clear();
        restoredRewardLoadoutByPlayer.Clear();
        missingRewardDraftLogged.Clear();
    }

    #endregion

    // =====================================================================
    #region Player Join / Leave

    public void PlayerJoined(
        PlayerRef player
    )
    {
        if (Object.HasStateAuthority == false)
        {
            return;
        }

        EnsurePlayerExperience(player);

        if (TryAdoptExistingPlayer(player))
            return;

        /*
         * 同一 PlayerRef 若仍殘留 Respawn Timer，
         * 先清除再進行正式初次生成。
         */
        RespawnTimers.Remove(
            player
        );

        SavedProfessions.Remove(
            player
        );

        SpawnPlayer(
            player,
            isRespawn: false
        );
    }

    public void PlayerLeft(
        PlayerRef player
    )
    {
        if (Object.HasStateAuthority == false)
        {
            return;
        }

        pendingDeathPlayers.Remove(
            player
        );

        RespawnTimers.Remove(
            player
        );

        // 玩家已離開 Session，不再保留其死亡前職業資料。
        SavedProfessions.Remove(
            player
        );

        PlayerExperiences.Remove(player);
        PlayerRewardDrafts.Remove(player);
        acquiredRewardIdsByPlayer.Remove(player);
        restoredRewardLoadoutByPlayer.Remove(player);
        missingRewardDraftLogged.Remove(player);

        UnsubscribeFromPlayerDeath(
            player
        );

        if (Players.TryGet(
                player,
                out Player playerBehaviour
            ))
        {
            Players.Remove(
                player
            );

            /*
             * 清除 Runner 的 Player Object 關聯，
             * 避免 HUD 或其他查詢系統取得即將 Despawn 的舊物件。
             */
            Runner.SetPlayerObject(
                player,
                null
            );

            NetworkObject playerObject =
                playerBehaviour != null
                    ? playerBehaviour.Object
                    : null;

            if (playerObject != null &&
                playerObject.IsValid)
            {
                Runner.Despawn(
                    playerObject
                );
            }
        }

        if (debugPlayerLifecycle)
        {
            Debug.Log(
                $"[GameLogic] 玩家已離開，不再重生。" +
                $"\nPlayer：{player}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Player Spawn

    private void AdoptSceneConfigurationFrom(GameLogic sceneGameLogic)
    {
        if (sceneGameLogic == null || sceneGameLogic == this)
            return;

        playerSpawnPoints = sceneGameLogic.playerSpawnPoints != null
            ? (Transform[])sceneGameLogic.playerSpawnPoints.Clone()
            : Array.Empty<Transform>();
        fallbackSpawnPosition = sceneGameLogic.fallbackSpawnPosition;
        initialSpawnPrototype = sceneGameLogic.initialSpawnPrototype;
        if (sceneGameLogic.rewardCatalog != null)
            rewardCatalog = sceneGameLogic.rewardCatalog;

        if (debugPlayerLifecycle)
        {
            Debug.Log(
                $"[GameLogic] 已接管場景 '{sceneGameLogic.gameObject.scene.name}' 的出生設定。",
                this);
        }
    }

    private void ReconcileConnectedPlayers()
    {
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            EnsurePlayerExperience(player);
            if (!TryAdoptExistingPlayer(player) &&
                !RespawnTimers.ContainsKey(player))
            {
                SpawnPlayer(player, isRespawn: false);
            }
        }
    }

    public bool TryGetPlayerExperience(
        PlayerRef player,
        out PlayerExperienceState state)
    {
        state = default;
        if (Object == null || !Object.IsValid ||
            !PlayerExperiences.TryGet(player, out NetworkPlayerExperience value))
            return false;

        state = value.ToState();
        return true;
    }

    /// <summary>Called only by State Authority after validating a grant.</summary>
    public bool TryAwardPlayerExperience(PlayerRef player, int amount)
    {
        return TryAwardPlayerExperienceCore(player, amount, applyAbilityMultiplier: true);
    }

    private bool TryAwardPlayerExperienceCore(PlayerRef player, int amount, bool applyAbilityMultiplier)
    {
        if (Object == null || !Object.HasStateAuthority || amount <= 0 ||
            !Players.TryGet(player, out Player playerBehaviour) ||
            playerBehaviour == null || playerBehaviour.Health == null ||
            !playerBehaviour.Health.IsAlive ||
            !PlayerExperiences.TryGet(player, out NetworkPlayerExperience value))
            return false;

        if (applyAbilityMultiplier)
        {
            IPlayerExperienceMultiplier modifier =
                PlayerAbilityQualification.GetModule<IPlayerExperienceMultiplier>(playerBehaviour);
            amount = PlayerAbilityExperienceRules.ApplyMultiplier(amount,
                modifier != null ? modifier.ExperienceMultiplier : 1f);
        }

        PlayerExperienceState before = value.ToState();
        PlayerExperienceState after = PlayerExperienceRules.Award(before, amount);
        if (after.Level == before.Level &&
            after.Experience == before.Experience &&
            after.PendingRewards == before.PendingRewards)
            return false;

        PlayerExperiences.Set(player, new NetworkPlayerExperience(after));
        // 升級結果已由 State Authority 提交，同步套用鈎索新上限及 Inspector 補充方式。
        if (playerBehaviour.GrappleCharges != null)
            playerBehaviour.GrappleCharges.SynchronizeLevelStateAuthority();
        if (after.PendingRewards > 0)
            EnsureRewardDraft(player);
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Developer-only request; the Host still owns the XP grant.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable)]
    public void RPC_RequestDebugLevelUp(RpcInfo info = default)
    {
        if (info.Source != PlayerRef.None)
            TryDebugLevelUpStateAuthority(info.Source);
    }

    private bool TryDebugLevelUpStateAuthority(PlayerRef player)
    {
        if (!DevelopmentToolsPolicy.IsEnabled || !Application.isPlaying ||
            Object == null || !Object.HasStateAuthority ||
            !IsPrimaryForRunner ||
            !TryGetPlayerExperience(player, out PlayerExperienceState current))
            return false;

        int amount = DevelopmentLevelUpRules.ExperienceToNextLevel(current);
        return amount > 0 && TryAwardPlayerExperienceCore(player, amount, applyAbilityMultiplier: false);
    }
#endif

    public bool TryGetPlayerRewardDraft(
        PlayerRef player, out PlayerRewardNetworkState draft)
    {
        draft = default;
        return Object != null && Object.IsValid &&
            PlayerRewardDrafts.TryGet(player, out draft);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable)]
    public void RPC_RequestRewardChoice(
        int draftRevision, int choiceIndex, RpcInfo info = default)
    {
        if (info.Source != PlayerRef.None)
            TryClaimRewardStateAuthority(
                info.Source, draftRevision, choiceIndex, out _);
    }

    public bool TryClaimRewardStateAuthority(
        PlayerRef player, int draftRevision, int choiceIndex,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (Object == null || !Object.HasStateAuthority ||
            !IsPrimaryForRunner ||
            !PlayerExperiences.TryGet(player, out NetworkPlayerExperience xp) ||
            xp.PendingRewards <= 0 ||
            !PlayerRewardDrafts.TryGet(player, out PlayerRewardNetworkState draft) ||
            draft.DraftRevision != draftRevision)
        {
            failureReason = "待選獎勵不存在或選單已更新。";
            return false;
        }

        string rewardId = draft.GetChoice(choiceIndex);
        if (string.IsNullOrEmpty(rewardId) ||
            rewardCatalog == null ||
            !rewardCatalog.TryFind(rewardId, out PlayerRewardDefinition reward) ||
            !reward.IsImplementedAbilityReward ||
            !Players.TryGet(player, out Player playerBehaviour) ||
            playerBehaviour == null ||
            playerBehaviour.AbilityRuntimeManager == null)
        {
            failureReason = "獎勵無效或玩家目前沒有可套用的能力管理器。";
            return false;
        }

        // 候選建立後仍可能換武器；失敗不扣待選數、不改候選或取得紀錄。
        if (!PlayerAbilityQualification.IsAllowed(playerBehaviour, reward.AbilityDefinition))
        {
            failureReason = "目前武器不符合此技能的領取條件；待選獎勵仍保留。";
            return false;
        }

        if (!playerBehaviour.AbilityRuntimeManager
            .TryEquipRewardAbilityStateAuthority(
                reward.AbilityDefinition, out failureReason))
            return false;

        if (reward.Category == RewardCategory.GrappleHit)
            draft.EquippedGrappleHitId = rewardId;
        else
            draft.EquippedGrappleFocusId = rewardId;

        if (!acquiredRewardIdsByPlayer.TryGetValue(player,
                out HashSet<string> acquired))
        {
            acquired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            acquiredRewardIdsByPlayer[player] = acquired;
        }
        acquired.Add(rewardId);

        PlayerExperiences.Set(player,
            new NetworkPlayerExperience(
                PlayerExperienceRules.ClaimReward(xp.ToState())));
        draft.SetChoices(null);
        PlayerRewardDrafts.Set(player, draft);
        EnsureRewardDraft(player);
        restoredRewardLoadoutByPlayer[player] = playerBehaviour;
        return true;
    }

    /// <summary>
    /// Called once per confirmed enemy death by the enemy's State Authority.
    /// Each living connected player gets the base amount; the killer gets
    /// the small bonus. Pending rewards are handled per player by Award.
    /// </summary>
    public int AwardEnemyKillExperience(PlayerRef killer, int baseExperience)
    {
        if (Object == null || !Object.HasStateAuthority ||
            !IsPrimaryForRunner || baseExperience <= 0 ||
            !PlayerExperiences.ContainsKey(killer))
            return 0;

        bool killerConnected = false;
        foreach (PlayerRef activePlayer in Runner.ActivePlayers)
        {
            if (activePlayer == killer)
            {
                killerConnected = true;
                break;
            }
        }

        if (!killerConnected)
            return 0;

        int awardedPlayers = 0;
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            bool alive = Players.TryGet(player, out Player playerBehaviour) &&
                playerBehaviour != null &&
                playerBehaviour.Health != null &&
                playerBehaviour.Health.IsAlive;

            int amount = PlayerExperienceRules.KillGrantForPlayer(
                baseExperience,
                hasPlayerKiller: true,
                isConnected: true,
                isAlive: alive,
                isKiller: player == killer);
            if (TryAwardPlayerExperience(player, amount))
                awardedPlayers++;
        }

        return awardedPlayers;
    }

    /// <summary>
    /// 子彈時間正式致死事件的替代發放路徑。只處理施放快照內、同一 PlayerObject
    /// 仍連線存活者；待選者保留自己的份額但沿用 Award 拒收，不轉送給其他玩家。
    /// </summary>
    public int AwardSharedEnemyKillExperience(BulletTimeExperienceContext context, int baseExperience)
    {
        if (Object == null || !Object.HasStateAuthority || !IsPrimaryForRunner ||
            context == null || context.Runner != Runner || baseExperience <= 0) return 0;
        var connected = new HashSet<PlayerRef>();
        foreach (PlayerRef player in Runner.ActivePlayers) connected.Add(player);
        var recipients = new Dictionary<int, PlayerRef>();
        for (int i = 0; i < context.ParticipantCount; i++)
        {
            BulletTimeParticipant snapshot = context.GetParticipant(i);
            if (!connected.Contains(snapshot.Player) ||
                !Players.TryGet(snapshot.Player, out Player current) || current == null ||
                current.Object == null || !current.Object.IsValid ||
                current.Object.Id != snapshot.PlayerObjectId || current.Health == null ||
                !current.Health.IsAlive || !PlayerExperiences.ContainsKey(snapshot.Player)) continue;
            recipients[snapshot.Player.RawEncoded] = snapshot.Player;
        }
        var shares = PlayerAbilityExperienceRules.CreateEqualShares(baseExperience,
            PlayerExperienceRules.DefaultKillerBonus, new List<int>(recipients.Keys));
        int awardedPlayers = 0;
        foreach (KeyValuePair<int, int> share in shares)
            if (share.Value > 0 && TryAwardPlayerExperience(recipients[share.Key], share.Value)) awardedPlayers++;
        return awardedPlayers;
    }

    /// <summary>Copies only the Host player's progress into the Host save.</summary>
    public bool TryWriteHostExperienceToSave(GameSaveData save)
    {
        if (Object == null || !Object.HasStateAuthority || save == null ||
            !TryGetPlayerExperience(Runner.LocalPlayer, out PlayerExperienceState state) ||
            !PlayerRewardDrafts.TryGet(Runner.LocalPlayer,
                out PlayerRewardNetworkState draft))
            return false;

        PlayerExperienceSaveBridge.WriteHost(save, state);
        acquiredRewardIdsByPlayer.TryGetValue(Runner.LocalPlayer,
            out HashSet<string> acquired);
        string[] acquiredIds = acquired != null
            ? new List<string>(acquired).ToArray()
            : Array.Empty<string>();
        Array.Sort(acquiredIds, StringComparer.Ordinal);
        var choices = new string[draft.ChoiceCount];
        for (int i = 0; i < choices.Length; i++)
            choices[i] = draft.GetChoice(i);
        PlayerRewardSaveBridge.WriteHost(save,
            new PlayerRewardSaveSnapshot(
                draft.EquippedGrappleHitId.ToString(),
                draft.EquippedGrappleFocusId.ToString(),
                choices, acquiredIds));
        return true;
    }

    /// <summary>Only after a failed stage has been committed to disk.</summary>
    public void ResetPlayerExperiencesForFailedRun()
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        var players = new List<PlayerRef>();
        foreach (KeyValuePair<PlayerRef, NetworkPlayerExperience> pair in
            PlayerExperiences)
            players.Add(pair.Key);

        foreach (PlayerRef player in players)
        {
            PlayerExperiences.Set(player,
                new NetworkPlayerExperience(new PlayerExperienceState(1, 0, 0)));
            PlayerRewardDrafts.Set(player, default);
        }
        acquiredRewardIdsByPlayer.Clear();
        restoredRewardLoadoutByPlayer.Clear();
        missingRewardDraftLogged.Clear();
    }

    private void EnsurePlayerExperience(PlayerRef player)
    {
        if (PlayerExperiences.ContainsKey(player))
            return;

        PlayerExperienceState initial = new PlayerExperienceState(1, 0, 0);
        if (player == Runner.LocalPlayer)
        {
            GameSaveRuntimeContext context =
                Runner.GetComponent<GameSaveRuntimeContext>();
            if (context != null && context.HasWritableHostSave)
                initial = PlayerExperienceSaveBridge.ReadHost(context.ActiveSave);
        }

        PlayerExperiences.Set(player, new NetworkPlayerExperience(initial));

        PlayerRewardSaveSnapshot rewards =
            new PlayerRewardSaveSnapshot(null, null, null, null);
        if (player == Runner.LocalPlayer)
        {
            GameSaveRuntimeContext context =
                Runner.GetComponent<GameSaveRuntimeContext>();
            if (context != null && context.HasWritableHostSave)
                rewards = PlayerRewardSaveBridge.ReadHost(context.ActiveSave);
        }

        var rewardState = new PlayerRewardNetworkState
        {
            EquippedGrappleHitId = rewards.EquippedGrappleHitId,
            EquippedGrappleFocusId = rewards.EquippedGrappleFocusId
        };
        rewardState.SetChoices(rewards.PendingCandidateIds);
        PlayerRewardDrafts.Set(player, rewardState);
        acquiredRewardIdsByPlayer[player] = new HashSet<string>(
            rewards.AcquiredRewardIds, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsurePendingRewardDrafts()
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        foreach (KeyValuePair<PlayerRef, NetworkPlayerExperience> pair in
            PlayerExperiences)
        {
            if (pair.Value.PendingRewards > 0)
                EnsureRewardDraft(pair.Key);
        }
    }

    private void ResolveRewardCatalog()
    {
        if (rewardCatalog == null)
            rewardCatalog = Resources.Load<PlayerRewardCatalog>(
                "Progression/PlayerRewardCatalog");

        if (rewardCatalog == null && Object != null && Object.HasStateAuthority)
            Debug.LogError("[GameLogic] 找不到 PlayerRewardCatalog；無法產生升級候選。", this);
    }

    private void EnsureRewardDraft(PlayerRef player)
    {
        if (Object == null || !Object.HasStateAuthority ||
            !PlayerExperiences.TryGet(player, out NetworkPlayerExperience xp) ||
            xp.PendingRewards <= 0 ||
            !PlayerRewardDrafts.TryGet(player, out PlayerRewardNetworkState draft) ||
            draft.ChoiceCount > 0 ||
            !Players.TryGet(player, out Player playerBehaviour) ||
            playerBehaviour == null ||
            playerBehaviour.AbilityRuntimeManager == null ||
            playerBehaviour.AbilityRuntimeManager.LoadoutRevision <= 0)
            return;

        ResolveRewardCatalog();
        if (rewardCatalog == null)
            return;
        if (!rewardCatalog.TryBuildDraftPool(out RewardDraftEntry[] pool,
                out string error))
        {
            if (missingRewardDraftLogged.Add(player))
                Debug.LogError($"[GameLogic] 獎勵清單無效：{error}", this);
            return;
        }

        acquiredRewardIdsByPlayer.TryGetValue(player,
            out HashSet<string> acquired);
        string[] equipped = playerBehaviour.AbilityRuntimeManager
            .GetEquippedAbilityIds();
        // Draw 比較的是 Reward ID，不把 Ability ID 誤當成相同識別碼。
        var equippedSet = new HashSet<string>(equipped, StringComparer.OrdinalIgnoreCase);
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerRewardDefinition reward in rewardCatalog.Rewards)
        {
            if (reward == null || reward.AbilityDefinition == null) continue;
            if (!PlayerAbilityQualification.IsAllowed(playerBehaviour, reward.AbilityDefinition) ||
                equippedSet.Contains(reward.AbilityDefinition.AbilityId)) exclusions.Add(reward.StableRewardId);
        }
        int targetLevel = Math.Max(1, xp.Level - xp.PendingRewards + 1);
        ulong seed = RewardDraftSeed(player, targetLevel,
            draft.DraftRevision);
        string[] choices = RewardDraftRules.Draw(pool, targetLevel, seed,
            acquired, exclusions);
        if (choices.Length == 0)
        {
            if (missingRewardDraftLogged.Add(player))
                Debug.LogError("[GameLogic] 沒有可選的獎勵；待選升級仍保留。", this);
            return;
        }

        missingRewardDraftLogged.Remove(player);
        draft.SetChoices(choices);
        PlayerRewardDrafts.Set(player, draft);
    }

    private static ulong RewardDraftSeed(
        PlayerRef player, int level, int revision)
    {
        string key = $"{player}:{level}:{revision}";
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < key.Length; i++)
        {
            hash ^= key[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private void RestoreSelectedRewardAbilities()
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        foreach (KeyValuePair<PlayerRef, Player> pair in Players)
        {
            Player playerBehaviour = pair.Value;
            if (playerBehaviour == null ||
                restoredRewardLoadoutByPlayer.TryGetValue(pair.Key,
                    out Player restored) && restored == playerBehaviour ||
                !PlayerRewardDrafts.TryGet(pair.Key,
                    out PlayerRewardNetworkState draft))
                continue;

            PlayerAbilityRuntimeManager manager =
                playerBehaviour.AbilityRuntimeManager;
            if (manager == null || manager.LoadoutRevision <= 0)
                continue;

            if (!TryRestoreRewardAbility(manager,
                    draft.EquippedGrappleHitId.ToString()) ||
                !TryRestoreRewardAbility(manager,
                    draft.EquippedGrappleFocusId.ToString()))
                continue;

            restoredRewardLoadoutByPlayer[pair.Key] = playerBehaviour;
        }
    }

    private bool TryRestoreRewardAbility(
        PlayerAbilityRuntimeManager manager, string rewardId)
    {
        if (string.IsNullOrEmpty(rewardId))
            return true;
        if (rewardCatalog == null ||
            !rewardCatalog.TryFind(rewardId,
                out PlayerRewardDefinition reward) ||
            !reward.IsImplementedAbilityReward)
            return false;
        return manager.TryEquipRewardAbilityStateAuthority(
            reward.AbilityDefinition, out _);
    }

    private bool TryAdoptExistingPlayer(PlayerRef player)
    {
        if (!Runner.TryGetPlayerObject(
                player,
                out NetworkObject playerObject) ||
            playerObject == null ||
            !playerObject.IsValid)
        {
            return false;
        }

        Player playerBehaviour = playerObject.GetComponent<Player>();
        if (playerBehaviour == null)
            return false;

        Players.Set(player, playerBehaviour);
        SubscribeToPlayerDeath(player, playerBehaviour.Health);
        SavePlayerProfession(player, playerBehaviour.Profession);
        return true;
    }

    /// <summary>
    /// 由 State Authority 生成一個新的 Player NetworkObject。
    ///
    /// 初次加入與死亡重生都統一走這個入口，
    /// 避免兩條 Spawn 流程日後設定不同步。
    /// </summary>
    private void SpawnPlayer(
        PlayerRef player,
        bool isRespawn
    )
    {
        if (Object.HasStateAuthority == false)
        {
            return;
        }

        if (Players.TryGet(
                player,
                out Player existingPlayer
            ))
        {
            if (existingPlayer != null &&
                existingPlayer.Object != null &&
                existingPlayer.Object.IsValid)
            {
                if (debugPlayerLifecycle)
                {
                    Debug.LogWarning(
                        $"[GameLogic] PlayerRef 已有有效 Player，忽略重複 Spawn。" +
                        $"\nPlayer：{player}",
                        this
                    );
                }

                return;
            }

            // Dictionary 中只有失效參考時，先移除再重建。
            Players.Remove(
                player
            );
        }

        Vector3 spawnPosition;
        Quaternion spawnRotation;
        int spawnPointIndex;

        if (initialSpawnPrototype != null &&
        initialSpawnPrototype.gameObject.scene == gameObject.scene &&
        initialSpawnPrototype.TryGetInitialSpawnPose(
            Runner,
            out Pose entryPose
        ))
        {
            spawnPosition = entryPose.position;
            spawnRotation = entryPose.rotation;

            // -2 代表使用本輪固定抽中的入口。
            spawnPointIndex = -2;
        }
        else
        {
            ResolveNextSpawnPose(
                out spawnPosition,
                out spawnRotation,
                out spawnPointIndex
            );
        }

        PlayerProfessionType professionToRestore =
            PlayerProfessionType.None;

        SavedProfessions.TryGet(
            player,
            out professionToRestore
        );

        NetworkObject playerObject =
            Runner.Spawn(
                playerPrefab,
                spawnPosition,
                spawnRotation,
                player,

                /*
                 * 新 Player 真正 Spawned 以前先還原死亡前職業。
                 *
                 * 如此 PlayerProfession.Spawned() 與
                 * PlayerProfessionRuntimeManager.Spawned()
                 * 第一次讀取時就能取得正確職業，
                 * 不會先建立錯誤 Runtime 再切換一次。
                 */
                (runner, spawnedObject) =>
                {
                    if (IsValidProfession(
                            professionToRestore
                        ) == false)
                    {
                        // 初次加入還沒有保存職業，讓 Prefab 使用原本預設值。
                        return;
                    }

                    PlayerProfession spawnedProfession =
                        spawnedObject.GetComponent<PlayerProfession>();

                    if (spawnedProfession == null)
                    {
                        return;
                    }

                    spawnedProfession.InitializeBeforeSpawn(
                        professionToRestore
                    );
                }
            );

        if (playerObject == null)
        {
            Debug.LogError(
                $"[GameLogic] Runner.Spawn 沒有產生 Player NetworkObject。" +
                $"\nPlayer：{player}",
                this
            );

            return;
        }

        Player playerBehaviour =
            playerObject.GetComponent<Player>();

        if (playerBehaviour == null)
        {
            Debug.LogError(
                "[GameLogic] Player Prefab Root 找不到 Player 元件，" +
                "本次生成物件會立即 Despawn。",
                playerObject
            );

            Runner.Despawn(
                playerObject
            );

            return;
        }

        /*
         * 正式保存目前場上的 Player。
         */
        Players.Add(
            player,
            playerBehaviour
        );

        /*
         * 將 PlayerRef 與 NetworkObject 綁定到 Fusion Runner。
         *
         * 後續本地 HUD 可以使用：
         * Runner.TryGetPlayerObject(Runner.LocalPlayer, out ...)
         *
         * 在死亡等待三秒期間此關聯會被清除；
         * 重生後再指向全新的 Player Object。
         */
        Runner.SetPlayerObject(
            player,
            playerObject
        );

        SubscribeToPlayerDeath(
            player,
            playerBehaviour.Health
        );

        /*
         * 初次加入時 professionToRestore 會是 None，
         * PlayerProfession.Spawned() 已改用 Prefab 預設職業。
         *
         * Spawn 完成後把真正結果保存下來，
         * 之後死亡便能正確還原。
         */
        SavePlayerProfession(
            player,
            playerBehaviour.Profession
        );

        if (debugPlayerLifecycle)
        {
            Debug.Log(
                $"[GameLogic] 玩家已{(isRespawn ? "重生" : "生成")}。" +
                $"\nPlayer：{player}" +
                $"\nSpawn Point Index：{spawnPointIndex}" +
                $"\nPosition：{spawnPosition}" +
                $"\nRotation：{spawnRotation.eulerAngles}" +
                $"\nHealth：{playerBehaviour.Health?.CurrentHealth:F1}",
                playerObject
            );
        }
    }

    /// <summary>
    /// 從 Inspector Spawn Point 陣列依序輪流取得下一個有效出生點。
    ///
    /// Null 元素會被跳過；陣列無有效元素時使用 Fallback。
    /// </summary>
    private void ResolveNextSpawnPose(
        out Vector3 position,
        out Quaternion rotation,
        out int spawnPointIndex
    )
    {
        position =
            fallbackSpawnPosition;

        rotation =
            Quaternion.identity;

        spawnPointIndex =
            -1;

        if (playerSpawnPoints == null ||
            playerSpawnPoints.Length <= 0)
        {
            LogMissingSpawnPointFallback();
            return;
        }

        int spawnPointCount =
            playerSpawnPoints.Length;

        /*
         * 最多檢查陣列長度次數，
         * 避免陣列全部為 Null 時形成無限迴圈。
         */
        for (int attempt = 0;
             attempt < spawnPointCount;
             attempt++)
        {
            int safeCurrentIndex =
                Mathf.Abs(
                    NextSpawnPointIndex
                ) %
                spawnPointCount;

            NextSpawnPointIndex =
                (safeCurrentIndex + 1) %
                spawnPointCount;

            Transform spawnPoint =
                playerSpawnPoints[
                    safeCurrentIndex
                ];

            if (spawnPoint == null)
            {
                continue;
            }

            position =
                spawnPoint.position;

            rotation =
                spawnPoint.rotation;

            spawnPointIndex =
                safeCurrentIndex;

            return;
        }

        LogMissingSpawnPointFallback();
    }

    private void LogMissingSpawnPointFallback()
    {
        if (debugPlayerLifecycle == false)
        {
            return;
        }

        Debug.LogWarning(
            $"[GameLogic] 沒有可用的 Player Spawn Point，" +
            $"本次使用 Fallback Position：{fallbackSpawnPosition}。",
            this
        );
    }

    #endregion

    // =====================================================================
    #region Profession Persistence

    private void SavePlayerProfession(
        PlayerRef player,
        PlayerProfession profession
    )
    {
        if (profession == null)
        {
            return;
        }

        PlayerProfessionType currentProfession =
            profession.CurrentProfession;

        if (IsValidProfession(
                currentProfession
            ) == false)
        {
            return;
        }

        // =========================================================
        // [修正 CS1612]
        // NetworkDictionary 是 Struct，不能對回傳屬性使用 [key] = value。
        // 改用 Fusion 的 .Set()，它會自動處理「有則更新，無則新增」。
        // =========================================================
        SavedProfessions.Set(
            player, 
            currentProfession
        );
    }

    private static bool IsValidProfession(
        PlayerProfessionType profession
    )
    {
        return
            profession == PlayerProfessionType.Attack ||
            profession == PlayerProfessionType.Tank ||
            profession == PlayerProfessionType.Support;
    }

    #endregion

    // =====================================================================
    #region Player Death Subscription

    private void SubscribeToPlayerDeath(
        PlayerRef player,
        PlayerHealth health
    )
    {
        UnsubscribeFromPlayerDeath(
            player
        );

        if (health == null)
        {
            Debug.LogError(
                $"[GameLogic] Player 找不到 PlayerHealth，無法建立死亡重生訂閱。" +
                $"\nPlayer：{player}",
                this
            );

            return;
        }

        /*
         * PlayerHealth.Died 是無參數 Action，
         * 因此使用 Closure 保存這次對應的 PlayerRef 與 Health 實例。
         *
         * Callback 本身會存進 Dictionary，
         * 確保之後能使用完全相同的 Delegate 取消訂閱。
         */
        Action deathCallback =
            () => QueuePlayerDeath(
                player,
                health
            );

        health.Died +=
            deathCallback;

        subscribedHealthByPlayer[player] =
            health;

        deathCallbackByPlayer[player] =
            deathCallback;
    }

    private void UnsubscribeFromPlayerDeath(
        PlayerRef player
    )
    {
        if (subscribedHealthByPlayer.TryGetValue(
                player,
                out PlayerHealth health
            ) &&
            deathCallbackByPlayer.TryGetValue(
                player,
                out Action callback
            ))
        {
            if (health != null)
            {
                health.Died -=
                    callback;
            }
        }

        subscribedHealthByPlayer.Remove(
            player
        );

        deathCallbackByPlayer.Remove(
            player
        );
    }

    private void UnsubscribeAllPlayerDeaths()
    {
        foreach (
            KeyValuePair<PlayerRef, PlayerHealth> pair
                in subscribedHealthByPlayer
        )
        {
            if (pair.Value == null)
            {
                continue;
            }

            if (deathCallbackByPlayer.TryGetValue(
                    pair.Key,
                    out Action callback
                ))
            {
                pair.Value.Died -=
                    callback;
            }
        }

        subscribedHealthByPlayer.Clear();
        deathCallbackByPlayer.Clear();
    }

    /// <summary>
    /// PlayerHealth.Died 的 Callback。
    ///
    /// 這裡只驗證並排入佇列，不立即 Despawn。
    /// </summary>
    private void QueuePlayerDeath(
        PlayerRef player,
        PlayerHealth sourceHealth
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        if (Players.TryGet(
                player,
                out Player currentPlayer
            ) == false ||
            currentPlayer == null ||
            currentPlayer.Health != sourceHealth)
        {
            // 舊 Player 或失效訂閱送來的 Event，不進入重生流程。
            return;
        }

        pendingDeathPlayers.Add(
            player
        );

        if (debugPlayerLifecycle)
        {
            Debug.Log(
                $"[GameLogic] 收到 PlayerHealth.Died，已排入下一個 Fusion Tick 處理。" +
                $"\nPlayer：{player}",
                sourceHealth
            );
        }
    }

    #endregion

    // =====================================================================
    #region Death / Despawn

    private void ProcessPendingPlayerDeaths()
    {
        if (pendingDeathPlayers.Count <= 0)
        {
            return;
        }

        pendingDeathBuffer.Clear();

        foreach (
            PlayerRef player
                in pendingDeathPlayers
        )
        {
            pendingDeathBuffer.Add(
                player
            );
        }

        pendingDeathPlayers.Clear();

        for (int i = 0;
             i < pendingDeathBuffer.Count;
             i++)
        {
            BeginPlayerRespawnWait(
                pendingDeathBuffer[i]
            );
        }

        pendingDeathBuffer.Clear();
    }

    /// <summary>
    /// 正式把死亡 Player 從場上移除，並建立三秒 Respawn Timer。
    /// </summary>
    private void BeginPlayerRespawnWait(
        PlayerRef player
    )
    {
        if (Players.TryGet(
                player,
                out Player playerBehaviour
            ) == false)
        {
            return;
        }

        /*
         * Player NetworkObject 即將消失，
         * 先把目前正式職業保存到 GameLogic。
         */
        SavePlayerProfession(
            player,
            playerBehaviour != null
                ? playerBehaviour.Profession
                : null
        );

        UnsubscribeFromPlayerDeath(
            player
        );

        Players.Remove(
            player
        );

        Runner.SetPlayerObject(
            player,
            null
        );

        NetworkObject playerObject =
            playerBehaviour != null
                ? playerBehaviour.Object
                : null;

        if (playerObject != null &&
            playerObject.IsValid)
        {
            Runner.Despawn(
                playerObject
            );
        }

        /*
         * 如果玩家已在死亡處理前離線，
         * 不應再替該 PlayerRef 建立重生排程。
         */
        if (IsPlayerStillActive(
                player
            ) == false)
        {
            return;
        }

        float safeRespawnDelay =
            Mathf.Max(
                0f,
                respawnDelay
            );

        if (safeRespawnDelay <= 0f)
        {
            SpawnPlayer(
                player,
                isRespawn: true
            );

            return;
        }

        TickTimer timer =
            TickTimer.CreateFromSeconds(
                Runner,
                safeRespawnDelay
            );

        // =========================================================
        // [修正 CS1612]
        // 取代原本的 RespawnTimers[player] = timer 與 Add() 判斷。
        // =========================================================
        RespawnTimers.Set(
            player, 
            timer
        );

        if (debugPlayerLifecycle)
        {
            Debug.Log(
                $"[GameLogic] 死亡 Player 已 Despawn，開始等待重生。" +
                $"\nPlayer：{player}" +
                $"\nRespawn Delay：{safeRespawnDelay:F2} 秒",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Respawn Timer

    private void ProcessExpiredRespawnTimers()
    {
        if (RespawnTimers.Count <= 0)
        {
            return;
        }

        expiredRespawnBuffer.Clear();

        foreach (
            KeyValuePair<PlayerRef, TickTimer> pair
                in RespawnTimers
        )
        {
            if (pair.Value.ExpiredOrNotRunning(
                    Runner
                ))
            {
                expiredRespawnBuffer.Add(
                    pair.Key
                );
            }
        }

        /*
         * 先離開 foreach，才修改 NetworkDictionary。
         */
        for (int i = 0;
             i < expiredRespawnBuffer.Count;
             i++)
        {
            PlayerRef player =
                expiredRespawnBuffer[i];

            RespawnTimers.Remove(
                player
            );

            if (IsPlayerStillActive(
                    player
                ) == false)
            {
                continue;
            }

            SpawnPlayer(
                player,
                isRespawn: true
            );
        }

        expiredRespawnBuffer.Clear();
    }

    /// <summary>
    /// 確認 PlayerRef 仍然存在於 Runner.ActivePlayers。
    ///
    /// 不使用額外 LINQ Allocation，直接走訪目前玩家。
    /// </summary>
    private bool IsPlayerStillActive(
        PlayerRef targetPlayer
    )
    {
        foreach (
            PlayerRef activePlayer
                in Runner.ActivePlayers
        )
        {
            if (activePlayer == targetPlayer)
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
