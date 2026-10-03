# 共用黑幕與場景載入

最後核對：2026-09-18，Phase 4 前置-1。

- `ScreenFadeLayer`：Screen Space Overlay 黑色 Image，使用最高現有 Sorting Layer 與 sortingOrder 32767。FadeIn 表示黑色到透明；FadeOut 表示透明到黑色。預設 1.5 秒，使用 Unscaled Time。新的淡出／淡入會從目前 alpha 重新開始。
- `LocalSceneTransition`：由 InputManager 的 Fusion SceneLoadStart／SceneLoadDone 建立並驅動，Overlay 跨場景存在。一般場景切換在載入開始遮黑並持有 AllInput 票證；若 Fusion Menu Connect Loading 正在顯示，黑幕先維持透明，保留內建連線動畫。Connect UI 關閉且本地 PlayerObject／PlayerLocalView 相機綁定完成後，才從黑色淡入並在結束時釋放自己的票證。
- `MenuConnectionUiTransitionGate`：`MenuUIController` 在 Fusion Menu 的 Loading 畫面實際顯示時持有此本地閘門，直到該 GameObject 不再位於啟用中的 Hierarchy 才放行。不能只信任 SDK 的 `IsShowing`，因為父物件先停用時該旗標可能殘留為 true。`LocalSceneTransition` 因此不會在 Quick Play／Continue／Party Connect 的 Loading UI 還看得到時淡入或解除 AllInput，也不會因殘留旗標永久黑屏。
- 第二次載入會取消舊淡入進度並重新全黑；Shutdown 或 Runner 銷毀會清掉本次轉場，不釋放其他系統票證。

## Inspector

編輯 `Assets/Prefabs/Runner.prefab` 的 `InputManager > Scene Fade Duration`（預設 1.5）。不需要手動新增 Canvas、NetworkBehaviour 或變動玩家 Prefab。播放時可在 SceneTransitionOverlay 的 ScreenFadeLayer 檢視 Alpha／調整 Fade Duration；執行期修改不會保存到 Prefab。

獨立使用工具可在本地物件 AddComponent<ScreenFadeLayer>() 後呼叫 FadeIn／FadeOut。工具本身不取得輸入鎖；需要封鎖互動時另持有 LocalPlayerControl 票證。正式轉場在 SceneLoadStart 直接遮黑，避免非同步卸載先顯示空場景，沒有額外延後 Fusion 的切場時序。

## 關卡計時

StageFlowController 每次 Spawn 建立空的 LoadedPlayers。Client 確認本地載入完成後以可靠 RPC 傳送自己的當前 PlayerObject NetworkId；Host 使用 RpcInfo.Source 驗證連線身分與當前 PlayerObject。當次 StageFlow NetworkObject 隔離上一次關卡回報。

Host 必須同時通過地圖準備、SceneManager 非忙碌、所有仍連線玩家有有效 PlayerObject 與本次載入回報，才建立 StageTimer。較快玩家依自己的載入狀態淡入；Host 不以自己的 SceneLoadDone 推定 Client 完成。此階段的完成條件不宣稱已同步未來的多 Chunk 拓撲。

詳見 [Phase 4 前置-1 配置與驗證](../../../../Documentation/ProjectArchitecture/144_PrePhase4-1_共用控制與轉場.md)。

離線玩家會從 LoadedPlayers 移除，避免反覆加入離線留下舊條目。Stage HUD 在 Initializing 顯示等待載入，不會把未建立計時器顯示為不限時。
