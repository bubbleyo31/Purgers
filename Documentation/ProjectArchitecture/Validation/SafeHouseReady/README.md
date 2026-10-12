# 安全屋全員準備 1 秒出發

最後核對日期：2026-10-10（局部）
核對來源：SafeHouseFlowController、SafeHouseReadyRules、SafeHouseHudController、SafeHouseReadyRulesTests、SafeHouse.unity。
相關文件：[145 操作與設定](../../145_PrePhase4-2_安全屋鍵盤準備與HUD.md)。

## 實作與資產

- State Authority 全員準備優先於同 Tick 投票逾時；全員準備保存 PausedReadyCheckTicks、清除 ReadyCheckTimeout，啟動出發 TickTimer。
- 取消準備／新加入回 ReadyCheck 時恢復原餘額，不重給 7 秒；ESC 仍清除整票。Client 僅送原 RPC。
- HUD 出發階段只顯示出發秒數；RemainingReadyCheckSeconds 在 Countdown 回傳同步的凍結餘額。
- SafeHouse 場景透過 Unity Editor SerializedObject 設定 countdownDurationSeconds=1；git diff 只有這一個序列化欄位由 3 改 1，投票仍 7 秒。

## 自動驗證

- [RED](red.json)：29 項執行，新預設 1 秒與 Countdown 不因投票逾時取消共 2 項預期失敗。
- [聚焦 GREEN](focused.json)：29/29 通過，job eedf8a2264a34149908ad32e207e9d37。
- [PurgersRegression](regression.json)：completed=647、探索 total=662、status=failed、result=null。唯一回報失敗為既有 BossStageAssetTests.GameSceneContainsBossAssetWiring（MapChunk_Boss 物件同一性引用）；不可將探索數當完成數或宣稱全綠。此次未修改 Game 場景或 Boss 接線。
- Unity Editor scriptCompilationFailed=false。首次編譯已有 BattleHudReferenceLayout 的 TextureImporter.spritesheet CS0618 與 MCP WebSocket 訊息；本次安全屋程式未見新增編譯錯誤。

## 受控多人

[執行結果](runtime.txt) 與 [MCP execute_code 探針](runtime-probe.cs.txt)：同 Editor 的真正 Fusion Host/Client、Multiple Peer、正式 SafeHouse 與 Game Scene。

1. Client 準備走既有 RPC，Host 剩 0.2 秒時準備；確認出發 Timer 約 1 秒、投票 Timer 停止。
2. 經過原投票到期點，Phase 仍 Countdown，Host/Client 的剩餘投票秒數凍結且一致。
3. Client 取消準備，Host 恢復原餘額，餘額到期取消投票並清空準備狀態。
4. 全員準備與投票到期同 Tick，仍進入 Countdown，完成後兩端實際出現 Game StageFlowController。

探針以 IBeforeTick 注入時限與 ReadyCheck 起點，未驗證 Host 靠近終端機按 E 的自然開票輸入，也不是獨立程序／不同電腦的 multiplayer 證明。不寫存檔；測試 Runner 完成後 Shutdown。

## 剩餘邊界

未執行獨立 Windows Host/Client、Build 或實體 TAB/ESC 鍵盤操作。受控進場核對兩端 Game Flow 已載入，不代表完整 Game 戰鬥／存檔循環驗收。
## 晚加入與重連補驗

[最終結果](latejoin.txt) 與 [探針](latejoin-probe.cs.txt) 通過：倒數期間新 Client 加入為未準備、中止出發並恢復原投票餘額；Client 斷線後移除名單、剩餘已準備 Host 繼續倒數；Client 重連重新未準備並中止出發；Client 取消整票 RPC 清空 Ready 與兩個計時器。

本補驗只將測試中 Host 的出發／投票時間延長為 120 秒，給網路握手與載入時間；未保存 Scene。正式場景仍 1／7 秒，1 秒進場由上面的 runtime.txt 證明。第一輪用 15 秒出發窗口時，Client 握手尚未完成即已切場，因此 [第一輪探針](latejoin-first-attempt.txt) 等不到安全屋取消條件；第二輪記錄握手約 16.3 秒後到達，延長窗口才成功覆蓋晚加入情境。第一輪失敗保留，不算功能通過證據。

Runtime Console 出現 NavMeshSurface 複製提示及 Shutdown／切場時 Fusion 同 Tick Spawn/Despawn 提示；本輪不宣稱 Console 全零。未修改 NavMesh、玩家 Spawn 或 Shutdown 管線。