# Phase 4 前置-1：共用玩家控制與轉場工具

最後核對日期：2026-09-18（局部）。
核對來源：Player、PlayerMovement、PlayerLocalView、InputManager、SafeHouseFlowController、StageFlowController 及直接輸入來源。

## 查核與最小架構

原本 StageFlowController 只等 Host 的 PlayerObject 建立，不能證明 Client 完成載入。輸入主線為 InputManager → NetInput → Player → 移動／技能；E 終端機與開發快捷鍵繞過 NetInput，UI 也有獨立的 EventSystem。

新增 Control 的票證鎖與 Transition 的本地黑幕。PlayerActionGate 的戰鬥限制來源、State Authority 生成／死亡重生、Scene Authority 切場及存檔責任維持原設計。SafeHouse → Game 也使用既有 GameLogic.PrepareForAuthoritativeSceneTransition 鉤子，防止卸載期間舊重生計時器繼續生成玩家。

```text
多系統 Acquire → 本機票證集合 → InputManager 過濾 → NetInput 遮罩 → Fusion 模擬
                         └→ 直接快捷鍵／EventSystem 閘門
SceneLoadStart → 全黑＋AllInput
SceneLoadDone＋本地 Player／Camera 已綁定＋Connect UI 已隱藏 → 黑幕淡入 → 釋放本次 AllInput
                                     └→ 本次 StageFlow 的完成回報
Host：地圖準備＋所有在線玩家回報＋有效 PlayerObject → Active／StageTimer
```

## 設定與使用

1. Runner Prefab 的 InputManager 新增 `Scene Fade Duration`，預設 1.5 秒。
2. 不需要改 Player Prefab 或新增場景 Canvas；執行期建立本地跨場景 Overlay。
3. 其他系統使用 `LocalPlayerControl.Acquire(mask, reason)`，保存 IDisposable 並在結束／OnDisable 釋放。相同 reason 的兩張票證仍互相獨立，沒有全域 UnlockAll。
4. Movement 與 Look 可以分開持有；AllInput 也能獨立持有。Movement 攔截主動操作但不凍結已存在的物理／技能位移；Look 攔截視角輸入但不停止相機跟隨角色。
5. ScreenFadeLayer 提供 FadeIn、FadeOut 及 SetImmediate。正式非同步載入開始直接遮黑，完成後淡入；不另延遲既有 Fusion 切場。
6. Quick Play、Continue 與 Party Connect 由專案的 MenuUIController 追蹤 FusionMenuUILoading 是否仍實際位於啟用中的 Hierarchy。SDK 在父物件先停用時可能留下 `IsShowing=true`，所以不能只等該旗標；Connect UI 尚看得到時 LocalSceneTransition 保持黑幕透明但仍持有 AllInput，讓內建連線動畫可見。物件停用且本地玩家視角就緒後，才切至黑色並淡入遊戲，不修改 FusionMenu 套件原碼。
7. 原始 Scene、Prefab 序列化內容不需遷移。Host 與 Client 必須使用同一版 NetInput 與 StageFlow Networked schema。

## 載入回報邊界

本地完成指 Fusion 載入回呼已完成、Runner 不忙碌、當前本地 PlayerObject 有效且 PlayerLocalView 已綁定場景相機。若由 Fusion Menu 連入，還必須等 Connect Loading UI 的 Hide 動畫完成。Host 額外保留既有 MapPreparationReady 條件。計時可以在各端正在做 1.5 秒視覺淡入時開始，因為淡入與載入是分開的條件。

載入回報只接受 Fusion 提供的來源身分與該來源當前 PlayerObject ID；Client 不能替別人回報。重新進入相同場景也必須重新回報；離線玩家不列入等待，加入的玩家在 Active 前必須完成回報。Active 後不重設已啟動計時。

沒有以超時當作載入完成的捷徑。若載入／相機綁定失敗，保持黑幕與等待狀態，Shutdown 清理；不默默放行未完成玩家。多 Chunk 地圖拓撲同步仍屬後續 Phase 4。

## 驗證紀錄

