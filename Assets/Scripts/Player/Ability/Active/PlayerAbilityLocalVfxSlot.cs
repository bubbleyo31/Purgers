using System;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>可選的純本機特效接口；輸入僅是已確認的呈現資料，不可用來決定傷害。</summary>
public interface IPlayerAbilityLocalVfxReceiver
{
    void ConfigureAbilityVfx(Vector3 origin, Vector3 endPosition, float radius, float halfAngleDegrees);
}

/// <summary>技能特效插槽。物件不掛在玩家／投射物底下，技能卸下後仍可正常播完並自動清除。</summary>
[Serializable]
public sealed class PlayerAbilityLocalVfxSlot
{
    [SerializeField, Tooltip("可選本機特效 Prefab。不得包含 NetworkObject、Collider 或 Rigidbody；留空使用原有示意呈現。Prefab 的 +Z 為向前，可用 ParticleSystem／Animator；可實作 IPlayerAbilityLocalVfxReceiver 取得射線終點及範圍。")]
    private GameObject prefab;
    [SerializeField, Tooltip("可選定位子物件。取它相對技能／投射物 Root 的位移與旋轉，套在確認發射的位置及方向；不更改攻擊起點或命中範圍。留空使用確認位置。")]
    private Transform anchor;
    [SerializeField, Min(0.05f), Tooltip("本機特效存活秒數，預設 2 秒；應涵蓋特效播放長度。與技能傷害、冷卻及投射物網路存活時間無關。")]
    private float lifetimeSeconds = 2f;

    public bool HasValidPrefab => prefab != null && IsVisualOnly(prefab);

    public bool TryPlay(Transform source, Vector3 origin, Quaternion rotation,
        Vector3 endPosition, float radius = 0f, float halfAngleDegrees = 0f)
    {
        if (source == null || !HasValidPrefab) return false;
        Vector3 position = origin;
        Quaternion orientation = rotation;
        if (anchor != null && anchor != source && anchor.IsChildOf(source))
        {
            position += rotation * source.InverseTransformPoint(anchor.position);
            orientation *= Quaternion.Inverse(source.rotation) * anchor.rotation;
        }
        var instance = UnityEngine.Object.Instantiate(prefab, position, orientation);
        instance.name = prefab.name + " (Ability Local VFX)";
        instance.hideFlags = HideFlags.DontSave;
        if (source.gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(instance, source.gameObject.scene);
        foreach (var receiver in instance.GetComponentsInChildren<MonoBehaviour>(true))
            if (receiver is IPlayerAbilityLocalVfxReceiver visual)
                visual.ConfigureAbilityVfx(origin, endPosition, radius, halfAngleDegrees);
        UnityEngine.Object.Destroy(instance, Mathf.Max(0.05f, lifetimeSeconds));
        return true;
    }

    /// <summary>外觀不能增加本機物理實體或註冊另一個網路物件。</summary>
    public static bool IsVisualOnly(GameObject visual)
    {
        return visual != null && visual.GetComponentInChildren<NetworkObject>(true) == null &&
            visual.GetComponentInChildren<Collider>(true) == null && visual.GetComponentInChildren<Rigidbody>(true) == null &&
            visual.GetComponentInChildren<Collider2D>(true) == null && visual.GetComponentInChildren<Rigidbody2D>(true) == null;
    }
}
