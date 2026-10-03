# Assets/Scripts/MenuImplementation 程式導覽

_Menu 的開房、選檔與刪檔。

Phase 4-E：`MenuConnectionBehaviour.New Save Cycle Length` 只影響之後 Quick Play／Fresh Host 建立的新存檔；Continue 沿用既有 `CycleLengthSnapshot`。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [MenuConnection.cs](./MenuConnection.cs) | MenuConnection | `MenuConnection` 的主要實作入口。 |
| [MenuConnectionBehaviour.cs](./MenuConnectionBehaviour.cs) | MenuConnectionBehaviour | `MenuConnectionBehaviour` 的主要實作入口。 |
| [MenuSaveFlowController.cs](./MenuSaveFlowController.cs) | MenuSaveFlowController | 建立、選取、刪除存檔並啟動 Host Session。 |
| [MenuSaveLaunchPolicy.cs](./MenuSaveLaunchPolicy.cs) | MenuSaveLaunchKind, MenuSaveLaunchPolicy | `MenuSaveLaunchPolicy` 的主要實作入口。 |
| [MenuSaveSlotRow.cs](./MenuSaveSlotRow.cs) | MenuSaveSlotRow | `MenuSaveSlotRow` 的主要實作入口。 |
| [MenuSceneLaunchPolicy.cs](./MenuSceneLaunchPolicy.cs) | MenuSceneLaunchPolicy | `MenuSceneLaunchPolicy` 的主要實作入口。 |
| [MenuUIController.cs](./MenuUIController.cs) | MenuUIController | `MenuUIController` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


Phase 4 前置-1：MenuConnection 在未指定 Runner Prefab 的備援路徑也建立 InputManager，確保有共用輸入與載入回呼。`MenuUIController` 也會等 Fusion Menu Loading 畫面的 Hide 動畫完成，才讓跨場景黑幕淡入；不修改 Photon FusionMenu 套件原碼。

## Menu 視覺（2026-10-03 局部核對）

- `MenuBrushButtonVisual`：既有 Button 的筆觸與互動呈現，不接管 On Click。
- `MenuSaveSummaryView`：由 MenuSaveFlowController 推送所選存檔／數量，僅顯示；未選取時清除摘要。
- 子視窗 V2 已配置於 _Menu，正式存檔列改用獨立 SaveSlotRowBrushV2；原 Prefab 與正式流程保留。
- 架構、Inspector 與驗證入口：[166](../../../Documentation/ProjectArchitecture/166_Menu筆觸視覺與配置.md)。