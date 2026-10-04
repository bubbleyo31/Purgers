using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 子彈時間的施放、同步快照及延後命中佇列。生命仍由既有 DamageReceiverUtility 結算，
/// 敵人控制與玩家移動由原 Owner 查詢 Registry，不在此直接寫位置或動畫。
/// </summary>
public sealed class PlayerBulletTimeAbility : PlayerActiveAbilityBase
{
    public const int MaximumAffectedPlayers = 16;
    public const int MaximumAffectedEnemies = 256;

    [Header("子彈時間範圍與時間")]
    [SerializeField, Min(0.01f), Tooltip("子彈時間持續秒數；預設 2 秒。以 Fusion 時間結算，結束後開始冷卻。")]
    private float durationSeconds = 2f;
    [SerializeField, Min(0.01f), Tooltip("發動當下以施放者為中心取樣的球形半徑（公尺）。名單只取樣一次，不追加入場對象；原型預設 12 公尺。最多同步 256 隻敵人，超過時取消該次施放而不截斷名單。")]
    private float effectRadius = 12f;
    [SerializeField, Range(0.01f, 1f), Tooltip("受影響玩家的移動時間倍率；原型預設 0.25。既有移動 Owner 套用，攻擊、轉向與飛行物速度維持原值。")]
    private float playerMovementMultiplier = 0.25f;

    [Header("舊版覆蓋設定（保留序列化，新版不使用）")]
    [SerializeField, Tooltip("舊版色塊欄位，保留相容。新版紫色由 Resources/Presentation/ActiveAbilityColorGrading 的 Lift Gamma Gain 控制。")]
    private Color overlayColor = new Color(0.03f, 0.75f, 0.75f, 1f);
    [SerializeField, Range(0f, 0.6f), Tooltip("舊版透明度，已不影響畫面；新版使用 AbilityColorGradingSettings 的 Bullet Time / Strength。")]
    private float overlayOpacity = 0.18f;
    [SerializeField, Min(0.01f), Tooltip("舊版淡入淡出欄位，已不使用；新版時間由 AbilityColorGradingSettings 的 Fade Seconds 設定。")]
    private float overlayFadeSeconds = 0.25f;

    [Networked] public int AffectedPlayerCount { get; private set; }
    [Networked] public int AffectedEnemyCount { get; private set; }
    [Networked] public Vector3 SnapshotCenter { get; private set; }
    [Networked] private float SessionStartTime { get; set; }
    [Networked] private PlayerRef Caster { get; set; }
    [Networked, Capacity(MaximumAffectedPlayers)]
    private NetworkArray<BulletTimeParticipant> AffectedPlayers => default;
    [Networked, Capacity(MaximumAffectedEnemies)]
    private NetworkArray<NetworkId> AffectedEnemies => default;

    private readonly List<PreparedDamageSnapshot> pendingDamage = new List<PreparedDamageSnapshot>();
    private BulletTimeExperienceContext experienceContext;
    private NetworkRunner sessionRunner;
    private bool spawned;
    private bool hadStateAuthority;
    private bool flushing;

    public bool IsSessionActive => spawned && Object != null && Object.IsValid && Runner != null &&
        Phase == PlayerActiveAbilityPhase.Active && !PhaseTimer.ExpiredOrNotRunning(Runner);
    public float PlayerMovementMultiplier => Mathf.Clamp(playerMovementMultiplier, 0.01f, 1f);
    public Color OverlayColor => overlayColor;
    public float OverlayOpacity => Mathf.Clamp(overlayOpacity, 0f, 0.6f);
    public float OverlayFadeSeconds => Mathf.Max(0.01f, overlayFadeSeconds);
    internal bool CanQueueDamage => IsSessionActive && Object.HasStateAuthority && !flushing;

