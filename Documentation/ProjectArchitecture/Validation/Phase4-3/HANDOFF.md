# Phase 4 前置-3 HUD 與最高強化／撤離提示交接摘要

> 最後核對：2026-09-19  
> 範圍：左上 HUD 排列、中下方動能增傷 HUD、最高強化 1.5 秒保持、撤離區提示。  
> 不在範圍：小地圖邏輯、任務完成條件、轉場黑幕修改。

> 此資料夾名稱與本文其餘歷史紀錄保留舊階段標籤；正式名稱已校正為「Phase 4 前置-3」。

完整設計與配置見 `../../146_PrePhase4-3_HUD排列與資訊精簡.md`。

## 最終行為

- 左上依序為小地圖接入容器、關卡等級／純數字時間／任務文字、安全屋 Ready Check。
- `Speed_UI` 位於畫面中下方。舊類名 `LocalPlayerSpeedSlider` 為 Unity 序列化相容性保留，量條目前只代表 `NormalizedEnergy`／增傷率，不代表速度。
- 動能到達 1 時進入 1.5 秒「最高強化增益」。保持期間能量、量條及傷害倍率固定滿值；再次符合滿能量充能條件會把同一個 Timer 刷新為 1.5 秒，不建立第二層效果。
- 最高強化時顯示 `傷害增加100%`，字級、量條尺寸與顏色切換為強化樣式。
- 關卡處於可撤離狀態且本機玩家進入選定撤離區時，量條上方顯示綠色提示。未全員到齊顯示等待文字；撤離 Timer 啟動後顯示 0.1 秒精度倒數。
- 左上任務文字與撤離倒數仍保留。`ScreenFadeLayer` 沿用既有最高排序，這次沒有修改轉場黑幕。

## 已修改檔案

### 程式與測試

| 檔案 | 變更／責任 |
|---|---|
| `Assets/Scripts/Player/PlayerGrappleMomentumEnergy.cs` | 新增 `maximumBoostHoldDuration`、Networked `MaximumBoostTimer`、最高強化查詢，以及可獨立測試的 `PlayerGrappleMomentumEnergyRules`。 |
| `Assets/Scripts/UI/Player/LocalPlayerSpeedSlider.cs` | 正式中下方 HUD；改讀能量與倍率、套用最高強化樣式、顯示本機撤離提示。類名未更動。 |
| `Assets/Scripts/UI/Player/PlayerGrappleMomentumEnergyDebugHUD.cs` | 最高強化期間顯示剩餘秒數，且不誤標為快速衰退；正式預設仍關閉。 |
| `Assets/Scripts/GameFlow/Stage/StageFlowController.cs` | 新增唯讀 `IsExtractionAvailable`，作為 HUD 與未來任務完成條件的單一入口。 |
| `Assets/Scripts/Editor/Tests/HudLayoutTests.cs` | 覆蓋刷新、不疊加、保持滿值、到期衰退，以及場景 Slider／綠色提示引用。 |
| `Assets/Scripts/Tool/Testing/Phase42NetworkVerification.cs` | HUD 驗證文字更新為 `傷害增加` 格式。 |

### Prefab、場景與文件

| 檔案 | 變更／責任 |
|---|---|
| `Assets/Prefabs/KCC_Player.prefab` | 關閉詳細動能 Debug HUD；玩家 Prefab Variant 繼續承接動能元件。 |
| `Assets/Scenes/_Menu.unity` | `Speed_UI` 排到中下方，加入增傷樣式與 `ExtractionCountdownPanel` 引用。持續性 Gameplay HUD 原本就由此場景提供。 |
| `Assets/Scenes/Game.unity` | Phase 4 前置-3 左上三列 `StageHUD`、小地圖空容器與排序。 |
| `Assets/Scenes/SafeHouse.unity` | Phase 4 前置-3 左上三列 `SafeHouseHUD`、小地圖空容器與 Ready Check 第三列。 |
| `Documentation/ProjectArchitecture/02_程式職責總表.md` | 同步元件責任。 |
| `Documentation/ProjectArchitecture/31_鈎索動能與傷害倍率.md` | 同步最高強化、傷害與驗證邊界。 |
| `Documentation/ProjectArchitecture/100_UI觀戰與聊天.md` | 同步正式 HUD 資料來源及顯示責任。 |
| `Documentation/ProjectArchitecture/146_PrePhase4-3_HUD排列與資訊精簡.md` | Phase 4 前置-3 完整配置、尺寸、排序與驗證紀錄。 |

