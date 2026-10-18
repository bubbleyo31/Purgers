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


## 2026-10-10 受傷來源提示

`LocalPlayerDamageIndicator` 訂閱本地受傷事件，使用固定世界位置快照顯示方向弧與倒三角；`DamageIndicatorSettings` 提供共用圖像、色彩、尺寸、時間與遮擋設定。`DamageIndicatorRules`／`DamageIndicatorOcclusionQuery` 處理方向、時序及限定 PhysicsScene／預算的持續遮擋；`DamageIndicatorGraphic` 提供可替換的 UI 幾何。正式配置與驗證見 [49](../../../../Documentation/ProjectArchitecture/49_玩家受傷來源指示.md)。

## 2026-10-13 安全屋限制與 HUD（局部）

安全屋 Q／E 經 PlayerActionGate 封鎖，玩家友傷／自傷經既有 DamageReceiverUtility／PlayerHealth 拒絕；普通武器與 ALT 獎勵維持。SafeHouseHudController.HudRunner 可供技能／經驗子 HUD 綁定；2026-10-13 已依使用者授權接到正式 SafeHouse 場景，保留原安全屋資訊。詳見 Documentation/ProjectArchitecture/145_PrePhase4-2_安全屋鍵盤準備與HUD.md 及 Validation/SafeHouseGameplay/README.md。

## 2026-10-18 觀戰身分列

LocalSpectatorHUD 只讀 LocalPlayerSpectatorController.Candidates／SpectatedPlayer；姓名取 GameLogic.TeammateIdentities，PlayerPortraitProvider 提供圖片，SharedPlayerPortraitProvider 為目前共用圖實作。Prefab 接入 _Menu ReferenceFrame；設定及驗證見架構文件 100 與 Validation/SpectatorSwitch。
