# Assets/Scripts/UI/Player 程式導覽

此資料夾集中放置相近責任的遊戲程式。

2026-09-22：`LocalMinimapController` 沿用同一套快取／裁切／M 展開來呈現 SafeHouse。Game 使用 NetworkMapState；SafeHouse 使用自己的 Flow、預烘焙 Cartography 與該 Runner 的本機玩家，不繼承上一關地形／探索／敵人。SafeHouse 全圖可見、不新增探索 RPC；詳細烘焙限制見 GameFlow/SafeHouse/README.md。

## 修改入口

- 先由下表找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [LocalPlayerGrappleAimIndicator.cs](./LocalPlayerGrappleAimIndicator.cs) | LocalPlayerGrappleAimIndicator, GrappleAimIndicatorState | `LocalPlayerGrappleAimIndicator` 的主要實作入口。 |
| [LocalMinimapController.cs](./LocalMinimapController.cs) | LocalMinimapController | 區塊貼圖快取／裁切、高度灰階、探索 dirty 更新、M 展開與獨立 UI 標記；只讀 NetworkMapState／EnemyActor。 |
| [LocalPlayerHealthSlider.cs](./LocalPlayerHealthSlider.cs) | LocalPlayerHealthSlider | `LocalPlayerHealthSlider` 的主要實作入口。 |
| [LocalPlayerSpeedSlider.cs](./LocalPlayerSpeedSlider.cs) | LocalPlayerSpeedSlider | 中下方動能增傷量條、最高強化樣式與本機撤離提示；只讀本機玩家，消失／重生重新綁定。舊類名為序列化相容性保留。 |
| [PlayerGrappleMomentumEnergyDebugHUD.cs](./PlayerGrappleMomentumEnergyDebugHUD.cs) | PlayerGrappleMomentumEnergyDebugHUD | 預設關閉的詳細開發診斷，不取代正式 uGUI 動能增傷 UI。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

