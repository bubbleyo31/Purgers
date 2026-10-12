# Assets/Scripts/Player/Profession/Tank 程式導覽

此資料夾集中放置相近責任的遊戲程式。

## 修改入口

- 先由下表找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [TankAirDashAbility.cs](./TankAirDashAbility.cs) | TankAirDashPhase, TankAirDashTargetType, TankAirDashCompletionReason, TankAirDashAbility | `TankAirDashAbility` 的主要實作入口。 |
| [TankGrappleGatherAbility.cs](./TankGrappleGatherAbility.cs) | TankGrappleGatherAbility, GatherCandidate | `TankGrappleGatherAbility` 的主要實作入口。 |
| [TankGuardAbility.cs](./TankGuardAbility.cs) | TankGuardPhase, TankGuardAbility | `TankGuardAbility` 的主要實作入口。 |
| [TankMeleeCombo.cs](./TankMeleeCombo.cs) | TankMeleeComboStep, TankMeleeAttackPhase, TankMeleeAttackDefinition, TankMeleeCombo | `TankMeleeCombo` 的主要實作入口。 |
| [TankProfessionRuntimeDriver.cs](./TankProfessionRuntimeDriver.cs) | TankProfessionRuntimeDriver | `TankProfessionRuntimeDriver` 的主要實作入口。 |
| [TankQuickDashAbility.cs](./TankQuickDashAbility.cs) | TankQuickDashAbility | `TankQuickDashAbility` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


## 三段預備與幀時序（2026-10-10）

`TankMeleePreparationProgress` 保存可重播的前搖／停格與兩種時間；`TankMeleeCombo` 決定出手、Combo 與權威傷害。共用 `AnimationFrameTiming` 只負責幀換算。逐段手動設定及驗證邊界見 [47 Tank 預備攻擊與動畫幀時序](../../../../../Documentation/ProjectArchitecture/47_Tank預備攻擊與動畫幀時序.md)。
