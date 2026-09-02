using Fusion;
using UnityEngine;

/// <summary>
/// 玩家所有需要透過 Photon Fusion 傳送的按鍵。
///
/// 注意：
/// 這些數值一旦開始正式使用後，
/// 不建議任意交換既有編號。
///
/// 未來要新增按鍵時，
/// 繼續往後增加即可。
/// </summary>
public enum InputButton
{
    /// <summary>
    /// 跳躍。
    ///
    /// 地面時：
    /// 一般跳躍。
    ///
    /// 空中時：
    /// 二段跳。
    /// </summary>
    Jump = 0,

    /// <summary>
    /// 勾索。
    ///
    /// 目前綁定：
    /// Q
    ///
    /// 第一次按：
    /// 發射勾索。
    ///
    /// 再次按：
    /// 主動取消勾索。
    /// </summary>
    Grapple = 1,

    /// <summary>
    /// 跑步。
    ///
    /// 目前綁定：
    /// Left Shift
    /// </summary>
    Sprint = 2,

    /// <summary>
    /// 武器射擊。
    ///
    /// 目前綁定：
    /// 滑鼠左鍵。
    ///
    /// 這是一個「按住」型輸入，
/// 自動步槍之後會根據射速決定何時真正射出子彈。
    /// </summary>
    Fire = 3,

    /// <summary>
    /// 武器瞄準。
    ///
    /// 目前綁定：
    /// 滑鼠右鍵。
    ///
    /// 這是一個「按住」型輸入。
    ///
    /// 未來：
/// 一般狀態 → ADS。
/// GrappleAirborne → 專注技能。
    /// </summary>
    Aim = 4,

    /// <summary>
    /// 武器換彈。
    ///
    /// 目前綁定：
    /// R
    /// </summary>
    Reload = 5,

    /// <summary>
    /// 第一主動技能。
    ///
    /// 目前先保留，
    /// 尚未綁定正式鍵位。
    /// </summary>
    Ability1 = 6,

    /// <summary>
    /// 第二主動技能。
    ///
    /// 目前先保留，
    /// 尚未綁定正式鍵位。
    /// </summary>
    Ability2 = 7,

    /*
     * 職業 F 快速行動。
     *
     * Attack → Quick Melee
     * Tank   → Quick Dash
     * Support→ 未來能力
     */
    QuickAction = 8,

    /// <summary>
    /// 玩家蹲下／滑鏟輸入。
    ///
    /// 目前綁定：
    /// Left Control。
    ///
    /// 這是持續按住型輸入：
    /// 按住時保持蹲下，符合速度與地面條件時進入滑鏟。
    /// </summary>
    Crouch = 9
}

/// <summary>
/// Photon Fusion 玩家每個 Tick 使用的網路輸入資料。
///
/// 所有真正會影響遊戲模擬的輸入，
/// 都應該經由這裡傳送。
///
/// 不要讓 AttackRifle、技能等 NetworkBehaviour
/// 自己直接讀 Input.GetMouseButton。
/// </summary>
public struct NetInput : INetworkInput
{
    /// <summary>
    /// 玩家所有按鍵狀態。
    /// </summary>
    public NetworkButtons Buttons;

    /// <summary>
    /// 玩家移動方向。
    ///
    /// X：
/// -1 = 左
/// +1 = 右
///
/// Y：
/// -1 = 後
/// +1 = 前
    /// </summary>
    public Vector2 Direction;

    /// <summary>
    /// 玩家本 Frame 累積的視角旋轉量。
    ///
    /// X：Pitch
/// Y：Yaw
    /// </summary>
    public Vector2 LookDelta;
}