    public override void Spawned()
    {
        spawned = true;
        sessionRunner = Runner;
        hadStateAuthority = Object.HasStateAuthority;
        PlayerBulletTimeSessionRegistry.Register(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // 先從索引移除，避免裝備替換或離線結算時再次進入同一佇列。
        spawned = false;
        PlayerBulletTimeSessionRegistry.Unregister(this);
        if (hadStateAuthority) FlushPendingDamage();
        pendingDamage.Clear();
        experienceContext = null;
        sessionRunner = null;
        hadStateAuthority = false;
    }

    protected override void Activate()
    {
        if (!Authority) return;
        Vector3 center = Owner.transform.position;
        float radiusSquared = Mathf.Max(0.01f, effectRadius) * Mathf.Max(0.01f, effectRadius);
        var players = new List<BulletTimeParticipant>();
        var seenPlayers = new HashSet<PlayerRef>();
        AddPlayerSnapshot(Owner, center, radiusSquared, players, seenPlayers);
        foreach (PlayerRef playerRef in Runner.ActivePlayers)
            AddPlayerSnapshot(PlayerAbilityQualification.FindPlayer(Runner, playerRef),
                center, radiusSquared, players, seenPlayers);
        players.Sort((a, b) => a.Player.RawEncoded.CompareTo(b.Player.RawEncoded));

        var enemies = new List<NetworkId>();
        foreach (EnemyActor enemy in FindObjectsOfType<EnemyActor>())
        {
            if (enemy == null || !enemy.IsFusionSpawned || enemy.Object == null || !enemy.Object.IsValid ||
                enemy.Runner != Runner || !enemy.IsAlive ||
                (enemy.transform.position - center).sqrMagnitude > radiusSquared) continue;
            enemies.Add(enemy.Object.Id);
        }
        if (players.Count > MaximumAffectedPlayers || enemies.Count > MaximumAffectedEnemies)
        {
            Debug.LogWarning("[子彈時間] 範圍內對象超過同步容量；本次施放取消，沒有截斷影響名單。", this);
            FinishAbility();
            return;
        }

        pendingDamage.Clear();
        flushing = false;
        SnapshotCenter = center;
        SessionStartTime = (float)Runner.SimulationTime;
        Caster = Owner.Object.InputAuthority;
        AffectedPlayerCount = players.Count;
        AffectedEnemyCount = enemies.Count;
        for (int i = 0; i < players.Count; i++) AffectedPlayers.Set(i, players[i]);
        for (int i = 0; i < enemies.Count; i++) AffectedEnemies.Set(i, enemies[i]);
        experienceContext = new BulletTimeExperienceContext(Runner, players);
        BeginActive(durationSeconds);
    }

    private void AddPlayerSnapshot(Player player, Vector3 center, float radiusSquared,
        List<BulletTimeParticipant> snapshot, HashSet<PlayerRef> seen)
    {
        if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != Runner ||
            player.Health == null || !player.Health.IsAlive ||
            (player.transform.position - center).sqrMagnitude > radiusSquared ||
            !seen.Add(player.Object.InputAuthority)) return;
        snapshot.Add(new BulletTimeParticipant { Player = player.Object.InputAuthority,
            PlayerObjectId = player.Object.Id });
    }

    protected override void FinishAbility()
    {
        if (!Authority) return;
        // 先停止效果，正式傷害結算不會再次進入本次 session。
        base.FinishAbility();
        FlushPendingDamage();
        AffectedPlayerCount = 0;
        AffectedEnemyCount = 0;
        experienceContext = null;
    }

    public bool AffectsEnemy(EnemyActor enemy)
    {
        if (!IsSessionActive || enemy == null || !enemy.IsFusionSpawned || enemy.Object == null ||
            !enemy.Object.IsValid || enemy.Runner != Runner) return false;
        for (int i = 0; i < AffectedEnemyCount; i++)
            if (AffectedEnemies[i] == enemy.Object.Id) return true;
        return false;
    }

    public bool AffectsPlayer(Player player)
    {
        if (!IsSessionActive || player == null || player.Object == null || !player.Object.IsValid ||
            player.Runner != Runner) return false;
        for (int i = 0; i < AffectedPlayerCount; i++)
        {
            BulletTimeParticipant entry = AffectedPlayers[i];
            if (entry.Player == player.Object.InputAuthority && entry.PlayerObjectId == player.Object.Id)
                return true;
        }
        return false;
    }

    internal int CompareSessionOrder(PlayerBulletTimeAbility other)
    {
        int time = SessionStartTime.CompareTo(other.SessionStartTime);
        return time != 0 ? time : Caster.RawEncoded.CompareTo(other.Caster.RawEncoded);
    }

    internal bool TryQueueDamage(PreparedDamageSnapshot pending)
    {
        if (!CanQueueDamage || pending == null || pending.IsResolved || pendingDamage.Contains(pending)) return false;
        DamageRequest request = pending.Request;
        request.BulletTimeExperience = experienceContext;
        pending.Request = request;
        pendingDamage.Add(pending);
        return true;
    }

    private void FlushPendingDamage()
    {
        if (flushing || pendingDamage.Count == 0) return;
        if (sessionRunner == null || !sessionRunner.IsRunning)
        {
            pendingDamage.Clear();
            return;
        }
        flushing = true;
        PreparedDamageSnapshot[] captured = pendingDamage.ToArray();
        pendingDamage.Clear();
        try
        {
            foreach (PreparedDamageSnapshot pending in captured)
            {
                if (!DamageReceiverUtility.TryResolvePreparedDamage(pending, out DamageResult result)) continue;
                Player attacker = PlayerAbilityQualification.FindPlayer(sessionRunner, result.Request.Attacker);
                if (attacker != null)
                {
                    var relay = attacker.GetComponent<PlayerCombatFeedbackRelay>();
                    if (relay != null) relay.ReportAbilityDamage(result);
                }
            }
        }
        finally { flushing = false; }
    }

    private void OnDestroy()
    {
        PlayerBulletTimeSessionRegistry.Unregister(this);
        pendingDamage.Clear();
    }
}
