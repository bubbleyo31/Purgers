using UnityEngine;

/// <summary>
/// 鈎索動能系統的遊戲內即時驗證面板。
///
/// 僅顯示本機 Input Authority 玩家。
/// 正式非 Development Build 不顯示。
/// 不需要 Canvas。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(
    typeof(PlayerGrappleMomentumEnergy)
)]
public sealed class
    PlayerGrappleMomentumEnergyDebugHUD :
    MonoBehaviour
{
    [Header("顯示設定")]

    [SerializeField]
    private bool showDebugHUD =
        true;

    [SerializeField]
    private Vector2 panelPosition =
        new Vector2(20f, 20f);

    [SerializeField]
    [Min(280f)]
    private float panelWidth =
        390f;

    [SerializeField]
    [Min(160f)]
    private float panelHeight =
        205f;

    [Header("傷害換算驗證")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "只用於 HUD 預覽，不會改變任何實際武器傷害。"
    )]
    private float previewBaseDamage =
        100f;

    private PlayerGrappleMomentumEnergy
        energySystem;

    private GUIStyle titleStyle;
    private GUIStyle centeredBarStyle;

    private void Awake()
    {
        energySystem =
            GetComponent<
                PlayerGrappleMomentumEnergy
            >();
    }

    private void OnGUI()
    {
        if (!DevelopmentToolsPolicy.IsEnabled ||
            showDebugHUD == false ||
            energySystem == null ||
            energySystem.Object == null ||
            energySystem.Object.HasInputAuthority ==
                false)
        {
            return;
        }

        EnsureStyles();

        Rect panelRect =
            new Rect(
                panelPosition.x,
                panelPosition.y,
                panelWidth,
                panelHeight
            );

        GUILayout.BeginArea(
            panelRect,
            GUI.skin.box
        );

        GUILayout.Label(
            "Grapple Momentum Energy",
            titleStyle
        );

        DrawEnergyBar();

        float multiplier =
            energySystem.DamageMultiplier;

        float previewDamage =
            Mathf.Max(
                0f,
                previewBaseDamage
            ) *
            multiplier;

        GUILayout.Label(
            $"速度：{energySystem.CurrentWorldSpeed:0.00}" +
            $" / 充能門檻：{energySystem.RequiredChargeSpeed:0.00}"
        );

        GUILayout.Label(
            $"來源：{ResolveSourceLabel()}"
        );

        GUILayout.Label(
            $"流程：{ResolveFlowLabel()}"
        );

        GUILayout.Label(
            $"倍率：×{multiplier:0.000}" +
            $"　增傷：+{(multiplier - 1f) * 100f:0.0}%"
        );

        GUILayout.Label(
            $"傷害換算預覽：{previewBaseDamage:0.##}" +
            $" → {previewDamage:0.##}"
        );

        GUILayout.EndArea();
    }

    private void DrawEnergyBar()
    {
        float normalizedEnergy =
            Mathf.Clamp01(
                energySystem.NormalizedEnergy
            );

        Rect barRect =
            GUILayoutUtility.GetRect(
                panelWidth - 24f,
                24f
            );

        GUI.Box(
            barRect,
            GUIContent.none
        );

        Rect fillRect =
            new Rect(
                barRect.x + 2f,
                barRect.y + 2f,
                Mathf.Max(
                    0f,
                    barRect.width - 4f
                ) *
                normalizedEnergy,
                Mathf.Max(
                    0f,
                    barRect.height - 4f
                )
            );

        Color previousColor =
            GUI.color;

        GUI.color =
            ResolveFlowColor();

        GUI.DrawTexture(
            fillRect,
            Texture2D.whiteTexture
        );

        GUI.color =
            previousColor;

        GUI.Label(
            barRect,
            $"能量 {energySystem.EnergyPercent:0.0}%",
            centeredBarStyle
        );
    }

    private string ResolveSourceLabel()
    {
        if (energySystem.IsAttachedMovementActive)
        {
            return
                energySystem
                    .ChargeWhileGrappleAttached
                    ? "Attached（允許充能）"
                    : "Attached（充能開關關閉）";
        }

        if (energySystem
            .IsReleaseMomentumMovementActive)
        {
            return
                energySystem
                    .ChargeWhileReleaseMomentum
                    ? "Release Momentum（允許充能）"
                    : "Release Momentum（充能開關關閉）";
        }

        return
            "非鈎索移動";
    }

    private string ResolveFlowLabel()
    {
        if (energySystem.IsChargingNow)
        {
            if (energySystem.FullChargeDuration <=
                0f)
            {
                return
                    "立即充滿";
            }

            return
                $"充能 +" +
                $"{100f / energySystem.FullChargeDuration:0.##}" +
                "%／秒";
        }

        if (energySystem.IsFastDecayingNow)
        {
            if (energySystem.FastDecayDuration <=
                0f)
            {
                return
                    "立即清空";
            }

            return
                $"快速衰退 -" +
                $"{100f / energySystem.FastDecayDuration:0.##}" +
                "%／秒";
        }

        if (energySystem.SlowDecayDuration <=
            0f)
        {
            return
                "立即清空";
        }

        return
            $"慢速衰退 -" +
            $"{100f / energySystem.SlowDecayDuration:0.##}" +
            "%／秒";
    }

    private Color ResolveFlowColor()
    {
        if (energySystem.IsChargingNow)
        {
            return
                new Color(
                    0.15f,
                    0.85f,
                    0.35f,
                    1f
                );
        }

        if (energySystem.IsFastDecayingNow)
        {
            return
                new Color(
                    0.95f,
                    0.25f,
                    0.2f,
                    1f
                );
        }

        return
            new Color(
                1f,
                0.65f,
                0.1f,
                1f
            );
    }

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle =
                new GUIStyle(
                    GUI.skin.label
                )
                {
                    fontStyle =
                        FontStyle.Bold,

                    fontSize =
                        16
                };
        }

        if (centeredBarStyle == null)
        {
            centeredBarStyle =
                new GUIStyle(
                    GUI.skin.label
                )
                {
                    alignment =
                        TextAnchor.MiddleCenter,

                    fontStyle =
                        FontStyle.Bold
                };
        }
    }
}
