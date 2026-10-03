using Fusion;
using UnityEngine;

/// <summary>
/// Player Root 的命中視覺廣播器。只有 State Authority 的正式命中可發送；
/// 每個接收端保存該射手自己的 FIFO，切職業不清除，玩家 Despawn／切場清除。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerWeaponImpactEffects : NetworkBehaviour
{
    [Header("共用命中特效設定")]
    [SerializeField, Tooltip("指定所有玩家共用的 WeaponImpactSettings。掛在 KCC_Player 根物件，不要掛在會隨切職業銷毀的 Runtime。未指定時不廣播也不顯示。")]
    private WeaponImpactSettings settings;

    private bool spawned;
    private bool receivedAny;
    private uint lastReceived;
    private uint nextSequence;
    private NetworkId lastWeapon;
    private int lastShot;
    private WeaponImpactVisuals visuals;
    private int visualScene;
    private WeaponImpactSettings visualSettings;

    public override void Spawned()
    {
        spawned = true;
        receivedAny = false;
        nextSequence = 0;
        lastWeapon = default;
    }

    /// <summary>由兩把槍的權威 ProcessHit 呼叫；與傷害是否被接收／治療是否滿血無關。</summary>
    public void PublishConfirmedHit(NetworkObject weapon, int shotSequence, GameObject hitObject,
        Vector3 point, Vector3 normal)
    {
        if (!spawned || !isActiveAndEnabled || Object == null || !Object.IsValid ||
            !Object.HasStateAuthority || weapon == null || !weapon.IsValid || !weapon.HasStateAuthority ||
            weapon.Runner != Runner || settings == null || hitObject == null ||
            !WeaponImpactVisuals.ValidPoint(point) || !WeaponImpactVisuals.ValidPoint(normal) ||
            normal.sqrMagnitude < 0.0001f) return;
        byte effects = settings.GetEffectsForLayer(hitObject.layer);
        if (effects == 0) return;
        if (lastWeapon == weapon.Id && unchecked(shotSequence - lastShot) <= 0) return;
        lastWeapon = weapon.Id;
        lastShot = shotSequence;
        normal.Normalize();

        NetworkId anchorId = default;
        Vector3 localPoint = point;
        Vector3 localNormal = normal;
        var anchor = hitObject.GetComponentInParent<NetworkObject>();
        if (anchor == Object) return;
        if (anchor != null && anchor.IsValid && anchor.Runner == Runner)
        {
            anchorId = anchor.Id;
            localPoint = anchor.transform.InverseTransformPoint(point);
            localNormal = anchor.transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
        }
        uint sequence = ++nextSequence;
        // 私有決定性取樣，不消耗 Gameplay 的 UnityEngine.Random 狀態。
        uint hash = unchecked(sequence * 747796405u + 2891336453u);
        int variant = settings.VariantCount > 0 ? (int)(hash % (uint)settings.VariantCount) : -1;
        float angle = ((hash >> 8) % 3600) * 0.1f;
        RPC_PresentImpact(sequence, point, normal, anchorId, localPoint, localNormal,
            hitObject.layer, effects, variant, angle);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable,
        TickAligned = false, InvokeLocal = true)]
    private void RPC_PresentImpact(uint sequence, Vector3 point, Vector3 normal, NetworkId anchorId,
        Vector3 localPoint, Vector3 localNormal, int hitLayer, byte effects, int variant, float angle)
    {
        if (!spawned || !isActiveAndEnabled || settings == null || Runner == null) return;
        if (receivedAny && unchecked((int)(sequence - lastReceived)) <= 0) return;
        receivedAny = true;
        lastReceived = sequence;
        effects &= settings.GetEffectsForLayer(hitLayer);
        if (effects == 0) return;

        Transform surface = null;
        if (anchorId.IsValid)
        {
            if (Runner.TryFindObject(anchorId, out NetworkObject anchor) && anchor != null && anchor.IsValid)
            {
                surface = anchor.transform;
                point = surface.TransformPoint(localPoint);
                normal = surface.worldToLocalMatrix.transpose.MultiplyVector(localNormal).normalized;
            }
        }
        else
        {
            // 只辨識本機同 Runner 的靜態表面，不重算射擊、不更改權威命中結果。
            var physics = Runner.GetPhysicsScene();
            if (physics.IsValid() && hitLayer >= 0 && hitLayer <= 31 &&
                physics.Raycast(point + normal * 0.04f, -normal, out RaycastHit hit, 0.08f,
                    1 << hitLayer, QueryTriggerInteraction.Ignore))
                surface = hit.collider.transform;
        }
        EnsureVisuals();
        visuals.Present(point, normal, surface, effects, variant, angle);
    }

    private void EnsureVisuals()
    {
        if (visuals != null && (visualScene != gameObject.scene.handle || visualSettings != settings)) ClearVisuals();
        if (visuals != null) return;
        visualScene = gameObject.scene.handle;
        visualSettings = settings;
        visuals = new WeaponImpactVisuals(settings, gameObject.scene);
    }

    private void LateUpdate()
    {
        if (visuals == null) return;
        if (!spawned || Object == null || !Object.IsValid || visualScene != gameObject.scene.handle ||
            visualSettings != settings) { ClearVisuals(); return; }
        visuals.Tick(Time.deltaTime);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        spawned = false;
        ClearVisuals();
    }

    private void OnDisable() => ClearVisuals();
    private void OnDestroy() => ClearVisuals();
    private void ClearVisuals()
    {
        visuals?.Dispose();
        visuals = null;
        visualSettings = null;
    }
}