## 資料與網路責任

| 資料／行為 | Owner | 說明 |
|---|---|---|
| `NormalizedEnergy` | Fusion 玩家預測 Simulation | `[Networked]`，由 `Player.FixedUpdateNetwork()` 取得輸入後呼叫 `TickSimulation()`；不能描述成只由 State Authority 執行。State Authority 在 `Spawned()` 初始化為零。 |
| `MaximumBoostTimer` | Fusion 玩家預測 Simulation | `[Networked] TickTimer`。達峰或保持中再次符合充能條件時覆寫同一 Timer，所以可刷新但不疊加。 |
| Bullet／Melee 增傷結果 | 既有權威傷害流程 | `IOutgoingDamageModifier` 只乘上正數 `RequestedDamage`；不改 `BaseDamage`，不處理治療、環境傷害或鈎索次數。真正命中與傷害結果仍由既有 State Authority 流程決定。 |
| `StageFlowController` Phase、撤離點、`ExtractionHoldTimer` | State Authority／Host | Host 選定撤離點、檢查所有存活玩家、啟動撤離 Timer 並完成關卡。Client 不可自行決定撤離成功。 |
| 中下方增傷與撤離提示 | 本機 Presentation | `LocalPlayerSpeedSlider` 只綁定 `Object.HasInputAuthority` 的 Player，讀取 Networked 能量與 StageFlow 狀態，不寫入 Gameplay 結果。 |
| 左上 Stage／SafeHouse HUD | 本機 Presentation | 只讀對應 Flow Controller。Ready、Stage Timer 與撤離結果仍由原有網路狀態提供。 |

## Inspector 與場景設定

### `PlayerGrappleMomentumEnergy`

程式預設：Attached 充能開啟、Release Momentum 充能開啟、速度門檻 100、充滿 10 秒、最高強化 1.5 秒、靜止門檻 0.1、快速衰退 2 秒、移動衰退 6 秒、最高傷害倍率 2。

目前 `KCC_Player` 是 `Assets/Photon/FusionAddons/KCC/Prefabs/KCC.prefab` 的 Prefab Variant，另有既存覆寫：

- `requiredChargeSpeed = 20`
- `fullChargeDuration = 2`
- `maximumBoostHoldDuration = 1.5`：沒有 Variant 覆寫，沿用腳本新欄位預設。
- `showDebugHUD = false`

後續調整充能手感時，必須先確認改的是腳本預設、基底 KCC Prefab，或 `KCC_Player` Variant；不要只看原始碼的 100／10 就判定執行期數值。

### `_Menu/GameplayHUD Canvas/PlayerHUDCanvas`

`PlayerHUDController` 上的 `LocalPlayerSpeedSlider` 必須保持以下引用：

- `speedSlider` → `Speed_UI` 內的 Slider，範圍 0～1。
- `speedVisualRoot` → `Speed_UI`。
- `damageBonusLabel` → `Speed_UI` 內的傷害文字。
- `extractionPromptRoot` → `Speed_UI/ExtractionCountdownPanel`。
- `extractionPromptLabel` → `ExtractionCountdownPanel/Label`。

序列化樣式：

| 項目 | 一般 | 最高強化 |
|---|---:|---:|
| 量條顏色 | RGBA `(0.12, 0.58, 1, 1)` | RGBA `(1, 0.48, 0.08, 1)` |
| 文字顏色 | 白色 | RGBA `(1, 0.82, 0.2, 1)` |
| 文字大小 | 24 | 36 |
| 量條尺寸 | 380×14 | 440×22 |

`Speed_UI` 錨定畫面下方中央，位置 `(0, 200)`。`ExtractionCountdownPanel` 位於量條上方，尺寸 300×50、位置 `(0, 18)`，底色 RGBA `(0.08, 0.55, 0.24, 0.94)`，`Raycast Target` 關閉並預設 inactive；由 Controller 在本機進入有效撤離區後開啟。

### 左上 HUD 與排序

- Game `StageHUD` 與 SafeHouse `SafeHouseHUD`：Screen Space Overlay，Sorting Order 100。
- `TopLeftColumn`：小地圖接入容器、關卡資訊、Ready Check／保留槽依序固定排列。
- `MinimapContent` 只是空 RectTransform；沒有相機、RenderTexture、追蹤或同步邏輯。
- 黑幕沿用 `ScreenFadeLayer` 的既有最高排序 32767，不需為此功能新增 Canvas。

