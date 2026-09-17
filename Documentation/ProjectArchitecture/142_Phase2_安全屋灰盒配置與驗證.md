# Phase 2：安全屋灰盒配置與驗證

> 最後核對：2026-09-18  
> 場景：`Assets/Scenes/SafeHouse.unity`  
> 範圍：Menu 進入安全屋、Host 開啟 Ready Check、全員 Ready 後進入 Game。

## 1. 現在可以怎麼玩

1. 從 `_Menu` 按 **QUICK PLAY**，系統建立新 Host 存檔並進入 SafeHouse。
2. 畫面會持續顯示目前 `StageLevel`；新存檔為 1。
3. Host 走近藍色開始裝置，World Space 提示顯示「按 E 開始遊戲」。
4. Host 按 E 後，所有玩家看到「全員準備」面板。
5. 每位玩家按「確認準備」。新加入者預設未準備，離線者會從名單移除；確認後目前不能取消。
6. 全員 Ready 後由 State Authority 載入 `Game`。

Continue 會載入 Host 選擇的存檔後進入同一個 SafeHouse。Client 仍從 Party Menu 加入，不會建立或寫入本機存檔。

## 2. 灰盒場景內容

```text
SafeHouse
├─ GrayboxEnvironment
│  ├─ Floor / Walls
│  ├─ CenterPlatform
│  └─ Benches
├─ PlayerSpawnPoints
│  ├─ PlayerSpawnPoint_01
│  ├─ PlayerSpawnPoint_02
│  ├─ PlayerSpawnPoint_03
│  └─ PlayerSpawnPoint_04
├─ StartTerminal
│  ├─ TerminalBody / TerminalScreen
│  ├─ InteractionAnchor
│  └─ WorldPrompt Canvas / TMP
├─ SafeHouseFlow
│  ├─ NetworkObject
│  └─ SafeHouseFlowController
├─ GameLogic
├─ SafeHouseHUD
│  ├─ StageLevelPanel
│  └─ ReadyCheckPanel
├─ Directional Light
└─ SafeHousePreviewCamera（停用）
```

環境、平台、長椅與開始裝置都只使用 Unity Cube 與簡單 Material。可直接替換 MeshRenderer／模型，但請保留下列功能節點與引用：

- 四個 `PlayerSpawnPoint_*` Transform。
- `InteractionAnchor`，它是 Host 距離驗證中心。
- `WorldPrompt` 的 Canvas 與 TMP。
- `SafeHouseFlow` 上的 NetworkObject／Controller。
- `GameLogic`、SafeHouseHUD 與按鈕引用。

## 3. Inspector 重要欄位

### SafeHouseFlowController

- `Start Terminal Anchor`：指定 `StartTerminal/InteractionAnchor`。
- `Interaction Distance`：Host 可按 E 的距離，預設 3.5。
- `Gameplay Scene Name`：全員 Ready 後載入的 Build Settings 名稱，預設 `Game`。
- `Debug Flow`：需要 Console 流程紀錄時開啟。

### SafeHouseStartTerminalView

- `Flow Controller`：指向 SafeHouseFlow。
- `Prompt Root`：整個 WorldPrompt Canvas 根物件。
- `Prompt Label`：World Space TMP 文字。
- 此提示只對本機 Host 顯示；Client 看不到 E 提示是正確行為。

### SafeHouseHudController

- `Stage Level Label`：常駐關卡等級文字。
- `Ready Panel`、`Ready Count Label`、`Ready Button`、`Loading Label`：Ready Check 畫面。
- Ready Button 只送出玩家意圖，正式 Ready 狀態由 State Authority 寫入。

### GameLogic

- SafeHouse Scene 的四個 `Player Spawn Points` 必須全部指向安全屋出生點。
- Game Scene 保留自己的出生設定。切場時持續存在的主 GameLogic 會接管新場景設定，場景副本隨後由 Authority 移除。
- `ISceneLoadDone` 會在舊場景 Player 被卸載後補建玩家；不要另放常駐 Player 或自行呼叫 `Runner.Spawn`。

## 4. Build Settings 與 MenuConfig

Build Settings 必須啟用：

1. `Assets/Scenes/_Menu.unity`
2. `Assets/Scenes/SafeHouse.unity`
3. `Assets/Scenes/Game.unity`

`Assets/Settings/MenuConfig.asset` 的第一個有效場景必須是 `SafeHouse`。專案的 `MenuSceneLaunchPolicy` 會把 PlayerPrefs 內過期的 `Game` 選擇校正到 SafeHouse，因此不需要手動清 PlayerPrefs，也不要修改 Photon FusionMenu 套件原碼。

## 5. 單機 Host 驗證

1. 開 `_Menu`，清 Console，進 Play Mode。
2. 按 Quick Play。
3. 確認進入 SafeHouse，玩家沒有穿地板，StageLevel 為 1。
4. 遠離裝置時按 E 不應有反應；靠近後顯示提示。
5. 按 E，確認 Ready 面板為 `0 / 1`。
6. 按「確認準備」，確認進入 Game。
7. 在 Console／Debugger 檢查：一個 Running Host Runner、一個有效 GameLogic、一個 Player，且 `TryGetPlayerObject(LocalPlayer)` 成功。
8. Console 不應新增 Error／Warning。

本輪 UnityMCP 實測通過上述流程；Ready 畫面與切場後 Game 畫面截圖位於 `Temp/CodexPhase2`。Temp 不是永久美術資產。

## 6. Host／Client 驗證

請使用獨立 Build、第二個 Editor 專案副本或 ParrelSync Clone。不要用同一個 Runner 的單機畫面代替多人驗證。

1. Host 從 `_Menu` 按 Quick Play，進入 SafeHouse 並記下 Session Code。
2. Client 從 Party Menu 輸入 Code 加入。
3. 確認 Host 的 `GameSaveRuntimeContext` 為 `HostWritable` 且有 ActiveSave；Client 為 `ClientReadOnly` 且 ActiveSave 為空。
4. Client 加入後，Host 與 Client 的 Ready 人數都應顯示 `0 / 2`。
5. Client 靠近開始裝置不會看到 Host 的 E 提示，也不能開啟 Ready Check。
6. Host 靠近並按 E；兩端都出現 Ready 面板。
7. 先讓一端 Ready，兩端都應顯示 `1 / 2`，不得切場。
8. 第二端 Ready 後，兩端一起進入 Game。
9. 在 Ready Check 期間讓 Client 離線，Host 的總人數應降為 1；若 Host 已 Ready，應符合剩餘全員 Ready 並進場。
10. 在 Ready Check 期間加入新 Client，新玩家必須是未準備，不能沿用舊名額的 Ready。
11. 確認每個 Runner 各自只有一個 GameLogic 與一個對應 PlayerObject，Console 無 Error／Warning。

## 7. 目前邊界

- 已實作的是 `_Menu → SafeHouse → Game`。
- `Game → SafeHouse` 需要 Phase 3 的撤離成功、限時／全滅失敗、StageLevel 提交與循環重設，現在尚未接入。
- Ready 確認目前不可取消且沒有逾時。
- 灰盒只提供空間與流程驗證；未建立商店、天賦、補給或正式美術。
- 獨立 Host／Client 尚需依第 6 節人工實測後，才能宣稱多人流程完成。
