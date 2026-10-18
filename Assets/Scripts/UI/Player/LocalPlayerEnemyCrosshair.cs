using Fusion;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 所有武器共用的本地敵人準心顏色；
/// 不改鈎索資格，也不作為衝刺／傷害授權。
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalPlayerEnemyCrosshair : MonoBehaviour
{
    [Header("共用敵人準心")]
    [SerializeField, Tooltip(
        "可指定多個要變色的準心 Graphic，例如 Center、外圈等；" +
        "未指定有效項目時，取同物件 Graphic。")]
    private Graphic[] crosshairs = new Graphic[0];

    [SerializeField, Tooltip(
        "本地玩家所屬 Runner；可留空自動尋找，Multi Peer 時請指定對應 Runner。")]
    private NetworkRunner runner;

    [SerializeField, Tooltip(
        "用於畫面中心射線的世界相機，不要填 ViewModel 相機；留空使用 Camera.main。")]
    private Camera aimCamera;

    [SerializeField, Tooltip(
        "敵人的戰鬥 Hitbox Collider Layer，預設 Hit Mask。")]
    private LayerMask enemyHitMask = 1 << 9;

    [SerializeField, Tooltip(
        "可遮擋瞄準的世界／角色 Layer；預設排除 UI 與第一人稱模型。" +
        "牆必須包含，避免隔牆變紅。")]
    private LayerMask obstructionMask =
        Physics.DefaultRaycastLayers & ~((1 << 5) | (1 << 7) | (1 << 8));

    [SerializeField, Min(.1f), Tooltip(
        "準心偵測距離，公尺；獨立於 Tank 衝刺距離，" +
        "不表示目前武器能命中或衝刺。")]
    private float maximumDistance = 100f;

    [SerializeField, Tooltip(
        "直接瞄到存活敵人時的顏色；保留每張準心原始透明度。")]
    private Color enemyColor = Color.red;

    private Graphic[] activeCrosshairs;
    private Color[] originalColors;
    private RaycastHit[] hits = new RaycastHit[16];

    private void OnEnable()
    {
        bool hasAssignedGraphic = false;

        if (crosshairs != null)
        {
            foreach (var graphic in crosshairs)
            {
                if (graphic != null)
                {
                    hasAssignedGraphic = true;
                    break;
                }
            }
        }

        // 固定本次啟用使用的清單，確保圖片與原始顏色一一對應。
        activeCrosshairs = hasAssignedGraphic
            ? (Graphic[])crosshairs.Clone()
            : new Graphic[] { GetComponent<Graphic>() };

        originalColors = new Color[activeCrosshairs.Length];

        for (int i = 0; i < activeCrosshairs.Length; i++)
        {
            if (activeCrosshairs[i] != null)
                originalColors[i] = activeCrosshairs[i].color;
        }
    }

    private void LateUpdate()
    {
        bool aimed = false;

        if (runner == null || !runner.IsRunning)
        {
            runner = null;

            foreach (var candidate in NetworkRunner.Instances)
            {
                if (candidate.IsRunning &&
                    candidate.LocalPlayer.IsRealPlayer)
                {
                    runner = candidate;
                    break;
                }
            }
        }

        if (aimCamera == null)
            aimCamera = Camera.main;

        if (runner != null &&
            aimCamera != null &&
            LocalPlayerSpectatorController.TryGetViewedPlayerObject(runner, out var playerObject) &&
            playerObject != null &&
            playerObject.IsValid)
        {
            var player = playerObject.GetComponent<Player>();

            Ray ray = aimCamera.ViewportPointToRay(
                new Vector3(.5f, .5f, 0f));

            aimed = PlayerEnemyAimQuery.TryGetLocal(
                player,
                ray.origin,
                ray.direction,
                maximumDistance,
                enemyHitMask,
                obstructionMask,
                ref hits);
        }

        ApplyTargetState(aimed);
    }

    public void ApplyTargetState(bool aimedAtEnemy)
    {
        if (activeCrosshairs == null || originalColors == null)
            return;

        for (int i = 0; i < activeCrosshairs.Length; i++)
        {
            var graphic = activeCrosshairs[i];

            if (graphic == null)
                continue;

            Color color = aimedAtEnemy
                ? enemyColor
                : originalColors[i];

            color.a = originalColors[i].a;
            graphic.color = color;
        }
    }

    private void OnDisable()
    {
        ApplyTargetState(false);
    }
}