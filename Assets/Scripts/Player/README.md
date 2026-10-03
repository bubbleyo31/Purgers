# Assets/Scripts/Player 程式導覽

Phase 4 前置-1：Player 與 PlayerMovement 消費 NetInput.BlockedControls，不用 Host 的本機鎖判斷遠端玩家。PlayerLocalView.IsLocalViewReady 提供轉場完成條件，並補綁較晚建立的 CameraFollow；死亡重生仍由 GameLogic 負責。

玩家移動、職業、能力與第一人稱表現。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [IPlayerIncomingDamageModifier.cs](./IPlayerIncomingDamageModifier.cs) | IPlayerIncomingDamageModifier | `IPlayerIncomingDamageModifier` 的主要實作入口。 |
| [IPlayerMovementInputModifier.cs](./IPlayerMovementInputModifier.cs) | IPlayerMovementInputModifier | `IPlayerMovementInputModifier` 的主要實作入口。 |
| [PlayerGrapple.cs](./PlayerGrapple.cs) | GrapplePhase, GrappleLockedLateralIntent, GrappleCancelReason, PlayerGrapple | `PlayerGrapple` 的主要實作入口。 |
| [PlayerGrappleCharges.cs](./PlayerGrappleCharges.cs) | PlayerGrappleCharges | `PlayerGrappleCharges` 的主要實作入口。 |
| [PlayerGrappleMomentumEnergy.cs](./PlayerGrappleMomentumEnergy.cs) | PlayerGrappleMomentumEnergy | `PlayerGrappleMomentumEnergy` 的主要實作入口。 |
| [PlayerGrappleVisual.cs](./PlayerGrappleVisual.cs) | PlayerGrappleVisual | `PlayerGrappleVisual` 的主要實作入口。 |
| [PlayerIncomingDamageModifierBridge.cs](./PlayerIncomingDamageModifierBridge.cs) | PlayerIncomingDamageModifierBridge | `PlayerIncomingDamageModifierBridge` 的主要實作入口。 |
| [PlayerLocalView.cs](./PlayerLocalView.cs) | PlayerLocalView | `PlayerLocalView` 的主要實作入口。 |
| [PlayerMovement.cs](./PlayerMovement.cs) | PlayerMovement, FrameResult | `PlayerMovement` 的主要實作入口。 |
| [PlayerSlideController.cs](./PlayerSlideController.cs) | PlayerSlideController | `PlayerSlideController` 的主要實作入口。 |
| [PlayerSprintLatchController.cs](./PlayerSprintLatchController.cs) | PlayerSprintLatchController | `PlayerSprintLatchController` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


2026-10-02：PlayerGrappleCharges 改為玩家等級制使用能量；GrappleEnergyRules 為純規則。設定與網路生命週期見 [鈎索架構](../../../Documentation/ProjectArchitecture/30_鈎索系統.md)。
