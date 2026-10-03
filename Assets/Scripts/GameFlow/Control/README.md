# 共用本地玩家控制鎖

最後核對：2026-09-18，Phase 4 前置-1。

`PlayerControlLocks` 是無場景依賴的票證集合；每次 Acquire 都有獨立身分，相同 reason 也不會互相覆蓋。Dispose 可重複呼叫，只釋放該張票證。`LocalPlayerControl` 管理本機集合與 EventSystem 攔截，不替代既有網路 `PlayerActionGate`。

```csharp
using Purgers.GameFlow.Control;
private System.IDisposable movementLock;
void Begin() => movementLock = LocalPlayerControl.Acquire(PlayerControlMask.Movement, "Dialogue");
void OnDisable() { movementLock?.Dispose(); movementLock = null; }
```

同一系統不可反覆 Acquire 後丟失舊票證；需要重複呼叫時先檢查欄位是否為 null。

| 選項 | 攔截範圍 |
| --- | --- |
| Movement | WASD、跳躍、跑步、蹲滑、Q 勾索、F 快速行動；Tank／Support 的右鍵位移技能啟動。普通 Attack ADS 與開火保留。 |
| Look | 滑鼠 Pitch／Yaw 控制。仍維持 CameraFollow 與角色移動時的鏡頭跟隨。 |
| AllInput | 全部 NetInput、Enter／Esc 游標切換、E 終端機、F1–F7 等開發快捷鍵、UI 滑鼠點擊及鍵盤 Submit／Navigation。 |

Movement 控制鎖不凍結物理，不取消已啟動技能、慣性、擊退或重力。需要禁止一切主動操作時使用 AllInput。Look 不停用相機、FOV 或受擊特效。

`InputManager` 在票證變動時清除未提交輸入，在 OnInput 過濾並把遮罩帶入 `NetInput.BlockedControls`。`Player`／`PlayerMovement` 使用該 tick 的遮罩重現相同結果；不讀 Host 的本機靜態鎖來控制遠端玩家。此集合與現有 CameraFollow 一樣，支援每程序一個本地玩家；多人驗證使用獨立程序。

AllInput 期間暫停啟用中的 EventSystem，最後一張票證解除才復原。呼叫方若自己持有其他 UI 禁用原因，應同樣取得本工具票證。新輸入來源必須查詢 AllInputBlocked；直接使用 Unity Input API 無法由 Canvas 自動阻止。

驗證：[Phase 4 前置-1 配置與驗證](../../../../Documentation/ProjectArchitecture/144_PrePhase4-1_共用控制與轉場.md)。
