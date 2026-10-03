# Phase 4 前置-2：安全屋鍵盤 Ready Check 與左上 HUD

> 最後核對：2026-09-18（局部）  
> 核對來源：`SafeHouseFlowController`、`SafeHouseHudController`、`SafeHouseReadyRules`、`PlayerControlLocks`、`SafeHouse.unity`  
> 相關文件：[Phase 2 安全屋](142_Phase2_安全屋灰盒配置與驗證.md)、[Phase 4 前置-1 共用控制](144_PrePhase4-1_共用控制與轉場.md)

## 行為

1. Host 仍需靠近開始裝置按 E，State Authority 重新驗證 Host 身分與距離後開啟 Ready Check。
2. Ready Check 開啟後，各端按 TAB 切換自己的準備／取消準備；滑鼠按鈕只保留為可選操作。
3. 一次投票預設維持 7 秒，由 `readyCheckTimeoutSeconds` 可調。逾時時 State Authority 清除所有準備狀態並回到 `WaitingForHost`；Host 必須重新靠近按 E 才能再次開始。
4. 投票期間任何在線玩家按 ESC 都能取消整次投票；Client 經 `RPC_RequestCancelReadyCheck` 請求，只有 State Authority 能真正關閉投票。
5. Client 不直接寫入 `ReadyByPlayer`。Client 送出 `RPC_RequestToggleReady`，State Authority 只使用 Fusion 提供的 `RpcInfo.Source`，並驗證玩家仍在線及 Phase 可接受切換。
6. 每位在線玩家在 HUD 有一顆 LED：綠色亮燈為已準備，暗色為未準備；同時顯示已準備與未準備人數及投票剩餘時間。
7. 全員準備後，Host State Authority 建立網路 `TickTimer` 並進入 `Countdown`，倒數固定 3 秒。任何玩家在倒數中切回未準備時，Authority 立即清除 Timer 並回到 `ReadyCheck`；ESC 或 7 秒投票逾時會取消整次投票。
8. 倒數完成後才進入 `LoadingStage`，沿用 `GameLogic.PrepareForAuthoritativeSceneTransition` 與 Fusion Scene Authority 切場。
9. Ready HUD 位於 SafeHouse HUD 左上第三列；Phase 4 前置-3 已遷移至 `TopLeftColumn/ReadyCheckSlot/ReadyCheckPanel`，尺寸與位置以 [146](146_PrePhase4-3_HUD排列與資訊精簡.md) 為準，不再使用初版 `(20, -200)` 的直接 Canvas 定位。

## 輸入與表現責任

```text
本機 TAB
→ SafeHouseHudController（僅採集意圖；AllInput 時拒絕）
→ SafeHouseFlowController.RequestToggleLocalReady
├─ Host／State Authority：直接驗證與切換自身 NetworkDictionary 項目
└─ Client：RPC_RequestToggleReady → State Authority 驗證與切換
→ Render 讀取 ReadyByPlayer／Countdown TickTimer
→ LED、已準備／未準備數、投票與出發倒數文字

本機 ESC
→ SafeHouseHudController（投票顯示時採集取消意圖；AllInput 時拒絕）
→ SafeHouseFlowController.RequestCancelLocalReadyCheck
├─ Host／State Authority：直接驗證並取消整次投票
└─ Client：RPC_RequestCancelReadyCheck → State Authority 驗證並取消整次投票
```

Ready HUD 顯示期間**不取得任何 `LocalPlayerControl` 票證**：玩家仍可移動、觀看、攻擊與操作既有介面。它只在既有 Phase 4 前置-1 `AllInput` 轉場鎖存在時拒絕 TAB／ESC 意圖，不改動 Phase 4 前置-1 的鎖定或 Photon Fusion connect UI 過場順序。

## Networked schema

- `ReadyByPlayer`：既有 `NetworkDictionary<PlayerRef, NetworkBool>`，在線玩家加入為未準備、離線時移除。
- `StageStartCountdown`：新增 `TickTimer`，只由 State Authority 建立、取消與清除。
- `ReadyCheckTimeout`：State Authority 開票時建立的 `TickTimer`；預設 7 秒且可由 `readyCheckTimeoutSeconds` 調整。
- `SafeHousePhase.Countdown`：全員準備後、正式載入前的獨立 Phase；保留既有 enum 數值，新增值附加為 3。

## 驗證紀錄

- TDD：新增整次投票取消權限與逾時規則案例，完成 RED → GREEN。
- `PurgersRegression` EditMode：59／59 通過，job `d3dc55e21bd748cc99277204c05f1c8b`；完整 EditMode 65／65 通過，job `2815b071619347aab00d7d407679849b`。
- Windows Development Build：最終乾淨建置 `build-a3d47159ab` 為 0 errors、0 warnings。
- Editor Host＋獨立 Windows Client 實測同一 Session：7 秒自動取消、Client 整票取消 RPC、Client Ready RPC、一般取消、全員 Ready 啟動 3 秒倒數、倒數中 Client 取消、重新 Ready、兩端切入 Game 均通過。
- Host／Client 兩端均讀回：2 位玩家對應 2 顆 LED、已準備／未準備數與投票秒數正確、左上第三列位置正確，且 Ready HUD 互動期間 `LocalPlayerControl` mask 為 `None`。
- 另以真正 `FusionMenuUIMain` Quick Play 路徑查核 Phase 4 前置-1：connect UI 顯示時 `LocalSceneTransition` fade alpha 全程維持 0，connect UI 關閉後才恢復場景 fade，結果 PASS。
- Client Player log 搜尋 `Exception|Error|Assert|FAIL` 為 0；Editor Console Error／Warning 為 0。

證據：[Host.txt](Validation/Phase4-2/Host.txt)、[Client.txt](Validation/Phase4-2/Client.txt)、[MenuConnectAnimation.txt](Validation/Phase4-2/MenuConnectAnimation.txt)、[MenuConnectAnimation.png](Validation/Phase4-2/MenuConnectAnimation.png)、[verification.txt](Validation/Phase4-2/verification.txt)。驗證器直接呼叫與 TAB／ESC 相同的本機入口，以隔離鍵盤硬體注入差異；實體鍵仍應依下列清單複驗。

## 手動複驗

1. Editor Host 與獨立 Client 進入 SafeHouse，Host 按 E。
2. 兩端 Ready HUD 都位於左上第三列，LED 數等於在線玩家數，初始皆熄燈，顯示已準備 0、未準備 2 與約 7 秒投票時間；同時確認移動、觀看及攻擊未被鎖定。
3. 不操作直到 7 秒結束；兩端投票 UI 都關閉，Host 必須再按 E 才重新出現。
4. 重新開始投票，Client 按 ESC；兩端投票 UI 都立即關閉。
5. 再次開始投票，Client 按 TAB：兩端變成一顆亮燈／一顆熄燈與 1／1；再按 TAB 回到 0／2。
6. Host 與 Client 依序按 TAB；兩端顯示約 3 秒出發倒數。
7. 倒數中任一端按 TAB，兩端立即回到 Ready Check，場景不可載入；倒數中按 ESC 則整次投票關閉。
8. 重新開票並全員準備；倒數完成後兩端進入 Game。
9. 從 Menu 的 Quick Play 再進一次 SafeHouse，確認 Photon Fusion connect 動畫完整顯示，動畫結束後才接手本機場景 fade。
