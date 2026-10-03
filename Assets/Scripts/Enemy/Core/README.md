# Assets/Scripts/Enemy/Core 程式導覽

敵人資料、AI、戰鬥與呈現。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [EnemyActionGate.cs](./EnemyActionGate.cs) | EnemyActionLockFlags, EnemyActionLockSource, EnemyActionGate | `EnemyActionGate` 的主要實作入口。 |
| [EnemyActor.cs](./EnemyActor.cs) | EnemyActor | `EnemyActor` 的主要實作入口。 |
| [EnemyDeathLifecycleController.cs](./EnemyDeathLifecycleController.cs) | EnemyDeathPhase, IEnemyDeathGameplayExtension, EnemyDeathLifecycleController | 權威死亡時間軸；首次正式致死時交給 GameLogic 發經驗。 |
| [EnemyDefinition.cs](./EnemyDefinition.cs) | EnemyCombatFamily, EnemyVariant, EnemyLocomotionKind, EnemyChaseKind, EnemyDefinition | 敵人靜態分類與 Phase 6-C 基礎擊殺經驗入口。 |
| [EnemyStateController.cs](./EnemyStateController.cs) | EnemyBrainState, EnemyActionState, EnemyControlState, EnemyStateController | `EnemyStateController` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

