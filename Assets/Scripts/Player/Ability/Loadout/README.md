# Assets/Scripts/Player/Ability/Loadout 程式導覽

此資料夾集中放置相近責任的遊戲程式。

## 修改入口

- 先由下表找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [IPlayerAbilityRuntimeModule.cs](./IPlayerAbilityRuntimeModule.cs) | IPlayerAbilityRuntimeModule | 能力執行階段模組介面。 |
| [PlayerAbilityDefinition.cs](./PlayerAbilityDefinition.cs) | PlayerAbilityDefinition | 能力的資料定義、解鎖條件與裝備限制。 |
| [PlayerAbilityLoadoutDefinition.cs](./PlayerAbilityLoadoutDefinition.cs) | PlayerAbilityLoadoutDefinition | 起始可替換裝備配置。 |
| [PlayerAbilityRuntime.cs](./PlayerAbilityRuntime.cs) | PlayerAbilityRuntime | 單一能力的執行階段生命週期。 |
| [PlayerAbilityRuntimeManager.cs](./PlayerAbilityRuntimeManager.cs) | PlayerAbilityRuntimeManager, SpawnPlanEntry | 掛載、切換與管理玩家能力。 |
| [PlayerAbilitySlotLayoutDefinition.cs](./PlayerAbilitySlotLayoutDefinition.cs) | PlayerAbilityCategoryCapacity, PlayerAbilitySlotLayoutDefinition | 能力欄位布局規則。 |
| [PlayerAbilityTypes.cs](./PlayerAbilityTypes.cs) | PlayerAbilityCategory, PlayerProfessionMask, PlayerAbilityProfessionRule, IPlayerAbilityCategorized | 能力類型與共用列舉。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