- Unity EditMode：49／49 通過（含 5 個新增控制鎖測試），job `201ddf8fa8424d598a764e49e854868d`。
- 最終 Windows Development Build：0 errors、0 warnings，job `build-437bd9f7a7`，2026-09-18 13:19:03 UTC 完成。
- Editor Host＋獨立 Windows Client，執行 `SafeHouse → Game → SafeHouse → Game`。第一次 Client 延後回報 10 秒，Host 一直維持 Initializing／acks=1／time=0；回報到齊才建立約 600 秒計時器。
- 返回後第二次 Game 重新等待；Client 於等待期間離線，Host 依剩餘玩家啟動。額外讀取確認載入字典實際 Count=1，沒有留下離線條目。
- Host／Client 都驗證同時持有移動與鏡頭票證時，釋放額外 AllInput 不會釋放原票證。
- 黑幕獨立使用（未持有 AllInput）仍覆蓋既有 UI／IMGUI。截圖見 [black.png](Validation/Phase4-1/black.png)。最終 Canvas order=32767；現有 StageHUD=80，其餘已檢查 Canvas=0。
- `Time.timeScale=0` 時，1.5 秒淡入與淡出都能完成；Alpha 到 0 仍保留另一系統的 AllInput，EventSystem 啟用數為 0；釋放最後票證後恢復為 1。
- 以執行期待提交 NetInput 注入方向、視角與 Fire，取得 AllInput 後三者均清除；結束時沒有剩餘鎖。
- 安全屋透過 PlayerHealth.ReceiveDamage 的真實傷害流程觸發死亡；舊 PlayerObject 消失後由 GameLogic 生成新物件，生命恢復 100，沒有觸發場景黑幕。時間與物件 ID 見 [respawn.txt](Validation/Phase4-1/respawn.txt)。

完整紀錄：[Host.txt](Validation/Phase4-1/Host.txt)、[Client.txt](Validation/Phase4-1/Client.txt)、[visual.txt](Validation/Phase4-1/visual.txt)。驗證工具複製 Photon 設定並固定兩端使用 asia，排除各程序最佳區域快取不同造成的 GameNotFound；不修改正式連線設定。測試器直接建立 Runner／載入安全屋，沒有宣稱真人操作過整套選單與每一個實體鍵位。

測試使用 Temp 下專用存檔，沒有讀寫正式存檔；沒有保存測試時的 Scene／Prefab 變更。實體鍵鼠逐項操作與較慢硬體載入可依下方清單複驗。多 Chunk 拓撲同步不在這次驗證範圍。

## 手動複驗

1. 使用同版 Development Build 作 Client，Editor 作 Host，從安全屋進入 Game。
2. 載入與淡入期間持續嘗試 WASD、滑鼠、Space、Q、F、R、F1–F7、E、Enter／Esc、UI 點擊與鍵盤 Submit；不應產生新動作。
3. 本地 Player／Camera 準備好才開始淡入；恢復後短暫按鍵不得補發，持續按住的操作則依原輸入語意恢復。
4. 同時持有 Movement 與 Look，分別 Dispose；解除一者不影響另一者。加上 AllInput 再解除，原票證仍存在。
5. 刻意延後 Client 回報，Host 必須保持 Initializing，計時器未建立；Client 回報後才變成 Active。
6. 返回安全屋再進入 Game，確認不能沿用第一次的完成狀態。等待期間 Client 離線，Host 應依剩餘玩家重新判斷。
7. 檢查黑幕覆蓋 SafeHouse／Stage HUD、聊天與開發 HUD；Time.timeScale=0 時淡入淡出仍完成。
8. 死亡／重生仍由 GameLogic 執行，不因本地 Player 消失自行觸發場景黑幕。
9. 從 `_Menu` Quick Play、Continue 與 Party Connect 各進一次 SafeHouse：Loading UI 必須先完整隱藏，黑幕才開始淡入；淡入結束前不能控制玩家。

## Client 父節點斷言修正

較早一輪 Client 原始 log 查核發現原 GameLogic.Spawned 在 Fusion Multi-Peer 場景根下直接呼叫 MakeDontDestroyOnLoad，違反該 API 要求。現在先以 worldPositionStays=true 解除父節點，再由原 Runner 放入自己的保留根；不改生成、死亡排程或權威。另查核目前 SafeHouse／Game 序列化 respawnDelay 都是 1 秒，本輪維持設定，安全屋實測約 1.21 秒完成新玩家與視角綁定。

最終雙程序複驗於 2026-09-18 13:21:36 UTC 完成；Client 原始紀錄的 exception／error／assertion 搜尋結果為 0，Editor Console error／warning 為 0。證據目錄保留 ClientPlayer.txt 全文，父節點修正後未再出現斷言。