## 驗證結果

- TDD：先確認最高強化規則與新版場景契約失敗，再完成實作。
- `PurgersRegression` EditMode：75／75 通過，job `e93bc61248e2483f8330aa2e4c2a54b8`。
- Editor Host＋獨立 Windows Client：Ready Check、7 秒逾時、整票取消、單人取消、倒數中取消、重新準備、3 秒出發與切入 Game 通過；兩端確認純數字 Stage Timer 與中下方 `傷害增加0%` HUD。
- 最高強化執行期讀值：active、剩餘 1.50 秒、量條 1.00、`傷害增加100%`、字級 36、量條 440×22、橘色量條與金色文字。
- 視覺 QA：強制本機 Presentation 狀態後確認綠色 `撤離倒數 2.4` 面板、最高強化 HUD 與左上資訊能同時顯示；強制值未寫回場景。
- Windows Development Build：job `build-652b92ecb2`，0 errors、0 warnings，輸出 `Builds/Phase43Detail/Purgers.exe`，442.9 MB。
- 最終 Unity Console：0 errors、0 warnings。
- 證據：本資料夾的 `Host.txt`、`Client.txt`、`verification.txt` 與既有 Phase 4 前置-3 截圖；新增最高強化截圖位於 `Builds/Phase43Detail/Evidence/Phase43_MaxBoost_ExtractionCountdown.png`。

## 已知限制

- Host／Client 流程驗證已通過，但尚未以自然鈎索加速完整重跑「兩端能量一致性＋權威 Bullet／Melee 實際傷害」。目前規則由 EditMode 測試、執行期讀值與既有傷害回歸覆蓋。
- 綠色撤離面板的視覺以執行期強制 Presentation 狀態查核；尚未另跑兩位玩家自然站入撤離點的完整截圖流程。
- `IsExtractionAvailable` 目前定義為網路已就緒、Phase 為 Active、已有選定撤離點。未來加入「完成任務後才可撤離」時，應只擴充這個入口與 Host 規則，不在 HUD 複製任務判定。
- 未額外重跑 late join／reconnect。Ready Check 與切場流程的 Host／Client 驗證不等於這兩項情境。
- 類名 `LocalPlayerSpeedSlider` 與欄位名 `speedSlider`／`speedVisualRoot` 為序列化相容性保留；未來若要改名，必須先規劃 Scene／Prefab 遷移。
- 小地圖仍是空容器。下一個功能不可把小地圖狀態塞進 `StageHudController`；應建立獨立 Presentation owner，再掛入 `MinimapContent`。
- 轉場黑幕本次沒有修改，也不應因 HUD 擴充而降低其排序。

## 下一個功能先閱讀

依此順序閱讀：

1. `Documentation/ProjectArchitecture/00_CHATGPT快速讀取.md`
2. `Documentation/ProjectArchitecture/146_PrePhase4-3_HUD排列與資訊精簡.md`
3. `Documentation/ProjectArchitecture/31_鈎索動能與傷害倍率.md`
4. `Assets/Scripts/UI/Player/README.md`
5. `Assets/Scripts/GameFlow/Stage/README.md`
6. `Assets/Scripts/Player/README.md`
7. `Assets/Scripts/Tool/Testing/README.md`

接著讀實作：

1. `Assets/Scripts/UI/Player/LocalPlayerSpeedSlider.cs`
2. `Assets/Scripts/Player/PlayerGrappleMomentumEnergy.cs`
3. `Assets/Scripts/Player.cs` 的 `FixedUpdateNetwork()`
4. `Assets/Scripts/GameFlow/Stage/StageFlowController.cs`
5. `Assets/Scripts/GameFlow/Stage/StageHudController.cs`
6. `Assets/Scripts/GameFlow/SafeHouse/SafeHouseHudController.cs`
7. `Assets/Scripts/Editor/Tests/HudLayoutTests.cs`
8. `Assets/Scripts/Tool/Testing/Phase42NetworkVerification.cs`

若下一個功能是小地圖，先從 Game／SafeHouse 的 `TopLeftColumn/MinimapSlot/MinimapContent` 接入，不改此文件描述的 HUD 排序與黑幕層級。若下一個功能是任務完成條件，先擴充 Host 的 `StageFlowController` 與 `IsExtractionAvailable`，再讓兩個 HUD 繼續只讀結果。
