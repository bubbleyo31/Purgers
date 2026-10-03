# Phase 2：安全屋灰盒配置與驗證

> 最後核對：2026-09-18  
> 場景：`Assets/Scenes/SafeHouse.unity`  
> 範圍：Menu 進入安全屋、Host 開啟 Ready Check、全員 Ready 後進入 Game。

## 1. 現在可以怎麼玩

1. 從 `_Menu` 按 **QUICK PLAY**，系統建立新 Host 存檔並進入 SafeHouse。
2. 畫面會持續顯示目前 `StageLevel`；新存檔為 1。
3. Host 走近藍色開始裝置，World Space 提示顯示「按 E 開始遊戲」。
4. Host 按 E 後，所有玩家看到「全員準備」面板。
5. 每位玩家按 TAB 切換準備／取消；新加入者預設未準備，離線者會從名單移除。
6. 全員 Ready 後由 State Authority 倒數 3 秒；任何取消立即中止倒數，完成後載入 `Game`。

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
- `Ready Panel` 位於左上第三列；Title 顯示玩家 LED，Status 顯示已準備／未準備與倒數，Button 文字顯示 TAB 操作。
- TAB／可選 Ready Button 只送出玩家意圖，正式 Ready 狀態與倒數由 State Authority 寫入。

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
5. 按 E，確認 Ready 面板為已準備 0、未準備 1，且有一顆熄滅 LED。
6. 按 TAB 亮燈並開始 3 秒倒數；再按 TAB 應取消倒數。重新準備後確認進入 Game。
7. 在 Console／Debugger 檢查：一個 Running Host Runner、一個有效 GameLogic、一個 Player，且 `TryGetPlayerObject(LocalPlayer)` 成功。
8. Console 不應新增 Error／Warning。

Phase 2 的 Host 單機流程與 Phase 4 前置-2 的 Editor Host＋獨立 Windows Client 流程均已實測；最新多人紀錄見 [145](145_PrePhase4-2_安全屋鍵盤準備與HUD.md)。

## 6. Host／Client 驗證

請使用獨立 Build、第二個 Editor 專案副本或 ParrelSync Clone。不要用同一個 Runner 的單機畫面代替多人驗證。

1. Host 從 `_Menu` 按 Quick Play，進入 SafeHouse 並記下 Session Code。
2. Client 從 Party Menu 輸入 Code 加入。
3. 確認 Host 的 `GameSaveRuntimeContext` 為 `HostWritable` 且有 ActiveSave；Client 為 `ClientReadOnly` 且 ActiveSave 為空。
4. Client 加入後，Host 與 Client 都應顯示兩顆熄滅 LED、已準備 0、未準備 2。
5. Client 靠近開始裝置不會看到 Host 的 E 提示，也不能開啟 Ready Check。
6. Host 靠近並按 E；兩端都出現 Ready 面板。
7. 先讓一端按 TAB，兩端都應顯示一亮一滅與 1／1；同一端再按 TAB，兩端回到 0／2，且不得切場。
8. 兩端都 Ready 後應開始 3 秒倒數；倒數中一端取消，兩端立即回到 Ready Check。重新 Ready 並完成倒數後，兩端一起進入 Game。
9. 在 Ready Check 期間讓 Client 離線，Host 的總人數應降為 1；若 Host 已 Ready，應符合剩餘全員 Ready 並進場。
10. 在 Ready Check 期間加入新 Client，新玩家必須是未準備，不能沿用舊名額的 Ready。
11. 確認每個 Runner 各自只有一個 GameLogic 與一個對應 PlayerObject，Console 無 Error／Warning。

## 7. 目前邊界

- 已實作的是 `_Menu → SafeHouse → Game`。
- `Game → SafeHouse` 需要 Phase 3 的撤離成功、限時／全滅失敗、StageLevel 提交與循環重設，現在尚未接入。
- Ready 可用 TAB 雙向切換；全員準備後有 3 秒倒數，目前沒有 Ready Check 逾時或 Host 強制關閉。
- 灰盒只提供空間與流程驗證；未建立商店、天賦、補給或正式美術。
- Ready／取消／倒數中取消與切場已完成獨立 Host／Client 驗證；加入／離線的 Phase 2 規則仍應在變更連線生命週期時複驗。
