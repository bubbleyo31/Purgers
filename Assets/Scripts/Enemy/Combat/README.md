# Assets/Scripts/Enemy/Combat 程式導覽

敵人資料、AI、戰鬥與呈現。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [EnemyBeamPresenter.cs](./EnemyBeamPresenter.cs) | EnemyBeamPresenter | `EnemyBeamPresenter` 的主要實作入口。 |
| [EnemyChaseBrain.cs](./EnemyChaseBrain.cs) | EnemyChaseBrain | `EnemyChaseBrain` 的主要實作入口。 |
| [EnemyChaseMotor.cs](./EnemyChaseMotor.cs) | EnemyChaseMotor | `EnemyChaseMotor` 的主要實作入口。 |
| [EnemyCombatAnimatorDriver.cs](./EnemyCombatAnimatorDriver.cs) | EnemyCombatAnimatorDriver | `EnemyCombatAnimatorDriver` 的主要實作入口。 |
| [EnemyCombatCore.cs](./EnemyCombatCore.cs) | EnemyCombatOptionId, EnemyCombatActionPhase, EnemyCombatOptionTickResult, EnemyCombatOption, EnemyDamageUtility | `EnemyCombatCore` 的主要實作入口。 |
| [EnemyCombatDecisionController.cs](./EnemyCombatDecisionController.cs) | EnemyCombatDecisionController | `EnemyCombatDecisionController` 的主要實作入口。 |
| [EnemyFlyingChaseMotor.cs](./EnemyFlyingChaseMotor.cs) | EnemyFlyingChaseMotor | `EnemyFlyingChaseMotor` 的主要實作入口。 |
| [EnemyMeleeDashAttack.cs](./EnemyMeleeDashAttack.cs) | EnemyMeleeDashAttack | `EnemyMeleeDashAttack` 的主要實作入口。 |
| [EnemyMeleeSwingAttack.cs](./EnemyMeleeSwingAttack.cs) | EnemyMeleeSwingAttack | `EnemyMeleeSwingAttack` 的主要實作入口。 |
| [EnemyPatrolArea.cs](./EnemyPatrolArea.cs) | EnemyPatrolPointQueryResult, EnemyPatrolArea | `EnemyPatrolArea` 的主要實作入口。 |
| [EnemyProjectile.cs](./EnemyProjectile.cs) | EnemyProjectile | `EnemyProjectile` 的主要實作入口。 |
| [EnemyRangedBeamAttack.cs](./EnemyRangedBeamAttack.cs) | EnemyRangedBeamAttack | `EnemyRangedBeamAttack` 的主要實作入口。 |
| [EnemyRangedProjectileAttack.cs](./EnemyRangedProjectileAttack.cs) | EnemyRangedProjectileAttack | `EnemyRangedProjectileAttack` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

